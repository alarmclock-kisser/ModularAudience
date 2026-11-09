using ModularAudience.Audio.Processors_V1;

namespace ModularAudience.Audio.Effects;

/// <summary>Offline, RAM-only DSP. No method edits an AudioObj or creates history; the caller commits once.</summary>
public static partial class AudioEffectProcessor
{
    private const int MaximumOutputSamples = 134_217_728;

    public static Task<AudioEffectResult> ProcessAsync(ReadOnlyMemory<float> input, int sampleRate, int channels,
        AudioEffectSettings settings, IProgress<double>? progress = null, CancellationToken cancellationToken = default,
        AudioEffectExecutionOptions? execution = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        if (sampleRate <= 0 || channels <= 0 || channels > 64 || input.IsEmpty || input.Length % channels != 0 || input.Length > MaximumOutputSamples)
            throw new ArgumentException("Effects require a valid sample rate and non-empty, complete audio frames.");
        var options = execution ?? new AudioEffectExecutionOptions();
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
            var context = new RenderContext(sampleRate, channels, options.MaximumThreads, progress, cancellationToken);
            AudioEffectResult result;
            if (snapshot.Parameters.TryGetValue("WetPercent", out double wet) && wet == 0)
                result = new(source, channels); // Bypass must not append a silent tail.
            else if (snapshot.Effect == AudioEffectKind.PitchShift)
            {
                double ratio = Math.Pow(2, snapshot["Semitones"] / 12);
                EnsureLength(source.Length / channels / ratio, channels);
                using var audio = new AudioObj { Data = source, Channels = channels, SampleRate = sampleRate };
                using var shifted = await PitchShifter.CreatePitchShiftAsync(audio, (float)snapshot["Semitones"],
                    snapshot.Choice("Method") == 0, progress, options.MaximumThreads, cancellationToken).ConfigureAwait(false);
                result = new(shifted.Data, channels, snapshot.Choice("Method") == 1 ? ratio : null);
            }
            else if (snapshot.Effect == AudioEffectKind.TimeStretch)
                result = await StretchAsync(source, snapshot, context).ConfigureAwait(false);
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
            double gain = snapshot.Parameters.TryGetValue("OutputGainDb", out double db) ? DbToGain(db) : 1;
            for (int i = 0; i < result.Samples.Length; i++)
            {
                if ((i & 65535) == 0) cancellationToken.ThrowIfCancellationRequested();
                if (gain != 1) result.Samples[i] = (float)(result.Samples[i] * gain);
                if (!float.IsFinite(result.Samples[i])) throw new ArithmeticException("The effect produced non-finite audio. Reduce its drive or gain.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(1);
            return result;
        }, cancellationToken);
    }

    private sealed record RenderContext(int Rate, int Channels, int MaximumThreads, IProgress<double>? Progress, CancellationToken Token)
    {
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
            throw new ArgumentOutOfRangeException(nameof(frames), "Rendered audio would exceed the 512 MiB per-effect output limit. Use a smaller selection or shorter tail.");
        return Math.Max(1, checked((int)Math.Round(frames)));
    }

    private static double DbToGain(double db) => Math.Pow(10, db / 20);
    private static double GainToDb(double gain) => 20 * Math.Log10(Math.Max(1e-12, gain));
    private static double Coefficient(double milliseconds, int rate) => Math.Exp(-1 / (Math.Max(0.001, milliseconds) * 0.001 * rate));
    private static double ClampFrequency(double hz, int rate) => Math.Clamp(hz, Math.Min(1, rate * 0.01), rate * 0.45);
    private static double MixSample(double dry, double wet, AudioEffectSettings settings) => dry + (wet - dry) * settings["WetPercent"] / 100;

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
                double gain = DbToGain(settings["GainDb"]);
                for (int i = 0; i < source.Length; i++) { context.Check(i, source.Length); output[i] = (float)(source[i] * gain); }
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
                double target = DbToGain(settings["TargetDb"]);
                for (int frame = 0; frame < frames; frame++)
                {
                    context.Check(frame, frames);
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
                    for (int channel = 0; channel < channels; channel++) output[frame * channels + channel] = (float)(source[frame * channels + channel] * envelope);
                }
                break;
            case AudioEffectKind.StereoWidth:
                source.CopyTo(output, 0);
                double width = settings["WidthPercent"] / 100;
                var bassFilters = Enumerable.Range(0, channels / 2).Select(_ => Biquad.LowPass(ClampFrequency(settings["MonoBassHz"], context.Rate), context.Rate)).ToArray();
                for (int frame = 0; frame < frames; frame++)
                {
                    context.Check(frame, frames);
                    for (int channel = 0; channel + 1 < channels; channel += 2)
                    {
                        int index = frame * channels + channel;
                        double mid = (source[index] + (double)source[index + 1]) * 0.5;
                        double side = (source[index] - (double)source[index + 1]) * 0.5;
                        if (settings["MonoBassHz"] > 0) side -= bassFilters[channel / 2].Process(side);
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
