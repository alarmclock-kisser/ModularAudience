using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Processors_V4;

namespace ModularAudience.Audio.Tests
{
    [TestClass]
    public sealed class AnalogSeparationTests
    {
        [TestMethod]
        public async Task HarmonicAwareSeparationKeepsUpperBandsAudible()
        {
            const int sampleRate = 16000;
            int sampleCount = sampleRate * 2;
            float[] data = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                double time = i / (double) sampleRate;
                double envelope = Math.Clamp((time - 0.05) / 0.1, 0.0, 1.0) * Math.Clamp((2.0 - time) / 0.1, 0.0, 1.0);
                data[i] = (float) (envelope * (
                    0.5 * Math.Sin(2.0 * Math.PI * 110.0 * time) +
                    0.3 * Math.Sin(2.0 * Math.PI * 880.0 * time) +
                    0.2 * Math.Sin(2.0 * Math.PI * 4400.0 * time)));
            }

            using AudioTestScope scope = new();
            AudioObj source = scope.Create(data, sampleRate);
            AnalogSeparationResult result = await AnalogSeparationProcessor.SeparateAsync(
                source,
                [
                    new AnalogSeparationBand("Low", 60, 300),
                    new AnalogSeparationBand("Mid", 300, 2000),
                    new AnalogSeparationBand("High", 2000, 7000)
                ],
                new AnalogSeparationSettings(2048, 0.75, 4, false, 2, true, true));
            scope.Own(result.Stems);

            Assert.AreEqual(3, result.Stems.Count);
            foreach (AudioObj stem in result.Stems)
            {
                double rms = Math.Sqrt(stem.Data.Select(sample => sample * sample).Average());
                Assert.IsTrue(rms > 0.01, $"{stem.Name} was reconstructed as near-silence: RMS={rms:F6}");
            }
        }
    }
}