using ModularAudience.Audio;
using ModularAudience.Audio.Midi;
using ModularAudience.Audio.Vocal;
using ModularAudience.Forms.Modules;
using NAudio.Wave;
using System.Globalization;

namespace ModularAudience.Forms.Modules.Dialogs;

public sealed partial class VocalSynthDialog : Form
{
    private sealed record SessionSnapshot(
        VocalSynthSettings Settings, bool ShowSyllables, string? VoiceSelection);

    private static readonly object SessionSnapshotLock = new();
    private static SessionSnapshot? lastSessionSnapshot;
    private readonly VocalEmphasisSyntaxStore emphasisStore;
    private VocalCustomSamplesDialog? customSamplesDialog;
    private VocalEmphasisSyntax emphasisSyntax;
    private readonly VocalVoiceProfileStore profileStore;
    private readonly ComboBox languageCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly ComboBox voiceCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly NumericUpDown speed = new() { Minimum = 0.5m, Maximum = 2.0m, Increment = 0.05m, DecimalPlaces = 2, Value = 1.0m, Width = 80 };
    private readonly TextHistoryRichTextBox textEditor = new()
    {
        Multiline = true,
        Dock = DockStyle.Fill,
        ScrollBars = RichTextBoxScrollBars.Both,
        BorderStyle = BorderStyle.FixedSingle,
        WordWrap = false,
        DetectUrls = false,
        Font = new Font("Consolas", 11f),
    };
    private readonly Label status = new() { Dock = DockStyle.Fill, AutoSize = true, Text = "Ready." };
    private readonly Button prehear = new() { Text = "Prehear", AutoSize = true };
    private readonly Button create = new() { Text = "Create", AutoSize = true };
    private readonly Button designProfileBtn = new() { Text = "Design Profile...", AutoSize = true };
    private readonly Button emphasisBtn = new() { Text = "Emphasis Syntax...", AutoSize = true };

    private VocalSynthSettings settings;
    private AudioObj? audio;
    private AudioCollectionView? collection;
    private CancellationTokenSource? rendering;
    private int revision, ready = -1;
    private bool disposedFlag, creating, syncing, formattingSyllables, fillingSyllables;
    private readonly System.Windows.Forms.Timer debounce = new() { Interval = 300 };
    private WaveOut? output;
    private VocalPreview? provider;
    private readonly System.Windows.Forms.Timer playbackTimer = new() { Interval = 40 };

    private sealed class VocalPreview : ISampleProvider
    {
        private readonly float[] data;
        private int pos;
        public WaveFormat WaveFormat { get; }
        public bool Loop { get; set; }
        public VocalPreview(float[] data, int sampleRate, int channels)
        {
            this.data = data;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
        }

        public int Read(Span<float> buffer)
        {
            if (pos >= data.Length)
            {
                if (Loop) { pos = 0; Finished = false; }
                else { Finished = true; return 0; }
            }
            int n = Math.Min(buffer.Length, data.Length - pos);
            data.AsSpan(pos, n).CopyTo(buffer);
            pos += n;
            if (pos >= data.Length && !Loop) Finished = true;
            return n;
        }
        public bool Finished { get; private set; }
    }

