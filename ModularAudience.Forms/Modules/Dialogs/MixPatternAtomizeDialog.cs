using ModularAudience.Audio.Processors_V4;

namespace ModularAudience.Forms.Modules.Dialogs;

internal sealed partial class MixPatternAtomizeDialog : Form
{
    internal MixPatternAtomizeDialog()
    {
        this.InitializeComponent();
    }

    internal LoopAtomizerSettings Settings => LoopAtomizerSettings.Default with
    {
        Sensitivity = this.comboBox_sensitivity.SelectedIndex switch
        {
            0 => AtomizeSensitivity.Conservative,
            2 => AtomizeSensitivity.Aggressive,
            _ => AtomizeSensitivity.Balanced
        },
        MinSliceMs = (int)this.numeric_minSlice.Value,
        TailPaddingMs = (int)this.numeric_tail.Value,
        MinimumRmsLevel = (float)this.numeric_minRms.Value,
        MinimumAtomicDurationMs = (int)this.numeric_minDuration.Value,
        AllowSingleAtomFallback = this.checkBox_singleFallback.Checked,
        EnableDeduplication = false
    };
}
