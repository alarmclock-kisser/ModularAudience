using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: DoNotParallelize]

namespace ModularAudience.Audio.Tests
{
    internal static class AudioTestData
    {
        public static float[] Hit(int sampleRate, double durationMs = 120, double frequency = 180, double decayMs = 30)
        {
            float[] samples = new float[(int)(sampleRate * durationMs / 1000)];
            for (int i = 0; i < samples.Length; i++)
            {
                double time = i / (double)sampleRate;
                double attack = Math.Min(1.0, i / Math.Max(1.0, sampleRate * 0.001));
                double release = Math.Min(1.0, (samples.Length - i) / Math.Max(1.0, sampleRate * 0.002));
                samples[i] = (float)(0.7 * attack * release * Math.Exp(-time / (decayMs / 1000)) * Math.Sin(2 * Math.PI * frequency * time));
            }

            return samples;
        }

        public static float[] Tone(int sampleRate, double durationMs, double frequency, double attackMs, double releaseMs)
        {
            float[] samples = new float[(int)(sampleRate * durationMs / 1000)];
            for (int i = 0; i < samples.Length; i++)
            {
                double attack = Math.Min(1.0, i / Math.Max(1.0, sampleRate * attackMs / 1000));
                double release = Math.Min(1.0, (samples.Length - i) / Math.Max(1.0, sampleRate * releaseMs / 1000));
                samples[i] = (float)(0.6 * attack * release * Math.Sin(2 * Math.PI * frequency * i / sampleRate));
            }

            return samples;
        }

        public static float[] Track(int sampleRate, double durationMs, params (double StartMs, float[] Samples, float Gain)[] hits)
        {
            float[] track = new float[(int)(sampleRate * durationMs / 1000)];
            foreach ((double startMs, float[] samples, float gain) in hits)
            {
                int start = (int)(sampleRate * startMs / 1000);
                for (int i = 0; i < samples.Length && start + i < track.Length; i++)
                {
                    track[start + i] += samples[i] * gain;
                }
            }

            return track;
        }

        public static float[] Stereo(float[] mono, bool oppositePhase)
        {
            float[] stereo = new float[mono.Length * 2];
            for (int i = 0; i < mono.Length; i++)
            {
                stereo[i * 2] = mono[i];
                stereo[(i * 2) + 1] = oppositePhase ? -mono[i] : mono[i];
            }

            return stereo;
        }
    }

    internal sealed class AudioTestScope : IDisposable
    {
        private readonly List<AudioObj> owned = [];

        public AudioObj Create(float[] data, int sampleRate = 16000, int channels = 1)
        {
            AudioObj audio = new()
            {
                Name = "Regression",
                Data = data,
                SampleRate = sampleRate,
                Channels = channels,
                BitDepth = 32,
                Length = data.Length,
                Duration = TimeSpan.FromSeconds(data.Length / (double)(sampleRate * channels))
            };
            this.owned.Add(audio);
            return audio;
        }

        public void Own(IEnumerable<AudioObj> atomics) => this.owned.AddRange(atomics);

        public void Dispose()
        {
            foreach (AudioObj audio in this.owned.Distinct())
            {
                audio.Dispose();
            }
        }
    }
}
