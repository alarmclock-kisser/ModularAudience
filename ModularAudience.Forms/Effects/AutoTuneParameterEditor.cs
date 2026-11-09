using ModularAudience.Audio.Effects.AutoTune;

namespace ModularAudience.Forms.Effects;

internal sealed class AutoTuneParameterEditor : UserControl
{
    private sealed record NumberField(NumericUpDown Control, Func<AutoTuneSettings, decimal> Read);
    private readonly Dictionary<string, NumberField> numbers = [];
    private readonly ComboBox scale = Combo(), rootNote = Combo(), analysisChannel = Combo(), fftSize = Combo(), overlap = Combo();
    private readonly DomainUpDown noteLength = new() { ReadOnly = true, Width = 125, Wrap = false };
    private readonly CheckBox[] notes = new CheckBox[12];
    private readonly CheckBox protectPeak = new() { Text = "Peak protection (attenuate only)", AutoSize = true };
    private readonly GroupBox customNotes = new() { Text = "Allowed notes (Custom scale only; absolute C–B pitch classes)", Height = 88 };
    private readonly ToolTip tips = new() { IsParameterHelp = true };
    private bool syncing;

    internal AutoTuneParameterEditor()
    {
        var tabs = new TabControl { Dock = DockStyle.Fill };
        FlowLayoutPanel tuning = Page(tabs, "Tuning / notes"), detection = Page(tabs, "Detection / quality"), output = Page(tabs, "Mix / output");
        this.Controls.Add(tabs);
        this.scale.Items.AddRange(Enum.GetValues<AutoTuneScale>().Cast<object>().ToArray());
        this.rootNote.Items.AddRange(["C", "C# / Db", "D", "D# / Eb", "E", "F", "F# / Gb", "G", "G# / Ab", "A", "A# / Bb", "B"]);
        this.noteLength.Items.AddRange(AutoTuneSettings.NoteLengths.Cast<object>().ToArray());
        Field(tuning, "Scale", this.scale); Field(tuning, "Root key", this.rootNote); Field(tuning, "Minimum key duration", this.noteLength);
        this.tips.SetToolTip(this.noteLength, "1 means a whole note (4 beats) at Tempo BPM. Shorter detected notes are left dry; 1/4 is a quarter note.");
        var notePanel = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
        string[] labels = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
        for (int i = 0; i < this.notes.Length; i++)
        {
            this.notes[i] = new CheckBox { Text = labels[i], AutoSize = true, Margin = new Padding(6) };
            this.notes[i].CheckedChanged += this.ChangedControl;
            notePanel.Controls.Add(this.notes[i]);
        }
        this.customNotes.Controls.Add(notePanel);
        tuning.Controls.Add(this.customNotes);
        tuning.SizeChanged += (_, _) => this.customNotes.Width = Math.Max(250, tuning.ClientSize.Width - 30);

        Number(tuning, nameof(AutoTuneSettings.IntensityPercent), "Intensity (%)", 0, 100, 1, s => (decimal)s.IntensityPercent, "0 is bypass; 100 applies the full measured correction.");
        Number(tuning, nameof(AutoTuneSettings.TempoBpm), "Tempo (BPM)", 20, 400, 0.1m, s => (decimal)s.TempoBpm, "Tempo used to convert minimum musical note length to time.");
        Number(tuning, nameof(AutoTuneSettings.MaximumCorrectionSemitones), "Max keys adjustment (semitones)", 0, 24, 0.1m, s => (decimal)s.MaximumCorrectionSemitones, "Notes needing a larger correction are left dry, not force-shifted. 0 bypasses pitch correction.");
        Number(tuning, nameof(AutoTuneSettings.RetuneMilliseconds), "Retune time (ms)", 0, 2000, 1, s => (decimal)s.RetuneMilliseconds, "0 = hard/robotic correction; longer values approach the target more gently.");
        Number(tuning, nameof(AutoTuneSettings.ReleaseMilliseconds), "Correction release (ms)", 0, 2000, 1, s => (decimal)s.ReleaseMilliseconds, "Time to return the shift to neutral after voiced material.");
        Number(tuning, nameof(AutoTuneSettings.PitchSmoothingMilliseconds), "Note tracking smoothing (ms)", 0, 500, 1, s => (decimal)s.PitchSmoothingMilliseconds, "Smooth the pitch used to select a target note; retune time separately smooths the correction.");
        Number(tuning, nameof(AutoTuneSettings.HysteresisCents), "Note-switch hysteresis (cents)", 0, 100, 1, s => (decimal)s.HysteresisCents, "Extra preference for the current note to avoid target chatter near boundaries.");
        Number(tuning, nameof(AutoTuneSettings.DeadZoneCents), "Leave in-tune notes (cents)", 0, 50, 0.5m, s => (decimal)s.DeadZoneCents, "Do not correct deviations inside this tolerance.");
        Number(tuning, nameof(AutoTuneSettings.VibratoPreservationPercent), "Preserve vibrato (%)", 0, 100, 1, s => (decimal)s.VibratoPreservationPercent, "Retain fast pitch variation relative to the vibrato averaging window.");
        Number(tuning, nameof(AutoTuneSettings.VibratoWindowMilliseconds), "Vibrato averaging window (ms)", 20, 1000, 1, s => (decimal)s.VibratoWindowMilliseconds, "Longer windows separate slow note center from faster vibrato more strongly.");
        Number(tuning, nameof(AutoTuneSettings.ReferenceA4Hz), "Concert A4 reference (Hz)", 400, 480, 0.1m, s => (decimal)s.ReferenceA4Hz, "Equal-tempered tuning reference; normally 440 Hz.");
        Number(tuning, nameof(AutoTuneSettings.FineTuneCents), "Target tuning offset (cents)", -100, 100, 0.5m, s => (decimal)s.FineTuneCents, "Fine offset for every allowed target note, relative to the A4 reference.");

        this.analysisChannel.Items.AddRange(Enum.GetValues<AutoTuneAnalysisChannel>().Cast<object>().ToArray());
        this.fftSize.Items.AddRange(AutoTuneSettings.FftSizes.Cast<object>().ToArray());
        this.overlap.Items.AddRange(AutoTuneSettings.Overlaps.Cast<object>().ToArray());
        Field(detection, "Linked pitch-analysis channel", this.analysisChannel);
        this.tips.SetToolTip(this.analysisChannel, "Strongest avoids cancellation in antiphase stereo. All output channels always share one pitch-correction curve; this is not polyphonic separation.");
        Field(detection, "Synthesis FFT size", this.fftSize); Field(detection, "Synthesis overlap", this.overlap);
        Number(detection, nameof(AutoTuneSettings.MinimumFrequencyHz), "Lowest detected pitch (Hz)", 20, 2000, 1, s => (decimal)s.MinimumFrequencyHz, "Lower values need longer detection windows and more memory.");
        Number(detection, nameof(AutoTuneSettings.MaximumFrequencyHz), "Highest detected pitch (Hz)", 21, 5000, 1, s => (decimal)s.MaximumFrequencyHz, "Must be above the minimum and below half the source sample rate.");
        Number(detection, nameof(AutoTuneSettings.MinimumConfidence), "Minimum voiced confidence", 0, 1, 0.01m, s => (decimal)s.MinimumConfidence, "pYIN voiced probability gate. Higher values bypass more uncertain material.");
        Number(detection, nameof(AutoTuneSettings.SilenceThresholdDb), "Silence / noise gate (dBFS)", -100, 0, 1, s => (decimal)s.SilenceThresholdDb, "Material below this RMS level is treated as unvoiced.");
        Number(detection, nameof(AutoTuneSettings.AnalysisHopMilliseconds), "Pitch-analysis step (ms)", 5, 50, 1, s => (decimal)s.AnalysisHopMilliseconds, "Smaller steps track notes faster and use more CPU/memory.");

        Number(output, nameof(AutoTuneSettings.WetPercent), "Wet / dry (%)", 0, 100, 1, s => (decimal)s.WetPercent, "0 = original; 100 = corrected voiced material.");
        Number(output, nameof(AutoTuneSettings.OutputGainDb), "Output gain (dB)", -24, 12, 0.1m, s => (decimal)s.OutputGainDb, "Output gain for the processed region, including its dry/wet mix.");
        Number(output, nameof(AutoTuneSettings.VoicingFadeMilliseconds), "Voiced / unvoiced crossfade (ms)", 0, 200, 1, s => (decimal)s.VoicingFadeMilliseconds, "Blend consonants and uncertain frames back to dry audio smoothly.");
        Number(output, nameof(AutoTuneSettings.BoundaryFadeMilliseconds), "Selection-edge blend (ms)", 0, 100, 1, s => (decimal)s.BoundaryFadeMilliseconds, "Blend from original to processed samples at both region edges; nothing outside the region changes.");
        Number(output, nameof(AutoTuneSettings.FormantPreservationPercent), "Formant preservation (%)", 0, 100, 1, s => (decimal)s.FormantPreservationPercent, "Approximate spectral-envelope compensation, not voice separation. 0 disables it.");
        Number(output, nameof(AutoTuneSettings.FormantSmoothingHz), "Formant envelope bandwidth (Hz)", 50, 2500, 10, s => (decimal)s.FormantSmoothingHz, "Frequency smoothing used by the optional spectral-envelope compensation.");
        output.Controls.Add(this.protectPeak);
        this.protectPeak.CheckedChanged += this.ChangedControl;
        foreach (ComboBox combo in new[] { this.scale, this.rootNote, this.analysisChannel, this.fftSize, this.overlap }) combo.SelectedIndexChanged += this.ChangedControl;
        this.noteLength.SelectedItemChanged += this.ChangedControl;
        this.ApplySettings(new AutoTuneSettings());
    }

