using ModularAudience.Audio;
using ModularAudience.Audio.Midi;
using ModularAudience.Audio.Vocal;
using ModularAudience.Forms.Modules;
using NAudio.Wave;
using System.Globalization;
using System.Runtime.InteropServices;

namespace ModularAudience.Forms.Modules.Dialogs;

public sealed partial class VocalSynthDialog : Form
{
    private sealed record SessionSnapshot(
        VocalSynthSettings Settings, bool ShowSyllables, bool FollowSpokenWord,
        string? LastMidiFolder, string? VoiceSelection);

    private readonly record struct SyllableFormattingRun(int Start, int Length, bool Underline);
    private readonly record struct PlaybackWordRange(int TextStart, int TextLength, int StartFrame, int EndFrame);
    private readonly record struct TextWord(int Start, int Length, int SyllableCount);
    private sealed record SyllablePreviewResult(
        IReadOnlyList<VocalSyllableTextRange> UnderlinedRanges,
        IReadOnlyList<SyllableFormattingRun> Runs);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    private const int EmGetScrollPos = 0x04DD;
    private const int EmSetScrollPos = 0x04DE;

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
    private CancellationTokenSource? syllablePreviewCalculation;
    private CancellationTokenSource? playbackPreparation;
    private int revision, ready = -1;
    private int syllablePreviewRevision;
    private bool disposedFlag, creating, syncing, formattingSyllables, fillingSyllables;
    private readonly System.Windows.Forms.Timer debounce = new() { Interval = 300 };
    private readonly System.Windows.Forms.Timer syllablePreviewDebounce = new() { Interval = 250 };
    private WaveOut? output;
    private VocalPreview? provider;
    private readonly System.Windows.Forms.Timer playbackTimer = new() { Interval = 40 };
    private string displayedSyllableText = string.Empty;
    private IReadOnlyList<VocalSyllableTextRange> displayedSyllableRanges = [];
    private string lastMidiFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
    private bool startingHear;
    private int lastFollowedCursorPosition = -1;

    private sealed class VocalPreview : ISampleProvider
    {
        private readonly float[] data;
        private readonly IReadOnlyList<PlaybackWordRange> wordRanges;
        private int pos, finished;
        public WaveFormat WaveFormat { get; }
        public bool Loop { get; set; }
        public IReadOnlyList<PlaybackWordRange> WordRanges => wordRanges;
        public int PlayedFrames => Volatile.Read(ref pos) / WaveFormat.Channels;
        public bool Finished => Volatile.Read(ref finished) != 0;

        public VocalPreview(
            float[] data, int sampleRate, int channels, IReadOnlyList<PlaybackWordRange> wordRanges)
        {
            this.data = data;
            this.wordRanges = wordRanges;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
        }

        public int Read(Span<float> buffer)
        {
            int position = Volatile.Read(ref pos);
            if (position >= data.Length)
            {
                if (Loop)
                {
                    position = 0;
                    Volatile.Write(ref pos, 0);
                    Volatile.Write(ref finished, 0);
                }
                else
                {
                    Volatile.Write(ref finished, 1);
                    return 0;
                }
            }
            int n = Math.Min(buffer.Length, data.Length - position);
            data.AsSpan(position, n).CopyTo(buffer);
            position += n;
            Volatile.Write(ref pos, position);
            if (position >= data.Length && !Loop) Volatile.Write(ref finished, 1);
            return n;
        }
    }

