using ModularAudience.Audio;
using ModularAudience.Audio.Synthesis;
using NAudio.Wave;

namespace ModularAudience.Forms.Modules.Dialogs;

public sealed class InstrumentSampleSynthDialog : Form
{
    private readonly SynthFamily family;
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
    private readonly Button hear = new() { Text = "Prehear", AutoSize = true }, create = new() { Text = "Create", AutoSize = true };
    private readonly System.Windows.Forms.Timer debounce = new() { Interval = 180 }, playbackTimer = new() { Interval = 40 };
    private readonly string[] noteNames = Enumerable.Range(12, 97).Select(InstrumentSampleSynth.NoteName).ToArray();
    private static readonly (string Name, double Beats)[] Durations =
    [ ("1/64", 0.0625), ("1/32", 0.125), ("1/16", 0.25), ("1/8", 0.5), ("1/4", 1), ("1/2", 2), ("1 Bar", 4), ("2 Bars", 8), ("4 Bars", 16) ];
    private readonly ToolTip tips = new();
    private AudioObj? audio;
    private AudioCollectionView? collection;
    private CancellationTokenSource? rendering;
    private WaveOut? output;
    private SynthPreview? provider;
    private int revision, ready = -1;
    private bool syncing, disposed, creating;
    private double[] overviewMin = [], overviewMax = [];
    private float peak;

    public InstrumentSampleSynthDialog(SynthFamily family, InstrumentSynthStore? store = null)
    {
        this.store = store ?? InstrumentSynthStore.Shared;
        this.family = family; settings = new() { Family = family }; settings.ApplyVoiceDefaults();
        if (family is SynthFamily.SynthBassAndLead or SynthFamily.BowedStrings) settings.MidiNote = 48;
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
            Field(family == SynthFamily.Keyboard ? "Keyboard Type" : "Instrument Type", voice);
        }
        note.Items.AddRange(noteNames.Cast<object>().ToArray()); note.SelectedItem = InstrumentSampleSynth.NoteName(settings.MidiNote);
        if (family != SynthFamily.DrumSet) Field("Key Note", note);
        duration.Items.AddRange(Durations.Select(d => (object)d.Name).ToArray()); duration.SelectedIndex = 4;
        articulation.Items.AddRange(["Straight", "Dotted", "Triplet"]); articulation.SelectedIndex = 0;
        Field("Key Duration", duration); Field("Timing", articulation);
        tips.SetToolTip(duration, "Single-note gate length at the selected BPM. Release adds a separate tail; no repeated note sequence.");
        var parameters = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true, Padding = new Padding(4) };
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
        Number(nameof(settings.Noise), "Breath / bow / strike noise (%)", 0, 100, 1, "Breath for winds; bow texture for strings; hammer/strike/noise for percussion.");
        Number(nameof(settings.Drive), "Saturation / drive (%)", 0, 100, 1, "Nonlinear warmth and distortion.");
        if (family != SynthFamily.DrumSet)
        {
            Number(nameof(settings.DetuneCents), "Unison detune (cents)", 0, 50, 0.25m, "Partial detuning and unison voices; stereo width adds spatial separation.");
            Number(nameof(settings.VibratoCents), "Vibrato depth (cents)", 0, 100, 1, "Delayed pitch modulation on sustained wind, bowed and synth voices.");
            Number(nameof(settings.VibratoHz), "Vibrato / rotary rate (Hz)", 0, 20, 0.1m, "Vibrato rate; organ also uses it for the rotary amplitude movement.");
        }
        Number(nameof(settings.CutoffHz), "Low-pass cutoff (Hz)", 20, 24000, 50, "Resonant output filter; automatically limited below Nyquist.");
        Number(nameof(settings.Resonance), "Filter resonance Q", 0.1m, 12, 0.1m, "Resonance at the filter cutoff.");
        Number(nameof(settings.FilterEnvelope), "Filter envelope (%)", 0, 100, 1, "Initial cutoff lift, falling with decay.");
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
        FormClosing += (_, _) => { CancelAutoHear(); rendering?.Cancel(); StopHear(); };
        Safe(() => ReceivePrehearDelay(this.store.LoadPrehearDelay()));
        Safe(RefreshPresets);
        this.store.PrehearDelayChanged += ReceivePrehearDelay;
        this.store.PresetsChanged += ReceivePresetsChanged;
        ApplyNumbers(); QueueRender(); initialized = true;
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
        if (output != null) { StopHear(); return; }
        try
        {
            if (!await EnsureRendered()) return;
            StartHear();
        }
        catch (Exception error) { StopHear(); if (!disposed) status.Text = error.Message; }
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
    private void AddToCollection()
    {
        if (audio == null) throw new InvalidOperationException("Render a valid sample first.");
        if (collection == null || collection.IsDisposed) collection = new AudioCollectionView([], "Synthesized " + InstrumentSynthCatalog.FamilyName(family));
        var created = audio.Clone();
        if (family == SynthFamily.DrumSet)
            created.Tag = (SynthDrum)drum.SelectedItem! switch
            {
                SynthDrum.Kick or SynthDrum.BassDrum => DrumsetElement.Kick,
                SynthDrum.Snare => DrumsetElement.Snare,
                SynthDrum.Clap => DrumsetElement.Clap,
                SynthDrum.Rimshot => DrumsetElement.Rim,
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
        string basis = audio.Name, name = basis; int index = 2;
        while (collection.AudioC.Audios.Any(a => a.Name == name)) name = basis + " " + index++;
        created.Rename(name); collection.AudioC.Audios.Add(created); collection.Show();
        status.Text = "Created: " + name;
    }
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
            CancelAutoHear(); autoHearTimer.Dispose(); rendering?.Cancel(); StopHear(); debounce.Dispose(); playbackTimer.Dispose(); tips.Dispose(); audio?.Dispose();
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
