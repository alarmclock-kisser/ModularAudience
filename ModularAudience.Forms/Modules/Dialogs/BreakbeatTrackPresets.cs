namespace ModularAudience.Forms.Modules.Dialogs;

internal sealed record BreakbeatTrackPreset(string Name, string Description, string Pattern, string Answer,
    int Density, int Amen, int Bars, int MinLength, int MaxLength, int PitchChance, float PitchRange,
    bool Stretch, bool Varispeed, bool Trim, int Motif, int Repeat, float VolumeMin, float VolumeMax)
{
    public override string ToString() => Name;
    public BreakbeatGenerateOptions Defaults(BreakbeatGenerateOptions source, int stepsPerBar = 16)
    {
        var result = source.Clone();
        // Preset note lengths are authored against a standard 16 steps/bar grid.
        // Scale them with the active resolution so other resolutions keep the same
        // musical note lengths instead of becoming too short/long.
        int scaleNumerator = Math.Max(1, stepsPerBar);
        double scale = scaleNumerator / 16.0;
        int minLength = Math.Max(1, (int)Math.Round(MinLength * scale));
        int maxLength = Math.Max(minLength, (int)Math.Round(MaxLength * scale));
        minLength = Math.Clamp(minLength, 1, 16);
        maxLength = Math.Clamp(maxLength, 1, 16);
        result.Preset = Name; result.DensityPercent = Density; result.AmenFactorPercent = Amen;
        result.PhraseBars = Bars; result.MinNoteLengthSteps = minLength; result.MaxNoteLengthSteps = maxLength;
        result.PitchChancePercent = PitchChance; result.PitchRangeSemitones = PitchRange;
        result.AllowPitchedNotes = PitchChance > 0; result.AllowTimeStretch = Stretch;
        result.AllowVarispeed = Varispeed; result.AllowTruncated = Trim;
        result.MotifLengthSteps = Motif; result.MotifRepeatPercent = Repeat;
        result.VolumeMinPercent = VolumeMin; result.VolumeMaxPercent = VolumeMax;
        return result;
    }
}

