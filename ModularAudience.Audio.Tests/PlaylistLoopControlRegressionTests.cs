using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Processing;
using ModularAudience.Forms;
using ModularAudience.Forms.Modules;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Timer = System.Windows.Forms.Timer;

namespace ModularAudience.Audio.Tests
{
    [TestClass]
    [DoNotParallelize]
    public sealed class PlaylistLoopControlRegressionTests
    {
        private const int MouseDown = 0x0201;
        private const int MouseUp = 0x0202;
        private const int MouseMove = 0x0200;

        [STATestMethod]
        public void SecondTextClickDoubleClickAndDragNeverToggleCheckbox()
        {
            using AudioTestScope scope = new();
            WithPlaylist([scope.Create(new float[64000])], (_, list) =>
            {
                list.SetItemChecked(0, false);
                list.SelectedIndex = -1;
                int checks = 0;
                list.ItemCheck += (_, _) => checks++;
                Point text = TextPoint(list, 0);
                Click(list, text);
                Click(list, text);
                Mouse(list, 0x0203, text);
                Mouse(list, MouseUp, text);
                Mouse(list, MouseDown, text);
                Mouse(list, MouseMove, new Point(list.ClientRectangle.Right + 20, text.Y));
                Assert.IsTrue(list.Capture, "Text dragging must retain capture.");
                Mouse(list, MouseUp, text);

                Assert.AreEqual(0, list.SelectedIndex);
                Assert.IsFalse(list.GetItemChecked(0));
                Assert.AreEqual(0, checks, "Text input must never toggle membership, even on an already selected row.");
                Assert.IsFalse(list.Capture);
            });
        }

        [STATestMethod]
        public void CheckboxClickAndSpaceEachToggleExactlyOnce()
        {
            using AudioTestScope scope = new();
            WithPlaylist([scope.Create(new float[64000])], (_, list) =>
            {
                list.SetItemChecked(0, false);
                int checks = 0;
                list.ItemCheck += (_, _) => checks++;
                Click(list, new Point(3, TextPoint(list, 0).Y));
                Assert.IsTrue(list.GetItemChecked(0));
                Assert.AreEqual(1, checks);

                SendMessage(list.Handle, 0x0100, (IntPtr) Keys.Space, (IntPtr) 1);
                SendMessage(list.Handle, 0x0102, (IntPtr) ' ', (IntPtr) 1);
                SendMessage(list.Handle, 0x0101, (IntPtr) Keys.Space, (IntPtr) 1);
                Assert.IsFalse(list.GetItemChecked(0));
                Assert.AreEqual(2, checks, "One Space keypress must toggle once, not once per native message.");

                Click(list, new Point(3, TextPoint(list, 0).Y));
                Assert.IsTrue(list.GetItemChecked(0));
                Assert.AreEqual(3, checks, "A checkbox on a selected row must still respond to a single click.");
            });
        }

        [STATestMethod]
        public void NormalLoopClickTargetsFocusedRowEvenWhenOnlyOtherRowsAreChecked()
        {
            using AudioTestScope scope = new();
            AudioObj[] audios = CreateTracks(scope);
            WithPlaylist(audios, (dialog, list) =>
            {
                SetChecks(list, 0, 2);
                Click(list, TextPoint(list, 1));

                ClickLoop(dialog, 0.5f, control: false);

                Assert.IsFalse(audios[0].LoopEnabled);
                Assert.IsTrue(audios[1].LoopEnabled, "Normal loop actions must target the focused, unchecked row.");
                Assert.IsFalse(audios[2].LoopEnabled);
            });
        }

        [STATestMethod]
        public void ControlLoopClickTargetsEveryCheckedOverlapTrackNotFocusedRow()
        {
            using AudioTestScope scope = new();
            AudioObj[] audios = CreateTracks(scope);
            WithPlaylist(audios, (dialog, list) =>
            {
                SetChecks(list, 0, 2);
                Click(list, TextPoint(list, 1));

                ClickLoop(dialog, 0.5f, control: true);

                Assert.IsTrue(audios[0].LoopEnabled);
                Assert.IsFalse(audios[1].LoopEnabled, "Ctrl must not implicitly include the focused unchecked track.");
                Assert.IsTrue(audios[2].LoopEnabled, "Ctrl must include all checked overlap tracks, not just the primary one.");
            });
        }

