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
    private Button midiButton = null!;
    private Label midiFileLabel = null!;
    private NumericUpDown midiTrackNumber = null!;
    private Button editMidiButton = null!;
    private CheckBox showSyllablesCheckBox = null!;
    private FlowLayoutPanel midiOptionsRow = null!;
    private Panel textContentPanel = null!;
    private RichTextBox syllablePreview = null!;

    private void InitializeMidiControls(Button prehear, TextBox textEditor)
    {
        midiButton = new Button
        {
            Name = "midiButton",
            Text = "MIDI",
            AutoSize = true,
        };
        midiFileLabel = new Label
        {
            Name = "midiFileLabel",
            Text = "No MIDI loaded",
            AutoSize = true,
            AutoEllipsis = true,
            MaximumSize = new Size(240, 0),
            Margin = new Padding(4, 8, 4, 0),
            AccessibleName = "MIDI file and selected track",
        };
        midiTrackNumber = new NumericUpDown
        {
            Name = "midiTrackNumber",
            Minimum = 1,
            Maximum = 1,
            Value = 1,
            Width = 58,
            Enabled = false,
        };
        editMidiButton = new Button
        {
            Name = "editMidiButton",
            Text = "Edit",
            AutoSize = true,
            Enabled = false,
        };
        showSyllablesCheckBox = new CheckBox
        {
            Name = "showSyllablesCheckBox",
            Text = "Show syllables",
            AutoSize = true,
            Enabled = false,
            Margin = new Padding(6, 8, 4, 0),
        };

        midiOptionsRow = new FlowLayoutPanel
        {
            Name = "midiOptionsRow",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = Padding.Empty,
        };
        midiOptionsRow.Controls.AddRange(
        [
            prehear,
            midiButton,
            midiFileLabel,
            new Label { Text = "Track:", AutoSize = true, Margin = new Padding(4, 8, 0, 0) },
            midiTrackNumber,
            editMidiButton,
            showSyllablesCheckBox,
        ]);
        midiButton.Click += (_, _) => OpenMidiFile();
        midiTrackNumber.ValueChanged += (_, _) => MidiTrackNumberChanged();
        editMidiButton.Click += (_, _) => EditLoadedMidi();
        showSyllablesCheckBox.CheckedChanged += (_, _) => ToggleSyllablePreview();

        textContentPanel = new Panel { Dock = DockStyle.Fill };
        syllablePreview = new RichTextBox
        {
            Name = "syllablePreview",
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = SystemColors.Window,
            ForeColor = Color.Black,
            ScrollBars = RichTextBoxScrollBars.Both,
            Font = textEditor.Font,
            Visible = false,
        };
        textContentPanel.Controls.Add(textEditor);
        textContentPanel.Controls.Add(syllablePreview);
    }

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