internal static class BreakbeatTrackPresets
{
    // X = accent, o = ghost, . = space; templates are musical sixteenths, mapped to the editor grid.
    public static IReadOnlyList<BreakbeatTrackPreset> All { get; } =
    [
        new("AmenSnareLadder", "Backbeats with ghost pickups and a rising minor-scale snare ladder at the phrase end.",
            "....X..o.o..X.oo", "....X.oo.o..Xooo", 72, 30, 4, 1, 2, 75, 7, false, false, true, 8, 65, 48, 110),
        new("JungleRoller", "Driving syncopated doubles; the final bar breaks into a rapid descending roll.",
            "X..o..XX..o.X.oo", "X.oX..oX.o.XXXXX", 78, 40, 4, 1, 2, 35, 3, true, false, true, 8, 55, 62, 115),
        new("FunkGhostPocket", "Sparse backbeat pocket, soft anticipations and a displaced second-bar answer.",
            "X..oX.o...o.X.o.", "..o.X..o.X..X.o.", 65, 25, 2, 1, 3, 10, 1, false, false, true, 8, 80, 35, 105),
        new("DnBTwoStep", "Firm two-step anchors, a delayed second hit and a short end-of-phrase pickup.",
            "X...X.....X.X...", "X...X..o..X.X.oo", 85, 10, 4, 1, 2, 0, 0, false, false, true, 16, 90, 85, 110),
        new("HalfTimeTitan", "Heavy half-time spaces, a central slam and low-pitched long replies.",
            "X.......X.....o.", "X.....o.X...o...", 85, 5, 2, 2, 8, 65, 7, true, true, false, 16, 80, 85, 120),
        new("RaggaOffbeat", "Offbeat skanks in a call-and-response phrase, with alternating high/low pitch.",
            "..X...X...X...X.", "..X.o.X...X..oX.", 80, 80, 2, 1, 2, 60, 5, false, true, true, 4, 85, 65, 108),
        new("EuclidFive", "Five evenly distributed attacks against sixteen steps; the answer rotates the rhythm.",
            "X..X..X..X..X...", ".X..X..X..X..X..", 90, 15, 4, 1, 2, 20, 2, false, false, true, 16, 100, 72, 105),
        new("TresilloChop", "A clipped 3-3-2 pulse repeated twice per bar with a final answer flourish.",
            "X..X..X.X..X..X.", "X..X..X.X..X.oXX", 90, 20, 2, 1, 2, 15, 2, false, false, true, 8, 95, 78, 110),
        new("FootworkSwitch", "Angular double hits, holes and alternating phrase halves with octave-like pitch jumps.",
            "XX....X..XX...X.", "..XX.X....XX.X..", 80, 35, 4, 1, 2, 80, 12, false, true, true, 8, 50, 60, 112),
        new("RatchetPressure", "Quarter-note anchors tighten into increasingly short end-of-phrase retriggers.",
            "X...X...X...X.o.", "X...X.o.X.XXXXXX", 90, 20, 4, 1, 4, 30, 4, true, false, true, 4, 45, 50, 115),
        new("TapeDive", "Long varispeed hits descend in pitch and open into a slow tape-stop-like tail.",
            "X.......X...o...", "X.....X.....X...", 95, 5, 4, 2, 8, 100, 12, false, true, false, 16, 60, 80, 110),
        new("ElasticOrbit", "Pitch-preserving stretched hits alternate short and long durations around a syncopated orbit.",
            "X..X....X..X....", "..X...X...X...X.", 90, 25, 2, 2, 6, 0, 0, true, false, false, 8, 70, 65, 105),
        new("GlitchShards", "Isolated hard-cut shards, clustered microbursts and jagged quarter-tone pitch changes.",
            "X.o..XX....o.X.X", ".XX...o.XX...XXX", 65, 55, 4, 1, 1, 90, 12, true, true, true, 3, 25, 25, 120),
        new("DubEchoSteps", "A strong initial hit followed by softer, progressively lower rhythmic echoes.",
            "X..o..o..o..o...", "X..o..o.....o..o", 95, 20, 2, 1, 3, 75, 5, false, false, true, 16, 95, 25, 110),
        new("TribalTomSpiral", "A rotating three-step tom cycle with a pentatonic up/down pitch contour.",
            "X..X..X..X..X..o", "..X..X..X..X..XX", 85, 20, 4, 1, 3, 90, 7, false, true, true, 6, 85, 65, 115),
        new("NeuroMachine", "Mechanical alternating accents and clipped bursts; the answer reverses the pitch direction.",
            "X.X..X.XX.X..X..", "X.XX.X..X..X.XXX", 85, 30, 4, 1, 2, 65, 6, true, true, true, 8, 90, 78, 115)
    ];

    public static BreakbeatTrackPreset? Find(BreakbeatGenerateOptions options) =>
        options.PresetIntensityPercent > 0 ? All.FirstOrDefault(p => p.Name == options.Preset) : null;

    internal sealed record Hit(int Column, int Length, float Pitch, float Volume, BreakbeatGenerateOptions Kinds);

