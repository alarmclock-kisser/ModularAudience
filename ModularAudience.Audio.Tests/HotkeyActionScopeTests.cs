using System.Diagnostics;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Forms;
using ModularAudience.Forms.Hotkeys;
using ModularAudience.Forms.Modules;
using ModularAudience.Forms.Modules.Dialogs;

namespace ModularAudience.Audio.Tests
{
    [TestClass]
    [DoNotParallelize]
    public sealed class HotkeyActionScopeTests
    {
        private WinFormsTestContext? uiContext;

        [TestInitialize]
        public void Initialize()
        {
            this.uiContext = new WinFormsTestContext();
            Assert.IsNull(WindowMain.Instance);
            Assert.AreEqual(0, WindowMain.TrackViews.Count);
            Assert.AreEqual(0, WindowMain.CollectionViews.Count);
        }

        [TestCleanup]
        public void Cleanup()
        {
            try
            {
                foreach (TimeStretchDialog dialog in Application.OpenForms.OfType<TimeStretchDialog>().ToArray())
                {
                    dialog.Close();
                }
                foreach (TrackView view in WindowMain.TrackViews.ToArray())
                {
                    CloseTrack(view);
                }
                foreach (AudioCollectionView collection in WindowMain.CollectionViews.ToArray())
                {
                    collection.Close();
                    collection.Dispose();
                }
                WindowMain.LastSelectedCollectionView = null;
                Assert.AreEqual(0, WindowMain.TrackViews.Count);
                Assert.AreEqual(0, WindowMain.CollectionViews.Count);
            }
            finally
            {
                this.uiContext?.Dispose();
            }
        }

        [STATestMethod]
        public void TrackViewHotkeyBindsToItsIndexAndReusesThatIndexForANewTrack()
        {
            using AudioTestScope scope = new();
            TrackView first = new(scope.Create(new float[4096]));
            TrackView second = new(scope.Create(new float[4096]));
            Assert.AreEqual(0, first.TrackViewId);
            Assert.AreEqual(1, second.TrackViewId);
            CheckBox firstMute = Find<CheckBox>(first, "checkBox_mute");
            CheckBox secondMute = Find<CheckBox>(second, "checkBox_mute");
            HotkeyActionTarget target = ClickTarget(first, firstMute);
            Assert.AreEqual(0, target.TrackViewId);
            Assert.IsFalse(target.UseLastFocusedTrackView);
            Assert.AreEqual(string.Empty, target.OwnerTitle);

            WindowMain.LastSelectedTrackView = second;
            Assert.AreSame(first, HotkeyActionResolver.ResolveOwner(target));
            bool originalSecondMute = secondMute.Checked;
            bool originalFirstMute = firstMute.Checked;
            Assert.IsTrue(HotkeyActionResolver.ExecuteControlGesture(target));
            Assert.AreEqual(!originalFirstMute, firstMute.Checked);
            Assert.AreEqual(originalSecondMute, secondMute.Checked);

            CloseTrack(first);
            WindowMain.LastSelectedTrackView = second;
            Assert.IsNull(HotkeyActionResolver.ResolveOwner(target));
            Assert.IsFalse(HotkeyActionResolver.ExecuteControlGesture(target), "A missing #0 must not execute on #1.");

            AudioObj replacementAudio = scope.Create(new float[4096]);
            replacementAudio.Name = "Different track at the same index";
            TrackView replacement = new(replacementAudio);
            Assert.AreEqual(0, replacement.TrackViewId);
            WindowMain.LastSelectedTrackView = second;
            Assert.AreSame(replacement, HotkeyActionResolver.ResolveOwner(target));
            CheckBox replacementMute = Find<CheckBox>(replacement, "checkBox_mute");
            bool originalReplacementMute = replacementMute.Checked;
            Assert.IsTrue(HotkeyActionResolver.ExecuteControlGesture(target));
            Assert.AreEqual(!originalReplacementMute, replacementMute.Checked);
        }

