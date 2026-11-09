using System.Diagnostics;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Arrangement;
using ModularAudience.Forms;
using ModularAudience.Forms.Modules;
using ModularAudience.Forms.Modules.Dialogs;

namespace ModularAudience.Audio.Tests;

[TestClass]
[DoNotParallelize]
public sealed class AudioCollectionMultitrackTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [STATestMethod]
    public void ContextMenuShowsTheSelectionCountAndRequiresAtLeastOneAudio()
    {
        using var fixture = new Fixture(120, 120, 120);
        ListBox list = Field<ListBox>(fixture.Collection, "listBox_audios");
        ToolStripMenuItem item = Field<ToolStripMenuItem>(fixture.Collection, "menuToolStripItem_openMultitrack");
        Assert.IsTrue(Field<ContextMenuStrip>(fixture.Collection, "contextMenuStrip_audios").Items.Contains(item));
        foreach (int count in new[] { 0, 1, 3, 0 })
        {
            list.ClearSelected();
            for (int i = 0; i < count; i++) { list.SetSelected(i, true); }
            fixture.Collection.UpdateContextMenuState();
            Assert.AreEqual($"Open as Multitrack View ({count})", item.Text);
            Assert.AreEqual(count > 0, item.Enabled);
        }
    }

    [STATestMethod]
    [DataRow(1)]
    [DataRow(3)]
    public void ContextMenuOpensOneMixWithAllSelectedTracksJoinedInListOrder(int count)
    {
        using var fixture = new Fixture(137.5f, 137.5f, 137.5f);
        ListBox list = Field<ListBox>(fixture.Collection, "listBox_audios");
        list.ClearSelected();
        for (int i = 0; i < count; i++) { list.SetSelected(i, true); }
        fixture.Collection.UpdateContextMenuState();
        MixTrackView[] before = MixTrackView.OpenMixes.ToArray();
        Field<ToolStripMenuItem>(fixture.Collection, "menuToolStripItem_openMultitrack").PerformClick();
        WaitUntil(() => !Field<bool>(fixture.Collection, "openingMultitrack"));
        MixTrackView mix = MixTrackView.OpenMixes.Except(before).Single();
        fixture.AddMix(mix);
        Assert.IsTrue(mix.Visible);
        Assert.AreEqual(137.5, mix.Arrangement.Bpm);
        CollectionAssert.AreEqual(fixture.Audios.Take(count).Select(audio => audio.Name).ToArray(),
            mix.Arrangement.Lanes.Select(lane => lane.Name).ToArray());
        Assert.AreEqual(count, WindowMain.TrackViews.Count(view => ReferenceEquals(view.JoinedMix, mix)));
        Assert.IsTrue(WindowMain.TrackViews.Where(view => ReferenceEquals(view.JoinedMix, mix)).All(view => !view.Visible));
        Assert.IsTrue(mix.Arrangement.Lanes.All(lane => lane.Clips.Single().Start == 0));
        for (int i = 0; i < count; i++)
        {
            MixSource source = mix.Arrangement.Sources[mix.Arrangement.Lanes[i].Clips.Single().SourceId];
            Assert.AreNotSame(fixture.Audios[i].Data, source.Data);
            CollectionAssert.AreEqual(fixture.OriginalSamples[i], fixture.Audios[i].Data);
        }
    }

    [STATestMethod]
    public void BatchCollectsOnlyMismatchedTempoDialogsBeforeStartingAnyStretchAndPreservesOriginals()
    {
        using var fixture = new Fixture(100, 80, 100, 150);
        MixTrackView mix = fixture.NewMix();
        int dialogs = 0;
        mix.DialogPresenter = form =>
        {
            Assert.IsInstanceOfType<TimeStretchDialog>(form, "Batch import must directly ask for stretch settings, not open a tempo-choice prompt.");
            var stretch = (TimeStretchDialog)form;
            int audioIndex = dialogs++ == 0 ? 1 : 3;
            StringAssert.Contains(stretch.Text, fixture.Audios[audioIndex].Name);
            Assert.IsTrue(stretch.IsConfigureMode);
            Assert.IsTrue(mix.Visible, "The first track is already visible while later tracks are configured.");
            Assert.AreEqual(100d, mix.Arrangement.Bpm);
            Assert.AreEqual(1, mix.Arrangement.Lanes.Count);
            Assert.AreEqual(1, mix.Arrangement.Sources.Count);
            Assert.IsFalse(Field<bool>(mix, "busy"), "No DSP import operation may start until every dialog has completed.");
            Assert.AreEqual((decimal)fixture.Audios[audioIndex].Bpm, Field<NumericUpDown>(stretch, "numericUpDown_initialBpm").Value);
            NumericUpDown target = Field<NumericUpDown>(stretch, "numericUpDown_targetBpm");
            Assert.AreEqual(100m, target.Minimum);
            Assert.AreEqual(100m, target.Maximum);
            Confirm(stretch, useV2: dialogs == 2);
            return Task.FromResult(DialogResult.OK);
        };

        Drain(mix.JoinCollectionSelectionAsync(fixture.Audios));
        Assert.AreEqual(2, dialogs);
        Assert.AreEqual(100d, mix.Arrangement.Bpm);
        Assert.AreEqual(100m, Field<NumericUpDown>(mix, "bpm").Value);
        CollectionAssert.AreEqual(fixture.Audios.Select(audio => audio.Name).ToArray(), mix.Arrangement.Lanes.Select(lane => lane.Name).ToArray());
        int[] lengths = [8000, 6400, 8000, 12000];
        for (int i = 0; i < fixture.Audios.Length; i++)
        {
            MixClip clip = mix.Arrangement.Lanes[i].Clips.Single();
            MixSource imported = mix.Arrangement.Sources[clip.SourceId];
            Assert.AreEqual(0d, clip.Start);
            Assert.AreEqual(ClipTempoMode.Original, clip.TempoMode, "The prepared source must not be stretched a second time on playback.");
            Assert.AreEqual(lengths[i], imported.Data.Length);
            Assert.AreEqual(100f, imported.Bpm);
            MixSource original = MixTempoOrigins.Resolve(mix.Arrangement, imported).Origin;
            CollectionAssert.AreEqual(fixture.OriginalSamples[i], original.Data, "Future tempo changes must retain the pristine pre-import source.");
            CollectionAssert.AreEqual(fixture.OriginalSamples[i], fixture.Audios[i].Data, "Bag audio must never be edited by import DSP.");
            Assert.AreEqual(fixture.Tempos[i], fixture.Audios[i].Bpm);
        }
    }

    [STATestMethod]
    public void CancellingALaterDialogKeepsTheFirstTrackWithoutStretchingOrLeakingUncommittedWindows()
    {
        using var fixture = new Fixture(100, 80, 150);
        MixTrackView mix = fixture.NewMix();
        int dialogs = 0;
        mix.DialogPresenter = form =>
        {
            Assert.IsFalse(Field<bool>(mix, "busy"));
            if (++dialogs == 2) { return Task.FromResult(DialogResult.Cancel); }
            Confirm((TimeStretchDialog)form, useV2: true);
            return Task.FromResult(DialogResult.OK);
        };
        Drain(mix.JoinCollectionSelectionAsync(fixture.Audios));
        WaitUntil(() => fixture.NewTrackViews().Count() == 1);
        Assert.AreEqual(2, dialogs);
        Assert.AreEqual(1, mix.Arrangement.Lanes.Count);
        Assert.AreEqual(1, mix.Arrangement.Sources.Count);
        Assert.AreEqual(fixture.Audios[0].Name, mix.Arrangement.Lanes[0].Name);
        Assert.AreSame(mix, fixture.NewTrackViews().Single().JoinedMix);
        for (int i = 0; i < fixture.Audios.Length; i++) { CollectionAssert.AreEqual(fixture.OriginalSamples[i], fixture.Audios[i].Data); }
    }

    [STATestMethod]
    public void ClosingTheMixDuringConfigurationCancelsTheRemainderAndCleansHiddenWindows()
    {
        using var fixture = new Fixture(100, 80, 150);
        MixTrackView mix = fixture.NewMix();
        Task joining = mix.JoinCollectionSelectionAsync(fixture.Audios);
        WaitUntil(() => mix.WindowGroup.Windows.Any(window => window is TimeStretchDialog));
        Assert.IsFalse(joining.IsCompleted);
        mix.Dispose();
        Drain(joining);
        WaitUntil(() => fixture.NewTrackViews().Count() == 1);
        Assert.IsTrue(fixture.NewTrackViews().Single().Visible, "Only the committed first track is restored by the normal Join lifecycle.");
        Assert.AreEqual(0, mix.WindowGroup.Windows.Count);
    }

    [STATestMethod]
    public void FirstTrackCanUseScannedBpmAndEqualOrUnknownTemposDoNotPrompt()
    {
        using var fixture = new Fixture(0, 132, 0);
        fixture.Audios[0].ScannedBpm = 132;
        MixTrackView mix = fixture.NewMix();
        mix.DialogPresenter = _ => throw new AssertFailedException("Matching and unknown source tempos must not prompt.");
        Drain(mix.JoinCollectionSelectionAsync(fixture.Audios));
        Assert.AreEqual(132d, mix.Arrangement.Bpm);
        Assert.AreEqual(3, mix.Arrangement.Lanes.Count);
        Assert.AreEqual(0f, fixture.Audios[0].Bpm);
        Assert.AreEqual(132f, fixture.Audios[0].ScannedBpm);
    }

    private static void Confirm(TimeStretchDialog dialog, bool useV2)
    {
        NumericUpDown chunk = Field<NumericUpDown>(dialog, "numericUpDown_chunkSize");
        chunk.Tag = 2048; chunk.Value = 1024;
        Field<NumericUpDown>(dialog, "numericUpDown_threads").Value = 1;
        Field<CheckBox>(dialog, "checkBox_offload").Checked = false;
        Field<CheckBox>(dialog, "checkBox_trim").Checked = false;
        typeof(TimeStretchDialog).GetMethod("SaveConfirmedSettings", Private)!.Invoke(dialog, [useV2]);
    }

    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private)!.GetValue(target)!;
    private static void Drain(Task task)
    {
        WaitUntil(() => task.IsCompleted, 30);
        task.GetAwaiter().GetResult();
    }
    private static void WaitUntil(Func<bool> done, int seconds = 10)
    {
        SynchronizationContext? context = SynchronizationContext.Current;
        Stopwatch timeout = Stopwatch.StartNew();
        while (!done() && timeout.Elapsed < TimeSpan.FromSeconds(seconds))
        {
            Application.DoEvents();
            SynchronizationContext.SetSynchronizationContext(context);
            Thread.Sleep(1);
        }
        Assert.IsTrue(done(), "The UI operation did not complete before the test timeout.");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly WinFormsTestContext context = new();
        private readonly HashSet<TrackView> previousTracks = WindowMain.TrackViews.ToHashSet();
        private readonly List<MixTrackView> mixes = [];
        internal AudioObj[] Audios { get; }
        internal float[][] OriginalSamples { get; }
        internal float[] Tempos { get; }
        internal AudioCollectionView Collection { get; }
        internal Fixture(params float[] tempos)
        {
            this.Tempos = tempos;
            this.Audios = tempos.Select((tempo, i) => new AudioObj
            {
                Name = "Selected " + (i + 1), Bpm = tempo, SampleRate = 16000, Channels = 1, BitDepth = 32,
                Data = Enumerable.Range(0, 8000).Select(frame => 0.2f * (float)Math.Sin(2 * Math.PI * (220 + i * 30) * frame / 16000)).ToArray(),
                Length = 8000, Duration = TimeSpan.FromSeconds(0.5)
            }).ToArray();
            this.OriginalSamples = this.Audios.Select(audio => audio.Data.ToArray()).ToArray();
            this.Collection = new AudioCollectionView(this.Audios, "Multitrack selection regression");
        }
        internal void AddMix(MixTrackView mix) => this.mixes.Add(mix);
        internal MixTrackView NewMix() { var mix = new MixTrackView(); this.AddMix(mix); return mix; }
        internal IEnumerable<TrackView> NewTrackViews() => WindowMain.TrackViews.Where(view => !this.previousTracks.Contains(view));
        public void Dispose()
        {
            foreach (MixTrackView mix in this.mixes) { mix.Dispose(); }
            foreach (TrackView view in this.NewTrackViews().ToArray()) { view.Close(); }
            WaitUntil(() => !this.NewTrackViews().Any());
            this.Collection.Close();
            foreach (AudioObj audio in this.Audios) { audio.Dispose(); }
            this.context.Dispose();
        }
    }
}
