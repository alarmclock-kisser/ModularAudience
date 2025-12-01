using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ModularAudience.Generators
{
    public static partial class BreakbeatGenerator
    {
        // Preset: Amen-style snare ladder/scale using only the provided snare tracks (preserving order)
        // Parameters match GenerateBreakPatternAsync
        public static async Task<List<bool[]>> Preset_AmenSnareScale_Old(IEnumerable<DrumsetElement> drumset, int bars, float density, int resolution = DefaultResolution, float swing = 0.0f, float complexity = 1.0f, bool interleaved = false, int? seed = null)
        {
            if (bars <= 0) throw new ArgumentOutOfRangeException(nameof(bars));
            if (resolution <= 0) throw new ArgumentOutOfRangeException(nameof(resolution));

            var elements = drumset.ToList();
            int totalSteps = bars * resolution;

            // Find indices of snares in the provided drumset (preserve order)
            var snareIndices = elements
                .Select((e, i) => new { e, i })
                .Where(x => x.e == DrumsetElement.Snare || x.e == DrumsetElement.SnareRattle)
                .Select(x => x.i)
                .ToList();

            // If no snares, fallback to generic generator
            if (snareIndices.Count == 0)
            {
                return await GenerateBreakPatternAsync(drumset, bars, density, resolution, swing, complexity, interleaved, seed);
            }

            // Initialize empty patterns for all elements
            var patterns = elements.Select(e => new bool[totalSteps]).ToList();

            var rnd = seed.HasValue ? new Random(seed.Value) : new Random(NextSeed());

            // Build a master roll timeline focused around typical snare backbeat positions
            var masterHits = new List<int>();

            // core positions inside a bar: beat 2 and 4 in 4/4 (at resolution/4 and 3*resolution/4)
            int posA = resolution / 4; // e.g. 4
            int posB = (3 * resolution) / 4; // e.g. 12

            for (int bar = 0; bar < bars; bar++)
            {
                int baseOffset = bar * resolution;

                // Primary clusters around posA and posB
                foreach (var center in new[] { posA, posB })
                {
                    int centerPos = baseOffset + center;

                    // Add a small cluster: denser near center, sparser further out
                    // Order: center-2, center-1, center, center+1, center+2
                    var offsets = new[] { -2, -1, 0, 1, 2 };
                    foreach (var off in offsets)
                    {
                        int p = centerPos + off;
                        if (p < 0 || p >= totalSteps) continue;

                        // Probability to include decreases with distance
                        double prob = 0.95 - (Math.Abs(off) * 0.25) + (rnd.NextDouble() - 0.5) * 0.15;
                        if (rnd.NextDouble() < Math.Clamp(prob, 0.05, 0.99)) masterHits.Add(p);
                    }

                    // small chance for a short roll following the cluster
                    if (rnd.NextDouble() < 0.6)
                    {
                        int rollLen = rnd.Next(2, Math.Max(2, resolution / 8));
                        for (int r = 1; r <= rollLen; r++)
                        {
                            int rp = centerPos + r;
                            if (rp >= 0 && rp < totalSteps && rnd.NextDouble() < 0.85)
                            {
                                masterHits.Add(rp);
                            }
                        }
                    }
                }

                // Occasional ghost cluster earlier in bar for variation
                if (rnd.NextDouble() < 0.4)
                {
                    int alt = baseOffset + (resolution / 2);
                    for (int off = -1; off <= 1; off++)
                    {
                        int p = alt + off;
                        if (p >= 0 && p < totalSteps && rnd.NextDouble() < 0.6) masterHits.Add(p);
                    }
                }
            }

            // Ensure uniqueness and sort
            masterHits = masterHits.Distinct().OrderBy(x => x).ToList();

            // If still empty, fall back
            if (masterHits.Count == 0)
            {
                return await GenerateBreakPatternAsync(drumset, bars, density, resolution, swing, complexity, interleaved, seed);
            }

            // Distribute master hits cyclically across snare tracks in given order to create ladder
            int snareCount = snareIndices.Count;
            for (int i = 0; i < masterHits.Count; i++)
            {
                int hitPos = masterHits[i];
                int snareTrack = snareIndices[i % snareCount];
                patterns[snareTrack][hitPos] = true;
            }

            // Optionally add denser small rolls on the first few snares to emphasize ladder start
            for (int si = 0; si < Math.Min(2, snareCount); si++)
            {
                int trackIdx = snareIndices[si];
                // For each primary hit assigned to this track, chance to create a short roll (adjacent hits)
                var primaryPositions = Enumerable.Range(0, totalSteps).Where(p => patterns[trackIdx][p]).ToList();
                foreach (var p in primaryPositions)
                {
                    if (rnd.NextDouble() < 0.55)
                    {
                        // add up to 2 following rapid hits
                        for (int r = 1; r <= 2; r++)
                        {
                            int rp = p + r;
                            if (rp < totalSteps && rnd.NextDouble() < 0.7)
                            {
                                patterns[trackIdx][rp] = true;
                            }
                        }
                    }
                }
            }

            // Fill supporting parts for non-snare elements with subdued patterns
            for (int idx = 0; idx < elements.Count; idx++)
            {
                var elem = elements[idx];
                if (snareIndices.Contains(idx)) continue;

                // Use the generator helper to create a lightweight line
                var localRnd = new Random((seed ?? NextSeed()) ^ idx);
                var line = GenerateLineForElement(elem, totalSteps, resolution, density * 0.45f, swing, Math.Max(0.4f, complexity * 0.6f), localRnd);

                // Merge into patterns (OR)
                for (int p = 0; p < totalSteps; p++) if (line[p]) patterns[idx][p] = true;
            }

            // Optionally interleave to avoid overlapping hits across tracks
            var result = patterns;
            if (interleaved)
            {
                result = MakeInterleaved(result, elements.ToArray());
            }

            return await Task.FromResult(result);
        }

        public static async Task<List<bool[]>> Preset_AmenSnareScale(IEnumerable<DrumsetElement> drumset, int bars, float density, int resolution = DefaultResolution, float swing = 0.0f, float complexity = 1.0f, bool interleaved = false, int? seed = null)
        {
            if (bars <= 0) throw new ArgumentOutOfRangeException(nameof(bars));
            if (resolution <= 0) throw new ArgumentOutOfRangeException(nameof(resolution));

            var elements = drumset.ToList();
            int totalSteps = bars * resolution;

            var basePatterns = await GenerateBreakPatternAsync(elements, bars, density, resolution, swing, complexity, false, seed);
            var patterns = basePatterns.Select(p => p.ToArray()).ToList();

            var snareIndices = elements
                .Select((e, i) => new { e, i })
                .Where(x => x.e == DrumsetElement.Snare || x.e == DrumsetElement.SnareRattle)
                .Select(x => x.i)
                .ToList();

            if (snareIndices.Count == 0)
            {
                if (interleaved)
                    patterns = MakeInterleaved(patterns, elements.ToArray());
                return patterns;
            }

            int baseSeed = seed ?? NextSeed();
            var rnd = new Random(baseSeed ^ 0xA553);

            for (int bar = 0; bar < bars; bar++)
            {
                int offset = bar * resolution;
                float barFactor = (bar + 1f) / bars;
                float inten = Math.Clamp(complexity * barFactor, 0.4f, 2.0f);

                int backbeatA = offset + resolution / 4;
                int backbeatB = offset + (3 * resolution) / 4;

                foreach (var snIdx in snareIndices)
                {
                    if (backbeatA >= 0 && backbeatA < totalSteps)
                        patterns[snIdx][backbeatA] = true;
                    if (backbeatB >= 0 && backbeatB < totalSteps)
                        patterns[snIdx][backbeatB] = true;
                }

                int tailStart = offset + (3 * resolution) / 4;
                int tailEnd = offset + resolution - 1;
                for (int pos = tailStart; pos <= tailEnd; pos++)
                {
                    foreach (var snIdx in snareIndices)
                    {
                        double p = 0.25 * inten;
                        if (bar == bars - 1) p *= 1.4;
                        if (rnd.NextDouble() < p)
                            patterns[snIdx][pos] = true;
                    }
                }

                if (bar == bars - 1)
                {
                    int rollCenter = offset + (3 * resolution) / 4;
                    int rollLen = Math.Max(2, resolution / 8);
                    foreach (var snIdx in snareIndices)
                    {
                        for (int r = -rollLen; r <= rollLen; r++)
                        {
                            int pos = rollCenter + r;
                            if (pos < offset || pos >= offset + resolution) continue;
                            double p = 0.4 + 0.4 * (1.0 - Math.Abs(r) / (double) rollLen);
                            if (rnd.NextDouble() < p * inten)
                                patterns[snIdx][pos] = true;
                        }
                    }
                }
            }

            if (interleaved)
                patterns = MakeInterleaved(patterns, elements.ToArray());

            return patterns;
        }

        public static async Task<List<bool[]>> Preset_JungleRoller(IEnumerable<DrumsetElement> drumset, int bars, float density, int resolution = DefaultResolution, float swing = 0.0f, float complexity = 1.0f, bool interleaved = false, int? seed = null)
        {
            if (bars <= 0) throw new ArgumentOutOfRangeException(nameof(bars));
            if (resolution <= 0) throw new ArgumentOutOfRangeException(nameof(resolution));

            var elements = drumset.ToList();
            int totalSteps = bars * resolution;

            float baseDensity = Math.Clamp(density * 1.3f, 0.25f, 0.9f);
            float baseComplexity = Math.Clamp(complexity * 1.4f, 0.6f, 4.0f);
            float baseSwing = Math.Clamp(swing + 0.04f, 0f, 0.2f);

            var patterns = await GenerateBreakPatternAsync(elements, bars, baseDensity, resolution, baseSwing, baseComplexity, false, seed);
            patterns = patterns.Select(p => p.ToArray()).ToList();

            var snareIndices = elements
                .Select((e, i) => new { e, i })
                .Where(x => x.e == DrumsetElement.Snare || x.e == DrumsetElement.SnareRattle)
                .Select(x => x.i)
                .ToList();

            if (snareIndices.Count > 0)
            {
                int baseSeed = seed ?? NextSeed();
                var rnd = new Random(baseSeed ^ 0xB00B);

                for (int bar = 0; bar < bars; bar++)
                {
                    int offset = bar * resolution;
                    float barFactor = (bar + 1f) / bars;

                    foreach (var snIdx in snareIndices)
                    {
                        for (int local = 0; local < resolution; local++)
                        {
                            int pos = offset + local;
                            if (!patterns[snIdx][pos]) continue;

                            if (rnd.NextDouble() < 0.6 * barFactor)
                            {
                                int rollLen = rnd.Next(2, Math.Max(3, resolution / 8));
                                for (int r = 1; r <= rollLen; r++)
                                {
                                    int rp = pos + r;
                                    if (rp >= offset + resolution || rp >= totalSteps) break;
                                    double p = 0.7 + 0.3 * (1.0 - r / (double) rollLen);
                                    if (rnd.NextDouble() < p)
                                        patterns[snIdx][rp] = true;
                                }
                            }
                        }
                    }
                }
            }

            var thinkIndices = elements
                .Select((e, i) => new { e, i })
                .Where(x => x.e == DrumsetElement.ThinkBreak)
                .Select(x => x.i)
                .ToList();

            if (thinkIndices.Count > 0)
            {
                int baseSeed = (seed ?? NextSeed()) ^ 0xC0DE;
                var rnd = new Random(baseSeed);

                foreach (var tbIdx in thinkIndices)
                {
                    var line = patterns[tbIdx];
                    for (int bar = 0; bar < bars; bar++)
                    {
                        if (rnd.NextDouble() < 0.5)
                        {
                            var tmp = new bool[resolution];
                            int offset = bar * resolution;
                            for (int i = 0; i < resolution; i++)
                                tmp[i] = line[offset + i];

                            Array.Reverse(tmp);
                            for (int i = 0; i < resolution; i++)
                                line[offset + i] = tmp[i];
                        }
                    }
                }
            }

            if (interleaved)
                patterns = MakeInterleaved(patterns, elements.ToArray());

            return patterns;
        }

        public static async Task<List<bool[]>> Preset_FunkShuffle(IEnumerable<DrumsetElement> drumset, int bars, float density, int resolution = DefaultResolution, float swing = 0.0f, float complexity = 1.0f, bool interleaved = false, int? seed = null)
        {
            if (bars <= 0) throw new ArgumentOutOfRangeException(nameof(bars));
            if (resolution <= 0) throw new ArgumentOutOfRangeException(nameof(resolution));

            var elements = drumset.ToList();
            int totalSteps = bars * resolution;

            float baseDensity = Math.Clamp(density * 0.85f, 0.1f, 0.7f);
            float baseComplexity = Math.Clamp(complexity, 0.5f, 2.0f);
            float baseSwing = Math.Clamp(Math.Max(swing, 0.18f), 0.15f, 0.35f);

            var patterns = await GenerateBreakPatternAsync(elements, bars, baseDensity, resolution, baseSwing, baseComplexity, false, seed);
            patterns = patterns.Select(p => p.ToArray()).ToList();

            var hatIndices = elements
                .Select((e, i) => new { e, i })
                .Where(x => x.e == DrumsetElement.HiHatClosed || x.e == DrumsetElement.Shaker || x.e == DrumsetElement.Ride)
                .Select(x => x.i)
                .ToList();

            var snareIndices = elements
                .Select((e, i) => new { e, i })
                .Where(x => x.e == DrumsetElement.Snare || x.e == DrumsetElement.Clap || x.e == DrumsetElement.Rim)
                .Select(x => x.i)
                .ToList();

            int baseSeed = seed ?? NextSeed();
            var rnd = new Random(baseSeed ^ 0xF00D);

            if (hatIndices.Count > 0)
            {
                foreach (var hIdx in hatIndices)
                {
                    var line = patterns[hIdx];
                    for (int bar = 0; bar < bars; bar++)
                    {
                        int offset = bar * resolution;
                        for (int s = 0; s < resolution; s++)
                        {
                            int pos = offset + s;
                            bool isTripletSlot = (s % 2 == 1);
                            if (!isTripletSlot) continue;
                            if (rnd.NextDouble() < 0.45)
                                line[pos] = true;
                        }
                    }
                }
            }

            if (snareIndices.Count > 0)
            {
                foreach (var snIdx in snareIndices)
                {
                    var line = patterns[snIdx];
                    for (int bar = 0; bar < bars; bar++)
                    {
                        int offset = bar * resolution;
                        int q = resolution / 4;
                        int posA = offset + q;
                        int posB = offset + 3 * q;
                        if (posA >= 0 && posA < totalSteps) line[posA] = true;
                        if (posB >= 0 && posB < totalSteps) line[posB] = true;

                        if (rnd.NextDouble() < 0.55)
                        {
                            int ghost = posB - 1;
                            if (ghost >= offset && ghost < offset + resolution)
                                line[ghost] = true;
                        }
                    }
                }
            }

            if (interleaved)
                patterns = MakeInterleaved(patterns, elements.ToArray());

            return patterns;
        }

        public static async Task<List<bool[]>> Preset_DnBStepper(IEnumerable<DrumsetElement> drumset, int bars, float density, int resolution = DefaultResolution, float swing = 0.0f, float complexity = 1.0f, bool interleaved = false, int? seed = null)
        {
            if (bars <= 0) throw new ArgumentOutOfRangeException(nameof(bars));
            if (resolution <= 0) throw new ArgumentOutOfRangeException(nameof(resolution));

            var elements = drumset.ToList();
            int totalSteps = bars * resolution;

            float baseDensity = Math.Clamp(density * 0.9f, 0.15f, 0.8f);
            float baseComplexity = Math.Clamp(complexity, 0.6f, 2.5f);
            float baseSwing = Math.Clamp(swing * 0.5f, 0f, 0.08f);

            var patterns = await GenerateBreakPatternAsync(elements, bars, baseDensity, resolution, baseSwing, baseComplexity, false, seed);
            patterns = patterns.Select(p => p.ToArray()).ToList();

            var kickIndices = elements
                .Select((e, i) => new { e, i })
                .Where(x => x.e == DrumsetElement.Kick)
                .Select(x => x.i)
                .ToList();

            var snareIndices = elements
                .Select((e, i) => new { e, i })
                .Where(x => x.e == DrumsetElement.Snare || x.e == DrumsetElement.SnareRattle || x.e == DrumsetElement.Clap)
                .Select(x => x.i)
                .ToList();

            int baseSeed = seed ?? NextSeed();
            var rnd = new Random(baseSeed ^ 0x5E7);

            for (int bar = 0; bar < bars; bar++)
            {
                int offset = bar * resolution;
                int q = resolution / 4;

                foreach (var kIdx in kickIndices)
                {
                    var line = patterns[kIdx];
                    int k0 = offset;
                    if (k0 >= 0 && k0 < totalSteps) line[k0] = true;

                    int k2 = offset + 2 * q;
                    if (k2 >= 0 && k2 < totalSteps && rnd.NextDouble() < 0.9)
                        line[k2] = true;

                    if (rnd.NextDouble() < 0.6)
                    {
                        int late = offset + 3 * q + rnd.Next(-2, 3);
                        if (late >= offset && late < offset + resolution)
                            line[late] = true;
                    }
                }

                foreach (var snIdx in snareIndices)
                {
                    var line = patterns[snIdx];
                    int sA = offset + q;
                    int sB = offset + 3 * q;
                    if (sA >= 0 && sA < totalSteps) line[sA] = true;
                    if (sB >= 0 && sB < totalSteps) line[sB] = true;

                    if (rnd.NextDouble() < 0.45)
                    {
                        int ghost = sB - 1;
                        if (ghost >= offset && ghost < offset + resolution)
                            line[ghost] = true;
                    }
                }
            }

            if (interleaved)
                patterns = MakeInterleaved(patterns, elements.ToArray());

            return patterns;
        }






    }
}
