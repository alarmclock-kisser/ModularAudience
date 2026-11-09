namespace ModularAudience.Forms.Hints;

/// <summary>Interaction documentation lives separately from designer layout and parameter help.</summary>
internal static class HintContent
{
    internal static void Apply(AppToolTip tips, Control root, IReadOnlyDictionary<string, string> content)
    {
        foreach (var (name, text) in content)
            foreach (Control control in root.Controls.Find(name, searchAllChildren: true)) tips.SetToolTip(control, text);
    }

    internal static readonly Dictionary<string, string> Main = new()
    {
        ["button_import"] = "Import audio or MIDI files into the current import workflow.\nClick: select files. Shift+click: select a folder. The Structure and One Bag options control how imported files are grouped.",
        ["button_newTrack"] = "Create a new audio track.\nRight-click: browse YouTube, synthesize an instrument sample or open the Waveform Modeller. These tools create audio objects without replacing existing tracks.",
        ["button_newBag"] = "Create an empty audio collection (bag). Drag audio between collections to organize sources for playback, editing and mixes.",
        ["button_export"] = "Export audio using the global format and quality below. Export creates files; it does not change the audio currently held in RAM.",
        ["comboBox_exportFormat"] = "Global audio export format. WAV uses PCM bit depth; MP3 uses bitrate in kbps. Format and quality are saved in AppData and shared with other application instances.",
        ["comboBox_exportBits"] = "Export quality for the selected format: PCM bits for WAV, kbps for MP3. Higher settings use more disk space; they do not recover detail missing from the source.",
        ["checkBox_structure"] = "Keep imported folder structure when distributing files to audio collections. This preference is saved; it does not reorganize collections already open.",
        ["checkBox_oneBag"] = "Put new imports into one bag instead of distributing them across separate collections. Existing audio and collection contents are unchanged.",
        ["button_record"] = "Start or stop recording the master output.\nRight-click: configure the negative start offset (pre-roll). The track log records playback transitions during the recording.",
        ["button_random"] = "Import a random track from the focused track's root directory.\nRight-click: choose whether imported tracks are stretched to the target tempo.",
        ["button_loopControl"] = "Open the ensemble loop controller. Choose the active/checked track group there before applying synchronized loop, jump or capture actions.",
        ["vScrollBar_masterLimiter"] = "Master output attenuation: 100% leaves the level unchanged; lower values reduce all playback. This is a linear gain limit, not a dB value, and does not rewrite source samples.",
        ["button_copyLog"] = "Copy application diagnostics. Hover a log entry to read its complete message, even when global hints are disabled. The log's right-click menu offers additional copy/filter actions."
    };

    internal static readonly Dictionary<string, string> Track = new()
    {
        ["pictureBox_waveform"] = "Waveform editor: drag with the left mouse button to select audio; drag a selection edge to resize it.\nRight-click: seek, copy, edit or apply effects to the selection.\nMouse wheel: pan while stopped; Ctrl+wheel: zoom around the pointer. During playback the wheel zooms the view.",
        ["button_playback"] = "Start or stop this track. Ctrl+click applies the playback toggle to all TrackViews. Playback and editing use this window's audio clone; the collection source remains separate.",
        ["button_pause"] = "Pause or resume the current playback position. Ctrl+click applies the pause/resume action across TrackViews rather than restarting playback.",
        ["button_loop"] = "Enable or cycle the track's loop range. Click cycles forward; Shift+click cycles backward; Ctrl+click disables the loop. The highlighted waveform range shows the active boundaries.",
        ["checkBox_sync"] = "Include this standalone track in synchronized playback/rate actions. Ctrl+click toggles the sync state across TrackViews. Joined mix tracks follow their mix transport instead.",
        ["checkBox_mute"] = "Silence this track's output without deleting audio or losing its playback position. The volume control and source sample data are preserved.",
        ["checkBox_solo"] = "Listen to this track while other non-solo tracks are suppressed. This changes monitoring, not exported source sample data.",
        ["checkBox_join"] = "Join a non-destructive multi-track mix. Right-click to select or create a mix; the original audio remains available and the mix supplies the shared transport.",
        ["hScrollBar_rate"] = "Playback rate / varispeed changes speed and pitch together. Ctrl applies rate changes across TrackViews; synced tracks share changes. Right-click to jump to a rate or reset the center.",
        ["hScrollBar_offset"] = "Pan the visible waveform while playback is stopped. This changes the editor view, not the sample's start time or audio content.",
        ["vScrollBar_volume"] = "Adjust this track's playback level. Muting preserves the chosen level. Source audio samples are not rewritten by the volume scrollbar."
    };

