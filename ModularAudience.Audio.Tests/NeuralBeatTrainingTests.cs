using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Generators;
using ModularAudience.Forms.Modules.Dialogs;
using System.Reflection;
using System.Text.Json;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class NeuralBeatTrainingTests
{
    private static BeatEngineConfig Config(int samples = 2) => new() { SampleCount = samples, Bars = 1, BeatsPerBar = 4, StepsPerBeat = 2,
        Interleaved = 0, LearningRate = .5f, WeightDecay = 0 };

    [TestMethod]
    public async Task FeedbackPersistsAcrossGenerationAndLearnsBothSlotsAndRests()
    {
        using var engine = new AsyncBeatGraphEngine(Config());
        bool[][] pattern = [Enumerable.Repeat(true, 8).ToArray(), new bool[8]];
        await engine.ApplyFeedbackAsync("root", pattern, 1);
        var trained = engine.Snapshot().Branches.Single();
        Assert.IsTrue(trained.Weights[0][0] > trained.Weights[0][1]);
        Assert.IsTrue(trained.DensityWeights[0][1] > trained.DensityWeights[0][0]);
        await engine.GeneratePatternAsync("root");
        Assert.AreEqual(trained.Weights[0][0], engine.Snapshot().Branches[0].Weights[0][0]);
        bool[][] rest = [new bool[8], new bool[8]];
        await engine.ApplyFeedbackAsync("root", rest, 1);
        Assert.IsTrue(engine.Snapshot().Branches[0].DensityWeights[0][0] > trained.DensityWeights[0][0]);
        Assert.AreEqual(2, engine.Snapshot().Branches[0].FeedbackCount);
        Assert.AreEqual((2L, 2), engine.GetTrainingIterations("root"));
    }

    [TestMethod]
    public async Task SingleSlotFeedbackIsFiniteAndCancellationIsAtomic()
    {
        using var engine = new AsyncBeatGraphEngine(Config(1));
        await engine.ApplyFeedbackAsync("root", Enumerable.Repeat(0, 8).ToArray(), 1);
        Assert.IsTrue(engine.Snapshot().Branches[0].Weights.All(row => row.All(float.IsFinite)));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        string before = JsonSerializer.Serialize(engine.Snapshot());
        await Assert.ThrowsAsync<OperationCanceledException>(() => engine.ApplyFeedbackAsync("root", new[] { Enumerable.Repeat(true, 8).ToArray() }, 0, cancellation.Token));
        Assert.AreEqual(before, JsonSerializer.Serialize(engine.Snapshot()));
    }

    [TestMethod]
    public async Task ForkCopiesWeightsIndependentlyAndTrimRemovesOnlyDescendants()
    {
        using var engine = new AsyncBeatGraphEngine(Config());
        engine.SetWeight("root", 0, 0, 2);
        string child = await engine.CreateRemixNodeAsync("root", 0);
        Assert.AreEqual(2f, engine.Snapshot().Branches.Single(branch => branch.Id == child).Weights[0][0]);
        engine.SetWeight(child, 0, 0, 4);
        Assert.AreEqual(2f, engine.Snapshot().Branches[0].Weights[0][0]);
        string descendant = await engine.CreateRemixNodeAsync(child, 0);
        string sibling = await engine.CreateRemixNodeAsync("root", 0);
        await engine.ApplyFeedbackAsync(child, new[] { Enumerable.Repeat(true, 8).ToArray(), new bool[8] }, 1);
        engine.TrimBranch(child);
        Assert.AreEqual((1L, 0), engine.GetTrainingIterations("root"), "Total iterations must survive trimming trained branches.");
        CollectionAssert.AreEquivalent(new[] { "root", sibling }, engine.Snapshot().Branches.Select(branch => branch.Id).ToArray());
        Assert.ThrowsExactly<InvalidOperationException>(() => engine.TrimBranch("root"));
    }

    [TestMethod]
    public async Task PatternHistoryIsDeepCopiedAndPatternForkSeedsExactRhythm()
    {
        using var engine = new AsyncBeatGraphEngine(Config());
        var generated = await engine.GeneratePatternAsync("root");
        var saved = engine.Snapshot().Branches[0].Patterns.Single();
        generated[0][0] = !generated[0][0];
        Assert.AreEqual(saved.Pattern[0][0], engine.Snapshot().Branches[0].Patterns[0].Pattern[0][0]);
        string child = engine.ForkPattern("root", saved.Id);
        var branch = engine.Snapshot().Branches.Single(branch => branch.Id == child);
        Assert.AreEqual(saved.Id, branch.SourcePatternId);
        for (int step = 0; step < 8; step++)
            Assert.AreEqual(2f, branch.DensityWeights[step][saved.Pattern.Count(row => row[step])]);
    }

    [TestMethod]
    public async Task ArchiveRoundTripWithAndWithoutPcmCanContinueTraining()
    {
        using var engine = new AsyncBeatGraphEngine(Config(1));
        using var sample = new AudioObj { Data = new float[] { 0, .2f, -.3f, .4f }, SampleRate = 16000, Channels = 1 };
        sample.Rename("Kick");
        var pattern = await engine.GeneratePatternAsync("root");
        await engine.ApplyFeedbackAsync("root", pattern, .75f);
        string child = await engine.CreateRemixNodeAsync("root", .2f);
        var document = new BeatTrainingDocument { Model = engine.Snapshot(), ActiveNodeId = child, Bpm = 95,
            Slots = [new() { Name = sample.Name, SampleRate = 16000, Channels = 1, SampleLength = 4 }] };
        string path = Path.Combine(Path.GetTempPath(), "beat-session-" + Guid.NewGuid().ToString("N") + ".nbtrain");
        try
        {
            foreach (bool embedded in new[] { false, true })
            {
                await BeatTrainingArchive.SaveAsync(path, document, [sample], embedded);
                using var restored = await BeatTrainingArchive.LoadAsync(path);
                Assert.AreEqual(child, restored.Document.ActiveNodeId);
                Assert.AreEqual(JsonSerializer.Serialize(document.Model), JsonSerializer.Serialize(restored.Engine.Snapshot()));
                Assert.AreEqual(embedded ? 1 : 0, restored.Samples.Count);
                if (embedded) CollectionAssert.AreEqual(sample.Data, restored.Samples[0].Data);
                var next = await restored.Engine.GeneratePatternAsync(child);
                await restored.Engine.ApplyFeedbackAsync(child, next, 1);
                Assert.AreEqual(1, restored.Engine.Snapshot().Branches.Single(branch => branch.Id == child).FeedbackCount);
            }
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void RestoreRejectsCyclesNonFiniteWeightsAndWrongDimensions()
    {
        using var engine = new AsyncBeatGraphEngine(Config());
        var snapshot = engine.Snapshot();
        snapshot.Branches[0].Weights[0][0] = float.NaN;
        Assert.ThrowsExactly<InvalidDataException>(() => AsyncBeatGraphEngine.Restore(snapshot));
        snapshot = engine.Snapshot(); snapshot.Branches[0].Weights[0] = [];
        Assert.ThrowsExactly<InvalidDataException>(() => AsyncBeatGraphEngine.Restore(snapshot));
        Assert.ThrowsExactly<ArgumentException>(() => new AsyncBeatGraphEngine(Config() with { SampleCount = 0 }));
    }

    [STATestMethod]
    public void SessionUiLocksParametersAndBlocksMismatchedSamples()
    {
        using var sample = new AudioObj { Data = new float[8], SampleRate = 16000, Channels = 1 };
        using var window = new NeuralBeatEngineDialog([sample]);
        using var engine = new AsyncBeatGraphEngine(Config(1));
        Set(window, "engine", engine); Set(window, "sessionLocked", true);
        Invoke(window, "UpdateSessionAvailability");
        Assert.IsFalse(Field<NumericUpDown>(window, "numericUpDown_bars").Enabled);
        Assert.IsTrue(Field<NumericUpDown>(window, "numericUpDown_bpm").Enabled);
        Assert.IsTrue(Field<Button>(window, "button_generate").Enabled);
        var collection = Field<AudioCollection>(window, "Audios");
        var clone = sample.Clone(); collection.Audios.Add(clone);
        Assert.IsFalse(Field<Button>(window, "button_generate").Enabled);
        StringAssert.Contains(Field<Label>(window, "sessionStatus").Text, "expected 1");
        collection.Audios.Remove(clone); clone.Dispose();
        Assert.IsTrue(Field<Button>(window, "button_generate").Enabled);
        Set(window, "engine", null);
        foreach (var audio in collection.Audios) audio.Dispose(); collection.Audios.Clear();
    }

    [STATestMethod]
    public void TreeExplorerAndSessionToolbarInitializeWithPatternAndWeightViews()
    {
        using var sample = new AudioObj { Data = new float[8], SampleRate = 16000, Channels = 1 };
        using var window = new NeuralBeatEngineDialog([sample]);
        _ = window.Handle;
        using var engine = new AsyncBeatGraphEngine(Config(1));
        engine.GeneratePatternAsync("root").GetAwaiter().GetResult();
        Set(window, "engine", engine); Set(window, "sessionLocked", true);
        Invoke(window, "UpdateSessionAvailability");
        Invoke(window, "Explorer_Click", null, EventArgs.Empty);
        using var explorer = Field<Form>(window, "explorer");
        var tree = Field<TreeView>(explorer, "tree");
        Assert.AreEqual(1, tree.Nodes.Count);
        Assert.AreEqual(1, tree.Nodes[0].Nodes.Count);
        Assert.AreEqual(8, Field<DataGridView>(explorer, "grid").RowCount);
        tree.SelectedNode = tree.Nodes[0].Nodes[0];
        Assert.IsTrue(Field<DataGridView>(explorer, "grid").Columns[0].ReadOnly);
        explorer.Close();
        Set(window, "engine", null);
        var collection = Field<AudioCollection>(window, "Audios");
        foreach (var audio in collection.Audios) audio.Dispose(); collection.Audios.Clear();
    }

    [STATestMethod]
    public void ClosingTrainedSessionClearsSamplesWithoutReadingDisposedEngine()
    {
        using var sample = new AudioObj { Data = new float[8], SampleRate = 16000, Channels = 1 };
        using var window = new NeuralBeatEngineDialog([sample, sample]);
        _ = window.Handle;
        var engine = new AsyncBeatGraphEngine(Config());
        engine.ApplyFeedbackAsync("root", new[] { Enumerable.Repeat(true, 8).ToArray(), new bool[8] }, 1).GetAwaiter().GetResult();
        Set(window, "engine", engine);
        Set(window, "sessionLocked", true);
        Set(window, "currentPattern", new[] { Enumerable.Repeat(true, 8).ToArray(), new bool[8] });
        Invoke(window, "NeuralBeatEngineDialog_FormClosing", window, new FormClosingEventArgs(CloseReason.UserClosing, false));
        Assert.AreEqual(0, Field<AudioCollection>(window, "Audios").Count);
        Assert.IsNull(Field<AsyncBeatGraphEngine?>(window, "engine"));
        Assert.IsNull(Field<bool[][]?>(window, "currentPattern"));
        Assert.ThrowsExactly<ObjectDisposedException>(() => engine.GetTrainingIterations("root"));
    }

    [STATestMethod]
    public void FourSamplePatternRemainsAboveFeedbackAcrossWindowSizes()
    {
        using var sample = new AudioObj { Data = new float[8], SampleRate = 16000, Channels = 1 };
        using var window = new NeuralBeatEngineDialog([sample, sample, sample, sample]);
        _ = window.Handle;
        Set(window, "currentPattern", Enumerable.Range(0, 4).Select(_ => new bool[16]).ToArray());
        var map = Field<PictureBox>(window, "pictureBox_beatMap");
        var feedback = Field<GroupBox>(window, "groupBox_feedback");
        foreach (var size in new[] { window.MinimumSize, new System.Drawing.Size(940, 850), new System.Drawing.Size(1200, 950), window.MinimumSize })
        {
            window.Size = size; window.PerformLayout();
            Assert.IsTrue(map.Parent!.Bottom <= feedback.Top, $"Pattern viewport {map.Parent.Bounds} overlaps feedback {feedback.Bounds} at {size}.");
            Assert.IsTrue(map.Bottom <= map.Parent.ClientSize.Height, $"Pattern image {map.Bounds} exceeds viewport {map.Parent.ClientSize}.");
            Assert.IsTrue(feedback.Bottom <= feedback.Parent!.ClientSize.Height, $"Feedback is outside the visible layout at {size}.");
            var flow = Field<FlowLayoutPanel>(window, "flowLayoutPanel_feedback");
            foreach (Control control in flow.Controls)
                Assert.IsTrue(control.Bottom <= flow.ClientSize.Height, $"Feedback control {control.Name} is clipped at {size}.");
        }
        var collection = Field<AudioCollection>(window, "Audios");
        foreach (var audio in collection.Audios) audio.Dispose(); collection.Audios.Clear();
    }

    [STATestMethod]
    public void LoadedSessionRestoresOptionsCountersAndLocksImmediately()
    {
        using var sample = new AudioObj { Data = new float[8], SampleRate = 16000, Channels = 1 };
        using var window = new NeuralBeatEngineDialog([sample]);
        var config = Config(1) with { Bars = 2, BeatsPerBar = 7, StepsPerBeat = 3, LearningRate = .7f,
            WeightDecay = .02f, Temperature = .65f, MinWeight = -5, MaxWeight = 8, ThreadCount = 3 };
        using var engine = new AsyncBeatGraphEngine(config);
        engine.ApplyFeedbackAsync("root", new[] { Enumerable.Repeat(true, config.TotalSteps).ToArray() }, .75f).GetAwaiter().GetResult();
        var loaded = new LoadedBeatTraining { Engine = engine, Document = new BeatTrainingDocument
        {
            Model = engine.Snapshot(), Bpm = 85, Mutation = .9f, LoopFeedback = true, LoopPlayback = false,
            Slots = [new() { Name = "Slot 1", SampleRate = 16000, Channels = 1, SampleLength = 8 }]
        } };
        window.GetType().GetMethod("ApplyLoadedTraining", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [loaded]);
        Assert.AreEqual(2m, Field<NumericUpDown>(window, "numericUpDown_bars").Value);
        Assert.AreEqual(7m, Field<NumericUpDown>(window, "numericUpDown_beatsPerBar").Value);
        Assert.AreEqual(3m, Field<NumericUpDown>(window, "numericUpDown_stepsPerBeat").Value);
        Assert.AreEqual(85m, Field<NumericUpDown>(window, "numericUpDown_bpm").Value);
        Assert.AreEqual(.7m, Field<NumericUpDown>(window, "numericUpDown_learningRate").Value);
        Assert.AreEqual(.9m, Field<NumericUpDown>(window, "numericUpDown_mutation").Value);
        Assert.IsFalse(Field<NumericUpDown>(window, "numericUpDown_bars").Enabled);
        Assert.IsFalse(Field<NumericUpDown>(window, "numericUpDown_beatsPerBar").Enabled);
        Assert.IsFalse(Field<NumericUpDown>(window, "numericUpDown_stepsPerBeat").Enabled);
        Assert.IsFalse(Field<NumericUpDown>(window, "numericUpDown_interleaved").Enabled);
        Assert.IsTrue(Field<NumericUpDown>(window, "numericUpDown_learningRate").Enabled);
        Assert.IsTrue(Field<NumericUpDown>(window, "numericUpDown_threadCount").Enabled);
        int threads = Math.Min(5, Environment.ProcessorCount);
        Assert.AreEqual((decimal)Environment.ProcessorCount, Field<NumericUpDown>(window, "numericUpDown_threadCount").Maximum);
        Field<NumericUpDown>(window, "numericUpDown_threadCount").Value = threads;
        Assert.AreEqual(threads, engine.Config.ThreadCount);
        Assert.AreEqual(threads, engine.Snapshot().Config.ThreadCount);
        Assert.IsTrue(Field<NumericUpDown>(window, "numericUpDown_mutation").Enabled);
        Field<NumericUpDown>(window, "numericUpDown_temperature").Value = 1.5m;
        Field<NumericUpDown>(window, "numericUpDown_learningRate").Value = .25m;
        Assert.AreEqual(1.5f, engine.Config.Temperature);
        Assert.AreEqual(.25f, engine.Config.LearningRate);
        var tooltips = Field<ToolTip>(window, "optionToolTips");
        foreach (string label in new[] { "label_learningRate", "label_temperature", "label_weightDecay", "label_minWeight", "label_maxWeight", "label_threadCount", "label_bars", "label_beatsPerBar", "label_stepsPerBeat", "label_bpm", "label_interleaved", "label_mutation" })
            Assert.IsFalse(string.IsNullOrWhiteSpace(tooltips.GetToolTip(Field<Label>(window, label))), label);
        Assert.IsTrue(Field<CheckBox>(window, "checkBox_loopFeedback").Checked);
        Assert.IsFalse(Field<CheckBox>(window, "checkBox_loopUntilFeedback").Checked);
        StringAssert.Contains(Field<Label>(window, "trainingIterationsLabel").Text, "Training iterations: 1");
        Set(window, "engine", null);
        var collection = Field<AudioCollection>(window, "Audios");
        foreach (var audio in collection.Audios) audio.Dispose(); collection.Audios.Clear();
    }

    [TestMethod]
    public async Task LiveLearningSettingsPreserveStructureAndPersistLatestValues()
    {
        using var engine = new AsyncBeatGraphEngine(Config());
        engine.SetWeight("root", 0, 0, 5);
        engine.SetWeight("root", 0, 1, -5);
        string child = await engine.CreateRemixNodeAsync("root", 0);
        engine.UpdateLearningSettings(.8f, .4f, .02f, -2, 3);
        var snapshot = engine.Snapshot();
        foreach (var branch in snapshot.Branches)
        {
            Assert.AreEqual(3f, branch.Weights[0][0]);
            Assert.AreEqual(-2f, branch.Weights[0][1]);
        }
        Assert.AreEqual(8, snapshot.Config.TotalSteps);
        using var restored = AsyncBeatGraphEngine.Restore(snapshot);
        Assert.AreEqual(.8f, restored.Config.LearningRate);
        Assert.AreEqual(.4f, restored.Config.Temperature);
        Assert.AreEqual(.02f, restored.Config.WeightDecay);
        Assert.AreEqual(0L, restored.GetTrainingIterations(child).Total);
        await restored.ApplyFeedbackAsync(child, new[] { Enumerable.Repeat(true, 8).ToArray(), new bool[8] }, .75f);
        Assert.AreEqual(1L, restored.GetTrainingIterations(child).Total);
    }

    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(target)!;
    private static void Set(object target, string name, object? value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    private static object? Invoke(object target, string name, params object?[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
}
