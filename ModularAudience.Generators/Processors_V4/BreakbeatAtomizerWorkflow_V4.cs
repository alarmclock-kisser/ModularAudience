using ModularAudience.Audio;
using ModularAudience.Audio.Processors_V4;

namespace ModularAudience.Generators
{
    public sealed record BreakbeatWorkflowSettings(
        float Bpm,
        int Bars,
        int HitsPerBar,
        float Density,
        float Complexity,
        int Resolution,
        float Swing);

    public sealed record BreakbeatWorkflowResult(AudioObj? Rendered, string CollectionName, string LogMessage);

    public sealed record AtomizeWorkflowResult(IReadOnlyList<AudioObj> Atomics, bool IsLikelyDrumLoop, string? SummaryLog);

    public static class BreakbeatAtomizerWorkflow_V4
    {
        public static async Task<BreakbeatWorkflowResult> GenerateBreakbeatAsync(
            IReadOnlyList<AudioObj> selected,
            string collectionTitle,
            BreakbeatWorkflowSettings settings,
            CancellationToken cancellationToken = default)
        {
            if (selected.Count <= 1)
            {
                return new BreakbeatWorkflowResult(null, string.Empty, string.Empty);
            }

            cancellationToken.ThrowIfCancellationRequested();
            List<AudioObj> sources = selected.Select(audio => audio.Clone()).ToList();
            DrumsetElement[] mappedElements = MapSelectedAudiosToDrumset(selected);
            int seed = Random.Shared.Next(1, int.MaxValue);

            List<bool[]> pattern = await BreakbeatGenerator_V2.GenerateBreakPatternAsync(
                drumset: mappedElements,
                bars: settings.Bars,
                density: settings.Density,
                resolution: settings.Resolution,
                swing: settings.Swing,
                complexity: settings.Complexity,
                interleaved: false,
                seed: seed,
                preset: " - None - ");

            int targetHits = settings.HitsPerBar * settings.Bars;
            RetargetPatternHitCount(pattern, mappedElements, targetHits, settings.Resolution);

            cancellationToken.ThrowIfCancellationRequested();
            AudioObj rendered = await BreakbeatGenerator_V2.RenderBreakbeatAsync(
                pattern,
                sources,
                settings.Bpm,
                settings.Resolution,
                settings.Swing,
                "Breakbeat");

            string collectionName = BuildBreakbeatCollectionName(selected, collectionTitle);
            rendered.Rename(collectionName);

            string log = $"Generated breakbeat from {selected.Count} selected atomic sample(s) at {settings.Bpm:F1} BPM, {settings.Bars} bar(s), target {settings.HitsPerBar} hits/bar.";
            return new BreakbeatWorkflowResult(rendered, collectionName, log);
        }

        public static async Task<AtomizeWorkflowResult> AtomizeAsync(
            AudioObj source,
            LoopAtomizerSettings settings,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LoopAtomizerResult result = await LoopAtomizer_V4.AtomizeAsync(source, settings);
            List<AudioObj> atomics = result.Atomics.ToList();

            string? summary = null;
            if (result.IsLikelyDrumLoop)
            {
                foreach (AudioObj atomic in atomics)
                {
                    if (Enum.TryParse(atomic.SampleTag, true, out DrumsetElement element))
                    {
                        atomic.Tag = element;
                    }
                }

                summary = string.Join(", ", atomics.Where(a => a.Tag is DrumsetElement).Select(a => $"{a.Name}={a.Tag}"));
            }

            return new AtomizeWorkflowResult(atomics, result.IsLikelyDrumLoop, summary);
        }

        private static string BuildBreakbeatCollectionName(IReadOnlyList<AudioObj> selected, string collectionTitle)
        {
            string prefix = selected.Count == 0
                ? (string.IsNullOrWhiteSpace(collectionTitle) ? "Breakbeat" : collectionTitle)
                : string.Join("+", selected.Take(3).Select(x => string.IsNullOrWhiteSpace(x.Name) ? "Sample" : x.Name.Trim()));

            if (selected.Count > 3)
            {
                prefix += $"+{selected.Count - 3}";
            }

            return prefix + "_Breakbeat";
        }

        private static DrumsetElement[] MapSelectedAudiosToDrumset(IReadOnlyList<AudioObj> selected)
        {
            string[] names = selected.Select(audio => audio.Name ?? string.Empty).ToArray();
            DrumsetElement[] fallback = BreakbeatGenerator_V2.MatchSampleNamesToDrumsetElements(names);
            DrumsetElement[] mapped = new DrumsetElement[selected.Count];

            for (int i = 0; i < selected.Count; i++)
            {
                AudioObj audio = selected[i];
                if (audio.Tag is DrumsetElement tagged)
                {
                    mapped[i] = tagged;
                    continue;
                }

                if (Enum.TryParse(audio.SampleTag, true, out DrumsetElement sampleTagElement))
                {
                    mapped[i] = sampleTagElement;
                    continue;
                }

                DrumsetElement inferred = fallback[i];
                double durationMs = ResolveDuration(audio).TotalMilliseconds;
                if (inferred == DrumsetElement.HiHatClosed && durationMs > 220)
                {
                    inferred = DrumsetElement.HiHatOpen;
                }
                else if (inferred == DrumsetElement.CrashShort && durationMs > 650)
                {
                    inferred = DrumsetElement.CrashLong;
                }
                else if (inferred == DrumsetElement.Snare && durationMs > 450)
                {
                    inferred = DrumsetElement.SnareRattle;
                }

                mapped[i] = inferred;
            }

            return mapped;
        }

