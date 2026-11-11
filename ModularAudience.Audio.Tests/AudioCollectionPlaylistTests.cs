using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Processing;
using ModularAudience.Forms;
using ModularAudience.Forms.Hints;
using ModularAudience.Forms.Modules;
using NAudio.Wave;

namespace ModularAudience.Audio.Tests;

[TestClass]
[DoNotParallelize]
public sealed class AudioCollectionPlaylistTests
{
    [STATestMethod]
    public void EnqueuePlaylistMenuRequiresAtLeastOneSelectedTrack()
    {
        using AudioTestScope scope = new();
        using AudioCollectionView collection = new([scope.Create([0.1f]), scope.Create([0.2f])]);
        try
        {
            ListBox list = Field<ListBox>(collection, "listBox_audios");
            ToolStripMenuItem item = Field<ToolStripMenuItem>(collection, "menuToolStripItem_enqueuePlaylist");
            Assert.AreEqual("Enqueue playlist", item.Text);
            Assert.IsTrue(Field<ContextMenuStrip>(collection, "contextMenuStrip_audios").Items.Contains(item));

            list.ClearSelected();
            collection.UpdateContextMenuState();
            Assert.IsFalse(item.Enabled);

            list.SetSelected(0, true);
            collection.UpdateContextMenuState();
            Assert.IsTrue(item.Enabled);

            list.SetSelected(1, true);
            collection.UpdateContextMenuState();
            Assert.IsTrue(item.Enabled);

            list.ClearSelected();
            collection.UpdateContextMenuState();
            Assert.IsFalse(item.Enabled);
        }
        finally
        {
            collection.Close();
        }
    }

