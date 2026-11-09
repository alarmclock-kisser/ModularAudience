namespace ModularAudience.Audio.Effects;

public static partial class AudioEffectProcessor
{
    private static AudioEffectResult Delay(float[] source, AudioEffectSettings settings, RenderContext context)
    {
        int channels = context.Channels, inputFrames = source.Length / channels;
        int frames = EnsureLength(inputFrames + Math.Round(settings["TailSeconds"] * context.Rate), channels);
        float[] output = new float[checked(frames * channels)];
        double delayFrames = Math.Max(1, context.Rate * settings["DelayMs"] / 1000);
        int tapCount = settings.Effect == AudioEffectKind.MultiTapDelay ? settings.Choice("TapCount") : 1;
        double[] tapMultipliers = Enumerable.Range(1, tapCount).Select(tap =>
            (settings.Effect != AudioEffectKind.MultiTapDelay ? tap : settings.Choice("Pattern") switch
            {
                1 => 1 + (tap - 1) * 1.5, 2 => 1 + (tap - 1) * 2d / 3, _ => tap
            })).ToArray();
        double[] tapPositions = tapMultipliers.Select(multiplier => delayFrames * multiplier).ToArray();
        double maximumDelay = Math.Max(1, context.Rate * context.Automation.Maximum("DelayMs") / 1000);
        var lines = Enumerable.Range(0, channels).Select(_ => new DelayLine((int)Math.Ceiling(maximumDelay * tapMultipliers[^1]) + 2)).ToArray();
        var filters = Enumerable.Range(0, channels).Select(_ => Biquad.LowPass(settings["LowPassHz"], context.Rate)).ToArray();
        var echoes = new double[channels]; var feedbackSamples = new double[channels];
        for (int frame = 0; frame < frames; frame++)
        {
            context.Check(frame, frames);
            double feedback = context.Automation.Value("FeedbackPercent", frame) / 100;
            if (context.Automation.Has("DelayMs"))
            {
                double currentDelay = Math.Max(1, context.Rate * context.Automation.Value("DelayMs", frame) / 1000);
                for (int tap = 0; tap < tapCount; tap++) tapPositions[tap] = currentDelay * tapMultipliers[tap];
            }
            if (context.Automation.Has("LowPassHz"))
            {
                Biquad coefficients = Biquad.LowPass(context.Automation.Value("LowPassHz", frame), context.Rate);
                for (int channel = 0; channel < channels; channel++) filters[channel].Reconfigure(coefficients);
            }
            for (int channel = 0; channel < channels; channel++)
            {
                double echo = 0;
                for (int tap = 0; tap < tapCount; tap++)
                {
                    int from = settings.Effect == AudioEffectKind.MultiTapDelay && (tap & 1) != 0 && (channel ^ 1) < channels ? channel ^ 1 : channel;
                    double level = tapCount == 1 ? 1 : Math.Pow(context.Automation.Value("TapDecayPercent", frame) / 100, tap);
                    echo += lines[from].Read(tapPositions[tap]) * level;
                }
                echoes[channel] = filters[channel].Process(echo);
                feedbackSamples[channel] = lines[channel].Read(tapPositions[^1]);
            }
            for (int channel = 0; channel < channels; channel++)
            {
                double dry = frame < inputFrames ? source[frame * channels + channel] : 0;
                int partner = settings.Effect == AudioEffectKind.PingPongDelay && (channel ^ 1) < channels ? channel ^ 1 : channel;
                // Feed back the filtered echo for a single delay; multi-tap feeds the
                // last tap, avoiding an unstable sum of many feedback gains.
                double returned = tapCount == 1 ? echoes[partner] : feedbackSamples[partner];
                lines[channel].Write(dry + returned * feedback);
                output[frame * channels + channel] = (float)MixSample(dry, echoes[channel], context, frame);
            }
        }
        return new(output, channels);
    }

