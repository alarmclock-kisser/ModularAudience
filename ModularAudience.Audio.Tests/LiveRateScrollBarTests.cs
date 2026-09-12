using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Processing;
using ModularAudience.Forms.Controls;
using System.Reflection;
using static ModularAudience.Forms.Controls.LiveRateScrollBar;
using Timer = System.Windows.Forms.Timer;

namespace ModularAudience.Audio.Tests
{
    [TestClass]
    [DoNotParallelize]
    public sealed class LiveRateScrollBarTests
    {
        [DataTestMethod]
        [DataRow(-450, -1)]
        [DataRow(-450, 1)]
        [DataRow(0, -1)]
        [DataRow(0, 1)]
        [DataRow(450, -1)]
        [DataRow(450, 1)]
        public void GreaterDistanceProducesLargerRateSteps(int current, int direction)
        {
            TrackGeometry geometry = GeometryAt(current);
            int nearX = direction < 0 ? geometry.ThumbLeft - 1 : geometry.ThumbRight;
            int farX = nearX + direction * 200;
            int near = GetNextValue(current, -500, 500, nearX, geometry);
            int far = GetNextValue(current, -500, 500, farX, geometry);
            double nearStep = Math.Abs(PlaybackRateMapping.MapFactor(near) - PlaybackRateMapping.MapFactor(current));
            double farStep = Math.Abs(PlaybackRateMapping.MapFactor(far) - PlaybackRateMapping.MapFactor(current));
            Assert.IsTrue(direction * (near - current) > 0);
            Assert.IsTrue(nearStep <= 0.003, $"Near-thumb step was {nearStep}.");
            Assert.IsTrue(farStep > nearStep);
            Assert.IsTrue(farStep <= 0.015001);
        }

        [TestMethod]
        public void EveryRateStepStaysWithinOnePointFivePercentagePoints()
        {
            for (int current = -500; current <= 500; current++)
            {
                foreach (int mouseX in new[] { -10000, 10000 })
                {
                    int next = GetNextValue(current, -500, 500, mouseX, GeometryAt(current));
                    double step = Math.Abs(PlaybackRateMapping.MapFactor(next) - PlaybackRateMapping.MapFactor(current));
                    Assert.IsTrue(next >= -500 && next <= 500);
                    Assert.IsTrue(step <= 0.015001, $"Rate step from {current} to {next} was {step}.");
                    Assert.IsTrue(mouseX < 0 ? next <= current : next >= current);
                }
            }
        }

        [DataTestMethod]
        [DataRow(-1)]
        [DataRow(1)]
        public void StepSizeIncreasesMonotonicallyWithDistance(int direction)
        {
            TrackGeometry geometry = GeometryAt(0);
            int previousStep = 0;
            for (int distance = 1; distance <= 400; distance++)
            {
                int mouseX = direction < 0 ? geometry.ThumbLeft - distance : geometry.ThumbRight + distance - 1;
                int step = Math.Abs(GetNextValue(0, -500, 500, mouseX, geometry));
                Assert.IsTrue(step >= previousStep);
                previousStep = step;
            }
        }

        [DataTestMethod]
        [DataRow(-500)]
        [DataRow(0)]
        [DataRow(500)]
        public void PointerOverThumbDoesNotMoveTheValue(int current)
        {
            TrackGeometry geometry = GeometryAt(current);
            for (int mouseX = geometry.ThumbLeft; mouseX < geometry.ThumbRight; mouseX++)
            {
                Assert.AreEqual(current, GetNextValue(current, -500, 500, mouseX, geometry));
            }
        }

        [DataTestMethod]
        [DataRow(19, false)]
        [DataRow(20, true)]
        [DataRow(193, true)]
        [DataRow(194, false)]
        [DataRow(205, false)]
        [DataRow(206, true)]
        [DataRow(379, true)]
        [DataRow(380, false)]
        public void OnlyTheTrackStartsFollowing(int mouseX, bool expected)
        {
            Assert.AreEqual(expected, GeometryAt(0).IsTrackClick(mouseX));
        }

        [DataTestMethod]
        [DataRow(0, -500)]
        [DataRow(399, 500)]
        public void FollowingContinuesAcrossArrowRegionsToTheLimit(int mouseX, int expected)
        {
            int current = 0;
            for (int i = 0; i < 2000 && current != expected; i++)
            {
                int next = GetNextValue(current, -500, 500, mouseX, GeometryAt(current));
                Assert.IsTrue(expected < 0 ? next < current : next > current);
                current = next;
            }
            Assert.AreEqual(expected, current);
            Assert.AreEqual(expected, GetNextValue(current, -500, 500, mouseX, GeometryAt(current)));
        }

        [DataTestMethod]
        [DataRow(113, 40)]
        [DataRow(113, 73)]
        [DataRow(400, 100)]
        [DataRow(400, 300)]
        public void FollowingStopsAtThePointerWithoutOvershoot(int width, int mouseX)
        {
            int current = 0;
            int direction = mouseX < width / 2 ? -1 : 1;
            for (int i = 0; i < 2000; i++)
            {
                TrackGeometry geometry = GeometryAt(current, width);
                int next = GetNextValue(current, -500, 500, mouseX, geometry);
                if (next == current)
                {
                    Assert.IsTrue(mouseX >= geometry.ThumbLeft && mouseX < geometry.ThumbRight);
                    return;
                }
                Assert.IsTrue(direction * (next - current) > 0);
                current = next;
            }
            Assert.Fail("The thumb did not stop at the pointer.");
        }

