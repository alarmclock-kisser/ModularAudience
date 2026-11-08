using System.ComponentModel;
using System.Text.Json;

namespace ModularAudience.Audio.Processing;

public enum SampleEffectKind
{
    Gain, Normalize, RemoveDc, Invert, Reverse, FadeIn, FadeOut, Trim, Silence,
    LowPass, HighPass, BandPass, Notch, PeakEq, LowShelf, HighShelf,
    Compressor, Limiter, NoiseGate, SoftClip, HardClip, WaveFold, Rectify, BitCrush,
    Tremolo, RingModulation, AutoPan, StereoWidth, Pan, SwapChannels, Mono,
    Delay, PingPongDelay, Reverb, Chorus, Flanger, Phaser,
    Varispeed, PitchShift, TimeStretch, WaveShaper, DrawWaveform, VolumeEnvelope
}

public sealed record SampleDrawPoint(double Position, double Value);

public sealed class SampleEffect
{
    [Category("1. Effect")] public SampleEffectKind Kind { get; set; }
    [Category("1. Effect")] public bool Enabled { get; set; } = true;
    [Category("1. Effect"), Description("Wet signal amount, 0 to 100 percent.")]
    public double MixPercent { get; set; } = 100;
    [Category("2. Selection"), Description("Start of the affected region, percent of the current signal.")]
    public double StartPercent { get; set; }
    [Category("2. Selection")] public double EndPercent { get; set; } = 100;
    [Category("3. Filter / level"), Description("Filter/EQ frequency or ring-modulation carrier, in Hz.")]
    public double FrequencyHz { get; set; } = 1000;
    [Category("3. Filter / level"), Description("Filter resonance / EQ bandwidth. 0.1 to 30.")]
    public double Q { get; set; } = 0.707;
    [Category("3. Filter / level"), Description("Gain/EQ gain, normalize target or dynamics threshold, in dB.")]
    public double GainDb { get; set; }
    [Category("4. Character"), Description("Drive (distortion); ratio (compressor); bits (bitcrush); width (stereo); slope (fade); decay (reverb).")]
    public double Amount { get; set; } = 1;
    [Category("4. Character"), Description("Milliseconds: delay time, chorus/flanger base time, or dynamics attack.")]
    public double TimeMs { get; set; } = 100;
    [Category("4. Character"), Description("Delay feedback in percent; dynamics release in milliseconds.")]
    public double Feedback { get; set; } = 30;
    [Category("4. Character"), Description("Modulation rate in Hz. Bitcrush: sample-hold factor.")]
    public double Rate { get; set; } = 2;
    [Category("4. Character"), Description("Modulation depth, 0 to 100 percent. Pan: -100 left to +100 right.")]
    public double Depth { get; set; } = 50;
    [Category("5. Pitch / time"), Description("Pitch shift in semitones, -24 to +24.")]
    public double Semitones { get; set; }
    [Category("5. Pitch / time"), Description("Varispeed playback rate or time-stretch duration factor, 0.125 to 8.")]
    public double Factor { get; set; } = 1;
    [Category("6. Waveshaper"), Description("Transfer equation; x is the input amplitude. Example: tanh(3*x), sin(pi*x), x*abs(x).")]
    public string Equation { get; set; } = "tanh(3*x)";
    [Category("6. Waveshaper"), Description("Drawing channel: 0 = all channels, 1 = first, 2 = second, etc.")]
    public int Channel { get; set; }
    [Browsable(false)] public List<SampleDrawPoint> Points { get; set; } = [];

