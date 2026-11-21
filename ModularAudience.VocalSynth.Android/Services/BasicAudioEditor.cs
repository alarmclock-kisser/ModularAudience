using ModularAudience.Audio.Vocal;

namespace ModularAudience.VocalSynth.Android.Services;

internal enum AudioEditMode
{
    TempoPreserving,
    Varispeed,
    PitchShift,
}

internal static class BasicAudioEditor
{
    internal static VocalAudioData Process(
        VocalAudioData source,
        double trimStart,
        double trimEnd,
        double fadeIn,
        double fadeOut,
        AudioEditMode mode,
        double rate)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Channels is < 1 or > 2 || source.SampleRate <= 0
            || source.Data.Length % source.Channels != 0)
            throw new InvalidDataException("The selected audio format cannot be edited.");
        if (!double.IsFinite(rate) || rate is < 0.5 or > 2)
            throw new ArgumentOutOfRangeException(nameof(rate));
        int frames = source.Data.Length / source.Channels;
        int startFrame = Math.Clamp((int)Math.Round(Math.Clamp(trimStart, 0, 0.99) * frames), 0, frames - 1);
        int endFrame = Math.Clamp((int)Math.Round(Math.Clamp(trimEnd, 0.01, 1) * frames), startFrame + 1, frames);
        int channels = source.Channels;
        float[] trimmed = source.Data.AsSpan(startFrame * channels, (endFrame - startFrame) * channels).ToArray();
        int trimmedFrames = trimmed.Length / channels;
        int fadeInFrames = (int)Math.Round(Math.Clamp(fadeIn, 0, 1) * trimmedFrames);
        int fadeOutFrames = (int)Math.Round(Math.Clamp(fadeOut, 0, 1) * trimmedFrames);
        for (int frame = 0; frame < trimmedFrames; frame++)
        {
            double gain = 1;
            if (fadeInFrames > 0 && frame < fadeInFrames)
                gain = Math.Min(gain, frame / (double)fadeInFrames);
            int remaining = trimmedFrames - frame - 1;
            if (fadeOutFrames > 0 && remaining < fadeOutFrames)
                gain = Math.Min(gain, remaining / (double)fadeOutFrames);
            for (int channel = 0; channel < channels; channel++)
                trimmed[frame * channels + channel] = (float)(trimmed[frame * channels + channel] * gain);
        }

        float[] transformed = mode switch
        {
            AudioEditMode.TempoPreserving => StretchWsola(trimmed, channels, rate),
            AudioEditMode.Varispeed => Resample(trimmed, channels, rate),
            AudioEditMode.PitchShift => ChangePitchWithoutLengthChange(trimmed, channels, rate),
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
        return new VocalAudioData
        {
            Data = transformed,
            SampleRate = source.SampleRate,
            Channels = channels,
            BitDepth = 16,
            Length = transformed.LongLength,
            Duration = TimeSpan.FromSeconds(transformed.Length / (double)channels / source.SampleRate),
        };
    }

    private static float[] ChangePitchWithoutLengthChange(float[] source, int channels, double pitchRate)
    {
        if (Math.Abs(pitchRate - 1) < 0.0001)
            return source;
        float[] resampled = Resample(source, channels, pitchRate);
        return StretchWsola(resampled, channels, 1 / pitchRate);
    }

    private static float[] Resample(float[] source, int channels, double rate)
    {
        int sourceFrames = source.Length / channels;
        int outputFrames = Math.Max(1, (int)Math.Round(sourceFrames / rate));
        float[] output = new float[checked(outputFrames * channels)];
        for (int frame = 0; frame < outputFrames; frame++)
        {
            double position = Math.Min(frame * rate, sourceFrames - 1);
            int first = (int)position;
            int second = Math.Min(first + 1, sourceFrames - 1);
            float blend = (float)(position - first);
            for (int channel = 0; channel < channels; channel++)
            {
                float a = source[first * channels + channel];
                float b = source[second * channels + channel];
                output[frame * channels + channel] = a + (b - a) * blend;
            }
        }
        return output;
    }

    private static float[] StretchWsola(float[] source, int channels, double rate)
    {
        int sourceFrames = source.Length / channels;
        int outputFrames = Math.Max(1, (int)Math.Round(sourceFrames / rate));
        if (Math.Abs(rate - 1) < 0.0001)
            return source;

        const int windowFrames = 1024;
        const int overlapFrames = 512;
        const int searchFrames = 128;
        if (sourceFrames < windowFrames * 2)
            return Resample(source, channels, rate);
        float[] output = new float[checked(outputFrames * channels)];
        int firstFrames = Math.Min(windowFrames, Math.Min(sourceFrames, outputFrames));
        Array.Copy(source, output, firstFrames * channels);
        int outputPosition = windowFrames - overlapFrames;
        while (outputPosition < outputFrames)
        {
            int candidate = Math.Clamp(
                (int)Math.Round(outputPosition * rate),
                0,
                Math.Max(0, sourceFrames - windowFrames));
            int minimum = Math.Max(0, candidate - searchFrames);
            int maximum = Math.Min(Math.Max(0, sourceFrames - windowFrames), candidate + searchFrames);
            int best = minimum;
            double bestScore = double.NegativeInfinity;
            int compareFrames = Math.Min(overlapFrames, Math.Min(outputPosition, sourceFrames));
            for (int search = minimum; search <= maximum; search += 4)
            {
                double score = 0;
                for (int frame = 0; frame < compareFrames; frame += 4)
                {
                    for (int channel = 0; channel < channels; channel++)
                    {
                        score += output[(outputPosition - compareFrames + frame) * channels + channel]
                            * source[(search + frame) * channels + channel];
                    }
                }
                if (score > bestScore)
                {
                    bestScore = score;
                    best = search;
                }
            }

            int available = Math.Min(windowFrames, Math.Min(sourceFrames - best, outputFrames - outputPosition));
            int crossfade = Math.Min(overlapFrames, available);
            for (int frame = 0; frame < crossfade; frame++)
            {
                float blend = (frame + 1f) / (crossfade + 1);
                for (int channel = 0; channel < channels; channel++)
                {
                    int target = (outputPosition + frame) * channels + channel;
                    float previous = output[target];
                    float incoming = source[(best + frame) * channels + channel];
                    output[target] = previous * (1 - blend) + incoming * blend;
                }
            }
            int copyFrames = available - crossfade;
            if (copyFrames > 0)
                Array.Copy(source, (best + crossfade) * channels, output, (outputPosition + crossfade) * channels, copyFrames * channels);
            outputPosition += windowFrames - overlapFrames;
        }
        return output;
    }
}