    public static IEnumerable<Hit> Generate(BreakbeatTrackPreset preset, BreakbeatGenerateOptions options,
        int first, int last, int resolution, Random random)
    {
        double intensity = options.PresetIntensityPercent / 100d;
        int phraseSteps = options.PhraseBars * resolution;
        var motifNoise = new Dictionary<int, double>();
        for (int column = first; column <= last; column++)
        {
            // Absolute bar phase keeps separate loop selections and differently sized rectangles musically aligned.
            int phrase = column % phraseSteps;
            int bar = phrase / resolution;
            int step = column % resolution;
            int sixteenth = step * 16 / resolution;
            bool answer = bar == options.PhraseBars - 1;
            string mask = answer ? preset.Answer : preset.Pattern;
            // At fine resolutions, reserve sub-sixteenth cells for rolls rather than duplicating every anchor.
            bool substep = step != (int)Math.Ceiling(sixteenth * resolution / 16d);
            bool burst = (preset.Name is "JungleRoller" or "RatchetPressure" or "GlitchShards")
                && answer && sixteenth >= 12;
            char accent = substep && !burst ? '.' : mask[sixteenth];
            double weight = accent == 'X' ? 1 : accent == 'o' ? 0.48 : 0.015;
            double amen = options.AmenFactorPercent / 100d;
            double legacy = options.DensityPercent / 100d * (column % 2 == 0 ? 1 - amen : 1);
            double structured = Math.Min(1, options.DensityPercent / 100d * weight * 1.8)
                * (accent == 'X' || column % 2 != 0 ? 1 : 1 - amen);
            int motifIndex = column % options.MotifLengthSteps;
            double noise;
            if (motifNoise.TryGetValue(motifIndex, out double previous) && random.Next(100) < options.MotifRepeatPercent)
                noise = previous;
            else { noise = random.NextDouble(); motifNoise[motifIndex] = noise; }
            if (noise >= legacy * (1 - intensity) + structured * intensity) continue;

            int min = options.MinNoteLengthSteps, max = options.MaxNoteLengthSteps;
            int randomLength = random.Next(min, max + 1);
            double progress = phrase / (double)Math.Max(1, phraseSteps - 1);
            double lengthShape = preset.Name switch
            {
                "TapeDive" => progress,
                "HalfTimeTitan" => accent == 'X' ? 1 : 0,
                "ElasticOrbit" => sixteenth % 2 == 0 ? 1 : 0.25,
                "RatchetPressure" => 1 - progress,
                _ => accent == 'X' ? 0.35 : 0
            };
            int length = Math.Clamp((int)Math.Round(randomLength * (1 - intensity) + (min + (max - min) * lengthShape) * intensity), min, max);
            float pitch = 0;
            if (options.AllowPitchedNotes && random.Next(100) < options.PitchChancePercent)
            {
                double contour = preset.Name switch
                {
                    "AmenSnareLadder" => new[] { -1d, -0.7, -0.4, 0, 0.3, 0.7, 1, 0.7 }[(phrase * 8 / phraseSteps) % 8],
                    "JungleRoller" => 1 - 2 * step / (double)Math.Max(1, resolution - 1),
                    "HalfTimeTitan" => -0.5 - progress * 0.5,
                    "RaggaOffbeat" => sixteenth % 8 < 4 ? 1 : -0.6,
                    "FootworkSwitch" => sixteenth % 4 < 2 ? -1 : 1,
                    "TapeDive" => -progress,
                    "DubEchoSteps" => -step / (double)resolution,
                    "TribalTomSpiral" => new[] { -1d, -0.7, -0.3, 0, 0.4, 1, 0.4, 0 }[sixteenth / 2],
                    "NeuroMachine" => (sixteenth % 2 == 0 ? 0.7 : -1) * (answer ? -1 : 1),
                    _ => random.NextDouble() * 2 - 1
                };
                double shift = (random.NextDouble() * 2 - 1) * (1 - intensity) + contour * intensity;
                float range = options.PitchRangeSemitones;
                pitch = Math.Clamp((float)Math.Round(shift * range * 4) / 4, -range, range);
            }
            double dynamics = preset.Name == "DubEchoSteps" ? 1 - step / (double)resolution
                : accent == 'X' ? 0.85 + random.NextDouble() * 0.15 : random.NextDouble() * 0.45;
            float volume = options.VolumeMinPercent + (options.VolumeMaxPercent - options.VolumeMinPercent)
                * (float)(random.NextDouble() * (1 - intensity) + dynamics * intensity);

            var kinds = options.Clone();
            var allowed = new List<int>();
            if (options.AllowTruncated) allowed.Add(0);
            if (options.AllowTimeStretch) allowed.Add(1);
            if (options.AllowVarispeed) allowed.Add(2);
            if (allowed.Count > 0)
            {
                int preferred = preset.Name switch
                {
                    "TapeDive" or "HalfTimeTitan" or "FootworkSwitch" or "TribalTomSpiral" => 2,
                    "ElasticOrbit" => 1,
                    "RatchetPressure" => answer ? 0 : 1,
                    "GlitchShards" => sixteenth % 3,
                    "NeuroMachine" => sixteenth % 2 == 0 ? 1 : 2,
                    _ => 0
                };
                int kind = allowed.Contains(preferred) ? preferred : allowed[random.Next(allowed.Count)];
                kinds.AllowTruncated = kind == 0; kinds.AllowTimeStretch = kind == 1; kinds.AllowVarispeed = kind == 2;
            }
            yield return new(column, Math.Min(length, last - column + 1), pitch, volume, kinds);
        }
    }
}
