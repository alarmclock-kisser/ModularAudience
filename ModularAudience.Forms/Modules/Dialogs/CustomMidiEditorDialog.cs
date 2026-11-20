using ModularAudience.Audio;
using ModularAudience.Audio.Midi;
using ModularAudience.Audio.Synthesis;
using ModularAudience.Generators;
using ModularAudience.Forms.Helpers;
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
        private const int BottomMargin = 20;
        private const int MinimumKeyGap = 12;
        private const double EmptyTailRatio = 0.20;
        private const int DefaultTicksPerQuarterNote = 960;
        private const int DefaultLowestNote = 36; // C2
        private const int DefaultHighestNote = 84; // C6
        private const int MaximumHistoryDepth = 100;

        private readonly MidiSampleRenderCache sampleRenderCache = new();
        private readonly BreakbeatInstrumentSampleCache customInstrumentSampleCache = new();
        private readonly ContextMenuStrip instrumentMenu = new();
        private readonly HashSet<Task> activeRenders = [];
        private readonly List<EditorSnapshot> undoHistory = [];
        private readonly List<EditorSnapshot> redoHistory = [];

        private MidiFileData midiFile;
        private MidiInstrument instrument = MidiInstrument.Sine;
        private InstrumentSynthSettings? customSynthSettings;
        private AudioObj? customSample;
        private AudioCollectionView? customCollection;

        private Point? editStart;
        private Point? rectangleSelectionStart;
        private Point? rectangleSelectionEnd;
        private Point? selectionDragStart;
        private Point? selectionDragEnd;
        private MidiSelection? noteSelection;
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

        private CancellationTokenSource? playbackCts;
        private CancellationTokenSource? saveRenderCts;
        private AudioObj? previewAudio;
        private bool previewCaretVisible;

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
            MidiSelection? Selection);

        private sealed record MidiRenderOptions(
            MidiInstrument Instrument,
            InstrumentSynthSettings? CustomSynthSettings,
            double Bpm,
            double PitchFrequency);

        public CustomMidiEditorDialog(MidiFileData? midiFileData = null, double initialBpm = 120.0)
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

        private MidiTrackData Track => this.midiFile.Tracks[0];

        internal MidiFileData MidiFile => this.midiFile;

        internal MidiTrackData SelectedTrack => this.Track;

        internal MidiInstrument Instrument => this.instrument;

        private int LowestNote => Math.Clamp(this.domainUpDown_minKey.SelectedIndex, 0, 127);

        private int HighestNote => Math.Clamp(this.domainUpDown_maxKey.SelectedIndex, 0, 127);

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

            return source.Tracks.Count == 1 ? source : source.MergeTracks("Piano Roll");
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
            for (int note = 0; note < 128; note++)
            {
                this.domainUpDown_minKey.Items.Add(MidiNoteName(note));
                this.domainUpDown_maxKey.Items.Add(MidiNoteName(note));
            }
        }

        private void SetKeyRange(int lowest, int highest)
        {
            (lowest, highest) = NormalizeKeyRange(lowest, highest, lowestChanged: true);
            this.updatingKeyRange = true;
            try
            {
                this.domainUpDown_minKey.SelectedIndex = lowest;
                this.domainUpDown_maxKey.SelectedIndex = highest;
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
            if (this.customSynthSettings is InstrumentSynthSettings synth)
            {
                text = $"{InstrumentSynthCatalog.FamilyName(synth.Family)} / {synth.VoiceName}";
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
            this.toolTip_editor.SetToolTip(this.label_instrument, $"Selected MIDI instrument: {text}");
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

            int lowest = this.domainUpDown_minKey.SelectedIndex;
            int highest = this.domainUpDown_maxKey.SelectedIndex;
            if (lowest < 0 || highest < 0)
            {
                return;
            }

            (int validLowest, int validHighest) = NormalizeKeyRange(lowest, highest, lowestChanged);
            this.updatingKeyRange = true;
            try
            {
                if (this.domainUpDown_minKey.SelectedIndex != validLowest)
                {
                    this.domainUpDown_minKey.SelectedIndex = validLowest;
                }

                if (this.domainUpDown_maxKey.SelectedIndex != validHighest)
                {
                    this.domainUpDown_maxKey.SelectedIndex = validHighest;
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
            this.noteSelection);

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
            this.midiFile = MidiFileData.CreateGenerated(
                [track],
                this.midiFile.TicksPerQuarterNote,
                (double)snapshot.Bpm,
                pitchFrequency: (double)snapshot.PitchFrequency,
                timeSignatureNumerator: this.midiFile.TimeSignatureNumerator,
                timeSignatureDenominator: this.midiFile.TimeSignatureDenominator);

            this.restoringHistory = true;
            try
            {
                this.numericUpDown_bpm.Value = snapshot.Bpm;
                this.numericUpDown_pitchFrequency.Value = snapshot.PitchFrequency;
                this.numericUpDown_resolution.Value = snapshot.Resolution;
                this.domainUpDown_minKey.SelectedIndex = snapshot.LowestNote;
                this.domainUpDown_maxKey.SelectedIndex = snapshot.HighestNote;
                this.noteSelection = snapshot.Selection;
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
            this.midiFile = MidiFileData.CreateGenerated(
                [replacement],
                this.midiFile.TicksPerQuarterNote,
                this.Bpm,
                pitchFrequency: this.PitchFrequency,
                timeSignatureNumerator: this.midiFile.TimeSignatureNumerator,
                timeSignatureDenominator: this.midiFile.TimeSignatureDenominator);
            this.noteSelection = null;
            this.editorLengthTicks = Math.Max(this.DefaultViewLengthTicks, this.GetRequiredEditorLength());
            this.SetView(this.viewStartTick, this.viewEndTick);
            this.CommitHistoryTransaction();
            this.UpdateScrollBar();
            this.pictureBox_editor.Invalidate();
        }

        private List<MidiNoteData> GetSelectedNotes()
        {
            if (this.noteSelection is not MidiSelection selection)
            {
                return [];
            }

            return this.Track.Notes.Where(note => this.IsNoteSelected(note)).ToList();
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

        private void PlaceGhostPreview()
        {
            if (!this.ghostPreviewActive || !this.ghostPreviewValid)
            {
                return;
            }

            List<MidiNoteData> placed = this.GetGhostNotesAtPointer().ToList();
            this.BeginHistoryTransaction();
            this.Track.Notes.AddRange(placed);
            this.Track.ExtendLengthTo(placed.Max(note => note.StartTick + note.DurationTicks));
            this.noteSelection = null;
            this.ghostPreviewActive = false;
            this.ghostPreviewValid = false;
            this.ghostNotes.Clear();
            this.CommitHistoryTransaction();
            this.editorLengthTicks = Math.Max(this.editorLengthTicks, this.GetRequiredEditorLength());
            this.SetView(this.viewStartTick, this.viewEndTick);
            this.UpdateScrollBar();
            this.pictureBox_editor.Invalidate();
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
            int height = Math.Max(1, this.pictureBox_editor.ClientSize.Height - BottomMargin);
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
                float y = NoteToY(note, lowest, noteCount, height);
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
                e.Graphics.DrawLine(pen, x, 0, x, height);
            }

            if (this.noteSelection is MidiSelection noteSelection)
            {
                float selectionX = KeyLabelWidth + (float)((noteSelection.StartTick - visibleStartTick) * this.pixelsPerTick);
                float selectionEndX = KeyLabelWidth + (float)((noteSelection.EndTick - visibleStartTick) * this.pixelsPerTick);
                float top = NoteToY(noteSelection.HighestNote, lowest, noteCount, height);
                float bottom = NoteToY(noteSelection.LowestNote, lowest, noteCount, height)
                    + Math.Max(3, height / (float)noteCount - 1);
                RectangleF selectedBounds = RectangleF.FromLTRB(
                    Math.Max(KeyLabelWidth, selectionX),
                    Math.Max(0, top),
                    Math.Min(KeyLabelWidth + width, selectionEndX),
                    Math.Min(height, bottom));
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
                float y = NoteToY(note.NoteNumber, lowest, noteCount, height);
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
                    float y = NoteToY(note.NoteNumber, lowest, noteCount, height);
                    float noteHeight = Math.Max(3, height / (float)noteCount - 1);
                    RectangleF rect = new(x, y, Math.Min(noteWidth, KeyLabelWidth + width - x), noteHeight);
                    e.Graphics.FillRectangle(ghostBrush, rect);
                    e.Graphics.DrawRectangle(ghostPen, rect.X, rect.Y, rect.Width, rect.Height);
                }
            }

            if (this.previewCaretVisible && this.previewAudio?.PlayerPlaying == true && this.previewAudio.Duration > TimeSpan.Zero)
            {
                double secondsPerTick = 60.0 / Math.Max(1.0, this.Bpm) / Math.Max(1, this.midiFile.TicksPerQuarterNote);
                long previewTick = (long)Math.Round(this.previewAudio.CurrentTime.TotalSeconds / secondsPerTick);
                previewTick = Math.Clamp(previewTick, 0, Math.Max(this.GridTicks, this.Track.LengthTicks));
                float caretX = KeyLabelWidth + (float)((previewTick - visibleStartTick) * this.pixelsPerTick);
                if (caretX >= KeyLabelWidth && caretX <= KeyLabelWidth + width)
                {
                    using Pen caretPen = new(Color.Red, 2f);
                    e.Graphics.DrawLine(caretPen, caretX, 0, caretX, height);
                }
            }
        }

        private void pictureBox_editor_MouseDown(object? sender, MouseEventArgs e)
        {
            this.lastEditorPointer = e.Location;
            this.hasEditorPointer = true;
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
                this.BeginHistoryTransaction();
                this.selectionDragStart = this.ClampToEditor(e.Location);
                this.selectionDragEnd = this.selectionDragStart;
                this.pictureBox_editor.Capture = true;
                this.pictureBox_editor.Invalidate();
                return;
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

            if (this.panning)
            {
                this.PanView(e.Location.X - this.panStart.X);
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

        private async void pictureBox_editor_MouseUp(object? sender, MouseEventArgs e)
        {
            this.lastEditorPointer = e.Location;
            this.hasEditorPointer = true;
            if (this.ghostPreviewActive)
            {
                return;
            }

            if (this.panning)
            {
                this.panning = false;
                this.pictureBox_editor.Capture = false;
                this.pictureBox_editor.Cursor = Cursors.Cross;
                return;
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
                this.CommitHistoryTransaction();
                this.pictureBox_editor.Invalidate();
                return;
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

            if (this.checkBox_preview.Checked && this.editButton == MouseButtons.Left)
            {
                Point previewPoint = new((start.X + end.X) / 2, start.Y);
                MidiNoteData? previewNote = this.FindNoteAt(previewPoint);
                if (previewNote != null)
                {
                    try
                    {
                        await this.PreviewNoteAsync(previewNote);
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
            }
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

        private bool IsNoteSelected(MidiNoteData note) => this.noteSelection is MidiSelection selection
            && note.NoteNumber >= selection.LowestNote
            && note.NoteNumber <= selection.HighestNote
            && note.StartTick < selection.EndTick
            && note.StartTick + note.DurationTicks > selection.StartTick;

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
            int height = Math.Max(1, this.pictureBox_editor.ClientSize.Height - BottomMargin);
            int count = Math.Max(1, this.HighestNote - this.LowestNote + 1);

            this.Track.Notes.RemoveAll(note =>
            {
                float noteTop = NoteToY(note.NoteNumber, this.LowestNote, count, height);
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
            Math.Clamp(point.Y, 0, Math.Max(0, this.pictureBox_editor.ClientSize.Height - BottomMargin)));

        private long PointToTick(int x)
        {
            long relativeTick = (long)Math.Floor((x - KeyLabelWidth) / this.pixelsPerTick / this.GridTicks) * this.GridTicks;
            long tick = this.viewStartTick + relativeTick;
            return Math.Clamp(tick, 0, Math.Max(0, this.editorLengthTicks));
        }

        private int PointToNote(int y)
        {
            int height = Math.Max(1, this.pictureBox_editor.ClientSize.Height - BottomMargin);
            int count = Math.Max(1, this.HighestNote - this.LowestNote + 1);
            return Math.Clamp(this.HighestNote - (int)Math.Floor(y / (double)height * count), this.LowestNote, this.HighestNote);
        }

        private double CalculatePixelsPerTick(long visibleLengthTicks)
        {
            int width = Math.Max(1, this.pictureBox_editor.ClientSize.Width - KeyLabelWidth);
            return Math.Max(0.000001, width / (double)Math.Max(1, visibleLengthTicks));
        }

        private static float NoteToY(int note, int lowest, int count, int height) =>
            (count - 1 - (note - lowest)) / (float)Math.Max(1, count) * height;

        private static Rectangle NormalizeRectangle(Point first, Point second) => Rectangle.FromLTRB(
            Math.Min(first.X, second.X),
            Math.Min(first.Y, second.Y),
            Math.Max(first.X, second.X),
            Math.Max(first.Y, second.Y));

        private MidiFileData CreateOutputMidi()
        {
            MidiTrackData track = new()
            {
                Index = this.Track.Index,
                Name = string.IsNullOrWhiteSpace(this.Track.Name) ? "Piano Roll" : this.Track.Name
            };
            track.Notes.AddRange(this.Track.Notes
                .OrderBy(note => note.StartTick)
                .ThenBy(note => note.NoteNumber));
            return MidiFileData.CreateGenerated(
                [track],
                this.midiFile.TicksPerQuarterNote,
                this.Bpm,
                pitchFrequency: this.PitchFrequency,
                timeSignatureNumerator: this.midiFile.TimeSignatureNumerator,
                timeSignatureDenominator: this.midiFile.TimeSignatureDenominator);
        }

        private MidiFileData CreateOperationMidi()
        {
            IEnumerable<MidiNoteData> sourceNotes = this.Track.Notes;
            long offset = 0;
            if (this.noteSelection is MidiSelection selection)
            {
                sourceNotes = this.Track.Notes
                    .Where(note => note.NoteNumber >= selection.LowestNote
                        && note.NoteNumber <= selection.HighestNote
                        && note.StartTick < selection.EndTick
                        && note.StartTick + note.DurationTicks > selection.StartTick)
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

            try
            {
                MidiFileData midi = this.CreateOutputMidi();
                await this.PreviewMidiAsync(midi, midi.Tracks[0].Index, showCaret: true);
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

        private async Task PreviewMidiAsync(MidiFileData midi, int trackIndex, bool showCaret)
        {
            await this.StopPreviewAsync();
            CancellationTokenSource runCts = new();
            this.playbackCts = runCts;
            this.button_play.Text = "Stop";
            ProgressDialog? progressDialog = null;
            try
            {
                this.previewCaretVisible = showCaret;
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

        private void ApplyMidiFile(MidiFileData file)
        {
            this.midiFile = NormalizeEditorFile(file, this.Bpm);
            this.numericUpDown_bpm.Value = (decimal)Math.Clamp(this.midiFile.DefaultBpm, 20.0, 400.0);
            this.numericUpDown_pitchFrequency.Value = (decimal)Math.Clamp(this.midiFile.PitchFrequency, 1.0, 1000.0);
            long viewLength = Math.Max(this.GetRequiredEditorLength(), this.DefaultViewLengthTicks);
            this.editorLengthTicks = viewLength;
            this.SetView(0, viewLength);
            this.pixelsPerTick = this.CalculatePixelsPerTick(viewLength);
            this.UpdateScrollBar();
            this.pictureBox_editor.Invalidate();
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
                string? exportedPath = await this.CreateOutputMidi().ExportAsync(dialog.FileName);
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
                await this.StopPlaybackAsync();
                Task[] renders = this.activeRenders.ToArray();
                if (renders.Length > 0)
                {
                    await Task.WhenAll(renders);
                }
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
            this.previewAudio?.Dispose();
            this.playbackCts?.Dispose();
            this.saveRenderCts?.Dispose();
            this.customSample?.Dispose();
            this.sampleRenderCache.Dispose();
            this.customInstrumentSampleCache.Dispose();
            base.OnFormClosed(e);
        }
    }
}
