using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Processing;
using NAudio.Wave;
using System.Reflection;
using static ModularAudience.Audio.Tests.LiveRateContinuityTests;

namespace ModularAudience.Audio.Tests
{
    [TestClass]
    public sealed class VarispeedSampleProviderTests
    {
        [TestMethod]
        public void RampsToTheLatestTargetWithoutRestartingAtUnity()
        {
            VarispeedSampleProvider provider = CreateProvider();
            provider.SetTargetRate(2f);
            Assert.AreEqual(1d, provider.CurrentRate);
            provider.Read(new float[240 * 2]);
            Assert.AreEqual(1.5, provider.CurrentRate, 0.00001);
            provider.SetTargetRate(0.5f);
            provider.Read(new float[240 * 2]);
            Assert.AreEqual(1d, provider.CurrentRate, 0.00001);
            provider.Read(new float[240 * 2]);
            Assert.AreEqual(0.5, provider.CurrentRate, 0.00001);
        }

        [TestMethod]
        public void IntegratesRampedRatesWithoutRewritingAlreadyQueuedPositions()
        {
            VarispeedSampleProvider provider = CreateProvider();
            provider.Read(new float[480 * 2]);
            provider.SetTargetRate(2f);
            provider.Read(new float[480 * 2]);
            Assert.AreEqual(480d, provider.GetSourceFramePosition(480), 0.00001);
            Assert.AreEqual(1200d, provider.GetSourceFramePosition(960), 0.00001);
            provider.SetTargetRate(0.5f);
            Assert.AreEqual(1200d, provider.GetSourceFramePosition(960), 0.00001);
            provider.Read(new float[480 * 2]);
            Assert.AreEqual(1800d, provider.GetSourceFramePosition(1440), 0.00001);
            Assert.AreEqual(480d, provider.GetSourceFramePosition(480), 0.00001);
        }

        [DataTestMethod]
        [DataRow(44100)]
        [DataRow(96000)]
        public void MapsDifferentDeviceSampleRatesInSourceFrames(int outputRate)
        {
            VarispeedSampleProvider provider = CreateProvider(outputRate: outputRate);
            provider.Read(new float[outputRate / 10 * 2]);
            Assert.AreEqual(4800d, provider.GetSourceFramePosition(outputRate / 10), 0.00001);
        }

        [TestMethod]
        public void ServicePositionsUseRenderedRatesRatherThanTheNewestTarget()
        {
            using PlaybackGraph graph = new(CreateStereoData(), 48000, 2);
            graph.Pipeline.Read(new float[480 * 2]);
            graph.Playback.AdjustSampleRate(2f).GetAwaiter().GetResult();
            graph.Pipeline.Read(new float[480 * 2]);
            Assert.AreEqual(960d, GetPosition(graph, 960), 2d);
            Assert.AreEqual(2400d, GetPosition(graph, 1920), 2d);
            graph.Playback.AdjustSampleRate(0.5f).GetAwaiter().GetResult();
            Assert.AreEqual(960d, GetPosition(graph, 960), 2d);
            Assert.AreEqual(2400d, GetPosition(graph, 1920), 2d);
        }

        [TestMethod]
        public void RateUpdatesPreserveLoopBoundsAndStereoChannels()
        {
            using PlaybackGraph graph = new(CreateStereoData(), 48000, 2);
            graph.Playback.SetLoop(64, 256);
            ISampleProvider pipeline = graph.Pipeline;
            float[] buffer = new float[2048];
            graph.Pipeline.Read(buffer);
            foreach (float rate in new[] { 0.5f, 2f, 1f })
            {
                graph.Playback.AdjustSampleRate(rate).GetAwaiter().GetResult();
                Assert.AreSame(pipeline, graph.Pipeline);
                Assert.AreEqual(buffer.Length, graph.Pipeline.Read(buffer));
                for (int i = 0; i < buffer.Length; i += 2)
                {
                    Assert.AreEqual(-2 * buffer[i], buffer[i + 1], 0.00001);
                }
            }
            long position = GetPosition(graph, 8192);
            Assert.IsTrue(position >= 64 && position < 256 && position % 2 == 0);
        }

