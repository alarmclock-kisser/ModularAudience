namespace ModularAudience.Audio.Effects;

public static partial class AudioEffectProcessor
{
    private static AudioEffectResult Dynamics(float[] source, AudioEffectSettings settings, RenderContext context)
    {
        if (settings.Effect == AudioEffectKind.Limiter) return Limit(source, settings, context);
        if (settings.Effect == AudioEffectKind.DeEsser) return DeEss(source, settings, context);
        int frames = source.Length / context.Channels;
        float[] output = new float[source.Length];
        double attack = Coefficient(settings["AttackMs"], context.Rate), release = Coefficient(settings["ReleaseMs"], context.Rate);
        double envelope = 0, smoothedGainDb = settings.Effect == AudioEffectKind.Gate ? -settings["RangeDb"] : 0;
        int holdRemaining = 0;
        for (int frame = 0; frame < frames; frame++)
        {
            context.Check(frame, frames);
            double peak = 0;
            for (int channel = 0; channel < context.Channels; channel++) peak = Math.Max(peak, Math.Abs(source[frame * context.Channels + channel]));
            double detectorCoefficient = peak > envelope ? attack : release;
            envelope = peak + detectorCoefficient * (envelope - peak);
            double level = GainToDb(envelope), threshold = settings["ThresholdDb"], targetDb;
            if (settings.Effect == AudioEffectKind.Compressor)
            {
                double over = level - threshold, knee = settings["KneeDb"], slope = 1 / settings["Ratio"] - 1;
                targetDb = knee > 0 && Math.Abs(over) <= knee / 2
                    ? slope * Math.Pow(over + knee / 2, 2) / (2 * knee) : over > 0 ? slope * over : 0;
            }
            else if (settings.Effect == AudioEffectKind.Gate)
            {
                if (level >= threshold) holdRemaining = (int)Math.Round(settings["HoldMs"] * context.Rate / 1000);
                bool open = level >= threshold || holdRemaining-- > 0;
                targetDb = open ? 0 : -settings["RangeDb"];
            }
            else targetDb = Math.Clamp((level - threshold) * (settings["Ratio"] - 1), -settings["RangeDb"], 0);

            // Opening an expander/gate uses attack, closing uses release. A
            // compressor instead attacks increasing gain reduction.
            bool attacking = settings.Effect == AudioEffectKind.Compressor ? targetDb < smoothedGainDb : targetDb > smoothedGainDb;
            double coefficient = attacking ? attack : release;
            smoothedGainDb = targetDb + coefficient * (smoothedGainDb - targetDb);
            double gain = DbToGain(smoothedGainDb + (settings.Effect == AudioEffectKind.Compressor ? settings["MakeupDb"] : 0));
            for (int channel = 0; channel < context.Channels; channel++)
            {
                int index = frame * context.Channels + channel;
                output[index] = (float)MixSample(source[index], source[index] * gain, settings);
            }
        }
        return new(output, context.Channels);
    }

    private static AudioEffectResult Limit(float[] source, AudioEffectSettings settings, RenderContext context)
    {
        int frames = source.Length / context.Channels;
        int lookAhead = (int)Math.Round(settings["LookAheadMs"] * context.Rate / 1000);
        double ceiling = DbToGain(settings["CeilingDb"]), drive = DbToGain(settings["InputGainDb"]);
        double release = Coefficient(settings["ReleaseMs"], context.Rate), gain = 1;
        float[] output = new float[source.Length];
        // A monotonic deque gives a linked forward peak window in O(n), without
        // delaying or padding the returned audio.
        int capacity = Math.Min(frames, lookAhead + 1) + 1;
        int[] positions = new int[capacity]; double[] peaks = new double[capacity];
        int head = 0, tail = 0, next = 0;
        for (int frame = 0; frame < frames; frame++)
        {
            context.Check(frame, frames);
            while (head != tail && positions[head] < frame) head = (head + 1) % capacity;
            while (next < frames && next <= (long)frame + lookAhead)
            {
                double peak = 0;
                for (int channel = 0; channel < context.Channels; channel++) peak = Math.Max(peak, Math.Abs(source[next * context.Channels + channel] * drive));
                while (head != tail && peaks[(tail - 1 + capacity) % capacity] <= peak) tail = (tail - 1 + capacity) % capacity;
                positions[tail] = next++; peaks[tail] = peak; tail = (tail + 1) % capacity;
            }
            double target = peaks[head] > ceiling ? ceiling / peaks[head] : 1;
            gain = target < gain ? target : target + release * (gain - target);
            for (int channel = 0; channel < context.Channels; channel++)
            {
                int index = frame * context.Channels + channel;
                output[index] = (float)Math.Clamp(source[index] * drive * gain, -ceiling, ceiling);
            }
        }
        return new(output, context.Channels);
    }

    private static AudioEffectResult DeEss(float[] source, AudioEffectSettings settings, RenderContext context)
    {
        int channels = context.Channels, frames = source.Length / channels;
        float[] output = new float[source.Length]; var band = new double[channels];
        double bandwidth = settings["BandwidthOctaves"], q = 1 / (Math.Pow(2, bandwidth / 2) - Math.Pow(2, -bandwidth / 2));
        var filters = Enumerable.Range(0, channels).Select(_ => Biquad.BandPass(settings["FrequencyHz"], context.Rate, q)).ToArray();
        double attack = Coefficient(settings["AttackMs"], context.Rate), release = Coefficient(settings["ReleaseMs"], context.Rate);
        double envelope = 0, reduction = 0;
        for (int frame = 0; frame < frames; frame++)
        {
            context.Check(frame, frames);
            double detector = 0;
            for (int channel = 0; channel < channels; channel++)
            {
                band[channel] = filters[channel].Process(source[frame * channels + channel]);
                detector = Math.Max(detector, Math.Abs(band[channel]));
            }
            double coefficient = detector > envelope ? attack : release;
            envelope = detector + coefficient * (envelope - detector);
            double targetDb = Math.Min(settings["MaximumReductionDb"], Math.Max(0, GainToDb(envelope) - settings["ThresholdDb"]) * (1 - 1 / settings["Ratio"]));
            coefficient = targetDb > reduction ? attack : release;
            reduction = targetDb + coefficient * (reduction - targetDb);
            double gain = DbToGain(-reduction);
            for (int channel = 0; channel < channels; channel++)
            {
                int index = frame * channels + channel;
                double processed = settings.Choice("Mode") == 0 ? source[index] + band[channel] * (gain - 1) : source[index] * gain;
                output[index] = (float)MixSample(source[index], processed, settings);
            }
        }
        return new(output, channels);
    }
}