        [STATestMethod]
        public void ControlLoopClickWithEmptyCheckedGroupDoesNotFallBackToFocus()
        {
            using AudioTestScope scope = new();
            AudioObj[] audios = CreateTracks(scope);
            WithPlaylist(audios, (dialog, list) =>
            {
                SetChecks(list);
                Click(list, TextPoint(list, 1));
                ClickLoop(dialog, 0.5f, control: false);
                Assert.IsTrue(audios[1].LoopEnabled);

                ClickLoop(dialog, 0.5f, control: true);

                Assert.IsTrue(audios[1].LoopEnabled, "An empty Ctrl group must not toggle off the focused track's existing loop.");
                Assert.IsFalse(audios[0].LoopEnabled);
                Assert.IsFalse(audios[2].LoopEnabled);
            });
        }

        [STATestMethod]
        public void MultiplierSelectionFollowsTheFocusedPlaylistTrack()
        {
            using AudioTestScope scope = new();
            AudioObj[] audios = CreateTracks(scope);
            audios[0].UpdateLoopFraction(0, audios[0].Length, audios[0].Length, true, false);
            audios[0].Metrics["loop.ui.multiplier"] = 2.0;
            audios[1].UpdateLoopFraction(0, audios[1].Length, audios[1].Length, true, false);
            audios[1].Metrics["loop.ui.multiplier"] = 0.5;

            WithPlaylist(audios[..2], (dialog, list) =>
            {
                MethodInfo update = typeof(LoopControl).GetMethod(
                    "UpdateLoopButtonsState",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                DomainUpDown multiplier = Field<DomainUpDown>(dialog, "domainUpDown_multiplier");

                Click(list, TextPoint(list, 0));
                update.Invoke(dialog, null);
                Assert.AreEqual("2", multiplier.SelectedItem);

                Click(list, TextPoint(list, 1));
                update.Invoke(dialog, null);
                Assert.AreEqual("1/2", multiplier.SelectedItem);
            });
        }

        [STATestMethod]
        public void NoLoopMultiplierRemainsSelectedAndScalesCaptureLength()
        {
            using AudioTestScope scope = new();
            AudioObj focused = scope.Create(Enumerable.Range(0, 40).Select(value => (float)value).ToArray(), 4);
            AudioObj other = scope.Create(Enumerable.Range(100, 40).Select(value => (float)value).ToArray(), 4);
            focused.Volume = 100f;
            other.Volume = 100f;
            focused.Bpm = 120f;
            other.Bpm = 120f;
            focused.SetPosition(30);
            other.SetPosition(30);

            WithPlaylist([focused, other], (dialog, list) =>
            {
                Click(list, TextPoint(list, 0));
                DomainUpDown multiplier = Field<DomainUpDown>(dialog, "domainUpDown_multiplier");
                multiplier.SelectedItem = "2";

                typeof(LoopControl).GetMethod(
                    "UpdateLoopButtonsState",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dialog, null);
                Assert.AreEqual("2", multiplier.SelectedItem);

                MethodInfo merge = typeof(LoopControl).GetMethod(
                    "MergeLoopedTracksAsync",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                Task<AudioObj?> task = (Task<AudioObj?>) merge.Invoke(dialog, [new[] { focused, other }])!;
                AudioObj merged = task.GetAwaiter().GetResult()!;

                Assert.AreEqual(16, merged.Data.Length,
                    "Multi 2 must capture eight beats, while Multi 1 captures four beats.");
            });
        }

        [STATestMethod]
        public void LoopBoundsStayPerTrackAcrossDifferentSampleRatesChannelsAndFocusChanges()
        {
            using AudioTestScope scope = new();
            AudioObj mono = scope.Create(new float[160000], 32000, 1);
            AudioObj stereo = scope.Create(new float[480000], 48000, 2);
            mono.Bpm = 120;
            stereo.Bpm = 90;
            mono.Seek(1.25);
            stereo.Seek(2.5);
            WithPlaylist([mono, stereo], (dialog, list) =>
            {
                SetChecks(list, 0, 1);
                ClickLoop(dialog, 0.5f, control: true);
                AssertLoopBounds(mono, 40000, 56000);
                AssertLoopBounds(stereo, 240000, 304000);

                Click(list, TextPoint(list, 0));
                ClickLoop(dialog, 0.25f, control: false);
                AssertLoopBounds(mono, 40000, 48000);
                AssertLoopBounds(stereo, 240000, 304000);
                Click(list, TextPoint(list, 1));
                ClickLoop(dialog, 0.25f, control: false);
                AssertLoopBounds(stereo, 240000, 272000);
                AssertLoopBounds(mono, 40000, 48000);
            });
        }

        [STATestMethod]
        public void MergeCaptureUsesCurrentLoopPhaseAndIncludesFreeRunningTrack()
        {
            using AudioTestScope scope = new();
            AudioObj loop = scope.Create([0f, 1f, 2f, 3f, 4f, 5f, 6f, 7f]);
            AudioObj free = scope.Create([10f, 10f, 10f, 10f, 10f, 10f, 10f, 10f]);
            loop.Volume = 100f;
            free.Volume = 100f;
            loop.UpdateLoopFraction(2, 6, 4, true, false);
            loop.SetPosition(4);
            free.SetPosition(1);

            WithPlaylist([loop, free], (dialog, _) =>
            {
                MethodInfo merge = typeof(LoopControl).GetMethod(
                    "MergeLoopedTracksAsync",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                Task<AudioObj?> task = (Task<AudioObj?>) merge.Invoke(dialog, [new[] { loop, free }])!;
                AudioObj merged = task.GetAwaiter().GetResult()!;

                CollectionAssert.AreEqual(
                    new[] { 14f, 15f, 12f, 13f },
                    merged.Data,
                    "Capture must start at the current loop phase and include the free-running track.");
            });
        }

        [STATestMethod]
        public void MergeCaptureWithoutLoopUsesLastFourFocusedBeats()
        {
            using AudioTestScope scope = new();
            AudioObj focused = scope.Create(Enumerable.Range(0, 20).Select(value => (float)value).ToArray(), 4);
            AudioObj other = scope.Create(Enumerable.Range(100, 20).Select(value => (float)value).ToArray(), 4);
            focused.Volume = 100f;
            other.Volume = 100f;
            focused.Bpm = 120f;
            other.Bpm = 120f;
            focused.SetPosition(12);
            other.SetPosition(12);

            WithPlaylist([focused, other], (dialog, list) =>
            {
                Click(list, TextPoint(list, 0));
                MethodInfo merge = typeof(LoopControl).GetMethod(
                    "MergeLoopedTracksAsync",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                Task<AudioObj?> task = (Task<AudioObj?>) merge.Invoke(dialog, [new[] { focused, other }])!;
                AudioObj merged = task.GetAwaiter().GetResult()!;

                CollectionAssert.AreEqual(
                    new[] { 108f, 110f, 112f, 114f, 116f, 118f, 120f, 122f },
                    merged.Data,
                    "A no-loop copy must contain the four beats immediately before the click.");
            });
        }

        [STATestMethod]
        public void MergeCaptureUsesHighestCompatibleMultipleBpm()
        {
            using AudioTestScope scope = new();
            AudioObj halfTime = scope.Create(new float[8]);
            AudioObj doubleTime = scope.Create(new float[8]);
            halfTime.Volume = 100f;
            doubleTime.Volume = 100f;
            halfTime.Bpm = 105f;
            doubleTime.Bpm = 210f;
            halfTime.UpdateLoopFraction(0, 8, 8, true, false);
            doubleTime.UpdateLoopFraction(0, 8, 8, true, false);

            WithPlaylist([halfTime, doubleTime], (dialog, _) =>
            {
                MethodInfo merge = typeof(LoopControl).GetMethod(
                    "MergeLoopedTracksAsync",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                Task<AudioObj?> task = (Task<AudioObj?>) merge.Invoke(dialog, [new[] { halfTime, doubleTime }])!;
                AudioObj merged = task.GetAwaiter().GetResult()!;

                Assert.AreEqual(210f, merged.Bpm, 0.001f);
            });
        }

        [STATestMethod]
        public void JumpUsesCurrentVarispeedRateForAudibleMilliseconds()
        {
            using AudioTestScope scope = new();
            AudioObj audio = scope.Create(new float[160000]);
            audio.SampleRateFactor = 1.5;

            WithPlaylist([audio], (dialog, _) =>
            {
                typeof(LoopControl).GetField("lastJumpMs", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(dialog, 100d);
                typeof(LoopControl).GetField("lastJumpValue", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(dialog, 100d);
                Field<NumericUpDown>(dialog, "numericUpDown_jump").Value = 100;
                audio.SetPosition(1000);

                MethodInfo jump = typeof(LoopControl).GetMethod(
                    "JumpByMilliseconds",
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    binder: null,
                    types: [typeof(AudioObj), typeof(int)],
                    modifiers: null)!;
                jump.Invoke(dialog, [audio, 1]);

                Assert.AreEqual(3400, audio.Position,
                    "Jump milliseconds must advance source frames by the current varispeed rate.");
            });
        }

        [STATestMethod]
        public void RateUpdatesJumpDistanceContinuouslyWithoutDiscreteHalving()
        {
            using AudioTestScope scope = new();
            AudioObj audio = scope.Create(new float[160000]);
            audio.Bpm = 120;

            WithPlaylist([audio], (dialog, _) =>
            {
                MethodInfo update = typeof(LoopControl).GetMethod(
                    "UpdateJumpDistanceForRate",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;

                audio.SampleRateFactor = 125.0 / 114.5;
                update.Invoke(dialog, [audio]);
                Assert.AreEqual(114.5, (double)Field<NumericUpDown>(dialog, "numericUpDown_jump").Value, 0.01);

                audio.SampleRateFactor = 2.0;
                update.Invoke(dialog, [audio]);
                Assert.AreEqual(62.5, (double)Field<NumericUpDown>(dialog, "numericUpDown_jump").Value, 0.01,
                    "A programmatic rate update must not be halved again by ValueChanged.");
            });
        }

        [STATestMethod]
        public void TimestretchMetadataRefreshUpdatesLoopControlTempo()
        {
            using AudioTestScope scope = new();
            AudioObj audio = scope.Create(new float[160000]);
            audio.Bpm = 120;

            WithPlaylist([audio], (dialog, list) =>
            {
                NumericUpDown jump = Field<NumericUpDown>(dialog, "numericUpDown_jump");
                Assert.AreEqual(125, (double) jump.Value, 0.01);

                audio.Bpm = 150;
                audio.StretchFactor = 1.0;
                MethodInfo refresh = typeof(LoopControl).GetMethod(
                    "RefreshAudioTiming",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                refresh.Invoke(dialog, [audio]);

                Assert.AreEqual(100, (double) jump.Value, 0.01);
                StringAssert.Contains(list.Items[0]!.ToString()!, "[150.0 BPM]");
            });
        }

        [STATestMethod]
        public void RateChangeScalesTheSelectedJumpStepInsteadOfResettingIt()
        {
            using AudioTestScope scope = new();
            AudioObj audio = scope.Create(new float[160000]);
            audio.Bpm = 150;

            WithPlaylist([audio], (dialog, _) =>
            {
                NumericUpDown jump = Field<NumericUpDown>(dialog, "numericUpDown_jump");
                MethodInfo update = typeof(LoopControl).GetMethod(
                    "UpdateJumpDistanceForRate",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                update.Invoke(dialog, [audio]);

                Assert.AreEqual(100, (double) jump.Value, 0.01);
                jump.Value = 200;
                jump.Value = 400;

                audio.ManualSampleRateFactor = 1.25;
                MethodInfo updateFromTrackView = typeof(LoopControl).GetMethod(
                    "UpdateJumpDistanceFromTrackView",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
                updateFromTrackView.Invoke(dialog, [audio]);

                Assert.AreEqual(320, (double) jump.Value, 0.01,
                    "A 400 ms selected step must be scaled by the current rate, not reset to one beat.");
            });
        }

        [STATestMethod]
        public void RateGestureKeepsValueAndInvariantTextWhileSteadyAndAfterRelease()
        {
            using AudioTestScope scope = new();
            AudioObj audio = scope.Create(new float[64000]);
            audio.Bpm = 120;
            audio.StretchFactor = 1.5;
            WithPlaylist([audio], (_, list) =>
            {
                CultureInfo previousCulture = CultureInfo.CurrentCulture;
                try
                {
                    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
                    Point text = TextPoint(list, 0);
                    Point right = new(list.ClientRectangle.Right + 20, text.Y);
                    Mouse(list, MouseDown, text);
                    Mouse(list, MouseMove, right);
                    AssertRate(audio, list);
                    PumpMessages(TimeSpan.FromMilliseconds(650));
                    AssertRate(audio, list);
                    Mouse(list, MouseMove, right);
                    Assert.IsTrue(list.Capture);
                    AssertRate(audio, list);
                    Mouse(list, MouseUp, right);
                    Assert.IsFalse(list.Capture);
                    PumpMessages(TimeSpan.FromMilliseconds(650));
                    AssertRate(audio, list);
                }
                finally
                {
                    CultureInfo.CurrentCulture = previousCulture;
                }
            });
        }

        [STATestMethod]
        public void RateGestureAccumulatesMouseMovesOnlyOnce()
        {
            using AudioTestScope scope = new();
            AudioObj audio = scope.Create(new float[64000]);
            WithPlaylist([audio], (_, list) =>
            {
                Point start = TextPoint(list, 0);
                int targetX = list.ClientRectangle.Right - 5;
                Mouse(list, MouseDown, start);
                for (int x = start.X + 20; x < targetX; x += 20)
                {
                    Mouse(list, MouseMove, new Point(x, start.Y));
                }
                Mouse(list, MouseMove, new Point(targetX, start.Y));

                Assert.IsTrue(audio.ManualSampleRateFactor > 1.0f);
                Assert.IsTrue(audio.ManualSampleRateFactor < 1.8f,
                    "Repeated mouse messages must not multiply the full drag distance repeatedly.");
                Mouse(list, MouseUp, new Point(targetX, start.Y));
            });
        }

        [STATestMethod]
        public void RateGestureReturnsToOriginalRateWhenMouseReturnsToGrabPoint()
        {
            using AudioTestScope scope = new();
            AudioObj audio = scope.Create(new float[64000]);
            WithPlaylist([audio], (_, list) =>
            {
                Point start = TextPoint(list, 0);
                Mouse(list, MouseDown, start);
                Mouse(list, MouseMove, new Point(start.X - 80, start.Y));
                Mouse(list, MouseMove, new Point(start.X - 40, start.Y));
                Mouse(list, MouseMove, start);
                Mouse(list, MouseUp, start);

                Assert.AreEqual(1.0, audio.ManualSampleRateFactor, 0.000001,
                    "Returning to the grab point must cancel the complete relative drag.");
            });
        }

        [STATestMethod]
        public void RateDragResynchronizesAfterTrackViewFineAdjustment()
        {
            using AudioTestScope scope = new();
            AudioObj audio = scope.Create(new float[64000]);

            WithPlaylist([audio], (dialog, _) =>
            {
                Dictionary<Guid, double> offsets = Field<Dictionary<Guid, double>>(dialog, "_audioRateDragOffset");
                offsets[audio.Id] = 500.0 * Math.Log2(1.25);
                audio.ManualSampleRateFactor = 1.5f;

                MethodInfo apply = typeof(LoopControl).GetMethod(
                    "ApplyPlaylistRateToAudioAsync",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                Task task = (Task) apply.Invoke(dialog, [audio, 10, false])!;
                task.GetAwaiter().GetResult();

                double expected = Math.Pow(2.0, (500.0 * Math.Log2(1.5) + 10.0) / 500.0);
                Assert.AreEqual(expected, audio.ManualSampleRateFactor, 0.000001,
                    "LoopControl must continue from the TrackView-adjusted rate, not its old drag offset.");
            });
        }

        [STATestMethod]
        public void ControlRateGestureTargetsEveryCheckedOverlapTrackNotFocusedRow()
        {
            using AudioTestScope scope = new();
            AudioObj[] audios = CreateTracks(scope);
            WithPlaylist(audios, (_, list) =>
            {
                SetChecks(list, 0, 2);
                Point start = TextPoint(list, 1);
                Point target = new(list.ClientRectangle.Right - 5, start.Y);

                WithControlKey(true, () =>
                {
                    Mouse(list, MouseDown, start);
                    Mouse(list, MouseMove, target);
                    Mouse(list, MouseUp, target);
                });

                Assert.IsTrue(audios[0].ManualSampleRateFactor > 1.0f);
                Assert.AreEqual(1.0, audios[1].ManualSampleRateFactor, 0.000001,
                    "Ctrl rate dragging must not include the focused unchecked row.");
                Assert.IsTrue(audios[2].ManualSampleRateFactor > 1.0f);
            });
        }

        [STATestMethod]
        public void ShiftRateGestureAlignsAllListedTracksFromHighestRateWhenMovingRight()
        {
            using AudioTestScope scope = new();
            AudioObj[] audios = CreateTracks(scope);
            audios[0].ManualSampleRateFactor = 1.25f;
            audios[1].ManualSampleRateFactor = 0.8f;
            audios[2].ManualSampleRateFactor = 1.5f;

            WithPlaylist(audios, (_, list) =>
            {
                Point start = TextPoint(list, 1);
                Point target = new(list.ClientRectangle.Right - 5, start.Y);
                WithShiftKey(true, () =>
                {
                    Mouse(list, MouseDown, start);
                    Mouse(list, MouseMove, target);
                    Mouse(list, MouseUp, target);
                });

                Assert.IsTrue(audios.All(audio => audio.ManualSampleRateFactor > 1.5f));
                Assert.AreEqual(audios[0].ManualSampleRateFactor, audios[1].ManualSampleRateFactor, 0.000001);
                Assert.AreEqual(audios[0].ManualSampleRateFactor, audios[2].ManualSampleRateFactor, 0.000001);
            });
        }

        [STATestMethod]
        public void ShiftRateGestureAlignsAllListedTracksFromLowestRateWhenMovingLeft()
        {
            using AudioTestScope scope = new();
            AudioObj[] audios = CreateTracks(scope);
            audios[0].ManualSampleRateFactor = 1.25f;
            audios[1].ManualSampleRateFactor = 0.8f;
            audios[2].ManualSampleRateFactor = 1.5f;

            WithPlaylist(audios, (_, list) =>
            {
                Point start = TextPoint(list, 1);
                Point target = new(10, start.Y);
                WithShiftKey(true, () =>
                {
                    Mouse(list, MouseDown, start);
                    Mouse(list, MouseMove, target);
                    Mouse(list, MouseUp, target);
                });

                Assert.IsTrue(audios.All(audio => audio.ManualSampleRateFactor < 0.8f));
                Assert.AreEqual(audios[0].ManualSampleRateFactor, audios[1].ManualSampleRateFactor, 0.000001);
                Assert.AreEqual(audios[0].ManualSampleRateFactor, audios[2].ManualSampleRateFactor, 0.000001);
            });
        }

        [STATestMethod]
        public void ControlRateClickResetsOnlyClickedTrack()
        {
            using AudioTestScope scope = new();
            AudioObj[] audios = CreateTracks(scope);
            audios[0].ManualSampleRateFactor = 1.25f;
            audios[1].ManualSampleRateFactor = 0.8f;
            audios[2].ManualSampleRateFactor = 1.5f;

            WithPlaylist(audios, (_, list) =>
            {
                Point target = TextPoint(list, 1);
                WithControlKey(true, () =>
                {
                    Mouse(list, MouseDown, target);
                    Mouse(list, MouseUp, target);
                    PumpMessages(TimeSpan.FromMilliseconds(200));
                });

                Assert.AreEqual(1.25, audios[0].ManualSampleRateFactor, 0.000001);
                Assert.AreEqual(1.0, audios[1].ManualSampleRateFactor, 0.000001);
                Assert.AreEqual(1.5, audios[2].ManualSampleRateFactor, 0.000001);
            });
        }

        [STATestMethod]
        public void ControlRightClickRateResetResetsEveryListedTrack()
        {
            using AudioTestScope scope = new();
            AudioObj[] audios = CreateTracks(scope);
            audios[0].ManualSampleRateFactor = 1.25f;
            audios[1].ManualSampleRateFactor = 0.8f;
            audios[2].ManualSampleRateFactor = 1.5f;

            WithPlaylist(audios, (dialog, list) =>
            {
                Point target = TextPoint(list, 1);
                MethodInfo rightClick = typeof(LoopControl).GetMethod(
                    "checkedListBox_playlistTracks_MouseDown",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                MethodInfo opening = typeof(LoopControl).GetMethod(
                    "contextMenuStrip_playlistItem_Opening",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;

                WithControlKey(true, () =>
                {
                    rightClick.Invoke(dialog, [list, new MouseEventArgs(MouseButtons.Right, 1, target.X, target.Y, 0)]);
                    var cancel = new System.ComponentModel.CancelEventArgs();
                    opening.Invoke(dialog, [null, cancel]);
                    Assert.IsTrue(cancel.Cancel, "Ctrl-right-click must suppress the context menu.");
                    PumpMessages(TimeSpan.FromMilliseconds(200));
                });

                foreach (AudioObj audio in audios)
                {
                    Assert.AreEqual(1.0, audio.ManualSampleRateFactor, 0.000001,
                        "Ctrl-right-click reset must affect every listed track.");
                }
            });
        }

        [STATestMethod]
        public void StopResetsManualRateButPauseStateDoesNotResetIt()
        {
            using AudioTestScope scope = new();
            AudioObj audio = scope.Create(new float[64000]);
            audio.ManualSampleRateFactor = 1.5f;
            audio.SampleRateFactor = 1.5f;

            audio.PauseAsync().GetAwaiter().GetResult();
            Assert.AreEqual(1.5, audio.ManualSampleRateFactor, 0.000001);

            audio.StopAsync().GetAwaiter().GetResult();
            Assert.AreEqual(1.0, audio.ManualSampleRateFactor, 0.000001);
            Assert.AreEqual(1.0, audio.SampleRateFactor, 0.000001);
        }

        private static void WithPlaylist(AudioObj[] audios, Action<LoopControl, CheckedListBox> verify)
        {
            Assert.AreEqual(ApartmentState.STA, Thread.CurrentThread.GetApartmentState());
            Assert.IsNull(WindowMain.Instance, "Tests must not construct WindowMain.");
            Assert.IsNull(typeof(WindowMain).GetField("_lastSelectedTrackView", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null));
            Assert.AreEqual(0, ((ICollection) typeof(WindowMain).GetField("TrackViews", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!).Count);
            using PlaylistEngine engine = new();
            using LoopControl dialog = new();
            using Timer refreshTimer = Field<Timer>(dialog, "playlistTargetsTimer");
            Dictionary<Guid, PlaylistEngine.PreparedPlaylistTrack> active = Field<Dictionary<Guid, PlaylistEngine.PreparedPlaylistTrack>>(engine, "_activePreparedTracks");
            PropertyInfo instance = typeof(WindowMain).GetProperty(nameof(WindowMain.Instance))!;
            WindowMain? previousInstance = WindowMain.Instance;
            try
            {
                foreach (AudioObj audio in audios)
                {
                    active.Add(audio.Id, new() { Audio = audio, OriginalPath = $"{audio.Id}.wav", PlayPath = $"{audio.Id}.wav" });
                }
                instance.SetValue(null, CreatePlaylistShell(engine));
                dialog.TopMost = false;
                dialog.ShowInTaskbar = false;
                dialog.Show();
                CheckedListBox list = Control<CheckedListBox>(dialog, "checkedListBox_playlistTracks");
                Assert.AreEqual(audios.Length, list.Items.Count);
                WithControlKey(false, () => verify(dialog, list));
                Assert.IsFalse(Application.OpenForms.Cast<Form>().Any(form => form is WindowMain or TrackView));
            }
            finally
            {
                refreshTimer.Stop();
                dialog.Hide();
                instance.SetValue(null, previousInstance);
                active.Clear();
            }
        }

        private static WindowMain CreatePlaylistShell(PlaylistEngine engine)
        {
            // Only GetActivePlaylistAudios uses this shell; no Form constructor, handle or main-window services run.
            WindowMain shell = (WindowMain) RuntimeHelpers.GetUninitializedObject(typeof(WindowMain));
            GC.SuppressFinalize(shell);
            typeof(WindowMain).GetField("_playlist", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(shell, engine);
            return shell;
        }

        private static AudioObj[] CreateTracks(AudioTestScope scope) =>
            [scope.Create(new float[64000]), scope.Create(new float[64000]), scope.Create(new float[64000])];

        private static void SetChecks(CheckedListBox list, params int[] checkedRows)
        {
            for (int i = 0; i < list.Items.Count; i++)
            {
                list.SetItemChecked(i, checkedRows.Contains(i));
            }
        }

        private static Point TextPoint(CheckedListBox list, int row)
        {
            Rectangle bounds = list.GetItemRectangle(row);
            return new Point(list.ClientSize.Width / 2, bounds.Top + bounds.Height / 2);
        }

        private static void Click(CheckedListBox list, Point point)
        {
            Mouse(list, MouseDown, point);
            Mouse(list, MouseUp, point);
        }

        private static void Mouse(CheckedListBox list, int message, Point point) => SendMessage(list.Handle, message,
            message == MouseUp ? IntPtr.Zero : (IntPtr) 1, (IntPtr) ((point.Y << 16) | (point.X & 0xffff)));

        private static void ClickLoop(LoopControl dialog, float fraction, bool control)
        {
            Button button = Control<Panel>(dialog, "panel_buttons").Controls.OfType<Button>()
                .Single(button => string.Equals(button.Tag?.ToString(), fraction.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
            Assert.IsTrue(button.Enabled);
            WithControlKey(control, button.PerformClick);
        }

        private static void WithControlKey(bool pressed, Action action)
        {
            byte[] previous = new byte[256];
            Assert.IsTrue(GetKeyboardState(previous));
            byte[] state = (byte[]) previous.Clone();
            state[(int) Keys.ControlKey] = state[(int) Keys.LControlKey] = pressed ? (byte) 0x80 : (byte) 0;
            state[(int) Keys.RControlKey] = 0;
            try
            {
                Assert.IsTrue(SetKeyboardState(state));
                Assert.AreEqual(pressed, System.Windows.Forms.Control.ModifierKeys.HasFlag(Keys.Control));
                action();
            }
            finally
            {
                Assert.IsTrue(SetKeyboardState(previous), "Keyboard state must be restored after each gesture.");
            }
        }

        private static void WithShiftKey(bool pressed, Action action)
        {
            byte[] previous = new byte[256];
            Assert.IsTrue(GetKeyboardState(previous));
            byte[] state = (byte[]) previous.Clone();
            state[(int) Keys.ShiftKey] = state[(int) Keys.LShiftKey] = pressed ? (byte) 0x80 : (byte) 0;
            state[(int) Keys.RShiftKey] = 0;
            try
            {
                Assert.IsTrue(SetKeyboardState(state));
                Assert.AreEqual(pressed, System.Windows.Forms.Control.ModifierKeys.HasFlag(Keys.Shift));
                action();
            }
            finally
            {
                Assert.IsTrue(SetKeyboardState(previous), "Keyboard state must be restored after each gesture.");
            }
        }

        private static void AssertLoopBounds(AudioObj audio, long start, long end)
        {
            Assert.IsTrue(audio.LoopEnabled);
            Assert.AreEqual(start, Field<long>(audio, "loopFractionStartSamples"), "Loop start must belong to this track's sample domain.");
            Assert.AreEqual(end, Field<long>(audio, "loopFractionEndSamples"), "Loop end must retain this track's own rate, channels and anchor.");
        }

        private static void AssertRate(AudioObj audio, CheckedListBox list)
        {
            Assert.AreEqual(1.2959409952, audio.ManualSampleRateFactor, 0.000001);
            Assert.AreEqual(1.2959409952, audio.SampleRateFactor, 0.000001);
            StringAssert.Contains(list.Items[0]!.ToString()!, "[233.3 BPM] {+29.6%}");
        }

        private static void PumpMessages(TimeSpan duration)
        {
            Stopwatch elapsed = Stopwatch.StartNew();
            while (elapsed.Elapsed < duration)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
        }

        private static T Control<T>(LoopControl dialog, string name) where T : System.Windows.Forms.Control =>
            (T) dialog.Controls.Find(name, true).Single();

        private static T Field<T>(object instance, string name) =>
            (T) instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetKeyboardState([Out] byte[] state);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetKeyboardState(byte[] state);
    }
}