    public override string ToString() => $"{(Enabled ? "●" : "○")} {Kind} · {MixPercent:0}% [{StartPercent:0.#}–{EndPercent:0.#}%]";
    public static SampleEffect Create(SampleEffectKind kind)
    {
        var e = new SampleEffect { Kind = kind };
        switch (kind)
        {
            case SampleEffectKind.Gain: e.GainDb = 3; break;
            case SampleEffectKind.Normalize: e.GainDb = -1; break;
            case SampleEffectKind.LowPass: e.FrequencyHz = 4000; break;
            case SampleEffectKind.HighPass: e.FrequencyHz = 100; break;
            case SampleEffectKind.PeakEq: case SampleEffectKind.LowShelf: case SampleEffectKind.HighShelf: e.GainDb = 6; break;
            case SampleEffectKind.Compressor: e.GainDb = -18; e.Amount = 4; e.TimeMs = 10; e.Feedback = 100; break;
            case SampleEffectKind.Limiter: e.GainDb = -1; break;
            case SampleEffectKind.NoiseGate: e.GainDb = -40; e.TimeMs = 3; e.Feedback = 80; break;
            case SampleEffectKind.SoftClip: case SampleEffectKind.WaveFold: e.Amount = 3; break;
            case SampleEffectKind.HardClip: e.Amount = 2; break;
            case SampleEffectKind.BitCrush: e.Amount = 8; e.Rate = 4; break;
            case SampleEffectKind.Delay: case SampleEffectKind.PingPongDelay: e.TimeMs = 250; e.MixPercent = 35; break;
            case SampleEffectKind.Reverb: e.TimeMs = 50; e.Amount = 2; e.MixPercent = 30; break;
            case SampleEffectKind.Chorus: e.TimeMs = 18; e.Depth = 40; e.Rate = 0.7; e.MixPercent = 50; break;
            case SampleEffectKind.Flanger: e.TimeMs = 3; e.Depth = 80; e.Rate = 0.3; e.MixPercent = 50; break;
            case SampleEffectKind.Phaser: e.Rate = 0.4; e.FrequencyHz = 700; e.MixPercent = 50; break;
            case SampleEffectKind.Pan: e.Depth = 0; break;
            case SampleEffectKind.Varispeed: e.Factor = 1.25; break;
            case SampleEffectKind.PitchShift: e.Semitones = 7; break;
            case SampleEffectKind.TimeStretch: e.Factor = 1.5; break;
        }
        return e;
    }
}

public sealed class SampleModellerSettings
{
    public int Version { get; set; } = 1;
    public string Name { get; set; } = "My sample preset";
    public List<SampleEffect> Effects { get; set; } = [];
    public SampleModellerSettings Clone() => JsonSerializer.Deserialize<SampleModellerSettings>(JsonSerializer.Serialize(this))!;
    public void Validate()
    {
        if (Version != 1 || Effects == null || Effects.Count > 128) throw new ArgumentException("Unsupported preset or too many effects (maximum 128).");
        foreach (var e in Effects)
        {
            if (e == null || !Enum.IsDefined(e.Kind)) throw new ArgumentException("Unknown effect.");
            double[] values = [e.MixPercent, e.StartPercent, e.EndPercent, e.FrequencyHz, e.Q, e.GainDb, e.Amount, e.TimeMs, e.Feedback, e.Rate, e.Depth, e.Semitones, e.Factor];
            if (values.Any(v => !double.IsFinite(v)) || e.StartPercent < 0 || e.EndPercent > 100 || e.StartPercent >= e.EndPercent
                || e.MixPercent < 0 || e.MixPercent > 100 || e.FrequencyHz < 0 || e.Q < 0.1 || e.Q > 30
                || e.GainDb < -96 || e.GainDb > 48 || e.Amount < 0 || e.Amount > 100 || e.TimeMs < 0 || e.TimeMs > 10000
                || e.Feedback < 0 || e.Feedback > 2000 || e.Rate < 0 || e.Rate > 20000 || Math.Abs(e.Depth) > 100
                || Math.Abs(e.Semitones) > 24 || e.Factor < 0.125 || e.Factor > 8 || e.Channel < 0 || e.Channel > 32)
                throw new ArgumentException("Effect parameters are outside their supported ranges.");
            if (e.Points == null || e.Points.Count > 200000 || e.Points.Any(p => !double.IsFinite(p.Position) || !double.IsFinite(p.Value)
                || p.Position < 0 || p.Position > 1 || Math.Abs(p.Value) > 4)) throw new ArgumentException("Invalid drawing points.");
        }
    }
}

public sealed class SampleModellerPresetStore
{
    public static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ModularAudience", "SampleModellerPresets");
    private readonly string directory;
    public SampleModellerPresetStore(string? directory = null) => this.directory = directory ?? DefaultDirectory;
    private string PathFor(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name is "." or "..")
            throw new ArgumentException("Enter a preset name without file-name special characters (maximum 100 characters).");
        return Path.Combine(directory, name + ".json");
    }
    public string[] Names() => Directory.Exists(directory) ? Directory.GetFiles(directory, "*.json").Select(p => Path.GetFileNameWithoutExtension(p)!).Order(StringComparer.OrdinalIgnoreCase).ToArray() : [];
    public SampleModellerSettings Load(string name)
    {
        var result = JsonSerializer.Deserialize<SampleModellerSettings>(File.ReadAllText(PathFor(name))) ?? throw new ArgumentException("Empty preset.");
        result.Validate(); return result;
    }
    public void Save(string name, SampleModellerSettings settings)
    {
        settings.Validate(); string path = PathFor(name); Directory.CreateDirectory(directory);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true })); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public void Delete(string name) => File.Delete(PathFor(name));
}