        [STATestMethod]
        public void EveryTrackViewCaptureKindStoresTheIndexNotLastFocusOrTrackName()
        {
            using AudioTestScope scope = new();
            TrackView view = new(scope.Create(new float[4096]));
            HScrollBar scrollBar = Find<HScrollBar>(view, "hScrollBar_rate");
            Control waveform = Find<PictureBox>(view, "pictureBox_waveform");
            ContextMenuStrip menu = Field<ContextMenuStrip>(view, "contextMenu_waveform");
            ToolStripItem item = menu.Items.OfType<ToolStripMenuItem>().First();
            using NumericUpDown numeric = new() { Name = "testNumeric" };
            view.Controls.Add(numeric);
            HotkeyActionTarget[] targets =
            [
                ClickTarget(view, waveform),
                HotkeyActionResolver.BuildScrollBarTarget(scrollBar, view),
                HotkeyActionResolver.BuildNumericUpDownTarget(numeric, view, true, HotkeyModifiers.None),
                HotkeyActionResolver.BuildContextMenuItemTarget(waveform, view, item, HotkeyModifiers.None, waveform.PointToScreen(Point.Empty))
            ];
            foreach (HotkeyActionTarget target in targets)
            {
                Assert.AreEqual(view.TrackViewId, target.TrackViewId);
                Assert.IsFalse(target.UseLastFocusedTrackView);
                Assert.AreEqual(string.Empty, target.OwnerTitle);
            }
        }

        [STATestMethod]
        public void CollectionCheckboxTargetsFollowCurrentOrLastFocusInsteadOfRecordedName()
        {
            using AudioTestScope scope = new();
            AudioCollectionView first = new([scope.Create([0f])], "Original named collection");
            AudioCollectionView second = new([scope.Create([0f])], "Another collection");
            CheckBox firstPreview = Find<CheckBox>(first, "checkBox_preview");
            CheckBox secondPreview = Find<CheckBox>(second, "checkBox_preview");
            HotkeyActionTarget target = ClickTarget(first, firstPreview);
            Assert.AreEqual(string.Empty, target.OwnerTitle);
            string actionName = HotkeyActionResolver.BuildActionName(first, firstPreview, HotkeyMouseGesture.LeftClick, HotkeyModifiers.None, target);
            StringAssert.Contains(actionName, "current/last focused");
            Assert.IsFalse(actionName.Contains(first.Text, StringComparison.Ordinal));

            Focus(second);
            using Form focusSink = new();
            focusSink.Show();
            focusSink.Activate();
            Application.DoEvents();
            Assert.AreSame(second, HotkeyActionResolver.ResolveOwner(target));
            bool originalFirst = firstPreview.Checked;
            bool originalSecond = secondPreview.Checked;
            Assert.IsTrue(HotkeyActionResolver.ExecuteControlGesture(target));
            Assert.AreEqual(originalFirst, firstPreview.Checked);
            Assert.AreEqual(!originalSecond, secondPreview.Checked);

            first.Close();
            Focus(second);
            Assert.AreSame(second, HotkeyActionResolver.ResolveOwner(target), "Closing the recording collection must not invalidate the action.");
            Assert.IsTrue(HotkeyActionResolver.ExecuteControlGesture(target));
            Assert.AreEqual(originalSecond, secondPreview.Checked);
        }

        [STATestMethod]
        public void CollectionContextActionRunsOnceForTheCombinedSelectionAfterRecordingViewCloses()
        {
            using AudioTestScope scope = new();
            AudioCollectionView recording = new([scope.Create([0f])], "Recorded here");
            HotkeyActionTarget target = ContextTarget(recording, "menuToolStripItem_timeStretchSelected");
            Assert.IsTrue(target.UseCollectionSelection);
            ToolStripItem recordedItem = MenuItem(recording, "menuToolStripItem_timeStretchSelected");
            Assert.AreEqual("AudioCollectionView(s) Selection -> Time-Stretch Selected",
                HotkeyActionResolver.BuildContextMenuItemActionName(recording, recordedItem));
            recording.Close();

            AudioObj firstAudio = scope.Create(new float[128]);
            AudioObj secondAudio = scope.Create(new float[128]);
            AudioCollectionView first = new([firstAudio], "First selection");
            AudioCollectionView second = new([secondAudio], "Second selection");
            second.UpdateContextMenuState();
            Assert.IsFalse(MenuItem(second, "menuToolStripItem_timeStretchSelected").Enabled);
            Find<ListBox>(first, "listBox_audios").SetSelected(0, true);
            Find<ListBox>(second, "listBox_audios").SetSelected(0, true);
            Focus(second);

            Assert.IsTrue(HotkeyActionResolver.ExecuteContextMenuItem(target));
            TimeStretchDialog[] dialogs = Application.OpenForms.OfType<TimeStretchDialog>().ToArray();
            Assert.AreEqual(1, dialogs.Length, "A cross-collection selection must open one dialog, not one per collection.");
            CollectionAssert.AreEquivalent(new[] { firstAudio, secondAudio }, dialogs[0].Tracks);
            CollectionAssert.AreEquivalent(new[] { firstAudio, secondAudio }, first.GetContextAudios());
        }

