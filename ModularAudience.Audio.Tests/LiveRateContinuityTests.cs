using Microsoft.VisualStudio.TestTools.UnitTesting;
using NAudio.Wave;
using System.Reflection;

namespace ModularAudience.Audio.Tests
{
    [TestClass]
    public sealed class LiveRateContinuityTests
    {
        private const int SampleRate = 48000;
        private const int Channels = 2;
        public TestContext TestContext { get; set; } = null!;

        [DataTestMethod]
        [DataRow(5)]
        [DataRow(20)]
        [DataRow(40)]
        public void ContinuousRateChangesKeepTheSignalContinuous(int intervalMs)
        {
            using PlaybackGraph graph = new(CreateQuadratureTone(), SampleRate, Channels);
            float[] warmup = new float[SampleRate / 10 * Channels];
            graph.Pipeline.Read(warmup);
            float[] buffer = new float[SampleRate * intervalMs / 1000 * Channels];
            SignalMetrics metrics = new(warmup[^2], warmup[^1]);
            double sourceFrames = warmup.Length / Channels;
            int replacements = 0;
            long allocated = 0;
            for (int update = 0; update < 100; update++)
            {
                float rate = GetRate(update);
                graph.SetIdealSourceAnchor((long) sourceFrames * Channels);
                ISampleProvider previous = graph.Pipeline;
                long before = GC.GetAllocatedBytesForCurrentThread();
                graph.Playback.AdjustSampleRate(rate).GetAwaiter().GetResult();
                int read = graph.Pipeline.Read(buffer);
                allocated += GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.AreEqual(buffer.Length, read);
                replacements += ReferenceEquals(previous, graph.Pipeline) ? 0 : 1;
                metrics.Observe(buffer);
                sourceFrames += buffer.Length / Channels * (double) rate;
            }

            string result = $"Synthetic interval={intervalMs} ms; requests=100; replacements={replacements}; " +
                $"peak boundary step={metrics.PeakBoundaryStep:F6}; peak sample step={metrics.PeakSampleStep:F6}; " +
                $"low-amplitude frames={metrics.LowAmplitudeFrames}; update+render allocated={allocated} B. " +
                "Device-free rendering with ideal frame-aligned source anchors, not measured UI/device timing.";
            this.TestContext.WriteLine(result);
            Assert.IsTrue(metrics.PeakBoundaryStep < 0.08 && metrics.LowAmplitudeFrames == 0, result);
        }

        [TestMethod]
        public void TargetUpdatesDoNotReplaceThePipeline()
        {
            using PlaybackGraph graph = new(CreateQuadratureTone(), SampleRate, Channels);
            ISampleProvider pipeline = graph.Pipeline;
            foreach (float rate in new[] { 0.5f, 2f, 1f })
            {
                graph.Playback.AdjustSampleRate(rate).GetAwaiter().GetResult();
                Assert.AreSame(pipeline, graph.Pipeline, "A rate target must not replace the active signal pipeline.");
            }
        }

        private static float GetRate(int update)
        {
            if (update == 0) { return 2f; }
            if (update == 99) { return 1f; }
            int position = update < 50 ? -500 + update * 20 : 500 - (update - 50) * 20;
            return (float) Math.Pow(2.0, position / 500.0);
        }

        private static float[] CreateQuadratureTone()
        {
            float[] data = new float[SampleRate * 12 * Channels];
            for (int frame = 0; frame < data.Length / Channels; frame++)
            {
                double phase = 2 * Math.PI * 440 * frame / SampleRate;
                data[frame * Channels] = (float) (0.5 * Math.Cos(phase));
                data[frame * Channels + 1] = (float) (0.5 * Math.Sin(phase));
            }
            return data;
        }

        private sealed class SignalMetrics(float left, float right)
        {
            private float previousLeft = left;
            private float previousRight = right;
            public double PeakBoundaryStep { get; private set; }
            public double PeakSampleStep { get; private set; }
            public int LowAmplitudeFrames { get; private set; }

            public void Observe(float[] buffer)
            {
                for (int i = 0; i < buffer.Length; i += Channels)
                {
                    float left = buffer[i];
                    float right = buffer[i + 1];
                    Assert.IsTrue(float.IsFinite(left) && float.IsFinite(right));
                    double dx = left - this.previousLeft;
                    double dy = right - this.previousRight;
                    double step = Math.Sqrt(dx * dx + dy * dy);
                    this.PeakSampleStep = Math.Max(this.PeakSampleStep, step);
                    if (i == 0) { this.PeakBoundaryStep = Math.Max(this.PeakBoundaryStep, step); }
                    if (left * left + right * right < 0.01f) { this.LowAmplitudeFrames++; }
                    this.previousLeft = left;
                    this.previousRight = right;
                }
            }
        }

        internal sealed class PlaybackGraph : IDisposable
        {
            private static readonly FieldInfo PipelineField = GetField("pipeline");
            public AudioPlaybackService Playback { get; } = new();
            public ISampleProvider Pipeline => (ISampleProvider) PipelineField.GetValue(this.Playback)!;

            public PlaybackGraph(float[] data, int sampleRate, int channels, int? outputRate = null)
            {
                SetField("rawData", data);
                SetField("rawSampleRate", sampleRate);
                SetField("rawChannels", channels);
                SetField("switching", new SwitchingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(outputRate ?? sampleRate, channels)));
                this.Playback.SeekSamples(0);
            }

            // The legacy implementation reconstructs its source from the device clock on every update.
            // Give it an ideal frame-aligned anchor so this probe isolates signal-state resets, not clock jitter.
            public void SetIdealSourceAnchor(long sourceSamples)
            {
                SetField("positionOriginSourceSamples", (double) sourceSamples);
                SetField("positionOriginOutputSamples", 0L);
            }

            private void SetField(string name, object value) => GetField(name).SetValue(this.Playback, value);
            private static FieldInfo GetField(string name) => typeof(AudioPlaybackService).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
            public void Dispose() => this.Playback.Dispose();
        }
    }
}