        private static TimeSpan ResolveDuration(AudioObj audio)
        {
            if (audio.Duration > TimeSpan.Zero)
            {
                return audio.Duration;
            }

            if (audio.Data != null && audio.Data.Length > 0 && audio.SampleRate > 0)
            {
                int channels = Math.Max(1, audio.Channels);
                double totalFrames = audio.Data.LongLength / (double)channels;
                double seconds = totalFrames / audio.SampleRate;
                if (seconds > 0)
                {
                    return TimeSpan.FromSeconds(seconds);
                }
            }

            return TimeSpan.Zero;
        }

        private static void RetargetPatternHitCount(List<bool[]> pattern, DrumsetElement[] elements, int targetHits, int resolution)
        {
            int currentHits = pattern.Sum(line => line.Count(hit => hit));
            if (currentHits == targetHits)
            {
                return;
            }

            int maxAttempts = Math.Max(64, targetHits * 12);
            int attempts = 0;
            while (currentHits < targetHits && attempts++ < maxAttempts)
            {
                if (!TryAddPatternHit(pattern, elements, resolution))
                {
                    break;
                }

                currentHits++;
            }

            attempts = 0;
            while (currentHits > targetHits && attempts++ < maxAttempts)
            {
                if (!TryRemovePatternHit(pattern, elements, resolution))
                {
                    break;
                }

                currentHits--;
            }
        }

        private static bool TryAddPatternHit(List<bool[]> pattern, DrumsetElement[] elements, int resolution)
        {
            int bestTrack = -1;
            int bestStep = -1;
            double bestScore = double.MinValue;

            for (int track = 0; track < pattern.Count; track++)
            {
                bool[] line = pattern[track];
                for (int step = 0; step < line.Length; step++)
                {
                    if (line[step])
                    {
                        continue;
                    }

                    double score = ScorePatternPosition(elements[track], line, step, resolution, adding: true);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestTrack = track;
                        bestStep = step;
                    }
                }
            }

            if (bestTrack < 0 || bestStep < 0)
            {
                return false;
            }

            pattern[bestTrack][bestStep] = true;
            return true;
        }

        private static bool TryRemovePatternHit(List<bool[]> pattern, DrumsetElement[] elements, int resolution)
        {
            int bestTrack = -1;
            int bestStep = -1;
            double bestScore = double.MinValue;

            for (int track = 0; track < pattern.Count; track++)
            {
                bool[] line = pattern[track];
                for (int step = 0; step < line.Length; step++)
                {
                    if (!line[step] || IsAnchorStep(elements[track], step % resolution, resolution))
                    {
                        continue;
                    }

                    double score = ScorePatternPosition(elements[track], line, step, resolution, adding: false);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestTrack = track;
                        bestStep = step;
                    }
                }
            }

            if (bestTrack < 0 || bestStep < 0)
            {
                return false;
            }

            pattern[bestTrack][bestStep] = false;
            return true;
        }

        private static double ScorePatternPosition(DrumsetElement element, bool[] line, int step, int resolution, bool adding)
        {
            int stepInBar = step % resolution;
            bool neighbor = (step > 0 && line[step - 1]) || (step < line.Length - 1 && line[step + 1]);
            bool anchor = IsAnchorStep(element, stepInBar, resolution);
            double score = anchor ? 5.0 : 1.0;

            if (stepInBar % Math.Max(1, resolution / 4) == 0)
            {
                score += 2.0;
            }
            else if (stepInBar % Math.Max(1, resolution / 8) == 0)
            {
                score += 0.8;
            }

            if (neighbor)
            {
                score += adding ? 0.9 : 1.8;
            }

            score += element switch
            {
                DrumsetElement.Kick => adding ? 2.8 : 0.4,
                DrumsetElement.Snare or DrumsetElement.SnareRattle => adding ? 2.2 : 0.6,
                DrumsetElement.HiHatClosed or DrumsetElement.Shaker => adding ? 1.6 : 2.0,
                DrumsetElement.HiHatOpen or DrumsetElement.CrashLong or DrumsetElement.CrashShort => adding ? -1.2 : 3.2,
                DrumsetElement.Ride => adding ? 1.1 : 1.9,
                DrumsetElement.TomHigh or DrumsetElement.TomMid or DrumsetElement.TomLow or DrumsetElement.FloorTom => adding ? 0.4 : 2.1,
                _ => adding ? 0.7 : 1.5
            };

            return score;
        }

        private static bool IsAnchorStep(DrumsetElement element, int stepInBar, int resolution)
        {
            int quarter = Math.Max(1, resolution / 4);
            return element switch
            {
                DrumsetElement.Kick => stepInBar == 0 || stepInBar == quarter * 2,
                DrumsetElement.Snare or DrumsetElement.SnareRattle or DrumsetElement.Clap or DrumsetElement.Rim => stepInBar == quarter || stepInBar == quarter * 3,
                DrumsetElement.HiHatClosed or DrumsetElement.Ride or DrumsetElement.Shaker => stepInBar % quarter == 0,
                _ => false
            };
        }
    }
}
