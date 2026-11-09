using System.Numerics;
using MathNet.Numerics.Providers.FourierTransform;
using ModularAudience.Audio.Effects.AutoTune;
using ModularAudience.Audio.Effects.Curves;

namespace ModularAudience.Audio.Effects;

public static partial class AudioEffectProcessor
{
    private static AudioEffectResult CurvePitch(float[] source, AudioEffectSettings settings, RenderContext context)
    {
        int frames = source.Length / context.Channels;
        if (settings.Choice("Method") == 1)
            return ResampleSpeed(source, frame => Math.Pow(2, context.Automation.Value("Semitones", frame) / 12), context);
        const int hop = 64;
        var shifts = new double[(frames + hop - 1) / hop + 1];
        for (int i = 0; i < shifts.Length; i++)
        {
            context.Token.ThrowIfCancellationRequested(); shifts[i] = context.Automation.Value("Semitones", Math.Min(frames - 1, i * hop));
        }
        if (shifts.All(value => Math.Abs(value) < 1e-12)) return new(source, context.Channels);
        var curve = new AutoTuneCorrectionCurve(shifts, Enumerable.Repeat(1f, shifts.Length).ToArray(), hop, 0, 0);
        var quality = new AutoTuneSettings { FftSize = context.Rate < 12000 ? 1024 : 2048, Overlap = 8 };
        float[] shifted = AutoTunePhaseVocoder.Render(source, context.Rate, context.Channels, quality, curve,
            context.MaximumThreads, context.Progress, context.Token);
        return new(shifted, context.Channels);
    }

    private static AudioEffectResult ResampleSpeed(float[] source, Func<double, double> speedAt, RenderContext context)
    {
        int frames = source.Length / context.Channels;
        var map = new EffectCurveTimeMap(frames, context.Channels, frame => 1 / speedAt(frame), context.Token);
        context.SourceAtOutput = map.SourceAt;
        if (map.IsIdentity) return new(source, context.Channels, 1);
        int outputFrames = EnsureLength(map.OutputFrames, context.Channels);
        var output = new float[outputFrames * context.Channels];
        // Variable-ratio, anti-aliased windowed-sinc reconstruction. One shared map preserves channel alignment.
        for (int frame = 0; frame < outputFrames; frame++)
        {
            context.Check(frame, outputFrames);
            double position = map.SourceAt(frame), ratio = speedAt(position);
            double cutoff = Math.Min(1, 1 / ratio); int radius = (int)Math.Ceiling(32 / cutoff);
            int first = Math.Max(0, (int)Math.Floor(position) - radius + 1), last = Math.Min(frames - 1, (int)Math.Floor(position) + radius);
            for (int channel = 0; channel < context.Channels; channel++)
            {
                double sum = 0, weights = 0;
                for (int index = first; index <= last; index++)
                {
                    double x = (position - index) * cutoff, absolute = Math.Abs(x);
                    double weight = absolute < 1e-12 ? cutoff : absolute >= 32 ? 0 :
                        cutoff * Math.Sin(Math.PI * x) / (Math.PI * x) * Math.Sin(Math.PI * x / 32) / (Math.PI * x / 32);
                    sum += source[index * context.Channels + channel] * weight; weights += weight;
                }
                output[frame * context.Channels + channel] = Math.Abs(weights) > 1e-12 ? (float)(sum / weights) : 0;
            }
        }
        // A genuinely variable tempo is not representable by one truthful BPM multiplier; a constant curve still is.
        return new(output, context.Channels, map.ConstantFactor is double factor ? 1 / factor : null);
    }

