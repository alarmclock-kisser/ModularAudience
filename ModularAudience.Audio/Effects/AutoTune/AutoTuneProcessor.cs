using ModularAudience.Audio.Processors_V4;

namespace ModularAudience.Audio.Effects.AutoTune;

public sealed record AutoTuneResult(float[] Samples, int AnalyzedFrames, int VoicedFrames, int CorrectedFrames, double PeakProtectionGain);

/// <summary>Offline, RAM-only, linked-channel monophonic tuning; never changes duration or source samples.</summary>
public static class AutoTuneProcessor
{
    public static Task<AutoTuneResult> ProcessAsync(ReadOnlyMemory<float> samples, int sampleRate, int channels,
        AutoTuneSettings settings, IProgress<double>? progress = null, CancellationToken cancellationToken = default,
        AudioEffectExecutionOptions? execution = null) =>
        Task.Run(() => Process(samples, sampleRate, channels, settings, progress, cancellationToken, execution), cancellationToken);

    public static AutoTuneResult Process(ReadOnlyMemory<float> samples, int sampleRate, int channels,
        AutoTuneSettings settings, IProgress<double>? progress = null, CancellationToken cancellationToken = default,
        AudioEffectExecutionOptions? execution = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate(sampleRate);
        execution ??= new AudioEffectExecutionOptions();
        execution.Validate();
        if (channels is < 1 or > 32 || samples.Length % channels != 0) throw new ArgumentException("Audio must contain complete frames with 1–32 channels.");
        float[] source = samples.ToArray();
        for (int i = 0; i < source.Length; i++)
        {
            if ((i & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (!float.IsFinite(source[i])) throw new ArgumentException("Auto Tune requires finite audio samples.");
        }
        if (source.Length == 0) return new(source, 0, 0, 0, 1);
        progress?.Report(0);
        if (settings.IntensityPercent == 0 || settings.WetPercent == 0 || settings.MaximumCorrectionSemitones == 0)
            return Finish(source, source, null, sampleRate, channels, settings, progress, cancellationToken);

        float[] mono = AnalysisSignal(source, channels, settings.AnalysisChannel, cancellationToken);
        int hop = Math.Max(1, (int)Math.Round(sampleRate * settings.AnalysisHopMilliseconds / 1000));
        PyinPitchFrame[] pitches = PyinPitchTracker.Track(mono, sampleRate, hop,
            settings.MinimumFrequencyHz, settings.MaximumFrequencyHz, execution.MaximumThreads, cancellationToken);
        progress?.Report(0.45);
        AutoTuneCorrectionCurve curve = AutoTuneCurveBuilder.Build(mono, pitches, sampleRate, hop, settings, cancellationToken);
        progress?.Report(0.55);
        bool hasCorrection = curve.CorrectedFrames > 0 && curve.Semitones.Any(value => Math.Abs(value) > 1e-7);
        float[] shifted = hasCorrection
            ? AutoTunePhaseVocoder.Render(source, sampleRate, channels, settings, curve, execution.MaximumThreads, progress, cancellationToken)
            : source;
        return Finish(source, shifted, hasCorrection ? curve : curve with { WetEnvelope = new float[curve.WetEnvelope.Length] },
            sampleRate, channels, settings, progress, cancellationToken);
    }

    private static AutoTuneResult Finish(float[] source, float[] shifted, AutoTuneCorrectionCurve? curve,
        int sampleRate, int channels, AutoTuneSettings settings, IProgress<double>? progress, CancellationToken token)
    {
        if (ReferenceEquals(source, shifted) && settings.OutputGainDb == 0)
        {
            progress?.Report(1);
            return new(source, curve?.Semitones.Length ?? 0, curve?.VoicedFrames ?? 0, curve?.CorrectedFrames ?? 0, 1);
        }
        int frames = source.Length / channels;
        float[] mixed = new float[source.Length];
        double gain = Math.Pow(10, settings.OutputGainDb / 20), peak = 0;
        for (int frame = 0; frame < frames; frame++)
        {
            if ((frame & 2047) == 0) token.ThrowIfCancellationRequested();
            double wet = settings.WetPercent / 100 * (curve?.WetAt(frame) ?? 0);
            for (int channel = 0; channel < channels; channel++)
            {
                int index = frame * channels + channel;
                mixed[index] = (float)((source[index] + (shifted[index] - source[index]) * wet) * gain);
                if (!float.IsFinite(mixed[index])) throw new ArithmeticException("Pitch correction produced non-finite output.");
                peak = Math.Max(peak, Math.Abs(mixed[index]));
            }
        }
        double protection = settings.ProtectPeak && peak > 1 ? 1 / peak : 1;
        int fadeFrames = Math.Min(frames / 2, (int)Math.Round(sampleRate * settings.BoundaryFadeMilliseconds / 1000));
        for (int frame = 0; frame < frames; frame++)
        {
            if ((frame & 2047) == 0) token.ThrowIfCancellationRequested();
            double fade = fadeFrames <= 0 ? 1 : Math.Clamp(Math.Min(frame, frames - 1 - frame) / (double)fadeFrames, 0, 1);
            for (int channel = 0; channel < channels; channel++)
            {
                int index = frame * channels + channel;
                mixed[index] = (float)(source[index] + (mixed[index] * protection - source[index]) * fade);
            }
        }
        progress?.Report(1);
        return new(mixed, curve?.Semitones.Length ?? 0, curve?.VoicedFrames ?? 0, curve?.CorrectedFrames ?? 0, protection);
    }

    private static float[] AnalysisSignal(float[] source, int channels, AutoTuneAnalysisChannel mode, CancellationToken token)
    {
        int selected = mode == AutoTuneAnalysisChannel.Right ? Math.Min(1, channels - 1) : 0;
        if (mode == AutoTuneAnalysisChannel.Strongest)
        {
            double[] energy = new double[channels];
            for (int i = 0; i < source.Length; i++)
            {
                if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                energy[i % channels] += (double)source[i] * source[i];
            }
            selected = Array.IndexOf(energy, energy.Max());
        }
        float[] mono = new float[source.Length / channels];
        for (int frame = 0; frame < mono.Length; frame++)
        {
            if ((frame & 2047) == 0) token.ThrowIfCancellationRequested();
            if (mode != AutoTuneAnalysisChannel.Average) mono[frame] = source[frame * channels + selected];
            else
            {
                double sum = 0;
                for (int channel = 0; channel < channels; channel++) sum += source[frame * channels + channel];
                mono[frame] = (float)(sum / channels);
            }
        }
        return mono;
    }
}
