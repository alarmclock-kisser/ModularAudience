using ModularAudience.Audio;
using ModularAudience.Audio.Midi;
using ModularAudience.Audio.Synthesis;
using ModularAudience.Audio.Vocal;
using ModularAudience.Generators;
using ModularAudience.Forms.Helpers;
using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace ModularAudience.Forms.Modules.Dialogs
{
    internal sealed record MidiClipboardEntry(List<MidiNoteData> Notes, DateTime CopiedAt)
    {
        public string Caption => $"{this.Notes.Count} notes · {this.CopiedAt:HH:mm:ss}";
    }

    /// <summary>
    /// Standalone, modeless piano-roll editor. It owns its MIDI data and render path
    /// so it does not depend on a MIDI Renderer window.
    /// </summary>
    public partial class CustomMidiEditorDialog : Form
    {
        private const int KeyLabelWidth = 44;
        private const int TopMargin = 18;
        private const int BottomMargin = 20;
        private const int MinimumBarLabelSpacing = 56;
        private const int MinimumTimeLabelSpacing = 72;
        private const int MinimumKeyGap = 12;
        private const double EmptyTailRatio = 0.20;
        private const int DefaultTicksPerQuarterNote = 960;
        private const int DefaultLowestNote = 36; // C2
        private const int DefaultHighestNote = 84; // C6
        private const int MaximumHistoryDepth = 100;
        private static string lastMidiFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);

        private readonly MidiSampleRenderCache sampleRenderCache = new();
        private readonly BreakbeatInstrumentSampleCache customInstrumentSampleCache = new();
        private readonly ContextMenuStrip instrumentMenu = new();
        private readonly HashSet<Task> activeRenders = [];
        private readonly List<EditorSnapshot> undoHistory = [];
        private readonly List<EditorSnapshot> redoHistory = [];

        private MidiFileData midiFile;
        private int selectedTrackPosition;
        private MidiInstrument instrument = MidiInstrument.Sine;
        private InstrumentSynthSettings? customSynthSettings;
        private AudioObj? customSample;
        private AudioCollectionView? customCollection;

        private Point? editStart;
        private Point? rectangleSelectionStart;
        private Point? rectangleSelectionEnd;
        private Point? selectionDragStart;
        private Point? selectionDragEnd;
        private Point? pendingNoteClickStart;
        private MidiNoteData? pendingNoteClick;
        private Point? selectionMoveStart;
        private long selectionMoveAnchorTick;
        private int selectionMoveAnchorNote;
        private bool selectionMoveDragging;
        private MidiSelection? selectionMoveOriginalSelection;
        private List<MidiNoteData>? selectionMoveOriginalNotes;
        private List<MidiNoteData>? selectionMoveMovedNotes;
        private MidiSelection? noteSelection;
        private HashSet<MidiNoteData>? noteSelectionNotes;
        private EditorSnapshot? pendingHistorySnapshot;
        private MouseButtons editButton;
        private int editNote;
        private long editPreviousLength;
        private bool panning;
        private Point panStart;
        private long panViewStartTick;
        private long panViewEndTick;
        private long viewStartTick;
        private long viewEndTick;
        private long editorLengthTicks;
        private double pixelsPerTick;
        private bool updatingScrollBar;
        private bool updatingKeyRange;
        private bool restoringHistory;
        private bool initialized;
        private bool updatingTrackSelector;
        private bool closing;
        private bool allowClose;
        private readonly List<MidiClipboardEntry> clipboardHistory = [];
        private const int MaxClipboardHistoryEntries = 50;
        private bool pasteHoldTracking;
        private Keys pasteHoldKeyCode = Keys.None;
        private readonly System.Windows.Forms.Timer pasteHoldTimer = new() { Interval = 250 };
        private List<MidiNoteData> ghostNotes = [];
        private bool ghostPreviewActive;
        private bool ghostPreviewValid;
        private Point lastEditorPointer;
        private bool hasEditorPointer;
        private Point? pendingPlayFromHerePoint;

        private CancellationTokenSource? playbackCts;
        private CancellationTokenSource? saveRenderCts;
        private AudioObj? previewAudio;
        private bool previewCaretVisible;
        private long previewStartTick;
        private long playFromHereStartTick;

        private MicrophoneAudioCapture? recordingCapture;
        private string? recordingPath;
        private readonly Stopwatch recordingClock = new();
        private bool recordingActive;
        private bool recordingStarting;
        private bool recordingFinalizing;
        private CancellationTokenSource? recordingCts;

        private sealed class CollectionSnapshot : IDisposable
        {
            public List<AudioObj> Samples { get; } = [];

            public void Dispose()
            {
                foreach (AudioObj sample in this.Samples)
                {
                    sample.Dispose();
                }
            }
        }

        internal readonly record struct MidiSelection(
            long StartTick,
            long EndTick,
            int LowestNote,
            int HighestNote);

        private sealed record EditorSnapshot(
            List<MidiNoteData> Notes,
            long LengthTicks,
            decimal Bpm,
            decimal PitchFrequency,
            decimal Resolution,
            int LowestNote,
            int HighestNote,
            MidiSelection? Selection,
            List<MidiNoteData>? SelectedNotes);

        private sealed record MidiRenderOptions(
            MidiInstrument Instrument,
            InstrumentSynthSettings? CustomSynthSettings,
            double Bpm,
            double PitchFrequency);

        public CustomMidiEditorDialog(MidiFileData? midiFileData = null, double initialBpm = 100.0)
        {
            this.InitializeComponent();
            this.KeyPreview = true;
            this.components ??= new System.ComponentModel.Container();
            this.components.Add(this.instrumentMenu);
            this.components.Add(this.pasteHoldTimer);
            this.pasteHoldTimer.Tick += this.pasteHoldTimer_Tick;
            this.KeyUp += this.CustomMidiEditorDialog_KeyUp;
            Hints.HintContent.Apply(this.toolTip_editor, this, Hints.HintContent.CustomMidiEditor);
            this.midiFile = NormalizeEditorFile(midiFileData, initialBpm);
            this.UpdateTrackSelector();
            this.numericUpDown_bpm.Value = (decimal)Math.Clamp(this.midiFile.DefaultBpm, 20.0, 400.0);
            this.numericUpDown_pitchFrequency.Value = (decimal)Math.Clamp(this.midiFile.PitchFrequency, 1.0, 1000.0);
            this.PopulateNoteNames();
            this.SetKeyRange(DefaultLowestNote, DefaultHighestNote);
            this.SetInstrument(MidiInstrument.Sine);
            this.pictureBox_editor.MouseEnter += (_, _) => this.pictureBox_editor.Focus();
            this.pictureBox_editor.MouseMove += this.TrackPointer;

            long initialViewLength = Math.Max(this.GetRequiredEditorLength(), this.DefaultViewLengthTicks);
            this.editorLengthTicks = initialViewLength;
            this.viewStartTick = 0;
            this.viewEndTick = initialViewLength;
            this.pixelsPerTick = this.CalculatePixelsPerTick(initialViewLength);

            this.pictureBox_editor.Cursor = Cursors.Cross;
            this.initialized = true;
            this.UpdateScrollBar();
            this.pictureBox_editor.Invalidate();
        }

        private MidiTrackData Track => this.midiFile.Tracks[this.selectedTrackPosition];

        internal MidiFileData MidiFile => this.midiFile;

        internal MidiTrackData SelectedTrack => this.Track;

        internal MidiInstrument Instrument => this.instrument;

        private int LowestNote => MidiNoteFromKeyIndex(this.domainUpDown_minKey.SelectedIndex);

        private int HighestNote => MidiNoteFromKeyIndex(this.domainUpDown_maxKey.SelectedIndex);

        private int PianoRollHeight => Math.Max(1, this.pictureBox_editor.ClientSize.Height - TopMargin - BottomMargin);

        private int PianoRollBottom => TopMargin + this.PianoRollHeight;

        private int Resolution => Math.Max(1, (int)this.numericUpDown_resolution.Value);

        private long GridTicks => Math.Max(1, this.midiFile.TicksPerQuarterNote / this.Resolution);

        private double PitchFrequency => (double)this.numericUpDown_pitchFrequency.Value;

        private double Bpm => (double)this.numericUpDown_bpm.Value;

        private long DefaultViewLengthTicks => Math.Max(this.GridTicks * 8, this.midiFile.TicksPerBar);

        private static MidiFileData NormalizeEditorFile(MidiFileData? source, double initialBpm)
        {
            if (source is null)
            {
                return CreateEmptyEditorFile(DefaultTicksPerQuarterNote, initialBpm);
            }

            if (source.Tracks.Count == 0)
            {
                return MidiFileData.CreateGenerated(
                    [CreateEditorTrack()],
                    source.TicksPerQuarterNote,
                    source.DefaultBpm,
                    source.FilePath,
                    source.PitchFrequency,
                    source.TimeSignatureNumerator,
                    source.TimeSignatureDenominator);
            }

            return source;
        }

        private static MidiTrackData CreateEditorTrack() => new()
        {
            Index = 0,
            Name = "Piano Roll"
        };

        private static MidiFileData CreateEmptyEditorFile(int ticksPerQuarterNote, double bpm) =>
            MidiFileData.CreateGenerated([CreateEditorTrack()], ticksPerQuarterNote, bpm);

        private void PopulateNoteNames()
        {
            for (int note = 127; note >= 0; note--)
            {
                this.domainUpDown_minKey.Items.Add(MidiNoteName(note));
                this.domainUpDown_maxKey.Items.Add(MidiNoteName(note));
            }
        }

        private static int MidiNoteFromKeyIndex(int selectedIndex) =>
            selectedIndex < 0 ? 0 : 127 - selectedIndex;

        private static int KeyIndexFromMidiNote(int note) => 127 - Math.Clamp(note, 0, 127);

        private void SetKeyRange(int lowest, int highest)
        {
            (lowest, highest) = NormalizeKeyRange(lowest, highest, lowestChanged: true);
            this.updatingKeyRange = true;
            try
            {
                this.domainUpDown_minKey.SelectedIndex = KeyIndexFromMidiNote(lowest);
                this.domainUpDown_maxKey.SelectedIndex = KeyIndexFromMidiNote(highest);
            }
            finally
            {
                this.updatingKeyRange = false;
            }
        }

        /// <summary>Keeps the visible key range ordered and at least one octave wide.</summary>
        internal static (int Lowest, int Highest) NormalizeKeyRange(int lowest, int highest, bool lowestChanged)
        {
            lowest = Math.Clamp(lowest, 0, 127);
            highest = Math.Clamp(highest, 0, 127);
            if (highest - lowest >= MinimumKeyGap)
            {
                return (lowest, highest);
            }

            if (lowestChanged)
            {
                lowest = Math.Clamp(lowest, 0, 127 - MinimumKeyGap);
                highest = Math.Max(highest, lowest + MinimumKeyGap);
                if (highest > 127)
                {
                    highest = 127;
                    lowest = 127 - MinimumKeyGap;
                }
            }
            else
            {
                highest = Math.Clamp(highest, MinimumKeyGap, 127);
                lowest = Math.Min(lowest, highest - MinimumKeyGap);
                if (lowest < 0)
                {
                    lowest = 0;
                    highest = MinimumKeyGap;
                }
            }

            return (lowest, highest);
        }

        internal static string MidiNoteName(int note) => ((note % 12) switch
        {
            0 => "C",
            1 => "C#",
            2 => "D",
            3 => "D#",
            4 => "E",
            5 => "F",
            6 => "F#",
            7 => "G",
            8 => "G#",
            9 => "A",
            10 => "A#",
            _ => "B"
        }) + (note / 12 - 1);

        internal static string FriendlyInstrumentName(MidiInstrument value) => value switch
        {
            MidiInstrument.Pluck => "Pluck / Karplus-Strong",
            MidiInstrument.CustomSample => "Custom Sample",
            MidiInstrument.CustomCollection => "Custom Collection",
            _ => value.ToString()
        };

        private void UpdateInstrumentLabel()
        {
            string text;
            string? keyNoteHint = null;
            if (this.customSynthSettings is InstrumentSynthSettings synth)
            {
                text = $"{InstrumentSynthCatalog.FamilyName(synth.Family)} / {synth.VoiceName}";
                if (synth.Family != SynthFamily.DrumSet && Math.Abs(synth.KeyNoteSemitones) > 1e-12)
                {
                    int keyNote = (int)Math.Round(synth.KeyNoteSemitones) + InstrumentSynthSettings.BaseMidiNote;
                    string sign = synth.KeyNoteSemitones > 0 ? "+" : string.Empty;
                    keyNoteHint = $"Key Note {MidiNoteName(keyNote)} ({sign}{synth.KeyNoteSemitones:0.##} st against C4): rendered MIDI notes are shifted by this offset.";
                }
            }
            else if (this.instrument == MidiInstrument.CustomSample && this.customSample != null)
            {
                text = $"{FriendlyInstrumentName(this.instrument)}: {this.customSample.Name}";
            }
            else if (this.instrument == MidiInstrument.CustomCollection && this.customCollection != null)
            {
                text = $"{FriendlyInstrumentName(this.instrument)}: {this.customCollection.Text}";
            }
            else
            {
                text = FriendlyInstrumentName(this.instrument);
            }

            this.label_instrument.Text = text;
            this.toolTip_editor.SetToolTip(this.label_instrument, keyNoteHint is null
                ? $"Selected MIDI instrument: {text}"
                : $"{text}\r\n{keyNoteHint}");
        }

        private void SetInstrument(MidiInstrument value)
        {
            this.customSynthSettings = null;
            this.instrument = value;
            if (value != MidiInstrument.CustomSample)
            {
                this.customSample?.Dispose();
                this.customSample = null;
            }

            if (value != MidiInstrument.CustomCollection)
            {
                this.customCollection = null;
            }

            this.UpdateInstrumentLabel();
        }

        private void button_instrument_Click(object? sender, EventArgs e)
        {
            ContextMenuStrip menu = this.instrumentMenu;
            while (menu.Items.Count > 0)
            {
                menu.Items[0].Dispose();
            }

            foreach (MidiInstrument value in Enum.GetValues<MidiInstrument>())
            {
                string label = FriendlyInstrumentName(value);
                if (value == MidiInstrument.CustomSample)
                {
                    ToolStripMenuItem sampleItem = new(label)
                    {
                        Checked = this.customSynthSettings is null && this.instrument == value
                    };
                    foreach (AudioObj audio in WindowMain.CollectionViews
                                 .Where(view => !view.IsDisposed)
                                 .SelectMany(view => view.AudioC.Audios)
                                 .Where(audio => audio.Channels > 0 && audio.SampleRate > 0 && audio.Data.Length >= audio.Channels)
                                 .DistinctBy(audio => audio.Id))
                    {
                        AudioObj selected = audio;
                        sampleItem.DropDownItems.Add(new ToolStripMenuItem(selected.Name, null, (_, _) =>
                        {
                            this.customSample?.Dispose();
                            this.customSample = selected.Clone();
                            this.SetInstrument(MidiInstrument.CustomSample);
                        }));
                    }

                    if (sampleItem.DropDownItems.Count == 0)
                    {
                        sampleItem.DropDownItems.Add(new ToolStripMenuItem("No audio objects are currently open") { Enabled = false });
                    }

                    menu.Items.Add(sampleItem);
                    continue;
                }

                if (value == MidiInstrument.CustomCollection)
                {
                    ToolStripMenuItem collectionItem = new(label)
                    {
                        Checked = this.customSynthSettings is null && this.instrument == value
                    };
                    foreach (AudioCollectionView view in WindowMain.CollectionViews.Where(view => !view.IsDisposed))
                    {
                        AudioCollectionView selected = view;
                        collectionItem.DropDownItems.Add(new ToolStripMenuItem(
                            $"{selected.Text} ({selected.AudioC.Audios.Count} samples)",
                            null,
                            (_, _) =>
                            {
                                this.customCollection = selected;
                                this.SetInstrument(MidiInstrument.CustomCollection);
                            })
                        {
                            Enabled = selected.AudioC.Audios.Any(sample =>
                                sample.Channels > 0 && sample.SampleRate > 0 && sample.Data.Length >= sample.Channels)
                        });
                    }

                    if (collectionItem.DropDownItems.Count == 0)
                    {
                        collectionItem.DropDownItems.Add(new ToolStripMenuItem("No audio collections are currently open") { Enabled = false });
                    }

                    menu.Items.Add(collectionItem);
                    continue;
                }

                menu.Items.Add(new ToolStripMenuItem(label, null, (_, _) => this.SetInstrument(value))
                {
                    Checked = this.customSynthSettings is null && this.instrument == value
                });
            }

            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem instrumentSynthMenu = new("Instrument Synth")
            {
                Name = "toolStripMenuItem_instrumentSynth"
            };
            foreach (SynthFamily family in Enum.GetValues<SynthFamily>())
            {
                SynthFamily selectedFamily = family;
                instrumentSynthMenu.DropDownItems.Add(new ToolStripMenuItem(
                    InstrumentSynthCatalog.FamilyName(family),
                    null,
                    (_, _) => this.OpenInstrumentSynth(selectedFamily))
                {
                    Checked = this.customSynthSettings?.Family == family
                });
            }

            menu.Items.Add(instrumentSynthMenu);

            menu.Show(this.button_instrument, 0, this.button_instrument.Height);
        }

        private void OpenInstrumentSynth(SynthFamily family)
        {
            InstrumentSynthSettings? initialSettings = this.customSynthSettings?.Family == family
                ? this.customSynthSettings.Clone()
                : null;
            InstrumentSampleSynthDialog dialog = new(
                family,
                onInstrumentConfirmed: this.ApplyCustomSynthSettings,
                initialSettings: initialSettings);
            dialog.Show(this);
        }

        private void ApplyCustomSynthSettings(InstrumentSynthSettings settings)
        {
            InstrumentSynthSettings snapshot = settings.Clone();
            snapshot.Validate();
            this.customSynthSettings = snapshot;
            this.customSample?.Dispose();
            this.customSample = null;
            this.customCollection = null;
            this.UpdateInstrumentLabel();
            this.pictureBox_editor.Invalidate();
        }

        private void numericUpDown_resolution_ValueChanged(object? sender, EventArgs e)
        {
            if (!this.initialized)
            {
                return;
            }

            this.editorLengthTicks = Math.Max(this.editorLengthTicks, this.GetRequiredEditorLength());
            long visibleLength = Math.Max(this.GridTicks, this.viewEndTick - this.viewStartTick);
            this.SetView(this.viewStartTick, this.viewStartTick + visibleLength);
            this.pixelsPerTick = this.CalculatePixelsPerTick(this.viewEndTick - this.viewStartTick);
            this.UpdateScrollBar();
            this.pictureBox_editor.Invalidate();
        }

        private void numericUpDown_bpm_ValueChanged(object? sender, EventArgs e)
        {
            if (this.initialized)
            {
                this.pictureBox_editor.Invalidate();
            }
        }

        private void numericUpDown_track_ValueChanged(object? sender, EventArgs e)
        {
            if (!this.initialized || this.updatingTrackSelector)
            {
                return;
            }

            int position = Math.Clamp((int)this.numericUpDown_track.Value - 1, 0, this.midiFile.Tracks.Count - 1);
            if (position == this.selectedTrackPosition)
            {
                return;
            }

            this.selectedTrackPosition = position;
            this.UpdateTrackSelector();
            this.noteSelection = null;
            this.noteSelectionNotes = null;
            this.pendingHistorySnapshot = null;
            this.undoHistory.Clear();
            this.redoHistory.Clear();
            this.ResetTrackView();
        }

        private void UpdateTrackSelector()
        {
            this.updatingTrackSelector = true;
            try
            {
                this.numericUpDown_track.Value = 1;
                this.numericUpDown_track.Maximum = Math.Max(1, this.midiFile.Tracks.Count);
                this.numericUpDown_track.Value = this.selectedTrackPosition + 1;
            }
            finally
            {
                this.updatingTrackSelector = false;
            }

            string trackName = string.IsNullOrWhiteSpace(this.Track.Name)
                ? $"Track {this.selectedTrackPosition + 1}"
                : this.Track.Name;
            this.label_trackName.Text = trackName;
            this.toolTip_editor.SetToolTip(this.label_trackName, trackName);
        }

        private void ResetTrackView()
        {
            long viewLength = Math.Max(this.GetRequiredEditorLength(), this.DefaultViewLengthTicks);
            this.editorLengthTicks = viewLength;
            this.SetView(0, viewLength);
            this.pixelsPerTick = this.CalculatePixelsPerTick(viewLength);
            this.UpdateScrollBar();
            this.pictureBox_editor.Invalidate();
        }

        private void numericUpDown_pitchFrequency_ValueChanged(object? sender, EventArgs e)
        {
            if (this.initialized)
            {
                this.pictureBox_editor.Invalidate();
            }
        }

        private void domainUpDown_minKey_SelectedItemChanged(object? sender, EventArgs e) => this.ApplyKeyRange(lowestChanged: true);

        private void domainUpDown_maxKey_SelectedItemChanged(object? sender, EventArgs e) => this.ApplyKeyRange(lowestChanged: false);

        private void ApplyKeyRange(bool lowestChanged)
        {
            if (!this.initialized || this.updatingKeyRange || this.restoringHistory)
            {
                return;
            }

            int lowestIndex = this.domainUpDown_minKey.SelectedIndex;
            int highestIndex = this.domainUpDown_maxKey.SelectedIndex;
            if (lowestIndex < 0 || highestIndex < 0)
            {
                return;
            }

            int lowest = MidiNoteFromKeyIndex(lowestIndex);
            int highest = MidiNoteFromKeyIndex(highestIndex);
            (int validLowest, int validHighest) = NormalizeKeyRange(lowest, highest, lowestChanged);
            this.updatingKeyRange = true;
            try
            {
                int validLowestIndex = KeyIndexFromMidiNote(validLowest);
                int validHighestIndex = KeyIndexFromMidiNote(validHighest);
                if (this.domainUpDown_minKey.SelectedIndex != validLowestIndex)
                {
                    this.domainUpDown_minKey.SelectedIndex = validLowestIndex;
                }

                if (this.domainUpDown_maxKey.SelectedIndex != validHighestIndex)
                {
                    this.domainUpDown_maxKey.SelectedIndex = validHighestIndex;
                }
            }
            finally
            {
                this.updatingKeyRange = false;
            }

            this.pictureBox_editor.Invalidate();
        }

        private EditorSnapshot CaptureSnapshot() => new(
            [.. this.Track.Notes],
            this.Track.LengthTicks,
            this.numericUpDown_bpm.Value,
            this.numericUpDown_pitchFrequency.Value,
            this.numericUpDown_resolution.Value,
            this.LowestNote,
            this.HighestNote,
            this.noteSelection,
            this.noteSelectionNotes is null ? null : [.. this.noteSelectionNotes]);

        private void BeginHistoryTransaction()
        {
            this.pendingHistorySnapshot ??= this.CaptureSnapshot();
        }

        private void CommitHistoryTransaction()
        {
            if (this.pendingHistorySnapshot is not EditorSnapshot snapshot)
            {
                return;
            }

            this.pendingHistorySnapshot = null;
            EditorSnapshot current = this.CaptureSnapshot();
            if (SnapshotContentEqual(snapshot, current))
            {
                return;
            }

            this.PushUndo(snapshot);
        }

        private void PushCurrentSnapshot()
        {
            this.pendingHistorySnapshot = null;
            this.PushUndo(this.CaptureSnapshot());
        }

        private void PushUndo(EditorSnapshot snapshot)
        {
            this.undoHistory.Add(snapshot);
            while (this.undoHistory.Count > MaximumHistoryDepth)
            {
                this.undoHistory.RemoveAt(0);
            }

            this.redoHistory.Clear();
        }

        private static bool SnapshotContentEqual(EditorSnapshot first, EditorSnapshot second)
        {
            if (first.LengthTicks != second.LengthTicks || first.Notes.Count != second.Notes.Count)
            {
                return false;
            }

            for (int index = 0; index < first.Notes.Count; index++)
            {
                MidiNoteData a = first.Notes[index];
                MidiNoteData b = second.Notes[index];
                if (a.NoteNumber != b.NoteNumber
                    || a.Channel != b.Channel
                    || a.Velocity != b.Velocity
                    || a.StartTick != b.StartTick
                    || a.DurationTicks != b.DurationTicks)
                {
                    return false;
                }
            }

            return true;
        }

        private void Undo()
        {
            if (this.undoHistory.Count == 0)
            {
                return;
            }

            _ = this.StopPlaybackAsync();
            EditorSnapshot current = this.CaptureSnapshot();
            EditorSnapshot previous = this.undoHistory[^1];
            this.undoHistory.RemoveAt(this.undoHistory.Count - 1);
            this.redoHistory.Add(current);
            while (this.redoHistory.Count > MaximumHistoryDepth)
            {
                this.redoHistory.RemoveAt(0);
            }

            this.RestoreSnapshot(previous);
        }

        private void Redo()
        {
            if (this.redoHistory.Count == 0)
            {
                return;
            }

            _ = this.StopPlaybackAsync();
            EditorSnapshot current = this.CaptureSnapshot();
            EditorSnapshot next = this.redoHistory[^1];
            this.redoHistory.RemoveAt(this.redoHistory.Count - 1);
            this.undoHistory.Add(current);
            while (this.undoHistory.Count > MaximumHistoryDepth)
            {
                this.undoHistory.RemoveAt(0);
            }

            this.RestoreSnapshot(next);
        }

        private void RestoreSnapshot(EditorSnapshot snapshot)
        {
            MidiTrackData track = new()
            {
                Index = this.Track.Index,
                Name = this.Track.Name
            };
            track.Notes.AddRange(snapshot.Notes);
            track.ExtendLengthTo(snapshot.LengthTicks);
            List<MidiTrackData> tracks = [.. this.midiFile.Tracks];
            tracks[this.selectedTrackPosition] = track;
            this.midiFile = MidiFileData.CreateGenerated(
                tracks,
                this.midiFile.TicksPerQuarterNote,
                (double)snapshot.Bpm,
                this.midiFile.FilePath,
                pitchFrequency: (double)snapshot.PitchFrequency,
                timeSignatureNumerator: this.midiFile.TimeSignatureNumerator,
                timeSignatureDenominator: this.midiFile.TimeSignatureDenominator);

            this.restoringHistory = true;
            try
            {
                this.numericUpDown_bpm.Value = snapshot.Bpm;
                this.numericUpDown_pitchFrequency.Value = snapshot.PitchFrequency;
                this.numericUpDown_resolution.Value = snapshot.Resolution;
                this.domainUpDown_minKey.SelectedIndex = KeyIndexFromMidiNote(snapshot.LowestNote);
                this.domainUpDown_maxKey.SelectedIndex = KeyIndexFromMidiNote(snapshot.HighestNote);
                this.noteSelection = snapshot.Selection;
                this.noteSelectionNotes = snapshot.SelectedNotes is null
                    ? null
                    : new HashSet<MidiNoteData>(snapshot.SelectedNotes);
            }
            finally
            {
                this.restoringHistory = false;
            }

            long visibleLength = Math.Max(this.GridTicks, this.viewEndTick - this.viewStartTick);
            this.editorLengthTicks = Math.Max(
                this.GetRequiredEditorLength(),
                Math.Max(snapshot.LengthTicks, this.DefaultViewLengthTicks));
            this.SetView(this.viewStartTick, this.viewStartTick + visibleLength);
            this.pixelsPerTick = this.CalculatePixelsPerTick(this.viewEndTick - this.viewStartTick);
            this.UpdateScrollBar();
            this.pictureBox_editor.Invalidate();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            bool control = (keyData & Keys.Control) == Keys.Control;
            bool shift = (keyData & Keys.Shift) == Keys.Shift;
            if (control && key == Keys.Z)
            {
                if (shift)
                {
                    this.Redo();
                }
                else
                {
                    this.Undo();
                }

                return true;
            }

            if (control && key == Keys.Y)
            {
                this.Redo();
                return true;
            }

            if (key == Keys.Delete
                && this.noteSelection is not null
                && this.ActiveControl is not (NumericUpDown or DomainUpDown or TextBoxBase or ComboBox))
            {
                this.DeleteSelectedNotes();
                return true;
            }

            if (key == Keys.Escape && this.ghostPreviewActive)
            {
                this.CancelGhostPreview();
                return true;
            }

            if (key == Keys.Escape && this.pasteHoldTracking)
            {
                this.pasteHoldTracking = false;
                this.pasteHoldKeyCode = Keys.None;
                this.pasteHoldTimer.Stop();
                return true;
            }

            if (key == Keys.Escape && this.noteSelection is not null)
            {
                this.noteSelection = null;
                this.noteSelectionNotes = null;
                this.pictureBox_editor.Invalidate();
                return true;
            }

            if (control && key == Keys.C)
            {
                this.CopySelectedNotes();
                return true;
            }

            if (control && key == Keys.X)
            {
                this.CutSelectedNotes();
                return true;
            }

            if (control && key == Keys.V)
            {
                if (!this.pasteHoldTracking && !this.ghostPreviewActive)
                {
                    this.pasteHoldTracking = true;
                    this.pasteHoldKeyCode = key;
                    this.pasteHoldTimer.Stop();
                    this.pasteHoldTimer.Start();
                }

                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void CustomMidiEditorDialog_KeyUp(object? sender, KeyEventArgs e)
        {
            if (!this.pasteHoldTracking || e.KeyCode != this.pasteHoldKeyCode)
            {
                return;
            }

            this.pasteHoldTracking = false;
            this.pasteHoldKeyCode = Keys.None;
            bool heldForHistory = !this.pasteHoldTimer.Enabled;
            this.pasteHoldTimer.Stop();
            if (!heldForHistory && this.clipboardHistory.Count > 0)
            {
                this.StartGhostPreview(this.clipboardHistory[0].Notes, addToHistory: false);
            }
        }

        private void pasteHoldTimer_Tick(object? sender, EventArgs e)
        {
            this.pasteHoldTimer.Stop();
            if (!this.pasteHoldTracking)
            {
                return;
            }

            this.pasteHoldTracking = false;
            this.pasteHoldKeyCode = Keys.None;
            this.ShowClipboardHistory();
        }

        private void CopySelectedNotes()
        {
            List<MidiNoteData> selected = this.GetSelectedNotes();
            if (selected.Count == 0)
            {
                return;
            }

            this.StoreClipboardHistory(selected);
        }

        private void CutSelectedNotes()
        {
            List<MidiNoteData> selected = this.GetSelectedNotes();
            if (selected.Count == 0)
            {
                return;
            }

            this.StoreClipboardHistory(selected);
            this.BeginHistoryTransaction();
            HashSet<MidiNoteData> removal = selected.ToHashSet();
            List<MidiNoteData> remaining = this.Track.Notes.Where(note => !removal.Contains(note)).ToList();
            MidiTrackData replacement = new() { Index = this.Track.Index, Name = this.Track.Name };
            replacement.Notes.AddRange(remaining);
            List<MidiTrackData> tracks = [.. this.midiFile.Tracks];
            tracks[this.selectedTrackPosition] = replacement;
            this.midiFile = MidiFileData.CreateGenerated(
                tracks,
                this.midiFile.TicksPerQuarterNote,
                this.Bpm,
                this.midiFile.FilePath,
                pitchFrequency: this.PitchFrequency,
                timeSignatureNumerator: this.midiFile.TimeSignatureNumerator,
                timeSignatureDenominator: this.midiFile.TimeSignatureDenominator);
            this.noteSelection = null;
            this.noteSelectionNotes = null;
            this.editorLengthTicks = Math.Max(this.DefaultViewLengthTicks, this.GetRequiredEditorLength());
            this.SetView(this.viewStartTick, this.viewEndTick);
            this.CommitHistoryTransaction();
            this.UpdateScrollBar();
            this.pictureBox_editor.Invalidate();
        }

        private void DeleteSelectedNotes()
        {
            List<MidiNoteData> selected = this.GetSelectedNotes();
            if (selected.Count == 0)
            {
                this.noteSelection = null;
                this.noteSelectionNotes = null;
                this.pictureBox_editor.Invalidate();
                return;
            }

            long previousLength = this.Track.LengthTicks;
            this.BeginHistoryTransaction();
            HashSet<MidiNoteData> removal = selected.ToHashSet();
            this.Track.Notes.RemoveAll(removal.Contains);
            this.noteSelection = null;
            this.noteSelectionNotes = null;
            this.UpdateEditorLengthAfterNoteChange(previousLength);
            this.CommitHistoryTransaction();
            this.pictureBox_editor.Invalidate();
        }

        private List<MidiNoteData> GetSelectedNotes()
        {
            if (this.noteSelection is not MidiSelection selection)
            {
                return [];
            }

            if (this.noteSelectionNotes is HashSet<MidiNoteData> selectedNotes)
            {
                return this.Track.Notes.Where(selectedNotes.Contains).ToList();
            }

            return this.Track.Notes.Where(note => this.IsNoteSelected(note)).ToList();
        }

        private void SetNoteSelection(IEnumerable<MidiNoteData> notes)
        {
            List<MidiNoteData> selected = notes.ToList();
            if (selected.Count == 0)
            {
                this.noteSelection = null;
                this.noteSelectionNotes = null;
                return;
            }

            long startTick = selected.Min(note => note.StartTick);
            long endTick = Math.Max(startTick + this.GridTicks,
                selected.Max(note => note.StartTick + note.DurationTicks));
            this.noteSelection = new MidiSelection(
                startTick,
                endTick,
                selected.Min(note => note.NoteNumber),
                selected.Max(note => note.NoteNumber));
            this.noteSelectionNotes = new HashSet<MidiNoteData>(selected);
        }

        private bool ToggleNoteSelection(MidiNoteData note)
        {
            HashSet<MidiNoteData> selected = new(this.GetSelectedNotes());
            bool selectedNow = selected.Add(note);
            if (!selectedNow)
            {
                selected.Remove(note);
            }

            this.SetNoteSelection(selected);
            return selectedNow;
        }

        internal static MidiNoteData? GetSingleNoteForPrehear(IReadOnlyList<MidiNoteData> notes) =>
            notes.Count == 1 ? notes[0] : null;

        private async Task PrehearNoteIfEnabledAsync(MidiNoteData note)
        {
            if (!this.checkBox_preview.Checked)
            {
                return;
            }

            try
            {
                await this.PreviewNoteAsync(note);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (!this.IsDisposed)
                {
                    WindowMainStaticHelpers.ShowErrorWithCopyButton(this, "MIDI note preview failed", ex);
                }
            }
        }

        private void StoreClipboardHistory(IEnumerable<MidiNoteData> notes)
        {
            List<MidiNoteData> copied = NormalizeNotes(notes);
            if (copied.Count == 0)
            {
                return;
            }

            for (int index = this.clipboardHistory.Count - 1; index >= 0; index--)
            {
                if (NotesEqual(this.clipboardHistory[index].Notes, copied))
                {
                    this.clipboardHistory.RemoveAt(index);
                }
            }

            this.clipboardHistory.Insert(0, new MidiClipboardEntry(copied, DateTime.Now));
            while (this.clipboardHistory.Count > MaxClipboardHistoryEntries)
            {
                this.clipboardHistory.RemoveAt(this.clipboardHistory.Count - 1);
            }
        }

        private static List<MidiNoteData> NormalizeNotes(IEnumerable<MidiNoteData> notes)
        {
            MidiNoteData[] source = notes.ToArray();
            if (source.Length == 0)
            {
                return [];
            }

            long minimumTick = source.Min(note => note.StartTick);
            int minimumNote = source.Min(note => note.NoteNumber);
            return source.Select(note => new MidiNoteData
            {
                NoteNumber = note.NoteNumber - minimumNote,
                Channel = note.Channel,
                Velocity = note.Velocity,
                StartTick = note.StartTick - minimumTick,
                DurationTicks = Math.Max(1, note.DurationTicks)
            }).OrderBy(note => note.StartTick).ThenBy(note => note.NoteNumber).ToList();
        }

        private static bool NotesEqual(IReadOnlyList<MidiNoteData> first, IReadOnlyList<MidiNoteData> second) =>
            first.Count == second.Count && first.Zip(second).All(pair =>
                pair.First.NoteNumber == pair.Second.NoteNumber
                && pair.First.Channel == pair.Second.Channel
                && pair.First.Velocity == pair.Second.Velocity
                && pair.First.StartTick == pair.Second.StartTick
                && pair.First.DurationTicks == pair.Second.DurationTicks);

        private void ShowClipboardHistory()
        {
            if (this.clipboardHistory.Count == 0)
            {
                return;
            }

            using MidiClipboardHistoryDialog dialog = new(this.clipboardHistory);
            Point cursor = Cursor.Position;
            Rectangle workArea = Screen.GetWorkingArea(this);
            dialog.StartPosition = FormStartPosition.Manual;
            dialog.Location = new Point(
                Math.Clamp(cursor.X - 60, workArea.Left, Math.Max(workArea.Left, workArea.Right - dialog.Width)),
                Math.Clamp(cursor.Y - 60, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - dialog.Height)));
            if (dialog.ShowDialog(this) != DialogResult.OK || dialog.SelectedEntry is not MidiClipboardEntry chosen)
            {
                return;
            }

            this.clipboardHistory.Remove(chosen);
            this.clipboardHistory.Insert(0, chosen);
            this.StartGhostPreview(chosen.Notes, addToHistory: false);
        }

        private void StartGhostPreview(IEnumerable<MidiNoteData> notes, bool addToHistory)
        {
            List<MidiNoteData> normalized = NormalizeNotes(notes);
            if (normalized.Count == 0)
            {
                return;
            }

            if (addToHistory)
            {
                this.StoreClipboardHistory(normalized);
            }

            this.ghostNotes = normalized;
            this.ghostPreviewActive = true;
            this.UpdateGhostPreview(this.hasEditorPointer
                ? this.lastEditorPointer
                : new Point(KeyLabelWidth, 0));
            this.pictureBox_editor.Focus();
            this.pictureBox_editor.Invalidate();
        }

        private IEnumerable<MidiNoteData> GetGhostNotesAtPointer()
        {
            if (this.ghostNotes.Count == 0)
            {
                return [];
            }

            Point pointer = this.hasEditorPointer ? this.lastEditorPointer : new Point(KeyLabelWidth, 0);
            long anchorTick = this.PointToTick(pointer.X);
            int minPitch = this.ghostNotes.Min(note => note.NoteNumber);
            int maxPitch = this.ghostNotes.Max(note => note.NoteNumber);
            int anchorPitch = this.PointToNote(pointer.Y);
            anchorPitch = Math.Clamp(anchorPitch, minPitch, 127 - maxPitch + minPitch);
            return this.ghostNotes.Select(note => new MidiNoteData
            {
                NoteNumber = anchorPitch + note.NoteNumber - minPitch,
                Channel = note.Channel,
                Velocity = note.Velocity,
                StartTick = anchorTick + note.StartTick,
                DurationTicks = note.DurationTicks
            }).ToArray();
        }

        private void UpdateGhostPreview(Point pointer)
        {
            this.lastEditorPointer = this.ClampToEditor(pointer);
            this.hasEditorPointer = true;
            this.ghostPreviewValid = this.GetGhostNotesAtPointer().All(note =>
                note.NoteNumber is >= 0 and <= 127 && note.StartTick >= 0 && note.DurationTicks > 0);
        }

        private async void PlaceGhostPreview()
        {
            if (!this.ghostPreviewActive || !this.ghostPreviewValid)
            {
                return;
            }

            List<MidiNoteData> placed = this.GetGhostNotesAtPointer().ToList();
            this.BeginHistoryTransaction();
            this.Track.Notes.AddRange(placed);
            this.Track.ExtendLengthTo(placed.Max(note => note.StartTick + note.DurationTicks));
            this.ghostPreviewActive = false;
            this.ghostPreviewValid = false;
            this.ghostNotes.Clear();
            this.CommitHistoryTransaction();
            this.SetNoteSelection(placed);
            this.editorLengthTicks = Math.Max(this.editorLengthTicks, this.GetRequiredEditorLength());
            this.SetView(this.viewStartTick, this.viewEndTick);
            this.UpdateScrollBar();
            this.pictureBox_editor.Invalidate();
            MidiNoteData? noteToPrehear = GetSingleNoteForPrehear(placed);
            if (noteToPrehear is not null)
            {
                await this.PrehearNoteIfEnabledAsync(noteToPrehear);
            }
        }

        private void CancelGhostPreview()
        {
            // Remix/generate ghosts are already stored when created; cancelling never loses them.
            this.ghostPreviewActive = false;
            this.ghostPreviewValid = false;
            this.ghostNotes.Clear();
            this.pictureBox_editor.Invalidate();
        }

        private void TrackPointer(object? sender, MouseEventArgs e)
        {
            this.lastEditorPointer = e.Location;
            this.hasEditorPointer = true;
            if (this.ghostPreviewActive)
            {
                this.UpdateGhostPreview(e.Location);
                this.pictureBox_editor.Invalidate();
            }
        }

        private void pictureBox_editor_Paint(object? sender, PaintEventArgs e)
        {
            int lowest = this.LowestNote;
            int highest = Math.Max(lowest, this.HighestNote);
            int noteCount = highest - lowest + 1;
            int width = Math.Max(1, this.pictureBox_editor.ClientSize.Width - KeyLabelWidth);
            int height = this.PianoRollHeight;
            int pianoTop = TopMargin;
            int pianoBottom = pianoTop + height;
            long lengthTicks = Math.Max(this.GridTicks, this.editorLengthTicks);
            long visibleStartTick = Math.Clamp(this.viewStartTick, 0, Math.Max(0, lengthTicks - this.GridTicks));
            long visibleLengthTicks = Math.Clamp(this.viewEndTick - this.viewStartTick, this.GridTicks, lengthTicks);
            long visibleEndTick = Math.Min(lengthTicks, visibleStartTick + visibleLengthTicks);

            e.Graphics.SmoothingMode = SmoothingMode.HighQuality;
            e.Graphics.Clear(Color.FromArgb(24, 24, 28));

            using Pen gridPen = new(Color.FromArgb(48, 48, 56));
            using Pen beatPen = new(Color.FromArgb(64, 64, 74));
            using Pen barPen = new(Color.FromArgb(92, 92, 106));
            using Font font = new("Segoe UI", 8f);
            for (int note = lowest; note <= highest; note++)
            {
                float y = NoteToY(note, lowest, noteCount, height, pianoTop);
                e.Graphics.DrawLine(note % 12 == 0 ? barPen : gridPen, KeyLabelWidth, y, KeyLabelWidth + width, y);
                e.Graphics.DrawString(MidiNoteName(note), font, Brushes.Gainsboro, 2, y - 7);
            }

            long firstGridTick = visibleStartTick - visibleStartTick % this.GridTicks;
            long ticksPerBar = Math.Max(1, this.midiFile.TicksPerBar);
            long ticksPerQuarter = Math.Max(1, this.midiFile.TicksPerQuarterNote);
            for (long tick = firstGridTick; tick <= visibleEndTick; tick += this.GridTicks)
            {
                float x = KeyLabelWidth + (float)((tick - visibleStartTick) * this.pixelsPerTick);
                Pen pen = tick % ticksPerBar == 0 ? barPen : tick % ticksPerQuarter == 0 ? beatPen : gridPen;
                e.Graphics.DrawLine(pen, x, pianoTop, x, pianoBottom);
            }

            if (this.noteSelection is MidiSelection noteSelection)
            {
                float selectionX = KeyLabelWidth + (float)((noteSelection.StartTick - visibleStartTick) * this.pixelsPerTick);
                float selectionEndX = KeyLabelWidth + (float)((noteSelection.EndTick - visibleStartTick) * this.pixelsPerTick);
                float top = NoteToY(noteSelection.HighestNote, lowest, noteCount, height, pianoTop);
                float bottom = NoteToY(noteSelection.LowestNote, lowest, noteCount, height, pianoTop)
                    + Math.Max(3, height / (float)noteCount - 1);
                RectangleF selectedBounds = RectangleF.FromLTRB(
                    Math.Max(KeyLabelWidth, selectionX),
                    Math.Max(pianoTop, top),
                    Math.Min(KeyLabelWidth + width, selectionEndX),
                    Math.Min(pianoBottom, bottom));
                if (selectedBounds.Width > 0 && selectedBounds.Height > 0)
                {
                    using Brush selectionBrush = new SolidBrush(Color.FromArgb(26, Color.DeepSkyBlue));
                    using Pen selectionPen = new(Color.FromArgb(180, Color.DeepSkyBlue)) { DashStyle = DashStyle.Dash };
                    e.Graphics.FillRectangle(selectionBrush, selectedBounds);
                    e.Graphics.DrawRectangle(selectionPen, selectedBounds.X, selectedBounds.Y, selectedBounds.Width, selectedBounds.Height);
                }
            }

            using Brush noteBrush = new SolidBrush(Color.DeepSkyBlue);
            using Brush selectedNoteBrush = new SolidBrush(Color.FromArgb(245, 180, 95));
            foreach (MidiNoteData note in this.Track.Notes)
            {
                if (note.NoteNumber < lowest || note.NoteNumber > highest)
                {
                    continue;
                }

                long noteEndTick = note.StartTick + note.DurationTicks;
                if (noteEndTick <= visibleStartTick || note.StartTick >= visibleEndTick)
                {
                    continue;
                }

                long clippedStartTick = Math.Max(note.StartTick, visibleStartTick);
                long clippedEndTick = Math.Min(noteEndTick, visibleEndTick);
                float x = KeyLabelWidth + (float)((clippedStartTick - visibleStartTick) * this.pixelsPerTick);
                float noteWidth = Math.Max(2, (float)((clippedEndTick - clippedStartTick) * this.pixelsPerTick));
                float y = NoteToY(note.NoteNumber, lowest, noteCount, height, pianoTop);
                float noteHeight = Math.Max(3, height / (float)noteCount - 1);
                Brush brush = this.IsNoteSelected(note) ? selectedNoteBrush : noteBrush;
                e.Graphics.FillRectangle(brush, x, y, Math.Min(noteWidth, KeyLabelWidth + width - x), noteHeight);
            }

            if (this.selectionDragStart is Point selectionStart && this.selectionDragEnd is Point selectionEnd)
            {
                Rectangle selectionRectangle = NormalizeRectangle(selectionStart, selectionEnd);
                using Brush selectionBrush = new SolidBrush(Color.FromArgb(45, Color.DeepSkyBlue));
                using Pen selectionPen = new(Color.DeepSkyBlue, 1f) { DashStyle = DashStyle.Dash };
                e.Graphics.FillRectangle(selectionBrush, selectionRectangle);
                e.Graphics.DrawRectangle(selectionPen, selectionRectangle);
            }

            if (this.rectangleSelectionStart is Point rectangleStart && this.rectangleSelectionEnd is Point rectangleEnd)
            {
                Rectangle selectionRectangle = NormalizeRectangle(rectangleStart, rectangleEnd);
                using Pen selectionPen = new(Color.White, 1f) { DashStyle = DashStyle.Dash };
                e.Graphics.DrawRectangle(selectionPen, selectionRectangle);
            }

            if (this.ghostPreviewActive)
            {
                using Brush ghostBrush = new SolidBrush(this.ghostPreviewValid
                    ? Color.FromArgb(155, Color.MediumAquamarine)
                    : Color.FromArgb(155, Color.IndianRed));
                using Pen ghostPen = new(this.ghostPreviewValid ? Color.MediumAquamarine : Color.IndianRed)
                {
                    DashStyle = DashStyle.Dash
                };
                foreach (MidiNoteData note in this.GetGhostNotesAtPointer())
                {
                    if (note.NoteNumber < lowest || note.NoteNumber > highest)
                    {
                        continue;
                    }

                    long endTick = note.StartTick + note.DurationTicks;
                    if (endTick <= visibleStartTick || note.StartTick >= visibleEndTick)
                    {
                        continue;
                    }

                    float x = KeyLabelWidth + (float)((Math.Max(note.StartTick, visibleStartTick) - visibleStartTick) * this.pixelsPerTick);
                    float noteWidth = Math.Max(2, (float)((Math.Min(endTick, visibleEndTick) - Math.Max(note.StartTick, visibleStartTick)) * this.pixelsPerTick));
                    float y = NoteToY(note.NoteNumber, lowest, noteCount, height, pianoTop);
                    float noteHeight = Math.Max(3, height / (float)noteCount - 1);
                    RectangleF rect = new(x, y, Math.Min(noteWidth, KeyLabelWidth + width - x), noteHeight);
                    e.Graphics.FillRectangle(ghostBrush, rect);
                    e.Graphics.DrawRectangle(ghostPen, rect.X, rect.Y, rect.Width, rect.Height);
                }
            }

            if (this.previewCaretVisible && this.previewAudio?.PlayerPlaying == true && this.previewAudio.Duration > TimeSpan.Zero)
            {
                double secondsPerTick = 60.0 / Math.Max(1.0, this.Bpm) / Math.Max(1, this.midiFile.TicksPerQuarterNote);
                long previewTick = this.previewStartTick
                    + (long)Math.Round(this.previewAudio.CurrentTime.TotalSeconds / secondsPerTick);
                previewTick = Math.Clamp(previewTick, 0, Math.Max(this.GridTicks, this.Track.LengthTicks));
                float caretX = KeyLabelWidth + (float)((previewTick - visibleStartTick) * this.pixelsPerTick);
                if (caretX >= KeyLabelWidth && caretX <= KeyLabelWidth + width)
                {
                    using Pen caretPen = new(Color.Red, 2f);
                    e.Graphics.DrawLine(caretPen, caretX, pianoTop, caretX, pianoBottom);
                }
            }

            this.DrawRulerLabels(e.Graphics, visibleStartTick, visibleEndTick, width, pianoBottom);
        }

        private void DrawRulerLabels(Graphics graphics, long visibleStartTick, long visibleEndTick, int width, int pianoBottom)
        {
            using Font font = new("Segoe UI", 8f);
            using Pen rulerPen = new(Color.FromArgb(92, 92, 106));
            long ticksPerBar = Math.Max(1, this.midiFile.TicksPerBar);
            int barStride = GetBarLabelStride(ticksPerBar * this.pixelsPerTick);
            long firstMeasure = Math.Max(1, (long)Math.Ceiling(visibleStartTick / (double)ticksPerBar) + 1);
            long lastMeasure = (long)Math.Floor(visibleEndTick / (double)ticksPerBar) + 1;
            long firstLabeledMeasure = ((firstMeasure + barStride - 1) / barStride) * barStride;
            if (firstLabeledMeasure > lastMeasure)
            {
                firstLabeledMeasure = firstMeasure;
            }

            for (long measure = firstLabeledMeasure; measure <= lastMeasure; measure += barStride)
            {
                double tick = (measure - 1d) * ticksPerBar;
                float x = KeyLabelWidth + (float)((tick - visibleStartTick) * this.pixelsPerTick);
                if (x >= KeyLabelWidth && x <= KeyLabelWidth + width)
                {
                    graphics.DrawLine(rulerPen, x, TopMargin - 4, x, TopMargin);
                    DrawRulerLabel(
                        graphics,
                        measure.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        font,
                        x,
                        0,
                        TopMargin,
                        KeyLabelWidth,
                        KeyLabelWidth + width);
                }

                if (measure > long.MaxValue - barStride)
                {
                    break;
                }
            }

            double secondsPerTick = 60.0 / Math.Max(1.0, this.Bpm) / Math.Max(1, this.midiFile.TicksPerQuarterNote);
            double pixelsPerSecond = this.pixelsPerTick / secondsPerTick;
            double timeInterval = GetTimeLabelIntervalSeconds(pixelsPerSecond);
            double startSeconds = visibleStartTick * secondsPerTick;
            double endSeconds = visibleEndTick * secondsPerTick;
            double seconds = Math.Ceiling(startSeconds / timeInterval) * timeInterval;
            for (; seconds <= endSeconds + 1e-9; seconds += timeInterval)
            {
                float x = KeyLabelWidth + (float)((seconds / secondsPerTick - visibleStartTick) * this.pixelsPerTick);
                if (x >= KeyLabelWidth && x <= KeyLabelWidth + width)
                {
                    graphics.DrawLine(rulerPen, x, pianoBottom, x, pianoBottom + 4);
                    DrawRulerLabel(
                        graphics,
                        FormatTimeRulerLabel(seconds),
                        font,
                        x,
                        pianoBottom,
                        BottomMargin,
                        KeyLabelWidth,
                        KeyLabelWidth + width);
                }
            }
        }

        private static void DrawRulerLabel(
            Graphics graphics,
            string text,
            Font font,
            float centerX,
            int top,
            int height,
            int leftEdge,
            int rightEdge)
        {
            Size measured = TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.NoPadding);
            int labelWidth = Math.Min(Math.Max(1, rightEdge - leftEdge), measured.Width + 6);
            int labelHeight = Math.Min(height, measured.Height + 2);
            int left = Math.Clamp(
                (int)Math.Round(centerX - labelWidth / 2.0),
                leftEdge,
                Math.Max(leftEdge, rightEdge - labelWidth));
            Rectangle bounds = new(left, top + Math.Max(0, (height - labelHeight) / 2), labelWidth, labelHeight);
            using Brush background = new SolidBrush(Color.FromArgb(24, 24, 28));
            graphics.FillRectangle(background, bounds);
            TextRenderer.DrawText(
                graphics,
                text,
                font,
                bounds,
                Color.Gainsboro,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        internal static int GetBarLabelStride(double pixelsPerBar)
        {
            if (!double.IsFinite(pixelsPerBar) || pixelsPerBar <= 0)
            {
                return 1;
            }

            int stride = 1;
            while (pixelsPerBar * stride < MinimumBarLabelSpacing && stride < 1 << 20)
            {
                stride *= 2;
            }

            return stride;
        }

        internal static double GetTimeLabelIntervalSeconds(double pixelsPerSecond)
        {
            if (!double.IsFinite(pixelsPerSecond) || pixelsPerSecond <= 0)
            {
                return 60;
            }

            double requiredInterval = Math.Max(1, MinimumTimeLabelSpacing / pixelsPerSecond);
            double magnitude = Math.Pow(10, Math.Floor(Math.Log10(requiredInterval)));
            foreach (double factor in new[] { 1, 2, 5, 10 })
            {
                double interval = factor * magnitude;
                if (interval >= requiredInterval)
                {
                    return interval;
                }
            }

            return 10 * magnitude;
        }

        internal static string FormatTimeRulerLabel(double seconds)
        {
            if (!double.IsFinite(seconds) || seconds < 0)
            {
                seconds = 0;
            }

            double roundedSeconds = Math.Round(seconds);
            long totalSeconds = roundedSeconds >= long.MaxValue ? long.MaxValue : (long)roundedSeconds;
            return string.Concat(
                (totalSeconds / 60).ToString(System.Globalization.CultureInfo.InvariantCulture),
                ":",
                (totalSeconds % 60).ToString("00", System.Globalization.CultureInfo.InvariantCulture));
        }

        private void pictureBox_editor_MouseDown(object? sender, MouseEventArgs e)
        {
            this.lastEditorPointer = e.Location;
            this.hasEditorPointer = true;
            this.pendingPlayFromHerePoint = null;
            if (this.ghostPreviewActive)
            {
                if (e.Button == MouseButtons.Left && this.ghostPreviewValid)
                {
                    this.PlaceGhostPreview();
                }
                else if (e.Button == MouseButtons.Right)
                {
                    this.CancelGhostPreview();
                }

                return;
            }

            if (e.Button is not (MouseButtons.Left or MouseButtons.Right))
            {
                return;
            }

            if ((ModifierKeys & Keys.Control) == Keys.None && !this.IsPianoRollPoint(e.Location))
            {
                return;
            }

            if (e.Button == MouseButtons.Right && (ModifierKeys & Keys.Shift) != Keys.None)
            {
                this.rectangleSelectionStart = this.ClampToEditor(e.Location);
                this.rectangleSelectionEnd = this.rectangleSelectionStart;
                this.editButton = MouseButtons.Right;
                this.editPreviousLength = this.editorLengthTicks;
                this.BeginHistoryTransaction();
                this.pictureBox_editor.Capture = true;
                this.pictureBox_editor.Invalidate();
                return;
            }

            if (e.Button == MouseButtons.Left
                && (ModifierKeys & Keys.Control) != Keys.None
                && (ModifierKeys & Keys.Shift) == Keys.None
                && this.IsPianoRollPoint(e.Location))
            {
                Point point = this.ClampToEditor(e.Location);
                MidiNoteData? clickedNote = this.FindNoteAt(point);
                if (clickedNote is not null)
                {
                    bool selected = this.ToggleNoteSelection(clickedNote);
                    this.pictureBox_editor.Invalidate();
                    if (selected)
                    {
                        _ = this.PrehearNoteIfEnabledAsync(clickedNote);
                    }

                    return;
                }
            }

            if ((ModifierKeys & Keys.Control) != Keys.None)
            {
                this.panning = true;
                this.panStart = e.Location;
                this.panViewStartTick = this.viewStartTick;
                this.panViewEndTick = this.viewEndTick;
                this.pictureBox_editor.Capture = true;
                this.pictureBox_editor.Cursor = Cursors.SizeWE;
                return;
            }

            if (e.Button == MouseButtons.Left && (ModifierKeys & Keys.Shift) != Keys.None)
            {
                this.BeginSelectionDrag(e.Location);
                return;
            }

            if (e.Button == MouseButtons.Left)
            {
                Point point = this.ClampToEditor(e.Location);
                MidiNoteData? clickedNote = this.FindNoteAt(point);
                if (clickedNote is not null
                    && (this.noteSelection is not MidiSelection || !this.IsPointInSelectionRectangle(point)))
                {
                    this.pendingNoteClickStart = point;
                    this.pendingNoteClick = clickedNote;
                    this.pictureBox_editor.Capture = true;
                    return;
                }
            }

            if (e.Button == MouseButtons.Left
                && this.noteSelection is MidiSelection
                && this.IsPointInSelectionRectangle(e.Location))
            {
                this.selectionMoveStart = this.ClampToEditor(e.Location);
                this.selectionMoveAnchorTick = this.PointToTick(e.Location.X);
                this.selectionMoveAnchorNote = this.PointToNote(e.Location.Y);
                this.selectionMoveOriginalSelection = this.noteSelection;
                this.selectionMoveOriginalNotes = this.GetSelectedNotes();
                this.selectionMoveMovedNotes = [];
                this.selectionMoveDragging = false;
                this.editPreviousLength = this.editorLengthTicks;
                this.pictureBox_editor.Capture = true;
                return;
            }

            if (e.Button == MouseButtons.Right)
            {
                if (!this.IsPianoRollPoint(e.Location))
                {
                    return;
                }

                Point point = this.ClampToEditor(e.Location);
                MidiNoteData? note = this.FindNoteAt(point);
                if (note == null)
                {
                    this.pendingPlayFromHerePoint = point;
                    this.pictureBox_editor.Capture = true;
                    return;
                }

                this.BeginRightErase(point);
                this.Track.Notes.Remove(note);
                this.pictureBox_editor.Invalidate();
                return;
            }

            if (e.Button == MouseButtons.Left)
            {
                this.noteSelection = null;
                this.noteSelectionNotes = null;
            }

            this.editStart = this.ClampToEditor(e.Location);
            this.editButton = e.Button;
            this.editNote = this.PointToNote(this.editStart.Value.Y);
            this.editPreviousLength = this.editorLengthTicks;
            this.BeginHistoryTransaction();
            this.pictureBox_editor.Capture = true;
            this.ApplyNoteRange(this.editStart.Value, this.editStart.Value, e.Button == MouseButtons.Left, updateView: false);
            this.pictureBox_editor.Invalidate();
        }

        private void pictureBox_editor_MouseMove(object? sender, MouseEventArgs e)
        {
            this.lastEditorPointer = e.Location;
            this.hasEditorPointer = true;
            if (this.ghostPreviewActive)
            {
                this.UpdateGhostPreview(e.Location);
                this.pictureBox_editor.Invalidate();
                return;
            }

            if (this.pendingPlayFromHerePoint is Point pendingPoint)
            {
                if (!HasExceededDragThreshold(pendingPoint, e.Location))
                {
                    return;
                }

                this.pendingPlayFromHerePoint = null;
                this.BeginRightErase(pendingPoint);
            }

            if (this.pendingNoteClickStart is Point noteClickStart && this.pendingNoteClick is not null)
            {
                if (!HasExceededDragThreshold(noteClickStart, e.Location))
                {
                    return;
                }

                this.pendingNoteClickStart = null;
                this.pendingNoteClick = null;
                this.BeginNoteDrawing(noteClickStart);
            }

            if (this.panning)
            {
                this.PanView(e.Location.X - this.panStart.X);
                return;
            }

            if (this.selectionMoveStart is Point
                && this.selectionMoveOriginalSelection is MidiSelection
                && this.selectionMoveOriginalNotes is List<MidiNoteData>)
            {
                if (!this.selectionMoveDragging)
                {
                    if (!HasExceededDragThreshold(this.selectionMoveStart.Value, e.Location))
                    {
                        return;
                    }

                    this.selectionMoveDragging = true;
                    this.BeginHistoryTransaction();
                    this.noteSelectionNotes = new HashSet<MidiNoteData>(this.selectionMoveOriginalNotes);
                    this.pictureBox_editor.Cursor = Cursors.SizeAll;
                }

                this.MoveSelectionTo(e.Location);
                this.pictureBox_editor.Invalidate();
                return;
            }

            if (this.editStart == null)
            {
                if (this.selectionDragStart is Point selectionStart)
                {
                    this.selectionDragEnd = this.ClampToEditor(e.Location);
                    this.pictureBox_editor.Invalidate();
                    return;
                }

                if (this.rectangleSelectionStart is Point && this.editButton == MouseButtons.Right)
                {
                    this.rectangleSelectionEnd = this.ClampToEditor(e.Location);
                    this.pictureBox_editor.Invalidate();
                }

                return;
            }

            Point current = this.ClampToEditor(e.Location);
            if (this.editButton == MouseButtons.Right)
            {
                this.ApplyNoteRange(current, current, add: false, updateView: false);
            }
            else if (this.PointToNote(current.Y) == this.editNote)
            {
                this.ApplyNoteRange(this.editStart.Value, current, add: this.editButton == MouseButtons.Left, updateView: false);
            }

            this.pictureBox_editor.Invalidate();
        }

        private void BeginRightErase(Point point)
        {
            this.editStart = point;
            this.editButton = MouseButtons.Right;
            this.editNote = this.PointToNote(point.Y);
            this.editPreviousLength = this.editorLengthTicks;
            this.BeginHistoryTransaction();
            this.pictureBox_editor.Capture = true;
        }

        private void BeginNoteDrawing(Point point)
        {
            this.noteSelection = null;
            this.noteSelectionNotes = null;
            this.editStart = this.ClampToEditor(point);
            this.editButton = MouseButtons.Left;
            this.editNote = this.PointToNote(this.editStart.Value.Y);
            this.editPreviousLength = this.editorLengthTicks;
            this.BeginHistoryTransaction();
            this.pictureBox_editor.Capture = true;
            this.ApplyNoteRange(this.editStart.Value, this.editStart.Value, add: true, updateView: false);
            this.pictureBox_editor.Invalidate();
        }

        private void BeginSelectionDrag(Point point)
        {
            this.BeginHistoryTransaction();
            this.noteSelection = null;
            this.noteSelectionNotes = null;
            this.selectionMoveStart = null;
            this.selectionMoveOriginalSelection = null;
            this.selectionMoveOriginalNotes = null;
            this.selectionMoveMovedNotes = null;
            this.selectionMoveDragging = false;
            this.selectionDragStart = this.ClampToEditor(point);
            this.selectionDragEnd = this.selectionDragStart;
            this.pictureBox_editor.Capture = true;
            this.pictureBox_editor.Invalidate();
        }

        private async void pictureBox_editor_MouseUp(object? sender, MouseEventArgs e)
        {
            this.lastEditorPointer = e.Location;
            this.hasEditorPointer = true;
            if (this.ghostPreviewActive)
            {
                return;
            }

            if (this.pendingPlayFromHerePoint is Point pendingPoint)
            {
                this.pendingPlayFromHerePoint = null;
                this.pictureBox_editor.Capture = false;
                if (e.Button == MouseButtons.Right && !HasExceededDragThreshold(pendingPoint, e.Location))
                {
                    this.playFromHereStartTick = this.PointToTick(pendingPoint.X);
                    this.contextMenuStrip_playFromHere.Show(this.pictureBox_editor, pendingPoint);
                }

                return;
            }

            if (this.panning)
            {
                this.panning = false;
                this.pictureBox_editor.Capture = false;
                this.pictureBox_editor.Cursor = Cursors.Cross;
                return;
            }

            if (this.pendingNoteClickStart is Point noteClickStart
                && this.pendingNoteClick is MidiNoteData clickedNote
                && e.Button == MouseButtons.Left)
            {
                this.pendingNoteClickStart = null;
                this.pendingNoteClick = null;
                if (HasExceededDragThreshold(noteClickStart, e.Location))
                {
                    this.BeginNoteDrawing(noteClickStart);
                }
                else
                {
                    this.pictureBox_editor.Capture = false;
                    this.SetNoteSelection([clickedNote]);
                    this.pictureBox_editor.Invalidate();
                    await this.PrehearNoteIfEnabledAsync(clickedNote);
                    return;
                }
            }

            if (this.rectangleSelectionStart is Point rectangleStart && e.Button == MouseButtons.Right)
            {
                Point rectangleEnd = this.ClampToEditor(e.Location);
                this.DeleteNotesInRectangle(rectangleStart, rectangleEnd);
                this.rectangleSelectionStart = null;
                this.rectangleSelectionEnd = null;
                this.pictureBox_editor.Capture = false;
                this.UpdateEditorLengthAfterNoteChange(this.editPreviousLength);
                this.CommitHistoryTransaction();
                this.pictureBox_editor.Invalidate();
                return;
            }

            if (this.selectionDragStart is Point selectionStart && e.Button == MouseButtons.Left)
            {
                Point selectionEnd = this.ClampToEditor(e.Location);
                this.selectionDragStart = null;
                this.selectionDragEnd = null;
                this.pictureBox_editor.Capture = false;
                this.noteSelection = this.CreateSelection(selectionStart, selectionEnd);
                this.noteSelectionNotes = null;
                this.CommitHistoryTransaction();
                this.pictureBox_editor.Invalidate();
                return;
            }

            if (this.selectionMoveStart is Point selectionMoveStart && e.Button == MouseButtons.Left)
            {
                if (this.selectionMoveDragging || HasExceededDragThreshold(selectionMoveStart, e.Location))
                {
                    if (!this.selectionMoveDragging)
                    {
                        this.selectionMoveDragging = true;
                        this.BeginHistoryTransaction();
                        this.noteSelectionNotes = new HashSet<MidiNoteData>(this.selectionMoveOriginalNotes!);
                    }

                    this.MoveSelectionTo(e.Location);
                    this.ClearSelectionMoveState();
                    this.pictureBox_editor.Capture = false;
                    this.pictureBox_editor.Cursor = Cursors.Cross;
                    this.UpdateEditorLengthAfterNoteChange(this.editPreviousLength);
                    this.CommitHistoryTransaction();
                    this.pictureBox_editor.Invalidate();
                    return;
                }

                MidiNoteData? noteAtSelectionStart = this.FindNoteAt(selectionMoveStart);
                this.ClearSelectionMoveState();
                if (noteAtSelectionStart is not null)
                {
                    this.pictureBox_editor.Capture = false;
                    this.pictureBox_editor.Cursor = Cursors.Cross;
                    this.SetNoteSelection([noteAtSelectionStart]);
                    this.pictureBox_editor.Invalidate();
                    await this.PrehearNoteIfEnabledAsync(noteAtSelectionStart);
                    return;
                }

                this.noteSelection = null;
                this.noteSelectionNotes = null;
                this.BeginNoteDrawing(selectionMoveStart);
            }

            if (this.editStart is not Point start || e.Button != this.editButton)
            {
                return;
            }

            Point end = this.ClampToEditor(e.Location);
            this.editStart = null;
            this.pictureBox_editor.Capture = false;
            this.UpdateEditorLengthAfterNoteChange(this.editPreviousLength);
            this.CommitHistoryTransaction();
            this.pictureBox_editor.Invalidate();

            if (this.editButton == MouseButtons.Left)
            {
                Point previewPoint = new((start.X + end.X) / 2, start.Y);
                MidiNoteData? previewNote = this.FindNoteAt(previewPoint);
                if (previewNote != null)
                {
                    this.SetNoteSelection([previewNote]);
                    await this.PrehearNoteIfEnabledAsync(previewNote);
                }
            }
        }

        private void ClearSelectionMoveState()
        {
            this.selectionMoveStart = null;
            this.selectionMoveOriginalSelection = null;
            this.selectionMoveOriginalNotes = null;
            this.selectionMoveMovedNotes = null;
            this.selectionMoveDragging = false;
        }

        private void ApplyNoteRange(Point first, Point second, bool add, bool updateView)
        {
            int note = Math.Clamp(this.PointToNote(first.Y), this.LowestNote, this.HighestNote);
            long startTick = this.PointToTick(Math.Min(first.X, second.X));
            long endTick = Math.Max(startTick + this.GridTicks, this.PointToTick(Math.Max(first.X, second.X)) + this.GridTicks);
            long previousLength = this.Track.LengthTicks;

            this.Track.Notes.RemoveAll(candidate =>
            {
                if (candidate.NoteNumber != note)
                {
                    return false;
                }

                bool overlaps = candidate.StartTick < endTick && candidate.StartTick + candidate.DurationTicks > startTick;
                return overlaps;
            });

            if (add)
            {
                this.Track.Notes.Add(new MidiNoteData
                {
                    NoteNumber = note,
                    Channel = 0,
                    Velocity = 100,
                    StartTick = startTick,
                    DurationTicks = endTick - startTick
                });
                this.Track.ExtendLengthTo(endTick);
            }

            if (updateView)
            {
                this.UpdateEditorLengthAfterNoteChange(previousLength);
            }
        }

        private MidiSelection CreateSelection(Point first, Point second)
        {
            long firstTick = this.PointToTick(Math.Min(first.X, second.X));
            long lastTick = this.PointToTick(Math.Max(first.X, second.X));
            long startTick = Math.Max(0, firstTick);
            long endTick = Math.Max(startTick + this.GridTicks, lastTick + this.GridTicks);
            int firstNote = this.PointToNote(first.Y);
            int secondNote = this.PointToNote(second.Y);
            return new MidiSelection(
                startTick,
                endTick,
                Math.Min(firstNote, secondNote),
                Math.Max(firstNote, secondNote));
        }

        private bool IsNoteSelected(MidiNoteData note)
        {
            if (this.noteSelection is not MidiSelection selection)
            {
                return false;
            }

            if (this.noteSelectionNotes is HashSet<MidiNoteData> selectedNotes)
            {
                return selectedNotes.Contains(note);
            }

            return note.NoteNumber >= selection.LowestNote
                && note.NoteNumber <= selection.HighestNote
                && note.StartTick < selection.EndTick
                && note.StartTick + note.DurationTicks > selection.StartTick;
        }

        private bool IsPointInSelectionRectangle(Point point)
        {
            if (this.noteSelection is not MidiSelection selection)
            {
                return false;
            }

            int width = Math.Max(1, this.pictureBox_editor.ClientSize.Width - KeyLabelWidth);
            float selectionX = KeyLabelWidth + (float)((selection.StartTick - this.viewStartTick) * this.pixelsPerTick);
            float selectionEndX = KeyLabelWidth + (float)((selection.EndTick - this.viewStartTick) * this.pixelsPerTick);
            int height = this.PianoRollHeight;
            int count = Math.Max(1, this.HighestNote - this.LowestNote + 1);
            float top = NoteToY(selection.HighestNote, this.LowestNote, count, height, TopMargin);
            float bottom = NoteToY(selection.LowestNote, this.LowestNote, count, height, TopMargin)
                + Math.Max(3, height / (float)count - 1);
            RectangleF bounds = RectangleF.FromLTRB(
                Math.Max(KeyLabelWidth, selectionX),
                Math.Max(TopMargin, top),
                Math.Min(KeyLabelWidth + width, selectionEndX),
                Math.Min(this.PianoRollBottom, bottom));
            return bounds.Contains(point);
        }

        private void MoveSelectionTo(Point point)
        {
            if (this.selectionMoveOriginalSelection is not MidiSelection originalSelection
                || this.selectionMoveOriginalNotes is not List<MidiNoteData> originals)
            {
                return;
            }

            long currentTick = this.PointToTick(point.X);
            int currentNote = this.PointToNote(point.Y);
            long tickDelta = currentTick - this.selectionMoveAnchorTick;
            if (originals.Count > 0)
            {
                tickDelta = Math.Max(tickDelta, -originals.Min(note => note.StartTick));
            }
            int noteDelta = currentNote - this.selectionMoveAnchorNote;

            if (originals.Count > 0)
            {
                int minimum = originals.Min(note => note.NoteNumber);
                int maximum = originals.Max(note => note.NoteNumber);
                noteDelta = Math.Clamp(noteDelta, -minimum, 127 - maximum);
            }

            HashSet<MidiNoteData> toRemove = new(originals);
            if (this.selectionMoveMovedNotes is List<MidiNoteData> moved)
            {
                toRemove.UnionWith(moved);
            }

            this.Track.Notes.RemoveAll(note => toRemove.Contains(note));

            List<MidiNoteData> replacements = [];
            foreach (MidiNoteData original in originals)
            {
                long startTick = Math.Max(0, original.StartTick + tickDelta);
                int noteNumber = Math.Clamp(original.NoteNumber + noteDelta, 0, 127);
                MidiNoteData replacement = new()
                {
                    NoteNumber = noteNumber,
                    Channel = original.Channel,
                    Velocity = original.Velocity,
                    StartTick = startTick,
                    DurationTicks = original.DurationTicks
                };
                replacements.Add(replacement);
                this.Track.ExtendLengthTo(startTick + replacement.DurationTicks);
            }
            this.Track.Notes.AddRange(replacements);
            this.selectionMoveMovedNotes = replacements;
            this.noteSelectionNotes = new HashSet<MidiNoteData>(replacements);

            long selectionStartTick = Math.Max(0, originalSelection.StartTick + tickDelta);
            long selectionEndTick = Math.Max(selectionStartTick + this.GridTicks,
                originalSelection.EndTick + tickDelta);
            this.noteSelection = new MidiSelection(
                selectionStartTick,
                selectionEndTick,
                Math.Clamp(originalSelection.LowestNote + noteDelta, 0, 127),
                Math.Clamp(originalSelection.HighestNote + noteDelta, 0, 127));
        }

        private MidiNoteData? FindNoteAt(Point point)
        {
            int note = this.PointToNote(point.Y);
            long tick = this.PointToTick(point.X);
            return this.Track.Notes.FirstOrDefault(candidate =>
                candidate.NoteNumber == note
                && candidate.StartTick <= tick
                && candidate.StartTick + candidate.DurationTicks > tick);
        }

        private void DeleteNotesInRectangle(Point first, Point second)
        {
            int left = Math.Min(first.X, second.X);
            int right = Math.Max(first.X, second.X);
            int top = Math.Min(first.Y, second.Y);
            int bottom = Math.Max(first.Y, second.Y);
            long startTick = this.PointToTick(left);
            long endTick = this.PointToTick(right) + this.GridTicks;
            int height = this.PianoRollHeight;
            int count = Math.Max(1, this.HighestNote - this.LowestNote + 1);

            this.Track.Notes.RemoveAll(note =>
            {
                float noteTop = NoteToY(note.NoteNumber, this.LowestNote, count, height, TopMargin);
                float noteBottom = noteTop + Math.Max(3, height / (float)count - 1);
                long noteEndTick = note.StartTick + note.DurationTicks;
                bool overlapsTime = note.StartTick <= endTick && noteEndTick >= startTick;
                bool overlapsNote = noteTop <= bottom && noteBottom >= top;
                return overlapsTime && overlapsNote;
            });
        }

        private void pictureBox_editor_MouseWheel(object? sender, MouseEventArgs e)
        {
            if ((ModifierKeys & Keys.Control) == Keys.None)
            {
                long scrollTicks = this.GridTicks * Math.Max(1, Math.Abs(e.Delta) / 120) * 4;
                this.ScrollView(e.Delta < 0 ? scrollTicks : -scrollTicks);
                return;
            }

            long totalLength = Math.Max(this.GridTicks, this.editorLengthTicks);
            bool zoomingOut = e.Delta < 0;
            if (!zoomingOut && totalLength <= this.GridTicks)
            {
                return;
            }

            long currentLength = Math.Max(this.GridTicks, this.viewEndTick - this.viewStartTick);
            double zoomFactor = zoomingOut ? 1.2 : 1.0 / 1.2;
            long newLength = Math.Max(this.GridTicks, (long)Math.Round(currentLength * zoomFactor));
            if (!zoomingOut)
            {
                newLength = Math.Min(newLength, totalLength);
            }

            int width = Math.Max(1, this.pictureBox_editor.ClientSize.Width - KeyLabelWidth);
            double mouseRatio = Math.Clamp((e.X - KeyLabelWidth) / (double)width, 0, 1);
            long tickAtMouse = this.viewStartTick + (long)Math.Round(currentLength * mouseRatio);
            long newStart = Math.Max(0, tickAtMouse - (long)Math.Round(newLength * mouseRatio));
            if (zoomingOut)
            {
                this.editorLengthTicks = Math.Max(this.editorLengthTicks, newStart + newLength);
            }

            this.SetView(newStart, newStart + newLength);
            this.pixelsPerTick = this.CalculatePixelsPerTick(this.viewEndTick - this.viewStartTick);
            this.UpdateScrollBar();
            this.pictureBox_editor.Invalidate();
        }

        private void ScrollView(long tickDelta)
        {
            long visibleLength = Math.Max(this.GridTicks, this.viewEndTick - this.viewStartTick);
            this.SetView(this.viewStartTick + tickDelta, this.viewStartTick + tickDelta + visibleLength);
            this.UpdateScrollBar();
            this.pictureBox_editor.Invalidate();
        }

        private void PanView(int pixelDelta)
        {
            long visibleLength = Math.Max(this.GridTicks, this.panViewEndTick - this.panViewStartTick);
            int width = Math.Max(1, this.pictureBox_editor.ClientSize.Width - KeyLabelWidth);
            long tickDelta = (long)Math.Round(-pixelDelta / (double)width * visibleLength);
            this.SetView(this.panViewStartTick + tickDelta, this.panViewEndTick + tickDelta);
            this.UpdateScrollBar();
            this.pictureBox_editor.Invalidate();
        }

        private void CustomMidiEditorDialog_Resize(object? sender, EventArgs e)
        {
            if (!this.initialized || this.pixelsPerTick <= 0)
            {
                return;
            }

            int width = Math.Max(1, this.pictureBox_editor.ClientSize.Width - KeyLabelWidth);
            long visibleLength = Math.Max(this.GridTicks, (long)Math.Ceiling(width / this.pixelsPerTick));
            this.SetView(this.viewStartTick, this.viewStartTick + visibleLength);
            this.editorLengthTicks = Math.Max(this.editorLengthTicks, this.viewEndTick);
            this.UpdateScrollBar();
            this.pictureBox_editor.Invalidate();
        }

        private void SetView(long startTick, long endTick)
        {
            long totalLength = Math.Max(this.GridTicks, this.editorLengthTicks);
            long visibleLength = Math.Clamp(endTick - startTick, this.GridTicks, totalLength);
            long clampedStart = Math.Clamp(startTick, 0, Math.Max(0, totalLength - visibleLength));
            this.viewStartTick = clampedStart;
            this.viewEndTick = clampedStart + visibleLength;
        }

        private void hScrollBar_editor_Scroll(object? sender, ScrollEventArgs e)
        {
            if (!this.initialized || this.updatingScrollBar)
            {
                return;
            }

            long totalLength = Math.Max(this.GridTicks, this.editorLengthTicks);
            long visibleLength = Math.Max(this.GridTicks, this.viewEndTick - this.viewStartTick);
            long scrollableLength = Math.Max(0, totalLength - visibleLength);
            if (scrollableLength == 0)
            {
                return;
            }

            int scrollRange = Math.Max(1, this.hScrollBar_editor.Maximum - this.hScrollBar_editor.LargeChange + 1);
            long newStart = (long)Math.Round(e.NewValue / (double)scrollRange * scrollableLength);
            this.SetView(newStart, newStart + visibleLength);
            this.pictureBox_editor.Invalidate();
        }

        private void UpdateScrollBar()
        {
            if (this.hScrollBar_editor == null || this.hScrollBar_editor.IsDisposed)
            {
                return;
            }

            long totalLength = Math.Max(this.GridTicks, this.editorLengthTicks);
            long visibleLength = Math.Max(this.GridTicks, this.viewEndTick - this.viewStartTick);
            bool scrollable = totalLength > visibleLength;
            this.updatingScrollBar = true;
            try
            {
                this.hScrollBar_editor.Enabled = scrollable;
                this.hScrollBar_editor.Visible = scrollable;
                if (!scrollable)
                {
                    this.hScrollBar_editor.Value = this.hScrollBar_editor.Minimum;
                    return;
                }

                int scrollRange = Math.Max(1, this.hScrollBar_editor.Maximum - this.hScrollBar_editor.LargeChange + 1);
                long scrollableLength = totalLength - visibleLength;
                int value = (int)Math.Clamp(Math.Round(this.viewStartTick / (double)scrollableLength * scrollRange), 0, scrollRange);
                this.hScrollBar_editor.Value = value;
            }
            finally
            {
                this.updatingScrollBar = false;
            }
        }

        private long GetRequiredEditorLength()
        {
            long lastNoteEnd = this.Track.Notes.Count == 0
                ? this.GridTicks
                : this.Track.Notes.Max(note => note.StartTick + note.DurationTicks);
            long paddedLength = (long)Math.Ceiling(lastNoteEnd / (1.0 - EmptyTailRatio));
            return Math.Max(this.GridTicks, paddedLength);
        }

        private void UpdateEditorLengthAfterNoteChange(long previousLength)
        {
            this.editorLengthTicks = Math.Max(this.editorLengthTicks, this.GetRequiredEditorLength());
            this.UpdateScrollBar();
            this.pictureBox_editor.Invalidate();
        }

        private Point ClampToEditor(Point point) => new(
            Math.Clamp(point.X, KeyLabelWidth, Math.Max(KeyLabelWidth, this.pictureBox_editor.ClientSize.Width - 1)),
            Math.Clamp(point.Y, TopMargin, Math.Max(TopMargin, this.PianoRollBottom - 1)));

        private bool IsPianoRollPoint(Point point) =>
            point.X >= KeyLabelWidth
            && point.X < this.pictureBox_editor.ClientSize.Width
            && point.Y >= TopMargin
            && point.Y < this.PianoRollBottom;

        private static bool HasExceededDragThreshold(Point start, Point current)
        {
            Size dragSize = SystemInformation.DragSize;
            return Math.Abs(current.X - start.X) >= Math.Max(1, dragSize.Width / 2)
                || Math.Abs(current.Y - start.Y) >= Math.Max(1, dragSize.Height / 2);
        }

        private long PointToTick(int x)
        {
            long relativeTick = (long)Math.Floor((x - KeyLabelWidth) / this.pixelsPerTick / this.GridTicks) * this.GridTicks;
            long tick = this.viewStartTick + relativeTick;
            return Math.Clamp(tick, 0, Math.Max(0, this.editorLengthTicks));
        }

        private int PointToNote(int y)
        {
            int height = this.PianoRollHeight;
            int count = Math.Max(1, this.HighestNote - this.LowestNote + 1);
            int relativeY = Math.Clamp(y - TopMargin, 0, height - 1);
            return Math.Clamp(this.HighestNote - (int)Math.Floor(relativeY / (double)height * count), this.LowestNote, this.HighestNote);
        }

        private double CalculatePixelsPerTick(long visibleLengthTicks)
        {
            int width = Math.Max(1, this.pictureBox_editor.ClientSize.Width - KeyLabelWidth);
            return Math.Max(0.000001, width / (double)Math.Max(1, visibleLengthTicks));
        }

        private static float NoteToY(int note, int lowest, int count, int height, int top = 0) =>
            top + (count - 1 - (note - lowest)) / (float)Math.Max(1, count) * height;

        private static Rectangle NormalizeRectangle(Point first, Point second) => Rectangle.FromLTRB(
            Math.Min(first.X, second.X),
            Math.Min(first.Y, second.Y),
            Math.Max(first.X, second.X),
            Math.Max(first.Y, second.Y));

        private MidiFileData CreateOutputMidi() => this.BuildOutputMidi(includeAllTracks: false);

        private MidiFileData CreateOutputMidiForExport() => this.BuildOutputMidi(includeAllTracks: true);

        private MidiFileData BuildOutputMidi(bool includeAllTracks)
        {
            IEnumerable<MidiTrackData> tracks = includeAllTracks
                ? this.midiFile.Tracks.Select(CloneTrackForOutput)
                : [CloneTrackForOutput(this.Track)];
            return MidiFileData.CreateGenerated(
                tracks,
                this.midiFile.TicksPerQuarterNote,
                this.Bpm,
                pitchFrequency: this.PitchFrequency,
                timeSignatureNumerator: this.midiFile.TimeSignatureNumerator,
                timeSignatureDenominator: this.midiFile.TimeSignatureDenominator);
        }

        private static MidiTrackData CloneTrackForOutput(MidiTrackData source)
        {
            MidiTrackData track = new()
            {
                Index = source.Index,
                Name = string.IsNullOrWhiteSpace(source.Name) ? $"Track {source.Index + 1}" : source.Name
            };
            track.Notes.AddRange(source.Notes
                .OrderBy(note => note.StartTick)
                .ThenBy(note => note.NoteNumber));
            track.ExtendLengthTo(source.LengthTicks);
            return track;
        }

        internal static MidiFileData CreateMidiPreviewFromTick(MidiFileData source, int trackIndex, long startTick)
        {
            ArgumentNullException.ThrowIfNull(source);
            MidiTrackData sourceTrack = source.Tracks.FirstOrDefault(track => track.Index == trackIndex)
                ?? throw new ArgumentOutOfRangeException(nameof(trackIndex));
            long playbackStartTick = Math.Clamp(startTick, 0, Math.Max(0, sourceTrack.LengthTicks));
            long playbackEndTick = Math.Max(playbackStartTick + 1, sourceTrack.LengthTicks);
            return MidiFileData.CreateEditSelection(
                source,
                trackIndex,
                playbackStartTick,
                playbackEndTick,
                lowestNote: 0,
                highestNote: 127).MidiFile;
        }

        private MidiFileData CreateOperationMidi()
        {
            IEnumerable<MidiNoteData> sourceNotes = this.Track.Notes;
            long offset = 0;
            if (this.noteSelection is MidiSelection selection)
            {
                sourceNotes = this.GetSelectedNotes()
                    .Select(note =>
                    {
                        long clippedStart = Math.Max(note.StartTick, selection.StartTick);
                        long clippedEnd = Math.Min(note.StartTick + note.DurationTicks, selection.EndTick);
                        return new MidiNoteData
                        {
                            NoteNumber = note.NoteNumber,
                            Channel = note.Channel,
                            Velocity = note.Velocity,
                            StartTick = clippedStart - selection.StartTick,
                            DurationTicks = Math.Max(1, clippedEnd - clippedStart)
                        };
                    });
            }
            else if (this.Track.Notes.Count > 0)
            {
                offset = this.Track.Notes.Min(note => note.StartTick);
                sourceNotes = this.Track.Notes.Select(note => new MidiNoteData
                {
                    NoteNumber = note.NoteNumber,
                    Channel = note.Channel,
                    Velocity = note.Velocity,
                    StartTick = note.StartTick - offset,
                    DurationTicks = note.DurationTicks
                });
            }

            MidiTrackData track = new() { Index = 0, Name = this.Track.Name };
            track.Notes.AddRange(sourceNotes.OrderBy(note => note.StartTick).ThenBy(note => note.NoteNumber));
            return MidiFileData.CreateGenerated(
                [track],
                this.midiFile.TicksPerQuarterNote,
                this.Bpm,
                pitchFrequency: this.PitchFrequency,
                timeSignatureNumerator: this.midiFile.TimeSignatureNumerator,
                timeSignatureDenominator: this.midiFile.TimeSignatureDenominator);
        }

        private List<MidiNoteData> FitGeneratedNotesToSelection(MidiFileData generated)
        {
            List<MidiNoteData> notes = generated.Tracks.SelectMany(track => track.Notes).ToList();
            if (this.noteSelection is not MidiSelection selection || notes.Count == 0)
            {
                return NormalizeNotes(notes);
            }

            long sourceStart = notes.Min(note => note.StartTick);
            long sourceEnd = notes.Max(note => note.StartTick + note.DurationTicks);
            long sourceSpan = Math.Max(1, sourceEnd - sourceStart);
            long targetSpan = Math.Max(this.GridTicks, selection.EndTick - selection.StartTick);
            int sourceLowest = notes.Min(note => note.NoteNumber);
            int sourceHighest = notes.Max(note => note.NoteNumber);
            int sourcePitchSpan = Math.Max(1, sourceHighest - sourceLowest);
            int targetPitchSpan = Math.Max(0, selection.HighestNote - selection.LowestNote);

            List<MidiNoteData> fitted = [];
            foreach (MidiNoteData note in notes)
            {
                long relativeStart = note.StartTick - sourceStart;
                long relativeEnd = relativeStart + Math.Max(1, note.DurationTicks);
                long start = selection.StartTick + (long)Math.Round(relativeStart / (double)sourceSpan * targetSpan);
                long end = selection.StartTick + (long)Math.Round(relativeEnd / (double)sourceSpan * targetSpan);
                int pitch = selection.LowestNote + (int)Math.Round((note.NoteNumber - sourceLowest) / (double)sourcePitchSpan * targetPitchSpan);
                fitted.Add(new MidiNoteData
                {
                    NoteNumber = Math.Clamp(pitch, selection.LowestNote, selection.HighestNote),
                    Channel = note.Channel,
                    Velocity = note.Velocity,
                    StartTick = start,
                    DurationTicks = Math.Max(1, end - start)
                });
            }

            return NormalizeNotes(fitted);
        }

        private async Task<AudioObj> RenderMidiAsync(
            MidiFileData midi,
            int trackIndex,
            bool previewQuality,
            CancellationToken cancellationToken,
            IProgress<double>? progress = null,
            MidiRenderOptions? renderOptions = null)
        {
            MidiRenderOptions options = renderOptions ?? this.CaptureRenderOptions();
            if (options.CustomSynthSettings is InstrumentSynthSettings customSynth)
            {
                Task<AudioObj> customRender = RenderCustomSynthMidiAsync(
                    midi,
                    trackIndex,
                    customSynth,
                    options.Bpm,
                    options.PitchFrequency,
                    cancellationToken,
                    progress);
                this.activeRenders.Add(customRender);
                try
                {
                    return await customRender;
                }
                finally
                {
                    this.activeRenders.Remove(customRender);
                }
            }

            MidiRenderQuality quality = MidiRenderQuality.Balanced;
            using AudioObj? sample = this.customSample?.Clone();
            using CollectionSnapshot collection = this.CaptureCustomCollection();
            return await this.TrackRenderAsync(
                () => MidiAudioRenderer.Render(
                    midi,
                    trackIndex,
                    options.Instrument,
                    options.Bpm,
                    sample,
                    cancellationToken: cancellationToken,
                    pitchFrequency: options.PitchFrequency,
                    previewQuality: previewQuality,
                    progress: progress,
                    renderQuality: quality,
                    sampleCache: this.sampleRenderCache,
                    customCollection: collection.Samples),
                cancellationToken);
        }

        private MidiRenderOptions CaptureRenderOptions() => new(
            this.instrument,
            this.customSynthSettings?.Clone(),
            this.Bpm,
            this.PitchFrequency);

        private static (string Instrument, string Subtype) GetInstrumentNameParts(MidiRenderOptions options)
        {
            if (options.CustomSynthSettings is InstrumentSynthSettings custom)
            {
                return (
                    InstrumentSynthCatalog.FamilyName(custom.Family),
                    custom.VoiceName);
            }

            return ("Midi", FriendlyInstrumentName(options.Instrument));
        }

        private static string SanitizeNamePart(string value)
        {
            char[] chars = value.Trim().Select(character =>
                char.IsLetterOrDigit(character) || character is '#' or '-'
                    ? character
                    : '_').ToArray();
            string result = new(chars);
            while (result.Contains("__", StringComparison.Ordinal))
            {
                result = result.Replace("__", "_", StringComparison.Ordinal);
            }

            return result.Trim('_');
        }

        internal static string CreateCustomMidiCollectionName(
            double bpm,
            MidiInstrument instrument,
            InstrumentSynthSettings? customSynthSettings = null)
        {
            MidiRenderOptions options = new(instrument, customSynthSettings, bpm, 440.0);
            (string instrumentName, string subtype) = GetInstrumentNameParts(options);
            return $"CustomMidi_[{bpm.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)} BPM]_{SanitizeNamePart(instrumentName)}_{SanitizeNamePart(subtype)}";
        }

        internal static string CreateMelodySampleBaseName(
            double bpm,
            MidiInstrument instrument,
            InstrumentSynthSettings? customSynthSettings = null)
        {
            MidiRenderOptions options = new(instrument, customSynthSettings, bpm, 440.0);
            (string instrumentName, string subtype) = GetInstrumentNameParts(options);
            return $"{SanitizeNamePart(instrumentName)}_{SanitizeNamePart(subtype)}_[{bpm.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}]_melody";
        }

        private static string CreateUniqueSampleName(AudioCollectionView collection, string baseName)
        {
            HashSet<string> existingNames = collection.AudioC.Audios
                .Select(audio => audio.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!existingNames.Contains(baseName))
            {
                return baseName;
            }

            for (int index = 2; ; index++)
            {
                string candidate = $"{baseName}_{index}";
                if (!existingNames.Contains(candidate))
                {
                    return candidate;
                }
            }
        }

        private static AudioCollectionView GetOrCreateCustomMidiCollection(string collectionName)
        {
            AudioCollectionView? existing = WindowMain.CollectionViews.FirstOrDefault(view =>
                !view.IsDisposed
                && string.Equals(view.Text, collectionName, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                return existing;
            }

            AudioCollectionView created = new([]);
            created.Rename(collectionName);
            return created;
        }

        private async Task<AudioObj> RenderCustomSynthMidiAsync(
            MidiFileData midi,
            int trackIndex,
            InstrumentSynthSettings sourceSettings,
            double bpm,
            double pitchFrequency,
            CancellationToken cancellationToken,
            IProgress<double>? progress)
        {
            MidiTrackData track = midi.Tracks.FirstOrDefault(candidate => candidate.Index == trackIndex)
                ?? throw new ArgumentOutOfRangeException(nameof(trackIndex));
            InstrumentSynthSettings baseSettings = sourceSettings.Clone();
            baseSettings.Validate();
            int sampleRate = baseSettings.SampleRate;
            int channels = baseSettings.Channels;
            double secondsPerTick = 60.0 / Math.Clamp(bpm, 20.0, 400.0) / midi.TicksPerQuarterNote;
            long lastTick = track.Notes.Count == 0
                ? 0
                : track.Notes.Max(note => note.StartTick + Math.Max(1, note.DurationTicks));
            double durationSeconds = track.Notes.Count == 0
                ? 0.1
                : lastTick * secondsPerTick + baseSettings.ReleaseSeconds;
            long framesLong = Math.Max(1L, (long)Math.Ceiling(durationSeconds * sampleRate));
            if (framesLong > int.MaxValue / channels)
            {
                throw new InvalidOperationException("The custom MIDI arrangement is too long to render in memory.");
            }

            int frameCount = (int)framesLong;
            float[] output = new float[checked(frameCount * channels)];
            MidiNoteData[] notes = track.Notes.OrderBy(note => note.StartTick).ToArray();
            for (int index = 0; index < notes.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                MidiNoteData note = notes[index];
                InstrumentSynthSettings noteSettings = BreakbeatPatternInstrumentRenderer.CreateSettingsForMidiNote(
                    baseSettings,
                    note.NoteNumber,
                    Math.Max(1, note.DurationTicks),
                    midi.TicksPerQuarterNote,
                    bpm,
                    pitchFrequency);
                using AudioObj renderedNote = await this.customInstrumentSampleCache
                    .GetOrRenderAsync(noteSettings, cancellationToken);

                int startFrame = (int)Math.Clamp(Math.Round(note.StartTick * secondsPerTick * sampleRate), 0, frameCount);
                int noteFrames = Math.Min(renderedNote.Data.Length / channels, frameCount - startFrame);
                float velocity = Math.Clamp(note.Velocity / 127f, 0f, 1f);
                int sampleCount = noteFrames * channels;
                int outputOffset = startFrame * channels;
                for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
                {
                    if ((sampleIndex & 0x3fff) == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    output[outputOffset + sampleIndex] += renderedNote.Data[sampleIndex] * velocity;
                }

                progress?.Report((index + 1) / (double)Math.Max(1, notes.Length));
            }

            float peak = output.Length == 0 ? 0 : output.Max(value => Math.Abs(value));
            if (peak > 0.95f)
            {
                float scale = 0.95f / peak;
                for (int index = 0; index < output.Length; index++)
                {
                    output[index] *= scale;
                }
            }

            return new AudioObj
            {
                Name = $"{InstrumentSynthCatalog.FamilyName(baseSettings.Family)} {baseSettings.VoiceName}",
                FilePath = string.Empty,
                Data = output,
                SampleRate = sampleRate,
                Channels = channels,
                BitDepth = 32,
                Length = output.Length,
                Duration = TimeSpan.FromSeconds(frameCount / (double)sampleRate),
                Bpm = (float)bpm,
                Volume = 100f
            };
        }

        private async Task<AudioObj> TrackRenderAsync(Func<AudioObj> render, CancellationToken cancellationToken)
        {
            Task<AudioObj> task = Task.Run(render, cancellationToken);
            this.activeRenders.Add(task);
            try
            {
                return await task;
            }
            finally
            {
                this.activeRenders.Remove(task);
            }
        }

        private CollectionSnapshot CaptureCustomCollection()
        {
            CollectionSnapshot snapshot = new();
            try
            {
                if (this.instrument == MidiInstrument.CustomCollection
                    && this.customCollection is { IsDisposed: false } view)
                {
                    foreach (AudioObj sample in view.AudioC.Audios)
                    {
                        if (sample.Channels > 0 && sample.SampleRate > 0 && sample.Data.Length >= sample.Channels)
                        {
                            snapshot.Samples.Add(sample.Clone());
                        }
                    }
                }

                return snapshot;
            }
            catch
            {
                snapshot.Dispose();
                throw;
            }
        }

        private async Task PreviewNoteAsync(MidiNoteData note)
        {
            MidiFileData previewFile = MidiFileData.CreateSingleNotePreview(this.midiFile, this.Track.Index, note);
            await this.PreviewMidiAsync(previewFile, previewFile.Tracks[0].Index, showCaret: false);
        }

        private async void button_play_Click(object? sender, EventArgs e)
        {
            if (this.playbackCts != null)
            {
                await this.StopPlaybackAsync();
                return;
            }

            if (this.Track.Notes.Count == 0)
            {
                return;
            }

            await this.PlayMidiFromTickAsync(0);
        }

        private async void toolStripMenuItem_playFromHere_Click(object? sender, EventArgs e)
        {
            await this.PlayMidiFromTickAsync(this.playFromHereStartTick);
        }

        private async Task PlayMidiFromTickAsync(long startTick)
        {
            try
            {
                MidiFileData midi = this.CreateOutputMidi();
                MidiTrackData track = midi.Tracks[0];
                long playbackStartTick = Math.Clamp(startTick, 0, Math.Max(0, track.LengthTicks));
                MidiFileData previewMidi = CreateMidiPreviewFromTick(midi, track.Index, playbackStartTick);
                if (previewMidi.Tracks[0].Notes.Count == 0)
                {
                    await this.StopPlaybackAsync();
                    return;
                }

                await this.PreviewMidiAsync(
                    previewMidi,
                    previewMidi.Tracks[0].Index,
                    showCaret: true,
                    caretStartTick: playbackStartTick);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (!this.IsDisposed)
                {
                    WindowMainStaticHelpers.ShowErrorWithCopyButton(this, "MIDI editor playback failed", ex);
                }
            }
            finally
            {
                if (this.playbackCts == null && !this.IsDisposed)
                {
                    this.button_play.Text = "Play";
                }
            }
        }

        private async Task StopPlaybackAsync() => await this.StopPreviewAsync();

        private async Task PreviewMidiAsync(MidiFileData midi, int trackIndex, bool showCaret, long caretStartTick = 0)
        {
            await this.StopPreviewAsync();
            CancellationTokenSource runCts = new();
            this.playbackCts = runCts;
            this.button_play.Text = "Stop";
            ProgressDialog? progressDialog = null;
            try
            {
                this.previewCaretVisible = showCaret;
                this.previewStartTick = showCaret ? Math.Max(0, caretStartTick) : 0;
                IProgress<double>? progress = null;
                if (showCaret)
                {
                    Progress<double> previewProgress = new(value => progressDialog?.Report(value));
                    progress = previewProgress;
                    progressDialog = new ProgressDialog(
                        "Rendering MIDI editor preview...",
                        progress,
                        windowCloseDelay: 0.0d,
                        ct: runCts.Token,
                        cancellationSource: runCts);
                    progressDialog.Show(this);
                    progressDialog.BringToFront();
                }

                this.previewAudio = await this.RenderMidiAsync(
                    midi,
                    trackIndex,
                    previewQuality: true,
                    cancellationToken: runCts.Token,
                    progress);
                progressDialog?.Complete();
                if (runCts.IsCancellationRequested || this.closing)
                {
                    this.previewAudio?.Dispose();
                    this.previewAudio = null;
                    return;
                }

                if (showCaret)
                {
                    this.timer_previewCaret.Start();
                }

                TaskCompletionSource playbackStopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
                await this.previewAudio.PlayAsync(runCts.Token, () => playbackStopped.TrySetResult());
                await playbackStopped.Task.WaitAsync(runCts.Token);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                if (progressDialog != null && !progressDialog.IsDisposed)
                {
                    progressDialog.Close();
                }

                if (ReferenceEquals(this.playbackCts, runCts))
                {
                    this.ResetPreviewState();
                }
                else
                {
                    runCts.Dispose();
                }
            }
        }

        private async Task StopPreviewAsync()
        {
            this.playbackCts?.Cancel();
            if (this.previewAudio != null)
            {
                try
                {
                    await this.previewAudio.StopAsync();
                }
                catch (ObjectDisposedException)
                {
                }
            }

            this.ResetPreviewState();
        }

        private void ResetPreviewState()
        {
            if (this.IsDisposed)
            {
                return;
            }

            this.timer_previewCaret.Stop();
            this.button_play.Text = "Play";
            this.playbackCts?.Dispose();
            this.playbackCts = null;
            this.previewAudio?.Dispose();
            this.previewAudio = null;
            this.previewCaretVisible = false;
            this.previewStartTick = 0;
            this.pictureBox_editor.Invalidate();
        }

        private void timer_previewCaret_Tick(object? sender, EventArgs e)
        {
            if (!this.IsDisposed)
            {
                this.pictureBox_editor.Invalidate();
            }
        }

        private async void button_generate_Click(object? sender, EventArgs e)
        {
            await this.StopPlaybackAsync();
            using MidiGeneratorDialog dialog = new(this.CreateOperationMidi());
            if (dialog.ShowDialog(this) != DialogResult.OK || dialog.GeneratedMidiFileData == null)
            {
                return;
            }

            this.StartGhostPreview(this.FitGeneratedNotesToSelection(dialog.GeneratedMidiFileData), addToHistory: true);
        }

        private async void button_remix_Click(object? sender, EventArgs e)
        {
            await this.StopPlaybackAsync();
            MidiFileData source = this.CreateOperationMidi();
            if (source.Tracks[0].Notes.Count == 0)
            {
                return;
            }
            using MidiRemixDialog dialog = new(source);
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            try
            {
                MidiFileData remixedFile = await MidiRemixer.RemixAsync(source, dialog.Settings, dialog.TrackIndex);
                if (!this.closing && !this.IsDisposed)
                {
                    List<MidiNoteData> remixedNotes = remixedFile.Tracks.SelectMany(track => track.Notes).ToList();
                    this.StartGhostPreview(remixedNotes, addToHistory: true);
                }
            }
            catch (Exception ex)
            {
                if (!this.IsDisposed)
                {
                    WindowMainStaticHelpers.ShowErrorWithCopyButton(this, "MIDI remix failed", ex);
                }
            }
        }

        private async void button_record_Click(object? sender, EventArgs e)
        {
            if (this.recordingFinalizing || this.closing || this.IsDisposed)
            {
                return;
            }

            if (this.recordingActive || this.recordingStarting)
            {
                await this.FinishRecordingAsync(accept: true, null);
                return;
            }

            try
            {
                IReadOnlyList<AudioInputDevice> devices = MicrophoneAudioCapture.GetInputDevices();
                if (devices.Count == 0)
                {
                    WindowMainStaticHelpers.ShowErrorWithCopyButton(
                        this,
                        "Microphone recording",
                        new InvalidOperationException("No microphone input device was found. Connect a microphone and try again."));
                    return;
                }

                this.comboBox_inputDevice.Items.Clear();
                foreach (AudioInputDevice device in devices)
                {
                    this.comboBox_inputDevice.Items.Add(device);
                }

                this.comboBox_inputDevice.SelectedIndex = Math.Clamp(this.comboBox_inputDevice.Items.Count - 1, 0, this.comboBox_inputDevice.Items.Count - 1);
                this.comboBox_inputDevice.Visible = true;
                this.label_recordStatus.Visible = true;
                this.progressBar_inputLevel.Visible = true;
                this.label_recordStatus.Text = "Starting microphone...";
                this.progressBar_inputLevel.Value = 0;
                await this.BeginRecordingCoreAsync();
            }
            catch (Exception ex)
            {
                this.RecordingUiIdle();
                if (!this.IsDisposed)
                {
                    this.label_recordStatus.Text = "Microphone could not be started.";
                    WindowMainStaticHelpers.ShowErrorWithCopyButton(this, "Microphone recording", ex);
                }
            }
        }

        private void comboBox_inputDevice_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (this.recordingStarting || this.recordingFinalizing
                || this.comboBox_inputDevice.SelectedItem is not AudioInputDevice device)
            {
                return;
            }

            _ = this.RestartRecordingCoreAsync(device);
        }

        private async Task BeginRecordingCoreAsync()
        {
            this.recordingStarting = true;
            this.button_record.Text = "Starting...";
            this.comboBox_inputDevice.Enabled = false;
            this.label_recordStatus.Text = "Starting microphone...";
            try
            {
                await this.StopRecordingCaptureCoreAsync();
                if (this.closing || this.IsDisposed)
                {
                    return;
                }

                if (this.comboBox_inputDevice.SelectedItem is not AudioInputDevice device)
                {
                    throw new InvalidOperationException("No microphone input device is selected.");
                }

                this.recordingPath = Path.Combine(Path.GetTempPath(), $"MA_MidiRecord_{Guid.NewGuid():N}.wav");
                if (File.Exists(this.recordingPath))
                {
                    File.Delete(this.recordingPath);
                }

                this.recordingCts = new();
                this.recordingCapture = MicrophoneAudioCapture.Start(device.Id, this.recordingPath!);
                this.recordingActive = true;
                this.recordingClock.Reset();
                this.recordingClock.Start();
                this.timer_recording.Start();
                this.button_record.Text = "Stop";
                this.label_recordStatus.Text = "Recording. Sing or play your melody.";
            }
            catch
            {
                this.recordingActive = false;
                this.recordingClock.Stop();
                this.timer_recording.Stop();
                this.RecordingUiIdle();
                throw;
            }
            finally
            {
                this.recordingStarting = false;
            }
        }

        private async Task RestartRecordingCoreAsync(AudioInputDevice device)
        {
            if (this.recordingStarting || this.recordingFinalizing)
            {
                return;
            }

            this.recordingStarting = true;
            this.comboBox_inputDevice.Enabled = false;
            this.label_recordStatus.Text = "Restarting microphone...";
            try
            {
                await this.StopRecordingCaptureCoreAsync();
                if (this.closing || this.IsDisposed)
                {
                    return;
                }

                this.recordingPath = Path.Combine(Path.GetTempPath(), $"MA_MidiRecord_{Guid.NewGuid():N}.wav");
                if (File.Exists(this.recordingPath))
                {
                    File.Delete(this.recordingPath);
                }

                this.recordingCts = new();
                this.recordingCapture = MicrophoneAudioCapture.Start(device.Id, this.recordingPath!);
                this.recordingActive = true;
                this.recordingClock.Reset();
                this.recordingClock.Start();
                this.timer_recording.Start();
                this.label_recordStatus.Text = "Recording. Sing or play your melody.";
            }
            catch
            {
                this.recordingActive = false;
                this.recordingClock.Stop();
                this.timer_recording.Stop();
                this.RecordingUiIdle();
                throw;
            }
            finally
            {
                this.recordingStarting = false;
            }
        }

        private void timer_recording_Tick(object? sender, EventArgs e)
        {
            MicrophoneAudioCapture? activeCapture = this.recordingCapture;
            if (activeCapture is null || !this.recordingActive)
            {
                return;
            }

            if (activeCapture.Failure is Exception failure)
            {
                _ = this.FinishRecordingAsync(accept: false, failure);
                return;
            }

            float peak = activeCapture.PeakLevel;
            this.progressBar_inputLevel.Value = Math.Clamp(
                (int)Math.Round(peak * this.progressBar_inputLevel.Maximum),
                this.progressBar_inputLevel.Minimum,
                this.progressBar_inputLevel.Maximum);
            this.label_recordStatus.Text = $"Recording {this.recordingClock.Elapsed:mm\\:ss\\.fff} · Mic {peak:P0}";
        }

        private async Task FinishRecordingAsync(bool accept, Exception? captureFailure)
        {
            if (this.recordingFinalizing)
            {
                return;
            }

            this.recordingFinalizing = true;
            this.timer_recording.Stop();
            this.button_record.Enabled = false;
            this.button_record.Text = "Analyzing...";
            this.comboBox_inputDevice.Enabled = false;
            this.label_recordStatus.Text = captureFailure is null
                ? "Stopping microphone and analyzing pitch..."
                : "Microphone recording failed.";
            try
            {
                string? currentRecordingPath = this.recordingPath;
                MicrophoneAudioCapture? activeCapture = this.recordingCapture;
                this.recordingCapture = null;
                this.recordingActive = false;
                this.recordingClock.Stop();
                if (activeCapture is not null)
                {
                    try
                    {
                        await activeCapture.StopAsync();
                    }
                    catch (Exception ex)
                    {
                        captureFailure ??= ex;
                    }
                }

                if (!accept || captureFailure is not null || currentRecordingPath is null
                    || !File.Exists(currentRecordingPath) || new FileInfo(currentRecordingPath).Length <= 44)
                {
                    if (captureFailure is not null && !this.IsDisposed && !this.closing)
                    {
                        WindowMainStaticHelpers.ShowErrorWithCopyButton(this, "Microphone recording", captureFailure);
                    }

                    this.DeleteRecordingFile();
                    this.RecordingUiIdle();
                    return;
                }

                this.label_recordStatus.Text = "Analyzing pitch (pYIN)...";
                List<MidiNoteData> notes = await this.AnalyzeRecordingToMidiNotesAsync(currentRecordingPath!);
                if (notes.Count == 0)
                {
                    this.DeleteRecordingFile();
                    this.RecordingUiIdle();
                    if (!this.IsDisposed && !this.closing)
                    {
                        WindowMainStaticHelpers.ShowErrorWithCopyButton(
                            this,
                            "Microphone recording",
                            new InvalidOperationException("No voiced pitch was detected in the recording. Sing closer to the microphone or try again."));
                    }

                    return;
                }

                this.StoreRecordingAsCollectionSample();
                this.StartGhostPreview(notes, addToHistory: true);
                this.button_record.Enabled = true;
                this.button_record.Text = "Record";
                this.label_recordStatus.Visible = true;
                this.label_recordStatus.Text = $"{notes.Count} note(s) in clipboard. Click in the piano roll to place.";
            }
            catch (Exception ex)
            {
                this.DeleteRecordingFile();
                this.RecordingUiIdle();
                if (!this.IsDisposed && !this.closing)
                {
                    WindowMainStaticHelpers.ShowErrorWithCopyButton(this, "Microphone pitch analysis", ex);
                }
            }
            finally
            {
                this.recordingFinalizing = false;
            }
        }

        private Task<List<MidiNoteData>> AnalyzeRecordingToMidiNotesAsync(string filePath)
        {
            AudioObj? audio = AudioObj.FromFile(filePath);
            if (audio is null || audio.Channels <= 0 || audio.SampleRate <= 0 || audio.Data.Length < audio.Channels)
            {
                return Task.FromException<List<MidiNoteData>>(
                    new InvalidOperationException("The recorded file could not be read as audio."));
            }

            float[] mono = audio.Channels == 1
                ? audio.Data
                : AverageToMono(audio.Data, audio.Channels);
            int rate = audio.SampleRate;
            int hop = Math.Max(1, rate / 100);
            double minimumHz = 55;
            double maximumHz = Math.Min(900, rate * 0.45);
            audio.Dispose();

            return Task.Run(
                () =>
                {
                    CancellationTokenSource cts = new();
                    try
                    {
                        PitchFrame[] frames = PitchTracker.Track(mono, rate, hop, minimumHz, maximumHz, 1, cts.Token);
                        return BuildMidiNotesFromPitchFrames(
                            frames,
                            rate,
                            hop,
                            this.midiFile.TicksPerQuarterNote,
                            this.Bpm,
                            a4: this.PitchFrequency,
                            minVoicedProbability: 0.5,
                            minNoteTicks: Math.Max(1, this.GridTicks));
                    }
                    finally
                    {
                        cts.Dispose();
                    }
                });
        }

        private void StoreRecordingAsCollectionSample()
        {
            if (this.recordingPath is null || !File.Exists(this.recordingPath))
            {
                return;
            }

            try
            {
                AudioObj? recorded = AudioObj.FromFile(this.recordingPath);
                if (recorded is null)
                {
                    return;
                }

                using (recorded)
                {
                    MidiRenderOptions options = this.CaptureRenderOptions();
                    string collectionName = CreateCustomMidiCollectionName(options.Bpm, options.Instrument, options.CustomSynthSettings);
                    AudioCollectionView renderedCollection = GetOrCreateCustomMidiCollection(collectionName);
                    string sampleName = CreateUniqueSampleName(
                        renderedCollection,
                        $"Singing_{DateTime.Now:yyyyMMdd_HHmmss}");
                    recorded.Rename(sampleName);
                    renderedCollection.AudioC.Audios.Add(recorded.Clone());
                }
            }
            catch (Exception ex)
            {
                LogManager.Log($"Could not store microphone recording in collection: {ex}");
            }
        }

        private void RecordingUiIdle()
        {
            if (this.IsDisposed)
            {
                return;
            }

            this.button_record.Enabled = true;
            this.button_record.Text = "Record";
            this.comboBox_inputDevice.Enabled = false;
            this.comboBox_inputDevice.Visible = false;
            this.label_recordStatus.Visible = false;
            this.progressBar_inputLevel.Visible = false;
        }

        private void DeleteRecordingFile()
        {
            try
            {
                if (this.recordingPath is not null && File.Exists(this.recordingPath))
                {
                    File.Delete(this.recordingPath);
                }
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Unable to delete temporary recording file: {exception.Message}");
            }

            this.recordingPath = null;
        }

        private static float[] AverageToMono(float[] interleaved, int channels)
        {
            int frames = interleaved.Length / channels;
            float[] mono = new float[frames];
            for (int frame = 0; frame < frames; frame++)
            {
                double sum = 0;
                int offset = frame * channels;
                for (int channel = 0; channel < channels; channel++)
                {
                    sum += interleaved[offset + channel];
                }

                mono[frame] = (float)(sum / channels);
            }

            return mono;
        }

        /// <summary>
        /// Converts a pYIN pitch frame trajectory into quantized MIDI notes.
        /// Frames with voiced probability below minVoicedProbability are treated as silence.
        /// Consecutive voiced frames with the same rounded MIDI note number are merged.
        /// Notes shorter than minNoteTicks are dropped.
        /// </summary>
        internal static List<MidiNoteData> BuildMidiNotesFromPitchFrames(
            IReadOnlyList<PitchFrame> frames,
            int sampleRate,
            int hopSize,
            int ticksPerQuarterNote,
            double bpm,
            double a4,
            double minVoicedProbability,
            long minNoteTicks)
        {
            if (frames.Count == 0)
            {
                return [];
            }

            double secondsPerTick = 60.0 / Math.Clamp(bpm, 20.0, 400.0) / Math.Max(1, ticksPerQuarterNote);
            double secondsPerFrame = hopSize / (double)Math.Max(1, sampleRate);
            List<MidiNoteData> notes = [];
            int currentNote = -1;
            long currentStartFrame = -1;
            int currentVoicedFrames = 0;
            for (int index = 0; index < frames.Count; index++)
            {
                double frequencyHz = frames[index].FrequencyHz;
                double voicedProbability = frames[index].VoicedProbability;
                bool voiced = frequencyHz > 0 && voicedProbability >= minVoicedProbability;
                if (!voiced)
                {
                    if (currentVoicedFrames > 0)
                    {
                        long durationTicks = Math.Max(1, (long)Math.Round(currentVoicedFrames * secondsPerFrame / secondsPerTick));
                        if (durationTicks >= minNoteTicks)
                        {
                            notes.Add(new MidiNoteData
                            {
                                NoteNumber = currentNote,
                                Channel = 0,
                                Velocity = 100,
                                StartTick = Math.Max(0, (long)Math.Round(currentStartFrame * secondsPerFrame / secondsPerTick)),
                                DurationTicks = durationTicks
                            });
                        }

                        currentVoicedFrames = 0;
                    }

                    continue;
                }

                int roundedNote = RoundFrequencyToMidiNote(frequencyHz, a4);
                if (roundedNote == currentNote)
                {
                    currentVoicedFrames++;
                }
                else
                {
                    if (currentVoicedFrames > 0)
                    {
                        long durationTicks = Math.Max(1, (long)Math.Round(currentVoicedFrames * secondsPerFrame / secondsPerTick));
                        if (durationTicks >= minNoteTicks)
                        {
                            notes.Add(new MidiNoteData
                            {
                                NoteNumber = currentNote,
                                Channel = 0,
                                Velocity = 100,
                                StartTick = Math.Max(0, (long)Math.Round(currentStartFrame * secondsPerFrame / secondsPerTick)),
                                DurationTicks = durationTicks
                            });
                        }
                    }

                    currentNote = roundedNote;
                    currentStartFrame = index;
                    currentVoicedFrames = 1;
                }
            }

            if (currentVoicedFrames > 0)
            {
                long durationTicks = Math.Max(1, (long)Math.Round(currentVoicedFrames * secondsPerFrame / secondsPerTick));
                if (durationTicks >= minNoteTicks)
                {
                    notes.Add(new MidiNoteData
                    {
                        NoteNumber = currentNote,
                        Channel = 0,
                        Velocity = 100,
                        StartTick = Math.Max(0, (long)Math.Round(currentStartFrame * secondsPerFrame / secondsPerTick)),
                        DurationTicks = durationTicks
                    });
                }
            }

            return notes;
        }

        internal static int RoundFrequencyToMidiNote(double frequencyHz, double a4)
        {
            double a4Reference = Math.Clamp(a4, 20.0, 1000.0);
            double semitones = 12.0 * Math.Log2(Math.Max(1e-6, frequencyHz) / a4Reference);
            int note = 69 + (int)Math.Round(semitones);
            return Math.Clamp(note, 0, 127);
        }

        private void ApplyMidiFile(MidiFileData file)
        {
            this.midiFile = NormalizeEditorFile(file, this.Bpm);
            this.selectedTrackPosition = 0;
            this.UpdateTrackSelector();
            this.noteSelection = null;
            this.noteSelectionNotes = null;
            this.pendingHistorySnapshot = null;
            this.undoHistory.Clear();
            this.redoHistory.Clear();
            this.numericUpDown_bpm.Value = (decimal)Math.Clamp(this.midiFile.DefaultBpm, 20.0, 400.0);
            this.numericUpDown_pitchFrequency.Value = (decimal)Math.Clamp(this.midiFile.PitchFrequency, 1.0, 1000.0);
            this.ResetTrackView();
        }

        private void button_import_Click(object? sender, EventArgs e)
        {
            string initialDirectory = Directory.Exists(lastMidiFolder)
                ? lastMidiFolder
                : Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            using OpenFileDialog dialog = new()
            {
                Title = "Import MIDI File",
                InitialDirectory = initialDirectory,
                Filter = "MIDI files (*.mid;*.midi)|*.mid;*.midi",
                DefaultExt = "mid",
                CheckFileExists = true,
                Multiselect = false,
                RestoreDirectory = true
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            lastMidiFolder = Path.GetDirectoryName(dialog.FileName) ?? lastMidiFolder;
            try
            {
                this.ApplyMidiFile(MidiFileData.Load(dialog.FileName));
            }
            catch (Exception ex)
            {
                LogManager.Log($"Custom MIDI import failed: {ex}");
                WindowMainStaticHelpers.ShowErrorWithCopyButton(this, "MIDI import failed", ex);
            }
        }

        private async void button_save_Click(object? sender, EventArgs e)
        {
            if (this.saveRenderCts != null || this.closing)
            {
                return;
            }

            CancellationTokenSource renderCts = new();
            this.saveRenderCts = renderCts;
            ProgressDialog? progressDialog = null;
            AudioObj? rendered = null;
            try
            {
                this.button_save.Enabled = false;
                const string renderMessage = "Rendering MIDI arrangement...";
                MidiFileData midi = this.CreateOutputMidi();
                MidiRenderOptions renderOptions = this.CaptureRenderOptions();
                string collectionName = CreateCustomMidiCollectionName(
                    renderOptions.Bpm,
                    renderOptions.Instrument,
                    renderOptions.CustomSynthSettings);
                string sampleBaseName = CreateMelodySampleBaseName(
                    renderOptions.Bpm,
                    renderOptions.Instrument,
                    renderOptions.CustomSynthSettings);
                Progress<double> progress = new(value => progressDialog?.Report(value));
                progressDialog = new ProgressDialog(renderMessage, progress, ct: renderCts.Token, cancellationSource: renderCts);
                progressDialog.Show(this);
                progressDialog.BringToFront();
                rendered = await this.RenderMidiAsync(
                    midi,
                    midi.Tracks[0].Index,
                    previewQuality: false,
                    cancellationToken: renderCts.Token,
                    progress,
                    renderOptions);
                if (renderCts.IsCancellationRequested || this.closing)
                {
                    return;
                }

                AudioCollectionView renderedCollection = GetOrCreateCustomMidiCollection(collectionName);
                rendered.Rename(CreateUniqueSampleName(renderedCollection, sampleBaseName));
                renderedCollection.AudioC.Audios.Add(rendered);
                rendered = null; // The collection owns the rendered audio.
                renderedCollection.Show();
                progressDialog.Complete();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (!this.IsDisposed && !this.closing)
                {
                    WindowMainStaticHelpers.ShowErrorWithCopyButton(this, "MIDI rendering failed", ex);
                }
            }
            finally
            {
                rendered?.Dispose();
                if (progressDialog != null && !progressDialog.IsDisposed)
                {
                    progressDialog.Close();
                    progressDialog.Dispose();
                }

                if (ReferenceEquals(this.saveRenderCts, renderCts))
                {
                    this.saveRenderCts = null;
                }

                renderCts.Dispose();
                if (!this.IsDisposed)
                {
                    this.button_save.Enabled = true;
                }
            }
        }

        private async void toolStripMenuItem_saveMidi_Click(object? sender, EventArgs e)
        {
            using SaveFileDialog dialog = new()
            {
                Title = "Save MIDI File",
                Filter = "MIDI Files (*.mid)|*.mid",
                DefaultExt = "mid",
                AddExtension = true,
                FileName = "CustomMidi.mid"
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            try
            {
                this.toolStripMenuItem_saveMidi.Enabled = false;
                string? exportedPath = await this.CreateOutputMidiForExport().ExportAsync(dialog.FileName);
                if (string.IsNullOrWhiteSpace(exportedPath))
                {
                    throw new IOException("The MIDI file could not be exported. See the log for details.");
                }
            }
            catch (Exception ex)
            {
                LogManager.Log($"Custom MIDI export failed: {ex}");
                if (!this.IsDisposed)
                {
                    WindowMainStaticHelpers.ShowErrorWithCopyButton(this, "MIDI export failed", ex);
                }
            }
            finally
            {
                if (!this.IsDisposed)
                {
                    this.toolStripMenuItem_saveMidi.Enabled = true;
                }
            }
        }

        private async void CustomMidiEditorDialog_FormClosing(object? sender, FormClosingEventArgs e)
        {
            if (this.allowClose)
            {
                return;
            }

            e.Cancel = true;
            if (this.closing)
            {
                return;
            }

            this.closing = true;
            try
            {
                this.saveRenderCts?.Cancel();
                this.recordingCts?.Cancel();
                this.timer_recording.Stop();
                this.button_record.Enabled = false;
                await this.StopRecordingCaptureCoreAsync();
                await this.StopPlaybackAsync();
                Task[] renders = this.activeRenders.ToArray();
                if (renders.Length > 0)
                {
                    await Task.WhenAll(renders);
                }

                this.DeleteRecordingFile();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                LogManager.Log($"Custom MIDI editor close failed: {ex}");
            }
            finally
            {
                this.allowClose = true;
                if (!this.IsDisposed)
                {
                    this.Close();
                }
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            this.timer_previewCaret.Stop();
            this.timer_recording.Stop();
            this.previewAudio?.Dispose();
            this.playbackCts?.Dispose();
            this.saveRenderCts?.Dispose();
            this.recordingCts?.Dispose();
            this.recordingCapture?.Dispose();
            this.customSample?.Dispose();
            this.sampleRenderCache.Dispose();
            this.customInstrumentSampleCache.Dispose();
            this.DeleteRecordingFile();
            base.OnFormClosed(e);
        }

        private async Task StopRecordingCaptureCoreAsync()
        {
            MicrophoneAudioCapture? activeCapture = this.recordingCapture;
            this.recordingCapture = null;
            this.recordingActive = false;
            this.recordingClock.Stop();
            this.timer_recording.Stop();
            if (activeCapture is null)
            {
                return;
            }

            try
            {
                await activeCapture.StopAsync();
            }
            catch (Exception exception)
            {
                LogManager.Log($"Could not stop microphone recording: {exception.Message}");
            }

            this.recordingCts?.Dispose();
            this.recordingCts = null;
        }
    }
}
