using ModularAudience.Forms.Effects;

namespace ModularAudience.Forms.Modules.Dialogs;

/// <summary>Dedicated Apply Effect workflow. The legacy TimeStretchDialog/batch/configuration UI is deliberately untouched.</summary>
internal sealed class TimeStretchEffectDialog : Form
{
    internal EffectOptionsEditor Editor { get; }
    internal TimeStretchEffectDialog(EffectDialogContext context, EffectPresetStore? store = null, IEffectPreviewPlayer? preview = null)
    {
        this.Editor = EffectDialogHost.Attach(this, context, store, preview);
        this.Name = "TimeStretchEffectDialog";
        this.Text = "Time Stretch / Speed Curve — " + context.TrackName;
    }
}