        [STATestMethod]
        public void ContextHotkeyWithNoSelectionDoesNotReuseStaleMenuEnabledState()
        {
            using AudioTestScope scope = new();
            AudioCollectionView collection = new([scope.Create([0f])]);
            ListBox list = Find<ListBox>(collection, "listBox_audios");
            list.SetSelected(0, true);
            collection.UpdateContextMenuState();
            HotkeyActionTarget target = ContextTarget(collection, "menuToolStripItem_timeStretchSelected");
            list.ClearSelected();

            Assert.IsFalse(HotkeyActionResolver.ExecuteContextMenuItem(target));
            Assert.AreEqual(0, Application.OpenForms.OfType<TimeStretchDialog>().Count());
        }

        [STATestMethod]
        public void MoveSelectionHotkeyRemovesSelectedAudiosFromAllSourceCollections()
        {
            using AudioTestScope scope = new();
            AudioObj selectedFirst = scope.Create([0f]);
            AudioObj selectedSecond = scope.Create([0f]);
            AudioObj unselected = scope.Create([0f]);
            AudioCollectionView first = new([selectedFirst, unselected]);
            AudioCollectionView second = new([selectedSecond]);
            HotkeyActionTarget target = ContextTarget(first, "menuToolStripItem_toNewCollection");
            Find<ListBox>(first, "listBox_audios").SetSelected(0, true);
            Find<ListBox>(second, "listBox_audios").SetSelected(0, true);
            Focus(second);

            Assert.IsTrue(HotkeyActionResolver.ExecuteContextMenuItem(target));
            AudioCollectionView moved = WindowMain.CollectionViews.Single(view => view != first && view != second);
            CollectionAssert.AreEquivalent(new[] { selectedFirst, selectedSecond }, moved.AudioC.Audios.ToArray());
            CollectionAssert.AreEqual(new[] { unselected }, first.AudioC.Audios.ToArray());
            Assert.AreEqual(0, second.AudioCount);
        }

        [STATestMethod]
        public void DeleteSelectionHotkeyRemovesSelectedAudiosFromAllSourceCollections()
        {
            using AudioTestScope scope = new();
            AudioObj selectedFirst = scope.Create([0f]);
            AudioObj selectedSecond = scope.Create([0f]);
            AudioObj unselected = scope.Create([0f]);
            AudioCollectionView first = new([selectedFirst, unselected]);
            AudioCollectionView second = new([selectedSecond]);
            HotkeyActionTarget target = ContextTarget(first, "menuToolStripItem_delete");
            Find<ListBox>(first, "listBox_audios").SetSelected(0, true);
            Find<ListBox>(second, "listBox_audios").SetSelected(0, true);
            Focus(second);

            Assert.IsTrue(HotkeyActionResolver.ExecuteContextMenuItem(target));
            PumpUntil(() => first.AudioCount == 1 && second.AudioCount == 0);
            CollectionAssert.AreEqual(new[] { unselected }, first.AudioC.Audios.ToArray());
        }

        [STATestMethod]
        public void DeleteDetachesEveryCollectionOnTheUiThreadBeforeSlowBackgroundDisposal()
        {
            using AudioTestScope scope = new();
            AudioObj firstAudio = scope.Create([0f]);
            AudioObj secondAudio = scope.Create([0f]);
            AudioCollectionView first = new([firstAudio]);
            AudioCollectionView second = new([secondAudio]);
            Find<ListBox>(first, "listBox_audios").SetSelected(0, true);
            Find<ListBox>(second, "listBox_audios").SetSelected(0, true);
            int uiThread = Environment.CurrentManagedThreadId;
            List<int> mutationThreads = [];
            first.AudioC.Audios.ListChanged += (_, _) => mutationThreads.Add(Environment.CurrentManagedThreadId);
            second.AudioC.Audios.ListChanged += (_, _) => mutationThreads.Add(Environment.CurrentManagedThreadId);
            using ManualResetEventSlim disposalEntered = new();
            using ManualResetEventSlim allowDisposal = new();
            firstAudio.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(AudioObj.Data))
                {
                    disposalEntered.Set();
                    allowDisposal.Wait(TimeSpan.FromSeconds(5));
                }
            };

