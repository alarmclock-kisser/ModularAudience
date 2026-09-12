using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Forms;
using ModularAudience.Forms.Modules;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using Timer = System.Windows.Forms.Timer;

namespace ModularAudience.Audio.Tests
{
    [TestClass]
    [DoNotParallelize]
    public sealed class LoopControlLifecycleTests
    {
        private const string NoTargetsText = "Target: no active playlist tracks";

        [STATestMethod]
        public void ConstructionWithoutMainWindowOrTrackViewInitializesControls()
        {
            WithIsolatedDialog(dialog =>
            {
                Assert.AreEqual("ModularAudience.Forms", dialog.GetType().Assembly.GetName().Name);
                Assert.IsFalse(dialog.IsDisposed);
                Assert.IsFalse(dialog.Visible);
                Assert.AreEqual(FormWindowState.Normal, dialog.WindowState);
                Assert.AreEqual(250, GetRefreshTimer(dialog).Interval);
                Assert.AreEqual(NoTargetsText, GetControl<Label>(dialog, "label_targetMode").Text);
                Assert.AreEqual(0, GetControl<CheckedListBox>(dialog, "checkedListBox_playlistTracks").Items.Count);
                Button[] loopButtons = GetControl<Panel>(dialog, "panel_buttons").Controls.OfType<Button>().ToArray();
                Assert.IsTrue(loopButtons.Length > 0, "Loop buttons must be created without any track.");
                Assert.IsTrue(loopButtons.All(button => !button.Enabled), "Loop buttons must be disabled without targets.");
            });
        }

        [STATestMethod]
        public void ShowWithoutTargetsDisplaysRestoredDialogAndRefreshesPlaylist()
        {
            WithIsolatedDialog(dialog =>
            {
                dialog.Show();

                AssertVisibleAndRestored(dialog);
                AssertPlaylistRefreshRuns(dialog);
            });
        }

        [STATestMethod]
        public void CloseCancelsDisposalHidesDialogAndStopsPlaylistRefresh()
        {
            WithIsolatedDialog(dialog =>
            {
                dialog.Show();
                AssertPlaylistRefreshRuns(dialog);
                bool closingWasCanceled = false;
                int closedEvents = 0;
                dialog.FormClosing += (_, e) => closingWasCanceled = e.Cancel;
                dialog.FormClosed += (_, _) => closedEvents++;

                dialog.Close();

                Assert.IsTrue(closingWasCanceled, "Closing must be canceled to keep the dialog reusable.");
                Assert.AreEqual(0, closedEvents, "A hidden dialog must not raise FormClosed.");
                Assert.IsFalse(dialog.Visible);
                Assert.IsFalse(dialog.IsDisposed);
                Timer timer = GetRefreshTimer(dialog);
                Assert.IsFalse(timer.Enabled, "Closing to hide must stop the playlist refresh timer.");
                Label label = GetControl<Label>(dialog, "label_targetMode");
                const string hiddenText = "Hidden dialog must not refresh";
                label.Text = hiddenText;
                PumpMessagesUntil(() => label.Text != hiddenText, TimeSpan.FromMilliseconds(timer.Interval * 2));
                Assert.AreEqual(hiddenText, label.Text, "A stopped timer must not refresh the hidden dialog.");
            });
        }

        [STATestMethod]
        public void ShowAfterCloseDisplaysSameDialogVisibleAndRestored()
        {
            WithIsolatedDialog(dialog =>
            {
                dialog.Show();
                IntPtr originalHandle = dialog.Handle;
                dialog.Close();
                Assert.IsFalse(dialog.Visible);
                Assert.IsFalse(dialog.IsDisposed);

                dialog.Show();

                AssertVisibleAndRestored(dialog);
                Assert.AreEqual(originalHandle, dialog.Handle, "Reopening must reuse the hidden dialog's window.");
            });
        }

