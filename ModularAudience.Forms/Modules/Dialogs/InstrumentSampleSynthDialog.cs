using ModularAudience.Audio;
using ModularAudience.Audio.Synthesis;
using NAudio.Wave;

namespace ModularAudience.Forms.Modules.Dialogs;

public sealed partial class InstrumentSampleSynthDialog : Form
{
    private static readonly object ActiveDialogsGate = new();
    private static readonly HashSet<InstrumentSampleSynthDialog> ActiveDialogs = [];
    private static readonly (string Label, double Value)[] OctaveOffsets =
    [
        ("0 (base)", 0), ("+8", 8), ("+4", 4), ("-4", -4), ("-8", -8)
    ];
    private static readonly (string Label, double Value)[] OctaveSteps =
    [
        ("2", 2), ("1", 1), ("0.5", 0.5), ("0.33", 1d / 3), ("0.25", 0.25), ("0.125", 0.125)
    ];
    private const long MaximumOctaveCollectionBytes = 512L * 1024 * 1024;

    private readonly SynthFamily family;
    private readonly Action<InstrumentSynthSettings>? onInstrumentConfirmed;
    private InstrumentSynthSettings settings;
    private readonly InstrumentSynthStore store;
    private readonly ComboBox savedPresets = Combo();
    private readonly TextBox presetName = new() { Width = 180, PlaceholderText = "Preset name" };
    private bool syncingPreference;
    private readonly ComboBox voice = Combo(), kit = Combo(), drum = Combo(), duration = Combo(), articulation = Combo(), format = Combo();
    private readonly DomainUpDown note = new() { ReadOnly = true, Wrap = true, Width = 90 };
    private readonly Dictionary<string, NumericUpDown> numbers = [];
    private readonly Label status = new() { Dock = DockStyle.Fill, AutoEllipsis = true, Text = "Preparing sample…" };
    private readonly WavePicture picture = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(22, 24, 30) };
    private readonly CheckBox loop = new() { Text = "Loop", AutoSize = true };
    private readonly NumericUpDown prehearDelay = new() { Minimum = 0, Maximum = 10000, Value = 200, Increment = 25, Width = 85 };
    private readonly System.Windows.Forms.Timer autoHearTimer = new();
    private bool initialized, autoHearPending, autoHearDue;
    private bool spaceKeyHeld;
    private int prehearRequestVersion;
    private readonly Button hear = new() { Text = "Prehear", AutoSize = true }, create = new() { Text = "Create", AutoSize = true };
    private readonly System.Windows.Forms.Timer debounce = new() { Interval = 180 }, playbackTimer = new() { Interval = 40 };
    private readonly string[] noteNames = Enumerable.Range(12, 97).Select(InstrumentSampleSynth.NoteName).ToArray();
    private static readonly (string Name, double Beats)[] Durations = [.. InstrumentSynthSettings.KeyDurations];
    private readonly ToolTip tips = new() { IsParameterHelp = true };
    private AudioObj? audio;
    private AudioCollectionView? collection;
    private CancellationTokenSource? rendering;
    private CancellationTokenSource? octaveRendering;
    private WaveOut? output;
    private SynthPreview? provider;
    private int revision, ready = -1;
    private bool syncing, disposed, creating;
    private bool octaveCreating;
    private double octaveOffsetSemitones;
    private double octaveStepSemitones = 1;
    private double[] overviewMin = [], overviewMax = [];
    private float peak;

    public InstrumentSampleSynthDialog(
        SynthFamily family,
        InstrumentSynthStore? store = null,
        Action<InstrumentSynthSettings>? onInstrumentConfirmed = null)
    {
        this.store = store ?? InstrumentSynthStore.Shared;
        this.onInstrumentConfirmed = onInstrumentConfirmed;
        this.family = family; settings = new() { Family = family }; settings.ApplyVoiceDefaults();
        this.InitializeSynthActionControls();
        KeyPreview = true;
        KeyDown += InstrumentSampleSynthDialog_KeyDown;
        KeyUp += InstrumentSampleSynthDialog_KeyUp;
        Deactivate += (_, _) => spaceKeyHeld = false;
        Text = $"Sample Synthesizer — {InstrumentSynthCatalog.FamilyName(family)}"; Name = "InstrumentSampleSynthDialog";
        ClientSize = new Size(1080, 780); MinimumSize = new Size(840, 620); StartPosition = FormStartPosition.CenterScreen;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1, Padding = new Padding(10) };
        root.ColumnStyles.Add(new(SizeType.Percent, 100)); root.RowStyles.Add(new(SizeType.AutoSize));
        root.RowStyles.Add(new(SizeType.AutoSize));
        root.RowStyles.Add(new(SizeType.Percent, 50)); root.RowStyles.Add(new(SizeType.Percent, 50)); root.RowStyles.Add(new(SizeType.Absolute, 55));
        var top = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        void Field(string label, Control control) { top.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(5, 8, 2, 0) }); top.Controls.Add(control); }
        if (family == SynthFamily.DrumSet)
        {
            kit.Items.AddRange(InstrumentSynthCatalog.DrumKits.Cast<object>().ToArray()); kit.SelectedIndex = 0;
            drum.Items.AddRange(Enum.GetValues<SynthDrum>().Cast<object>().ToArray()); drum.SelectedIndex = 0;
            Field("Drum Set Type", kit); Field("Drum Type", drum);
        }
        else
        {
            voice.Items.AddRange(InstrumentSynthCatalog.Voices(family).Cast<object>().ToArray()); voice.SelectedIndex = 0;
            Field(family switch
            {
                SynthFamily.Keyboard => "Keyboard Type",
                SynthFamily.OtherSoundFx => "Sound Effect Type",
                _ => "Instrument Type"
            }, voice);
        }
        note.Items.AddRange(noteNames.Cast<object>().ToArray()); note.SelectedItem = InstrumentSampleSynth.NoteName(settings.MidiNote);
        if (family != SynthFamily.DrumSet) Field("Key Note", note);
        duration.Items.AddRange(Durations.Select(d => (object)d.Name).ToArray());
        duration.SelectedIndex = family == SynthFamily.OtherSoundFx ? 5 : 4;
        articulation.Items.AddRange(["Straight", "Dotted", "Triplet"]); articulation.SelectedIndex = 0;
        Field("Key Duration", duration); Field("Timing", articulation);
        tips.SetToolTip(duration, "Single-note gate length at the selected BPM. Release adds a separate tail; no repeated note sequence.");
        var parameters = new FlowLayoutPanel { Name = "panel_parameters", Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true, Padding = new Padding(4) };
        void Number(string key, string label, decimal min, decimal max, decimal increment, string tooltip)
        {
            var panel = new FlowLayoutPanel { Width = 240, Height = 50, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            var caption = new Label { Text = label, AutoSize = true };
            var number = new NumericUpDown { Minimum = min, Maximum = max, Increment = increment, DecimalPlaces = increment < 1 ? 2 : 0, Width = 130 };
            numbers.Add(key, number); panel.Controls.Add(caption); panel.Controls.Add(number); parameters.Controls.Add(panel);
            tips.SetToolTip(number, tooltip); tips.SetToolTip(caption, tooltip);
            number.ValueChanged += (_, _) => QueueRender();
        }
        Number(nameof(settings.Bpm), "BPM", 20, 400, 0.5m, "Tempo used to calculate the musical gate length.");
        Number(nameof(settings.PitchSemitones), "Pitch (± semitones)", -16, 16, 0.25m, "Quarter-semitone pitch adjustment. Drums retain their type-specific base pitch.");
        Number(nameof(settings.AttackMs), "Attack (ms)", 0, 2000, 1, "Time to reach full amplitude.");
        Number(nameof(settings.DecaySeconds), "Decay (s)", 0.01m, 10, 0.01m, "Decay towards sustain; also controls partial damping and drum resonance.");
        Number(nameof(settings.SustainPercent), "Sustain (%)", 0, 100, 1, "Sustained envelope level while the key is held. Percussive presets default to zero.");
        Number(nameof(settings.ReleaseSeconds), "Release / tail (s)", 0.01m, 10, 0.01m, "Additional sample duration after key release; envelope fades to silence.");
        Number(nameof(settings.Brightness), "Brightness / hardness (%)", 0, 100, 1, "Harmonic content, modal damping and filter brightness.");
        Number(nameof(settings.Body), "Body / resonance (%)", 0, 100, 1, "Instrument body: string resonance, piano inharmonicity and wind formant character.");
        Number(nameof(settings.Noise),
            family == SynthFamily.OtherSoundFx ? "Noise / texture (%)" : "Breath / bow / strike noise (%)",
            0, 100, 1,
            family == SynthFamily.OtherSoundFx
                ? "Broadband texture for DJ Scratch and Radio Static."
                : "Breath for winds; bow texture for strings; hammer/strike/noise for percussion.");
        Number(nameof(settings.Drive), "Saturation / drive (%)", 0, 100, 1, "Nonlinear warmth and distortion.");
        if (family != SynthFamily.DrumSet)
        {
            Number(nameof(settings.DetuneCents), "Unison detune (cents)", 0, 50, 0.25m, "Partial detuning and unison voices; stereo width adds spatial separation.");
            Number(nameof(settings.VibratoCents), "Vibrato depth (cents)", 0, 100, 1, "Delayed pitch modulation on sustained wind, bowed and synth voices.");
            Number(nameof(settings.VibratoHz), "Vibrato / modulation rate (Hz)", 0, 20, 0.1m, "Vibrato rate; organ uses it for rotary movement and Wobble Bass uses it for filter-LFO speed.");
        }
        Number(nameof(settings.CutoffHz), "Low-pass cutoff (Hz)", 20, 24000, 50, "Resonant output filter; automatically limited below Nyquist.");
        Number(nameof(settings.Resonance), "Filter resonance Q", 0.1m, 12, 0.1m, "Resonance at the filter cutoff.");
        Number(nameof(settings.FilterEnvelope), "Filter envelope (%)", 0, 100, 1, "Initial cutoff lift, falling with decay; Wobble Bass also uses it as filter-LFO depth.");
        if (family is SynthFamily.PluckedStrings or SynthFamily.BowedStrings)
            Number(nameof(settings.StrikePosition), "Pluck / bow position (%)", 1, 99, 1, "Excitation position changes the harmonic notches and body response.");
        if (family is SynthFamily.DrumSet or SynthFamily.Organ)
            Number(nameof(settings.DrumSnap), family == SynthFamily.Organ ? "Percussion level (%)" : "Transient / snap (%)", 0, 100, 1, "Attack click / percussion component.");
        if (family == SynthFamily.DrumSet)
            Number(nameof(settings.DrumSweep), "Pitch sweep (%)", 0, 100, 1, "Exponential downward pitch sweep for membranes and kick attacks.");
        Number(nameof(settings.RoomPercent), "Room / early reflections (%)", 0, 100, 1, "Short stereo reflections; included in the release tail.");
        Number(nameof(settings.StereoWidth), "Stereo width (%)", 0, 100, 1, "Detuned spatial component and channel-offset reflections.");
        Number(nameof(settings.GainPercent), "Output level (%)", 0, 100, 1, "Final amplitude; peak protection attenuates only.");
        Number(nameof(settings.Seed), "Seed", 0, 999999, 1, "Repeatable strike/noise generation. Change this value for a fresh variant.");
        format.Items.AddRange(["44100 Hz · Stereo", "48000 Hz · Stereo", "22050 Hz · Mono", "44100 Hz · Mono", "96000 Hz · Stereo"]); format.SelectedIndex = 0;
        Field("Render format", format);
        Field("Prehear on change (ms)", prehearDelay);
        tips.SetToolTip(prehearDelay, "Wait after the last sound parameter change before playing the updated sample. 0 = off. If rendering takes longer, playback waits for the current sample.");
        prehearDelay.ValueChanged += (_, _) =>
        {
            prehearDelay.ForeColor = prehearDelay.Value == 0 ? SystemColors.GrayText : SystemColors.WindowText;
            CancelAutoHear();
            if (!syncingPreference) Safe(() => this.store.SavePrehearDelay((int)prehearDelay.Value));
        };
        picture.Paint += PaintWave;
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        bottom.Controls.Add(hear); bottom.Controls.Add(loop); bottom.Controls.Add(create);
        bottom.Controls.Add(this.button_createOctave); bottom.Controls.Add(this.button_ok);
        var randomize = new Button { Text = "New variation", AutoSize = true }; bottom.Controls.Add(randomize);
        randomize.Click += (_, _) => numbers[nameof(settings.Seed)].Value = Random.Shared.Next(1, 999999);
        bottom.Controls.Add(status); status.Width = 560; status.Height = 45; status.Dock = DockStyle.None;
        var presetBar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        presetBar.Controls.Add(new Label { Text = "Saved presets", AutoSize = true, Margin = new Padding(5, 8, 2, 0) });
        presetBar.Controls.Add(savedPresets); presetBar.Controls.Add(presetName);
        void PresetButton(string text, Action action)
        {
            var button = new Button { Text = text, AutoSize = true };
            button.Click += (_, _) => Safe(action); presetBar.Controls.Add(button);
        }
        PresetButton("Save preset", SavePreset);
        PresetButton("Load preset", LoadPreset);
        PresetButton("Rename preset", RenamePreset);
        PresetButton("Delete preset", DeletePreset);
        savedPresets.SelectedIndexChanged += (_, _) => { if (savedPresets.SelectedItem is string name) presetName.Text = name; };
        tips.SetToolTip(presetName, "Save creates or updates this named preset. Rename renames the selected preset to this name.");
        root.Controls.Add(top, 0, 0); root.Controls.Add(presetBar, 0, 1); root.Controls.Add(picture, 0, 2); root.Controls.Add(parameters, 0, 3); root.Controls.Add(bottom, 0, 4); Controls.Add(root);
        tips.SetToolTip(voice, "Choose the instrument voice within this family. Changing it loads voice-specific sound defaults; subsequent parameter changes customize the new voice.");
        tips.SetToolTip(kit, "Choose the drum-set character. Drum type selects the actual kick, snare, cymbal or other percussion voice within that set.");
        tips.SetToolTip(drum, "Percussion instrument to synthesize. Its characteristic base pitch and excitation remain type-specific; Pitch adds a relative tuning offset.");
        tips.SetToolTip(note, "Fundamental MIDI key for this single synthesized note. Duration and envelope control its gate and tail; this is not a repeating MIDI phrase.");
        tips.SetToolTip(articulation, "Straight uses the selected note length. Dotted multiplies it by 1.5; Triplet multiplies it by 2/3. BPM converts the resulting musical length to seconds.");
        tips.SetToolTip(format, "Sample rate and mono/stereo channel layout of the generated audio object. Filtering is limited below the selected rate's Nyquist frequency.");
        tips.SetToolTip(loop, "Repeat the single-note sample during prehear. This is an audition option; Create still produces one note plus its release tail.");
        tips.SetToolTip(savedPresets, "Saved presets for this synthesizer family. Select a name, then Load preset to restore its sound parameters; presets are stored in AppData.");
        Shown += (_, _) => FitInitialParameterHeight(parameters);
        voice.SelectedIndexChanged += (_, _) =>
        {
            if (syncing) return; settings.VoiceIndex = voice.SelectedIndex; settings.ApplyVoiceDefaults(); ApplyNumbers(); QueueRender();
        };
        foreach (var combo in new[] { kit, drum, duration, articulation, format }) combo.SelectedIndexChanged += (_, _) => QueueRender();
        note.SelectedItemChanged += (_, _) => QueueRender();
        hear.Click += async (_, _) => await ToggleHearAsync(); create.Click += async (_, _) => await CreateAsync();
        loop.CheckedChanged += (_, _) => { if (provider != null) provider.Loop = loop.Checked; };
        debounce.Tick += async (_, _) => { debounce.Stop(); await RenderAsync(); };
        autoHearTimer.Tick += (_, _) =>
        {
            autoHearTimer.Stop(); autoHearDue = true; TryAutoHear();
        };
        playbackTimer.Tick += (_, _) => { if (provider?.Finished == true) StopHear(); picture.Invalidate(); };
        FormClosing += (_, _) =>
        {
            prehearRequestVersion++;
            spaceKeyHeld = false;
            CancelAutoHear();
            rendering?.Cancel();
            this.octaveRendering?.Cancel();
            StopHear();
        };
        Safe(() => ReceivePrehearDelay(this.store.LoadPrehearDelay()));
        Safe(RefreshPresets);
        this.store.PrehearDelayChanged += ReceivePrehearDelay;
        this.store.PresetsChanged += ReceivePresetsChanged;
        ApplyNumbers(); QueueRender(); initialized = true;
        lock (ActiveDialogsGate) ActiveDialogs.Add(this);
    }
    private void FitInitialParameterHeight(FlowLayoutPanel parameters)
    {
        Rectangle workingArea = Screen.FromControl(this).WorkingArea;
        int maximumHeight = Math.Max(MinimumSize.Height, workingArea.Height);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            PerformLayout();
            // Measure at the full, unscrolled width after DPI scaling and wrapping.
            int preferredHeight = parameters.GetPreferredSize(new Size(parameters.Width, 0)).Height;
            // Waveform and parameters each receive half of the flexible height.
            int height = Math.Clamp(Height + 2 * (preferredHeight - parameters.ClientSize.Height), MinimumSize.Height, maximumHeight);
            if (height == Height) break;
            Height = height;
        }
        Location = new Point(workingArea.Left + Math.Max(0, (workingArea.Width - Width) / 2),
            workingArea.Top + Math.Max(0, (workingArea.Height - Height) / 2));
        // Shown runs only once. Later user resizes retain the normal AutoScroll behavior.
    }
    private void Safe(Action action)
    { try { action(); } catch (Exception error) { if (!disposed) status.Text = error.Message; } }
    private void ReceivePrehearDelay(int milliseconds)
    {
        if (disposed || IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(() => ReceivePrehearDelay(milliseconds)); return; }
        syncingPreference = true;
        try
        {
            prehearDelay.Value = milliseconds;
            prehearDelay.ForeColor = milliseconds == 0 ? SystemColors.GrayText : SystemColors.WindowText;
            CancelAutoHear();
        }
        finally { syncingPreference = false; }
    }
    private void ReceivePresetsChanged(SynthFamily changedFamily)
    {
        if (disposed || IsDisposed || changedFamily != family) return;
        if (InvokeRequired) { BeginInvoke(() => ReceivePresetsChanged(changedFamily)); return; }
        Safe(RefreshPresets);
    }
    private void RefreshPresets()
    {
        string? selected = savedPresets.SelectedItem as string;
        string[] names = store.Names(family);
        savedPresets.Items.Clear(); savedPresets.Items.AddRange(names);
        if (selected != null && names.Contains(selected)) savedPresets.SelectedItem = selected;
    }
    private void SavePreset()
    {
        string name = presetName.Text.Trim(); store.Save(family, name, CaptureSettings(), loop.Checked);
        savedPresets.SelectedItem = name; status.Text = "Preset saved: " + name;
    }
    private string SelectedPreset() => savedPresets.SelectedItem as string ?? throw new ArgumentException("Select a saved preset first.");
    private void LoadPreset()
    {
        var preset = store.Load(family, SelectedPreset()); ApplyPreset(preset);
    }
    private void RenamePreset()
    {
        string name = presetName.Text.Trim(); store.Rename(family, SelectedPreset(), name);
        savedPresets.SelectedItem = name; status.Text = "Preset renamed: " + name;
    }
    private void DeletePreset()
    {
        string name = SelectedPreset(); store.Delete(family, name); presetName.Clear(); status.Text = "Preset deleted: " + name;
    }
    private void ApplyPreset(InstrumentSynthPreset preset)
    {
        var next = preset.Settings.Clone(); next.Validate();
        if (next.Family != family) throw new ArgumentException("This preset belongs to a different instrument family.");
        int durationIndex = Array.FindIndex(Durations, d => Math.Abs(d.Beats - next.NoteBeats) < 1e-8);
        int timing = Math.Abs(next.DurationMultiplier - 1) < 1e-8 ? 0 : Math.Abs(next.DurationMultiplier - 1.5) < 1e-8 ? 1
            : Math.Abs(next.DurationMultiplier - 2d / 3) < 1e-8 ? 2 : -1;
        int formatIndex = (next.SampleRate, next.Channels) switch { (44100, 2) => 0, (48000, 2) => 1, (22050, 1) => 2, (44100, 1) => 3, (96000, 2) => 4, _ => -1 };
        if (durationIndex < 0 || timing < 0 || formatIndex < 0) throw new ArgumentException("This preset uses timing or an audio format not supported by this dialog.");
        StopHear(); CancelAutoHear(); syncing = true;
        try
        {
            settings = next;
            if (family == SynthFamily.DrumSet) { kit.SelectedIndex = next.KitIndex; drum.SelectedIndex = (int)next.Drum; }
            else voice.SelectedIndex = next.VoiceIndex;
            note.SelectedItem = InstrumentSampleSynth.NoteName(next.MidiNote);
            duration.SelectedIndex = durationIndex; articulation.SelectedIndex = timing; format.SelectedIndex = formatIndex;
            loop.Checked = preset.Loop; presetName.Text = preset.Name;
            ApplyNumbers();
        }
        finally { syncing = false; }
        QueueRender();
    }
    private static ComboBox Combo() => new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
    private void ApplyNumbers()
    {
        bool previousSyncing = syncing; syncing = true;
        try { foreach (var pair in numbers) pair.Value.Value = Math.Clamp(Convert.ToDecimal(typeof(InstrumentSynthSettings).GetProperty(pair.Key)!.GetValue(settings)), pair.Value.Minimum, pair.Value.Maximum); }
        finally { syncing = previousSyncing; }
    }
    private InstrumentSynthSettings CaptureSettings()
    {
        var snapshot = settings.Clone();
        foreach (var pair in numbers)
        {
            var property = typeof(InstrumentSynthSettings).GetProperty(pair.Key)!;
            property.SetValue(snapshot, Convert.ChangeType(pair.Value.Value, property.PropertyType));
        }
        if (family == SynthFamily.DrumSet) { snapshot.KitIndex = kit.SelectedIndex; snapshot.Drum = (SynthDrum)drum.SelectedIndex; }
        else snapshot.VoiceIndex = voice.SelectedIndex;
        snapshot.MidiNote = Math.Max(0, Array.IndexOf(noteNames, note.Text)) + 12;
        snapshot.NoteBeats = Durations[Math.Max(0, duration.SelectedIndex)].Beats;
        snapshot.DurationMultiplier = articulation.SelectedIndex switch { 1 => 1.5, 2 => 2d / 3, _ => 1 };
        snapshot.SampleRate = format.SelectedIndex switch { 1 => 48000, 2 => 22050, 4 => 96000, _ => 44100 };
        snapshot.Channels = format.SelectedIndex is 2 or 3 ? 1 : 2;
        snapshot.Validate(); return snapshot;
    }
    private void QueueRender()
    {
        if (syncing || disposed) return;
        revision++; rendering?.Cancel(); debounce.Stop(); debounce.Start(); status.Text = "Updating sample…";
        CancelAutoHear();
        if (initialized && prehearDelay.Value > 0)
        {
            StopHear();
            autoHearPending = true; autoHearTimer.Interval = (int)prehearDelay.Value; autoHearTimer.Start();
        }
    }
    private void CancelAutoHear()
    { autoHearTimer.Stop(); autoHearPending = false; autoHearDue = false; }
    private void TryAutoHear()
    {
        if (disposed || !autoHearPending || !autoHearDue || prehearDelay.Value == 0 || ready != revision || audio == null) return;
        CancelAutoHear();
        try { StopHear(); StartHear(); }
        catch (Exception error) { StopHear(); if (!disposed) status.Text = error.Message; }
    }
    private async Task RenderAsync()
    {
        if (disposed) return;
        var cts = new CancellationTokenSource(); rendering?.Cancel(); rendering = cts; int version = revision;
        try
        {
            var snapshot = CaptureSettings(); var next = await Task.Run(() => InstrumentSampleSynth.Render(snapshot, cts.Token), cts.Token);
            if (disposed || version != revision || cts.IsCancellationRequested) { next.Dispose(); return; }
            var old = audio; audio = next; ready = version;
            if (provider != null && (provider.WaveFormat.SampleRate != next.SampleRate || provider.WaveFormat.Channels != next.Channels)) StopHear();
            BuildOverview(next); provider?.Update(next.Data); old?.Dispose(); picture.Invalidate();
            status.Text = $"{snapshot.VoiceName} · Gate {snapshot.GateSeconds:0.###} s + tail {snapshot.ReleaseSeconds:0.##} s · {next.Duration.TotalSeconds:0.###} s · {next.SampleRate} Hz";
            TryAutoHear();
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!disposed && version == revision) status.Text = error.Message; }
        finally { if (ReferenceEquals(rendering, cts)) rendering = null; cts.Dispose(); }
    }
    private async Task<bool> EnsureRendered()
    {
        debounce.Stop(); if (ready != revision) await RenderAsync();
        return !disposed && audio != null && ready == revision;
    }
    private async Task ToggleHearAsync()
    {
        CancelAutoHear();
        if (output != null)
        {
            prehearRequestVersion++;
            StopHear();
            return;
        }

        await StartPrehearAsync();
    }

    private async void InstrumentSampleSynthDialog_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Space || e.Alt || presetName.Focused)
        {
            return;
        }

        e.Handled = true;
        e.SuppressKeyPress = true;
        if (!ShouldStartSpacePrehear(e.KeyCode, textInputFocused: false, spaceKeyHeld))
        {
            return;
        }

        spaceKeyHeld = true;
        await RestartPrehearFromSpaceAsync();
    }

    private void InstrumentSampleSynthDialog_KeyUp(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space)
        {
            spaceKeyHeld = false;
        }
    }

    internal static bool ShouldStartSpacePrehear(Keys keyCode, bool textInputFocused, bool keyAlreadyHeld)
        => keyCode == Keys.Space && !textInputFocused && !keyAlreadyHeld;

    private async Task RestartPrehearFromSpaceAsync()
    {
        Exception? stopError = StopAllPrehearPlayback();
        if (stopError is not null)
        {
            if (!disposed)
            {
                status.Text = $"Could not stop existing prehear: {stopError.Message}";
            }

            return;
        }

        await StartPrehearAsync();
    }

    private async Task StartPrehearAsync()
    {
        int requestVersion = ++prehearRequestVersion;
        try
        {
            if (!await EnsureRendered() || requestVersion != prehearRequestVersion) return;
            StartHear();
        }
        catch (Exception error) { StopHear(); if (!disposed) status.Text = error.Message; }
    }

    private static Exception? StopAllPrehearPlayback()
    {
        InstrumentSampleSynthDialog[] dialogs;
        lock (ActiveDialogsGate)
        {
            dialogs = ActiveDialogs.ToArray();
        }

        Exception? stopError = null;
        foreach (InstrumentSampleSynthDialog dialog in dialogs)
        {
            if (dialog.IsDisposed || dialog.Disposing)
            {
                continue;
            }

            try
            {
                if (dialog.InvokeRequired)
                {
                    dialog.Invoke((Action)dialog.StopPrehearPlayback);
                }
                else
                {
                    dialog.StopPrehearPlayback();
                }
            }
            catch (Exception error)
            {
                stopError ??= error;
                if (!dialog.disposed && !dialog.IsDisposed && !dialog.Disposing)
                {
                    dialog.status.Text = error.Message;
                }
            }
        }

        return stopError;
    }

    private void StopPrehearPlayback()
    {
        prehearRequestVersion++;
        CancelAutoHear();
        StopHear();
    }
    private void StartHear()
    {
        provider = new SynthPreview(audio!.SampleRate, audio.Channels) { Loop = loop.Checked }; provider.Update(audio.Data);
        output = new WaveOut { BufferMilliseconds = 60 }; output.Init(provider); output.Play(); hear.Text = "Stop prehear"; playbackTimer.Start();
    }
    private void StopHear() { playbackTimer.Stop(); output?.Stop(); output?.Dispose(); output = null; provider = null; hear.Text = "Prehear"; }
    private async Task CreateAsync()
    {
        if (creating) return; creating = true; create.Enabled = false;
        try { if (await EnsureRendered()) AddToCollection(); }
        catch (Exception error) { if (!disposed) status.Text = error.Message; }
        finally { creating = false; if (!disposed) create.Enabled = true; }
    }

    private void Confirm()
    {
        if (creating) return;
        creating = true;
        this.button_ok.Enabled = false;
        try
        {
            if (this.onInstrumentConfirmed is not null)
                this.onInstrumentConfirmed(CaptureSettings());
            Close();
        }
        catch (Exception error)
        {
            if (!disposed) status.Text = error.Message;
        }
        finally
        {
            creating = false;
            if (!disposed) this.button_ok.Enabled = true;
        }
    }

    private void SetOctaveOffset(double semitones)
    {
        if (!OctaveOffsets.Any(option => option.Value == semitones))
        {
            throw new ArgumentOutOfRangeException(nameof(semitones));
        }

        this.octaveOffsetSemitones = semitones;
        this.UpdateOctaveMenuChecks();
    }

    private void SetOctaveStep(double semitones)
    {
        if (!OctaveSteps.Any(option => option.Value == semitones))
        {
            throw new ArgumentOutOfRangeException(nameof(semitones));
        }

        this.octaveStepSemitones = semitones;
        this.UpdateOctaveMenuChecks();
    }

    private void UpdateOctaveMenuChecks()
    {
        foreach (ToolStripMenuItem item in this.octaveOffsetItems)
        {
            item.Checked = item.Tag is double value && value == this.octaveOffsetSemitones;
        }

        foreach (ToolStripMenuItem item in this.octaveStepItems)
        {
            item.Checked = item.Tag is double value && value == this.octaveStepSemitones;
        }
    }

    internal static double[] GetOctaveTranspositions(double step)
    {
        if (!OctaveSteps.Any(option => option.Value == step))
        {
            throw new ArgumentOutOfRangeException(nameof(step));
        }

        int intervals = (int)Math.Round(12 / step);
        return Enumerable.Range(0, intervals + 1)
            .Select(index => index == intervals ? 12d : index * step)
            .ToArray();
    }

    internal static InstrumentSynthSettings CreatePitchedSettings(InstrumentSynthSettings source, double semitones)
    {
        InstrumentSynthSettings pitched = source.Clone();
        if (pitched.Family == SynthFamily.DrumSet)
        {
            pitched.PitchSemitones += semitones;
        }
        else
        {
            double midiPitch = pitched.MidiNote + pitched.PitchSemitones + semitones;
            if (!double.IsFinite(midiPitch) || midiPitch is < 0 or > 127)
            {
                throw new InvalidOperationException("The generated octave is outside the supported MIDI range (0–127).");
            }

            int midiNote = (int)Math.Round(midiPitch);
            pitched.MidiNote = midiNote;
            pitched.PitchSemitones = midiPitch - midiNote;
        }

        pitched.Validate();
        return pitched;
    }

    private static long EstimateRenderBytes(InstrumentSynthSettings settings)
    {
        double frames = Math.Ceiling((settings.GateSeconds + settings.ReleaseSeconds) * settings.SampleRate);
        return checked((long)frames * settings.Channels * sizeof(float));
    }

    private async Task CreateOctaveAsync()
    {
        if (this.octaveCreating) return;
        this.octaveCreating = true;
        this.button_createOctave.Enabled = false;
        CancellationTokenSource cancellation = new();
        this.octaveRendering = cancellation;
        List<AudioObj> generated = [];
        try
        {
            InstrumentSynthSettings snapshot = CaptureSettings();
            double[] transpositions = GetOctaveTranspositions(this.octaveStepSemitones);
            InstrumentSynthSettings[] renderSettings = transpositions
                .Select(offset => CreatePitchedSettings(snapshot, this.octaveOffsetSemitones + offset))
                .ToArray();

            long estimatedBytes = 0;
            foreach (InstrumentSynthSettings item in renderSettings)
            {
                estimatedBytes = checked(estimatedBytes + EstimateRenderBytes(item));
            }

            if (estimatedBytes > MaximumOctaveCollectionBytes)
            {
                throw new InvalidOperationException(
                    $"This octave would require {estimatedBytes / (1024d * 1024d):0.#} MiB of sample data. "
                    + "Shorten the key duration, lower the sample rate, or choose a larger step.");
            }

            for (int index = 0; index < renderSettings.Length; index++)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                if (!disposed)
                {
                    status.Text = $"Rendering octave sample {index + 1} of {renderSettings.Length}…";
                }

                AudioObj rendered = await Task.Run(
                    () => InstrumentSampleSynth.Render(renderSettings[index], cancellation.Token),
                    cancellation.Token);
                if (snapshot.Family == SynthFamily.DrumSet)
                {
                    rendered.Tag = GetDrumsetElement(snapshot.Drum);
                }

                generated.Add(rendered);
            }

            cancellation.Token.ThrowIfCancellationRequested();
            if (disposed || IsDisposed) return;

            AudioCollectionView octaveCollection = new(
                generated,
                $"{InstrumentSynthCatalog.FamilyName(snapshot.Family)} - {snapshot.VoiceName} Octave");
            generated.Clear();
            try
            {
                octaveCollection.Show();
            }
            catch
            {
                octaveCollection.Dispose();
                throw;
            }

            status.Text = $"Created {renderSettings.Length} octave samples.";
        }
        catch (OperationCanceledException)
        {
            if (!disposed) status.Text = "Octave generation cancelled.";
        }
        catch (Exception error)
        {
            if (!disposed) status.Text = error.Message;
        }
        finally
        {
            foreach (AudioObj sample in generated) sample.Dispose();
            if (ReferenceEquals(this.octaveRendering, cancellation)) this.octaveRendering = null;
            cancellation.Dispose();
            this.octaveCreating = false;
            if (!disposed) this.button_createOctave.Enabled = true;
        }
    }

    private void AddToCollection()
    {
        if (audio == null) throw new InvalidOperationException("Render a valid sample first.");
        if (collection == null || collection.IsDisposed) collection = new AudioCollectionView([], "Synthesized " + InstrumentSynthCatalog.FamilyName(family));
        var created = audio.Clone();
        if (family == SynthFamily.DrumSet)
            created.Tag = GetDrumsetElement((SynthDrum)drum.SelectedItem!);
        string basis = audio.Name, name = basis; int index = 2;
        while (collection.AudioC.Audios.Any(a => a.Name == name)) name = basis + " " + index++;
        created.Rename(name); collection.AudioC.Audios.Add(created); collection.Show();
        status.Text = "Created: " + name;
    }

    private static DrumsetElement GetDrumsetElement(SynthDrum drum) => drum switch
    {
        SynthDrum.Kick or SynthDrum.BassDrum => DrumsetElement.Kick,
        SynthDrum.Snare => DrumsetElement.Snare,
        SynthDrum.Clap => DrumsetElement.Clap,
        SynthDrum.Rimshot => DrumsetElement.Rim,
        SynthDrum.PedalHiHat => DrumsetElement.HiHatClosed,
        SynthDrum.ClosedHiHat => DrumsetElement.HiHatClosed,
        SynthDrum.OpenHiHat => DrumsetElement.HiHatOpen,
        SynthDrum.LowTom => DrumsetElement.TomLow,
        SynthDrum.MidTom => DrumsetElement.TomMid,
        SynthDrum.HighTom => DrumsetElement.TomHigh,
        SynthDrum.Ride => DrumsetElement.Ride,
        SynthDrum.Crash => DrumsetElement.CrashLong,
        SynthDrum.Cowbell => DrumsetElement.Cowbell,
        _ => DrumsetElement.Shaker
    };
    private void BuildOverview(AudioObj value)
    {
        peak = 0;
        int frames = value.Data.Length / value.Channels, bins = Math.Min(4096, frames);
        overviewMin = new double[bins]; overviewMax = new double[bins];
        for (int b = 0; b < bins; b++)
            for (int i = b * frames / bins; i < (b + 1) * frames / bins; i++)
                for (int c = 0; c < value.Channels; c++)
                { float v = value.Data[i * value.Channels + c]; overviewMin[b] = Math.Min(overviewMin[b], v); overviewMax[b] = Math.Max(overviewMax[b], v); peak = Math.Max(peak, Math.Abs(v)); }
    }
    private void PaintWave(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics; float middle = picture.Height / 2f, scale = Math.Max(1, picture.Height / 2f - 18);
        using var grid = new Pen(Color.FromArgb(55, 60, 70)); using var wave = new Pen(Color.Turquoise);
        for (int i = 0; i <= 8; i++) g.DrawLine(grid, i * picture.Width / 8, 0, i * picture.Width / 8, picture.Height);
        g.DrawLine(grid, 0, middle, picture.Width, middle);
        for (int x = 0; x < picture.Width && overviewMin.Length > 0; x++)
        {
            int a = x * overviewMin.Length / Math.Max(1, picture.Width), b = Math.Min(overviewMin.Length, Math.Max(a + 1, (x + 1) * overviewMin.Length / Math.Max(1, picture.Width)));
            double min = 0, max = 0; for (int j = a; j < b; j++) { min = Math.Min(min, overviewMin[j]); max = Math.Max(max, overviewMax[j]); }
            g.DrawLine(wave, x, middle - (float)max * scale, x, middle - (float)min * scale);
        }
        if (audio != null)
        {
            g.DrawString($"0 s                                                       {audio.Duration.TotalSeconds:0.###} s · Peak {peak:0.000}", Font, Brushes.Silver, 5, picture.Height - 20);
            if (provider != null) { float x = provider.Progress * picture.Width; g.DrawLine(Pens.OrangeRed, x, 0, x, picture.Height); }
        }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            disposed = true; store.PrehearDelayChanged -= ReceivePrehearDelay; store.PresetsChanged -= ReceivePresetsChanged;
            prehearRequestVersion++;
            spaceKeyHeld = false;
            lock (ActiveDialogsGate) ActiveDialogs.Remove(this);
            CancelAutoHear(); autoHearTimer.Dispose(); rendering?.Cancel(); this.octaveRendering?.Cancel();
            StopHear(); debounce.Dispose(); playbackTimer.Dispose(); tips.Dispose(); audio?.Dispose(); this.components?.Dispose();
        }
        base.Dispose(disposing);
    }
    private sealed class WavePicture : PictureBox
    { public WavePicture() { DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw, true); } }
    private sealed class SynthPreview : ISampleProvider
    {
        private readonly object gate = new(); private float[] data = []; private int position; private float fade;
        public WaveFormat WaveFormat { get; }
        public bool Loop { get; set; }
        public bool Finished { get; private set; }
        public float Progress { get { lock (gate) return position / (float)Math.Max(1, data.Length); } }
        public SynthPreview(int rate, int channels) => WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(rate, channels);
        public void Update(float[] next) { lock (gate) { data = next; position = 0; fade = 0; Finished = false; } }
        public int Read(Span<float> buffer)
        {
            lock (gate)
            {
                int channels = WaveFormat.Channels;
                for (int i = 0; i < buffer.Length; i += channels)
                {
                    if (position >= data.Length) { if (Loop) { position = 0; fade = 0; } else Finished = true; }
                    fade = Math.Min(1, fade + 1f / Math.Max(1, WaveFormat.SampleRate / 250));
                    for (int c = 0; c < channels && i + c < buffer.Length; c++)
                        buffer[i + c] = !Finished && position + c < data.Length ? data[position + c] * fade : 0;
                    if (!Finished) position += channels;
                }
                return buffer.Length;
            }
        }
    }
}
