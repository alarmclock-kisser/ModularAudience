using ModularAudience.Audio;

namespace ModularAudience.Forms;

/// <summary>Remembers the last interaction source, without treating an old TrackView as permanent focus.</summary>
internal sealed class AudioCommandSelection
{
    private bool preferCollectionSelection;

    internal void FocusCollectionView() => this.preferCollectionSelection = true;
    internal void FocusTrackView() => this.preferCollectionSelection = false;

    internal AudioObj? Resolve(AudioObj? trackViewAudio, IEnumerable<AudioObj> selectedCollectionAudios)
    {
        if (!this.preferCollectionSelection && trackViewAudio != null)
        {
            return trackViewAudio;
        }

        AudioObj[] selected = selectedCollectionAudios.DistinctBy(audio => audio.Id).Take(2).ToArray();
        return selected.Length == 1 ? selected[0] : null;
    }
}