    [TestMethod]
    public void AutoImportWhenEmptyTogglesThePlaylistProviderAndDefaultsOff()
    {
        using PlaylistEngine engine = new();
        WindowMain shell = (WindowMain)RuntimeHelpers.GetUninitializedObject(typeof(WindowMain));
        GC.SuppressFinalize(shell);
        SetField(shell, "_playlist", engine);
        using ToolStripMenuItem item = new();

        Assert.IsFalse(Field<bool>(shell, "_playlistAutoImportWhenEmpty"));
        Assert.IsNull(engine.AutoEnqueuePathProvider);

        item.Checked = true;
        typeof(WindowMain).GetMethod("playlistMenu_AutoImportWhenEmpty_Click",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(shell, [item, EventArgs.Empty]);
        Assert.IsTrue(Field<bool>(shell, "_playlistAutoImportWhenEmpty"));
        Assert.IsNotNull(engine.AutoEnqueuePathProvider);

        item.Checked = false;
        typeof(WindowMain).GetMethod("playlistMenu_AutoImportWhenEmpty_Click",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(shell, [item, EventArgs.Empty]);
        Assert.IsFalse(Field<bool>(shell, "_playlistAutoImportWhenEmpty"));
        Assert.IsNull(engine.AutoEnqueuePathProvider);
    }

    [STATestMethod]
    [DataRow("idle")]
    [DataRow("playing")]
    [DataRow("paused")]
    public void MenuEnqueuesTheGlobalSelectionAsOneNextUpBatchWithoutChangingPlayback(string state)
    {
        using AudioTestScope scope = new();
        using PlaylistEngine engine = new();
        using Button button = new();
        using Label label = new();
        using AppToolTip tooltip = new();
        tooltip.AlwaysAvailable = true;
        AudioObj first = scope.Create([0.1f, 0.2f]);
        AudioObj unselected = scope.Create([]);
        AudioObj second = scope.Create([0.3f, 0.4f]);
        string firstPath = WriteSourceFile();
        string secondPath = WriteSourceFile();
        first.FilePath = firstPath;
        second.FilePath = secondPath;
        engine.EnqueueLast(["current.wav", "existing-next.wav"]);
        typeof(PlaylistEngine).GetProperty(nameof(PlaylistEngine.IsPlaying))!.SetValue(engine, state == "playing");
        typeof(PlaylistEngine).GetProperty(nameof(PlaylistEngine.IsPaused))!.SetValue(engine, state == "paused");
        if (state != "idle")
        {
            typeof(PlaylistEngine).GetProperty(nameof(PlaylistEngine.OriginalCurrentPath))!.SetValue(engine, "current.wav");
        }
        Func<string, CancellationToken, Task<string?>> preprocessor = (_, _) => throw new AssertFailedException(
            "Enqueue must not start playback or preprocess tracks eagerly.");
        engine.BeforeTrackPlay = preprocessor;

        WindowMain shell = (WindowMain)RuntimeHelpers.GetUninitializedObject(typeof(WindowMain));
        GC.SuppressFinalize(shell);
        SetField(shell, "_playlist", engine);
        SetField(shell, "button_playlist", button);
        SetField(shell, "label_currentlyEnqueued", label);
        SetField(shell, "toolTip_playlistInfo", tooltip);
        PropertyInfo instance = typeof(WindowMain).GetProperty(nameof(WindowMain.Instance))!;
        WindowMain? previous = WindowMain.Instance;
        AudioCollectionView? collection = null;
        AudioCollectionView? other = null;
        try
        {
            // Collection construction does not need a real main window or its startup services.
            collection = new([first, unselected]);
            other = new([second]);
            Field<ListBox>(collection, "listBox_audios").SetSelected(0, true);
            Field<ListBox>(other, "listBox_audios").SetSelected(0, true);
            instance.SetValue(null, shell);
            int notifications = 0;
            engine.TrackChanged += () => notifications++;

            collection.UpdateContextMenuState();
            Field<ToolStripMenuItem>(collection, "menuToolStripItem_enqueuePlaylist").PerformClick();

            string firstKey = "ram://" + first.Id.ToString("N");
            string secondKey = "ram://" + second.Id.ToString("N");
            string[] expected = state == "idle"
                ? [firstKey, secondKey, "current.wav", "existing-next.wav"]
                : ["current.wav", firstKey, secondKey, "existing-next.wav"];
            PlaylistQueueSnapshot snapshot = engine.GetQueueSnapshot();
            CollectionAssert.AreEqual(expected, snapshot.FilePaths.ToArray());
            Assert.AreEqual(firstKey, snapshot.NextPath);
            Assert.AreEqual(1, notifications, "All selected collections must be enqueued in one ordered batch.");
            Assert.AreEqual(state == "playing", snapshot.IsPlaying);
            Assert.AreEqual(state == "paused", snapshot.IsPaused);
            Assert.AreSame(preprocessor, engine.BeforeTrackPlay, "The existing timestretch pipeline must be retained.");
            Assert.IsNull(Field<Task?>(engine, "_runLoopTask"));
            Assert.IsTrue(Field<bool>(shell, "_playlistActivityStarted"));
            StringAssert.Contains(tooltip.GetToolTip(button)!, "Queue total: 4");
            bool hintsWereEnabled = HintPolicy.Enabled;
            try
            {
                HintPolicy.SetEnabled(false);
                Assert.IsTrue(tooltip.Active, "Playlist queue information must remain available when optional hints are disabled.");
                StringAssert.Contains(tooltip.GetToolTip(button)!, "Queue total: 4");
            }
            finally
            {
                HintPolicy.SetEnabled(hintsWereEnabled);
            }

            engine.Shuffle();
            CollectionAssert.AreEquivalent(expected, engine.GetQueueSnapshot().FilePaths.ToArray());
            if (state != "idle")
            {
                Assert.AreEqual("current.wav", engine.GetQueueSnapshot().FilePaths[0]);
            }
        }
        finally
        {
            instance.SetValue(null, previous);
            other?.Close();
            collection?.Close();
            File.Delete(firstPath);
            File.Delete(secondPath);
        }
    }

    [TestMethod]
    public async Task RamTracksAreBorrowedWithoutCopyingSavingOrReloadingSamples()
    {
        using AudioTestScope scope = new();
        using PlaylistEngine engine = new();
        AudioObj first = scope.Create([0.1f, -0.2f, 0.3f, -0.4f], 8000, 2);
        first.Name = "Generated: track?";
        first.ScannedBpm = 128;
        AudioObj second = scope.Create([0.5f, -0.6f], 16000);
        second.Name = first.Name;
        second.Bpm = 140;

        float[] originalSamples = first.Data;
        engine.BeforeTrackPlay = (_, _) => throw new AssertFailedException("RAM tracks must never enter the file pipeline.");
        Assert.AreEqual(2, engine.EnqueueAudioNext([first, first, second]));
        string[] keys = engine.GetQueueSnapshot().FilePaths.ToArray();
        Assert.AreNotEqual(keys[0], keys[1]);
        PlaylistEngine.PreparedPlaylistTrack? prepared = await Prepare(engine, keys[0]);
        Assert.IsNotNull(prepared);
        Assert.AreSame(first, prepared.Audio);
        Assert.AreSame(originalSamples, prepared.Audio.Data);
        Assert.IsFalse(prepared.OwnsAudio);
        Assert.IsNull(prepared.TempPath);
        Assert.AreEqual(128f, engine.GetTrackMetadata(keys[0]).Bpm);
        Assert.AreEqual(140f, engine.GetTrackMetadata(keys[1]).Bpm);
        CollectionAssert.AreEqual(new[] { 0.1f, -0.2f, 0.3f, -0.4f }, first.Data,
            "Preparing RAM audio must not normalize or otherwise edit the collection's samples in-place.");
        Assert.AreEqual(string.Empty, first.FilePath);
        engine.Clear();
        Assert.IsFalse(first.IsDisposed, "Clearing a playlist does not own/dispose collection objects.");
        Assert.AreSame(originalSamples, first.Data);
    }

    [TestMethod]
    public async Task RemovedAndDisposedRamTracksDisappearFromTheQueueAndPreparedCache()
    {
        using AudioTestScope scope = new();
        using PlaylistEngine engine = new();
        AudioObj first = scope.Create([0.1f]);
        AudioObj second = scope.Create([0.2f]);
        engine.EnqueueLast(["disk.wav"]);
        engine.EnqueueAudioNext([first, second]);
        string firstKey = engine.GetQueueSnapshot().FilePaths[0];
        Assert.IsNotNull(await Prepare(engine, firstKey));
        engine.RemoveUnavailableAudioReferences([second.Id]);
        Assert.IsFalse(engine.GetQueueSnapshot().FilePaths.Contains(firstKey));
        Assert.IsFalse(engine.HasPreparedOriginalPath(firstKey));
        Assert.IsFalse(first.IsDisposed, "Removing a reference is not deleting its source.");
        Assert.IsNull(await Prepare(engine, firstKey));
        second.Dispose();
        CollectionAssert.AreEqual(new[] { "disk.wav" }, engine.GetQueueSnapshot().FilePaths.ToArray());
    }

    [TestMethod]
    public async Task RemovalDuringRamPreprocessingCannotPublishOrStartAnObsoleteCopy()
    {
        using AudioTestScope scope = new();
        using PlaylistEngine engine = new();
        AudioObj source = scope.Create([0.1f]);
        AudioObj processed = scope.Create([0.2f]);
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.BeforeAudioTrackPlay = async (_, ct) =>
        {
            entered.SetResult();
            await release.Task.WaitAsync(ct);
            return processed;
        };
        engine.EnqueueAudioNext([source]);
        string key = engine.GetQueueSnapshot().FilePaths[0];
        Task<PlaylistEngine.PreparedPlaylistTrack?> preparation = Prepare(engine, key);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            engine.RemoveUnavailableAudioReferences([]);
            release.SetResult();
            Assert.IsNull(await preparation.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.IsTrue(processed.IsDisposed);
            Assert.IsFalse(source.IsDisposed);
            Assert.IsFalse(engine.HasPreparedOriginalPath(key));
            Assert.AreEqual(0, engine.GetQueueSnapshot().FilePaths.Count);
        }
        finally
        {
            release.TrySetResult();
        }
    }

    [STATestMethod]
    public void RemovingTracksAndClosingTheirCollectionInvalidateReferencesButNotDiskEntries()
    {
        using AudioTestScope scope = new();
        using PlaylistEngine engine = new();
        AudioObj first = scope.Create([0.1f]);
        AudioObj second = scope.Create([0.2f]);
        WindowMain shell = (WindowMain)RuntimeHelpers.GetUninitializedObject(typeof(WindowMain));
        GC.SuppressFinalize(shell);
        SetField(shell, "_playlist", engine);
        PropertyInfo instance = typeof(WindowMain).GetProperty(nameof(WindowMain.Instance))!;
        WindowMain? previous = WindowMain.Instance;
        using AudioCollectionView collection = new([first, second]);
        try
        {
            instance.SetValue(null, shell);
            engine.EnqueueLast(["disk.wav"]);
            engine.EnqueueAudioNext([first, second]);
            string firstKey = engine.GetQueueSnapshot().FilePaths[0];
            collection.AudioC.Audios.Remove(first);
            Application.DoEvents();
            Assert.IsFalse(engine.GetQueueSnapshot().FilePaths.Contains(firstKey));
            Assert.AreEqual(2, engine.GetQueueSnapshot().FilePaths.Count);
            collection.Close();
            CollectionAssert.AreEqual(new[] { "disk.wav" }, engine.GetQueueSnapshot().FilePaths.ToArray());
        }
        finally
        {
            instance.SetValue(null, previous);
            collection.Close();
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TimestretchForRamTracksStaysInMemoryAndPreservesTheReferencedOriginal(bool useV2)
    {
        using AudioTestScope scope = new();
        using PlaylistEngine engine = new();
        AudioObj source = scope.Create(Enumerable.Range(0, 16000)
            .Select(i => 0.2f * (float)Math.Sin(i * 2 * Math.PI * 220 / 16000)).ToArray());
        source.Bpm = 100;
        float[] samples = source.Data;
        float[] originalContent = source.Data.ToArray();
        WindowMain shell = (WindowMain)RuntimeHelpers.GetUninitializedObject(typeof(WindowMain));
        GC.SuppressFinalize(shell);
        SetField(shell, "_playlistStretchSettings", new PlaylistStretchSettings(125, 0.8f, 1024, 0.5f,
            1, useV2, false, false, true, false, false));
        engine.BeforeAudioTrackPlay = (audio, ct) => (Task<AudioObj?>)typeof(WindowMain)
            .GetMethod("PreprocessPlaylistAudioAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(shell, [audio, ct])!;
        engine.BeforeTrackPlay = (_, _) => throw new AssertFailedException("No RAM-to-file conversion is allowed.");
        engine.EnqueueAudioNext([source]);
        string key = engine.GetQueueSnapshot().FilePaths[0];
        PlaylistEngine.PreparedPlaylistTrack? prepared = await Prepare(engine, key);
        Assert.IsNotNull(prepared);
        Assert.AreNotSame(source, prepared.Audio, "Destructive stretching must use a RAM working copy only at preparation time.");
        Assert.IsTrue(prepared.OwnsAudio);
        Assert.IsNull(prepared.TempPath);
        Assert.AreEqual(key, prepared.PlayPath);
        Assert.AreEqual(125.0, prepared.Audio.Bpm * prepared.Audio.StretchFactor, 0.01);
        Assert.AreSame(samples, source.Data);
        CollectionAssert.AreEqual(originalContent, source.Data, "Neither stretcher nor normalization may alter samples in the bag.");
        Assert.AreEqual(100f, source.Bpm);
        Assert.AreEqual(1.0, source.StretchFactor, 0.0001);
        engine.RemoveUnavailableAudioReferences([]);
        Assert.IsTrue(prepared.Audio.IsDisposed);
        Assert.IsFalse(source.IsDisposed);
    }

    private static Task<PlaylistEngine.PreparedPlaylistTrack?> Prepare(PlaylistEngine engine, string key) =>
        (Task<PlaylistEngine.PreparedPlaylistTrack?>)typeof(PlaylistEngine)
            .GetMethod("PrepareTrackAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(engine, [key, CancellationToken.None])!;

    [TestMethod]
    public async Task MissingRamUsesTheRememberedOriginalFileInsteadOfDroppingTheQueueEntry()
    {
        using AudioTestScope scope = new();
        using PlaylistEngine engine = new();
        AudioObj source = scope.Create([0.1f, 0.2f]);
        string path = WriteSourceFile();
        try
        {
            source.FilePath = path;
            engine.EnqueueAudioNext([source]);
            string key = engine.GetQueueSnapshot().FilePaths.Single();
            Assert.AreSame(source, (await Prepare(engine, key))!.Audio, "RAM always wins while available.");
            string? filePreprocessorPath = null;
            engine.BeforeTrackPlay = (file, _) =>
            {
                filePreprocessorPath = file;
                return Task.FromResult<string?>(null);
            };

            source.Dispose();
            engine.RemoveUnavailableAudioReferences([]);
            CollectionAssert.AreEqual(new[] { key }, engine.GetQueueSnapshot().FilePaths.ToArray());
            Assert.IsFalse(engine.HasPreparedOriginalPath(key), "The obsolete RAM preparation must be invalidated.");
            PlaylistEngine.PreparedPlaylistTrack? fallback = await Prepare(engine, key);
            Assert.IsNotNull(fallback);
            Assert.AreNotSame(source, fallback.Audio);
            Assert.IsTrue(fallback.OwnsAudio);
            Assert.IsFalse(fallback.UsesRamSource);
            Assert.AreEqual(path, fallback.PlayPath);
            Assert.AreEqual(path, filePreprocessorPath, "The normal disk/timestretch preprocessor receives the actual fallback file.");
            Assert.AreEqual(key, fallback.OriginalPath, "Queue identity must survive source changes and shuffle.");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task LatestSuccessfulExportUpdatesFallbackButNeverOverridesAvailableRam(bool exportBeforeEnqueue)
    {
        using AudioTestScope scope = new();
        using PlaylistEngine engine = new();
        using PlaylistFiles files = new();
        AudioObj source = scope.Create([0.1f, -0.2f, 0.3f, -0.4f], 8000);
        string original = WriteSourceFile();
        source.FilePath = original;
        AudioExporter exporter = new(files.Directory);
        string requested = Path.Combine(files.Directory, "Latest.wav");
        try
        {
            if (!exportBeforeEnqueue) { engine.EnqueueAudioNext([source]); }
            string? firstExport = await exporter.ExportWavAsync(source, 16, writeBpmTag: false,
                customFilePath: requested, overwriteExisting: false);
            Assert.IsNotNull(firstExport);
            if (exportBeforeEnqueue) { engine.EnqueueAudioNext([source]); }
            string key = engine.GetQueueSnapshot().FilePaths.Single();
            Assert.AreEqual(firstExport, engine.GetTrackFilePath(key));
            Assert.AreSame(source, (await Prepare(engine, key))!.Audio);

            source.Data = [0.4f, -0.3f, 0.2f, -0.1f];
            string? latestExport = await exporter.ExportWavAsync(source, 16, writeBpmTag: false,
                customFilePath: requested, overwriteExisting: false);
            Assert.IsNotNull(latestExport);
            Assert.AreNotEqual(firstExport, latestExport, "The real collision-resolved filename must be remembered.");
            Assert.AreEqual(latestExport, source.LastExportPath);
            Assert.AreEqual(original, source.FilePath, "Export must not rewrite the original source path.");
            Assert.AreEqual(latestExport, engine.GetTrackFilePath(key));
            Assert.AreSame(source, (await Prepare(engine, key))!.Audio, "Even the latest export cannot replace available RAM.");
            Assert.IsNull(await exporter.ExportWavAsync(source, 7, writeBpmTag: false,
                customFilePath: Path.Combine(files.Directory, "Failed.wav")));
            Assert.AreEqual(latestExport, engine.GetTrackFilePath(key), "A failed export must never replace a valid fallback.");

            source.Dispose();
            engine.RemoveUnavailableAudioReferences([]);
            PlaylistEngine.PreparedPlaylistTrack? fallback = await Prepare(engine, key);
            Assert.IsNotNull(fallback);
            Assert.AreEqual(latestExport, fallback.PlayPath);
            Assert.AreEqual(4, fallback.Audio.Data.Length);
        }
        finally
        {
            File.Delete(original);
        }
    }

    [TestMethod]
    public async Task LaterFilePathAndExportChangesInvalidateOnlyTheDiskFallbackPreparation()
    {
        using AudioTestScope scope = new();
        using PlaylistEngine engine = new();
        using PlaylistFiles files = new();
        AudioObj source = scope.Create([0.2f, 0.3f], 8000);
        string first = WriteSourceFile();
        string second = WriteSourceFile();
        try
        {
            source.FilePath = first;
            engine.EnqueueAudioNext([source]);
            string key = engine.GetQueueSnapshot().FilePaths.Single();
            source.FilePath = second;
            Assert.AreEqual(second, engine.GetTrackFilePath(key));
            engine.RemoveUnavailableAudioReferences([]); // Source object is alive but its collection is gone.
            PlaylistEngine.PreparedPlaylistTrack? oldFallback = await Prepare(engine, key);
            Assert.IsNotNull(oldFallback);
            Assert.AreEqual(second, oldFallback.PlayPath);
            AudioExporter exporter = new(files.Directory);
            string? exported = await exporter.ExportWavAsync(source, 16, writeBpmTag: false,
                customFilePath: Path.Combine(files.Directory, "New.wav"));
            Assert.IsNotNull(exported);
            engine.GetQueueSnapshot();
            Assert.IsTrue(oldFallback.Audio.IsDisposed);
            PlaylistEngine.PreparedPlaylistTrack? latestFallback = await Prepare(engine, key);
            Assert.IsNotNull(latestFallback);
            Assert.AreEqual(exported, latestFallback.PlayPath);
            source.Data = [0.1f, 0.2f, 0.3f, 0.4f];
            Assert.AreEqual(exported, await exporter.ExportWavAsync(source, 16, writeBpmTag: false,
                customFilePath: exported, overwriteExisting: true));
            PlaylistEngine.PreparedPlaylistTrack? overwrittenFallback = await Prepare(engine, key);
            Assert.IsNotNull(overwrittenFallback);
            Assert.AreEqual(4, overwrittenFallback.Audio.Data.Length,
                "Overwriting the same export path must invalidate an already prepared older file version.");
            Assert.IsTrue(latestFallback.Audio.IsDisposed);
            File.Delete(exported);
            CollectionAssert.AreEqual(Array.Empty<string>(), engine.GetQueueSnapshot().FilePaths.ToArray(),
                "If neither RAM nor the remembered file remains, the queue entry must be removed.");
        }
        finally
        {
            File.Delete(first);
            File.Delete(second);
        }
    }

    private sealed class PlaylistFiles : IDisposable
    {
        internal string Directory { get; } = Path.Combine(Path.GetTempPath(), "playlist-source-tests-" + Guid.NewGuid().ToString("N"));
        public PlaylistFiles() => System.IO.Directory.CreateDirectory(this.Directory);
        public void Dispose() => System.IO.Directory.Delete(this.Directory, recursive: true);
    }

    [TestMethod]
    public async Task ExportPathChangeDuringFallbackPreparationRetriesTheLatestSourceWithoutConsumingTheQueue()
    {
        using AudioTestScope scope = new();
        using PlaylistEngine engine = new();
        AudioObj source = scope.Create([0.1f]);
        string first = WriteSourceFile();
        string latest = WriteSourceFile();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            source.FilePath = first;
            engine.EnqueueAudioNext([source]);
            string key = engine.GetQueueSnapshot().FilePaths.Single();
            engine.RemoveUnavailableAudioReferences([]);
            int calls = 0;
            engine.BeforeTrackPlay = async (_, ct) =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    entered.SetResult();
                    await release.Task.WaitAsync(ct);
                }
                return null;
            };
            Task<PlaylistEngine.PreparedPlaylistTrack?> preparation = Prepare(engine, key);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            source.LastExportPath = latest;
            release.SetResult();
            PlaylistEngine.PreparedPlaylistTrack? prepared = await preparation.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsNotNull(prepared);
            Assert.AreEqual(latest, prepared.PlayPath);
            Assert.AreEqual(2, calls);
            CollectionAssert.AreEqual(new[] { key }, engine.GetQueueSnapshot().FilePaths.ToArray());
        }
        finally
        {
            release.TrySetResult();
            File.Delete(first);
            File.Delete(latest);
        }
    }

    [STATestMethod]
    public void TimestretchConfigurationUsesRamReferencesInsteadOfTryingToReadVirtualKeysAsFiles()
    {
        using AudioTestScope scope = new();
        using PlaylistEngine engine = new();
        AudioObj source = scope.Create([0.1f]);
        source.Bpm = 125;
        engine.EnqueueAudioNext([source]);
        WindowMain shell = (WindowMain)RuntimeHelpers.GetUninitializedObject(typeof(WindowMain));
        GC.SuppressFinalize(shell);
        SetField(shell, "_playlist", engine);
        using var dialog = shell.CreatePlaylistStretchDialog();
        Assert.IsTrue(dialog.IsConfigureMode);
        Assert.AreEqual(0, Field<string[]?>(dialog, "_pendingFilePaths")?.Length ?? 0);
        Assert.AreEqual(125m, Field<NumericUpDown>(dialog, "numericUpDown_initialBpm").Value);
        Assert.AreSame(source, engine.GetQueuedAudioReferences().Single());
    }

    private static string WriteSourceFile()
    {
        string path = Path.Combine(Path.GetTempPath(), $"collection-playlist-test-{Guid.NewGuid():N}.wav");
        using WaveFileWriter writer = new(path, new WaveFormat(8000, 16, 1));
        writer.Write(new byte[32], 0, 32);
        return path;
    }

    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;

    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
}