        [TestMethod]
        public void ExplicitSeekAndDataSwapStillReanchorPlayback()
        {
            using PlaybackGraph graph = new(CreateStereoData(), 48000, 2);
            graph.Playback.AdjustSampleRate(1.5f).GetAwaiter().GetResult();
            graph.Pipeline.Read(new float[960]);
            graph.Playback.SeekSamples(500);
            Assert.AreEqual(500L, GetPosition(graph, 0));
            Assert.AreEqual(1.5, ((VarispeedSampleProvider) graph.Pipeline).CurrentRate);
            graph.Playback.SwapRawData(CreateStereoData(), 48000, 2, 1000);
            Assert.AreEqual(1000L, GetPosition(graph, 0));
            graph.Playback.SetLoop(64, 256);
            graph.Playback.ClearLoop(600);
            Assert.AreEqual(600L, GetPosition(graph, 0));
        }

        [TestMethod]
        public void ArrayReadHonorsOffsetAndKeepsIncompleteFramesUntouched()
        {
            VarispeedSampleProvider provider = CreateProvider();
            float[] buffer = Enumerable.Repeat(9f, 16).ToArray();
            Assert.AreEqual(8, provider.Read(buffer, 3, 9));
            Assert.AreEqual(9f, buffer[2]);
            Assert.AreEqual(9f, buffer[11]);
            Assert.AreEqual(9f, buffer[^1]);
        }

        [TestMethod]
        public void FiniteSourceEventuallyEndsIncludingItsFilterTail()
        {
            VarispeedSampleProvider provider = new(new ArraySampleProvider(new float[128], 48000, 2), 48000, 1);
            float[] buffer = new float[128];
            int total = 0;
            int read = -1;
            for (int i = 0; i < 10 && read != 0; i++)
            {
                read = provider.Read(buffer);
                total += read;
            }
            Assert.AreEqual(0, read);
            Assert.IsTrue(total >= 128 && total <= 256);
        }

        [TestMethod]
        public void TargetUpdatesAllocateNoManagedMemory()
        {
            VarispeedSampleProvider provider = CreateProvider();
            provider.SetTargetRate(1.1f);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) { provider.SetTargetRate(i % 2 == 0 ? 0.5f : 2f); }
            Assert.AreEqual(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        [TestMethod]
        public void TargetsAreClampedAndNonFiniteTargetsAreRejected()
        {
            VarispeedSampleProvider provider = CreateProvider();
            provider.SetTargetRate(10f);
            provider.Read(new float[960]);
            Assert.AreEqual(10d, provider.CurrentRate);
            provider.SetTargetRate(-1f);
            provider.Read(new float[960]);
            Assert.AreEqual(0.01, provider.CurrentRate, 0.00001);
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => provider.SetTargetRate(float.NaN));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => provider.SetTargetRate(float.PositiveInfinity));
        }

        [TestMethod]
        public async Task ConcurrentTargetsDoNotChangeFrameCountsOrStereoAlignment()
        {
            VarispeedSampleProvider provider = CreateProvider();
            Task updates = Task.Run(() =>
            {
                for (int i = 0; i < 10000; i++) { provider.SetTargetRate(i % 2 == 0 ? 0.5f : 2f); }
            });
            float[] buffer = new float[960];
            for (int i = 0; i < 40; i++)
            {
                Assert.AreEqual(buffer.Length, provider.Read(buffer));
                for (int j = 0; j < buffer.Length; j += 2)
                {
                    Assert.IsTrue(float.IsFinite(buffer[j]));
                    Assert.AreEqual(-2 * buffer[j], buffer[j + 1], 0.00001);
                }
            }
            await updates;
        }

        private static VarispeedSampleProvider CreateProvider(int outputRate = 48000) =>
            new(new ArraySampleProvider(CreateStereoData(), 48000, 2), outputRate, 1);

        private static float[] CreateStereoData()
        {
            float[] data = new float[48000 * 2];
            for (int i = 0; i < data.Length; i += 2) { data[i] = 0.25f; data[i + 1] = -0.5f; }
            return data;
        }

        private static long GetPosition(PlaybackGraph graph, long outputSamples) =>
            (long) typeof(AudioPlaybackService).GetMethod("GetCurrentSourceSampleIndex", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(graph.Playback, [outputSamples])!;
    }
}
