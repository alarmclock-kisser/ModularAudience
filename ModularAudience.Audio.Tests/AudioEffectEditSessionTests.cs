using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Effects;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class AudioEffectEditSessionTests
{
    [TestMethod]
    public void SelectionCommit_IsOneUndoRedoStepAndPreservesOutsideSamplesAndMetadata()
    {
        using var audio = new AudioObj { Data = [1, 2, 3, 4, 5, 6, 7, 8], SampleRate = 8000, Channels = 2,
            SelectionStart = 2, SelectionEnd = 6, Length = 8, Duration = TimeSpan.FromSeconds(4d / 8000), Name = "Original" };
        float[] before = (float[])audio.Data.Clone();
        AudioEffectEditSession session = AudioEffectEditSession.Capture(audio);

        Assert.IsTrue(session.Apply(audio, [30, 40, 50, 60]));

        CollectionAssert.AreEqual(new float[] { 1, 2, 30, 40, 50, 60, 7, 8 }, audio.Data);
        Assert.AreEqual(1, audio.PreviousSteps.Count);
        Assert.AreEqual("Original", audio.Name);
        Assert.AreEqual(8L, audio.Length);
        Assert.IsTrue(audio.Undo());
        CollectionAssert.AreEqual(before, audio.Data);
        Assert.IsTrue(audio.Redo());
        CollectionAssert.AreEqual(new float[] { 1, 2, 30, 40, 50, 60, 7, 8 }, audio.Data);
    }

    [TestMethod]
    public void CaptureAndUnchangedOutput_DoNotModifyUndoHistory()
    {
        using var audio = new AudioObj { Data = [0.1f, 0.2f], SampleRate = 8000, Channels = 1 };
        AudioEffectEditSession session = AudioEffectEditSession.Capture(audio);

        Assert.IsFalse(session.Apply(audio, session.Input.ToArray()));

        Assert.IsFalse(audio.CanUndo);
        Assert.IsFalse(audio.CanRedo);
    }

    [TestMethod]
    public void InPlaceOrOutsideEdits_MakeModelessSessionStaleWithoutCreatingUndoStep()
    {
        using var audio = new AudioObj { Data = [1, 2, 3, 4], SampleRate = 8000, Channels = 1, SelectionStart = 1, SelectionEnd = 3 };
        AudioEffectEditSession session = AudioEffectEditSession.Capture(audio);
        audio.Data[0] = 9;

        Assert.IsFalse(session.IsCurrent(audio));
        Assert.ThrowsExactly<InvalidOperationException>(() => session.Apply(audio, [20, 30]));
        Assert.AreEqual(9f, audio.Data[0]);
        Assert.IsFalse(audio.CanUndo);
    }

    [TestMethod]
    public void ApplyingNewEffectAfterUndo_ClearsRedo()
    {
        using var audio = new AudioObj { Data = [1, 2], SampleRate = 8000, Channels = 1 };
        AudioEffectEditSession.Capture(audio).Apply(audio, [3, 4]);
        audio.Undo();
        Assert.IsTrue(audio.CanRedo);

        AudioEffectEditSession.Capture(audio).Apply(audio, [5, 6]);

        Assert.IsFalse(audio.CanRedo);
    }

    [TestMethod]
    [DataRow(2)]
    [DataRow(6)]
    public void DurationChangingSelection_RipplesSuffixAndRestoresSelectionPositionGridAndFormatWithUndo(int renderedSamples)
    {
        using var audio = new AudioObj { Data = [1, 2, 3, 4, 5, 6, 7, 8], Channels = 2, SampleRate = 8000, Length = 8,
            Duration = TimeSpan.FromSeconds(4d / 8000), SelectionStart = 2, SelectionEnd = 6, StartingOffset = 6,
            ScrollOffset = 6, Bpm = 120, ScannedBpm = 121, BeatGrid = [true, false, false, true] };
        audio.SetPosition(3); audio.UpdateLoopFraction(2, 6, 4, true, false);
        AudioEffectEditSession session = AudioEffectEditSession.Capture(audio);
        float[] processed = Enumerable.Repeat(9f, renderedSamples).ToArray();

        Assert.IsTrue(session.Apply(audio, new AudioEffectResult(processed, 2, 0.5)));

        CollectionAssert.AreEqual(new float[] { 1, 2 }.Concat(processed).Concat([7f, 8f]).ToArray(), audio.Data);
        Assert.AreEqual((long)renderedSamples + 4, audio.Length);
        Assert.AreEqual((renderedSamples + 4d) / 16000, audio.Duration.TotalSeconds, 1e-7);
        Assert.AreEqual(2L, audio.SelectionStart); Assert.AreEqual(2L + renderedSamples, audio.SelectionEnd);
        Assert.AreEqual(2L + renderedSamples, audio.StartingOffset); Assert.AreEqual(2L + renderedSamples, audio.ScrollOffset);
        Assert.AreEqual(1L + renderedSamples / 2, audio.Position);
        Assert.AreEqual(120f, audio.Bpm, "A local duration change must not change the whole track's BPM.");
        Assert.AreEqual(121f, audio.ScannedBpm);
        Assert.IsTrue(audio.BeatGrid[^1]); Assert.AreEqual(1, audio.PreviousSteps.Count);
        Assert.IsTrue(audio.Undo());
        CollectionAssert.AreEqual(new float[] { 1, 2, 3, 4, 5, 6, 7, 8 }, audio.Data);
        Assert.AreEqual(6L, audio.SelectionEnd); Assert.AreEqual(3L, audio.Position); Assert.AreEqual(6L, audio.LoopEndSamples);
        Assert.IsTrue(audio.Redo());
        Assert.AreEqual(2L + renderedSamples, audio.SelectionEnd); Assert.AreEqual(2L + renderedSamples, audio.LoopEndSamples);
    }

    [TestMethod]
    public void MonoConversion_ChangesWholeTrackFormatButOnlyDualMonosASelection()
    {
        using var audio = new AudioObj { Data = [1, 3, 2, 4, 3, 5], Channels = 2, SampleRate = 8000, Length = 6 };
        audio.SelectionStart = 2; audio.SelectionEnd = 4;
        Assert.IsTrue(AudioEffectEditSession.Capture(audio).Apply(audio, new AudioEffectResult([3], 1)));
        CollectionAssert.AreEqual(new float[] { 1, 3, 3, 3, 3, 5 }, audio.Data); Assert.AreEqual(2, audio.Channels);
        audio.Undo(); audio.SelectionStart = audio.SelectionEnd = -1;
        Assert.IsTrue(AudioEffectEditSession.Capture(audio).Apply(audio, new AudioEffectResult([2, 3, 4], 1)));
        Assert.AreEqual(1, audio.Channels); Assert.AreEqual(3L, audio.Length);
        Assert.IsTrue(audio.Undo()); Assert.AreEqual(2, audio.Channels);
        Assert.IsTrue(audio.Redo()); Assert.AreEqual(1, audio.Channels);
    }

    [TestMethod]
    public void InvalidOrStaleVariableLengthOutput_CannotCreateHistoryOrOverwriteAudio()
    {
        using var audio = new AudioObj { Data = [1, 2, 3, 4], Channels = 2, SampleRate = 8000 };
        AudioEffectEditSession session = AudioEffectEditSession.Capture(audio);
        Assert.ThrowsExactly<ArgumentException>(() => session.Apply(audio, [1, 2, 3]));
        Assert.ThrowsExactly<ArgumentException>(() => session.Apply(audio, [float.NaN, 0]));
        Assert.ThrowsExactly<ArgumentException>(() => session.Apply(audio, []));
        Assert.IsFalse(audio.CanUndo);
        audio.Data = [5, 6, 7, 8];
        Assert.ThrowsExactly<InvalidOperationException>(() => session.Apply(audio, [9, 10]));
        CollectionAssert.AreEqual(new float[] { 5, 6, 7, 8 }, audio.Data); Assert.IsFalse(audio.CanUndo);
    }
}
