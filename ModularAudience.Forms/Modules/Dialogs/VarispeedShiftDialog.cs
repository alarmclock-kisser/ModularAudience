using ModularAudience.Forms.Effects;

namespace ModularAudience.Forms.Modules.Dialogs;

internal sealed class VarispeedShiftDialog : Form
{
    internal EffectOptionsEditor Editor { get; }
    internal VarispeedShiftDialog(EffectDialogContext context, EffectPresetStore? store = null, IEffectPreviewPlayer? preview = null)
    {
        this.Editor = EffectDialogHost.Attach(this, context, store, preview);
        this.Name = "VarispeedShiftDialog";
        this.Text = "Varispeed Shift / Speed Curve — " + context.TrackName;
    }
}
