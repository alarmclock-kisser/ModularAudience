using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Arrangement;
using ModularAudience.Audio.Processing;
using ModularAudience.Audio.Processors_V1;
using ModularAudience.Audio.Processors_V4;
using ModularAudience.Forms.Modules.Dialogs;
using ModularAudience.Forms.Modules;
using ModularAudience.Forms;
using System.Reflection;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class AudioDecompileTests
{
    [TestMethod]
    public async Task BeatProjectCreatesOneLosslessLanePerStem()
    {
        using AudioTestScope scope = new();
        float[] samples = Enumerable.Range(0, 24000)
            .Select(index => (float)Math.Sin(index * 0.01) * 0.5f)
            .ToArray();
        AudioObj source = scope.Create(samples, sampleRate: 16000);
        source.Bpm = 120;
        var separator = new TestStemSeparator("drums", "bass");
        var options = new AudioDecompileOptions
        {
            Output = AudioDecompileOutputKind.Project,
            ProjectDetail = AudioDecompileProjectDetail.Beats,
            MaxWorkers = 1
        };

        using AudioDecompileResult result = await AudioDecompilePipeline.RunAsync(source, separator, options);
        using MixArrangement project = result.TakeProject()
            ?? throw new AssertFailedException("The project result is missing.");

        Assert.AreEqual(2, project.Lanes.Count);
        Assert.AreEqual(120, project.Bpm);
        foreach (MixLane lane in project.Lanes)
        {
            Assert.AreEqual(3, lane.Clips.Count);
            Assert.AreEqual(0, lane.Clips[0].Start, 0.000001);
            Assert.AreEqual(0.5, lane.Clips[1].Start, 0.000001);
            Assert.AreEqual(1.0, lane.Clips[2].Start, 0.000001);
            Assert.AreEqual(lane.Clips[0].SourceOffset, lane.Clips[0].Start);
            Assert.AreEqual(lane.Clips[1].SourceOffset, lane.Clips[1].Start);
            Assert.AreEqual(0.5, lane.Clips[0].SourceDuration, 0.000001);
            Assert.AreEqual(0.5, lane.Clips[2].SourceDuration, 0.000001);
            Assert.AreEqual(0.001, lane.Clips[0].FadeIn, 0.000001);
        }

        foreach (MixSource projectSource in project.Sources.Values)
        {
            CollectionAssert.AreEqual(samples, projectSource.Data);
        }
    }

    [TestMethod]
    public void AdditionalLanesUseDeterministicOverlapColoring()
    {
        AudioDecompilePipeline.AudioDecompileAtomicPlan[] atomics =
        [
            new("First", 0, 100, null, null),
            new("Second", 50, 120, null, null),
            new("Third", 100, 50, null, null),
            new("Fourth", 175, 25, null, null)
        ];

        int[] lanes = AudioDecompilePipeline.AssignAdditionalLanes(atomics);

        CollectionAssert.AreEqual(new[] { 0, 1, 0, 0 }, lanes);
    }

    [TestMethod]
    public void AdditionalLanesPreferMatchingTonalFamilies()
    {
        using AudioTestScope scope = new();
        const int sampleRate = 16000;
        AudioObj low = scope.Create(AudioTestData.Tone(sampleRate, 120, 220, 2, 10), sampleRate);
        AudioObj high = scope.Create(AudioTestData.Tone(sampleRate, 120, 880, 2, 10), sampleRate);
        AudioDecompilePipeline.AudioDecompileAtomicPlan[] atomics =
        [
            new("Low first", 0, 100, low, null),
            new("High first", 0, 50, high, null),
            new("High repeat", 100, 100, high.Clone(), null),
            new("Low repeat", 150, 80, low.Clone(), null)
        ];
        scope.Own(atomics.Skip(2).Select(atomic => atomic.Audio!));

        int[] lanes = AudioDecompilePipeline.AssignAdditionalLanes(atomics);

        Assert.AreEqual(lanes[1], lanes[2],
            "The repeated high tone should reuse its prior tonal lane.");
        Assert.AreEqual(lanes[0], lanes[3],
            "The repeated low tone should reuse its prior tonal lane.");
    }

    [TestMethod]
    public async Task DualOutputKeepsFullProjectAndDeduplicatedCollections()
    {
        const int sampleRate = 16000;
        using AudioTestScope scope = new();
        float[] hit = AudioTestData.Hit(sampleRate);
        AudioObj source = scope.Create(AudioTestData.Track(
            sampleRate,
            1200,
            (100, hit, 1f),
            (450, hit, 1f),
            (800, hit, 1f)),
            sampleRate);
        source.Bpm = 120;
        var options = new AudioDecompileOptions
        {
            Output = AudioDecompileOutputKind.Project | AudioDecompileOutputKind.Collections,
            ProjectDetail = AudioDecompileProjectDetail.Transients,
            DeduplicateCollections = true,
            MaxWorkers = 1,
            V4Settings = LoopAtomizerSettings.Default with
            {
                MinimumRmsLevel = 0,
                MinimumAtomicDurationMs = 0,
                MaxWorkers = 1
            }
        };

        using AudioDecompileResult result = await AudioDecompilePipeline.RunAsync(
            source,
            new TestStemSeparator("drums"),
            options);
        using MixArrangement project = result.TakeProject()
            ?? throw new AssertFailedException("The dual-output project is missing.");
        IReadOnlyList<AudioDecompileCollection> collections = result.TakeCollections();
        scope.Own(collections.SelectMany(collection => collection.Atomics));

        Assert.HasCount(1, project.Lanes);
        Assert.HasCount(3, project.Lanes[0].Clips,
            "Project output must retain every atomic occurrence.");
        Assert.HasCount(1, collections);
        Assert.HasCount(1, collections[0].Atomics,
            "Collection output should contain the deduplicated representative.");
    }

    [TestMethod]
    public async Task ConfiguredAttackFadeIsAppliedToEveryProjectClip()
    {
        using AudioTestScope scope = new();
        AudioObj source = scope.Create(new float[24000], sampleRate: 16000);
        source.Bpm = 120;
        var options = new AudioDecompileOptions
        {
            Output = AudioDecompileOutputKind.Project,
            ProjectDetail = AudioDecompileProjectDetail.Bars,
            ProjectAttackMs = 37,
            MaxWorkers = 1
        };

        using AudioDecompileResult result = await AudioDecompilePipeline.RunAsync(
            source,
            new TestStemSeparator("synth"),
            options);
        using MixArrangement project = result.TakeProject()
            ?? throw new AssertFailedException("The project result is missing.");

        Assert.IsTrue(project.Lanes[0].Clips.All(clip => clip.FadeIn == 0.037));
    }

    [TestMethod]
    public async Task ProjectSnapChangesTimelineStartWithoutChangingSourceOffset()
    {
        const int sampleRate = 16000;
        using AudioTestScope scope = new();
        AudioObj source = scope.Create(
            AudioTestData.Track(
                sampleRate,
                12000,
                (1000, AudioTestData.Hit(sampleRate), 1f)),
            sampleRate);
        source.Bpm = 120;
        var options = new AudioDecompileOptions
        {
            Output = AudioDecompileOutputKind.Project,
            ProjectDetail = AudioDecompileProjectDetail.Transients,
            SnapToGrid = true,
            SnapDivision = 256,
            MaxWorkers = 1,
            V4Settings = LoopAtomizerSettings.Default with
            {
                EnableDeduplication = false,
                MinimumRmsLevel = 0,
                MinimumAtomicDurationMs = 0,
                MaxWorkers = 1
            }
        };

        using AudioDecompileResult result = await AudioDecompilePipeline.RunAsync(
            source,
            new TestStemSeparator("drums"),
            options);
        using MixArrangement project = result.TakeProject()
            ?? throw new AssertFailedException("The project result is missing.");
        MixClip clip = project.Lanes[0].Clips.Single();
        double step = 240d / 120d / 256d;

        Assert.IsTrue(project.Snap);
        Assert.AreEqual(256, project.SnapDivision);
        Assert.IsGreaterThan(0, clip.SourceOffset);
        Assert.AreEqual(
            Math.Round(clip.SourceOffset / step, MidpointRounding.AwayFromZero) * step,
            clip.Start,
            0.000001);
    }

    [TestMethod]
    public void ArrangementValidationAcceptsFineSnapDivisions()
    {
        using var mix = new MixArrangement
        {
            Bpm = 120,
            SnapDivision = 128
        };
        mix.Validate();
        mix.SnapDivision = 256;
        mix.Validate();
    }

    [TestMethod]
    public async Task SeparationProgressEndsAtEightyFivePercentBeforeAtomizing()
    {
        using AudioTestScope scope = new();
        AudioObj source = scope.Create(new float[16000], sampleRate: 16000);
        source.Bpm = 120;
        var updates = new List<AudioDecompileProgress>();
        var progress = new Progress<AudioDecompileProgress>(update =>
        {
            lock (updates)
            {
                updates.Add(update);
            }
        });
        var options = new AudioDecompileOptions
        {
            Output = AudioDecompileOutputKind.Project,
            MaxWorkers = 1,
            V4Settings = LoopAtomizerSettings.Default with { MaxWorkers = 1 }
        };

        using AudioDecompileResult result = await AudioDecompilePipeline.RunAsync(
            source,
            new TestStemSeparator("drums"),
            options,
            progress);
        await Task.Delay(20);

        AudioDecompileProgress separationEnd = updates
            .Last(update => update.Stage.StartsWith("Test separator", StringComparison.Ordinal));
        AudioDecompileProgress atomizeStart = updates
            .First(update => update.Stage.StartsWith("Atomizing", StringComparison.Ordinal));
        Assert.AreEqual(0.85, separationEnd.Fraction, 0.000001);
        Assert.IsTrue(atomizeStart.Fraction >= 0.85);
        Assert.IsTrue(atomizeStart.Fraction <= 0.995);
    }

    [TestMethod]
    public async Task CollectionDeduplicationCanBeDisabledWithoutAffectingSource()
    {
        const int sampleRate = 16000;
        using AudioTestScope scope = new();
        float[] hit = AudioTestData.Hit(sampleRate);
        float[] track = AudioTestData.Track(
            sampleRate, 1200, (100, hit, 1f), (450, hit, 1f), (800, hit, 1f));
        AudioObj source = scope.Create(track, sampleRate);
        source.Bpm = 120;
        var baseOptions = new AudioDecompileOptions
        {
            Output = AudioDecompileOutputKind.Collections,
            MaxWorkers = 1,
            V4Settings = LoopAtomizerSettings.Default with
            {
                MinimumRmsLevel = 0,
                MinimumAtomicDurationMs = 0,
                MaxWorkers = 1
            }
        };

        using AudioDecompileResult deduplicated = await AudioDecompilePipeline.RunAsync(
            source, new TestStemSeparator("drums"), baseOptions with { DeduplicateCollections = true });
        IReadOnlyList<AudioDecompileCollection> deduplicatedCollections = deduplicated.TakeCollections();
        scope.Own(deduplicatedCollections.SelectMany(collection => collection.Atomics));
        Assert.AreEqual(1, deduplicatedCollections[0].Atomics.Count);

        using AudioDecompileResult complete = await AudioDecompilePipeline.RunAsync(
            source, new TestStemSeparator("drums"), baseOptions with { DeduplicateCollections = false });
        IReadOnlyList<AudioDecompileCollection> completeCollections = complete.TakeCollections();
        scope.Own(completeCollections.SelectMany(collection => collection.Atomics));
        Assert.AreEqual(3, completeCollections[0].Atomics.Count);
        CollectionAssert.AreEqual(track, source.Data, "Decompile must not alter the owned source snapshot.");
    }

    [TestMethod]
    public async Task TransientAtomicsExposeOriginalFrameTimingForProjectPlacement()
    {
        const int sampleRate = 16000;
        using AudioTestScope scope = new();
        float[] hit = AudioTestData.Hit(sampleRate);
        AudioObj source = scope.Create(
            AudioTestData.Track(sampleRate, 800, (100, hit, 1f), (450, hit, 1f)),
            sampleRate);

        LoopAtomizerResult atomized = await LoopAtomizer_V4.AtomizeAsync(
            source,
            LoopAtomizerSettings.Default with
            {
                EnableDeduplication = false,
                MinimumRmsLevel = 0,
                MinimumAtomicDurationMs = 0,
                MaxWorkers = 1
            });
        scope.Own(atomized.Atomics);

        Assert.AreEqual(2, atomized.Atomics.Count);
        long[] starts = atomized.Atomics.Select(atomic =>
        {
            Assert.IsTrue(AudioAtomicTiming.TryGet(atomic, out long start, out long count));
            Assert.IsTrue(count > 0);
            return start;
        }).ToArray();
        Assert.IsTrue(starts[0] > 0 && starts[0] < sampleRate / 5);
        Assert.IsTrue(starts[1] > sampleRate / 3);
        Assert.IsTrue(starts[1] > starts[0]);
    }

    [TestMethod]
    public async Task LegacyAtomicsAlsoPreserveOriginalFrameTiming()
    {
        const int sampleRate = 16000;
        using AudioTestScope scope = new();
        float[] hit = AudioTestData.Hit(sampleRate);
        AudioObj source = scope.Create(
            AudioTestData.Track(sampleRate, 800, (100, hit, 1f), (450, hit, 1f)),
            sampleRate);

        AudioObj[] atomics = (await LoopAtomizer.AtomizeLoopAsync(
            source,
            similarityThreshold: null,
            cancellationToken: CancellationToken.None,
            deduplicate: false)).ToArray();
        scope.Own(atomics);

        Assert.IsTrue(atomics.Length > 0);
        foreach (AudioObj atomic in atomics)
        {
            Assert.IsTrue(AudioAtomicTiming.TryGet(atomic, out long start, out long count));
            Assert.IsTrue(start >= 0);
            Assert.IsTrue(count > 0);
        }
    }

    [TestMethod]
    public async Task CancellationStopsBeforeAnyOutputIsPublished()
    {
        using AudioTestScope scope = new();
        AudioObj source = scope.Create(new float[16000], sampleRate: 16000);
        source.Bpm = 120;
        using var cancellation = new CancellationTokenSource();
        var separator = new BlockingStemSeparator();
        Task<AudioDecompileResult> run = AudioDecompilePipeline.RunAsync(
            source,
            separator,
            new AudioDecompileOptions { MaxWorkers = 1 },
            cancellation.Token);

        await separator.Started;
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => run);
        Assert.IsFalse(separator.Completed);
        Assert.IsFalse(source.IsDisposed, "The pipeline must not dispose its caller-owned source snapshot.");
    }

    [STATestMethod]
    public void ConfigDialogSnapshotSurvivesOriginalDisposal()
    {
        AudioObj source = new()
        {
            Name = "Original",
            OriginalName = "Original",
            Data = [0.1f, 0.2f, 0.3f],
            SampleRate = 16000,
            Channels = 1,
            BitDepth = 32,
            Length = 3,
            Duration = TimeSpan.FromSeconds(3.0 / 16000.0),
            Bpm = 128
        };
        DecompileConfigDialog? dialog = null;
        try
        {
            dialog = new DecompileConfigDialog(source);
            FieldInfo field = typeof(DecompileConfigDialog).GetField(
                "sourceSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new AssertFailedException("The dialog snapshot field is missing.");
            AudioObj snapshot = field.GetValue(dialog) as AudioObj
                ?? throw new AssertFailedException("The dialog snapshot is missing.");
            Assert.AreNotSame(source, snapshot);
            Assert.AreNotSame(source.Data, snapshot.Data);
            source.Dispose();
            Assert.IsFalse(snapshot.IsDisposed);
            CollectionAssert.AreEqual(new[] { 0.1f, 0.2f, 0.3f }, snapshot.Data);
            Assert.AreEqual(128, snapshot.Bpm);
        }
        finally
        {
            dialog?.Dispose();
            source.Dispose();
        }
    }

    [STATestMethod]
    public void ConfigDialogCanTransferSnapshotAndCloseBeforeRunCompletes()
    {
        AudioObj source = new()
        {
            Name = "Original",
            Data = [0.1f, 0.2f],
            SampleRate = 16000,
            Channels = 1,
            Length = 2,
            Duration = TimeSpan.FromSeconds(2.0 / 16000.0)
        };
        var dialog = new DecompileConfigDialog(source);
        AudioObj snapshot = dialog.TransferSourceSnapshotOwnership();
        try
        {
            dialog.Close();
            Assert.IsTrue(dialog.IsDisposed);
            Assert.IsFalse(snapshot.IsDisposed);
            CollectionAssert.AreEqual(new[] { 0.1f, 0.2f }, snapshot.Data);
        }
        finally
        {
            snapshot.Dispose();
            source.Dispose();
        }
    }

    [STATestMethod]
    public void DecompiledProjectLoadsIntoMixTrackView()
    {
        var project = new MixArrangement
        {
            Name = "Decompiled Test",
            Bpm = 124,
            LoopEnd = 1
        };
        using AudioTestScope scope = new();
        AudioObj audio = scope.Create(new float[16000], sampleRate: 16000);
        MixSource source = project.AddSource(audio);
        var lane = new MixLane { Name = "drums" };
        project.Lanes.Add(lane);
        project.AddClip(lane, source, 0);
        using var view = new MixTrackView();

        view.LoadDecompiledProject(project);

        Assert.AreSame(project, view.Arrangement);
        Assert.AreEqual("Decompiled Test", view.Text);
        Assert.AreEqual(124, view.Arrangement.Bpm);
        Assert.AreEqual(1, view.Arrangement.Lanes.Count);
        Assert.AreEqual("drums", view.Arrangement.Lanes[0].Name);
    }

    [STATestMethod]
    public void DecompileDialogRestoresPersistedOutputAndAtomizerSettings()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "ModularAudience.DecompileTests",
            Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "settings.json");
        using AudioTestScope scope = new();
        AudioObj source = scope.Create(new float[16000], sampleRate: 16000);
        try
        {
            using (var first = new DecompileConfigDialog(source, path))
            {
                SetDialogField(first, "numeric_minSlice", new Action<NumericUpDown>(value => value.Value = 95));
                SetDialogField(first, "numeric_tailPadding", new Action<NumericUpDown>(value => value.Value = 50));
                SetDialogField(first, "numeric_minimumRms", new Action<NumericUpDown>(value => value.Value = 0.0023M));
                SetDialogField(first, "numeric_projectAttack", new Action<NumericUpDown>(value => value.Value = 12));
                SetDialogField(first, "checkBox_projectOutput", new Action<CheckBox>(value => value.Checked = true));
                SetDialogField(first, "checkBox_collectionOutput", new Action<CheckBox>(value => value.Checked = true));
                SetDialogField(first, "comboBox_projectOverlap", new Action<ComboBox>(value => value.SelectedIndex = 1));
                SetDialogField(first, "numeric_analogWindow", new Action<NumericUpDown>(value => value.Value = 8192));
                InvokePrivate(first, "SavePersistentSettings");
            }

            using var restored = new DecompileConfigDialog(source, path);
            Assert.AreEqual(95m, GetDialogField<NumericUpDown>(restored, "numeric_minSlice").Value);
            Assert.AreEqual(50m, GetDialogField<NumericUpDown>(restored, "numeric_tailPadding").Value);
            Assert.AreEqual(0.0023m, GetDialogField<NumericUpDown>(restored, "numeric_minimumRms").Value);
            Assert.AreEqual(12m, GetDialogField<NumericUpDown>(restored, "numeric_projectAttack").Value);
            Assert.IsTrue(GetDialogField<CheckBox>(restored, "checkBox_projectOutput").Checked);
            Assert.IsTrue(GetDialogField<CheckBox>(restored, "checkBox_collectionOutput").Checked);
            Assert.AreEqual(1, GetDialogField<ComboBox>(restored, "comboBox_projectOverlap").SelectedIndex);
            Assert.AreEqual(8192m, GetDialogField<NumericUpDown>(restored, "numeric_analogWindow").Value);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [STATestMethod]
    public void DecompileMenuIsEnabledOnlyForOneSelection()
    {
        using AudioTestScope scope = new();
        AudioObj first = scope.Create([0.1f], sampleRate: 8000);
        AudioObj second = scope.Create([0.2f], sampleRate: 8000);
        using var collection = new AudioCollectionView([first, second], "Decompile menu test");
        ListBox list = typeof(AudioCollectionView).GetField(
            "listBox_audios", BindingFlags.Instance | BindingFlags.NonPublic)?
            .GetValue(collection) as ListBox
            ?? throw new AssertFailedException("Collection list box is missing.");
        ToolStripMenuItem decompile = typeof(AudioCollectionView).GetField(
            "menuToolStripItem_decompile", BindingFlags.Instance | BindingFlags.NonPublic)?
            .GetValue(collection) as ToolStripMenuItem
            ?? throw new AssertFailedException("Decompile menu item is missing.");

        list.SetSelected(0, true);
        collection.UpdateContextMenuState();
        Assert.IsTrue(decompile.Enabled);

        list.SetSelected(1, true);
        collection.UpdateContextMenuState();
        Assert.IsFalse(decompile.Enabled);
        collection.Close();
    }

    private sealed class TestStemSeparator(params string[] stemNames) : IAudioStemSeparator
    {
        public string DisplayName => "Test separator";
        public int EstimatedStemCount => stemNames.Length;

        public Task<AudioStemSeparationResult> SeparateAsync(
            AudioObj source,
            IProgress<AudioStemSeparationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AudioDecompileStem[] stems = stemNames.Select(name =>
            {
                AudioObj audio = source.Clone();
                audio.Name = name;
                audio.OriginalName = name;
                return new AudioDecompileStem(name, audio);
            }).ToArray();
            progress?.Report(new AudioStemSeparationProgress(1, $"{stems.Length} test stems"));
            return Task.FromResult(new AudioStemSeparationResult(stems, []));
        }
    }

    private sealed class BlockingStemSeparator : IAudioStemSeparator
    {
        private readonly TaskCompletionSource started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string DisplayName => "Blocking separator";
        public int EstimatedStemCount => 1;
        public Task Started => this.started.Task;
        public bool Completed { get; private set; }

        public async Task<AudioStemSeparationResult> SeparateAsync(
            AudioObj source,
            IProgress<AudioStemSeparationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            this.started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            this.Completed = true;
            return new AudioStemSeparationResult([], []);
        }
    }

    private static T GetDialogField<T>(object target, string name) where T : class =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?
            .GetValue(target) as T
        ?? throw new AssertFailedException($"Field '{name}' is missing.");

    private static void SetDialogField<T>(object target, string name, Action<T> set) where T : class =>
        set(GetDialogField<T>(target, name));

    private static void InvokePrivate(object target, string name) =>
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(target, null);
}
