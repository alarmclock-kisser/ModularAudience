namespace ModularAudience.Forms.Modules.Dialogs;

public sealed partial class VocalSynthDialog
{
    private CheckBox windowsSpeechCheckBox = null!;

    private void InitializeSpeechBackendControl()
    {
        windowsSpeechCheckBox = new CheckBox
        {
            Name = "windowsSpeechCheckBox",
            Text = "Natural speech (Windows)",
            AutoSize = true,
            Checked = true,
            Margin = new Padding(6, 8, 6, 0),
        };
        windowsSpeechCheckBox.CheckedChanged += WindowsSpeechCheckedChanged;
    }
}