            // Deliberately omit a UI synchronization context. List mutations must all
            // finish before the first disposal can yield onto a thread-pool thread.
            SynchronizationContext? previous = SynchronizationContext.Current;
            Task deletion;
            try
            {
                SynchronizationContext.SetSynchronizationContext(null);
                deletion = first.DeleteSelectedAudiosAsync();
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
            }
            try
            {
                Assert.IsTrue(disposalEntered.Wait(TimeSpan.FromSeconds(5)));
                Assert.AreEqual(0, first.AudioCount);
                Assert.AreEqual(0, second.AudioCount, "The second bound list must be detached before the first disposal finishes.");
                Assert.IsTrue(mutationThreads.Count >= 2);
                Assert.IsTrue(mutationThreads.All(thread => thread == uiThread));
            }
            finally
            {
                allowDisposal.Set();
                PumpUntil(() => deletion.IsCompleted);
                deletion.GetAwaiter().GetResult();
            }
        }

        [STATestMethod]
        public void CollectionDoubleClickHotkeyOpensTheIndexedItemDespiteScrollingResizingAndCursorPosition()
        {
            using AudioTestScope scope = new();
            AudioCollectionView recording = new([scope.Create([0f])], "Record collection");
            ListBox recordedList = Find<ListBox>(recording, "listBox_audios");
            Rectangle bounds = recordedList.GetItemRectangle(0);
            HotkeyActionTarget target = HotkeyActionResolver.BuildTarget(recordedList, recording,
                HotkeyMouseGesture.LeftDoubleClick, HotkeyModifiers.None,
                recordedList.PointToScreen(new Point(bounds.Left + 2, bounds.Top + bounds.Height / 2)));
            Assert.AreEqual(0, target.CollectionItemIndex);
            StringAssert.Contains(HotkeyActionResolver.BuildActionName(recording, recordedList,
                HotkeyMouseGesture.LeftDoubleClick, HotkeyModifiers.None, target), "Track item #0");
            recording.Close();

            AudioObj[] audios = Enumerable.Range(0, 12).Select(index =>
            {
                AudioObj audio = scope.Create(new float[128]);
                audio.Name = $"Current item {index}";
                return audio;
            }).ToArray();
            AudioCollectionView collection = new(audios, "Different collection");
            collection.Height = 170;
            ListBox list = Find<ListBox>(collection, "listBox_audios");
            list.TopIndex = 5;
            Focus(collection);
            Point originalCursor = Cursor.Position;
            try
            {
                Cursor.Position = new Point(collection.Right + 30, collection.Bottom + 30);
                Assert.IsTrue(HotkeyActionResolver.ExecuteControlGesture(target));
                TrackView opened = WindowMain.TrackViews.Single();
                Assert.AreEqual(audios[0].Name, opened.OriginalAudio.Name);
                Assert.AreSame(collection.AudioC, opened.SourceCollection);
                target.CollectionItemIndex = 99;
                Assert.IsFalse(HotkeyActionResolver.ExecuteControlGesture(target));
                Assert.AreEqual(1, WindowMain.TrackViews.Count, "A missing item must be a no-op, not another selection.");
            }
            finally
            {
                Cursor.Position = originalCursor;
            }
        }

        private static HotkeyActionTarget ClickTarget(Form owner, Control control) => HotkeyActionResolver.BuildTarget(
            control, owner, HotkeyMouseGesture.LeftClick, HotkeyModifiers.None,
            control.PointToScreen(new Point(control.Width / 2, control.Height / 2)));

        private static HotkeyActionTarget ContextTarget(AudioCollectionView collection, string itemName)
        {
            ListBox list = Find<ListBox>(collection, "listBox_audios");
            return HotkeyActionResolver.BuildContextMenuItemTarget(list, collection,
                MenuItem(collection, itemName), HotkeyModifiers.None, list.PointToScreen(Point.Empty));
        }

        private static ToolStripItem MenuItem(AudioCollectionView collection, string name) =>
            Field<ContextMenuStrip>(collection, "contextMenuStrip_audios").Items.Find(name, true).Single();

        private static void Focus(AudioCollectionView collection)
        {
            collection.Activate();
            Find<ListBox>(collection, "listBox_audios").Focus();
            Application.DoEvents();
            Assert.AreSame(collection, WindowMain.LastSelectedCollectionView);
        }

        private static void CloseTrack(TrackView view)
        {
            if (!view.IsDisposed)
            {
                view.Close();
                PumpUntil(() => view.IsDisposed);
            }
        }

        private static void PumpUntil(Func<bool> complete)
        {
            Stopwatch watch = Stopwatch.StartNew();
            while (!complete() && watch.Elapsed < TimeSpan.FromSeconds(5))
            {
                Application.DoEvents();
                Thread.Sleep(1);
            }
            Assert.IsTrue(complete(), "UI operation did not complete within five seconds.");
        }

        private static T Find<T>(Control owner, string name) where T : Control => (T)owner.Controls.Find(name, true).Single();
        private static T Field<T>(object instance, string name) =>
            (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
    }
}
