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
        public static async Task<List<bool[]>> Preset_AmenSnareScale(IEnumerable<DrumsetElement> drumset, int bars, float density, int resolution = DefaultResolution, float swing = 0.0f, float complexity = 1.0f, bool interleaved = false, int? seed = null)
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
    }
}
