using System.Globalization;
using System.Numerics;
using System.Text.Json;
using MathNet.Numerics.IntegralTransforms;
using NAudio.Wave;

namespace ModularAudience.Audio.Synthesis;

public sealed class WaveformSettings
{
    public int Version { get; set; } = 1;
    public string Name { get; set; } = "Modelled waveform";
    public string Equation { get; set; } = "sin(tau*x)";
    public double Frequency { get; set; } = 220;
    public double Gain { get; set; } = 0.3;
    public double Duration { get; set; } = 5;
    public double Phase { get; set; }
    public double AttackMilliseconds { get; set; } = 5;
    public double ReleaseMilliseconds { get; set; } = 20;
    public int SampleRate { get; set; } = 44100;
    public int Channels { get; set; } = 2;
    public int Harmonics { get; set; } = 32;
    public bool Normalize { get; set; } = true;
    public bool RemoveDc { get; set; } = true;
    public double[]? DrawnCycle { get; set; }
    public WaveformSettings Clone() => JsonSerializer.Deserialize<WaveformSettings>(JsonSerializer.Serialize(this))!;
    public void Validate()
    {
        if (Version != 1 || string.IsNullOrWhiteSpace(Name) || !double.IsFinite(Frequency) || Frequency is < 0.1 or > 20000
            || !double.IsFinite(Gain) || Gain is < 0 or > 1 || !double.IsFinite(Duration) || Duration is < 0.01 or > 3600
            || !double.IsFinite(Phase) || Phase is < 0 or > 1 || SampleRate is not (22050 or 44100 or 48000 or 96000)
            || Channels is < 1 or > 8 || Harmonics is < 1 or > 128
            || !double.IsFinite(AttackMilliseconds) || AttackMilliseconds is < 0 or > 10000
            || !double.IsFinite(ReleaseMilliseconds) || ReleaseMilliseconds is < 0 or > 10000)
            throw new ArgumentException("Invalid waveform settings.");
        if (DrawnCycle != null && (DrawnCycle.Length != WaveformModel.TableSize || DrawnCycle.Any(v => !double.IsFinite(v) || Math.Abs(v) > 1)))
            throw new ArgumentException("A stored drawing must contain 2048 finite values in the range -1…1.");
    }
    public static string PresetDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ModularAudience", "Waveforms");
    public void Save(string path) { Validate(); WaveformModel.BuildCycle(this); File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); }
    public static WaveformSettings Load(string path)
    {
        var result = JsonSerializer.Deserialize<WaveformSettings>(File.ReadAllText(path)) ?? throw new InvalidDataException("Empty waveform file.");
        result.Validate(); WaveformModel.BuildCycle(result); return result;
    }
}

