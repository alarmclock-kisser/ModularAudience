using FuzzySharp;
using ModularAudience.Audio;

public enum DrumsetElement
{
    Kick,
    Snare,
    SnareRattle,
    HiHatClosed,
    HiHatOpen,
    Ride,
    TomHigh,
    TomMid,
    TomLow,
    FloorTom,
    Clap,
    Rim,
    CrashShort,
    CrashLong,
    Cowbell,
    Shaker,
    ThinkBreak // schneller, "jungle / breakcore" Stil - speziell für schnelle, komplexe breaks
}

namespace ModularAudience.Generators
{
    public static class BreakbeatGenerator
    {
        // Default resolution: 16 steps per bar (16th notes). Das entspricht dem, was man in Drum-Machines oft sieht.
        private const int DefaultResolution = 16;

        // Thread-safe random
        private static readonly Random GlobalRandom = new();
        private static readonly Lock RandLock = new();
        private static int NextSeed()
        {
            lock (RandLock)
            {
                return GlobalRandom.Next();
            }
        }

        // Vorlagen (Templates) für typische Patterns bei 16er-Resolution. Jedes Entry ist ein Array mit Indices (0..15) der Hits in einer Bar.
        private static readonly Dictionary<DrumsetElement, List<int[]>> Templates = new()
    {
        { DrumsetElement.HiHatClosed, new List<int[]> {
                new[]{0,2,4,6,8,10,12,14},          // durchgehende 8tel/16tel-Feeling (1,3,5,7,...)
                new[]{0,4,8,12},                    // einfache Viertel (als Variation)
                new[]{0,1,2,4,5,6,8,10,12,14}       // dichter Groove
            }
        },
        { DrumsetElement.HiHatOpen, new List<int[]> {
                new[]{6,14},                        // Off-beat Akzente
                new[]{3,11}
            }
        },
        { DrumsetElement.Snare, new List<int[]> {
                new[]{4,12},                        // klassische Backbeat (2 und 4 in 16er)
                new[]{4,10,12},                     // Break-Variante
                new[]{3,7,11,15}                    // amen-like rolls
            }
        },
        { DrumsetElement.SnareRattle, new List<int[]> {
                new[]{4,12},
                new[]{4,6,10,12}
            }
        },
        { DrumsetElement.Kick, new List<int[]> {
                new[]{0,8},                         // einfacher Kick auf 1 und 3
                new[]{0,7,10,12},                   // etwas gebrochener Groove
                new[]{0,4,10}                       // Break-Drive
            }
        },
        { DrumsetElement.CrashShort, new List<int[]> {
                new[]{0},                           // meist am Taktanfang
                new[]{0,8}
            }
        },
        { DrumsetElement.CrashLong, new List<int[]> {
                new[]{0}
            }
        },
        { DrumsetElement.Ride, new List<int[]> {
                new[]{0,2,4,6,8,10,12,14}
            }
        },
        { DrumsetElement.TomHigh, new List<int[]> {
                new[]{2,10},
                new[]{6,14}
            }
        },
        { DrumsetElement.TomMid, new List<int[]> {
                new[]{4,12}
            }
        },
        { DrumsetElement.TomLow, new List<int[]> {
                new[]{8}
            }
        },
        { DrumsetElement.FloorTom, new List<int[]> {
                new[]{8,12}
            }
        },
        { DrumsetElement.Clap, new List<int[]> {
                new[]{4,12}
            }
        },
        { DrumsetElement.Rim, new List<int[]> {
                new[]{4,12}
            }
        },
        { DrumsetElement.Cowbell, new List<int[]> {
                new[]{2,6,10,14}
            }
        },
        { DrumsetElement.Shaker, new List<int[]> {
                new[]{0,2,4,6,8,10,12,14}
            }
        },
        // ThinkBreak: schnelle, dichte Pattern für Jungle / Breakcore / fast amen-derivate
        { DrumsetElement.ThinkBreak, new List<int[]> {
                new[]{0,3,5,6,8,10,11,13,14,15},    // dichtes, schnell gebrochenes Muster
                new[]{0,2,3,5,6,9,11,13,14,15},
                new[]{0,1,2,4,6,7,9,10,12,14,15}
            }
        }
    };

        // Public API wie gewünscht. Diese Signatur wurde beibehalten; intern existiert eine mächtigere Overload mit mehr Parametern.
        public static Task<List<bool[]>> GenerateBreakPatternAsync(IEnumerable<DrumsetElement> drumset, int bars = 2, float density = 0.33f)
        {
            // Default-Extras: resolution=16, swing=0, complexityFactor=1.0, seed random
            return GenerateBreakPatternAsync(drumset, bars, density, DefaultResolution, swing: 0.0f, complexity: 1.0f, seed: NextSeed());
        }

