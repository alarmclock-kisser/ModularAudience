using ModularAudience.Audio.Arrangement;

namespace ModularAudience.Forms.Modules;

public sealed partial class MixTrackView
{
    internal void LoadDecompiledProject(MixArrangement project)
    {
        ArgumentNullException.ThrowIfNull(project);
        project.Validate();
        StopPlayback();
        this.operation?.Cancel();
        this.tempoDebounce.Stop();
        this.projectSaveTimer.Stop();
        this.ClearRangeSelection();
        this.wavePeaks.Clear();
        this.Arrangement.Dispose();
        this.Arrangement = project;
        this.undo.Clear();
        this.redo.Clear();
        this.projectPath = null;
        this.autoSave.Enabled = false;
        this.dirty = true;
        this.committed = MixAudioHistory.Capture(this.Arrangement);
        this.committedAudioKey = MixAudioHistory.Key(this.Arrangement);
        this.committedProject = this.Arrangement.CaptureProject();
        this.Restore(this.Arrangement.Capture());
    }
}
