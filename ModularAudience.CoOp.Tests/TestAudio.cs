using ModularAudience.Audio;

namespace ModularAudience.CoOp.Tests;

internal static class TestAudio
{
    public static AudioObj Create(
        string name,
        int sampleRate = 8000,
        int channels = 1,
        int sampleCount = 800,
        float fill = 0.25f)
    {
        var data = new float[sampleCount * channels];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = fill + i * 0.0001f;
        }

        return new AudioObj
        {
            Name = name,
            OriginalName = name,
            Data = data,
            SampleRate = sampleRate,
            Channels = channels,
            BitDepth = 16,
            Length = data.Length,
            Duration = TimeSpan.FromSeconds((double)sampleCount / sampleRate),
            Volume = 1f
        };
    }

    public static AudioCollection CreateCollection()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "ModularAudience-CoOp-Tests",
            Guid.NewGuid().ToString("N"));
        return new AudioCollection(directory);
    }

    public static async Task WaitUntilAsync(Func<bool> condition, int timeoutMilliseconds = 5000)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("The expected co-op state was not reached in time.");
            }

            await Task.Delay(20);
        }
    }
}