    private sealed class ReverbTank
    {
        internal readonly DelayLine[] Lines;
        internal readonly double[] Lengths, Feedback, Damped;
        internal readonly AllPassDelay[] Diffusers;
        internal readonly DelayLine Early;
        internal readonly double[] EarlyTaps;
        internal readonly ShimmerPitchDelay? Shimmer;
        internal readonly double InitialSize;
        private Biquad lowCut;
        internal double LastWet;
        internal double FilterWet(double input) => this.lowCut.Process(input);
        internal ReverbTank(AudioEffectSettings settings, RenderContext context, int channel)
        {
            double[] milliseconds = settings.Effect switch
            {
                AudioEffectKind.RoomReverb => [19.9, 23.3, 29.7, 31.7, 37.1, 41.1, 43.7, 47.3],
                AudioEffectKind.PlateReverb => [31.1, 37.7, 41.3, 47.9, 53.1, 59.3, 61.7, 67.1],
                AudioEffectKind.SpringReverb => [13.7, 17.3, 19.9, 23.1, 27.7, 29.3, 31.1, 37.3],
                AudioEffectKind.ChamberReverb => [23.9, 29.3, 31.7, 37.1, 43.3, 47.9, 53.3, 57.1],
                _ => [43.7, 53.1, 61.3, 67.7, 73.9, 79.3, 83.9, 89.3]
            };
            this.InitialSize = context.Automation.Value("SizePercent", 0);
            double size = this.InitialSize / 100;
            this.Lengths = milliseconds.Select((ms, index) => Math.Max(2, Math.Round((ms * size + channel * (0.17 + index * 0.031)) * context.Rate / 1000))).ToArray();
            double maximumSizeRatio = context.Automation.Maximum("SizePercent") / this.InitialSize;
            this.Lines = this.Lengths.Select(length => new DelayLine((int)Math.Ceiling(length * maximumSizeRatio) + context.Rate / 500 + 4)).ToArray();
            this.Feedback = this.Lengths.Select(length => Math.Pow(0.001, length / (context.Rate * settings["DecaySeconds"]))).ToArray();
            this.Damped = new double[8];
            double diffusion = settings["DiffusionPercent"] / 100;
            double[] diffusionTimes = settings.Effect switch
            {
                AudioEffectKind.SpringReverb => [0.37, 0.73, 1.19, 1.73, 2.71, 3.17, 4.31],
                AudioEffectKind.PlateReverb => [1.71, 2.79, 4.13, 7.73, 11.31, 13.79],
                _ => [2.31, 3.79, 5.17, 7.73]
            };
            this.Diffusers = diffusionTimes.Select((ms, index) => new AllPassDelay((int)Math.Max(1, (ms + channel * 0.13) * context.Rate / 1000),
                diffusion * (settings.Effect == AudioEffectKind.SpringReverb && (index & 1) == 1 ? -0.75 : 0.68))).ToArray();
            this.EarlyTaps = (settings.Effect == AudioEffectKind.ChamberReverb ? new[] { 7.3, 13.7, 19.1, 27.3, 37.9 } : new[] { 5.1, 11.7, 17.3 })
                .Select(ms => Math.Max(1, (ms * size + channel * 0.4) * context.Rate / 1000)).ToArray();
            this.Early = new DelayLine((int)Math.Ceiling(this.EarlyTaps[^1] * maximumSizeRatio) + 2);
            this.lowCut = Biquad.HighPass(settings.Effect == AudioEffectKind.SpringReverb ? 180 : 35, context.Rate);
            if (settings.Effect == AudioEffectKind.ShimmerReverb)
            {
                double semitones = new[] { 7, 12, 19 }[settings.Choice("ShimmerPitch")];
                this.Shimmer = new ShimmerPitchDelay(context.Rate, Math.Pow(2, semitones / 12));
            }
        }
    }