        [STATestMethod]
        public void ShowAfterCloseRestartsPlaylistRefreshTimer()
        {
            WithIsolatedDialog(dialog =>
            {
                dialog.Show();
                AssertPlaylistRefreshRuns(dialog);
                Timer originalTimer = GetRefreshTimer(dialog);
                dialog.Close();
                Assert.IsFalse(originalTimer.Enabled);

                dialog.Show();

                AssertVisibleAndRestored(dialog);
                Assert.AreSame(originalTimer, GetRefreshTimer(dialog), "Reopening must reuse the refresh timer.");
                AssertPlaylistRefreshRuns(dialog);
            });
        }

        [STATestMethod]
        public void CloseKeepsHiddenDialogRegisteredForReuse()
        {
            WithIsolatedDialog(dialog =>
            {
                FieldInfo windowField = GetMainField("LoopControlWindow");
                Assert.AreSame(dialog, windowField.GetValue(null));
                dialog.Show();

                dialog.Close();

                Assert.IsFalse(dialog.Visible);
                Assert.IsFalse(dialog.IsDisposed);
                Assert.AreSame(dialog, windowField.GetValue(null),
                    "Closing to hide must retain LoopControlWindow so the next open reuses the dialog.");
            });
        }

        private static void WithIsolatedDialog(Action<LoopControl> verify)
        {
            Assert.AreEqual(ApartmentState.STA, Thread.CurrentThread.GetApartmentState());
            AssertNoMainWindowOrTrackView();
            FieldInfo windowField = GetMainField("LoopControlWindow");
            object? previousWindow = windowField.GetValue(null);
            using LoopControl dialog = new();
            using Timer refreshTimer = GetRefreshTimer(dialog);
            try
            {
                windowField.SetValue(null, dialog);
                verify(dialog);
            }
            finally
            {
                refreshTimer.Stop();
                windowField.SetValue(null, previousWindow);
                AssertNoMainWindowOrTrackView();
            }
        }

        private static void AssertNoMainWindowOrTrackView()
        {
            Assert.IsNull(WindowMain.Instance, "Lifecycle tests must not construct WindowMain.");
            Assert.IsNull(GetMainField("_lastSelectedTrackView").GetValue(null), "No TrackView may be selected.");
            Assert.AreEqual(0, ((ICollection) GetMainField("TrackViews").GetValue(null)!).Count,
                "Lifecycle tests must run without any TrackView.");
            Assert.IsFalse(Application.OpenForms.Cast<Form>().Any(form => form is WindowMain or TrackView),
                "Lifecycle tests must not open WindowMain or TrackView.");
        }

        private static void AssertVisibleAndRestored(LoopControl dialog)
        {
            Assert.IsFalse(dialog.IsDisposed);
            Assert.IsTrue(dialog.Visible, "The dialog must be visible after showing or reopening.");
            Assert.IsTrue(dialog.IsHandleCreated);
            Assert.AreEqual(FormWindowState.Normal, dialog.WindowState, "The dialog must be restored, not minimized.");
        }

        private static void AssertPlaylistRefreshRuns(LoopControl dialog)
        {
            Assert.IsTrue(GetRefreshTimer(dialog).Enabled,
                "The playlist refresh timer must be enabled while LoopControl is visible, including after reopening.");
            Label label = GetControl<Label>(dialog, "label_targetMode");
            label.Text = "Waiting for playlist refresh";
            PumpMessagesUntil(() => label.Text == NoTargetsText, TimeSpan.FromSeconds(3));
            Assert.AreEqual(NoTargetsText, label.Text, "A real timer tick must refresh the playlist target label.");
        }

        private static void PumpMessagesUntil(Func<bool> condition, TimeSpan timeout)
        {
            Stopwatch elapsed = Stopwatch.StartNew();
            while (!condition() && elapsed.Elapsed < timeout)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
        }

        private static T GetControl<T>(LoopControl dialog, string name) where T : Control
        {
            Control[] matches = dialog.Controls.Find(name, true);
            Assert.AreEqual(1, matches.Length, $"Expected exactly one control named '{name}'.");
            return (T) matches[0];
        }

        private static Timer GetRefreshTimer(LoopControl dialog) =>
            (Timer) typeof(LoopControl).GetField("playlistTargetsTimer", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(dialog)!;

        private static FieldInfo GetMainField(string name) =>
            typeof(WindowMain).GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!;
    }
}
