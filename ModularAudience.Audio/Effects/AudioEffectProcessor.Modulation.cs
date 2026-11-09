namespace ModularAudience.Audio.Effects;

public static partial class AudioEffectProcessor
{
    private static AudioEffectResult Modulation(float[] source, AudioEffectSettings settings, RenderContext context)
    {
        int channels = context.Channels, frames = source.Length / channels;
        float[] output = new float[source.Length];
        double rate = settings["RateHz"], stereoPhase = settings["StereoPhase"] * Math.PI / 180;
        if (settings.Effect == AudioEffectKind.Tremolo)
        {
            for (int frame = 0; frame < frames; frame++)
            {
                context.Check(frame, frames);
                for (int channel = 0; channel < channels; channel++)
                {
                    double phase = frame * 2 * Math.PI * rate / context.Rate + (channel % 2) * stereoPhase;
                    double wave = settings.Choice("Waveform") switch
                    {
                        1 => 2 / Math.PI * Math.Asin(Math.Sin(phase)), 2 => Math.Sin(phase) >= 0 ? 1 : -1, _ => Math.Sin(phase)
                    };
                    double gain = 1 - settings["DepthPercent"] / 100 * (wave + 1) / 2;
                    int index = frame * channels + channel;
                    output[index] = (float)MixSample(source[index], source[index] * gain, settings);
                }
            }
        }
        else if (settings.Effect == AudioEffectKind.Phaser)
        {
            int stages = new[] { 2, 4, 6, 8, 12 }[settings.Choice("Stages")];
            var states = Enumerable.Range(0, channels).Select(_ => new double[stages]).ToArray();
            var feedback = new double[channels];
            for (int frame = 0; frame < frames; frame++)
            {
                context.Check(frame, frames);
                for (int channel = 0; channel < channels; channel++)
                {
                    double phase = frame * 2 * Math.PI * rate / context.Rate + (channel % 2) * stereoPhase;
                    double t = (1 + Math.Sin(phase)) / 2;
                    double hz = settings["MinimumHz"] * Math.Pow(settings["MaximumHz"] / settings["MinimumHz"], t);
                    int index = frame * channels + channel;
                    double signal = source[index] + feedback[channel] * settings["FeedbackPercent"] / 100;
                    for (int stage = 0; stage < stages; stage++)
                    {
                        double f = ClampFrequency(hz * Math.Pow(1.13, stage - stages / 2d), context.Rate);
                        double tangent = Math.Tan(Math.PI * f / context.Rate), a = (tangent - 1) / (tangent + 1);
                        double y = a * signal + states[channel][stage];
                        states[channel][stage] = signal - a * y; signal = y;
                    }
                    feedback[channel] = signal;
                    output[index] = (float)MixSample(source[index], signal, settings);
                }
            }
        }
        else
        {
            bool chorus = settings.Effect == AudioEffectKind.Chorus;
            double baseDelay = settings["DelayMs"] * context.Rate / 1000, depth = settings["DepthMs"] * context.Rate / 1000;
            int voices = chorus ? settings.Choice("Voices") : 1;
            var lines = Enumerable.Range(0, channels).Select(_ => new DelayLine((int)Math.Ceiling(baseDelay + depth * 2) + 4)).ToArray();
            for (int frame = 0; frame < frames; frame++)
            {
                context.Check(frame, frames);
                for (int channel = 0; channel < channels; channel++)
                {
                    double wet = 0;
                    for (int voice = 0; voice < voices; voice++)
                    {
                        double phase = frame * 2 * Math.PI * rate * (chorus ? 1 + voice * 0.07 : 1) / context.Rate +
                            (channel % 2) * stereoPhase + voice * 2 * Math.PI / voices;
                        double delay = baseDelay + depth * (1 + Math.Sin(phase));
                        wet += lines[channel].Read(Math.Max(1, delay)) / voices;
                    }
                    int index = frame * channels + channel;
                    lines[channel].Write(source[index] + (chorus ? 0 : wet * settings["FeedbackPercent"] / 100));
                    output[index] = (float)MixSample(source[index], wet, settings);
                }
            }
        }
        return new(output, channels);
    }
}
