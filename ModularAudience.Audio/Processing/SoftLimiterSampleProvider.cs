using NAudio.Wave;

namespace ModularAudience.Audio.Processing
{
    public sealed class SoftLimiterSampleProvider(ISampleProvider source) : ISampleProvider
    {
        public WaveFormat WaveFormat => source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            return this.Read(buffer.AsSpan(offset, count));
        }

        public int Read(Span<float> buffer)
        {
            int read = source.Read(buffer);
            const float drive = 1.1f;
            const float makeup = 0.88f;

            for (int i = 0; i < read; i++)
            {
                float sample = buffer[i] * drive;
                buffer[i] = MathF.Tanh(sample) * makeup;
            }

            return read;
        }
    }
}