        [STATestMethod]
        public void NativeTrackGestureKeepsCaptureOverBothArrows()
        {
            using LiveRateScrollBar bar = CreateScrollBar();
            BeginTrackGesture(bar);
            int previous = bar.Value;
            Invoke(bar, "StepTowardMouse", bar.Width - 1);
            Assert.IsTrue(bar.Value > previous);
            previous = bar.Value;
            Invoke(bar, "StepTowardMouse", 0);
            Assert.IsTrue(bar.Value < previous);
            Assert.IsTrue(bar.Capture);
            Assert.IsTrue(GetTimer(bar).Enabled);
            Assert.IsTrue((bool) GetField("followingTrack").GetValue(bar)!);
        }

        [STATestMethod]
        public void NativeArrowAndThumbPressesRemainNative()
        {
            using LiveRateScrollBar bar = CreateScrollBar();
            TrackGeometry geometry = GetGeometry(bar);
            foreach (int mouseX in new[] { geometry.Left - 1, geometry.Right, geometry.ThumbLeft, geometry.ThumbRight - 1 })
            {
                Assert.IsFalse((bool) Invoke(bar, "HandleLeftButtonDown", new Point(mouseX, bar.Height / 2))!);
                Assert.AreEqual(0, bar.Value);
                Assert.IsFalse(GetTimer(bar).Enabled);
            }
        }

        [STATestMethod]
        public void MouseReleaseEndsFollowingExactlyOnce()
        {
            using LiveRateScrollBar bar = CreateScrollBar();
            int endEvents = 0;
            bar.Scroll += (_, e) => endEvents += e.Type == ScrollEventType.EndScroll ? 1 : 0;
            BeginTrackGesture(bar);
            Message release = Message.Create(bar.Handle, 0x0202, IntPtr.Zero, IntPtr.Zero);
            Invoke(bar, "WndProc", release);
            Assert.IsFalse(bar.Capture);
            Assert.IsFalse(GetTimer(bar).Enabled);
            Assert.AreEqual(1, endEvents);
        }

        [STATestMethod]
        public void CaptureLossStopsTheRepeatTimer()
        {
            using LiveRateScrollBar bar = CreateScrollBar();
            BeginTrackGesture(bar);
            int previous = bar.Value;
            bar.Capture = false;
            Invoke(bar, "TrackRepeatTimer_Tick", null, EventArgs.Empty);
            Assert.IsFalse(GetTimer(bar).Enabled);
            Assert.IsFalse((bool) GetField("followingTrack").GetValue(bar)!);
            Assert.AreEqual(previous, bar.Value);
        }

        [STATestMethod]
        public void DisablingTheControlEndsFollowing()
        {
            using LiveRateScrollBar bar = CreateScrollBar();
            BeginTrackGesture(bar);
            bar.Enabled = false;
            Assert.IsFalse(GetTimer(bar).Enabled);
            Assert.IsFalse(bar.Capture);
        }

        [STATestMethod]
        public void CancellationEndsFollowing()
        {
            using LiveRateScrollBar bar = CreateScrollBar();
            BeginTrackGesture(bar);
            Message cancel = Message.Create(bar.Handle, 0x001F, IntPtr.Zero, IntPtr.Zero);
            Invoke(bar, "WndProc", cancel);
            Assert.IsFalse(GetTimer(bar).Enabled);
            Assert.IsFalse(bar.Capture);
        }

        [STATestMethod]
        public void DisposingTheControlStopsTheRepeatTimer()
        {
            LiveRateScrollBar bar = CreateScrollBar();
            using (bar)
            {
                BeginTrackGesture(bar);
            }
            Assert.IsFalse(GetTimer(bar).Enabled);
        }

        private static TrackGeometry GeometryAt(int value, int width = 400)
        {
            const int arrowWidth = 20;
            const int thumbWidth = 12;
            int travel = width - 2 * arrowWidth - thumbWidth;
            int thumbLeft = arrowWidth + (int) Math.Round((value + 500) / 1000.0 * travel);
            return new TrackGeometry(arrowWidth, width - arrowWidth, thumbLeft, thumbLeft + thumbWidth);
        }

        private static LiveRateScrollBar CreateScrollBar() => new()
        {
            Minimum = -500,
            Maximum = 500,
            LargeChange = 1,
            SmallChange = 1,
            Value = 0,
            Width = 400
        };

        private static void BeginTrackGesture(LiveRateScrollBar bar)
        {
            TrackGeometry geometry = GetGeometry(bar);
            Assert.IsTrue((bool) Invoke(bar, "HandleLeftButtonDown", new Point(geometry.Right - 1, bar.Height / 2))!);
            Assert.IsTrue(bar.Value > 0);
            Assert.IsTrue(bar.Capture);
            Assert.IsTrue(GetTimer(bar).Enabled);
        }

        private static TrackGeometry GetGeometry(LiveRateScrollBar bar)
        {
            object?[] args = [default(TrackGeometry)];
            Assert.IsTrue((bool) Invoke(bar, "TryGetGeometry", args)!);
            return (TrackGeometry) args[0]!;
        }

        private static object? Invoke(LiveRateScrollBar bar, string name, params object?[] args) =>
            typeof(LiveRateScrollBar).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(bar, args);

        private static FieldInfo GetField(string name) =>
            typeof(LiveRateScrollBar).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;

        private static Timer GetTimer(LiveRateScrollBar bar) => (Timer) GetField("trackRepeatTimer").GetValue(bar)!;
    }
}
