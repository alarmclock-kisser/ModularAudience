using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Processing;

namespace ModularAudience.Audio.Tests
{
    [TestClass]
    public sealed class PlaybackRateGestureTests
    {
        [TestMethod]
        public void MapsTheFullLogarithmicRangeWithAFineCenter()
        {
            Assert.AreEqual(0.5f, PlaybackRateGesture.MapFactor(-500));
            Assert.AreEqual(1f, PlaybackRateGesture.MapFactor(0));
            Assert.AreEqual(2f, PlaybackRateGesture.MapFactor(500));
            Assert.IsTrue(PlaybackRateGesture.MapFactor(1) - 1f < 0.002f);
            Assert.AreEqual(1f, PlaybackRateGesture.MapFactor(100) * PlaybackRateGesture.MapFactor(-100), 0.00001f);
        }

        [TestMethod]
        public void ChangesBeforePlaybackDoNotCarryOverIntoPlayback()
        {
            PlaybackRateGesture gesture = new();
            Assert.AreEqual(1f, gesture.Update(500, false, 0));
            Assert.AreEqual(1f, gesture.Update(500, true, 1));
            Assert.IsTrue(gesture.Update(499, true, 2) > 1f);
        }

        [TestMethod]
        public void AStationaryThumbExpiresWithoutAReleaseEvent()
        {
            PlaybackRateGesture gesture = new();
            Assert.AreEqual(2f, gesture.Update(500, true, 0));
            Assert.IsFalse(gesture.Expire(true, 59));
            Assert.IsTrue(gesture.Expire(true, 60));
            Assert.AreEqual(1f, gesture.Factor);
        }

        [TestMethod]
        public void DuplicateScrollEventsDoNotExtendTheMovementDeadline()
        {
            PlaybackRateGesture gesture = new();
            gesture.Update(500, true, 0);
            gesture.Update(500, true, 50);
            Assert.IsTrue(gesture.Expire(true, 60));
            Assert.AreEqual(1f, gesture.Update(500, true, 70));
        }

        [TestMethod]
        public void GenuineMovementExtendsTheDeadlineAndCanResumeAfterExpiry()
        {
            PlaybackRateGesture gesture = new();
            gesture.Update(500, true, 0);
            gesture.Update(499, true, 50);
            Assert.IsFalse(gesture.Expire(true, 60));
            Assert.IsTrue(gesture.Expire(true, 110));
            Assert.IsTrue(gesture.Update(498, true, 120) > 1f);
        }

        [TestMethod]
        public void PauseOrStopImmediatelyNeutralizesTheGesture()
        {
            PlaybackRateGesture gesture = new();
            gesture.Update(-500, true, 0);
            Assert.IsTrue(gesture.Expire(false, 1));
            Assert.AreEqual(1f, gesture.Update(-500, true, 2));
        }

        [TestMethod]
        public void CenterResetNeutralizesAndDoesNotReapplyThePreviousPosition()
        {
            PlaybackRateGesture gesture = new();
            gesture.Update(500, true, 0);
            gesture.Reset(0);
            Assert.AreEqual(1f, gesture.Update(0, true, 1));
            Assert.IsFalse(gesture.Expire(true, 100));
        }
    }
}