    public VocalSynthDialog(VocalVoiceProfileStore? store = null, VocalEmphasisSyntaxStore? estore = null)
    {
        profileStore = store ?? VocalVoiceProfileStore.Default();
        emphasisStore = estore ?? VocalEmphasisSyntaxStore.Default();
        SessionSnapshot? sessionSnapshot = ReadSessionSnapshot();
        settings = sessionSnapshot?.Settings
            ?? VocalSynthSettings.FromPreset(VocalVoicePreset.BuiltIn[2], VocalLanguage.English, "");
        lastMidiFolder = FindExistingMidiDirectory(sessionSnapshot?.LastMidiFolder)
            ?? FindExistingMidiDirectory(settings.SingingMidiFile?.FilePath)
            ?? lastMidiFolder;
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
            followSpokenWordCheckBox.Checked = sessionSnapshot?.FollowSpokenWord ?? false;
            RestoreMidiTrackNumber();
        }
        finally { syncing = false; }

        UpdateSpeechBackend();
        languageCombo.SelectedIndexChanged += (_, _) =>
        {
            if (syncing) return;
            settings.Language = (VocalLanguage)languageCombo.SelectedIndex;
            UpdateSpeechBackend();
            if (showSyllablesCheckBox.Checked) QueueSyllablePreview(immediate: true);
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
            QueueSyllablePreview();
            if (string.IsNullOrWhiteSpace(textEditor.Text))
            {
                revision++;
                rendering?.Cancel();
                playbackPreparation?.Cancel();
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
        syllablePreviewDebounce.Tick += async (_, _) =>
        {
            syllablePreviewDebounce.Stop();
            await RefreshSyllablePreviewAsync();
        };
        playbackTimer.Tick += (_, _) =>
        {
            UpdatePlaybackCursor();
            if (provider?.Finished == true) StopHear();
        };
        FormClosing += (_, _) =>
        {
            debounce.Stop();
            CancelSyllablePreview();
            playbackPreparation?.Cancel();
            rendering?.Cancel();
            StopHear();
        };
        FormClosed += (_, _) => SaveSessionSnapshot();

        settings.Text = textEditor.Text;
        displayedSyllableText = textEditor.Text;
        UpdateButtonState();
        if (!string.IsNullOrWhiteSpace(textEditor.Text)) QueueRender();
        if (showSyllablesCheckBox.Checked) QueueSyllablePreview(immediate: true);
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

    private static string? FindExistingMidiDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        string? directory = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        return !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory)
            ? directory
            : null;
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
                followSpokenWordCheckBox.Checked,
                lastMidiFolder,
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
        string bpm = midi.DefaultBpm.ToString("F1", CultureInfo.InvariantCulture);
        midiFileLabel.Text = $"{fileName} (#{pageNumber} {trackName}) [BPM: {bpm}]";
    }

