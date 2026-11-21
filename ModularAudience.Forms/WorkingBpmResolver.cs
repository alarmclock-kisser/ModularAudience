using ModularAudience.Audio;
using ModularAudience.Forms.Modules;
using System.Windows.Forms;

namespace ModularAudience.Forms;

internal static class WorkingBpmResolver
{
    internal const double DefaultBpm = 100.0;

    private static WeakReference<Form>? lastFocusedView;

    internal static void RecordFocus(Form view)
    {
        if (!view.IsDisposed && !view.Disposing)
        {
            lastFocusedView = new WeakReference<Form>(view);
        }
    }

    internal static double GetWorkingBpm(IEnumerable<AudioObj>? preferredAudios = null)
    {
        if (TryGetFormBpm(Form.ActiveForm, out double bpm))
        {
            return bpm;
        }

        if (lastFocusedView?.TryGetTarget(out Form? focusedView) == true
            && !focusedView.IsDisposed
            && !focusedView.Disposing
            && TryGetFormBpm(focusedView, out bpm))
        {
            return bpm;
        }

        if (TryGetAudioBpm(preferredAudios, out bpm)
            || TryGetAudioBpm(WindowMain.ResolveSelectedAudioForCommands(), out bpm))
        {
            return bpm;
        }

        if (WindowMain.LastSelectedTrackView is { IsDisposed: false, Disposing: false } trackView
            && TryGetAudioBpm(trackView.OriginalAudio, out bpm))
        {
            return bpm;
        }

        if (WindowMain.LastSelectedCollectionView is { IsDisposed: false, Disposing: false } collectionView
            && TryGetAudioBpm(collectionView.SelectedAudios, out bpm))
        {
            return bpm;
        }

        MixTrackView? openMix = MixTrackView.OpenMixes.LastOrDefault();
        if (openMix is not null && IsUsableBpm(openMix.Arrangement.Bpm, out bpm))
        {
            return bpm;
        }

        return DefaultBpm;
    }

    internal static bool TryGetAudioBpm(AudioObj? audio, out double bpm)
    {
        if (audio is not null
            && (IsUsableBpm(audio.Bpm, out bpm) || IsUsableBpm(audio.ScannedBpm, out bpm)))
        {
            return true;
        }

        bpm = DefaultBpm;
        return false;
    }

    private static bool TryGetAudioBpm(IEnumerable<AudioObj>? audios, out double bpm)
    {
        if (audios is not null)
        {
            foreach (AudioObj audio in audios)
            {
                if (TryGetAudioBpm(audio, out bpm))
                {
                    return true;
                }
            }
        }

        bpm = DefaultBpm;
        return false;
    }

    private static bool TryGetFormBpm(Form? form, out double bpm)
    {
        for (Form? current = form; current is not null; current = current.Owner)
        {
            if (current is MixTrackView mix && IsUsableBpm(mix.Arrangement.Bpm, out bpm))
            {
                return true;
            }

            if (current is TrackView track && TryGetAudioBpm(track.OriginalAudio, out bpm))
            {
                return true;
            }

            if (current is AudioCollectionView collection
                && TryGetAudioBpm(collection.SelectedAudios, out bpm))
            {
                return true;
            }
        }

        bpm = DefaultBpm;
        return false;
    }

    private static bool IsUsableBpm(double candidate, out double bpm)
    {
        if (double.IsFinite(candidate) && candidate is >= 10.0 and <= 400.0)
        {
            bpm = candidate;
            return true;
        }

        bpm = DefaultBpm;
        return false;
    }
}
