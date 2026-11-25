using ModularAudience.Audio;
using ModularAudience.Audio.Synthesis.Runtime;
using NAudio.Wave;
using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ModularAudience.Forms.Modules.Dialogs;

/// <summary>
/// Generic instrument synth window for custom (user-created) procedural instruments.
/// Mirrors the layout of InstrumentSampleSynthDialog but renders via the runtime synth host
/// instead of the hardcoded InstrumentSampleSynth engine.
/// </summary>
public sealed partial class CustomInstrumentSynthWindow : Form
{
    private readonly CustomInstrumentSynthHost host;
    private readonly Label status = new() { Dock = DockStyle.Fill, AutoEllipsis = true, Text = "Ready." };
    private readonly WavePicture picture = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(22, 24, 30) };
    private readonly Button hear = new() { Text = "Audition", AutoSize = true };
    private readonly CheckBox loop = new() { Text = "Loop", AutoSize = true };
    private readonly NumericUpDown note = new() { Minimum = 0, Maximum = 127, Value = 60, Width = 80 };
    private readonly NumericUpDown gain = new() { Minimum = 0, Maximum = 100, Value = 75, Width = 80 };
    private readonly System.Windows.Forms.Timer playbackTimer = new() { Interval = 40 };

    private WaveOut? output;
    private SynthPreviewProvider? previewProvider;
    private bool disposed;

    public CustomInstrumentSynthWindow(CustomInstrumentSynthHost host)
    {
        this.host = host ?? throw new ArgumentNullException(nameof(host));

        Text = $"Custom Instrument — {host.Descriptor.Name}"; Name = "CustomInstrumentSynthWindow";
        ClientSize = new Size(900, 600); MinimumSize = new Size(700, 450); StartPosition = FormStartPosition.CenterScreen;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(10) };
        root.ColumnStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.AutoSize));
        root.RowStyles.Add(new(SizeType.Percent, 40));
        root.RowStyles.Add(new(SizeType.Percent, 40));
        root.RowStyles.Add(new(SizeType.Absolute, 55));

        var top = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        top.Controls.Add(new Label { Text = "Instrument", AutoSize = true, Margin = new Padding(5, 8, 2, 0) });
        var nameLabel = new Label { Text = host.Descriptor.Name, AutoSize = true, Font = new Font(Font.FontFamily, 10, FontStyle.Bold) };
        top.Controls.Add(nameLabel);
        top.Controls.Add(new Label { Text = "MIDI Note", AutoSize = true, Margin = new Padding(15, 8, 2, 0) });
        top.Controls.Add(note);
        top.Controls.Add(new Label { Text = "Gain (%)", AutoSize = true, Margin = new Padding(15, 8, 2, 0) });
        top.Controls.Add(gain);
        top.Controls.Add(hear); top.Controls.Add(loop);
        root.Controls.Add(top, 0, 0);

        picture.Paint += PaintWave;
        root.Controls.Add(picture, 0, 1);

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        bottom.Controls.Add(status); status.Width = 560; status.Height = 45; status.Dock = DockStyle.None;
        root.Controls.Add(bottom, 0, 2);

        Controls.Add(root);

        hear.Click += async (_, _) => await ToggleHearAsync();
        loop.CheckedChanged += (_, _) =>
        {
            if (previewProvider != null) { previewProvider.Loop = loop.Checked; }
        };
        note.ValueChanged += (_, _) => { picture.Invalidate(); };
        gain.ValueChanged += (_, _) => { picture.Invalidate(); };
        playbackTimer.Tick += (_, _) =>
        {
            if (previewProvider != null && previewProvider.Finished) { StopHear(); }
            picture.Invalidate();
        };

        Shown += (_, _) => QueueRender();
        FormClosing += (_, _) =>
        {
            StopHear();
            playbackTimer.Dispose();
        };
    }

    private void QueueRender()
    {
        if (disposed) return;
        _ = RenderAsync();
    }

    private async Task RenderAsync()
    {
        try
        {
            IRuntimeProceduralSynth? synth = host.ActiveSynth;
            int rate = host.Descriptor.SampleRate;
            int ch = host.Descriptor.Channels;
            int frames = (int)Math.Ceiling(host.GateSeconds() * rate);
            var buffer = new float[frames * ch];

            if (synth != null)
            {
                int written = synth.Render(buffer, frames);
                previewProvider = new SynthPreviewProvider(rate, ch) { Data = buffer[..written], Loop = loop.Checked };
            }
            else
            {
                previewProvider = new SynthPreviewProvider(rate, ch) { Data = [], Loop = loop.Checked };
            }

            status.Text = $"{host.Descriptor.Name} · {rate} Hz · {ch} ch · {frames / (double)rate:0.###} s";
            picture.Invalidate();
        }
        catch (Exception ex)
        {
            status.Text = $"Render error: {ex.Message}";
        }
    }

    private async Task ToggleHearAsync()
    {
        if (output != null) { StopHear(); return; }

        if (previewProvider == null) { QueueRender(); await Task.Delay(100); }
        if (previewProvider == null) return;

        output = new WaveOut { BufferMilliseconds = 60 };
        output.Init(previewProvider);
        output.Play();
        hear.Text = "Stop audition";
        playbackTimer.Start();
    }

    private void StopHear()
    {
        playbackTimer.Stop();
        output?.Stop();
        output?.Dispose();
        output = null;
        hear.Text = "Audition";
    }

    private void PaintWave(object? sender, PaintEventArgs e)
    {
        if (previewProvider == null || previewProvider.Data.Length == 0) return;
        var g = e.Graphics; float middle = picture.Height / 2f, scale = Math.Max(1, picture.Height / 2f - 18);
        using var grid = new Pen(Color.FromArgb(55, 60, 70));
        using var wave = new Pen(Color.Turquoise);
        for (int i = 0; i <= 8; i++)
            g.DrawLine(grid, i * picture.Width / 8, 0, i * picture.Width / 8, picture.Height);
        g.DrawLine(grid, 0, middle, picture.Width, middle);

        int dataLen = previewProvider.Data.Length;
        int ch = previewProvider.Channels;
        int frames = dataLen / ch;
        for (int x = 0; x < picture.Width; x++)
        {
            int a = x * frames / Math.Max(1, picture.Width);
            int b = Math.Min(frames, Math.Max(a + 1, (x + 1) * frames / Math.Max(1, picture.Width)));
            double min = 0, max = 0;
            for (int j = a; j < b; j++)
                for (int c = 0; c < ch; c++)
                {
                    float s = previewProvider.Data[j * ch + c];
                    min = Math.Min(min, s); max = Math.Max(max, s);
                }
            g.DrawLine(wave, x, middle - (float)max * scale, x, middle - (float)min * scale);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            disposed = true;
            StopHear();
            playbackTimer.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed class WavePicture : PictureBox
    {
        public WavePicture() { DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw, true); }
    }

    private sealed class SynthPreviewProvider : ISampleProvider
    {
        private readonly object gate = new();
        private float[] data = [];
        private int position;
        private float fade;
        public WaveFormat WaveFormat { get; }
        public bool Loop { get; set; }
        public bool Finished { get; private set; }
        public float[] Data
        {
            get
            {
                lock (gate) { return data; }
            }
            set
            {
                lock (gate) { data = value; position = 0; fade = 0; Finished = false; }
            }
        }
        public int Channels => WaveFormat.Channels;

        public SynthPreviewProvider(int rate, int channels) => WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(rate, channels);

        public int Read(Span<float> buffer)
        {
            lock (gate)
            {
                int channels = WaveFormat.Channels;
                for (int i = 0; i < buffer.Length; i += channels)
                {
                    if (position >= data.Length)
                    {
                        if (Loop) { position = 0; fade = 0; }
                        else { Finished = true; }
                    }
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