    private static AudioEffectResult Reverb(float[] source, AudioEffectSettings settings, RenderContext context)
    {
        int channels = context.Channels, inputFrames = source.Length / channels;
        int frames = EnsureLength(inputFrames + Math.Round(settings["TailSeconds"] * context.Rate), channels);
        var output = new float[checked(frames * channels)];
        var tanks = Enumerable.Range(0, channels).Select(channel => new ReverbTank(settings, context, channel)).ToArray();
        double maximumPreDelay = context.Automation.Maximum("PreDelayMs") * context.Rate / 1000;
        var preDelays = Enumerable.Range(0, channels).Select(_ => new DelayLine((int)Math.Ceiling(maximumPreDelay) + 2)).ToArray();
        // One-pole damping sits inside every feedback path; RT60 controls the low band.
        var mixed = new double[8];
        for (int frame = 0; frame < frames; frame++)
        {
            context.Check(frame, frames);
            double preDelay = context.Automation.Value("PreDelayMs", frame) * context.Rate / 1000;
            double damping = 1 - Math.Exp(-2 * Math.PI * ClampFrequency(context.Automation.Value("DampingHz", frame), context.Rate) / context.Rate);
            double decay = context.Automation.Value("DecaySeconds", frame);
            for (int channel = 0; channel < channels; channel++)
            {
                ReverbTank tank = tanks[channel];
                double sizeRatio = context.Automation.Value("SizePercent", frame) / tank.InitialSize;
                double dry = frame < inputFrames ? source[frame * channels + channel] : 0;
                double excitation = preDelay < 1 ? dry : preDelays[channel].Read(preDelay);
                preDelays[channel].Write(dry);
                tank.Early.Write(excitation);
                double early = 0;
                for (int tap = 0; tap < tank.EarlyTaps.Length; tap++)
                    early += tank.Early.Read(tank.EarlyTaps[tap] * sizeRatio) * (tap % 2 == 0 ? 1 : -0.65) / (tap + 2);
                if (tank.Shimmer is not null)
                    excitation += tank.Shimmer.Process(tank.LastWet) * context.Automation.Value("ShimmerPercent", frame) / 100 * 0.2;
                for (int index = 0; index < tank.Diffusers.Length; index++)
                {
                    if (context.Automation.Has("DiffusionPercent")) tank.Diffusers[index].SetCoefficient(context.Automation.Value("DiffusionPercent", frame) / 100 *
                        (settings.Effect == AudioEffectKind.SpringReverb && (index & 1) == 1 ? -0.75 : 0.68));
                    excitation = tank.Diffusers[index].Process(excitation);
                }
                double sum = 0;
                for (int line = 0; line < 8; line++)
                {
                    double modulation = settings.Effect is AudioEffectKind.PlateReverb or AudioEffectKind.ShimmerReverb
                        ? 0.0003 * context.Rate * Math.Sin(2 * Math.PI * (0.17 + line * 0.037) * frame / context.Rate + channel) : 0;
                    double value = tank.Lines[line].Read(tank.Lengths[line] * sizeRatio + modulation);
                    tank.Damped[line] += damping * (value - tank.Damped[line]);
                    mixed[line] = tank.Damped[line];
                    sum += value * (line % 3 == 0 ? -1 : 1);
                }
                // Normalized Hadamard scattering is orthogonal: it redistributes
                // feedback energy rather than multiplying it by the number of lines.
                for (int stride = 1; stride < 8; stride *= 2)
                    for (int start = 0; start < 8; start += stride * 2)
                        for (int offset = 0; offset < stride; offset++)
                        {
                            double a = mixed[start + offset], b = mixed[start + offset + stride];
                            mixed[start + offset] = a + b; mixed[start + offset + stride] = a - b;
                        }
                for (int line = 0; line < 8; line++)
                {
                    double feedback = context.Automation.Has("DecaySeconds") || context.Automation.Has("SizePercent")
                        ? Math.Pow(0.001, tank.Lengths[line] * sizeRatio / (context.Rate * decay)) : tank.Feedback[line];
                    tank.Lines[line].Write(excitation * (line % 2 == 0 ? 0.35 : -0.35) + mixed[line] / Math.Sqrt(8) * feedback);
                }
                double wet = sum / Math.Sqrt(8) * 0.7 + early * (settings.Effect is AudioEffectKind.RoomReverb or AudioEffectKind.ChamberReverb ? 0.7 : 0.3);
                // The filter must retain state; ReverbTank is a class, so update
                // the mutable value-type filter on the tank, not a local copy.
                tank.LastWet = tank.FilterWet(wet);
                output[frame * channels + channel] = (float)MixSample(dry, tank.LastWet, context, frame);
            }
        }
        return new(output, channels);
    }
}