    internal event EventHandler? SettingsChanged;

    internal AutoTuneSettings CaptureSettings()
    {
        var settings = new AutoTuneSettings
        {
            Scale = (AutoTuneScale)this.scale.SelectedItem!, RootNote = this.rootNote.SelectedIndex,
            CustomNotesMask = Enumerable.Range(0, 12).Where(i => this.notes[i].Checked).Aggregate(0, (mask, i) => mask | 1 << i),
            MinimumNoteLength = this.noteLength.SelectedItem?.ToString() ?? "1/64",
            AnalysisChannel = (AutoTuneAnalysisChannel)this.analysisChannel.SelectedItem!,
            FftSize = (int)this.fftSize.SelectedItem!, Overlap = (int)this.overlap.SelectedItem!, ProtectPeak = this.protectPeak.Checked,
            IntensityPercent = N(nameof(AutoTuneSettings.IntensityPercent)), TempoBpm = N(nameof(AutoTuneSettings.TempoBpm)),
            MaximumCorrectionSemitones = N(nameof(AutoTuneSettings.MaximumCorrectionSemitones)), RetuneMilliseconds = N(nameof(AutoTuneSettings.RetuneMilliseconds)),
            ReleaseMilliseconds = N(nameof(AutoTuneSettings.ReleaseMilliseconds)), PitchSmoothingMilliseconds = N(nameof(AutoTuneSettings.PitchSmoothingMilliseconds)),
            HysteresisCents = N(nameof(AutoTuneSettings.HysteresisCents)), DeadZoneCents = N(nameof(AutoTuneSettings.DeadZoneCents)),
            VibratoPreservationPercent = N(nameof(AutoTuneSettings.VibratoPreservationPercent)), VibratoWindowMilliseconds = N(nameof(AutoTuneSettings.VibratoWindowMilliseconds)),
            ReferenceA4Hz = N(nameof(AutoTuneSettings.ReferenceA4Hz)), FineTuneCents = N(nameof(AutoTuneSettings.FineTuneCents)),
            MinimumFrequencyHz = N(nameof(AutoTuneSettings.MinimumFrequencyHz)), MaximumFrequencyHz = N(nameof(AutoTuneSettings.MaximumFrequencyHz)),
            MinimumConfidence = N(nameof(AutoTuneSettings.MinimumConfidence)), SilenceThresholdDb = N(nameof(AutoTuneSettings.SilenceThresholdDb)),
            AnalysisHopMilliseconds = N(nameof(AutoTuneSettings.AnalysisHopMilliseconds)),
            WetPercent = N(nameof(AutoTuneSettings.WetPercent)), OutputGainDb = N(nameof(AutoTuneSettings.OutputGainDb)),
            VoicingFadeMilliseconds = N(nameof(AutoTuneSettings.VoicingFadeMilliseconds)), BoundaryFadeMilliseconds = N(nameof(AutoTuneSettings.BoundaryFadeMilliseconds)),
            FormantPreservationPercent = N(nameof(AutoTuneSettings.FormantPreservationPercent)), FormantSmoothingHz = N(nameof(AutoTuneSettings.FormantSmoothingHz))
        };
        settings.Validate();
        return settings;
    }