public static class WaveformModel
{
    public const int TableSize = 2048;
    public static double[] BuildCycle(WaveformSettings settings)
    {
        settings.Validate(); var expression = WaveExpression.Compile(settings.Equation);
        var cycle = new double[TableSize];
        for (int i = 0; i < cycle.Length; i++)
        {
            double value;
            try { value = expression(i / (double)cycle.Length); }
            catch (ArithmeticException error) { throw new ArgumentException("Equation evaluation failed: " + error.Message); }
            if (!double.IsFinite(value) || Math.Abs(value) > 1e6) throw new ArgumentException($"Equation is not finite/bounded at x={i / (double)cycle.Length:0.0000}.");
            cycle[i] = value;
        }
        double mean = settings.RemoveDc ? cycle.Average() : 0;
        for (int i = 0; i < cycle.Length; i++) cycle[i] -= mean;
        double peak = cycle.Max(Math.Abs);
        if (settings.Normalize && peak > 1e-12) for (int i = 0; i < cycle.Length; i++) cycle[i] /= peak;
        else if (peak > 1) throw new ArgumentException("Waveform exceeds ±1. Enable Normalize or reduce the equation's level.");
        return cycle;
    }
    public static float[] BandLimitedTable(double[] cycle, double frequency, int sampleRate)
    {
        var fft = cycle.Select(v => new Complex(v, 0)).ToArray(); Fourier.Forward(fft, FourierOptions.Matlab);
        int maximum = Math.Clamp((int)Math.Floor(sampleRate * 0.48 / frequency), 0, cycle.Length / 2 - 1);
        for (int i = maximum + 1; i < fft.Length - maximum; i++) fft[i] = Complex.Zero;
        Fourier.Inverse(fft, FourierOptions.Matlab);
        double peak = fft.Max(v => Math.Abs(v.Real));
        double scale = peak > 1 ? 1 / peak : 1;
        return fft.Select(v => (float)(v.Real * scale)).ToArray();
    }
    public static string FourierEquation(IReadOnlyList<double> cycle, int harmonics)
    {
        if (cycle.Count == 0) throw new ArgumentException("Draw a waveform first.");
        var fft = cycle.Select(v => new Complex(v, 0)).ToArray(); Fourier.Forward(fft, FourierOptions.Matlab);
        var terms = new List<string>();
        string Number(double value) => value.ToString("0.########", CultureInfo.InvariantCulture);
        if (Math.Abs(fft[0].Real / cycle.Count) > 1e-7) terms.Add(Number(fft[0].Real / cycle.Count));
        for (int h = 1; h <= Math.Min(harmonics, cycle.Count / 2 - 1); h++)
        {
            double cosine = 2 * fft[h].Real / cycle.Count, sine = -2 * fft[h].Imaginary / cycle.Count;
            if (Math.Abs(cosine) > 1e-7) terms.Add($"({Number(cosine)})*cos(tau*{h}*x)");
            if (Math.Abs(sine) > 1e-7) terms.Add($"({Number(sine)})*sin(tau*{h}*x)");
        }
        return terms.Count > 0 ? string.Join(" + ", terms) : "0";
    }
    public static AudioObj Render(WaveformSettings settings, CancellationToken token = default, IProgress<double>? progress = null)
    {
        var cycle = BuildCycle(settings); var table = BandLimitedTable(cycle, settings.Frequency, settings.SampleRate);
        long frames = (long)Math.Round(settings.Duration * settings.SampleRate);
        if (frames * settings.Channels > int.MaxValue) throw new ArgumentException("The requested audio exceeds the RAM buffer limit.");
        var output = new float[checked((int)frames * settings.Channels)];
        for (int i = 0; i < frames; i++)
        {
            if ((i & 16383) == 0) { token.ThrowIfCancellationRequested(); progress?.Report(i / (double)frames); }
            double phase = (settings.Phase + i * settings.Frequency / settings.SampleRate) % 1;
            double index = phase * table.Length; int first = (int)index % table.Length;
            double value = table[first] + (table[(first + 1) % table.Length] - table[first]) * (index - first);
            double envelope = 1;
            if (settings.AttackMilliseconds > 0) envelope = Math.Min(envelope, i / (settings.AttackMilliseconds * settings.SampleRate / 1000));
            if (settings.ReleaseMilliseconds > 0) envelope = Math.Min(envelope, (frames - 1 - i) / (settings.ReleaseMilliseconds * settings.SampleRate / 1000));
            float sample = (float)(value * settings.Gain * Math.Max(0, envelope));
            for (int ch = 0; ch < settings.Channels; ch++) output[i * settings.Channels + ch] = sample;
        }
        progress?.Report(1);
        return new AudioObj { Name = settings.Name, Data = output, SampleRate = settings.SampleRate, Channels = settings.Channels,
            Length = output.Length, Duration = TimeSpan.FromSeconds(frames / (double)settings.SampleRate), BitDepth = 32, Volume = 100 };
    }
}

/// <summary>Fixed-format live oscillator. UI publishes tables, never evaluates equations on the audio thread.</summary>
public sealed class WaveformLiveProvider : ISampleProvider
{
    private sealed record State(float[] Table, double Frequency, double Gain, double Phase);
    private State pending;
    private State current;
    private State? previous;
    private int transition;
    private double phase, frequency, gain;
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
    private double phaseOffset;
    public WaveformLiveProvider(float[] table, double frequency, double gain) { pending = current = new(table, frequency, gain, 0); this.frequency = frequency; this.gain = gain; }
    public void Update(float[] table, double frequency, double gain, double phaseOffset = 0) => Volatile.Write(ref pending, new State(table, frequency, gain, phaseOffset));
    public int Read(float[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    public int Read(Span<float> buffer)
    {
        int frames = buffer.Length / 2;
        for (int i = 0; i < frames; i++)
        {
            State next = Volatile.Read(ref pending);
            if (!ReferenceEquals(next, current)) { previous = current; current = next; transition = 441; }
            frequency += (current.Frequency - frequency) * 0.0025; gain += (current.Gain - gain) * 0.0025;
            phaseOffset += (current.Phase - phaseOffset) * 0.0025;
            float Lookup(float[] table) { double at = ((phase + phaseOffset) % 1) * table.Length; int n = (int)at % table.Length; return (float)(table[n] + (table[(n + 1) % table.Length] - table[n]) * (at - Math.Floor(at))); }
            double sample = Lookup(current.Table);
            if (transition > 0 && previous != null)
            { double blend = transition-- / 441.0; sample = sample * (1 - blend) + Lookup(previous.Table) * blend; }
            buffer[i * 2] = buffer[i * 2 + 1] = (float)Math.Clamp(sample * gain, -1, 1);
            phase = (phase + frequency / 44100) % 1;
        }
        return frames * 2;
    }
}
