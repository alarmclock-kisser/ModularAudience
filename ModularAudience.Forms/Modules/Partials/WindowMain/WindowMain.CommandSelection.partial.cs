using ModularAudience.Audio;
using ModularAudience.Forms.Helpers;
using ModularAudience.Forms.Modules;

namespace ModularAudience.Forms;

public partial class WindowMain
{
    internal static AudioCommandSelection CommandSelection { get; } = new();
    private AudioScanController? scanControls;

    private void InitializeScanControls()
    {
        this.scanControls = new AudioScanController(this,
            this.button_scanBpm, this.textBox_scanBpmResult,
            this.button_scanTiming, this.textBox_scanTimingResult,
            this.button_scanKey, this.textBox_scanKeyResult,
            log: LogManager.Log);
        this.Disposed += (_, _) => this.scanControls?.Dispose();
    }

    internal static void FocusCollectionCommands(AudioCollectionView collection)
    {
        LastSelectedCollectionView = collection;
        CommandSelection.FocusCollectionView();
        RefreshCommandSelection();
    }

    internal static void FocusTrackViewCommands(TrackView trackView)
    {
        CommandSelection.FocusTrackView();
        LastSelectedTrackView = trackView;
        // The same TrackView may have been focused before a collection was selected.
        // Its object reference has not changed, but the command target has.
        RefreshCommandSelection();
    }

    internal static void RefreshCommandSelection()
    {
        if (Instance is { IsDisposed: false, Disposing: false, scanControls: not null } main)
            WindowMainStaticHelpers.InvokeIfRequired(main, main.UpdateTrackDependentUI);
    }

    internal static AudioObj? ResolveSelectedAudioForCommands() => CommandSelection.Resolve(
        LastSelectedTrackView is { IsDisposed: false, Disposing: false } track ? track.OriginalAudio : null,
        SelectedTracks);
}