    internal void ApplySettings(AutoTuneSettings settings)
    {
        settings.Validate();
        this.syncing = true;
        try
        {
            this.scale.SelectedItem = settings.Scale; this.rootNote.SelectedIndex = settings.RootNote;
            this.analysisChannel.SelectedItem = settings.AnalysisChannel; this.fftSize.SelectedItem = settings.FftSize; this.overlap.SelectedItem = settings.Overlap;
            this.noteLength.SelectedItem = settings.MinimumNoteLength; this.protectPeak.Checked = settings.ProtectPeak;
            for (int i = 0; i < 12; i++) this.notes[i].Checked = (settings.CustomNotesMask & (1 << i)) != 0;
            foreach (NumberField field in this.numbers.Values) field.Control.Value = field.Read(settings);
            this.customNotes.Enabled = settings.Scale == AutoTuneScale.Custom;
        }
        finally { this.syncing = false; }
        this.SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private double N(string name) => (double)this.numbers[name].Control.Value;

    private void ChangedControl(object? sender, EventArgs e)
    {
        if (this.syncing) return;
        this.customNotes.Enabled = this.scale.SelectedItem is AutoTuneScale.Custom;
        this.SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Number(FlowLayoutPanel panel, string key, string label, decimal min, decimal max, decimal step,
        Func<AutoTuneSettings, decimal> read, string tip)
    {
        var control = new NumericUpDown { Name = key, Minimum = min, Maximum = max, Increment = step, DecimalPlaces = step < 1 ? 2 : 0, Width = 150 };
        this.numbers.Add(key, new(control, read));
        Field(panel, label, control);
        this.tips.SetToolTip(control, tip);
        control.ValueChanged += this.ChangedControl;
    }

    private static void Field(FlowLayoutPanel panel, string label, Control control)
    {
        var field = new FlowLayoutPanel { Width = 250, Height = 64, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(6) };
        field.Controls.Add(new Label { Text = label, Width = 244, Height = 30 });
        field.Controls.Add(control); panel.Controls.Add(field);
    }

    private static FlowLayoutPanel Page(TabControl tabs, string name)
    {
        var page = new TabPage(name);
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true, Padding = new Padding(6) };
        page.Controls.Add(panel); tabs.TabPages.Add(page); return panel;
    }

    private static ComboBox Combo() => new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };

    protected override void Dispose(bool disposing)
    {
        if (disposing) this.tips.Dispose();
        base.Dispose(disposing);
    }
}
