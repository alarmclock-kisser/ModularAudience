using ModularAudience.Audio.Processors_V1;
using ModularAudience.Audio.Effects.Curves;

namespace ModularAudience.Audio.Effects;

/// <summary>Offline, RAM-only DSP. No method edits an AudioObj or creates history; the caller commits once.</summary>
public static partial class AudioEffectProcessor
{
    private const long MaximumOutputPcmBytes = 4L * 1024 * 1024 * 1024;
    private const int MaximumOutputSamples = (int)(MaximumOutputPcmBytes / sizeof(float));

    public static Task<AudioEffectResult> ProcessAsync(ReadOnlyMemory<float> input, int sampleRate, int channels,
        AudioEffectSettings settings, IProgress<double>? progress = null, CancellationToken cancellationToken = default,
        AudioEffectExecutionOptions? execution = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        if (sampleRate <= 0 || channels <= 0 || channels > 64 || input.IsEmpty || input.Length % channels != 0 || input.Length > MaximumOutputSamples)
            throw new ArgumentException("Effects require a valid sample rate and non-empty, complete audio frames.");
        var options = execution is null ? new AudioEffectExecutionOptions() : execution with { CurveScope = execution.CurveScope?.Copy() };
        options.Validate();
        AudioEffectSettings snapshot = settings.Copy();
        return Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            float[] source = input.ToArray();
            for (int i = 0; i < source.Length; i++)
            {
                if ((i & 65535) == 0) cancellationToken.ThrowIfCancellationRequested();
                if (!float.IsFinite(source[i])) throw new ArgumentException("Effects require finite input samples.");
            }
            var context = new RenderContext(sampleRate, channels, options.MaximumThreads, progress, cancellationToken)
            {
                Automation = new(snapshot.Automation, EffectCurveTargets.ForEffect(snapshot.Effect), key => EffectCurveTargets.BaseValue(snapshot, key),
                    source, channels, sampleRate, options.CurveScope, cancellationToken)
            };
            AudioEffectResult result;
            if (context.Automation.IsConstant(EffectCurveTargets.Amount, 0) ||
                snapshot.Parameters.ContainsKey("WetPercent") && context.Automation.IsConstant("WetPercent", 0))
                result = new(source, channels); // Bypass must not append a silent tail.
            else if (snapshot.Effect == AudioEffectKind.PitchShift)
            {
                if (context.Automation.Has("Semitones")) result = CurvePitch(source, snapshot, context);
                else
                {
                    double ratio = Math.Pow(2, snapshot["Semitones"] / 12);
                    EnsureLength(source.Length / channels / ratio, channels);
                    using var audio = new AudioObj { Data = source, Channels = channels, SampleRate = sampleRate };
                    using var shifted = await PitchShifter.CreatePitchShiftAsync(audio, (float)snapshot["Semitones"],
                        snapshot.Choice("Method") == 0, progress, options.MaximumThreads, cancellationToken).ConfigureAwait(false);
                    result = new(shifted.Data, channels, snapshot.Choice("Method") == 1 ? ratio : null);
                    if (snapshot.Choice("Method") == 1) context.SourceAtOutput = frame => frame * ratio;
                }
            }
            else if (snapshot.Effect == AudioEffectKind.TimeStretch)
                result = context.Automation.Has("Factor") ? CurveStretch(source, snapshot, context) : await StretchAsync(source, snapshot, context).ConfigureAwait(false);
            else if (snapshot.Effect == AudioEffectKind.VarispeedShift)
            {
                result = ResampleSpeed(source, frame => context.Automation.Value("Speed", frame), context);
                if (!context.Automation.Has("Speed")) result = result with { TempoMultiplier = snapshot["Speed"] };
            }
            else
                result = snapshot.Effect switch
                {
                    AudioEffectKind.RoomReverb or AudioEffectKind.HallReverb or AudioEffectKind.PlateReverb or
                    AudioEffectKind.SpringReverb or AudioEffectKind.ChamberReverb or AudioEffectKind.ShimmerReverb => Reverb(source, snapshot, context),
                    AudioEffectKind.SingleDelay or AudioEffectKind.SlapbackDelay or AudioEffectKind.PingPongDelay or AudioEffectKind.MultiTapDelay => Delay(source, snapshot, context),
                    AudioEffectKind.Compressor or AudioEffectKind.Limiter or AudioEffectKind.Gate or AudioEffectKind.Expander or AudioEffectKind.DeEsser => Dynamics(source, snapshot, context),
                    AudioEffectKind.Bitcrusher or AudioEffectKind.SampleRateReducer or AudioEffectKind.Overdrive or
                    AudioEffectKind.Saturation or AudioEffectKind.TapeSaturation => Distortion(source, snapshot, context),
                    AudioEffectKind.Chorus or AudioEffectKind.Flanger or AudioEffectKind.Phaser or AudioEffectKind.Tremolo => Modulation(source, snapshot, context),
                    AudioEffectKind.HumRemoval => RemoveHum(source, snapshot, context),
                    AudioEffectKind.NoiseReduction => ReduceNoise(source, snapshot, context),
                    _ => Utility(source, snapshot, context)
                };
            if (snapshot.Effect == AudioEffectKind.PitchShift)
                MatchPitchShiftPeak(source, result.Samples, cancellationToken);
            bool outputGain = snapshot.Parameters.ContainsKey("OutputGainDb"), amountCurve = context.Automation.Has(EffectCurveTargets.Amount);
            bool usedAmount = false, usedOutputGain = false;
            for (int frame = 0; frame < result.Samples.Length / result.Channels; frame++)
            {
                if ((frame & 8191) == 0) cancellationToken.ThrowIfCancellationRequested();
                double sourceFrame = context.SourceAtOutput(frame);
                double gain = outputGain ? DbToGain(context.Automation.Value("OutputGainDb", sourceFrame)) : 1;
                usedOutputGain |= gain != 1;
                double amount = amountCurve ? context.Automation.Value(EffectCurveTargets.Amount, sourceFrame) / 100 : 1;
                usedAmount |= amount != 0;
                for (int channel = 0; channel < result.Channels; channel++)
                {
                    int i = frame * result.Channels + channel;
                    double effected = result.Samples[i] * gain;
                    double dry = frame < source.Length / channels && channel < channels ? source[frame * channels + channel] : 0;
                    result.Samples[i] = (float)(amount == 1 ? effected : dry + (effected - dry) * amount);
                    if (!float.IsFinite(result.Samples[i])) throw new ArithmeticException("The effect produced non-finite audio. Reduce its drive or gain.");
                }
            }
            if (amountCurve && !usedAmount) result = new(source, channels);
            else if (context.Automation.Has("WetPercent") && !context.UsedWetMix)
            {
                if (!usedOutputGain) result = new(source, channels); // Preserve even signed-zero samples in an exact bypass.
                else if (result.Samples.Length > source.Length) result = result with { Samples = result.Samples.AsSpan(0, source.Length).ToArray() };
            }
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(1);
            return result;
        }, cancellationToken);
    }

    private static void MatchPitchShiftPeak(float[] reference, float[] output, CancellationToken token)
    {
        float referencePeak = 0, outputPeak = 0;
        for (int i = 0; i < reference.Length; i++)
        {
            if ((i & 65535) == 0) token.ThrowIfCancellationRequested();
            referencePeak = Math.Max(referencePeak, Math.Abs(reference[i]));
        }
        for (int i = 0; i < output.Length; i++)
        {
            if ((i & 65535) == 0) token.ThrowIfCancellationRequested();
            outputPeak = Math.Max(outputPeak, Math.Abs(output[i]));
        }
        if (referencePeak < 1e-6f || outputPeak < 1e-6f) return;
        double gain = referencePeak / (double)outputPeak;
        if (Math.Abs(gain - 1) < 0.001) return;
        for (int i = 0; i < output.Length; i++)
        {
            if ((i & 65535) == 0) token.ThrowIfCancellationRequested();
            output[i] = (float)(output[i] * gain);
        }
    }

    private sealed record RenderContext(int Rate, int Channels, int MaximumThreads, IProgress<double>? Progress, CancellationToken Token)
    {
        internal required EffectAutomationEvaluator Automation { get; init; }
        internal Func<double, double> SourceAtOutput { get; set; } = static frame => frame;
        internal bool UsedWetMix { get; set; }
        internal void Check(int frame, int total)
        {
            if ((frame & 1023) != 0) return;
            this.Token.ThrowIfCancellationRequested();
            this.Progress?.Report(frame / (double)Math.Max(1, total));
        }
    }

    private static int EnsureLength(double frames, int channels)
    {
        if (!double.IsFinite(frames) || frames <= 0 || frames * channels > MaximumOutputSamples)
            throw new ArgumentOutOfRangeException(nameof(frames), "Rendered audio would exceed the 4 GiB per-effect output limit. Use a smaller selection or shorter tail.");
        return Math.Max(1, checked((int)Math.Round(frames)));
    }

    private static double DbToGain(double db) => Math.Pow(10, db / 20);
    private static double GainToDb(double gain) => 20 * Math.Log10(Math.Max(1e-12, gain));
    private static double Coefficient(double milliseconds, int rate) => Math.Exp(-1 / (Math.Max(0.001, milliseconds) * 0.001 * rate));
    private static double ClampFrequency(double hz, int rate) => Math.Clamp(hz, Math.Min(1, rate * 0.01), rate * 0.45);
    private static double MixSample(double dry, double wet, RenderContext context, double frame)
    {
        double amount = context.Automation.Value("WetPercent", frame) / 100;
        context.UsedWetMix |= amount != 0;
        return dry + (wet - dry) * amount;
    }

    private static AudioEffectResult Utility(float[] source, AudioEffectSettings settings, RenderContext context)
    {
        int channels = context.Channels, frames = source.Length / channels;
        float[] output = new float[source.Length];
        switch (settings.Effect)
        {
            case AudioEffectKind.ConvertToMono:
                output = new float[frames];
                for (int frame = 0; frame < frames; frame++)
                {
                    context.Check(frame, frames);
                    double sum = 0;
                    for (int channel = 0; channel < channels; channel++) sum += source[frame * channels + channel];
                    output[frame] = (float)(sum / channels);
                }
                return new(output, 1);
            case AudioEffectKind.Reverse:
                for (int frame = 0; frame < frames; frame++)
                {
                    context.Check(frame, frames);
                    source.AsSpan(frame * channels, channels).CopyTo(output.AsSpan((frames - 1 - frame) * channels, channels));
                }
                break;
            case AudioEffectKind.Gain:
                for (int frame = 0; frame < frames; frame++)
                {
                    context.Check(frame, frames); double gain = DbToGain(context.Automation.Value("GainDb", frame));
                    for (int channel = 0; channel < channels; channel++) output[frame * channels + channel] = (float)(source[frame * channels + channel] * gain);
                }
                break;
            case AudioEffectKind.Normalize:
                var means = new double[channels]; var peaks = new double[channels];
                if (settings.Flag("RemoveDc"))
                {
                    for (int frame = 0; frame < frames; frame++)
                    {
                        context.Check(frame, frames * 3);
                        for (int channel = 0; channel < channels; channel++) means[channel] += source[frame * channels + channel] / (double)frames;
                    }
                }
                for (int frame = 0; frame < frames; frame++)
                {
                    context.Check(frame, frames * 3);
                    for (int channel = 0; channel < channels; channel++)
                        peaks[channel] = Math.Max(peaks[channel], Math.Abs(source[frame * channels + channel] - means[channel]));
                }
                if (!settings.Flag("IndependentChannels")) Array.Fill(peaks, peaks.Max());
                for (int frame = 0; frame < frames; frame++)
                {
                    context.Check(frame, frames);
                    double target = DbToGain(context.Automation.Value("TargetDb", frame));
                    for (int channel = 0; channel < channels; channel++)
                        output[frame * channels + channel] = peaks[channel] > 1e-12
                            ? (float)((source[frame * channels + channel] - means[channel]) * target / peaks[channel]) : 0;
                }
                break;
            case AudioEffectKind.FadeIn:
            case AudioEffectKind.FadeOut:
                int length = Math.Clamp((int)Math.Round(frames * settings["LengthPercent"] / 100), 1, frames);
                for (int frame = 0; frame < frames; frame++)
                {
                    context.Check(frame, frames);
                    int distance = settings.Effect == AudioEffectKind.FadeIn ? frame : frames - 1 - frame;
                    double t = length == 1 ? (distance == 0 ? 0 : 1) : Math.Clamp(distance / (double)(length - 1), 0, 1);
                    double envelope = settings.Choice("Curve") switch
                    {
                        1 => Math.Sin(t * Math.PI / 2), 2 => t * t * (3 - 2 * t), 3 => (Math.Exp(t * 5) - 1) / (Math.Exp(5) - 1), _ => t
                    };
                    if (context.Automation.Has(EffectCurveTargets.FadeEnvelope)) envelope = context.Automation.Value(EffectCurveTargets.FadeEnvelope, frame);
                    for (int channel = 0; channel < channels; channel++) output[frame * channels + channel] = (float)(source[frame * channels + channel] * envelope);
                }
                break;
            case AudioEffectKind.StereoWidth:
                source.CopyTo(output, 0);
                var bassFilters = Enumerable.Range(0, channels / 2).Select(_ => Biquad.LowPass(ClampFrequency(settings["MonoBassHz"], context.Rate), context.Rate)).ToArray();
                for (int frame = 0; frame < frames; frame++)
                {
                    context.Check(frame, frames);
                    double width = context.Automation.Value("WidthPercent", frame) / 100, monoBass = context.Automation.Value("MonoBassHz", frame);
                    if (context.Automation.Has("MonoBassHz"))
                    {
                        Biquad coefficients = Biquad.LowPass(ClampFrequency(monoBass, context.Rate), context.Rate);
                        for (int pair = 0; pair < bassFilters.Length; pair++) bassFilters[pair].Reconfigure(coefficients);
                    }
                    for (int channel = 0; channel + 1 < channels; channel += 2)
                    {
                        int index = frame * channels + channel;
                        double mid = (source[index] + (double)source[index + 1]) * 0.5;
                        double side = (source[index] - (double)source[index + 1]) * 0.5;
                        if (monoBass > 0) side -= bassFilters[channel / 2].Process(side);
                        output[index] = (float)(mid + side * width); output[index + 1] = (float)(mid - side * width);
                    }
                }
                break;
            default: throw new ArgumentOutOfRangeException(nameof(settings), "No DSP route exists for this effect.");
        }
        return new(output, channels);
    }

    private static async Task<AudioEffectResult> StretchAsync(float[] source, AudioEffectSettings settings, RenderContext context)
    {
        int inputFrames = source.Length / context.Channels;
        int targetFrames = EnsureLength(inputFrames * settings["Factor"], context.Channels);
        context.SourceAtOutput = frame => frame / settings["Factor"];
        if (targetFrames == inputFrames && !settings.Flag("TrimSilence")) return new(source, context.Channels);
        int fftSize = 256 << settings.Choice("FftSize");
        float[] output;
        if (settings.Choice("Method") == 0)
            output = PitchShifter.StretchPhaseVocoder(source, context.Channels, targetFrames, fftSize, settings["Overlap"], context.Progress, context.Token);
        else
        {
            output = new float[checked(targetFrames * context.Channels)];
            // Legacy V1 works on scalar chunks: isolate channels so it cannot pitch
            // or scramble interleaved stereo. Never give it the live editing object.
            for (int channel = 0; channel < context.Channels; channel++)
            {
                context.Token.ThrowIfCancellationRequested();
                var mono = new float[inputFrames];
                for (int frame = 0; frame < inputFrames; frame++) mono[frame] = source[frame * context.Channels + channel];
                using var audio = new AudioObj { Data = mono, SampleRate = context.Rate, Channels = 1, Length = inputFrames };
                using var result = await TimeStretcher.TimeStretchAllThreadsAsync(audio, fftSize, (float)settings["Overlap"], settings["Factor"],
                    keepData: true, normalize: 0, maxWorkers: context.MaximumThreads).ConfigureAwait(false);
                context.Token.ThrowIfCancellationRequested();
                for (int frame = 0; frame < Math.Min(targetFrames, result.Data.Length); frame++) output[frame * context.Channels + channel] = result.Data[frame];
                context.Progress?.Report((channel + 1d) / context.Channels);
            }
        }
        if (settings.Flag("PreservePeak"))
        {
            double before = source.Max(value => Math.Abs((double)value)), after = output.Max(value => Math.Abs((double)value));
            if (before > 1e-12 && after > 1e-12)
                for (int i = 0; i < output.Length; i++) { context.Check(i, output.Length); output[i] = (float)(output[i] * before / after); }
        }
        if (settings.Flag("TrimSilence"))
        {
            int start = 0, end = output.Length / context.Channels;
            bool Silent(int frame) => Enumerable.Range(0, context.Channels).All(channel => Math.Abs(output[frame * context.Channels + channel]) < 1e-5);
            while (start + 1 < end && Silent(start)) { context.Check(start, end); start++; }
            while (end - 1 > start && Silent(end - 1)) { context.Check(end, output.Length / context.Channels); end--; }
            output = output.AsSpan(start * context.Channels, (end - start) * context.Channels).ToArray();
        }
        return new(output, context.Channels, 1 / settings["Factor"]);
    }
}
