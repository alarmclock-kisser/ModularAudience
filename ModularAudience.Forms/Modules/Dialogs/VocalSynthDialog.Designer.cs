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
    private Label midiTrackNumberLabel = null!;
    private NumericUpDown midiSemitoneOffset = null!;
    private Label midiSemitoneOffsetLabel = null!;
    private Button editMidiButton = null!;
    private CheckBox showSyllablesCheckBox = null!;
    private FlowLayoutPanel midiOptionsRow = null!;
    private FlowLayoutPanel vocalActionsRow = null!;
    private Panel textContentPanel = null!;

    private void InitializeMidiControls(
        Button prehear,
        TextHistoryRichTextBox textEditor,
        Button create,
        Button designProfileBtn,
        Button emphasisBtn)
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
            MaximumSize = new Size(180, 0),
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
        midiTrackNumberLabel = new Label
        {
            Name = "midiTrackNumberLabel",
            Text = "Track:",
            AutoSize = true,
            Margin = new Padding(4, 8, 0, 0),
        };
        midiSemitoneOffsetLabel = new Label
        {
            Name = "midiSemitoneOffsetLabel",
            Text = "Semitone offset:",
            AutoSize = true,
            Margin = new Padding(4, 8, 0, 0),
        };
        midiSemitoneOffset = new NumericUpDown
        {
            Name = "midiSemitoneOffset",
            Minimum = -16m,
            Maximum = 16m,
            Increment = 0.25m,
            DecimalPlaces = 2,
            Value = 0m,
            Width = 76,
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
            editMidiButton,
            midiTrackNumberLabel,
            midiTrackNumber,
            midiSemitoneOffsetLabel,
            midiSemitoneOffset,
            midiFileLabel,
        ]);
        midiButton.Click += (_, _) => OpenMidiFile();
        midiTrackNumber.ValueChanged += (_, _) => MidiTrackNumberChanged();
        midiSemitoneOffset.ValueChanged += MidiSemitoneOffsetChanged;
        editMidiButton.Click += (_, _) => EditLoadedMidi();
        showSyllablesCheckBox.CheckedChanged += (_, _) => ToggleSyllablePreview();

        vocalActionsRow = new FlowLayoutPanel
        {
            Name = "vocalActionsRow",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = Padding.Empty,
        };
        vocalActionsRow.Controls.AddRange([showSyllablesCheckBox, emphasisBtn, designProfileBtn, create]);

        textContentPanel = new Panel { Dock = DockStyle.Fill };
        textContentPanel.Controls.Add(textEditor);
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
