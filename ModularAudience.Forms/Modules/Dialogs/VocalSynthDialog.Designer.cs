namespace ModularAudience.Forms.Modules.Dialogs;

public sealed partial class VocalSynthDialog
{
    private CheckBox windowsSpeechCheckBox = null!;
    private CheckBox unsupportedBeepCheckBox = null!;
    private FlowLayoutPanel profileRow = null!;
    private TextBox profileName = null!;
    private Button saveProfileBtn = null!;
    private Button loadProfileBtn = null!;
    private Button renameProfileBtn = null!;
    private Button deleteProfileBtn = null!;

    private void InitializeSpeechBackendControl()
    {
        windowsSpeechCheckBox = new CheckBox
        {
            Name = "windowsSpeechCheckBox",
            Text = "Natural speech (Windows)",
            AutoSize = true,
            Checked = false,
            Margin = new Padding(6, 8, 6, 0),
        };
        windowsSpeechCheckBox.CheckedChanged += WindowsSpeechCheckedChanged;
    }

    private void InitializeUnsupportedBeepControl()
    {
        unsupportedBeepCheckBox = new CheckBox
        {
            Name = "unsupportedBeepCheckBox",
            Text = "100 Hz beep for unsupported sounds",
            AutoSize = true,
            Checked = false,
            Margin = new Padding(6, 8, 6, 0),
        };
        unsupportedBeepCheckBox.CheckedChanged += UnsupportedBeepCheckedChanged;
    }

    private void InitializeProfileControls()
    {
        profileName = new TextBox { Width = 120, PlaceholderText = "Profile name", AccessibleName = "Profile name" };
        saveProfileBtn = new Button { Text = "Save Profile", AutoSize = true };
        loadProfileBtn = new Button { Text = "Load Profile", AutoSize = true };
        renameProfileBtn = new Button { Text = "Rename Profile", AutoSize = true };
        deleteProfileBtn = new Button { Text = "Delete Profile", AutoSize = true };
        profileRow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = Padding.Empty,
        };
        profileRow.Controls.AddRange([profileName, saveProfileBtn, loadProfileBtn, renameProfileBtn, deleteProfileBtn]);
        saveProfileBtn.Click += (_, _) => SaveProfile();
        loadProfileBtn.Click += (_, _) => LoadProfile();
        renameProfileBtn.Click += (_, _) => RenameProfile();
        deleteProfileBtn.Click += (_, _) => DeleteProfile();
    }
}