        // Erweiterte Overload: erlaubt feinere Steuerung (optional nutzbar).
        public static async Task<List<bool[]>> GenerateBreakPatternAsync(IEnumerable<DrumsetElement> drumset, int bars, float density, int resolution = DefaultResolution, float swing = 0.0f, float complexity = 1.0f, int? seed = null)
        {
            if (bars <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bars));
            }

            if (resolution <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(resolution));
            }

            density = Math.Clamp(density, 0f, 1f);
            swing = Math.Clamp(swing, 0f, 0.5f); // 0..0.5 reasonable
            complexity = Math.Clamp(complexity, 0.1f, 4.0f);

            int totalSteps = bars * resolution;
            var elements = drumset.ToList();
            var results = new List<bool[]>(elements.Count);

            // Per-element generation in (soft) parallel
            var tasks = elements.Select(async (elem, idx) =>
            {
                var rnd = seed.HasValue ? new Random((seed.Value ^ idx) + (int) elem) : new Random(NextSeed() ^ ((int) elem + idx));
                var line = GenerateLineForElement(elem, totalSteps, resolution, density, swing, complexity, rnd);
                await Task.Yield(); // make it actually async-friendly
                return (idx, elem, line);
            });

            var computed = await Task.WhenAll(tasks);

            // Return lines in the same order as input (handles duplicates correctly)
            var ordered = computed.OrderBy(x => x.idx).ToArray();
            for (int i = 0; i < ordered.Length; i++)
            {
                results.Add(ordered[i].line);
            }

            computed = await Task.WhenAll(tasks);

            // Return lines in same order as input
            foreach (var elem in elements)
            {
                var found = computed.First(f => f.elem == elem);
                results.Add(found.line);
            }

            return results;
        }


        // Methode die Sample Namen zu DrumsetElementen mappt mit Fuzzy-Matching, fallback zu Snare
        public static DrumsetElement[] MatchSampleNamesToDrumsetElements(string[] sampleNames)
        {
            string[] drumsetNames = Enum.GetNames<DrumsetElement>();
            var results = new DrumsetElement[sampleNames.Length];
            for (int i = 0; i < sampleNames.Length; i++)
            {
                int bestScore = int.MinValue;
                int bestIdx = -1;
                for (int j = 0; j < drumsetNames.Length; j++)
                {
                    int score = Fuzz.WeightedRatio(sampleNames[i], drumsetNames[j]);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestIdx = j;
                    }
                }

                // Fallback zu Snare, falls kein guter Match gefunden wurde
                results[i] = bestIdx >= 0 ? (DrumsetElement) bestIdx : DrumsetElement.Snare;
            }

            return results;
        }


        // Generiert eine einzelne Spur für ein Element
        private static bool[] GenerateLineForElement(DrumsetElement elem, int totalSteps, int resolution, float density, float swing, float complexity, Random rnd)
        {
            var line = new bool[totalSteps];

            // 1) Versuche eine Template-basierte Grundlinie zu wählen (wenn vorhanden)
            if (Templates.TryGetValue(elem, out var templList) && templList.Count > 0)
            {
                // Wahrscheinlichkeit, ein Template zu verwenden, ist abhängig vom Typ
                float templateUseChance = TemplateUseChanceFor(elem);
                if (rnd.NextDouble() < templateUseChance)
                {
                    var chosenTemplate = templList[rnd.Next(templList.Count)];
                    // Template definiert Hits relativ zu einer Bar bei 16er-Resolution
                    for (int b = 0; b < totalSteps / resolution; b++)
                    {
                        foreach (var idx16 in chosenTemplate)
                        {
                            int scaled = ScaleIndexToResolution(idx16, 16, resolution);
                            int pos = b * resolution + (scaled % resolution);
                            if (pos >= 0 && pos < totalSteps)
                            {
                                line[pos] = true;
                            }
                        }
                    }
                }
            }

            // 2) Ergänze per Zufall entsprechend 'density' und 'complexity'
            // Ziel: finalHits ~ density * totalSteps * typeFactor
            float typeFactor = TypeDensityFactor(elem);
            int desiredHits = (int) Math.Round(density * totalSteps * typeFactor * complexity);

            // Count current hits
            int currentHits = line.Count(x => x);

            // Add or remove hits to approach desiredHits
            // When adding, prefer positions that are near existing hits (human feel) or on-beats.
            int attempts = 0;
            while (currentHits < desiredHits && attempts < totalSteps * 4)
            {
                attempts++;
                int pos = WeightedPositionPick(elem, totalSteps, resolution, rnd);
                if (!line[pos])
                {
                    // small probability to push multiple hits (rolls) for snare/toms
                    if (ShouldAddRoll(elem, rnd))
                    {
                        AddRoll(line, pos, rnd);
                        currentHits = line.Count(x => x);
                    }
                    else
                    {
                        line[pos] = true;
                        currentHits++;
                    }
                }
            }

            // Optionally remove random hits if there are too many
            attempts = 0;
            while (currentHits > desiredHits && attempts < totalSteps * 4)
            {
                attempts++;
                int pos = rnd.Next(totalSteps);
                if (line[pos] && rnd.NextDouble() < 0.6)
                {
                    line[pos] = false;
                    currentHits--;
                }
            }

            // 3) Apply small humanization: swing and small random shifts for some elements
            if (swing > 0 && (elem == DrumsetElement.HiHatClosed || elem == DrumsetElement.Ride))
            {
                ApplySwing(line, resolution, swing);
            }

            if (elem == DrumsetElement.Kick || elem == DrumsetElement.Snare)
            {
                // sometimes create ghost-notes: lower-probability neighbor hits
                AddGhostNotes(line, rnd, probability: 0.05f * complexity);
            }

            return line;
        }

        // Helpers
        private static int ScaleIndexToResolution(int idxFrom16, int fromResolution, int toResolution)
        {
            if (fromResolution == toResolution)
            {
                return idxFrom16;
            }

            return (int) Math.Round(idxFrom16 * (toResolution / (double) fromResolution));
        }

        private static float TemplateUseChanceFor(DrumsetElement elem)
        {
            return elem switch
            {
                DrumsetElement.HiHatClosed => 0.95f,
                DrumsetElement.HiHatOpen => 0.6f,
                DrumsetElement.Ride => 0.9f,
                DrumsetElement.Snare => 0.7f,
                DrumsetElement.Kick => 0.75f,
                _ => 0.5f
            };
        }

        private static float TypeDensityFactor(DrumsetElement elem)
        {
            return elem switch
            {
                DrumsetElement.HiHatClosed => 1.2f,
                DrumsetElement.Shaker => 1.1f,
                DrumsetElement.Ride => 1.0f,
                DrumsetElement.Kick => 0.9f,
                DrumsetElement.Snare => 0.6f,
                DrumsetElement.SnareRattle => 0.5f,
                DrumsetElement.CrashShort => 0.15f,
                DrumsetElement.CrashLong => 0.08f,
                DrumsetElement.Clap => 0.5f,
                _ => 0.7f
            };
        }

        private static int WeightedPositionPick(DrumsetElement elem, int totalSteps, int resolution, Random rnd)
        {
            // Prefer on-beats and positions near existing beats for natural feel.
            // Build a simple weight array
            var weights = new double[totalSteps];
            for (int i = 0; i < totalSteps; i++)
            {
                // base weight: on-beats (quarter notes) are preferred
                int stepInBar = i % resolution;
                double w = 1.0;
                if (stepInBar % (resolution / 4) == 0)
                {
                    w += 3.0; // quarter-beats
                }

                if (stepInBar % (resolution / 8) == 0)
                {
                    w += 1.5; // eighths
                }

                // off-beat accents get a small bump depending on element
                if (elem == DrumsetElement.HiHatClosed && (stepInBar % 2 == 0))
                {
                    w += 1.0;
                }

                if (elem == DrumsetElement.Kick && (stepInBar == 0 || stepInBar == resolution / 2))
                {
                    w += 2.0;
                }

                // random jitter
                w *= 1.0 + (rnd.NextDouble() - 0.5) * 0.4;
                weights[i] = Math.Max(0.01, w);
            }

            // roulette select
            double sum = weights.Sum();
            double pick = rnd.NextDouble() * sum;
            double acc = 0;
            for (int i = 0; i < totalSteps; i++)
            {
                acc += weights[i];
                if (pick <= acc)
                {
                    return i;
                }
            }
            return rnd.Next(totalSteps);
        }

        private static bool ShouldAddRoll(DrumsetElement elem, Random rnd)
        {
            if (elem == DrumsetElement.Snare || elem == DrumsetElement.TomHigh || elem == DrumsetElement.TomMid || elem == DrumsetElement.TomLow)
            {
                return rnd.NextDouble() < 0.08; // gelegentliche rolls
            }
            return false;
        }

        private static void AddRoll(bool[] line, int pos, Random rnd)
        {
            int total = line.Length;
            line[pos] = true;
            int rollLen = rnd.Next(1, 4); // kurze Rolls
            for (int i = 1; i <= rollLen; i++)
            {
                int p = pos + i;
                if (p < total && rnd.NextDouble() < 0.75)
                {
                    line[p] = true;
                }
            }
        }

        private static void ApplySwing(bool[] line, int resolution, float swing)
        {
            // Simple swing: verschiebe jede zweite 16tel (ungerade 8tel) leicht nach vorne/achter
            // Implementation: swap hits between neighbor positions with probability proportional to swing
            int total = line.Length;
            for (int i = 0; i < total - 1; i++)
            {
                // operate on subdivisions: for 16er resolution, swing between positions 1 and 2, 3 and 4, ...
                if (((i % resolution) % 2) == 1)
                {
                    if (line[i] && !line[i + 1] && RandomChance(swing))
                    {
                        // move slightly forward
                        line[i] = false;
                        line[i + 1] = true;
                    }
                }
            }
        }

        private static void AddGhostNotes(bool[] line, Random rnd, float probability)
        {
            int total = line.Length;
            for (int i = 0; i < total; i++)
            {
                if (!line[i] && rnd.NextDouble() < probability)
                {
                    // add a faint ghost nearer to an existing hit
                    bool nearby = (i > 0 && line[i - 1]) || (i < total - 1 && line[i + 1]);
                    if (nearby && rnd.NextDouble() < 0.7)
                    {
                        line[i] = true;
                    }
                }
            }
        }

        private static bool RandomChance(double p)
        {
            lock (RandLock)
            {
                return GlobalRandom.NextDouble() < p;
            }
        }

        public static async Task<AudioObj> RenderBreakbeatAsync(List<bool[]> breakbeat, IEnumerable<AudioObj> samples, float bpm, int resolution, float swing)
        {
            // Annahmen:
            // - breakbeat.Count == samples.Count() (jede Spur ein Pattern)
            // - Jedes bool[] ist ein Pattern für ein Sample (z.B. Kick, Snare, ...)
            // - Alle Patterns haben die gleiche Länge (steps)
            // - Es wird EIN AudioObj erzeugt, das alle Spuren summiert (klassischer Drumloop)
            // - Die Samples werden als Loop (Pattern-Länge) ausgegeben

            if (breakbeat == null || samples == null || breakbeat.Count == 0 || !samples.Any())
            {
                return null!;
            }

            int numTracks = Math.Min(breakbeat.Count, samples.Count());
            int steps = breakbeat[0].Length;
            int sampleRate = 44100; // Standard
            int channels = 2; // Stereo-Default

            // Zeit pro Step (in Sekunden)
            float secondsPerStep = 60f / bpm * 4f / resolution; // 4/4-Takt

            // Gesamtlänge in Samples
            int totalSamples = (int) Math.Ceiling(secondsPerStep * steps * sampleRate);

            float[] mixBuffer = new float[totalSamples * channels];

            var sampleList = samples.ToList();

            for (int trackIdx = 0; trackIdx < numTracks; trackIdx++)
            {
                var pattern = breakbeat[trackIdx];
                var audio = sampleList[trackIdx];
                if (audio.Data == null || audio.Data.Length == 0)
                {
                    continue;
                }

                int audioChannels = audio.Channels > 0 ? audio.Channels : 1;
                int audioSampleRate = audio.SampleRate > 0 ? audio.SampleRate : sampleRate;
                float[] audioData = audio.Data;
                int audioLen = audioData.Length / audioChannels;

                // Für echtes Resampling: hier einbauen (aktuell: nur SampleRate-Check)
                // Downmix/Upmix Channels (Mono->Stereo, Stereo->Mono)
                for (int step = 0; step < steps; step++)
                {
                    if (!pattern[step])
                    {
                        continue;
                    }

                    // Swing: verschiebt jede zweite 16tel nach hinten (nur bei swing > 0)
                    float swingOffset = 0f;
                    if (swing > 0 && (step % 2 == 1))
                    {
                        swingOffset = secondsPerStep * swing;
                    }

                    int stepStart = (int) ((step * secondsPerStep + swingOffset) * sampleRate);

                    for (int n = 0; n < audioLen; n++)
                    {
                        int mixPos = (stepStart + n) * channels;
                        int srcPos = n * audioChannels;
                        if (mixPos + channels > mixBuffer.Length)
                        {
                            break;
                        }

                        for (int c = 0; c < channels; c++)
                        {
                            float sample = audioData[srcPos + (c % audioChannels)];
                            // Lautstärke pro Sample (falls gesetzt), sonst 1.0f
                            float vol = audio.Volume > 0f ? audio.Volume : 1.0f;
                            mixBuffer[mixPos + c] += sample * vol;
                        }
                    }
                }
            }

            // Clipping verhindern
            for (int i = 0; i < mixBuffer.Length; i++)
            {
                if (mixBuffer[i] > 1f)
                {
                    mixBuffer[i] = 1f;
                }

                if (mixBuffer[i] < -1f)
                {
                    mixBuffer[i] = -1f;
                }
            }

            // AudioObj erzeugen
            var result = new AudioObj
            {
                Name = "Breakbeat_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"),
                Data = mixBuffer,
                SampleRate = sampleRate,
                Channels = channels,
                Duration = TimeSpan.FromSeconds(secondsPerStep * steps),
                Length = mixBuffer.Length,
                BitDepth = 32,
                Bpm = bpm
            };

            return await Task.FromResult(result);
        }
    }
}

