using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Forms;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class AudioCommandSelectionTests
{
    [TestMethod]
    public void SingleCollectionSelectionWorksWithoutATrackView()
    {
        using AudioTestScope scope = new();
        AudioObj audio = scope.Create([0f]);
        AudioCommandSelection selection = new();

        Assert.AreSame(audio, selection.Resolve(null, [audio]));
    }

    [TestMethod]
    public void LastInteractionWinsEvenWhenTheSameTrackViewIsRefocused()
    {
        using AudioTestScope scope = new();
        AudioObj collectionAudio = scope.Create([0f]);
        AudioObj trackViewAudio = scope.Create([0f]);
        AudioCommandSelection selection = new();

        selection.FocusTrackView();
        Assert.AreSame(trackViewAudio, selection.Resolve(trackViewAudio, [collectionAudio]));
        selection.FocusCollectionView();
        Assert.AreSame(collectionAudio, selection.Resolve(trackViewAudio, [collectionAudio]));
        selection.FocusTrackView();
        Assert.AreSame(trackViewAudio, selection.Resolve(trackViewAudio, [collectionAudio]));
    }

    [TestMethod]
    public void CollectionMultiselectionDoesNotSilentlyChooseTheFirstAudioOrAnOldTrackView()
    {
        using AudioTestScope scope = new();
        AudioObj first = scope.Create([0f]);
        AudioObj second = scope.Create([0f]);
        AudioObj oldTrackView = scope.Create([0f]);
        AudioCommandSelection selection = new();
        selection.FocusCollectionView();

        Assert.IsNull(selection.Resolve(oldTrackView, []));
        Assert.IsNull(selection.Resolve(oldTrackView, [first, second]));
        Assert.AreSame(second, selection.Resolve(oldTrackView, [second]));
    }

    [TestMethod]
    public void ClosingTheFocusedTrackViewFallsBackToTheSingleCollectionSelection()
    {
        using AudioTestScope scope = new();
        AudioObj audio = scope.Create([0f]);
        AudioCommandSelection selection = new();
        selection.FocusTrackView();

        Assert.AreSame(audio, selection.Resolve(null, [audio]));
        Assert.IsNull(selection.Resolve(null, []));
    }

    [TestMethod]
    public void OneAudioShownInMultipleCollectionsIsStillASingleSelectedAudio()
    {
        using AudioTestScope scope = new();
        AudioObj audio = scope.Create([0f]);
        AudioCommandSelection selection = new();
        selection.FocusCollectionView();

        Assert.AreSame(audio, selection.Resolve(null, [audio, audio]));
    }
}
