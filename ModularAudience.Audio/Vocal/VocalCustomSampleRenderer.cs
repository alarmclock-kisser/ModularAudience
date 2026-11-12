using ModularAudience.Audio.Processors_V1;

namespace ModularAudience.Audio.Vocal;

internal static class VocalCustomSampleRenderer
{
    internal static int GetFrameCount(
        VocalCustomSampleToken token, int targetSampleRate, double speed, int targetChannels)
    {
        double frames = token.SampleFrameCount * (double)targetSampleRate
            / token.Mapping.SampleRate / speed;
        if (!double.IsFinite(frames) || frames > VocalSynth.MaxOutputSamples / targetChannels)
            throw new InvalidOperationException("The custom sample would exceed the supported vocal output size.");
        return Math.Max(1, (int)Math.Round(frames));
    }

    internal static float[] Render(
        VocalCustomSampleToken token,
        VocalSynthSettings settings,
        int targetFrames,
        int targetChannels,
        double? targetFrequency,
        CancellationToken cancellationToken)
    {
        if (targetFrames <= 0 || targetChannels is < 1 or > 2)
            throw new ArgumentOutOfRangeException(nameof(targetFrames));
        cancellationToken.ThrowIfCancellationRequested();

        VocalCustomSampleMapping mapping = token.Mapping;
        int sourceChannels = mapping.Channels;
        int sourceFrames = token.SampleFrameCount;
        if (sourceChannels is < 1 or > 2 || mapping.SampleRate <= 0
            || mapping.Samples.Length % sourceChannels != 0
            || token.SampleStartFrame < 0
            || token.SampleStartFrame > mapping.Samples.Length / sourceChannels - sourceFrames)
            throw new InvalidDataException("A custom sample mapping has an invalid audio format.");

        double convertedFrameCount = sourceFrames * (double)settings.SampleRate / mapping.SampleRate;
        if (!double.IsFinite(convertedFrameCount)
            || convertedFrameCount > VocalSynth.MaxOutputSamples / targetChannels)
            throw new InvalidOperationException("The custom sample would exceed the supported vocal output size.");
        int convertedFrames = Math.Max(1, (int)Math.Round(convertedFrameCount));
        float[] converted = new float[checked(convertedFrames * targetChannels)];
        for (int frame = 0; frame < convertedFrames; frame++)
        {
            if ((frame & 2047) == 0) cancellationToken.ThrowIfCancellationRequested();
            double position = convertedFrames <= 1 || sourceFrames <= 1
                ? 0
                : frame * (sourceFrames - 1.0) / (convertedFrames - 1);
            int leftFrame = (int)position;
            int rightFrame = Math.Min(leftFrame + 1, sourceFrames - 1);
            float fraction = (float)(position - leftFrame);
            for (int channel = 0; channel < targetChannels; channel++)
            {
                float left = ReadChannel(
                    mapping.Samples, token.SampleStartFrame + leftFrame, sourceChannels, targetChannels, channel);
                float right = ReadChannel(
                    mapping.Samples, token.SampleStartFrame + rightFrame, sourceChannels, targetChannels, channel);
                float sample = left + (right - left) * fraction;
                if (!float.IsFinite(sample))
                    throw new InvalidDataException("A custom sample contains an invalid audio value.");
                converted[frame * targetChannels + channel] = sample;
            }
        }

        float[] rendered = Stretch(converted, targetChannels, targetFrames, cancellationToken);
        if (targetFrequency is double frequency)
            rendered = ShiftPitch(rendered, settings, targetChannels, frequency, cancellationToken);
        ApplyEdgeFades(rendered, targetChannels, targetFrames, settings.SampleRate, cancellationToken);
        return rendered;
    }

    private static float ReadChannel(
        float[] samples, int frame, int sourceChannels, int targetChannels, int targetChannel)
    {
        int offset = frame * sourceChannels;
        if (targetChannels == 1 && sourceChannels == 2)
            return (samples[offset] + samples[offset + 1]) * 0.5f;
        return samples[offset + (sourceChannels == 1 ? 0 : targetChannel)];
    }

    private static float[] Stretch(
        float[] samples, int channels, int targetFrames, CancellationToken cancellationToken)
    {
        int sourceFrames = samples.Length / channels;
        if (targetFrames == sourceFrames) return samples;
        if (targetFrames >= 256 && sourceFrames >= 256)
        {
            int fftSize = 256;
            int maximumFftSize = Math.Min(4096, Math.Min(sourceFrames, targetFrames));
            while (fftSize * 2 <= maximumFftSize) fftSize *= 2;
            return PitchShifter.StretchPhaseVocoder(
                samples, channels, targetFrames, fftSize, cancellationToken: cancellationToken);
        }

        float[] result = new float[checked(targetFrames * channels)];
        for (int frame = 0; frame < targetFrames; frame++)
        {
            if ((frame & 2047) == 0) cancellationToken.ThrowIfCancellationRequested();
            double position = targetFrames <= 1 || sourceFrames <= 1
                ? 0
                : frame * (sourceFrames - 1.0) / (targetFrames - 1);
            int leftFrame = (int)position;
            int rightFrame = Math.Min(leftFrame + 1, sourceFrames - 1);
            float fraction = (float)(position - leftFrame);
            for (int channel = 0; channel < channels; channel++)
            {
                float left = samples[leftFrame * channels + channel];
                float right = samples[rightFrame * channels + channel];
                result[frame * channels + channel] = left + (right - left) * fraction;
            }
        }
        return result;
    }

    private static float[] ShiftPitch(
        float[] samples, VocalSynthSettings settings, int channels, double targetFrequency,
        CancellationToken cancellationToken)
    {
        if (!double.IsFinite(targetFrequency) || targetFrequency <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetFrequency));
        int frames = samples.Length / channels;
        if (frames < Math.Max(1024, settings.SampleRate / 30)) return samples;

        float[] mono = new float[frames];
        for (int frame = 0; frame < frames; frame++)
        {
            if ((frame & 2047) == 0) cancellationToken.ThrowIfCancellationRequested();
            mono[frame] = channels == 1
                ? samples[frame]
                : (samples[frame * channels] + samples[frame * channels + 1]) * 0.5f;
        }
        float[] pitched = VocalSinging.RenderWindowsAtPitch(
            mono, settings, targetFrequency, cancellationToken);
        if (channels == 1) return pitched;

        float[] stereo = new float[checked(frames * channels)];
        for (int frame = 0; frame < frames; frame++)
        {
            if ((frame & 2047) == 0) cancellationToken.ThrowIfCancellationRequested();
            stereo[frame * channels] = pitched[frame];
            stereo[frame * channels + 1] = pitched[frame];
        }
        return stereo;
    }

    private static void ApplyEdgeFades(
        float[] samples, int channels, int frames, int sampleRate, CancellationToken cancellationToken)
    {
        int fadeFrames = Math.Min(frames / 8, Math.Max(1, sampleRate / 200));
        for (int frame = 0; frame < fadeFrames; frame++)
        {
            if ((frame & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
            float fadeIn = (frame + 1f) / (fadeFrames + 1f);
            float fadeOut = (frame + 1f) / (fadeFrames + 1f);
            int start = frame * channels;
            int end = (frames - 1 - frame) * channels;
            for (int channel = 0; channel < channels; channel++)
            {
                samples[start + channel] *= fadeIn;
                samples[end + channel] *= fadeOut;
            }
        }
    }
}
