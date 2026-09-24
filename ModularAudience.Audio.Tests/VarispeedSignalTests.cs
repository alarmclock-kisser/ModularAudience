using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Processing;

namespace ModularAudience.Audio.Tests
{
    [TestClass]
    public sealed class VarispeedSignalTests
    {
        [DataTestMethod]
        [DataRow(0.5f, 48000)]
        [DataRow(1f, 48000)]
        [DataRow(2f, 48000)]
        [DataRow(1.5f, 44100)]
        public void RenderedToneActuallyReachesTheRequestedFrequency(float rate, int outputRate)
        {
            VarispeedSampleProvider provider = CreateTone(440, outputRate);
            float[] buffer = new float[outputRate / 10 * 2];
            provider.Read(buffer);
            provider.SetTargetRate(rate);
            provider.Read(buffer);
            Assert.AreEqual(buffer.Length, provider.Read(buffer));
            double totalPhase = 0;
            for (int i = 2; i < buffer.Length; i += 2)
            {
                double cross = (double)buffer[i - 2] * buffer[i + 1] - (double)buffer[i - 1] * buffer[i];
                double dot = (double)buffer[i - 2] * buffer[i] + (double)buffer[i - 1] * buffer[i + 1];
                totalPhase += Math.Atan2(cross, dot);
            }
            double frequency = totalPhase * outputRate / (2 * Math.PI * (buffer.Length / 2 - 1));
            Assert.AreEqual(440d * rate, frequency, 0.1, "Measured output pitch must follow the requested playback rate.");
        }

        [TestMethod]
        public void FasterPlaybackFiltersContentAboveOutputNyquist()
        {
            VarispeedSampleProvider provider = CreateTone(20000, 48000);
            float[] buffer = new float[9600];
            provider.Read(buffer);
            provider.SetTargetRate(2f);
            provider.Read(buffer);
            provider.Read(buffer);
            double energy = 0;
            foreach (float sample in buffer) { energy += sample * sample; }
            Assert.IsTrue(Math.Sqrt(energy / buffer.Length) < 0.005, "Speed-up must not fold a loud out-of-band tone into the audible band.");
        }

        private static VarispeedSampleProvider CreateTone(int frequency, int outputRate)
        {
            float[] data = new float[48000 * 2];
            for (int frame = 0; frame < data.Length / 2; frame++)
            {
                double phase = 2 * Math.PI * frequency * frame / 48000;
                data[frame * 2] = (float)(0.5 * Math.Cos(phase));
                data[frame * 2 + 1] = (float)(0.5 * Math.Sin(phase));
            }
            return new VarispeedSampleProvider(new ArraySampleProvider(data, 48000, 2), outputRate, 1);
        }
    }
}
