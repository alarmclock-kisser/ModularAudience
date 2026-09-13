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
