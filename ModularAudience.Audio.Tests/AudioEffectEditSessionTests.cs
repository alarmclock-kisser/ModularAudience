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
}