    internal static readonly Dictionary<string, string> Collection = new()
    {
        ["listBox_audios"] = "Audio collection: click to select; Ctrl+click toggles individual items; Shift extends the selection.\nDouble-click opens the audio in a TrackView. Drag selected audio to another bag or a mix. Right-click for sample, effect, export and organization actions.",
        ["checkBox_autoPlay"] = "Automatically audition the selected audio (up to 10 seconds). Ctrl+click shares this preference with open and future collections. Turning visual hints off does not disable audio audition.",
        ["checkBox_preview"] = "Show a floating waveform preview when hovering audio. Hovering selected items in a multi-selection can show their combined preview. Ctrl+click shares this choice with open and future collections. The global hints switch temporarily suppresses these popups without forgetting this choice.",
        ["button_export"] = "Export selected audio using the global output format and quality.\nShift+click exports every item in the collection. Ctrl+click with one selected item opens Save As; with multiple selected items it chooses a destination folder. Ctrl+Shift exports the whole collection to a chosen folder."
    };

    internal static readonly Dictionary<string, string> Midi = new()
    {
        ["pictureBox_midi"] = "Inspect the currently selected MIDI page. Drag a rectangle over notes to edit that range. The derived Merged page is read-only: edit its original pages and it updates automatically.",
        ["numericUpDown_track"] = "Choose an original MIDI page. If pages have been added with Merge, Merged is the last page and can be previewed, rendered or exported by itself.",
        ["numericUpDown_bpm"] = "Tempo for MIDI audio rendering and preview. Notes keep their MIDI tick positions; changing BPM changes their playback timing.",
        ["comboBox_instrument"] = "Choose the sound used for preview and audio rendering. Custom Sample uses a selected audio object; Custom Collection chooses samples from an open collection. MIDI file export keeps notes rather than rendered sound.",
        ["comboBox_renderQuality"] = "Choose the rendering quality/performance trade-off. Higher quality can require more processing, especially when custom samples must be pitched and stretched.",
        ["button_customInstrument"] = "Select an open audio object or collection for the custom instrument. Choose Custom Sample or Custom Collection in Instrument first.",
        ["button_preview"] = "Preview only the current MIDI page with the selected instrument, tempo and quality. Select Merged to hear its chosen pages together. Click again to stop or cancel rendering.",
        ["button_save"] = "Click: render all original MIDI pages together into one audio object.\nRight-click → Render this MIDI page only: render the displayed original or Merged page with the current instrument. The derived page is never counted twice.",
        ["button_export"] = "Click: export ONE .mid file containing all original pages as separate MIDI tracks.\nRight-click: export only the displayed page, or merge all originals into one track inside ONE MIDI file. No per-track file splitting."
    };

    internal static readonly Dictionary<string, string> Loops = new()
    {
        ["button_loop"] = "Set or toggle a musical loop fraction on the focused row. Ctrl targets checked active rows; an empty checked group does nothing. If every target already uses the fraction, the action turns those loops off.",
        ["button_copy"] = "Capture playing audio into a new audio object. Normal click captures the active playback ensemble; Ctrl+click captures only looping tracks using their playback rates. Source tracks are not replaced.",
        ["domainUpDown_multiplier"] = "Loop/capture duration multiplier. This determines capture duration when no loop is active. Ctrl applies multiplier changes to the checked target group.",
        ["numericUpDown_jump"] = "Forward/backward jump distance in milliseconds. Use the jump buttons to move the focused target; Ctrl applies actions to the checked active group.",
        ["button_forward"] = "Move the focused target forward by Jump ms. Ctrl applies the jump to checked active targets; it does not select or enable additional tracks.",
        ["button_backward"] = "Move the focused target backward by Jump ms. Ctrl applies the jump to checked active targets; source sample data stays unchanged."
    };

    internal static readonly Dictionary<string, string> MidiEditor = new()
    {
        ["pictureBox_editor"] = "Piano roll: left-drag adds a note; right-drag deletes notes; Shift+right-drag deletes notes inside a rectangle.\nMouse wheel pans horizontally; Ctrl+wheel zooms around the pointer; Ctrl+drag pans. Grid notes controls placement granularity.",
        ["button_save"] = "Apply this edited range back to its source MIDI page and close. A standalone edit opens a MIDI Renderer window. This Save applies MIDI edits; audio rendering is available in the renderer.",
        ["button_play"] = "Preview the edited MIDI page using its source renderer's instrument and tempo. Click again to stop. A red caret follows playback.",
        ["numericUpDown_pitchFrequency"] = "Reference tuning for MIDI note A4 in hertz (normally 440 Hz). The edited file retains this tuning for subsequent audio rendering.",
        ["domainUpDown_noteGranularity"] = "Select the note-placement grid. Finer grids permit shorter notes and smaller time steps; existing notes are not automatically quantized."
    };
}
