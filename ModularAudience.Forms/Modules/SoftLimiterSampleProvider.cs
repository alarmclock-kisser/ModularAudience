using NAudio.Wave;

namespace ModularAudience.Forms.Modules
{
    internal sealed class SoftLimiterSampleProvider(ISampleProvider source) : ISampleProvider
    {
        public WaveFormat WaveFormat => source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int read = source.Read(buffer, offset, count);
            const float drive = 1.1f;
            const float makeup = 0.88f;

            for (int i = 0; i < read; i++)
            {
                float sample = buffer[offset + i] * drive;
                buffer[offset + i] = MathF.Tanh(sample) * makeup;
            }

            return read;
        }
    }
}
