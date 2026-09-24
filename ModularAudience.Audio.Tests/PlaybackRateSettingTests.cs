using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Processing;
using NAudio.Wave;
using System.Reflection;

namespace ModularAudience.Audio.Tests
{
    [TestClass]
    public sealed class PlaybackRateSettingTests
    {
        [DataTestMethod]
        [DataRow(-500, 0.5f)]
        [DataRow(0, 1f)]
        [DataRow(161, 1.25f)]
        [DataRow(500, 2f)]
        public void ScrollbarPositionIsAnAbsoluteLogarithmicRate(int position, float expected)
        {
            Assert.AreEqual(expected, PlaybackRateMapping.MapFactor(position), 0.0001f);
            Assert.AreEqual(1f, PlaybackRateMapping.MapFactor(position) * PlaybackRateMapping.MapFactor(-position), 0.00001f);
            Assert.IsTrue(PlaybackRateMapping.MapFactor(1) - 1f < 0.002f);
        }

        [TestMethod]
        public async Task AValueSetBeforePlaybackIsStored()
        {
            using RateTestAudio audio = new(playing: false);
            audio.ManualSampleRateFactor = PlaybackRateMapping.MapFactor(161);
            await audio.ApplyCombinedSampleRateAsync();
            Assert.AreEqual(1.25, audio.SampleRateFactor, 0.0001);
            Assert.AreEqual(1.25, audio.ManualSampleRateFactor, 0.0001);
        }

        [TestMethod]
        public async Task RateStaysAt125PercentWithoutFurtherMovement()
        {
            using RateTestAudio audio = new(playing: true);
            ISampleProvider pipeline = audio.Pipeline;
            audio.ManualSampleRateFactor = 1.25;
            await audio.ApplyCombinedSampleRateAsync();
            float[] buffer = new float[960];
            for (int i = 0; i < 100; i++)
            {
                Assert.AreEqual(buffer.Length, pipeline.Read(buffer));
                Assert.AreEqual(1.25, ((VarispeedSampleProvider)pipeline).CurrentRate, 0.000001);
                Assert.AreEqual(1.25, audio.SampleRateFactor);
                Assert.AreSame(pipeline, audio.Pipeline);
            }
        }

        [TestMethod]
        public async Task ChangingRateWhilePausedUpdatesTheExistingPipeline()
        {
            using RateTestAudio audio = new(playing: false, paused: true);
            ISampleProvider pipeline = audio.Pipeline;
            audio.ManualSampleRateFactor = 1.25;
            await audio.ApplyCombinedSampleRateAsync();
            Assert.AreEqual(1.25f, audio.Service.PlaybackRate);
            Assert.AreSame(pipeline, audio.Pipeline);
            Assert.IsTrue(audio.Paused);
            Assert.IsFalse(audio.Playing);
        }

        [TestMethod]
        public async Task TimeStretchStopPreservesTheSelectedRate()
        {
            using RateTestAudio audio = new(playing: true);
            audio.ManualSampleRateFactor = 1.25;
            await audio.ApplyCombinedSampleRateAsync();
            await audio.PauseAsync();
            Assert.AreEqual(1.25, audio.SampleRateFactor);
            Assert.AreEqual(1.25, audio.ManualSampleRateFactor);
            await audio.StopAsync(preserveManualRate: true);
            Assert.AreEqual(1.25, audio.SampleRateFactor);
            Assert.AreEqual(1.25, audio.ManualSampleRateFactor);
        }

        [TestMethod]
        public async Task ExplicitCenterRestoresUnityWithoutReplacingThePipeline()
        {
            using RateTestAudio audio = new(playing: true);
            ISampleProvider pipeline = audio.Pipeline;
            audio.ManualSampleRateFactor = 1.25;
            await audio.ApplyCombinedSampleRateAsync();
            pipeline.Read(new float[960]);
            audio.ManualSampleRateFactor = PlaybackRateMapping.MapFactor(0);
            await audio.ApplyCombinedSampleRateAsync();
            pipeline.Read(new float[960]);
            Assert.AreEqual(1d, audio.SampleRateFactor);
            Assert.AreEqual(1d, ((VarispeedSampleProvider)pipeline).CurrentRate);
            Assert.AreSame(pipeline, audio.Pipeline);
        }

        private sealed class RateTestAudio : AudioObj
        {
            public AudioPlaybackService Service { get; }
            public ISampleProvider Pipeline => (ISampleProvider)GetServiceField("pipeline").GetValue(this.Service)!;

            public RateTestAudio(bool playing, bool paused = false)
            {
                this.Playing = playing;
                this.Paused = paused;
                this.Data = new float[48000 * 2];
                this.SampleRate = 48000;
                this.Channels = 2;
                this.Service = (AudioPlaybackService)typeof(AudioObj)
                    .GetField("playback", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(this)!;
                this.Service.SetLoop(0, this.Data.LongLength);
                GetServiceField("rawData").SetValue(this.Service, this.Data);
                GetServiceField("rawSampleRate").SetValue(this.Service, this.SampleRate);
                GetServiceField("rawChannels").SetValue(this.Service, this.Channels);
                GetServiceField("switching").SetValue(this.Service,
                    new SwitchingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(this.SampleRate, this.Channels)));
                this.Service.SeekSamples(0);
            }

            private static FieldInfo GetServiceField(string name) =>
                typeof(AudioPlaybackService).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
        }
    }
}