    public VocalSynthDialog(VocalVoiceProfileStore? store = null, VocalEmphasisSyntaxStore? estore = null)
    {
        profileStore = store ?? VocalVoiceProfileStore.Default();
        emphasisStore = estore ?? VocalEmphasisSyntaxStore.Default();
        SessionSnapshot? sessionSnapshot = ReadSessionSnapshot();
        settings = sessionSnapshot?.Settings
            ?? VocalSynthSettings.FromPreset(VocalVoicePreset.BuiltIn[2], VocalLanguage.English, "");
        settings.CustomSampleMappings = VocalCustomSampleStore.GetMappings();
        emphasisSyntax = sessionSnapshot is null ? emphasisStore.Load() : settings.EmphasisSyntax;
        settings.EmphasisSyntax = emphasisSyntax;
        InitializeSpeechBackendControl();
        InitializeUnsupportedBeepControl();
        InitializeProfileControls();
        InitializeMidiControls(prehear, textEditor, create, designProfileBtn, emphasisBtn);
        Text = "VocalSynth"; Name = "VocalSynthDialog";
        ClientSize = new Size(900, 650); MinimumSize = new Size(700, 500); StartPosition = FormStartPosition.CenterScreen;

        syncing = true;
        try
        {
            VocalLanguage[] languages = Enum.GetValues<VocalLanguage>();
            foreach (VocalLanguage language in languages) languageCombo.Items.Add(language.DisplayName());
            int languageIndex = Array.IndexOf(languages, settings.Language);
            if (languageIndex < 0)
            {
                settings.Language = VocalLanguage.English;
                languageIndex = Array.IndexOf(languages, settings.Language);
            }
            languageCombo.SelectedIndex = languageIndex;
            textEditor.Text = settings.Text;
            speed.Value = Math.Clamp((decimal)settings.Speed, speed.Minimum, speed.Maximum);
            settings.Speed = (double)speed.Value;
            windowsSpeechCheckBox.Checked = settings.UseWindowsSpeech;
            unsupportedBeepCheckBox.Checked = settings.BeepForUnsupportedSounds;
            midiSemitoneOffset.Value = Math.Clamp(
                (decimal)settings.SingingMidiSemitoneOffset,
                midiSemitoneOffset.Minimum,
                midiSemitoneOffset.Maximum);
            settings.SingingMidiSemitoneOffset = (double)midiSemitoneOffset.Value;
            showSyllablesCheckBox.Checked = sessionSnapshot?.ShowSyllables ?? false;
            RestoreMidiTrackNumber();
        }
        finally { syncing = false; }

        UpdateSpeechBackend();
        languageCombo.SelectedIndexChanged += (_, _) =>
        {
            if (syncing) return;
            settings.Language = (VocalLanguage)languageCombo.SelectedIndex;
            UpdateSpeechBackend();
            if (showSyllablesCheckBox.Checked) RefreshSyllablePreview();
            QueueRender();
        };

        // voice combo: built-ins + profiles
        string preferredVoice = sessionSnapshot?.VoiceSelection ?? "Built-in: " + settings.VoiceName;
        RefreshVoiceCombo(preferredVoice);
        bool restoredVoice = string.Equals(
            voiceCombo.SelectedItem?.ToString(), preferredVoice, StringComparison.Ordinal);
        voiceCombo.SelectedIndexChanged += (_, _) => ApplyVoiceSelection();
        if (!restoredVoice) ApplyVoiceSelection();

        speed.ValueChanged += (_, _) => { if (!syncing) { settings.Speed = (double)speed.Value; QueueRender(); } };

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(6) };
        void Field(string label, Control c) { top.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(4, 8, 2, 0) }); top.Controls.Add(c); }
        Field("Language", languageCombo);
        Field("Voice", voiceCombo);
        Field("Speed", speed);
        top.Controls.Add(windowsSpeechCheckBox);
        top.Controls.Add(unsupportedBeepCheckBox);
        top.Controls.Add(midiOptionsRow);
        top.SetFlowBreak(midiOptionsRow, true);
        top.Controls.Add(profileRow);
        top.SetFlowBreak(profileRow, true);
        top.Controls.Add(vocalActionsRow);

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(6) };
        bottom.Controls.Add(status);

        Controls.Add(textContentPanel);
        Controls.Add(top);
        Controls.Add(bottom);

        // events
        textEditor.TextChanged += (_, _) =>
        {
            if (syncing || formattingSyllables) return;
            settings.Text = textEditor.Text;
            if (!fillingSyllables) settings.IgnoreTextPausesForMidiSinging = false;
            UpdateButtonState();
            if (showSyllablesCheckBox.Checked) RefreshSyllablePreview();
            if (string.IsNullOrWhiteSpace(textEditor.Text))
            {
                revision++;
                rendering?.Cancel();
                debounce.Stop();
                audio?.Dispose();
                audio = null;
                ready = -1;
                status.Text = "Enter text to synthesize.";
                return;
            }
            QueueRender();
        };
        prehear.Click += async (_, _) => await ToggleHearAsync();
        create.Click += async (_, _) => await CreateAsync();
        emphasisBtn.Click += (_, _) => OpenEmphasisDialog();
        designProfileBtn.Click += (_, _) => OpenDesignerDialog();

        debounce.Tick += async (_, _) => { debounce.Stop(); await RenderAsync(); };
        playbackTimer.Tick += (_, _) => { if (provider?.Finished == true) StopHear(); };
        FormClosing += (_, _) => { debounce.Stop(); rendering?.Cancel(); StopHear(); };
        FormClosed += (_, _) => SaveSessionSnapshot();

        settings.Text = textEditor.Text;
        UpdateButtonState();
        if (!string.IsNullOrWhiteSpace(textEditor.Text)) QueueRender();
    }

    private static SessionSnapshot? ReadSessionSnapshot()
    {
        lock (SessionSnapshotLock)
        {
            return lastSessionSnapshot is { } snapshot
                ? snapshot with { Settings = snapshot.Settings.Clone() }
                : null;
        }
    }

    private void SaveSessionSnapshot()
    {
        settings.Language = languageCombo.SelectedIndex >= 0
            ? (VocalLanguage)languageCombo.SelectedIndex
            : settings.Language;
        settings.Text = textEditor.Text;
        settings.Speed = (double)speed.Value;
        settings.SingingMidiSemitoneOffset = (double)midiSemitoneOffset.Value;
        settings.EmphasisSyntax = emphasisSyntax;

        lock (SessionSnapshotLock)
        {
            VocalSynthSettings snapshotSettings = settings.Clone();
            snapshotSettings.CustomSampleMappings = [];
            lastSessionSnapshot = new SessionSnapshot(
                snapshotSettings,
                showSyllablesCheckBox.Checked,
                voiceCombo.SelectedItem?.ToString());
        }
    }

    private void RestoreMidiTrackNumber()
    {
        MidiFileData? midi = settings.SingingMidiFile;
        if (midi is null || midi.Tracks.Count == 0)
        {
            if (midi is not null)
            {
                settings.SingingMidiFile = null;
                settings.SingingMidiTrackIndex = null;
            }
            midiTrackNumber.Value = 1;
            UpdateMidiControls();
            return;
        }

        int selectedPage = -1;
        if (settings.SingingMidiTrackIndex is int selectedTrackIndex)
        {
            for (int page = 0; page < midi.Tracks.Count; page++)
            {
                if (midi.Tracks[page].Index == selectedTrackIndex)
                {
                    selectedPage = page;
                    break;
                }
            }
        }
        if (selectedPage < 0)
        {
            for (int page = 0; page < midi.Tracks.Count; page++)
            {
                if (midi.Tracks[page].Notes.Count > 0)
                {
                    selectedPage = page;
                    break;
                }
            }
        }
        if (selectedPage < 0) selectedPage = 0;

        settings.SingingMidiTrackIndex = midi.Tracks[selectedPage].Index;
        midiTrackNumber.Maximum = Math.Max(1, midi.Tracks.Count);
        midiTrackNumber.Value = selectedPage + 1;
        UpdateMidiControls();
    }

    private void UpdateButtonState()
    {
        bool hasText = !string.IsNullOrWhiteSpace(textEditor.Text);
        prehear.Enabled = hasText || output is not null;
        create.Enabled = hasText;
        UpdateMidiControls();
    }

    private void UpdateMidiControls()
    {
        MidiFileData? midi = settings.SingingMidiFile;
        int trackCount = midi?.Tracks.Count ?? 0;
        decimal maximum = Math.Max(1, trackCount);
        if (midiTrackNumber.Value > maximum) midiTrackNumber.Value = maximum;
        midiTrackNumber.Maximum = maximum;
        midiTrackNumber.Enabled = trackCount > 0;
        midiSemitoneOffset.Enabled = trackCount > 0;
        editMidiButton.Enabled = trackCount > 0;
        showSyllablesCheckBox.Enabled = true;
        MidiTrackData? selectedTrack = midi?.Tracks.FirstOrDefault(
            track => track.Index == settings.SingingMidiTrackIndex);
        fillSyllablesButton.Enabled = settings.Singing && selectedTrack is not null
            && selectedTrack.Notes.Count > 0;
        UpdateMidiFileLabel();
    }

    private void UpdateMidiFileLabel()
    {
        MidiFileData? midi = settings.SingingMidiFile;
        if (midi is null || midi.Tracks.Count == 0)
        {
            midiFileLabel.Text = "No MIDI loaded";
            return;
        }

        int pageNumber = Math.Clamp((int)midiTrackNumber.Value, 1, midi.Tracks.Count);
        string fileName = Path.GetFileName(midi.FilePath);
        string trackName = midi.Tracks[pageNumber - 1].Name;
        if (string.IsNullOrWhiteSpace(trackName)) trackName = $"Track {pageNumber}";
        midiFileLabel.Text = $"{fileName} (#{pageNumber} {trackName})";
    }

    private void OpenMidiFile()
    {
        string musicFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        using var dialog = new OpenFileDialog
        {
            Title = "Load MIDI for singing",
            Filter = "MIDI files (*.mid;*.midi)|*.mid;*.midi",
            InitialDirectory = musicFolder,
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            MidiFileData midi = MidiFileData.Load(dialog.FileName);
            if (midi.Tracks.Count == 0 || !midi.Tracks.Any(track => track.Notes.Count > 0))
                throw new InvalidDataException("The selected MIDI file contains no notes.");

            int pageIndex = 0;
            while (midi.Tracks[pageIndex].Notes.Count == 0) pageIndex++;
            syncing = true;
            try
            {
                midiTrackNumber.Maximum = Math.Max(1, midi.Tracks.Count);
                midiTrackNumber.Value = pageIndex + 1;
                SelectSingingVoiceForMidi(midi, pageIndex);
            }
            finally { syncing = false; }

            UpdateMidiControls();
            UpdateButtonState();
            status.Text = $"Loaded {Path.GetFileName(midi.FilePath)} for MIDI singing.";
            QueueRender();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or FormatException)
        {
            status.Text = "Could not load MIDI: " + error.Message;
            MessageBox.Show(this, error.Message, "MIDI load failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SelectSingingVoiceForMidi(MidiFileData midi, int pageIndex)
    {
        VocalVoicePreset preset = VocalVoicePreset.BuiltIn.First(voice => voice.Singing);
        var singingSettings = VocalSynthSettings.FromPreset(preset, settings.Language, textEditor.Text);
        singingSettings.UseWindowsSpeech = settings.UseWindowsSpeech;
        singingSettings.BeepForUnsupportedSounds = unsupportedBeepCheckBox.Checked;
        singingSettings.Speed = settings.Speed;
        singingSettings.EmphasisSyntax = emphasisSyntax;
        singingSettings.SingingMidiFile = midi;
        singingSettings.SingingMidiTrackIndex = midi.Tracks[pageIndex].Index;
        singingSettings.IgnoreTextPausesForMidiSinging = settings.IgnoreTextPausesForMidiSinging;
        singingSettings.CustomSampleMappings = VocalCustomSampleStore.GetMappings();
        settings = singingSettings;
        midiSemitoneOffset.Value = (decimal)settings.SingingMidiSemitoneOffset;

        int voiceIndex = voiceCombo.Items.IndexOf("Built-in: " + preset.Name);
        if (voiceIndex >= 0) voiceCombo.SelectedIndex = voiceIndex;
        speed.Value = (decimal)settings.Speed;
    }

    private void MidiTrackNumberChanged()
    {
        if (syncing || settings.SingingMidiFile is not MidiFileData midi || midi.Tracks.Count == 0) return;
        int pageIndex = Math.Clamp((int)midiTrackNumber.Value - 1, 0, midi.Tracks.Count - 1);
        settings.SingingMidiTrackIndex = midi.Tracks[pageIndex].Index;
        UpdateMidiControls();
        QueueRender();
    }

    private void MidiSemitoneOffsetChanged(object? sender, EventArgs e)
    {
        if (syncing) return;
        settings.SingingMidiSemitoneOffset = (double)midiSemitoneOffset.Value;
        QueueRender();
    }

    private void EditLoadedMidi()
    {
        MidiFileData? midi = settings.SingingMidiFile;
        if (midi is null || midi.Tracks.Count == 0) return;

        int pageNumber = Math.Clamp((int)midiTrackNumber.Value, 1, midi.Tracks.Count);
        StopHear();
        using var midiWindow = new MidiWindow(midi.FilePath, midi);
        midiWindow.SelectPage(pageNumber);
        midiWindow.ShowDialog(this);

        MidiFileData editedMidi = midiWindow.CurrentMidiFile;
        int selectedPage = Math.Clamp(pageNumber, 1, Math.Max(1, editedMidi.Tracks.Count));
        if (editedMidi.Tracks.Count > 0)
        {
            syncing = true;
            try
            {
                settings.SingingMidiFile = editedMidi;
                settings.SingingMidiTrackIndex = editedMidi.Tracks[selectedPage - 1].Index;
                midiTrackNumber.Maximum = editedMidi.Tracks.Count;
                midiTrackNumber.Value = selectedPage;
            }
            finally { syncing = false; }
        }
        UpdateMidiControls();
        QueueRender();
    }

    private void ToggleSyllablePreview()
    {
        RefreshSyllablePreview();
    }

    private void FillSyllables()
    {
        try
        {
            VocalMidiSyllableFillResult result = VocalSynth.FillMissingMidiSyllables(settings, textEditor.Text);
            fillingSyllables = true;
            try
            {
                if (result.AppendedText.Length > 0)
                {
                    textEditor.SelectionStart = textEditor.TextLength;
                    textEditor.SelectionLength = 0;
                    textEditor.SelectedText = result.AppendedText;
                }
            }
            finally { fillingSyllables = false; }

            settings.Text = textEditor.Text;
            settings.IgnoreTextPausesForMidiSinging = result.ExistingSyllables <= result.MidiSyllables;
            if (showSyllablesCheckBox.Checked) RefreshSyllablePreview();
            if (result.AddedSyllables == 0) QueueRender();
            status.Text = result.AddedSyllables > 0
                ? $"Added {result.AddedSyllables} syllables to match the selected MIDI track."
                : result.ExistingSyllables == result.MidiSyllables
                    ? "The text already matches the selected MIDI track."
                    : "The text has more syllables than the selected MIDI track; no text was changed.";
        }
        catch (Exception error) when (
            error is ArgumentException or InvalidOperationException or InvalidDataException)
        {
            status.Text = error.Message;
        }
    }

    private void RefreshSyllablePreview()
    {
        if (formattingSyllables) return;
        formattingSyllables = true;
        int selectionStart = textEditor.SelectionStart;
        int selectionLength = textEditor.SelectionLength;
        textEditor.SuspendLayout();
        try
        {
            string text = textEditor.Text;
            if (text.Length > 0)
            {
                using var underlinedFont = new Font(
                    textEditor.Font, textEditor.Font.Style | FontStyle.Underline);
                textEditor.Select(0, text.Length);
                textEditor.SelectionFont = textEditor.Font;

                if (showSyllablesCheckBox.Checked)
                {
                    VocalSynthSettings previewSettings = settings.Clone();
                    previewSettings.Text = text;
                    previewSettings.EmphasisSyntax = emphasisSyntax;
                    previewSettings.CustomSampleMappings = VocalCustomSampleStore.GetMappings();
                    IReadOnlyList<VocalSyllableTextRange> ranges =
                        VocalSynth.GetSyllableTextRanges(previewSettings);
                    foreach (VocalSyllableTextRange range in ranges)
                    {
                        if ((range.Index & 1) == 0 || range.Start < 0 || range.Length <= 0
                            || range.Start > text.Length - range.Length)
                            continue;
                        textEditor.Select(range.Start, range.Length);
                        textEditor.SelectionFont = underlinedFont;
                    }
                }
            }
        }
        finally
        {
            try
            {
                int safeStart = Math.Clamp(selectionStart, 0, textEditor.TextLength);
                int safeLength = Math.Clamp(selectionLength, 0, textEditor.TextLength - safeStart);
                textEditor.Select(safeStart, safeLength);
            }
            finally
            {
                textEditor.ResumeLayout();
                formattingSyllables = false;
            }
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (textEditor.ProcessHistoryCommand(keyData)) return true;
        if (keyData == (Keys.Shift | Keys.Enter) || keyData == Keys.Enter && ActiveControl != textEditor)
        {
            prehear.PerformClick();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void UpdateSpeechBackend()
    {
        bool supported = settings.Language is VocalLanguage.English or VocalLanguage.German;
        windowsSpeechCheckBox.Enabled = supported;
        settings.UseWindowsSpeech = supported && windowsSpeechCheckBox.Checked;
        unsupportedBeepCheckBox.Enabled = !settings.UseWindowsSpeech;
    }

    private void WindowsSpeechCheckedChanged(object? sender, EventArgs e)
    {
        if (syncing) return;
        UpdateSpeechBackend();
        QueueRender();
    }

    private void UnsupportedBeepCheckedChanged(object? sender, EventArgs e)
    {
        if (syncing) return;
        settings.BeepForUnsupportedSounds = unsupportedBeepCheckBox.Checked;
        QueueRender();
    }

    private void RefreshVoiceCombo(string? preferredSelection = null)
    {
        string selected = preferredSelection
            ?? voiceCombo.SelectedItem?.ToString()
            ?? "Built-in: " + settings.VoiceName;
        voiceCombo.Items.Clear();
        foreach (var p in VocalVoicePreset.BuiltIn)
            voiceCombo.Items.Add("Built-in: " + p.Name);
        foreach (var p in profileStore.LoadAll())
            voiceCombo.Items.Add("Profile: " + p.Name);
        if (voiceCombo.Items.Count > 0)
        {
            int index = voiceCombo.Items.IndexOf(selected);
            voiceCombo.SelectedIndex = index >= 0 ? index : 0;
        }
    }

    private void ApplyVoiceSelection()
    {
        if (syncing || voiceCombo.SelectedIndex < 0) return;
        var item = voiceCombo.SelectedItem?.ToString() ?? "";
        syncing = true;
        try
        {
            if (item.StartsWith("Built-in: "))
            {
                var name = item.Substring("Built-in: ".Length);
                var preset = VocalVoicePreset.BuiltIn.First(p => p.Name == name);
                var s = VocalSynthSettings.FromPreset(preset, settings.Language, settings.Text);
                s.UseWindowsSpeech = settings.UseWindowsSpeech;
                s.BeepForUnsupportedSounds = unsupportedBeepCheckBox.Checked;
                s.Speed = settings.Speed;
                s.EmphasisSyntax = settings.EmphasisSyntax;
                PreserveMidiSingingSettings(s);
                settings = s;
            }
            else if (item.StartsWith("Profile: "))
            {
                var name = item.Substring("Profile: ".Length);
                var profile = profileStore.LoadAll().FirstOrDefault(p => p.Name == name);
                if (profile != null)
                {
                    var s = VocalSynthSettings.FromProfile(profile, settings.Language, settings.Text);
                    s.UseWindowsSpeech = settings.UseWindowsSpeech;
                    s.BeepForUnsupportedSounds = unsupportedBeepCheckBox.Checked;
                    s.Speed = settings.Speed;
                    s.EmphasisSyntax = settings.EmphasisSyntax;
                    PreserveMidiSingingSettings(s);
                    settings = s;
                }
            }
            speed.Value = (decimal)settings.Speed;
        }
        finally { syncing = false; }
        QueueRender();
    }

    private void PreserveMidiSingingSettings(VocalSynthSettings replacement)
    {
        replacement.CustomSampleMappings = VocalCustomSampleStore.GetMappings();
        if (settings.SingingMidiFile is null) return;
        replacement.Singing = true;
        replacement.SingingMidiFile = settings.SingingMidiFile;
        replacement.SingingMidiTrackIndex = settings.SingingMidiTrackIndex;
        replacement.SingingMidiSemitoneOffset = settings.SingingMidiSemitoneOffset;
        replacement.IgnoreTextPausesForMidiSinging = settings.IgnoreTextPausesForMidiSinging;
    }

    public void SetCollection(AudioCollectionView? collection) => this.collection = collection;

    private void QueueRender()
    {
        if (disposedFlag) return;
        revision++;
        rendering?.Cancel();
        debounce.Stop();
        if (string.IsNullOrWhiteSpace(textEditor.Text))
        {
            ready = -1;
            status.Text = "Enter text to synthesize.";
            return;
        }
        debounce.Start();
        status.Text = "Updating…";
    }

    private async Task RenderAsync()
    {
        if (disposedFlag) return;
        var cts = new CancellationTokenSource();
        rendering?.Cancel();
        rendering = cts;
        int version = revision;
        try
        {
            settings.Text = textEditor.Text;
            settings.Speed = (double)speed.Value;
            settings.SingingMidiSemitoneOffset = (double)midiSemitoneOffset.Value;
            settings.CustomSampleMappings = VocalCustomSampleStore.GetMappings();
            var snapshot = settings.Clone();
            var (next, midiWarning) = await Task.Run(() =>
            {
                string? warning = VocalSynth.GetMidiSingingWarning(snapshot, cts.Token);
                return (VocalSynth.Render(snapshot, cts.Token), warning);
            }, cts.Token);
            if (disposedFlag || version != revision || cts.IsCancellationRequested) { next.Dispose(); return; }
            var old = audio; audio = next; ready = version;
            old?.Dispose();
            status.Text = $"Rendered {next.Duration.TotalSeconds:0.###} s · {next.SampleRate} Hz"
                + (midiWarning is null ? string.Empty : " · " + midiWarning);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!disposedFlag && version == revision) status.Text = error.Message; }
        finally
        {
            if (ReferenceEquals(rendering, cts)) rendering = null;
            cts.Dispose();
        }
    }

    private async Task<bool> EnsureRenderedAsync()
    {
        if (ready != revision)
        {
            debounce.Stop();
            await RenderAsync();
        }
        return !disposedFlag && ready == revision && audio is not null;
    }

    private async Task ToggleHearAsync()
    {
        if (output != null) { StopHear(); return; }
        try
        {
            if (!await EnsureRenderedAsync() || audio == null) return;
            provider = new VocalPreview(audio.Data, audio.SampleRate, audio.Channels);
            output = new WaveOut { BufferMilliseconds = 60 };
            output.Init(provider);
            output.Play();
            prehear.Text = "Stop";
            playbackTimer.Start();
        }
        catch (Exception error) { StopHear(); status.Text = error.Message; }
    }

    private void StopHear()
    {
        playbackTimer.Stop();
        output?.Stop();
        output?.Dispose();
        output = null;
        provider = null;
        prehear.Text = "Prehear";
        prehear.Enabled = !string.IsNullOrWhiteSpace(textEditor.Text);
    }

    private async Task CreateAsync()
    {
        if (creating) return;
        creating = true;
        create.Enabled = false;
        try
        {
            if (!await EnsureRenderedAsync() || audio == null) return;
            if (collection == null || collection.IsDisposed) collection = new AudioCollectionView([], "VocalSynth");
            var created = audio.Clone();
            string basis = audio.Name, name = basis; int index = 2;
            while (collection.AudioC.Audios.Any(a => a.Name == name)) name = basis + " " + index++;
            created.Rename(name);
            collection.AudioC.Audios.Add(created);
            collection.Show();
            status.Text = "Created: " + name;
        }
        catch (Exception error) { if (!disposedFlag) status.Text = error.Message; }
        finally { creating = false; if (!disposedFlag) UpdateButtonState(); }
    }

    private void OpenEmphasisDialog()
    {
        using var dlg = new VocalEmphasisSyntaxDialog(emphasisSyntax);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            emphasisSyntax = dlg.Syntax;
            emphasisStore.Save(emphasisSyntax);
            settings.EmphasisSyntax = emphasisSyntax;
            if (showSyllablesCheckBox.Checked) RefreshSyllablePreview();
            QueueRender();
        }
    }

    private void OpenCustomSamplesDialog()
    {
        VocalCustomSamplesDialog? dialog = customSamplesDialog;
        if (dialog is null || dialog.IsDisposed)
        {
            dialog = new VocalCustomSamplesDialog();
            dialog.MappingsChanged += (_, _) =>
            {
                settings.CustomSampleMappings = VocalCustomSampleStore.GetMappings();
                if (showSyllablesCheckBox.Checked) RefreshSyllablePreview();
                QueueRender();
            };
            customSamplesDialog = dialog;
        }

        if (!dialog.Visible)
            dialog.Show(this);
        else
        {
            if (dialog.WindowState == FormWindowState.Minimized)
                dialog.WindowState = FormWindowState.Normal;
            dialog.Activate();
        }
    }

    private void OpenDesignerDialog()
    {
        using var dlg = new VocalVoiceProfileDesignerDialog(null, profileStore);
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.ResultProfile is not null)
        {
            RefreshVoiceCombo();
            profileName.Text = dlg.ResultProfile.Name;
            status.Text = "Profile designed: " + dlg.ResultProfile.Name;
        }
    }

    private void SaveProfile()
    {
        var name = profileName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name)) { status.Text = "Enter a profile name."; return; }
        var profile = new VocalVoiceProfile
        {
            Name = name,
            BasePitchHz = settings.BasePitchHz,
            FormantScale = settings.FormantScale,
            Breathiness = settings.Breathiness,
            Roughness = settings.Roughness,
            VibratoCents = settings.VibratoCents,
            VibratoHz = settings.VibratoHz,
            Voicing = settings.Voicing,
            Noise = settings.Noise,
            Singing = settings.Singing,
        };
        profileStore.Save(profile);
        RefreshVoiceCombo();
        status.Text = "Profile saved: " + name;
    }

    private void LoadProfile()
    {
        // select in combo if exists
        var name = profileName.Text.Trim();
        var idx = -1;
        for (int i = 0; i < voiceCombo.Items.Count; i++)
            if (voiceCombo.Items[i].ToString() == "Profile: " + name) { idx = i; break; }
        if (idx >= 0) { voiceCombo.SelectedIndex = idx; status.Text = "Profile loaded: " + name; }
        else status.Text = "Profile not found.";
    }

    private void DeleteProfile()
    {
        var name = profileName.Text.Trim();
        if (profileStore.Delete(name)) { RefreshVoiceCombo(); status.Text = "Profile deleted: " + name; }
        else status.Text = "Profile not found.";
    }

    private void RenameProfile()
    {
        var oldName = profileName.Text.Trim();
        // ask new name via simple input
        using var dlg = new Form { Text = "Rename Profile", ClientSize = new Size(320, 100), StartPosition = FormStartPosition.CenterParent };
        var tb = new TextBox { Left = 12, Top = 12, Width = 290 };
        var ok = new Button { Text = "OK", Left = 12, Top = 45, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Left = 100, Top = 45, DialogResult = DialogResult.Cancel };
        dlg.Controls.Add(tb); dlg.Controls.Add(ok); dlg.Controls.Add(cancel);
        dlg.AcceptButton = ok;
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            var newName = tb.Text.Trim();
            if (string.IsNullOrWhiteSpace(newName)) return;
            if (profileStore.Rename(oldName, newName)) { RefreshVoiceCombo(); profileName.Text = newName; status.Text = "Profile renamed."; }
            else status.Text = "Rename failed.";
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            disposedFlag = true;
            rendering?.Cancel();
            StopHear();
            debounce.Dispose();
            playbackTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}