    private void OpenMidiFile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Load MIDI for singing",
            Filter = "MIDI files (*.mid;*.midi)|*.mid;*.midi",
            InitialDirectory = lastMidiFolder,
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        lastMidiFolder = Path.GetDirectoryName(dialog.FileName) ?? lastMidiFolder;

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
        if (syncing) return;
        QueueSyllablePreview(immediate: true);
    }

    private void QueueSyllablePreview(bool immediate = false)
    {
        if (disposedFlag) return;
        syllablePreviewDebounce.Stop();
        syllablePreviewRevision++;
        syllablePreviewCalculation?.Cancel();

        if (!showSyllablesCheckBox.Checked && displayedSyllableRanges.Count == 0)
        {
            displayedSyllableText = textEditor.Text;
            return;
        }

        if (showSyllablesCheckBox.Checked && !immediate)
        {
            syllablePreviewDebounce.Start();
            return;
        }

        _ = RefreshSyllablePreviewAsync();
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
            if (showSyllablesCheckBox.Checked) QueueSyllablePreview(immediate: true);
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

    private async Task RefreshSyllablePreviewAsync()
    {
        if (disposedFlag || formattingSyllables) return;
        int requestRevision = syllablePreviewRevision;
        string text = textEditor.Text;
        bool showSyllables = showSyllablesCheckBox.Checked;
        string previousText = displayedSyllableText;
        VocalSyllableTextRange[] previousRanges = displayedSyllableRanges.ToArray();
        VocalSynthSettings previewSettings = settings.Clone();
        previewSettings.Text = text;
        previewSettings.EmphasisSyntax = emphasisSyntax;
        previewSettings.CustomSampleMappings = VocalCustomSampleStore.GetMappings();
        var cancellation = new CancellationTokenSource();
        syllablePreviewCalculation?.Cancel();
        syllablePreviewCalculation = cancellation;
        try
        {
            SyllablePreviewResult result = await Task.Run(() =>
            {
                List<VocalSyllableTextRange> underlinedRanges = [];
                if (showSyllables && !string.IsNullOrWhiteSpace(text))
                {
                    IReadOnlyList<VocalSyllableTextRange> ranges =
                        VocalSynth.GetSyllableTextRanges(previewSettings, cancellation.Token);
                    foreach (VocalSyllableTextRange range in ranges)
                    {
                        cancellation.Token.ThrowIfCancellationRequested();
                        if ((range.Index & 1) != 0 && range.Start >= 0 && range.Length > 0
                            && range.Start <= text.Length - range.Length)
                            underlinedRanges.Add(range);
                    }
                }

                IReadOnlyList<VocalSyllableTextRange> formattedRanges = underlinedRanges.ToArray();
                IReadOnlyList<SyllableFormattingRun> runs = BuildSyllableFormattingRuns(
                    previousText, previousRanges, text, formattedRanges, cancellation.Token);
                return new SyllablePreviewResult(formattedRanges, runs);
            }, cancellation.Token);

            if (disposedFlag || requestRevision != syllablePreviewRevision
                || cancellation.IsCancellationRequested || text != textEditor.Text
                || showSyllables != showSyllablesCheckBox.Checked)
                return;
            ApplySyllablePreview(text, result);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error) when (
            error is ArgumentException or InvalidOperationException or InvalidDataException)
        {
            if (!disposedFlag && requestRevision == syllablePreviewRevision)
                status.Text = "Syllable preview failed: " + error.Message;
        }
        finally
        {
            if (ReferenceEquals(syllablePreviewCalculation, cancellation))
                syllablePreviewCalculation = null;
            cancellation.Dispose();
        }
    }

    private static IReadOnlyList<SyllableFormattingRun> BuildSyllableFormattingRuns(
        string previousText,
        IReadOnlyList<VocalSyllableTextRange> previousRanges,
        string text,
        IReadOnlyList<VocalSyllableTextRange> ranges,
        CancellationToken cancellationToken)
    {
        bool[] previousUnderline = CreateUnderlineMap(previousText.Length, previousRanges, cancellationToken);
        bool[] currentUnderline = CreateUnderlineMap(text.Length, ranges, cancellationToken);
        int prefix = 0;
        int sharedLength = Math.Min(previousText.Length, text.Length);
        while (prefix < sharedLength)
        {
            if ((prefix & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (previousText[prefix] != text[prefix]) break;
            prefix++;
        }

        int previousSuffix = previousText.Length;
        int currentSuffix = text.Length;
        while (previousSuffix > prefix && currentSuffix > prefix)
        {
            if ((previousSuffix & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (previousText[previousSuffix - 1] != text[currentSuffix - 1]) break;
            previousSuffix--;
            currentSuffix--;
        }

        int suffixOffset = currentSuffix - previousSuffix;
        List<SyllableFormattingRun> result = [];
        int runStart = -1;
        bool runUnderline = false;
        for (int index = 0; index < text.Length; index++)
        {
            if ((index & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            int previousIndex = index < prefix
                ? index
                : index >= currentSuffix ? index - suffixOffset : -1;
            bool underline = currentUnderline[index];
            bool changed = previousIndex < 0 || previousUnderline[previousIndex] != underline;
            if (changed)
            {
                if (runStart < 0)
                {
                    runStart = index;
                    runUnderline = underline;
                }
                else if (runUnderline != underline)
                {
                    result.Add(new SyllableFormattingRun(runStart, index - runStart, runUnderline));
                    runStart = index;
                    runUnderline = underline;
                }
            }
            else if (runStart >= 0)
            {
                result.Add(new SyllableFormattingRun(runStart, index - runStart, runUnderline));
                runStart = -1;
            }
        }

        if (runStart >= 0)
            result.Add(new SyllableFormattingRun(runStart, text.Length - runStart, runUnderline));
        return result;
    }

    private static bool[] CreateUnderlineMap(
        int textLength,
        IReadOnlyList<VocalSyllableTextRange> ranges,
        CancellationToken cancellationToken)
    {
        bool[] result = new bool[textLength];
        foreach (VocalSyllableTextRange range in ranges)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (range.Start < 0 || range.Length <= 0 || range.Start > textLength - range.Length)
                continue;
            for (int index = range.Start; index < range.Start + range.Length; index++)
            {
                if ((index & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                result[index] = true;
            }
        }
        return result;
    }

    private void ApplySyllablePreview(string text, SyllablePreviewResult result)
    {
        if (disposedFlag || text != textEditor.Text) return;
        if (result.Runs.Count == 0)
        {
            displayedSyllableText = text;
            displayedSyllableRanges = result.UnderlinedRanges;
            return;
        }

        int selectionStart = textEditor.SelectionStart;
        int selectionLength = textEditor.SelectionLength;
        bool hasScrollPosition = textEditor.IsHandleCreated;
        NativePoint scrollPosition = default;
        if (hasScrollPosition)
            SendMessage(textEditor.Handle, EmGetScrollPos, IntPtr.Zero, ref scrollPosition);

        formattingSyllables = true;
        textEditor.SuspendLayout();
        try
        {
            using var underlinedFont = new Font(
                textEditor.Font, textEditor.Font.Style | FontStyle.Underline);
            foreach (SyllableFormattingRun run in result.Runs)
            {
                if (run.Start < 0 || run.Length <= 0 || run.Start > text.Length - run.Length)
                    continue;
                textEditor.Select(run.Start, run.Length);
                textEditor.SelectionFont = run.Underline ? underlinedFont : textEditor.Font;
            }
        }
        finally
        {
            try
            {
                int safeStart = Math.Clamp(selectionStart, 0, textEditor.TextLength);
                int safeLength = Math.Clamp(selectionLength, 0, textEditor.TextLength - safeStart);
                textEditor.Select(safeStart, safeLength);
                if (hasScrollPosition)
                    SendMessage(textEditor.Handle, EmSetScrollPos, IntPtr.Zero, ref scrollPosition);
            }
            finally
            {
                textEditor.ResumeLayout();
                formattingSyllables = false;
            }
        }

        displayedSyllableText = text;
        displayedSyllableRanges = result.UnderlinedRanges;
    }

    private void CancelSyllablePreview()
    {
        syllablePreviewDebounce.Stop();
        syllablePreviewRevision++;
        syllablePreviewCalculation?.Cancel();
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
        playbackPreparation?.Cancel();
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
        if (startingHear) return;
        startingHear = true;
        CancellationTokenSource? preparation = null;
        try
        {
            if (!await EnsureRenderedAsync()) return;
            AudioObj? renderedAudio = audio;
            if (renderedAudio is null) return;

            IReadOnlyList<PlaybackWordRange> wordRanges = [];
            if (followSpokenWordCheckBox.Checked)
            {
                VocalSynthSettings previewSettings = settings.Clone();
                previewSettings.Text = textEditor.Text;
                previewSettings.Speed = (double)speed.Value;
                previewSettings.SingingMidiSemitoneOffset = (double)midiSemitoneOffset.Value;
                previewSettings.CustomSampleMappings = VocalCustomSampleStore.GetMappings();
                var tokenSource = new CancellationTokenSource();
                preparation = tokenSource;
                playbackPreparation?.Cancel();
                playbackPreparation = tokenSource;
                wordRanges = await Task.Run(
                    () => CreatePlaybackTimeline(
                        previewSettings,
                        renderedAudio.Data.Length / renderedAudio.Channels,
                        tokenSource.Token),
                    tokenSource.Token);
                if (disposedFlag || tokenSource.IsCancellationRequested
                    || ready != revision || !ReferenceEquals(audio, renderedAudio))
                    return;
            }

            if (disposedFlag) return;
            if (!followSpokenWordCheckBox.Checked) wordRanges = [];
            provider = new VocalPreview(
                renderedAudio.Data, renderedAudio.SampleRate, renderedAudio.Channels, wordRanges);
            output = new WaveOut { BufferMilliseconds = 60 };
            output.Init(provider);
            output.Play();
            prehear.Text = "Stop";
            lastFollowedCursorPosition = -1;
            playbackTimer.Start();
            UpdatePlaybackCursor();
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { StopHear(); status.Text = error.Message; }
        finally
        {
            if (ReferenceEquals(playbackPreparation, preparation))
                playbackPreparation = null;
            preparation?.Dispose();
            startingHear = false;
        }
    }

    private void StopHear()
    {
        playbackTimer.Stop();
        output?.Stop();
        output?.Dispose();
        output = null;
        provider = null;
        lastFollowedCursorPosition = -1;
        prehear.Text = "Prehear";
        prehear.Enabled = !string.IsNullOrWhiteSpace(textEditor.Text);
    }

    private void UpdatePlaybackCursor()
    {
        if (!followSpokenWordCheckBox.Checked || provider is null || provider.WordRanges.Count == 0)
            return;
        int position = GetPlaybackCursorTextPosition(provider.WordRanges, provider.PlayedFrames);
        if (position < 0) return;
        position = Math.Clamp(position, 0, textEditor.TextLength);
        if (position == lastFollowedCursorPosition) return;
        textEditor.Select(position, 0);
        textEditor.ScrollToCaret();
        lastFollowedCursorPosition = position;
    }

    private static int GetPlaybackCursorTextPosition(
        IReadOnlyList<PlaybackWordRange> wordRanges, int frame)
    {
        if (wordRanges.Count == 0) return -1;
        int previousEnd = wordRanges[0].TextStart + wordRanges[0].TextLength;
        foreach (PlaybackWordRange range in wordRanges)
        {
            if (frame < range.StartFrame) break;
            previousEnd = range.TextStart + range.TextLength;
            if (frame < range.EndFrame) return previousEnd;
        }
        return previousEnd;
    }

    private static IReadOnlyList<PlaybackWordRange> CreatePlaybackTimeline(
        VocalSynthSettings settings, int totalFrames, CancellationToken cancellationToken)
    {
        if (totalFrames <= 0 || string.IsNullOrWhiteSpace(settings.Text)) return [];
        IReadOnlyList<VocalSyllableTextRange> syllableRanges =
            VocalSynth.GetSyllableTextRanges(settings, cancellationToken);
        List<TextWord> words = [];
        bool useSyllableUnits = settings.Language is VocalLanguage.Chinese or VocalLanguage.Japanese
            && !settings.Text.Any(char.IsWhiteSpace);
        if (useSyllableUnits)
        {
            foreach (VocalSyllableTextRange range in syllableRanges)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (range.Start >= 0 && range.Length > 0
                    && range.Start <= settings.Text.Length - range.Length)
                    words.Add(new TextWord(range.Start, range.Length, 1));
            }
        }
        else
        {
            List<(int Start, int Length)> spans = GetTextWordSpans(settings.Text, cancellationToken);
            int syllableIndex = 0;
            foreach ((int start, int length) in spans)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int end = start + length;
                while (syllableIndex < syllableRanges.Count
                    && syllableRanges[syllableIndex].Start + syllableRanges[syllableIndex].Length <= start)
                    syllableIndex++;
                int count = 0;
                for (int index = syllableIndex; index < syllableRanges.Count
                    && syllableRanges[index].Start < end; index++)
                {
                    VocalSyllableTextRange range = syllableRanges[index];
                    if (range.Start + range.Length > start) count++;
                }
                if (count > 0) words.Add(new TextWord(start, length, count));
            }

            if (words.Count == 0)
                words.AddRange(spans.Select(span => new TextWord(span.Start, span.Length, 1)));
        }

        if (words.Count == 0)
        {
            foreach (VocalSyllableTextRange range in syllableRanges)
            {
                if (range.Start >= 0 && range.Length > 0
                    && range.Start <= settings.Text.Length - range.Length)
                    words.Add(new TextWord(range.Start, range.Length, 1));
            }
        }
        if (words.Count == 0) return [];

        double totalWeight = 0;
        for (int index = 0; index < words.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            totalWeight += Math.Max(1, words[index].SyllableCount);
            if (index + 1 < words.Count)
                totalWeight += GetPunctuationPauseWeight(
                    settings.Text,
                    words[index].Start + words[index].Length,
                    words[index + 1].Start);
        }
        if (totalWeight <= 0) return [];

        double framesPerWeight = totalFrames / totalWeight;
        double elapsedWeight = 0;
        List<PlaybackWordRange> result = new(words.Count);
        for (int index = 0; index < words.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TextWord word = words[index];
            int startFrame = (int)Math.Clamp(Math.Floor(elapsedWeight * framesPerWeight), 0, totalFrames);
            elapsedWeight += Math.Max(1, word.SyllableCount);
            int endFrame = (int)Math.Clamp(Math.Ceiling(elapsedWeight * framesPerWeight), 0, totalFrames);
            if (endFrame <= startFrame && startFrame < totalFrames)
                endFrame = startFrame + 1;
            if (endFrame > startFrame)
                result.Add(new PlaybackWordRange(word.Start, word.Length, startFrame, endFrame));
            if (index + 1 < words.Count)
                elapsedWeight += GetPunctuationPauseWeight(
                    settings.Text,
                    word.Start + word.Length,
                    words[index + 1].Start);
        }
        return result;
    }

    private static List<(int Start, int Length)> GetTextWordSpans(
        string text, CancellationToken cancellationToken)
    {
        List<(int Start, int Length)> result = [];
        for (int index = 0; index < text.Length;)
        {
            if ((index & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (!IsWordCharacter(text[index]))
            {
                index++;
                continue;
            }
            int start = index++;
            while (index < text.Length && IsWordCharacter(text[index])) index++;
            result.Add((start, index - start));
        }
        return result;
    }

    private static bool IsWordCharacter(char value)
    {
        if (char.IsLetterOrDigit(value)) return true;
        UnicodeCategory category = char.GetUnicodeCategory(value);
        return category is UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.EnclosingMark;
    }

    private static double GetPunctuationPauseWeight(string text, int start, int end)
    {
        double weight = 0;
        for (int index = start; index < end; index++)
        {
            weight += text[index] switch
            {
                ',' or '\uFF0C' => 0.35,
                ';' or ':' or '\uFF1B' or '\uFF1A' => 0.55,
                '.' or '!' or '?' or '\n' or '\r' or '\u3002' or '\uFF01' or '\uFF1F' => 1.0,
                _ => 0,
            };
        }
        return Math.Min(weight, 2.5);
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(
        IntPtr handle, int message, IntPtr wParam, ref NativePoint point);

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
            if (showSyllablesCheckBox.Checked) QueueSyllablePreview(immediate: true);
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
                if (showSyllablesCheckBox.Checked) QueueSyllablePreview(immediate: true);
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
            playbackPreparation?.Cancel();
            CancelSyllablePreview();
            StopHear();
            debounce.Dispose();
            syllablePreviewDebounce.Dispose();
            playbackTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}