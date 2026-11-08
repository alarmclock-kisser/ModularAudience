namespace ModularAudience.Audio.Synthesis;

/// <summary>Procedural instruments: modal keyboards/percussion, feedback strings and harmonic wind/reed voices.</summary>
public static class InstrumentSampleSynth
{
    public static AudioObj Render(InstrumentSynthSettings settings, CancellationToken token = default)
    {
        settings.Validate(); token.ThrowIfCancellationRequested();
        var s = settings.Clone(); int rate = s.SampleRate, channels = s.Channels;
        int frames = checked((int)Math.Ceiling((s.GateSeconds + s.ReleaseSeconds) * rate));
        double frequency = 440 * Math.Pow(2, (s.MidiNote - 69 + s.PitchSemitones) / 12);
        var random = new Random(s.Seed);
        var data = new float[checked(frames * channels)];
        var voice = new Voice(s, frequency, random);
        var filters = Enumerable.Range(0, channels).Select(_ => new Lowpass()).ToArray();
        double phase = 0, secondPhase = 0, lastNoise = 0;
        double[] roomDelays = [0.0297, 0.0371, 0.0411, 0.0533];
        double peak = 0;
        for (int i = 0; i < frames; i++)
        {
            if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
            double t = i / (double)rate;
            double noise = random.NextDouble() * 2 - 1;
            double highNoise = noise - lastNoise; lastNoise = noise;
            double breath = noise * 0.15 + highNoise * 0.35;
            double vibrato = Math.Sin(2 * Math.PI * s.VibratoHz * t) * s.VibratoCents * (1 - Math.Exp(-t * 5));
            double f = frequency * Math.Pow(2, vibrato / 1200);
            phase += f / rate; phase -= Math.Floor(phase);
            secondPhase += f * Math.Pow(2, s.DetuneCents / 1200) / rate; secondPhase -= Math.Floor(secondPhase);
            double env = Envelope(s, t);
            double sound = voice.Next(t, phase, secondPhase, noise, breath, highNoise);
            double cutoffEnvelope = 1 + s.FilterEnvelope / 100 * 5 * Math.Exp(-t / Math.Max(0.01, s.DecaySeconds * 0.25));
            double cutoff = Math.Clamp(s.CutoffHz * (0.15 + 0.85 * s.Brightness / 100) * cutoffEnvelope, 20, rate * 0.45);
            double saturated = Math.Tanh(sound * (1 + s.Drive * 0.12)) / Math.Max(1, Math.Sqrt(1 + s.Drive * 0.12));
            for (int c = 0; c < channels; c++)
            {
                double stereo = channels == 1 ? 0 : (c == 0 ? -1 : 1) * s.StereoWidth / 100;
                double widening = s.Family == SynthFamily.Organ
                    ? Math.Sin(2 * Math.PI * (0.7 + s.VibratoHz * 0.1) * t + c * Math.PI) * 0.18 * s.StereoWidth / 100
                    : Math.Sin(2 * Math.PI * frequency * Math.Pow(2, s.DetuneCents / 1200) * t + c * 0.4) * 0.08 * stereo;
                double value = filters[c].Apply((saturated + widening) * env, cutoff, s.Resonance, rate);
                if (s.RoomPercent > 0)
                {
                    for (int tap = 0; tap < roomDelays.Length; tap++)
                    {
                        int delay = (int)(rate * (roomDelays[tap] + c * 0.0017));
                        if (i >= delay) value += data[(i - delay) * channels + c] * s.RoomPercent / 100 * (0.16 - tap * 0.025);
                    }
                }
                double tailFade = Math.Min(1, (frames - i) / Math.Max(1d, rate * 0.01));
                value *= tailFade;
                data[i * channels + c] = double.IsFinite(value) ? (float)Math.Clamp(value, -8, 8) : 0;
                peak = Math.Max(peak, Math.Abs(data[i * channels + c]));
            }
        }
        // Attenuate only: retain attack/sustain and character differences, never amplify near-silent tails.
        double level = s.GainPercent / 100 * (peak > 0.95 ? 0.95 / peak : 1);
        for (int i = 0; i < data.Length; i++)
        { if ((i & 8191) == 0) token.ThrowIfCancellationRequested(); data[i] *= (float)level; }
        string note = NoteName(s.MidiNote);
        var result = new AudioObj
        {
            Data = data, SampleRate = rate, Channels = channels, BitDepth = 32, Length = data.Length,
            Duration = TimeSpan.FromSeconds(frames / (double)rate), Bpm = (float)s.Bpm, Volume = 100
        };
        result.Rename($"{s.VoiceName} · {note}{(s.PitchSemitones == 0 ? "" : $" {s.PitchSemitones:+0.##;-0.##} st")} · {s.Bpm:0.##} BPM");
        return result;
    }

    public static string NoteName(int midi)
    {
        string[] names = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
        return names[midi % 12] + (midi / 12 - 1);
    }
    private static double Envelope(InstrumentSynthSettings s, double t)
    {
        double attack = s.AttackMs / 1000;
        double held = Math.Max(0, Math.Min(t, s.GateSeconds) - attack);
        double sustain = s.SustainPercent / 100;
        double decay = Math.Exp(-held / Math.Max(0.01, s.DecaySeconds));
        double envelope = t < attack ? t / Math.Max(0.00001, attack) : sustain + (1 - sustain) * decay;
        if (t > s.GateSeconds) envelope *= Math.Exp(-6 * (t - s.GateSeconds) / s.ReleaseSeconds);
        return envelope;
    }

    private sealed class Voice
    {
        private static readonly double[] MetalRatios = [1, 1.342, 1.987, 2.534, 3.117, 4.231];
        private readonly InstrumentSynthSettings s;
        private readonly double f;
        private readonly double[] stringBuffer;
        private int stringIndex;
        private readonly double[] harmonicWeights = new double[25];
        private readonly double[] modalRatios, modalAmplitudes;
        private readonly double[] modalPhases;
        private readonly string name;
        private double stringPrevious;
        private readonly SynthDrumKit kit;
        public Voice(InstrumentSynthSettings settings, double frequency, Random random)
        {
            s = settings; f = frequency;
            kit = InstrumentSynthCatalog.DrumKits[s.KitIndex]; name = s.VoiceName;
            int length = Math.Clamp((int)(s.SampleRate / Math.Max(20, f)), 2, s.SampleRate);
            stringBuffer = new double[length];
            int strike = Math.Clamp((int)(length * s.StrikePosition / 100), 1, length - 1);
            for (int i = 0; i < length; i++)
            {
                double triangle = i < strike ? i / (double)strike : (length - i) / (double)(length - strike);
                stringBuffer[i] = (random.NextDouble() * 2 - 1) * s.Noise / 100 * 0.5 + (triangle * 2 - 1) * 0.6;
            }
            for (int h = 1; h < harmonicWeights.Length; h++)
            {
                double brightness = 0.35 + s.Brightness / 65;
                double weight = Math.Exp(-(h - 1) / (2 + brightness * 7)) / Math.Pow(h, 0.7);
                if (s.Family == SynthFamily.Woodwinds)
                {
                    weight *= 0.6 + Math.Exp(-Math.Pow((h * f - (300 + s.Body * 14)) / (400 + s.Body * 5), 2));
                    if (name.Contains("Flute") || name == "Pan Pipes") weight *= h == 1 ? 1 : Math.Exp(-h * 0.75);
                    if (name == "Clarinet") weight *= h % 2 == 0 ? 0.05 : 1.4;
                    if (name == "Oboe") weight *= 0.3 + Math.Exp(-Math.Pow((h - 5) / 3d, 2)) * 2;
                    if (name == "Bassoon") weight *= h % 2 == 0 ? 0.45 : 1.3;
                    if (name == "Tenor Sax") weight *= 1 + 1.8 * Math.Exp(-Math.Pow((h - 4) / 2.5, 2));
                }
                if (s.Family == SynthFamily.Brass)
                {
                    weight *= Math.Exp(-Math.Pow((h * f - (350 + s.Brightness * 20 + s.Body * 10)) / (600 + s.Brightness * 15), 2)) + 0.15;
                    if (name == "Muted Trumpet") weight *= h < 3 ? 0.1 : 1.6;
                }
                if (s.Family == SynthFamily.BowedStrings)
                {
                    double formant = 1 + Math.Exp(-Math.Pow((h * f - (350 + s.Body * 9)) / 500, 2));
                    weight *= formant * Math.Sin(Math.PI * h * (0.08 + s.StrikePosition / 500));
                }
                if (s.Family == SynthFamily.Organ)
                {
                    weight = h switch { 1 => 0.8, 2 => 0.65, 3 => 0.4, 4 => 0.35, 6 => 0.25, 8 => 0.18, 12 => 0.12, _ => 0 };
                    if (name == "Flute Stops") weight *= h > 2 ? 0.05 : 1;
                    if (name == "Reed Organ") weight *= h % 2 == 0 ? 0.2 : 1.5;
                    if (name == "Cathedral Mixture") weight *= h >= 4 ? 2 : 0.7;
                    if (name == "Rock Tonewheel") weight *= h >= 3 ? 1.8 : 1;
                    if (name == "Church Principal") weight *= h > 4 ? 0.6 : 1;
                }
                harmonicWeights[h] = h * f < s.SampleRate * 0.45 ? weight : 0;
            }
            (modalRatios, modalAmplitudes) = ModalProfile(s, name);
            modalPhases = new double[modalRatios.Length];
        }
        public double Next(double t, double phase, double secondPhase, double noise, double breath, double highNoise)
        {
            double brightness = s.Brightness / 100, body = s.Body / 100;
            switch (s.Family)
            {
                case SynthFamily.DrumSet: return Drum(t, noise, highNoise);
                case SynthFamily.Keyboard: case SynthFamily.MalletsAndBells:
                    double modal = 0;
                    for (int h = 0; h < modalRatios.Length; h++)
                    {
                        double ratio = modalRatios[h]; if (ratio * f > s.SampleRate * 0.46) continue;
                        double decay = Math.Exp(-t * (0.3 + h * (1.2 - brightness)) / Math.Max(0.1, s.DecaySeconds));
                        double detune = 1 + s.DetuneCents / 1200 * (h % 2 == 0 ? 0.4 : -0.3);
                        double modulation = Math.Pow(2, Math.Sin(2 * Math.PI * s.VibratoHz * t) * s.VibratoCents * (1 - Math.Exp(-t * 5)) / 1200);
                        modalPhases[h] = (modalPhases[h] + f * ratio * detune * modulation / s.SampleRate) % 1;
                        modal += Math.Sin(2 * Math.PI * modalPhases[h]) * modalAmplitudes[h] * decay;
                    }
                    if (name is "Tine Electric" or "Glass FM Keys" or "Reed Electric")
                    {
                        double mod = name == "Glass FM Keys" ? 3.5 : name == "Tine Electric" ? 2 : 1;
                        modal += Math.Sin(2 * Math.PI * f * t + (0.4 + brightness * 3) * Math.Exp(-t * 3) * Math.Sin(2 * Math.PI * f * mod * t)) * 0.4;
                    }
                    return modal * 0.4 + noise * s.Noise / 100 * Math.Exp(-t * (name == "Felt Upright" ? 80 : 250)) * 0.5;
                case SynthFamily.PluckedStrings:
                    double current = stringBuffer[stringIndex];
                    int next = (stringIndex + 1) % stringBuffer.Length;
                    double average = current * (0.3 + brightness * 0.55) + stringBuffer[next] * (0.7 - brightness * 0.55);
                    double decayPerCycle = Math.Exp(-1 / (f * Math.Max(0.05, s.DecaySeconds)));
                    stringBuffer[stringIndex] = average * decayPerCycle;
                    stringIndex = next;
                    stringPrevious += (current - stringPrevious) * (0.02 + body * 0.12);
                    double resonant = name == "Banjo" ? Math.Sin(2 * Math.PI * 1200 * t) * Math.Exp(-t * 18) * 0.12 : 0;
                    return current * 0.7 + stringPrevious * body * 0.6 + resonant;
                case SynthFamily.SynthBassAndLead:
                    double saw = BandSaw(phase, f / s.SampleRate), other = BandSaw(secondPhase, f / s.SampleRate);
                    double sub = Math.Sin(2 * Math.PI * phase);
                    return name switch
                    {
                        "Sub Anchor" => sub * 0.85 + Math.Sin(4 * Math.PI * phase) * brightness * 0.2,
                        "Acid Resonator" => (saw * 0.65 + Square(phase, f / s.SampleRate) * 0.35) * (0.6 + 0.4 * Math.Exp(-t * 7)),
                        "Reese Bass" => (saw + other) * 0.35 + sub * 0.3,
                        "Rubber Pluck" => Math.Sin(2 * Math.PI * phase + 1.8 * Math.Exp(-t * 8) * Math.Sin(4 * Math.PI * phase)) * 0.7,
                        "Silk Lead" => Square(phase, f / s.SampleRate) * 0.35 + other * 0.25,
                        "Hard Sync Lead" => Math.Sin(2 * Math.PI * phase + (2 + brightness * 5) * Math.Sin(4 * Math.PI * phase)) * 0.65,
                        "FM Growl" => Math.Sin(2 * Math.PI * phase + (1 + brightness * 4) * (0.5 + 0.5 * Math.Sin(t * 9)) * Math.Sin(2 * Math.PI * phase * 2)) * 0.7,
                        _ => (saw + other + BandSaw((secondPhase + phase * 0.002) % 1, f / s.SampleRate)) * 0.22
                    };
                default:
                    double sum = 0;
                    for (int h = 1; h < harmonicWeights.Length; h++)
                    {
                        double breathAttack = s.Family == SynthFamily.Brass ? 1 - Math.Exp(-t * (10 + h * 2)) : 1;
                        sum += harmonicWeights[h] * Math.Sin(2 * Math.PI * phase * h) * breathAttack;
                        if (s.DetuneCents > 0) sum += harmonicWeights[h] * Math.Sin(2 * Math.PI * secondPhase * h) * (name.Contains("Ensemble") || name.Contains("Section") ? 0.65 : 0.15);
                    }
                    double transient = s.Family == SynthFamily.Organ ? Math.Sin(2 * Math.PI * f * 3 * t) * Math.Exp(-t * 12) * s.DrumSnap / 200 : 0;
                    double air = breath * s.Noise / 100 * (s.Family == SynthFamily.BowedStrings ? 0.35 + brightness * 0.3 : 0.3);
                    return sum * 0.28 + air + transient;
            }
        }
        private double Drum(double t, double noise, double highNoise)
        {
            double ratio = Math.Pow(2, s.PitchSemitones / 12), decay = Math.Max(0.01, s.DecaySeconds * kit.Decay);
            double sweep = s.DrumSweep / 100 * kit.Sweep, snap = s.DrumSnap / 100 * kit.Click;
            double pitchedNoise = noise * (s.Noise / 100 + 0.15) * kit.Noise;
            double sine(double hz, double drop, double speed) => Math.Sin(2 * Math.PI * ratio * kit.Tone * (hz * t + drop * (1 - Math.Exp(-t * speed)) / speed));
            double metal = 0;
            for (int j = 0; j < MetalRatios.Length; j++)
            {
                double hz = (1900 + s.Brightness * 45) * ratio * MetalRatios[j] * kit.Metal;
                if (hz < s.SampleRate * 0.46) metal += Math.Sin(2 * Math.PI * hz * t) / MetalRatios.Length;
            }
            double value = s.Drum switch
            {
                SynthDrum.Kick => sine(48, 180 * sweep, 45) * Math.Exp(-t / decay) + highNoise * snap * Math.Exp(-t * 450) * 0.55,
                SynthDrum.BassDrum => sine(35, 80 * sweep, 22) * Math.Exp(-t / (decay * 1.4)) + pitchedNoise * snap * Math.Exp(-t * 180) * 0.2,
                SynthDrum.Snare => (sine(175, 70 * sweep, 40) * Math.Exp(-t / (decay * 0.3)) * 0.5 + pitchedNoise * Math.Exp(-t / (decay * 0.45)) * 0.8),
                SynthDrum.Rimshot => (sine(380, 100 * sweep, 80) + sine(830, 0, 1) * 0.6) * Math.Exp(-t / (decay * 0.12)),
                SynthDrum.Clap => pitchedNoise * (Math.Exp(-t * 100) + Pulse(t, 0.012, 150) + Pulse(t, 0.025, 110) + Pulse(t, 0.04, 25 / decay)) * 0.7,
                SynthDrum.ClosedHiHat => (metal * 0.7 + highNoise * kit.Noise * 0.3) * Math.Exp(-t / (decay * 0.12)),
                SynthDrum.OpenHiHat => (metal * 0.7 + highNoise * kit.Noise * 0.3) * Math.Exp(-t / (decay * 0.8)),
                SynthDrum.LowTom => Tom(85), SynthDrum.MidTom => Tom(125), SynthDrum.HighTom => Tom(185),
                SynthDrum.Ride => (metal * 0.6 + sine(740, 0, 1) * 0.15 + highNoise * kit.Noise * 0.1) * Math.Exp(-t / (decay * 1.1)),
                SynthDrum.Crash => (metal * 0.55 + highNoise * kit.Noise * 0.45) * (1 - Math.Exp(-t * 150)) * Math.Exp(-t / (decay * 1.8)),
                SynthDrum.Cowbell => (Math.Sin(2 * Math.PI * 560 * ratio * kit.Tone * t) + Math.Sin(2 * Math.PI * 845 * ratio * kit.Tone * t) * 0.75) * Math.Exp(-t / (decay * 0.3)),
                _ => highNoise * kit.Noise * Math.Exp(-t / (decay * 0.2)) * (0.6 + 0.4 * Math.Sin(t * 700))
            };
            if (kit.Name == "Beach Wood") value = value * 0.7 + sine(450, 30, 40) * Math.Exp(-t * 50) * s.Body / 150;
            if (kit.Name == "Tribal Earth") value += sine(120, 25, 20) * Math.Exp(-t / (decay * 0.5)) * s.Body / 200;
            if (kit.Name == "Electro Glass") value += metal * Math.Exp(-t / (decay * 0.4)) * 0.25;
            if (kit.Name == "Lo-Fi Dust") { value = Math.Round(value * 48) / 48; value += noise * 0.015 * Math.Exp(-t / decay); }
            return Math.Tanh(value * (1 + kit.Drive * 5)) * 0.8;
            double Tom(double hz) => (sine(hz, hz * 0.8 * sweep, 25) + sine(hz * 1.59, hz * 0.2 * sweep, 30) * 0.25)
                * Math.Exp(-t / (decay * 0.7)) + pitchedNoise * snap * Math.Exp(-t * 120) * 0.1;
        }
        private static double Pulse(double t, double start, double rate) => t >= start ? Math.Exp(-(t - start) * rate) : 0;
        private static (double[], double[]) ModalProfile(InstrumentSynthSettings s, string name)
        {
            if (s.Family == SynthFamily.MalletsAndBells)
                return name switch
                {
                    "Marimba" => ([1, 3.99, 10.1, 18.3], [1, 0.3, 0.12, 0.04]),
                    "Vibraphone" => ([1, 4, 10, 16], [1, 0.35, 0.08, 0.03]),
                    "Xylophone" => ([1, 3, 6.05, 10], [1, 0.55, 0.3, 0.1]),
                    "Glockenspiel" => ([1, 2.76, 5.4, 8.93, 13.34], [1, 0.6, 0.35, 0.2, 0.1]),
                    "Tubular Bells" => ([1, 1.414, 2.01, 2.756, 4.07, 5.43], [0.8, 0.5, 0.6, 0.4, 0.2, 0.12]),
                    _ => ([1, 2, 2.98, 4.1, 5.8], [1, 0.6, 0.35, 0.2, 0.1])
                };
            double inharmonicity = name == "Toy Piano" ? 0.012 : 0.00004 + (100 - s.Body) * 0.000005;
            var ratios = Enumerable.Range(1, 12).Select(h => h * Math.Sqrt(1 + inharmonicity * h * h)).ToArray();
            var amplitudes = Enumerable.Range(1, 12).Select(h => Math.Exp(-(h - 1) / (1.5 + s.Brightness / 12)) / h).ToArray();
            return (ratios, amplitudes);
        }
    }
    private static double BandSaw(double phase, double dt) => 2 * phase - 1 - PolyBlep(phase, dt);
    private static double Square(double phase, double dt) => (phase < 0.5 ? 1 : -1) + PolyBlep(phase, dt) - PolyBlep((phase + 0.5) % 1, dt);
    private static double PolyBlep(double t, double dt)
    {
        dt = Math.Clamp(dt, 0.000001, 0.49);
        if (t < dt) { double x = t / dt; return x + x - x * x - 1; }
        if (t > 1 - dt) { double x = (t - 1) / dt; return x * x + x + x + 1; }
        return 0;
    }
    private sealed class Lowpass
    {
        private double z1, z2;
        public double Apply(double x, double hz, double q, int rate)
        {
            double w = 2 * Math.PI * hz / rate, cos = Math.Cos(w), alpha = Math.Sin(w) / (2 * q), a0 = 1 + alpha;
            double b0 = (1 - cos) / (2 * a0), b1 = (1 - cos) / a0, a1 = -2 * cos / a0, a2 = (1 - alpha) / a0;
            double y = b0 * x + z1; z1 = b1 * x - a1 * y + z2; z2 = b0 * x - a2 * y; return y;
        }
    }
}
