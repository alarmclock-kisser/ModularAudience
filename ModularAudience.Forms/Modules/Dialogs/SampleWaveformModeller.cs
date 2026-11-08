using System.Text.Json;
using ModularAudience.Audio;
using ModularAudience.Audio.Processing;
using NAudio.Wave;

namespace ModularAudience.Forms.Modules.Dialogs;

/// <summary>One non-modal editing session bound to one collection sample.</summary>
public sealed class SampleWaveformModeller : Form
{
    private readonly AudioCollectionView collection;
    private readonly AudioObj target, original;
    private AudioObj? rendered;
    private SampleModellerSettings settings = new(), committed = new();
    private readonly Stack<SampleModellerSettings> undo = new(), redo = new();
    private readonly SampleModellerPresetStore presets = new();
    private readonly ComboBox preset = new() { Width = 190, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox presetName = new() { Width = 160, Text = "My sample preset" };
    private readonly ComboBox effectKind = new() { Width = 160, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ListBox chain = new() { Dock = DockStyle.Fill };
    private readonly PropertyGrid properties = new() { Dock = DockStyle.Fill, HelpVisible = true, ToolbarVisible = false };
    private readonly WavePlot plot = new() { Dock = DockStyle.Fill };
    private readonly WavePlot spectrum = new() { Dock = DockStyle.Bottom, Height = 135 };
    private double[] spectrumDb = [];
    private readonly HScrollBar scroll = new() { Dock = DockStyle.Bottom, Minimum = 0, Maximum = 100000, LargeChange = 1 };
    private readonly Label status = new() { Dock = DockStyle.Bottom, Height = 38, AutoEllipsis = true };
    private readonly NumericUpDown zoom = Number(1, 100000, 1, 1);
    private readonly NumericUpDown vertical = Number(0.1m, 32, 1, 0.1m);
    private readonly NumericUpDown selectionStart = Number(0, 99.999m, 0, 0.01m);
    private readonly NumericUpDown selectionEnd = Number(0.001m, 100, 100, 0.01m);
    private readonly NumericUpDown drawChannel = Number(1, 32, 1, 1);
    private readonly ComboBox gesture = new() { Width = 155, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox loop = new() { Text = "Loop", AutoSize = true };
    private readonly CheckBox bypass = new() { Text = "Hear original (A/B)", AutoSize = true };
    private readonly Button play = new() { Text = "Prehear", AutoSize = true };
    private readonly System.Windows.Forms.Timer debounce = new() { Interval = 180 };
    private readonly System.Windows.Forms.Timer meter = new() { Interval = 40 };
    private CancellationTokenSource? renderCts;
    private Task? renderTask;
    private int revision, readyRevision = -1;
    private bool saving, disposed;
    private SampleEffect? stroke;
    private double strokeLastPosition, strokeLastValue;
    private double selectAnchor;
    private WaveOut? output;
    private PreviewProvider? provider;
    private readonly Button save;

    public SampleWaveformModeller(AudioCollectionView collection, AudioObj sample)
    {
        this.collection = collection; target = sample; original = sample.Clone();
        original.Tag = sample.Tag; original.Key = sample.Key; original.SampleTag = sample.SampleTag;
        original.ScannedBpm = sample.ScannedBpm; original.ScannedKey = sample.ScannedKey;
        Text = $"Waveform Modeller — {sample.Name}"; Name = "SampleWaveformModeller";
        ClientSize = new Size(1250, 820); MinimumSize = new Size(900, 600); KeyPreview = true;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(8) };
        root.ColumnStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.AutoSize));
        root.RowStyles.Add(new(SizeType.Percent, 100)); root.RowStyles.Add(new(SizeType.Absolute, 38));
        var toolbar = Flow();
        toolbar.Controls.Add(play); play.Click += (_, _) => Safe(ToggleHear);
        toolbar.Controls.Add(loop); loop.CheckedChanged += (_, _) => { if (provider != null) provider.Loop = loop.Checked; };
        toolbar.Controls.Add(bypass); bypass.CheckedChanged += (_, _) => { UpdatePreview(); plot.Invalidate(); };
        save = Tool(toolbar, "Save", () => _ = SaveAsync((ModifierKeys & Keys.Control) != 0));
        Tool(toolbar, "Undo", () => History(false)); Tool(toolbar, "Redo", () => History(true));
        Tool(toolbar, "Reset chain", () => { settings.Effects.Clear(); Commit(); RefreshChain(); });
        Tool(toolbar, "Help", () => MessageBox.Show(this,
            "Add effects, then edit their properties. The chain runs top to bottom on the original sample.\n\n" +
            "Select: drag a time range. Add effects to selection applies the range to new effects.\nDraw waveform: zoom to individual samples; draw edits the selected channel.\nDraw envelope: draw a relative-volume curve for the selected channel (0 to 2).\nMouse wheel: zoom around pointer; scrollbar: pan.\n\n" +
            "Save: replace this sample and close. Ctrl+Save: add a named variation to the same collection and keep editing.\nCtrl+Z / Ctrl+Y: undo / redo. Space: prehear / stop.\n\n" +
            "Presets: enter a name and Save preset. Select a saved preset and Load. Edit the chain then Update preset, or Delete preset.\n" +
            "Gain above 0 dB can exceed full scale; the status shows clipping. Limiter or Normalize can control the final level.\n" +
            "Pitch/time use granular processing; delay/reverb tails remain within the sample duration.",
            "Sample Waveform Modeller", MessageBoxButtons.OK, MessageBoxIcon.Information));
        var presetbar = Flow();
        presetbar.Controls.Add(new Label { Text = "Preset", AutoSize = true, Margin = new Padding(3, 8, 3, 0) });
        presetbar.Controls.Add(preset); Tool(presetbar, "Load", LoadPreset);
        presetbar.Controls.Add(presetName); Tool(presetbar, "Save preset", SavePreset);
        Tool(presetbar, "Update preset", () => { if (preset.SelectedItem is string name) { settings.Name = name; presets.Save(name, settings); Commit(); status.Text = "Preset updated."; } });
        Tool(presetbar, "Delete preset", () => { if (preset.SelectedItem is string name) { presets.Delete(name); RefreshPresets(); } });
        var body = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, Size = new Size(1200, 600), SplitterDistance = 730 };
        var wavePanel = new Panel { Dock = DockStyle.Fill }; wavePanel.Controls.Add(plot); wavePanel.Controls.Add(scroll);
        wavePanel.Controls.Add(spectrum);
        spectrum.Paint += (_, e) =>
        {
            e.Graphics.Clear(Color.FromArgb(18, 20, 26));
            for (int i = 0; i < spectrumDb.Length; i++)
            {
                float x = i * spectrum.Width / (float)spectrumDb.Length;
                float height = (float)Math.Clamp((spectrumDb[i] + 90) / 90, 0, 1) * (spectrum.Height - 22);
                e.Graphics.FillRectangle(Brushes.CornflowerBlue, x, spectrum.Height - 22 - height, Math.Max(1, spectrum.Width / (float)spectrumDb.Length), height);
            }
            e.Graphics.DrawString("Spectrum 20 Hz → Nyquist (log). Click to add EQ: top +24 dB, middle 0 dB, bottom -24 dB.", Font, Brushes.Silver, 4, spectrum.Height - 20);
        };
        spectrum.MouseDown += (_, e) => Safe(() =>
        {
            if (e.Button != MouseButtons.Left) return;
            var eq = SampleEffect.Create(SampleEffectKind.PeakEq);
            eq.FrequencyHz = 20 * Math.Pow(Math.Max(1, original.SampleRate / 2d / 20), Math.Clamp(e.X / (double)Math.Max(1, spectrum.Width), 0, 1));
            eq.GainDb = Math.Clamp(24 - 48 * e.Y / (double)Math.Max(1, spectrum.Height - 22), -24, 24);
            settings.Effects.Add(eq); Commit(); RefreshChain(eq);
        });
        var waveTools = Flow(); waveTools.Dock = DockStyle.Top;
        Field(waveTools, "Zoom", zoom); Field(waveTools, "Y scale", vertical);
        Tool(waveTools, "Fit", () => { zoom.Value = 1; scroll.Value = 0; vertical.Value = 1; });
        var drawTools = Flow(); drawTools.Dock = DockStyle.Top;
        gesture.Items.AddRange(["Select range", "Draw waveform", "Draw envelope"]); gesture.SelectedIndex = 0;
        drawTools.Controls.Add(gesture); Field(drawTools, "Channel", drawChannel); drawChannel.Maximum = Math.Max(1, sample.Channels);
        Field(drawTools, "Start %", selectionStart); Field(drawTools, "End %", selectionEnd);
        Tool(drawTools, "All", () => { selectionStart.Value = 0; selectionEnd.Value = 100; });
        Tool(drawTools, "Use range", () =>
        {
            if (chain.SelectedItem is SampleEffect effect)
            { CheckRange(); effect.StartPercent = (double)selectionStart.Value; effect.EndPercent = (double)selectionEnd.Value; Commit(); properties.Refresh(); RefreshChain(effect); }
        });
        body.Panel1.Controls.Add(wavePanel); body.Panel1.Controls.Add(drawTools); body.Panel1.Controls.Add(waveTools);
        var effectsPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        effectsPanel.RowStyles.Add(new(SizeType.AutoSize)); effectsPanel.RowStyles.Add(new(SizeType.Absolute, 155));
        effectsPanel.RowStyles.Add(new(SizeType.AutoSize)); effectsPanel.RowStyles.Add(new(SizeType.Percent, 100));
        var addbar = Flow(); effectKind.Items.AddRange(Enum.GetValues<SampleEffectKind>().Cast<object>().ToArray()); effectKind.SelectedIndex = 0;
        addbar.Controls.Add(effectKind); Tool(addbar, "Add to selection", AddEffect);
        var chainbar = Flow(); Tool(chainbar, "Remove", RemoveEffect); Tool(chainbar, "↑", () => MoveEffect(-1)); Tool(chainbar, "↓", () => MoveEffect(1));
        Tool(chainbar, "On / Off", () => { if (chain.SelectedItem is SampleEffect e) { e.Enabled = !e.Enabled; Commit(); RefreshChain(e); properties.Refresh(); } });
        effectsPanel.Controls.Add(addbar, 0, 0); effectsPanel.Controls.Add(chain, 0, 1); effectsPanel.Controls.Add(chainbar, 0, 2); effectsPanel.Controls.Add(properties, 0, 3);
        body.Panel2.Controls.Add(effectsPanel);
        root.Controls.Add(toolbar, 0, 0); root.Controls.Add(presetbar, 0, 1); root.Controls.Add(body, 0, 2); root.Controls.Add(status, 0, 3); Controls.Add(root);
        chain.SelectedIndexChanged += (_, _) => properties.SelectedObject = chain.SelectedItem;
        properties.PropertyValueChanged += (_, _) => Safe(() =>
        {
            try { settings.Validate(); Commit(); RefreshChain(chain.SelectedItem); }
            catch { settings = committed.Clone(); RefreshChain(); throw; }
        });
        zoom.ValueChanged += (_, _) => plot.Invalidate(); vertical.ValueChanged += (_, _) => plot.Invalidate(); scroll.ValueChanged += (_, _) => plot.Invalidate();
        selectionStart.ValueChanged += (_, _) => { UpdatePreview(); plot.Invalidate(); };
        selectionEnd.ValueChanged += (_, _) => { UpdatePreview(); plot.Invalidate(); };
        plot.Paint += PaintWave; plot.MouseDown += BeginStroke; plot.MouseMove += ContinueStroke;
        plot.MouseUp += (_, _) => EndStroke(); plot.MouseCaptureChanged += (_, _) => { if (!plot.Capture) EndStroke(); };
        plot.MouseWheel += (_, e) =>
        {
            double anchor = PositionAt(e.X);
            zoom.Value = Math.Clamp(zoom.Value * (e.Delta > 0 ? 2 : 0.5m), zoom.Minimum, zoom.Maximum);
            double start = anchor - Math.Clamp(e.X / (double)Math.Max(1, plot.Width), 0, 1) / (double)zoom.Value;
            double max = 1 - 1 / (double)zoom.Value;
            scroll.Value = max > 0 ? (int)Math.Clamp(start / max * 100000, 0, 100000) : 0;
        };
        debounce.Tick += (_, _) => { debounce.Stop(); renderTask = RenderLatestAsync(); };
        meter.Tick += (_, _) => { if (output?.PlaybackState == PlaybackState.Playing && provider?.Finished == true) StopHear(); plot.Invalidate(); };
        Shown += (_, _) => { body.SplitterDistance = Math.Max(450, body.Width - 400); };
        FormClosing += (_, _) => { renderCts?.Cancel(); StopHear(); };
        Safe(RefreshPresets); committed = settings.Clone(); QueueRender();
    }

    private static NumericUpDown Number(decimal min, decimal max, decimal value, decimal step) => new()
    { Minimum = min, Maximum = max, Value = value, Increment = step, DecimalPlaces = step < 1 ? 3 : 0, Width = 77 };
    private static FlowLayoutPanel Flow() => new() { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
    private static void Field(Control parent, string label, Control control)
    { parent.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(4, 8, 2, 0) }); parent.Controls.Add(control); }
    private Button Tool(Control parent, string text, Action action)
    { var button = new Button { Text = text, AutoSize = true }; button.Click += (_, _) => Safe(action); parent.Controls.Add(button); return button; }
    private void Safe(Action action) { try { action(); } catch (Exception error) { status.Text = error.Message; } }
    private void CheckRange() { if (selectionStart.Value >= selectionEnd.Value) throw new ArgumentException("Selection start must be before its end."); }
    private void RefreshPresets() { preset.Items.Clear(); preset.Items.AddRange(presets.Names()); }
    private void SavePreset()
    { settings.Name = presetName.Text.Trim(); presets.Save(settings.Name, settings); Commit(); RefreshPresets(); preset.SelectedItem = settings.Name; status.Text = "Preset saved in AppData."; }
    private void LoadPreset()
    {
        if (preset.SelectedItem is not string name) return;
        settings = presets.Load(name); presetName.Text = name; Commit(); RefreshChain();
    }
    private void AddEffect()
    {
        CheckRange(); var effect = SampleEffect.Create((SampleEffectKind)effectKind.SelectedItem!);
        effect.StartPercent = (double)selectionStart.Value; effect.EndPercent = (double)selectionEnd.Value;
        settings.Effects.Add(effect); Commit(); RefreshChain(effect);
    }
    private void RemoveEffect() { if (chain.SelectedItem is SampleEffect e) { settings.Effects.Remove(e); Commit(); RefreshChain(); } }
    private void MoveEffect(int offset)
    {
        int index = chain.SelectedIndex, next = index + offset;
        if (index < 0 || next < 0 || next >= settings.Effects.Count) return;
        var effect = settings.Effects[index]; settings.Effects.RemoveAt(index); settings.Effects.Insert(next, effect); Commit(); RefreshChain(effect);
    }
    private void RefreshChain(object? selected = null)
    {
        int index = chain.SelectedIndex; chain.BeginUpdate(); chain.Items.Clear(); chain.Items.AddRange(settings.Effects.Cast<object>().ToArray());
        if (selected != null && chain.Items.Contains(selected)) chain.SelectedItem = selected;
        else if (chain.Items.Count > 0) chain.SelectedIndex = Math.Clamp(index, 0, chain.Items.Count - 1);
        else properties.SelectedObject = null;
        chain.EndUpdate();
    }
    private void Commit()
    {
        settings.Validate();
        if (JsonSerializer.Serialize(settings) == JsonSerializer.Serialize(committed)) return;
        undo.Push(committed.Clone()); redo.Clear(); committed = settings.Clone(); QueueRender();
    }
    private void History(bool forward)
    {
        EndStroke(); var from = forward ? redo : undo; var to = forward ? undo : redo;
        if (from.Count == 0) return;
        to.Push(committed.Clone()); settings = from.Pop(); committed = settings.Clone(); presetName.Text = settings.Name;
        RefreshChain(); QueueRender();
    }
    private void QueueRender()
    {
        if (disposed) return;
        revision++; renderCts?.Cancel(); debounce.Stop(); debounce.Start(); status.Text = "Updating waveform…";
    }
    private async Task RenderLatestAsync()
    {
        int generation = revision; var snapshot = settings.Clone();
        using var source = original.Clone();
        source.Tag = original.Tag; source.Key = original.Key; source.SampleTag = original.SampleTag;
        source.ScannedBpm = original.ScannedBpm; source.ScannedKey = original.ScannedKey;
        var cts = new CancellationTokenSource(); renderCts = cts;
        try
        {
            var result = await Task.Run(() => SampleModellerDsp.Render(source, snapshot, cts.Token), cts.Token);
            if (disposed || cts.IsCancellationRequested || generation != revision) { result.Dispose(); return; }
            var previous = rendered; rendered = result; readyRevision = generation; previous?.Dispose();
            spectrumDb = BuildSpectrum(result); spectrum.Invalidate();
            UpdatePreview(); plot.Invalidate();
            float peak = result.Data.Max(v => Math.Abs(v));
            status.Text = $"{result.Duration.TotalSeconds:0.000} s · {result.SampleRate} Hz · {result.Channels} ch · Peak {20 * Math.Log10(Math.Max(1e-12, peak)):0.0} dBFS{(peak > 1 ? " — above full scale" : "")} · {settings.Effects.Count} effects. Save replaces; Ctrl+Save creates a variation.";
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!disposed && generation == revision) status.Text = error.Message; }
        finally { if (ReferenceEquals(renderCts, cts)) renderCts = null; cts.Dispose(); }
    }
    private async Task SaveAsync(bool copy)
    {
        if (saving) return; saving = true; save.Enabled = false;
        try
        {
            EndStroke(); debounce.Stop();
            if (readyRevision != revision) { renderCts?.Cancel(); renderTask = RenderLatestAsync(); await renderTask; }
            if (disposed) return;
            if (readyRevision != revision || rendered == null) throw new InvalidOperationException("Resolve the effect error before saving.");
            CommitToCollection(copy);
            if (!copy) Close(); else status.Text = "New modelled variation added to the same collection.";
        }
        catch (Exception error) { if (!disposed) status.Text = error.Message; }
        finally { saving = false; if (!disposed) save.Enabled = true; }
    }
    private void CommitToCollection(bool copy)
    {
        if (collection.IsDisposed || !collection.AudioC.Audios.Contains(target)) throw new InvalidOperationException("The original sample is no longer in its collection.");
        if (rendered == null) throw new InvalidOperationException("No rendered audio is ready.");
        if (copy)
        {
            var variant = rendered.Clone(); variant.Tag = target.Tag; variant.Key = target.Key; variant.SampleTag = target.SampleTag;
            string basis = target.OriginalName + " — Modelled", name = basis; int count = 2;
            while (collection.AudioC.Audios.Any(a => a.Name == name)) name = basis + " " + count++;
            variant.FilePath = string.Empty; variant.Rename(name); collection.AudioC.Audios.Add(variant);
        }
        else
        {
            string name = target.OriginalName;
            target.ReplaceWith(rendered); target.Rename(name); collection.AudioC.Audios.ResetBindings();
        }
        WindowMain.Instance?.UpdateTrackDependentUI();
    }
    private AudioObj DisplayAudio => bypass.Checked ? original : rendered ?? original;
    private static double[] BuildSpectrum(AudioObj audio)
    {
        const int size = 2048;
        var fft = new System.Numerics.Complex[size]; int frames = audio.Data.Length / audio.Channels;
        int offset = Math.Max(0, (frames - size) / 2);
        for (int i = 0; i < Math.Min(size, frames); i++)
        {
            double value = 0; for (int c = 0; c < audio.Channels; c++) value += audio.Data[(offset + i) * audio.Channels + c] / (double)audio.Channels;
            fft[i] = value * (0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (size - 1)));
        }
        for (int i = 1, j = 0; i < size; i++)
        {
            int bit = size >> 1; for (; (j & bit) != 0; bit >>= 1) j ^= bit; j ^= bit;
            if (i < j) (fft[i], fft[j]) = (fft[j], fft[i]);
        }
        for (int length = 2; length <= size; length <<= 1)
        {
            var root = System.Numerics.Complex.FromPolarCoordinates(1, -2 * Math.PI / length);
            for (int start = 0; start < size; start += length)
            {
                var weight = System.Numerics.Complex.One;
                for (int j = 0; j < length / 2; j++)
                { var a = fft[start + j]; var b = fft[start + j + length / 2] * weight; fft[start + j] = a + b; fft[start + j + length / 2] = a - b; weight *= root; }
            }
        }
        return Enumerable.Range(0, 128).Select(i =>
        {
            double hz = 20 * Math.Pow(Math.Max(1, audio.SampleRate / 40d), i / 127d);
            int bin = Math.Clamp((int)(hz * size / audio.SampleRate), 1, size / 2);
            return 20 * Math.Log10(Math.Max(1e-12, fft[bin].Magnitude * 4 / size));
        }).ToArray();
    }
    private void ToggleHear()
    {
        if (output != null) { StopHear(); return; }
        provider = new PreviewProvider(original.SampleRate, original.Channels) { Loop = loop.Checked };
        UpdatePreview(); output = new WaveOut { BufferMilliseconds = 80 }; output.Init(provider); output.Play(); meter.Start(); play.Text = "Stop";
    }
    private void UpdatePreview()
    {
        var audio = DisplayAudio;
        int frames = audio.Data.Length / audio.Channels;
        int first = Math.Clamp((int)(frames * (double)selectionStart.Value / 100), 0, Math.Max(0, frames - 1));
        int last = Math.Clamp((int)(frames * (double)selectionEnd.Value / 100), first + 1, frames);
        provider?.Update(audio.Data, first, last);
    }
    private void StopHear() { meter.Stop(); output?.Stop(); output?.Dispose(); output = null; provider = null; play.Text = "Prehear"; }
    private double ViewStart => scroll.Value / 100000d * (1 - 1 / (double)zoom.Value);
    private double PositionAt(int x) => Math.Clamp(ViewStart + x / (double)Math.Max(1, plot.Width) / (double)zoom.Value, 0, 1);
    private double DrawValue(int y)
    {
        if (gesture.SelectedIndex == 2) return Math.Clamp(2 * (1 - y / (double)Math.Max(1, plot.Height)), 0, 2);
        double channelHeight = plot.Height / (double)Math.Max(1, original.Channels);
        double localY = y - ((int)drawChannel.Value - 1) * channelHeight;
        return Math.Clamp((1 - 2 * localY / Math.Max(1, channelHeight)) / (double)vertical.Value, -4, 4);
    }
    private void BeginStroke(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || saving) return;
        plot.Focus(); selectAnchor = PositionAt(e.X);
        if (gesture.SelectedIndex == 0) { plot.Capture = true; return; }
        stroke = SampleEffect.Create(gesture.SelectedIndex == 1 ? SampleEffectKind.DrawWaveform : SampleEffectKind.VolumeEnvelope);
        stroke.Channel = (int)drawChannel.Value; stroke.StartPercent = 0; stroke.EndPercent = 100;
        strokeLastPosition = selectAnchor; strokeLastValue = DrawValue(e.Y);
        stroke.Points.Add(new(strokeLastPosition, strokeLastValue)); settings.Effects.Add(stroke); plot.Capture = true;
    }
    private void ContinueStroke(object? sender, MouseEventArgs e)
    {
        if (!plot.Capture || (e.Button & MouseButtons.Left) == 0) return;
        double position = PositionAt(e.X);
        if (stroke == null)
        {
            selectionStart.Value = Math.Clamp((decimal)(Math.Min(selectAnchor, position) * 100), selectionStart.Minimum, selectionStart.Maximum);
            selectionEnd.Value = Math.Clamp((decimal)(Math.Max(selectAnchor, position) * 100), selectionEnd.Minimum, selectionEnd.Maximum); return;
        }
        double value = DrawValue(e.Y), left = Math.Min(strokeLastPosition, position), right = Math.Max(strokeLastPosition, position);
        stroke.Points.RemoveAll(p => p.Position >= left && p.Position <= right);
        stroke.Points.Add(new(strokeLastPosition, strokeLastValue)); stroke.Points.Add(new(position, value));
        strokeLastPosition = position; strokeLastValue = value;
        stroke.StartPercent = Math.Max(0, stroke.Points.Min(p => p.Position) * 100);
        stroke.EndPercent = Math.Min(100, Math.Max(stroke.StartPercent + 0.00001, stroke.Points.Max(p => p.Position) * 100));
        plot.Invalidate();
    }
    private void EndStroke()
    {
        if (stroke == null) { if (plot.Capture) plot.Capture = false; return; }
        var drawing = stroke; stroke = null; plot.Capture = false;
        double start = drawing.Points.Min(p => p.Position), end = drawing.Points.Max(p => p.Position);
        if (end <= start) end = Math.Min(1, start + 1d / Math.Max(1, original.Data.Length / original.Channels));
        if (end <= start) { settings.Effects.Remove(drawing); return; }
        drawing.StartPercent = start * 100; drawing.EndPercent = end * 100;
        drawing.Points = drawing.Points.OrderBy(p => p.Position).Select(p => new SampleDrawPoint((p.Position - start) / (end - start), p.Value)).ToList();
        Safe(() => { Commit(); RefreshChain(drawing); });
    }
    private void PaintWave(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(Color.FromArgb(22, 24, 30)); var audio = DisplayAudio;
        int frames = audio.Data.Length / audio.Channels; if (frames == 0) return;
        double start = ViewStart, width = 1 / (double)zoom.Value;
        using var gridPen = new Pen(Color.FromArgb(55, 60, 70)); using var wavePen = new Pen(Color.Turquoise);
        int channels = audio.Channels;
        for (int c = 0; c < channels; c++)
        {
            float middle = (c + 0.5f) * plot.Height / channels, amplitude = plot.Height / (2f * channels) * (float)vertical.Value;
            g.DrawLine(gridPen, 0, middle, plot.Width, middle);
            PointF? previous = null;
            for (int x = 0; x < plot.Width; x++)
            {
                int first = Math.Clamp((int)((start + x / (double)Math.Max(1, plot.Width) * width) * frames), 0, frames - 1);
                int last = Math.Clamp((int)((start + (x + 1d) / Math.Max(1, plot.Width) * width) * frames), first + 1, frames);
                float min = 0, max = 0;
                int stride = Math.Max(1, (last - first) / 160);
                for (int i = first; i < last; i += stride) { float v = audio.Data[i * channels + c]; min = Math.Min(min, v); max = Math.Max(max, v); }
                if (last - first <= 2)
                {
                    var point = new PointF(x, middle - audio.Data[first * channels + c] * amplitude);
                    if (previous is { } p) g.DrawLine(wavePen, p, point); previous = point;
                }
                else g.DrawLine(wavePen, x, middle - max * amplitude, x, middle - min * amplitude);
            }
            g.DrawString($"Ch {c + 1}", Font, Brushes.Silver, 4, c * plot.Height / channels + 4);
        }
        using var selectionBrush = new SolidBrush(Color.FromArgb(40, Color.CornflowerBlue));
        float selectionX = (float)(((double)selectionStart.Value / 100 - start) / width * plot.Width);
        float selectionRight = (float)(((double)selectionEnd.Value / 100 - start) / width * plot.Width);
        g.FillRectangle(selectionBrush, selectionX, 0, Math.Max(0, selectionRight - selectionX), plot.Height);
        if (stroke != null && stroke.Points.Count > 1)
        {
            var points = stroke.Points.OrderBy(p => p.Position).Select(p => new PointF((float)((p.Position - start) / width * plot.Width),
                gesture.SelectedIndex == 2 ? (float)((1 - p.Value / 2) * plot.Height)
                    : (float)(((int)drawChannel.Value - 1 + (1 - p.Value * (double)vertical.Value) / 2) * plot.Height / channels))).ToArray();
            g.DrawLines(Pens.Orange, points);
        }
        if (provider != null)
        { float x = (float)((provider.Position / (double)frames - start) / width * plot.Width); g.DrawLine(Pens.OrangeRed, x, 0, x, plot.Height); }
        g.DrawString($"{start * audio.Duration.TotalSeconds:0.0000} s — {(start + width) * audio.Duration.TotalSeconds:0.0000} s", Font, Brushes.White, 4, plot.Height - 20);
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Z)) { History(false); return true; }
        if (keyData == (Keys.Control | Keys.Y)) { History(true); return true; }
        if (keyData == (Keys.Control | Keys.S)) { _ = SaveAsync(true); return true; }
        if (keyData == Keys.Space && !properties.ContainsFocus && !presetName.ContainsFocus) { Safe(ToggleHear); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            disposed = true; renderCts?.Cancel(); StopHear(); debounce.Dispose(); meter.Dispose(); rendered?.Dispose();
            // Each render owns its PCM snapshot, so the session source can be released immediately.
            original.Dispose();
        }
        base.Dispose(disposing);
    }
    private sealed class WavePlot : Control
    { public WavePlot() { DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw | ControlStyles.Selectable, true); TabStop = true; } }
    private sealed class PreviewProvider : ISampleProvider
    {
        private readonly object gate = new(); private float[] data = []; private int first, last, position;
        private float gain;
        public bool Loop { get; set; }
        public bool Finished { get; private set; }
        public int Position { get { lock (gate) return position; } }
        public WaveFormat WaveFormat { get; }
        public PreviewProvider(int rate, int channels) => WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(rate, channels);
        public void Update(float[] value, int start, int end)
        {
            lock (gate) { data = value; first = start; last = end; if (position < first || position >= last) position = first; Finished = false; gain = 0; }
        }
        public int Read(float[] buffer, int offset, int count)
            => Read(buffer.AsSpan(offset, count));
        public int Read(Span<float> buffer)
        {
            lock (gate)
            {
                int channels = WaveFormat.Channels;
                for (int i = 0; i < buffer.Length; i += channels)
                {
                    if (position >= last) { if (Loop) { position = first; gain = 0; } else Finished = true; }
                    gain = Math.Min(1, gain + 1f / Math.Max(1, WaveFormat.SampleRate / 200));
                    float fade = Math.Min(gain, (last - position) / (float)Math.Max(1, WaveFormat.SampleRate / 200));
                    for (int c = 0; c < channels && i + c < buffer.Length; c++)
                        buffer[i + c] = !Finished && position * channels + c < data.Length
                            ? Math.Clamp(data[position * channels + c] * fade, -1, 1) : 0;
                    if (!Finished) position++;
                }
                return buffer.Length;
            }
        }
    }
}