    private static AudioEffectResult CurveStretch(float[] source, AudioEffectSettings settings, RenderContext context)
    {
        int frames = source.Length / context.Channels;
        var map = new EffectCurveTimeMap(frames, context.Channels, frame => context.Automation.Value("Factor", frame), context.Token);
        context.SourceAtOutput = map.SourceAt;
        if (map.IsIdentity && !settings.Flag("TrimSilence")) return new(source, context.Channels, 1);
        int targetFrames = EnsureLength(map.OutputFrames, context.Channels), size = 256 << settings.Choice("FftSize");
        // Fixed synthesis hops and continuously moving analysis centers: no independent chunk renders or splice seams.
        double maximumSpeed = 1 / context.Automation.Minimum("Factor");
        int safeHop = Math.Max(1, (int)Math.Floor(size / (4 * maximumSpeed)));
        int hop = Math.Max(1, Math.Min((int)Math.Round(size * (1 - settings["Overlap"])), safeHop));
        int windows = (int)Math.Ceiling((targetFrames + size / 2d) / hop) + 1, bins = size / 2 + 1;
        var window = Enumerable.Range(0, size).Select(i => 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / size)).ToArray();
        var weights = new float[targetFrames]; var output = new float[targetFrames * context.Channels];
        for (int index = 0; index < windows; index++)
        {
            context.Token.ThrowIfCancellationRequested(); int start = index * hop - size / 2;
            for (int i = Math.Max(0, -start); i < size && start + i < targetFrames; i++) weights[start + i] += (float)(window[i] * window[i]);
        }
        long finished = 0, total = (long)windows * context.Channels;
        void RenderChannel(int channel)
        {
            var fourier = new ManagedFourierTransformProvider(); var spectrum = new Complex[size];
            var magnitudes = new double[bins]; var phases = new double[bins]; var previous = new double[bins];
            var synthesis = new double[bins]; var locked = new double[bins]; var peaks = new List<int>(bins);
            double previousCenter = 0;
            for (int index = 0; index < windows; index++)
            {
                context.Token.ThrowIfCancellationRequested();
                int outputCenter = index * hop; double center = map.SourceAt(outputCenter), advance = center - previousCenter;
                for (int i = 0; i < size; i++)
                {
                    double position = center + i - size / 2;
                    int left = (int)Math.Floor(position); double fraction = position - left;
                    double a = left >= 0 && left < frames ? source[left * context.Channels + channel] : 0;
                    double b = left + 1 >= 0 && left + 1 < frames ? source[(left + 1) * context.Channels + channel] : 0;
                    spectrum[i] = new Complex((a + (b - a) * fraction) * window[i], 0);
                }
                fourier.Forward(spectrum, FourierTransformScaling.NoScaling);
                for (int bin = 0; bin < bins; bin++)
                {
                    magnitudes[bin] = spectrum[bin].Magnitude; phases[bin] = Math.Atan2(spectrum[bin].Imaginary, spectrum[bin].Real);
                    double omega = 2 * Math.PI * bin / size;
                    double frequency = index == 0 ? omega : omega + Math.IEEERemainder(phases[bin] - previous[bin] - omega * advance, 2 * Math.PI) / Math.Max(1e-12, advance);
                    synthesis[bin] = index == 0 ? phases[bin] : Math.IEEERemainder(synthesis[bin] + frequency * hop, 2 * Math.PI);
                    previous[bin] = phases[bin]; locked[bin] = synthesis[bin];
                }
                peaks.Clear();
                for (int bin = 1; bin < bins - 1; bin++) if (magnitudes[bin] > magnitudes[bin - 1] && magnitudes[bin] >= magnitudes[bin + 1]) peaks.Add(bin);
                if (peaks.Count > 0)
                {
                    int nearest = 0;
                    for (int bin = 1; bin < bins - 1; bin++)
                    {
                        while (nearest + 1 < peaks.Count && bin > (peaks[nearest] + peaks[nearest + 1]) / 2) nearest++;
                        int peak = peaks[nearest]; locked[bin] = synthesis[peak] + Math.IEEERemainder(phases[bin] - phases[peak], 2 * Math.PI);
                    }
                }
                Array.Clear(spectrum);
                for (int bin = 0; bin < bins; bin++)
                {
                    Complex value = Complex.FromPolarCoordinates(magnitudes[bin], locked[bin]);
                    spectrum[bin] = bin == 0 || bin == bins - 1 ? new Complex(value.Real, 0) : value;
                    if (bin > 0 && bin < bins - 1) spectrum[size - bin] = Complex.Conjugate(value);
                }
                fourier.Backward(spectrum, FourierTransformScaling.BackwardScaling);
                int start = outputCenter - size / 2;
                for (int i = Math.Max(0, -start); i < size && start + i < targetFrames; i++)
                    output[(start + i) * context.Channels + channel] += (float)(spectrum[i].Real * window[i]);
                previousCenter = center;
                long done = Interlocked.Increment(ref finished); if ((done & 15) == 0) context.Progress?.Report(0.95 * done / total);
            }
        }
        if (context.Channels == 1 || context.MaximumThreads == 1)
            for (int channel = 0; channel < context.Channels; channel++) RenderChannel(channel);
        else Parallel.For(0, context.Channels, new ParallelOptions { CancellationToken = context.Token,
            MaxDegreeOfParallelism = Math.Min(context.Channels, context.MaximumThreads) }, RenderChannel);
        for (int frame = 0; frame < targetFrames; frame++)
        {
            context.Check(frame, targetFrames);
            for (int channel = 0; channel < context.Channels; channel++) output[frame * context.Channels + channel] /= Math.Max(1e-12f, weights[frame]);
        }
        if (settings.Flag("PreservePeak"))
        {
            double before = source.Max(value => Math.Abs((double)value)), after = output.Max(value => Math.Abs((double)value));
            if (before > 1e-12 && after > 1e-12)
                for (int i = 0; i < output.Length; i++) { context.Check(i, output.Length); output[i] = (float)(output[i] * before / after); }
        }
        if (settings.Flag("TrimSilence"))
        {
            int first = 0, last = targetFrames;
            bool Silent(int frame) => Enumerable.Range(0, context.Channels).All(channel => Math.Abs(output[frame * context.Channels + channel]) < 1e-5);
            while (first + 1 < last && Silent(first)) { context.Check(first, last); first++; }
            while (last - 1 > first && Silent(last - 1)) { context.Check(last, targetFrames); last--; }
            output = output.AsSpan(first * context.Channels, (last - first) * context.Channels).ToArray();
            context.SourceAtOutput = frame => map.SourceAt(frame + first);
        }
        return new(output, context.Channels, map.ConstantFactor is double factor ? 1 / factor : null);
    }
}
