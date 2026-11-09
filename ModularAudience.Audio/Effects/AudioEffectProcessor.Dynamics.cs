namespace ModularAudience.Audio.Effects;

public static partial class AudioEffectProcessor
{
    private static AudioEffectResult Dynamics(float[] source, AudioEffectSettings settings, RenderContext context)
    {
        if (settings.Effect == AudioEffectKind.Limiter) return Limit(source, settings, context);
        if (settings.Effect == AudioEffectKind.DeEsser) return DeEss(source, settings, context);
        int frames = source.Length / context.Channels;
        float[] output = new float[source.Length];
        double envelope = 0, smoothedGainDb = settings.Effect == AudioEffectKind.Gate ? -context.Automation.Value("RangeDb", 0) : 0;
        int holdRemaining = 0;
        for (int frame = 0; frame < frames; frame++)
        {
            context.Check(frame, frames);
            double attack = Coefficient(context.Automation.Value("AttackMs", frame), context.Rate), release = Coefficient(context.Automation.Value("ReleaseMs", frame), context.Rate);
            double peak = 0;
            for (int channel = 0; channel < context.Channels; channel++) peak = Math.Max(peak, Math.Abs(source[frame * context.Channels + channel]));
            double detectorCoefficient = peak > envelope ? attack : release;
            envelope = peak + detectorCoefficient * (envelope - peak);
            double level = GainToDb(envelope), threshold = context.Automation.Value("ThresholdDb", frame), targetDb;
            if (settings.Effect == AudioEffectKind.Compressor)
            {
                double over = level - threshold, knee = context.Automation.Value("KneeDb", frame), slope = 1 / context.Automation.Value("Ratio", frame) - 1;
                targetDb = knee > 0 && Math.Abs(over) <= knee / 2
                    ? slope * Math.Pow(over + knee / 2, 2) / (2 * knee) : over > 0 ? slope * over : 0;
            }
            else if (settings.Effect == AudioEffectKind.Gate)
            {
                if (level >= threshold) holdRemaining = (int)Math.Round(context.Automation.Value("HoldMs", frame) * context.Rate / 1000);
                bool open = level >= threshold || holdRemaining-- > 0;
                targetDb = open ? 0 : -context.Automation.Value("RangeDb", frame);
            }
            else targetDb = Math.Clamp((level - threshold) * (context.Automation.Value("Ratio", frame) - 1), -context.Automation.Value("RangeDb", frame), 0);

            // Opening an expander/gate uses attack, closing uses release. A
            // compressor instead attacks increasing gain reduction.
            bool attacking = settings.Effect == AudioEffectKind.Compressor ? targetDb < smoothedGainDb : targetDb > smoothedGainDb;
            double coefficient = attacking ? attack : release;
            smoothedGainDb = targetDb + coefficient * (smoothedGainDb - targetDb);
            double gain = DbToGain(smoothedGainDb + (settings.Effect == AudioEffectKind.Compressor ? context.Automation.Value("MakeupDb", frame) : 0));
            for (int channel = 0; channel < context.Channels; channel++)
            {
                int index = frame * context.Channels + channel;
                output[index] = (float)MixSample(source[index], source[index] * gain, context, frame);
            }
        }
        return new(output, context.Channels);
    }

    private static AudioEffectResult Limit(float[] source, AudioEffectSettings settings, RenderContext context)
    {
        int frames = source.Length / context.Channels;
        int lookAhead = (int)Math.Round(settings["LookAheadMs"] * context.Rate / 1000);
        double gain = 1;
        float[] output = new float[source.Length];
        // A monotonic deque gives a linked forward peak window in O(n), without
        // delaying or padding the returned audio.
        int capacity = Math.Min(frames, lookAhead + 1) + 1;
        int[] positions = new int[capacity]; double[] peaks = new double[capacity];
        int head = 0, tail = 0, next = 0;
        for (int frame = 0; frame < frames; frame++)
        {
            context.Check(frame, frames);
            double ceiling = DbToGain(context.Automation.Value("CeilingDb", frame)), drive = DbToGain(context.Automation.Value("InputGainDb", frame));
            double release = Coefficient(context.Automation.Value("ReleaseMs", frame), context.Rate);
            while (head != tail && positions[head] < frame) head = (head + 1) % capacity;
            while (next < frames && next <= (long)frame + lookAhead)
            {
                double peak = 0;
                double futureDrive = DbToGain(context.Automation.Value("InputGainDb", next));
                for (int channel = 0; channel < context.Channels; channel++) peak = Math.Max(peak, Math.Abs(source[next * context.Channels + channel] * futureDrive));
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
        double envelope = 0, reduction = 0;
        for (int frame = 0; frame < frames; frame++)
        {
            context.Check(frame, frames);
            double attack = Coefficient(context.Automation.Value("AttackMs", frame), context.Rate), release = Coefficient(context.Automation.Value("ReleaseMs", frame), context.Rate);
            if (context.Automation.Has("FrequencyHz") || context.Automation.Has("BandwidthOctaves"))
            {
                double currentBandwidth = context.Automation.Value("BandwidthOctaves", frame);
                double currentQ = 1 / (Math.Pow(2, currentBandwidth / 2) - Math.Pow(2, -currentBandwidth / 2));
                Biquad coefficients = Biquad.BandPass(context.Automation.Value("FrequencyHz", frame), context.Rate, currentQ);
                for (int channel = 0; channel < channels; channel++) filters[channel].Reconfigure(coefficients);
            }
            double detector = 0;
            for (int channel = 0; channel < channels; channel++)
            {
                band[channel] = filters[channel].Process(source[frame * channels + channel]);
                detector = Math.Max(detector, Math.Abs(band[channel]));
            }
            double coefficient = detector > envelope ? attack : release;
            envelope = detector + coefficient * (envelope - detector);
            double targetDb = Math.Min(context.Automation.Value("MaximumReductionDb", frame), Math.Max(0, GainToDb(envelope) - context.Automation.Value("ThresholdDb", frame)) * (1 - 1 / context.Automation.Value("Ratio", frame)));
            coefficient = targetDb > reduction ? attack : release;
            reduction = targetDb + coefficient * (reduction - targetDb);
            double gain = DbToGain(-reduction);
            for (int channel = 0; channel < channels; channel++)
            {
                int index = frame * channels + channel;
                double processed = settings.Choice("Mode") == 0 ? source[index] + band[channel] * (gain - 1) : source[index] * gain;
                output[index] = (float)MixSample(source[index], processed, context, frame);
            }
        }
        return new(output, channels);
    }
}
