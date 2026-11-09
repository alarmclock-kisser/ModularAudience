using ModularAudience.Audio;
using ModularAudience.Audio.Synthesis;
using NAudio.Wave;

namespace ModularAudience.Forms.Modules.Dialogs;

public sealed class WaveformModeller : Form
{
    private WaveformSettings settings = new();
    private readonly TextBox equation = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, Height = 85 };
    private readonly Plot plot = new() { Dock = DockStyle.Fill };
    private readonly ToolTip parameterHelp = new() { IsParameterHelp = true };
    private readonly ToolTip interactionHints = new();
    private readonly Label status = new() { Dock = DockStyle.Bottom, Height = 35, AutoEllipsis = true };
    private readonly TextBox name = new() { Width = 180, Text = "Modelled waveform" };
    private readonly NumericUpDown frequency = Number(0.1m, 20000, 220, 0.1m);
    private readonly NumericUpDown gain = Number(0, 100, 30, 1);
    private readonly NumericUpDown duration = Number(0.01m, 3600, 5, 0.1m);
    private readonly NumericUpDown phase = Number(0, 1, 0, 0.01m);
    private readonly NumericUpDown harmonics = Number(1, 128, 32, 1);
    private readonly NumericUpDown attack = Number(0, 10000, 5, 1);
    private readonly NumericUpDown release = Number(0, 10000, 20, 1);
    private readonly ComboBox rate = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 85 };
    private readonly NumericUpDown channels = Number(1, 8, 2, 1);
    private readonly CheckBox normalize = new() { Text = "Normalize cycle", Checked = true, AutoSize = true };
    private readonly CheckBox dc = new() { Text = "Remove DC", Checked = true, AutoSize = true };
    private readonly Button hear = new() { Text = "Prehear", AutoSize = true };
    private readonly System.Windows.Forms.Timer debounce = new() { Interval = 120 };
    private readonly Stack<WaveformSettings> undo = new(), redo = new();
    private WaveformSettings committed = new();
    private double[] cycle = new double[WaveformModel.TableSize];
    private double[]? drawn;
    private bool editing, drawing, rendering;
    private Point lastDraw;
    private WaveformLiveProvider? live;
    private WaveOut? output;
    private CancellationTokenSource? renderCancellation;
    private AudioCollectionView? collection;
    private bool closeAfterRender;
    private bool disposedResources;

    public WaveformModeller()
    {
        Text = "Waveform Modeller"; Name = "WaveformModeller";
        ClientSize = new Size(1000, 700); MinimumSize = new Size(800, 600); StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), ColumnCount = 1, RowCount = 6 };
        root.ColumnStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.Absolute, 90)); root.RowStyles.Add(new(SizeType.AutoSize));
        root.RowStyles.Add(new(SizeType.Percent, 100)); root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.Absolute, 35));
        var toolbar = Flow();
        toolbar.Controls.Add(hear); hear.Click += (_, _) => ToggleHear();
        Button Tool(Control parent, string text, Action action)
        { var b = new Button { Text = text, AutoSize = true }; b.Click += (_, _) => Safe(action); parent.Controls.Add(b); return b; }
        Tool(toolbar, "Render → AudioObj", async () => await RenderAsync());
        Tool(toolbar, "Cancel render", () => renderCancellation?.Cancel());
        Tool(toolbar, "Save waveform…", Save); Tool(toolbar, "Load waveform…", LoadPreset);
        Tool(toolbar, "Undo", () => History(false)); Tool(toolbar, "Redo", () => History(true));
        Tool(toolbar, "Equation help", () => MessageBox.Show(this,
            "One cycle: x (or phase) = 0…1. Frequency controls repetition.\n\n" +
            "Examples:\nsin(tau*x)\n0.7sin(tau*x)+0.2sin(3*tau*x)\ntanh(3*sin(tau*x))\n\n" +
            "Operators: + - * / % ^ and parentheses. Implicit multiplication: 2pi, 2(x+1).\n" +
            "Constants: pi, tau, e; Unicode π τ × ÷ − supported.\n" +
            "Functions: sin cos tan tanh asin acos atan abs sqrt exp log ln log10 floor ceil round sign min max pow clamp.\n" +
            "Use decimal dots and commas between arguments. Draw the plot to generate an editable Fourier equation.\n\n" +
            "Live edits crossfade without restarting phase. Invalid equations retain the last valid sound.",
            "Waveform equations", MessageBoxButtons.OK, MessageBoxIcon.Information));
        toolbar.Controls.Add(name);
        var presets = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
        presets.Items.AddRange(new object[] { "Sine", "Saw", "Square", "Triangle", "Soft pulse", "FM texture" });
        presets.SelectedIndexChanged += (_, _) =>
        {
            equation.Text = presets.SelectedIndex switch
            {
                1 => "2*x-1", 2 => "sign(sin(tau*x))", 3 => "2/pi*asin(sin(tau*x))",
                4 => "tanh(3*sin(tau*x))", 5 => "sin(tau*x + 1.5*sin(3*tau*x))", _ => "sin(tau*x)"
            };
            drawn = null; QueueUpdate();
        };
        toolbar.Controls.Add(presets);
        var options = Flow();
        void Field(string text, Control control) { options.Controls.Add(new Label { Text = text, AutoSize = true, Margin = new Padding(6, 8, 2, 0) }); options.Controls.Add(control); }
        Field("Frequency Hz", frequency); Field("Level %", gain); Field("Length s", duration); Field("Phase cycles", phase);
        Field("Draw harmonics", harmonics); Field("Attack ms", attack); Field("Release ms", release);
        rate.Items.AddRange(new object[] { "22050", "44100", "48000", "96000" }); rate.SelectedIndex = 1;
        Field("Render Hz", rate); Field("Channels", channels); options.Controls.Add(normalize); options.Controls.Add(dc);
        var tools = Flow();
        Tool(tools, "Apply equation", () => { drawn = null; UpdateWave(true); });
        Tool(tools, "Smooth drawing", () =>
        {
            var input = drawn ?? cycle; var smooth = new double[input.Length];
            for (int i = 0; i < smooth.Length; i++) smooth[i] = (input[(i + input.Length - 1) % input.Length] + 2 * input[i] + input[(i + 1) % input.Length]) / 4;
            ApplyDrawing(smooth);
        });
        Tool(tools, "Invert", () => ApplyDrawing((drawn ?? cycle).Select(v => -v).ToArray()));
        Tool(tools, "Reverse", () => ApplyDrawing((drawn ?? cycle).Reverse().ToArray()));
        Tool(tools, "Clear", () => ApplyDrawing(new double[WaveformModel.TableSize]));
        tools.Controls.Add(new Label { Text = "Draw: left mouse. x = phase 0…1; tau = 2*pi. Live audio uses smoothed updates and Nyquist-limited tables.", AutoSize = true, MaximumSize = new Size(480, 0) });
        root.Controls.Add(toolbar, 0, 0); root.Controls.Add(equation, 0, 1); root.Controls.Add(options, 0, 2);
        root.Controls.Add(plot, 0, 3); root.Controls.Add(tools, 0, 4); root.Controls.Add(status, 0, 5); Controls.Add(root);
        parameterHelp.SetToolTip(equation, "Define one periodic cycle with x/phase from 0 to 1. tau = 2*pi. Use decimal dots; implicit multiplication and functions such as sin, tanh, min, max and clamp are supported. Invalid equations retain the last valid sound.");
        parameterHelp.SetToolTip(frequency, "Cycle repetition rate in hertz; this sets the fundamental pitch. Harmonics above Nyquist are limited when creating playback/render tables.");
        parameterHelp.SetToolTip(gain, "Final output amplitude as a percentage. Lower this to leave headroom; normalization of the cycle is controlled separately.");
        parameterHelp.SetToolTip(duration, "Length of the rendered audio object in seconds. Live prehear is a continuous oscillator and is not shortened by this setting.");
        parameterHelp.SetToolTip(phase, "Initial phase offset measured in cycles. One complete cycle is 1; phase changes the starting point of the waveform, not its frequency.");
        parameterHelp.SetToolTip(harmonics, "Number of Fourier harmonics used to turn a drawing into an editable equation. More harmonics preserve sharper detail but may add high-frequency content.");
        parameterHelp.SetToolTip(attack, "Fade-in duration of rendered audio in milliseconds. Use a short attack to reduce an abrupt start click.");
        parameterHelp.SetToolTip(release, "Fade-out duration of rendered audio in milliseconds. This softens the end of the selected output duration.");
        parameterHelp.SetToolTip(rate, "Sample rate of the rendered audio object. Higher rates increase the Nyquist limit and output size; they do not change the oscillator's fundamental frequency.");
        parameterHelp.SetToolTip(channels, "Number of output channels in the rendered audio object. Increasing channel count also increases its memory and file size.");
        parameterHelp.SetToolTip(normalize, "Scale the periodic cycle to a bounded reference peak before applying output level. Disable to retain relative amplitudes specified by the equation or drawing.");
        parameterHelp.SetToolTip(dc, "Remove the cycle's constant (DC) offset before playback/rendering. This centers asymmetric equations without changing their repetition rate.");
        parameterHelp.SetToolTip(name, "Name assigned to newly rendered audio objects. Saving a waveform stores its editable settings, not an already rendered recording.");
        parameterHelp.SetToolTip(presets, "Load a starting equation. Subsequent parameter edits and drawing can be undone/redone; rendering creates a separate audio object.");
        interactionHints.SetToolTip(plot, "Draw one cycle with the left mouse button. The horizontal axis is phase 0 to 1; the vertical axis is amplitude. Drawing is converted to a Fourier equation using Draw harmonics.");
        interactionHints.SetToolTip(hear, "Start or stop the continuous live oscillator. Valid edits crossfade into playback without restarting the current phase.");
        equation.TextChanged += (_, _) => { if (!editing) { drawn = null; QueueUpdate(); } };
        foreach (var n in new[] { frequency, gain, duration, phase, harmonics, attack, release, channels }) n.ValueChanged += (_, _) => QueueUpdate();
        rate.SelectedIndexChanged += (_, _) => QueueUpdate(); normalize.CheckedChanged += (_, _) => QueueUpdate(); dc.CheckedChanged += (_, _) => QueueUpdate();
        name.TextChanged += (_, _) => QueueUpdate();
        debounce.Tick += (_, _) => { debounce.Stop(); Safe(() => { if (drawn != null) SetDrawnEquation(); UpdateWave(!drawing); }); };
        plot.Paint += PaintWave;
        plot.MouseDown += (_, e) => { if (e.Button != MouseButtons.Left) return; drawing = true; drawn ??= (double[])cycle.Clone(); lastDraw = e.Location; plot.Capture = true; DrawTo(e.Location); };
        plot.MouseMove += (_, e) => { if (drawing) DrawTo(e.Location); };
        plot.MouseUp += (_, _) => { if (!drawing) return; drawing = false; plot.Capture = false; debounce.Stop(); SetDrawnEquation(); UpdateWave(true); };
        FormClosing += (_, e) => { if (rendering) { closeAfterRender = true; renderCancellation?.Cancel(); status.Text = "Cancelling render…"; e.Cancel = true; } else StopHear(); };
        ApplySettings(settings);
    }
    private static NumericUpDown Number(decimal min, decimal max, decimal value, decimal step) => new()
    { Minimum = min, Maximum = max, Value = value, Increment = step, DecimalPlaces = step < 1 ? 2 : 0, Width = 76 };
    private static FlowLayoutPanel Flow() => new() { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
    private void Safe(Action action) { try { action(); } catch (Exception error) { status.Text = error.Message; } }
    private void QueueUpdate() { if (editing) return; debounce.Stop(); debounce.Start(); }
    private WaveformSettings ReadSettings() => new()
    {
        Name = name.Text, Equation = equation.Text, Frequency = (double)frequency.Value, Gain = (double)gain.Value / 100,
        Duration = (double)duration.Value, Phase = (double)phase.Value, Harmonics = (int)harmonics.Value,
        AttackMilliseconds = (double)attack.Value, ReleaseMilliseconds = (double)release.Value,
        SampleRate = int.Parse(rate.Text), Channels = (int)channels.Value, Normalize = normalize.Checked, RemoveDc = dc.Checked,
        DrawnCycle = drawn == null ? null : (double[])drawn.Clone()
    };
    private void UpdateWave(bool commit)
    {
        try
        {
            var next = ReadSettings(); var newCycle = WaveformModel.BuildCycle(next);
            float[] table = WaveformModel.BandLimitedTable(newCycle, next.Frequency, 44100);
            cycle = newCycle; settings = next; live?.Update(table, next.Frequency, next.Gain, next.Phase);
            if (commit && System.Text.Json.JsonSerializer.Serialize(committed) != System.Text.Json.JsonSerializer.Serialize(next))
            { undo.Push(committed.Clone()); redo.Clear(); committed = next.Clone(); }
            status.Text = $"Valid — {next.Frequency:0.##} Hz; peak {cycle.Max(Math.Abs):0.000}. Edit the equation or draw a cycle. { (live != null ? "Live preview is playing." : "")}";
            plot.Invalidate();
        }
        catch (Exception error) when (error is FormatException or ArgumentException or ArithmeticException)
        { status.Text = error.Message + " Last valid waveform remains active."; }
    }
    private void ApplySettings(WaveformSettings value)
    {
        value.Validate(); debounce.Stop(); editing = true;
        try
        {
            name.Text = value.Name; equation.Text = value.Equation; frequency.Value = (decimal)value.Frequency;
            gain.Value = (decimal)(value.Gain * 100); duration.Value = (decimal)value.Duration; phase.Value = (decimal)value.Phase;
            harmonics.Value = value.Harmonics; attack.Value = (decimal)value.AttackMilliseconds; release.Value = (decimal)value.ReleaseMilliseconds;
            channels.Value = value.Channels; rate.SelectedItem = value.SampleRate.ToString(); normalize.Checked = value.Normalize; dc.Checked = value.RemoveDc;
            drawn = value.DrawnCycle == null ? null : (double[])value.DrawnCycle.Clone();
        }
        finally { editing = false; }
        UpdateWave(false); committed = value.Clone();
    }
    private void History(bool forward)
    {
        var source = forward ? redo : undo; var destination = forward ? undo : redo;
        if (source.Count == 0) return; debounce.Stop(); destination.Push(committed.Clone()); ApplySettings(source.Pop());
    }
    private void ApplyDrawing(double[] values) { drawn = values; SetDrawnEquation(); UpdateWave(true); }
    private void SetDrawnEquation()
    {
        if (drawn == null) return;
        editing = true;
        try { equation.Text = WaveformModel.FourierEquation(drawn, (int)harmonics.Value); }
        finally { editing = false; }
    }
    private void DrawTo(Point point)
    {
        if (drawn == null) return;
        int Index(int x) => Math.Clamp((int)Math.Round((x - 35) / (double)Math.Max(1, plot.Width - 50) * (drawn.Length - 1)), 0, drawn.Length - 1);
        double Value(int y) => Math.Clamp(1 - 2 * (y - 15) / (double)Math.Max(1, plot.Height - 40), -1, 1);
        int first = Index(lastDraw.X), last = Index(point.X); double a = Value(lastDraw.Y), b = Value(point.Y);
        for (int i = Math.Min(first, last); i <= Math.Max(first, last); i++)
            drawn[i] = first == last ? b : a + (b - a) * (i - first) / (last - first);
        lastDraw = point; plot.Invalidate();
        // Throttle drawing updates rather than restarting a debounce on every mouse move:
        // continuous strokes must remain audible, not wait until the mouse stops.
        if (!debounce.Enabled) debounce.Start();
    }
    private void PaintWave(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(Color.FromArgb(25, 28, 34));
        int width = Math.Max(1, plot.Width - 50), height = Math.Max(1, plot.Height - 40);
        using var grid = new Pen(Color.FromArgb(55, 62, 70));
        for (int i = 0; i <= 8; i++) g.DrawLine(grid, 35 + width * i / 8, 15, 35 + width * i / 8, 15 + height);
        for (int i = 0; i <= 4; i++) g.DrawLine(grid, 35, 15 + height * i / 4, 35 + width, 15 + height * i / 4);
        TextRenderer.DrawText(g, "+1", Font, new Point(2, 10), Color.Silver); TextRenderer.DrawText(g, "−1", Font, new Point(2, height + 5), Color.Silver);
        TextRenderer.DrawText(g, "x: phase 0 → 1", Font, new Point(35, height + 18), Color.Silver);
        var values = drawing && drawn != null ? drawn : cycle;
        using var pen = new Pen(Color.LightSkyBlue, 2);
        PointF? prior = null;
        for (int x = 0; x < width; x++)
        {
            double value = values[Math.Min(values.Length - 1, x * values.Length / width)];
            var p = new PointF(35 + x, (float)(15 + height * (1 - value) / 2));
            if (prior is { } before) g.DrawLine(pen, before, p); prior = p;
        }
    }
    private void ToggleHear()
    {
        if (output != null) { StopHear(); return; }
        UpdateWave(true);
        try
        {
            live = new(WaveformModel.BandLimitedTable(cycle, settings.Frequency, 44100), settings.Frequency, 0);
            output = new WaveOut { BufferMilliseconds = 60 }; output.Init(live); output.Play();
            live.Update(WaveformModel.BandLimitedTable(cycle, settings.Frequency, 44100), settings.Frequency, settings.Gain, settings.Phase);
            hear.Text = "Stop prehear";
        }
        catch (Exception error) { StopHear(); status.Text = error.Message; }
    }
    private void StopHear() { output?.Stop(); output?.Dispose(); output = null; live = null; hear.Text = "Prehear"; }
    private async Task RenderAsync()
    {
        if (rendering) return;
        WaveformSettings snapshot;
        try { snapshot = ReadSettings(); WaveformModel.BuildCycle(snapshot); }
        catch (Exception error) { status.Text = error.Message; return; }
        rendering = true; renderCancellation = new();
        try
        {
            var progress = new Progress<double>(p => { if (!IsDisposed) status.Text = $"Rendering {p:P0}"; });
            var token = renderCancellation.Token;
            var audio = await Task.Run(() => WaveformModel.Render(snapshot, token, progress), token);
            if (IsDisposed || disposedResources || token.IsCancellationRequested) { audio.Dispose(); return; }
            if (collection == null || collection.IsDisposed) collection = new AudioCollectionView([], "Modelled Waveforms");
            collection.AudioC.Audios.Add(audio); collection.Show(); status.Text = "Rendered AudioObj added to Modelled Waveforms.";
        }
        catch (OperationCanceledException) { if (!IsDisposed) status.Text = "Render cancelled."; }
        catch (Exception error) { if (!IsDisposed) status.Text = error.Message; }
        finally
        {
            rendering = false; renderCancellation.Dispose(); renderCancellation = null;
            if (closeAfterRender && !IsDisposed) Close();
        }
    }
    private void Save()
    {
        Directory.CreateDirectory(WaveformSettings.PresetDirectory);
        using var dialog = new SaveFileDialog { Filter = "Waveform model (*.json)|*.json", InitialDirectory = WaveformSettings.PresetDirectory, FileName = "waveform.json" };
        if (dialog.ShowDialog(this) == DialogResult.OK) { ReadSettings().Save(dialog.FileName); status.Text = "Waveform model saved."; }
    }
    private void LoadPreset()
    {
        Directory.CreateDirectory(WaveformSettings.PresetDirectory);
        using var dialog = new OpenFileDialog { Filter = "Waveform model (*.json)|*.json", InitialDirectory = WaveformSettings.PresetDirectory };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var loaded = WaveformSettings.Load(dialog.FileName); undo.Push(committed.Clone()); redo.Clear(); ApplySettings(loaded);
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (!equation.ContainsFocus && !name.ContainsFocus)
        {
            if (keyData == (Keys.Control | Keys.Z)) { History(false); return true; }
            if (keyData == (Keys.Control | Keys.Y)) { History(true); return true; }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposedResources) { disposedResources = true; parameterHelp.Dispose(); interactionHints.Dispose(); debounce.Dispose(); StopHear(); renderCancellation?.Cancel(); }
        base.Dispose(disposing);
    }
    private sealed class Plot : Control { public Plot() { DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw, true); } }
}
