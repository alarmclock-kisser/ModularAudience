namespace ModularAudience.Audio.Effects;

public static partial class AudioEffectProcessor
{
    private static AudioEffectResult Distortion(float[] source, AudioEffectSettings settings, RenderContext context) => settings.Effect switch
    {
        AudioEffectKind.Bitcrusher => Crush(source, settings, context),
        AudioEffectKind.SampleRateReducer => ReduceRate(source, settings, context),
        _ => Saturate(source, settings, context)
    };

    private static AudioEffectResult Crush(float[] source, AudioEffectSettings settings, RenderContext context)
    {
        float[] output = new float[source.Length]; double[] errors = new double[context.Channels];
        double levels = Math.Pow(2, settings["Bits"] - 1), drive = DbToGain(settings["DriveDb"]);
        int mode = settings.Choice("Mode"); uint random = 0x9E3779B9;
        double Noise()
        {
            random ^= random << 13; random ^= random >> 17; random ^= random << 5;
            return random / (double)uint.MaxValue;
        }
        for (int i = 0; i < source.Length; i++)
        {
            context.Check(i, source.Length);
            int channel = i % context.Channels;
            double value = Math.Clamp(source[i] * drive, -1, 1);
            if (mode == 2) value = Math.CopySign(Math.Log(1 + 255 * Math.Abs(value)) / Math.Log(256), value);
            if (mode == 3) value += errors[channel] * 0.85;
            if (settings.Flag("Dither")) value += (Noise() - Noise()) / levels;
            double quantized = (mode == 1 ? Math.Truncate(value * levels) : Math.Round(value * levels)) / levels;
            quantized = Math.Clamp(quantized, -1, 1 - 1 / levels);
            errors[channel] = Math.Clamp(value - quantized, -1 / levels, 1 / levels);
            if (mode == 2) quantized = Math.CopySign((Math.Pow(256, Math.Abs(quantized)) - 1) / 255, quantized);
            output[i] = (float)MixSample(source[i], quantized, settings);
        }
        return new(output, context.Channels);
    }

    private static AudioEffectResult ReduceRate(float[] source, AudioEffectSettings settings, RenderContext context)
    {
        int channels = context.Channels, frames = source.Length / channels;
        double rate = Math.Min(context.Rate, settings["RateHz"]);
        if (rate == context.Rate) return new(source, channels);
        float[] filtered = source;
        if (settings.Flag("AntiAlias"))
        {
            filtered = new float[source.Length];
            var lowPass = Enumerable.Range(0, channels).Select(_ => Biquad.LowPass(rate * 0.42, context.Rate)).ToArray();
            var lowPass2 = Enumerable.Range(0, channels).Select(_ => Biquad.LowPass(rate * 0.42, context.Rate)).ToArray();
            for (int frame = 0; frame < frames; frame++)
            {
                context.Check(frame, frames * 2);
                for (int channel = 0; channel < channels; channel++)
                {
                    int index = frame * channels + channel;
                    filtered[index] = (float)lowPass2[channel].Process(lowPass[channel].Process(source[index]));
                }
            }
        }
        var output = new float[source.Length]; double step = context.Rate / rate;
        for (int frame = 0; frame < frames; frame++)
        {
            context.Check(frame, frames);
            double position = Math.Floor(frame / step) * step;
            int before = Math.Clamp((int)Math.Floor(position), 0, frames - 1);
            int after = Math.Clamp((int)Math.Floor(position + step), before, frames - 1);
            double t = settings.Choice("Mode") == 0 || before == after ? 0 : Math.Clamp((frame - before) / (double)(after - before), 0, 1);
            for (int channel = 0; channel < channels; channel++)
            {
                double reduced = filtered[before * channels + channel] * (1 - t) + filtered[after * channels + channel] * t;
                int index = frame * channels + channel; output[index] = (float)MixSample(source[index], reduced, settings);
            }
        }
        return new(output, channels);
    }

    private static AudioEffectResult Saturate(float[] source, AudioEffectSettings settings, RenderContext context)
    {
        int channels = context.Channels, frames = source.Length / channels;
        var output = new float[source.Length]; var previous = new double[channels]; var memory = new double[channels];
        bool tape = settings.Effect == AudioEffectKind.TapeSaturation;
        double drive = DbToGain(settings["DriveDb"]), bias = tape ? settings["BiasPercent"] / 100 : 0;
        int oversampledRate = checked(context.Rate * 4);
        var antiAlias = Enumerable.Range(0, channels).Select(_ => Biquad.LowPass(context.Rate * 0.43, oversampledRate)).ToArray();
        var tone = Enumerable.Range(0, channels).Select(_ => Biquad.LowPass(settings["ToneHz"], context.Rate)).ToArray();
        var dc = Enumerable.Range(0, channels).Select(_ => Biquad.HighPass(15, context.Rate)).ToArray();
        double wowDepth = tape ? settings["WowDepthMs"] * context.Rate / 1000 : 0;
        var wow = Enumerable.Range(0, channels).Select(_ => new DelayLine((int)Math.Ceiling(wowDepth * 2) + 4)).ToArray();
        double Shape(double value)
        {
            double x = value * drive;
            if (tape) return (Math.Tanh(x + bias) - Math.Tanh(bias)) / Math.Max(0.1, Math.Tanh(drive));
            int mode = settings.Choice("Mode");
            if (settings.Effect == AudioEffectKind.Overdrive)
                return mode switch { 1 => Math.Clamp(x, -1, 1), 2 => 1 - Math.Abs(((x + 1) % 4 + 4) % 4 - 2), _ => x / (1 + Math.Abs(x)) * 1.5 };
            return mode switch
            {
                1 => Math.Atan(x) / Math.Atan(drive),
                2 => (Math.Tanh(x + 0.2) - Math.Tanh(0.2)) / Math.Tanh(drive),
                _ => Math.Tanh(x) / Math.Tanh(drive)
            };
        }
        for (int frame = 0; frame < frames; frame++)
        {
            context.Check(frame, frames);
            for (int channel = 0; channel < channels; channel++)
            {
                int index = frame * channels + channel;
                double input = source[index];
                if (tape)
                {
                    double remembered = settings["MemoryPercent"] / 100;
                    memory[channel] = remembered * memory[channel] + (1 - remembered) * input;
                    input = input * (1 - remembered * 0.3) + memory[channel] * remembered * 0.3;
                }
                double shaped = 0;
                for (int step = 1; step <= 4; step++)
                    shaped = antiAlias[channel].Process(Shape(previous[channel] + (input - previous[channel]) * step / 4));
                previous[channel] = input;
                shaped = tone[channel].Process(shaped);
                if (tape)
                {
                    shaped = dc[channel].Process(shaped);
                    if (wowDepth > 0)
                    {
                        double modulation = (1 + Math.Sin(frame * 2 * Math.PI * settings["WowRateHz"] / context.Rate)) * wowDepth;
                        wow[channel].Write(shaped); shaped = wow[channel].Read(1 + modulation);
                    }
                }
                output[index] = (float)MixSample(source[index], shaped, settings);
            }
        }
        return new(output, channels);
    }
}
