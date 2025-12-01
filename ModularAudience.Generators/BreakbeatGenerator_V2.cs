using FuzzySharp;
using ModularAudience.Audio;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ModularAudience.Generators
{
    public static partial class BreakbeatGenerator_V2
    {
        const int DefaultResolution = 16;

        static readonly Random GlobalRandom = new();
        static readonly object RandLock = new();
        static int NextSeed()
        {
            lock (RandLock)
            {
                return GlobalRandom.Next();
            }
        }

        static readonly Dictionary<DrumsetElement, List<int[]>> Templates = new()
        {
            {
                DrumsetElement.HiHatClosed,
                new List<int[]>
                {
                    new[] { 0,2,4,6,8,10,12,14 },
                    new[] { 0,4,8,12 },
                    new[] { 1,3,5,7,9,11,13,15 },
                    new[] { 0,1,2,4,6,8,10,12,14 },
                    new[] { 0,2,3,6,8,10,11,14 }
                }
            },
            {
                DrumsetElement.HiHatOpen,
                new List<int[]>
                {
                    new[] { 6,14 },
                    new[] { 3,11 },
                    new[] { 2,10 },
                    new[] { 4,12 }
                }
            },
            {
                DrumsetElement.Snare,
                new List<int[]>
                {
                    new[] { 4,12 },
                    new[] { 4,10,12 },
                    new[] { 3,7,11,15 },
                    new[] { 4,11,13 },
                    new[] { 2,6,10,14 }
                }
            },
            {
                DrumsetElement.SnareRattle,
                new List<int[]>
                {
                    new[] { 4,12 },
                    new[] { 4,6,10,12 },
                    new[] { 3,5,7,11,13,15 }
                }
            },
            {
                DrumsetElement.Kick,
                new List<int[]>
                {
                    new[] { 0,8 },
                    new[] { 0,7,10,12 },
                    new[] { 0,4,10 },
                    new[] { 0,3,8,11 },
                    new[] { 0,5,8,13 }
                }
            },
            {
                DrumsetElement.CrashShort,
                new List<int[]>
                {
                    new[] { 0 },
                    new[] { 0,8 },
                    new[] { 0,4 },
                }
            },
            {
                DrumsetElement.CrashLong,
                new List<int[]>
                {
                    new[] { 0 }
                }
            },
            {
                DrumsetElement.Ride,
                new List<int[]>
                {
                    new[] { 0,2,4,6,8,10,12,14 },
                    new[] { 0,4,8,12 }
                }
            },
            {
                DrumsetElement.TomHigh,
                new List<int[]>
                {
                    new[] { 2,10 },
                    new[] { 6,14 },
                    new[] { 3,7,11,15 }
                }
            },
            {
                DrumsetElement.TomMid,
                new List<int[]>
                {
                    new[] { 4,12 },
                    new[] { 6,10 }
                }
            },
            {
                DrumsetElement.TomLow,
                new List<int[]>
                {
                    new[] { 8 },
                    new[] { 6,14 }
                }
            },
            {
                DrumsetElement.FloorTom,
                new List<int[]>
                {
                    new[] { 8,12 },
                    new[] { 4,8,12 }
                }
            },
            {
                DrumsetElement.Clap,
                new List<int[]>
                {
                    new[] { 4,12 },
                    new[] { 4,11 }
                }
            },
            {
                DrumsetElement.Rim,
                new List<int[]>
                {
                    new[] { 4,12 },
                    new[] { 2,6,10,14 }
                }
            },
            {
                DrumsetElement.Cowbell,
                new List<int[]>
                {
                    new[] { 2,6,10,14 },
                    new[] { 1,5,9,13 }
                }
            },
            {
                DrumsetElement.Shaker,
                new List<int[]>
                {
                    new[] { 0,2,4,6,8,10,12,14 },
                    new[] { 1,3,5,7,9,11,13,15 }
                }
            },
            {
                DrumsetElement.ThinkBreak,
                new List<int[]>
                {
                    new[] { 0,3,5,6,8,10,11,13,14,15 },
                    new[] { 0,2,3,5,6,9,11,13,14,15 },
                    new[] { 0,1,2,4,6,7,9,10,12,14,15 },
                    new[] { 0,2,4,5,7,8,10,11,13,15 }
                }
            }
        };

        public static Task<List<bool[]>> GenerateBreakPatternAsync(IEnumerable<DrumsetElement> drumset, int bars = 2, float density = 0.33f, string preset = " - None - ")
        {
            return GenerateBreakPatternAsync(drumset, bars, density, DefaultResolution, 0.0f, 1.0f, false, null, preset);
        }

        public static async Task<List<bool[]>> GenerateBreakPatternAsync(IEnumerable<DrumsetElement> drumset, int bars, float density, int resolution = DefaultResolution, float swing = 0.0f, float complexity = 1.0f, bool interleaved = false, int? seed = null, string preset = " - None - ")
        {
            if (bars <= 0) throw new ArgumentOutOfRangeException(nameof(bars));
            if (resolution <= 0) throw new ArgumentOutOfRangeException(nameof(resolution));

            density = Math.Clamp(density, 0f, 1f);
            swing = Math.Clamp(swing, 0f, 0.5f);
            complexity = Math.Clamp(complexity, 0.1f, 4.0f);

            var presetNorm = (preset ?? string.Empty).Trim();
            if (!string.Equals(presetNorm, "- None -", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(presetNorm))
            {
                string key = presetNorm.Replace(" ", string.Empty).Replace("-", string.Empty);
                var method = typeof(BreakbeatGenerator).GetMethod($"Preset_{key}", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (method != null)
                {
                    LogCollection.Log("Using BreakbeatGenerator preset '" + method.Name + "'.");
                    var task = (Task<List<bool[]>>) method.Invoke(null, [drumset, bars, density, resolution, swing, complexity, interleaved, seed])!;
                    return await task;
                }
                LogCollection.Log("Could not get Method 'Preset_" + key + "', fallback to default break generation.");
            }

            var elements = drumset.ToList();
            int totalSteps = bars * resolution;
            int baseSeed = seed ?? NextSeed();

            var lines = new bool[elements.Count][];
            var tasks = new List<Task>();

            for (int i = 0; i < elements.Count; i++)
            {
                int idx = i;
                var elem = elements[idx];
                int elemSeed = baseSeed ^ (idx * 397) ^ (int) elem;
                tasks.Add(Task.Run(() =>
                {
                    var rnd = new Random(elemSeed);
                    lines[idx] = GenerateLineForElement(elem, bars, totalSteps, resolution, density, swing, complexity, rnd);
                }));
            }

            await Task.WhenAll(tasks);

            var result = lines.ToList();

            if (interleaved)
            {
                result = MakeInterleaved(result, elements.ToArray());
            }

            return result;
        }

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
                results[i] = bestIdx >= 0 ? (DrumsetElement) bestIdx : DrumsetElement.Snare;
            }
            return results;
        }

        static bool[] GenerateLineForElement(DrumsetElement elem, int bars, int totalSteps, int resolution, float density, float swing, float complexity, Random rnd)
        {
            var line = new bool[totalSteps];
            int stepsPerBar = resolution;
            int barCount = bars;

            if (Templates.TryGetValue(elem, out var templList) && templList.Count > 0)
            {
                for (int bar = 0; bar < barCount; bar++)
                {
                    float useChance = TemplateUseChanceFor(elem);
                    if (rnd.NextDouble() > useChance)
                        continue;

                    int templateIndex = rnd.Next(templList.Count);
                    var baseTemplate = templList[templateIndex];

                    bool mirror = rnd.NextDouble() < complexity * 0.25f;
                    int rotate = rnd.Next(0, 4);

                    ApplyTemplateToBar(line, elem, bar, stepsPerBar, baseTemplate, mirror, rotate, complexity, rnd);
                }
            }

            EnsureAnchors(elem, line, barCount, stepsPerBar, density, rnd);

            float typeFactor = TypeDensityFactor(elem);
            int desiredHits = (int) Math.Round(density * totalSteps * typeFactor * complexity);
            desiredHits = Math.Max(1, desiredHits);

            int currentHits = line.Count(x => x);

            int attempts = 0;
            while (currentHits < desiredHits && attempts < totalSteps * 6)
            {
                attempts++;
                int pos = WeightedPositionPick(elem, line, totalSteps, resolution, rnd);
                if (!line[pos])
                {
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

            attempts = 0;
            while (currentHits > desiredHits && attempts < totalSteps * 6)
            {
                attempts++;
                int pos = rnd.Next(totalSteps);
                if (!line[pos])
                    continue;

                int stepInBar = pos % resolution;
                bool isAnchor = IsAnchorStep(elem, stepInBar, resolution);
                if (isAnchor && rnd.NextDouble() < 0.9)
                    continue;

                if (rnd.NextDouble() < 0.6)
                {
                    line[pos] = false;
                    currentHits--;
                }
            }

            if (swing > 0 && (elem == DrumsetElement.HiHatClosed || elem == DrumsetElement.Ride || elem == DrumsetElement.Shaker))
            {
                ApplySwing(line, resolution, swing);
            }

            if (elem == DrumsetElement.Kick || elem == DrumsetElement.Snare || elem == DrumsetElement.SnareRattle)
            {
                AddGhostNotes(line, rnd, 0.05f * complexity);
            }

            int minSpacing = MinSpacingForElement(elem, resolution);
            if (minSpacing > 0)
            {
                EnforceMinStepSpacing(line, minSpacing);
            }

            return line;
        }

        static int MinSpacingForElement(DrumsetElement elem, int resolution)
        {
            int sixteenth = Math.Max(1, resolution / 16);

            return elem switch
            {
                DrumsetElement.Kick => sixteenth * 2,
                DrumsetElement.Snare => sixteenth * 2,
                DrumsetElement.SnareRattle => sixteenth * 2,
                DrumsetElement.Clap => sixteenth * 2,
                DrumsetElement.Rim => sixteenth * 2,
                DrumsetElement.TomHigh => sixteenth * 1,
                DrumsetElement.TomMid => sixteenth * 1,
                DrumsetElement.TomLow => sixteenth * 1,
                DrumsetElement.FloorTom => sixteenth * 1,
                DrumsetElement.ThinkBreak => sixteenth * 1,
                _ => 0
            };
        }

        static void EnforceMinStepSpacing(bool[] line, int minSpacing)
        {
            if (line == null || line.Length == 0 || minSpacing <= 0)
                return;

            int last = -minSpacing - 1;
            for (int i = 0; i < line.Length; i++)
            {
                if (!line[i])
                    continue;

                if (i - last < minSpacing)
                {
                    line[i] = false;
                }
                else
                {
                    last = i;
                }
            }
        }


        static void ApplyTemplateToBar(bool[] line, DrumsetElement elem, int barIndex, int stepsPerBar, int[] baseTemplate16, bool mirror, int rotateQuarterSteps, float complexity, Random rnd)
        {
            int resolution = stepsPerBar;
            int barOffset = barIndex * stepsPerBar;

            foreach (var idx16 in baseTemplate16)
            {
                int index16 = idx16;
                if (mirror)
                    index16 = 15 - index16;

                int step = ScaleIndexToResolution(index16, 16, resolution);

                if (rotateQuarterSteps != 0)
                {
                    int group = resolution / 4;
                    step = (step + rotateQuarterSteps * group) % resolution;
                    if (step < 0) step += resolution;
                }

                double keepProb = 0.6 + 0.4 * Math.Min(1.5, complexity);
                if (elem == DrumsetElement.ThinkBreak)
                    keepProb = 0.8 + 0.2 * Math.Min(1.5, complexity);

                if (rnd.NextDouble() > keepProb)
                    continue;

                int shift = 0;
                if (rnd.NextDouble() < complexity * 0.35f)
                {
                    shift = rnd.Next(-1, 2);
                }

                int posInBar = Math.Clamp(step + shift, 0, stepsPerBar - 1);
                int pos = barOffset + posInBar;
                if (pos >= 0 && pos < line.Length)
                    line[pos] = true;

                if (elem == DrumsetElement.Snare || elem == DrumsetElement.SnareRattle || elem == DrumsetElement.TomHigh || elem == DrumsetElement.TomMid || elem == DrumsetElement.TomLow)
                {
                    if (rnd.NextDouble() < complexity * 0.25f)
                    {
                        int ghost = pos + (rnd.NextDouble() < 0.5 ? -1 : 1);
                        if (ghost >= barOffset && ghost < barOffset + stepsPerBar)
                            line[ghost] = true;
                    }
                }
            }
        }

        static void EnsureAnchors(DrumsetElement elem, bool[] line, int bars, int resolution, float density, Random rnd)
        {
            int stepsPerBar = resolution;
            for (int bar = 0; bar < bars; bar++)
            {
                int offset = bar * stepsPerBar;
                if (elem == DrumsetElement.Kick)
                {
                    int s0 = offset + 0;
                    if (s0 >= 0 && s0 < line.Length)
                        line[s0] = true;

                    if (density >= 0.2f)
                    {
                        int s2 = offset + stepsPerBar / 2;
                        if (s2 >= 0 && s2 < line.Length && rnd.NextDouble() < 0.85)
                            line[s2] = true;
                    }
                }

                if (elem == DrumsetElement.Snare || elem == DrumsetElement.SnareRattle || elem == DrumsetElement.Clap || elem == DrumsetElement.Rim)
                {
                    int sA = offset + stepsPerBar / 4;
                    int sB = offset + (3 * stepsPerBar) / 4;
                    if (sA >= 0 && sA < line.Length && rnd.NextDouble() < 0.9)
                        line[sA] = true;
                    if (sB >= 0 && sB < line.Length && rnd.NextDouble() < 0.9)
                        line[sB] = true;
                }

                if (elem == DrumsetElement.HiHatClosed || elem == DrumsetElement.Ride || elem == DrumsetElement.Shaker)
                {
                    for (int s = 0; s < stepsPerBar; s += stepsPerBar / 4)
                    {
                        int pos = offset + s;
                        if (pos >= 0 && pos < line.Length && rnd.NextDouble() < 0.9)
                            line[pos] = true;
                    }
                }
            }
        }

        static bool IsAnchorStep(DrumsetElement elem, int stepInBar, int resolution)
        {
            int q = resolution / 4;
            if (elem == DrumsetElement.Kick)
            {
                return stepInBar == 0 || stepInBar == 2 * q;
            }
            if (elem == DrumsetElement.Snare || elem == DrumsetElement.SnareRattle || elem == DrumsetElement.Clap || elem == DrumsetElement.Rim)
            {
                return stepInBar == q || stepInBar == 3 * q;
            }
            if (elem == DrumsetElement.HiHatClosed || elem == DrumsetElement.Ride || elem == DrumsetElement.Shaker)
            {
                return stepInBar % q == 0;
            }
            return false;
        }

        static List<bool[]> MakeInterleaved(List<bool[]> breakbeat, DrumsetElement[]? elements = null)
        {
            var rnd = new Random();
            int totalSteps = breakbeat[0].Length;
            var interleaved = new List<bool[]>(breakbeat.Count);
            for (int i = 0; i < breakbeat.Count; i++)
                interleaved.Add(new bool[totalSteps]);

            for (int step = 0; step < totalSteps; step++)
            {
                var candidates = new List<int>();
                for (int lineIdx = 0; lineIdx < breakbeat.Count; lineIdx++)
                {
                    if (breakbeat[lineIdx][step])
                        candidates.Add(lineIdx);
                }

                if (candidates.Count == 0)
                    continue;

                int chosenIdx;
                if (elements == null || elements.Length != breakbeat.Count)
                {
                    chosenIdx = candidates[rnd.Next(candidates.Count)];
                }
                else
                {
                    int bestPriority = int.MinValue;
                    int bestIdx = candidates[0];
                    foreach (var cidx in candidates)
                    {
                        int priority = ElementInterleavePriority(elements[cidx]);
                        if (priority > bestPriority)
                        {
                            bestPriority = priority;
                            bestIdx = cidx;
                        }
                    }
                    chosenIdx = bestIdx;
                }

                interleaved[chosenIdx][step] = true;
            }

            return interleaved;
        }

        static int ElementInterleavePriority(DrumsetElement e)
        {
            return e switch
            {
                DrumsetElement.Kick => 100,
                DrumsetElement.Snare => 90,
                DrumsetElement.SnareRattle => 85,
                DrumsetElement.HiHatClosed => 80,
                DrumsetElement.HiHatOpen => 75,
                DrumsetElement.Ride => 70,
                DrumsetElement.TomHigh => 60,
                DrumsetElement.TomMid => 50,
                DrumsetElement.TomLow => 40,
                DrumsetElement.FloorTom => 35,
                DrumsetElement.Clap => 30,
                DrumsetElement.Rim => 25,
                _ => 10
            };
        }

        static int ScaleIndexToResolution(int idxFrom16, int fromResolution, int toResolution)
        {
            if (fromResolution == toResolution)
                return idxFrom16;
            double scaled = idxFrom16 * (toResolution / (double) fromResolution);
            return (int) Math.Round(scaled);
        }

        static float TemplateUseChanceFor(DrumsetElement elem)
        {
            return elem switch
            {
                DrumsetElement.HiHatClosed => 0.95f,
                DrumsetElement.HiHatOpen => 0.7f,
                DrumsetElement.Ride => 0.9f,
                DrumsetElement.Shaker => 0.9f,
                DrumsetElement.Snare => 0.75f,
                DrumsetElement.SnareRattle => 0.7f,
                DrumsetElement.Kick => 0.8f,
                DrumsetElement.ThinkBreak => 0.95f,
                _ => 0.55f
            };
        }

        static float TypeDensityFactor(DrumsetElement elem)
        {
            return elem switch
            {
                DrumsetElement.HiHatClosed => 1.2f,
                DrumsetElement.Shaker => 1.15f,
                DrumsetElement.Ride => 1.0f,
                DrumsetElement.Kick => 0.9f,
                DrumsetElement.Snare => 0.7f,
                DrumsetElement.SnareRattle => 0.55f,
                DrumsetElement.CrashShort => 0.18f,
                DrumsetElement.CrashLong => 0.08f,
                DrumsetElement.Clap => 0.5f,
                DrumsetElement.Rim => 0.4f,
                DrumsetElement.TomHigh => 0.5f,
                DrumsetElement.TomMid => 0.45f,
                DrumsetElement.TomLow => 0.4f,
                DrumsetElement.FloorTom => 0.35f,
                DrumsetElement.Cowbell => 0.4f,
                DrumsetElement.ThinkBreak => 1.4f,
                _ => 0.7f
            };
        }

        static int WeightedPositionPick(DrumsetElement elem, bool[] line, int totalSteps, int resolution, Random rnd)
        {
            var weights = new double[totalSteps];
            for (int i = 0; i < totalSteps; i++)
            {
                if (line[i])
                {
                    weights[i] = 0.01;
                    continue;
                }

                int stepInBar = i % resolution;
                double w = 1.0;

                int q = resolution / 4;
                int e = resolution / 8;

                if (stepInBar % q == 0)
                    w += 3.0;
                if (stepInBar % e == 0)
                    w += 1.5;

                bool neighborHit = (i > 0 && line[i - 1]) || (i < totalSteps - 1 && line[i + 1]);
                if (neighborHit)
                    w += 1.2;

                if (elem == DrumsetElement.HiHatClosed || elem == DrumsetElement.Shaker || elem == DrumsetElement.Ride)
                {
                    if (stepInBar % 2 == 0)
                        w += 0.8;
                }

                if (elem == DrumsetElement.Kick)
                {
                    if (stepInBar == 0 || stepInBar == 2 * q)
                        w += 2.0;
                }

                if (elem == DrumsetElement.Snare || elem == DrumsetElement.SnareRattle || elem == DrumsetElement.Clap || elem == DrumsetElement.Rim)
                {
                    if (stepInBar == q || stepInBar == 3 * q)
                        w += 2.5;
                }

                w *= 1.0 + (rnd.NextDouble() - 0.5) * 0.4;
                weights[i] = Math.Max(0.01, w);
            }

            double sum = weights.Sum();
            double pick = rnd.NextDouble() * sum;
            double acc = 0.0;
            for (int i = 0; i < totalSteps; i++)
            {
                acc += weights[i];
                if (pick <= acc)
                    return i;
            }
            return rnd.Next(totalSteps);
        }

        static bool ShouldAddRoll(DrumsetElement elem, Random rnd)
        {
            if (elem == DrumsetElement.Snare || elem == DrumsetElement.SnareRattle || elem == DrumsetElement.TomHigh || elem == DrumsetElement.TomMid || elem == DrumsetElement.TomLow || elem == DrumsetElement.ThinkBreak)
            {
                return rnd.NextDouble() < 0.12;
            }
            return false;
        }

        static void AddRoll(bool[] line, int pos, Random rnd)
        {
            int total = line.Length;
            line[pos] = true;
            int rollLen = rnd.Next(1, 4);
            for (int i = 1; i <= rollLen; i++)
            {
                int p = pos + i;
                if (p < total && rnd.NextDouble() < 0.75)
                    line[p] = true;
            }
        }

        static void ApplySwing(bool[] line, int resolution, float swing)
        {
            int total = line.Length;
            for (int i = 0; i < total - 1; i++)
            {
                int stepInBar = i % resolution;
                if ((stepInBar % 2) == 1)
                {
                    if (line[i] && !line[i + 1] && RandomChance(swing))
                    {
                        line[i] = false;
                        line[i + 1] = true;
                    }
                }
            }
        }

        static void AddGhostNotes(bool[] line, Random rnd, float probability)
        {
            int total = line.Length;
            for (int i = 0; i < total; i++)
            {
                if (line[i])
                    continue;

                if (rnd.NextDouble() >= probability)
                    continue;

                bool nearby = (i > 0 && line[i - 1]) || (i < total - 1 && line[i + 1]);
                if (!nearby)
                    continue;

                if (rnd.NextDouble() < 0.7)
                    line[i] = true;
            }
        }

        static bool RandomChance(double p)
        {
            lock (RandLock)
            {
                return GlobalRandom.NextDouble() < p;
            }
        }

        public static async Task<AudioObj> RenderBreakbeatAsync(List<bool[]> breakbeat, IEnumerable<AudioObj> samples, float bpm, int resolution, float swing, string? patternName = null)
        {
            if (breakbeat == null || samples == null || breakbeat.Count == 0 || !samples.Any())
                return null!;

            int numTracks = Math.Min(breakbeat.Count, samples.Count());
            int steps = breakbeat[0].Length;
            int sampleRate = 44100;
            int channels = 2;

            float secondsPerStep = 60f / bpm * 4f / resolution;
            int totalSamples = (int) Math.Ceiling(secondsPerStep * steps * sampleRate);

            float[] mixBuffer = new float[totalSamples * channels];
            var sampleList = samples.ToList();

            for (int trackIdx = 0; trackIdx < numTracks; trackIdx++)
            {
                var pattern = breakbeat[trackIdx];
                var audio = sampleList[trackIdx];
                if (audio.Data == null || audio.Data.Length == 0)
                    continue;

                await audio.NormalizeAsync(0.8f);

                int audioChannels = audio.Channels > 0 ? audio.Channels : 1;
                int audioSampleRate = audio.SampleRate > 0 ? audio.SampleRate : sampleRate;
                float[] audioData = audio.Data;
                int audioLen = audioData.Length / audioChannels;

                for (int step = 0; step < steps; step++)
                {
                    if (!pattern[step])
                        continue;

                    float swingOffset = 0f;
                    if (swing > 0 && (step % 2 == 1))
                        swingOffset = secondsPerStep * swing;

                    int stepStart = (int) ((step * secondsPerStep + swingOffset) * sampleRate);

                    for (int n = 0; n < audioLen; n++)
                    {
                        int mixPos = (stepStart + n) * channels;
                        int srcPos = n * audioChannels;
                        if (mixPos + channels > mixBuffer.Length)
                            break;

                        for (int c = 0; c < channels; c++)
                        {
                            float sample = audioData[srcPos + (c % audioChannels)];
                            float vol = audio.Volume;
                            if (vol <= 0f || float.IsNaN(vol) || float.IsInfinity(vol))
                                vol = 1.0f;
                            vol = (float) Math.Clamp(vol, 0.0, 1.0);
                            mixBuffer[mixPos + c] += sample * vol;
                        }
                    }
                }
            }

            float peak = 0f;
            for (int i = 0; i < mixBuffer.Length; i++)
            {
                float v = Math.Abs(mixBuffer[i]);
                if (v > peak) peak = v;
            }

            const float targetPeak = 0.95f;
            if (peak > 0f && peak > targetPeak)
            {
                float scale = targetPeak / peak;
                for (int i = 0; i < mixBuffer.Length; i++)
                    mixBuffer[i] *= scale;
            }

            patternName ??= "Breakbeat_";
            patternName = patternName.Replace("_", "") + "_";

            var result = new AudioObj
            {
                Name = patternName + DateTime.Now.ToString("yyyyMMdd_HHmmss"),
                Data = mixBuffer,
                SampleRate = sampleRate,
                Channels = channels,
                Duration = TimeSpan.FromSeconds(secondsPerStep * steps),
                Length = mixBuffer.Length,
                BitDepth = 32,
                Bpm = bpm
            };

            result.Rename(patternName + DateTime.Now.ToString("yyyyMMdd_HHmmss"));

            return await Task.FromResult(result);
        }
    }
}
