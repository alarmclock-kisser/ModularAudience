namespace ModularAudience.Audio.Processors_V4
{
    public static class DeterministicSeparationProcessor
    {
        public static Task<DeterministicSeparationAnalysis> AnalyzeAsync(AudioObj source,
            DeterministicSeparationSettings settings, IProgress<DeterministicSeparationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(settings);
            settings.Validate();
            if (settings.UseCqtAnalysis || settings.UseCqtSynthesis || settings.UsePyin || settings.UseIlrma)
            {
                throw new NotSupportedException("Advanced DSP cores are not yet connected to this pipeline. See SOURCE_SEPARATION_HANDOFF.md. Use automatic separation with advanced options disabled for now.");
            }
            float[] samples = source.Data;
            int sampleRate = source.SampleRate;
            int channels = source.Channels;
            string name = source.Name;
            float bpm = source.Bpm;
            string key = source.Key;
            ValidateFormat(samples, sampleRate, channels);
            return Task.Run(() =>
            {
                float[] copy = CopySamples(samples, cancellationToken);
                DeterministicAudioSnapshot snapshot = new(copy, sampleRate, channels, name, bpm, key);
                DeterministicSourceModel model = DeterministicSourceModel.Train(snapshot, settings, progress, cancellationToken);
                return new DeterministicSeparationAnalysis(snapshot, settings, model, Warnings(snapshot, settings, model));
            }, cancellationToken);
        }

        public static Task<DeterministicSeparationResult> SeparateAsync(DeterministicSeparationAnalysis analysis,
            IReadOnlyCollection<int>? sourceIds = null, IProgress<DeterministicSeparationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(analysis);
            int[] selectedIds = sourceIds?.Distinct().ToArray() ?? analysis.Sources.Select(source => source.Id).ToArray();
            if (selectedIds.Any(id => !analysis.Sources.Any(source => source.Id == id)))
            {
                throw new ArgumentException("The selection contains an unknown source group.", nameof(sourceIds));
            }
            int[] selected = analysis.Sources.Select((source, index) => (source, index))
                .Where(item => selectedIds.Contains(item.source.Id)).Select(item => item.index).ToArray();
            return Task.Run(() => Separate(analysis, selected, progress, cancellationToken), cancellationToken);
        }

        private static void ValidateFormat(float[] samples, int sampleRate, int channels)
        {
            ArgumentNullException.ThrowIfNull(samples);
            if (samples.Length == 0 || sampleRate <= 0 || channels is < 1 or > 2 || samples.Length % channels != 0)
            {
                throw new ArgumentException("A nonempty, frame-aligned mono or stereo signal with a valid sample rate is required.");
            }
        }

        private static float[] CopySamples(float[] samples, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            float[] copy = new float[samples.Length];
            for (int start = 0; start < samples.Length;)
            {
                token.ThrowIfCancellationRequested();
                int end = (int) Math.Min(samples.LongLength, (long) start + 65536);
                for (int index = start; index < end; index++)
                {
                    float value = samples[index];
                    if (!float.IsFinite(value))
                    {
                        throw new ArgumentException("The input contains NaN or infinity. Repair the source audio before separation.");
                    }
                    copy[index] = value;
                }
                start = end;
            }
            return copy;
        }

        private static IReadOnlyList<string> Warnings(DeterministicAudioSnapshot source,
            DeterministicSeparationSettings settings, DeterministicSourceModel model)
        {
            List<string> warnings =
            [
                "Groups and confidence scores are acoustic heuristics, not identified instruments or calibrated probabilities.",
                "Residual includes ambiguous, unmodeled and unselected material. Keep it to reconstruct the original mix.",
                "Restoration reduces masking artifacts; it cannot recover information missing from the recording."
            ];
            if (DeterministicSpectrogram.FrameCount(source, settings) > settings.AnalysisFrames)
            {
                warnings.Add("Dictionary learning uses distributed analysis windows, not every frame. Rare events can be missed; increase Analysis frames if needed.");
            }
            if (model.EstimatedRank >= settings.MaxComponents)
            {
                warnings.Add("The component limit was reached. Increase Max components and analyze again to explore a richer model.");
            }
            if (model.Sources.Count == 0)
            {
                warnings.Add("No source group was found in the analysis windows. Separation will return the original signal as Residual.");
            }
            return warnings.AsReadOnly();
        }

        private static DeterministicSeparationResult Separate(DeterministicSeparationAnalysis analysis, int[] selected,
            IProgress<DeterministicSeparationProgress>? progress, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            CheckOutputMemory(analysis, selected.Length);
            float[][] output = new float[selected.Length + 1][];
            for (int index = 0; index < output.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                output[index] = new float[analysis.Source.Samples.Length];
            }
            if (selected.Length > 0)
            {
                RenderBlocks(analysis, selected, output, progress, token);
            }
            progress?.Report(new(0.96, "Restoring mixture consistency in Residual"));
            double error = RestoreResidual(analysis.Source.Samples, output, token);
            token.ThrowIfCancellationRequested();
            progress?.Report(new(1, "Separation complete"));
            IReadOnlyList<AudioObj> stems = CreateStems(analysis, selected, output, token);
            return new(stems, error);
        }

        private static void CheckOutputMemory(DeterministicSeparationAnalysis analysis, int groups)
        {
            long outputBytes = checked(analysis.SampleCount * sizeof(float) * (groups + 1));
            long bins = analysis.Settings.WindowSize / 2 + 1;
            long frames = analysis.Settings.BlockFrames + analysis.Settings.MedianFrames + 32;
            long workingBytes = checked(frames * (analysis.Channels * analysis.Settings.WindowSize * 16L
                + bins * (64L + analysis.Sources.Count * sizeof(float))));
            long available = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            if (available > 0 && outputBytes + workingBytes + GC.GetTotalMemory(false) > available * 0.85)
            {
                throw new InvalidOperationException($"Separation requires approximately {(outputBytes + workingBytes) / 1048576.0:F0} MiB of additional memory. Select fewer groups or use a shorter input.");
            }
        }

        private static void RenderBlocks(DeterministicSeparationAnalysis analysis, int[] selected, float[][] output,
            IProgress<DeterministicSeparationProgress>? progress, CancellationToken token)
        {
            DeterministicSeparationSettings settings = analysis.Settings;
            int total = DeterministicSpectrogram.FrameCount(analysis.Source, settings);
            // In addition to median context, 16 frames let the causal mask smoother settle at block edges.
            int halo = settings.MedianFrames / 2 + 16;
            double[] window = DeterministicSpectrogram.CreateWindow(settings.WindowSize);
            for (int start = 0; start < total;)
            {
                token.ThrowIfCancellationRequested();
                int count = Math.Min(settings.BlockFrames, total - start);
                int first = Math.Max(0, start - halo);
                int end = (int) Math.Min(total, (long) start + count + halo);
                progress?.Report(new(0.94 * start / total, $"Separating frames {start + 1}-{start + count} of {total}"));
                DeterministicSpectrogram block = DeterministicSpectrogram.Read(analysis.Source, settings, first, end - first, true, token);
                float[][][] masks = analysis.Model.BuildMasks(block, settings, token);
                DeterministicSynthesis.AddBlock(analysis.Source, settings, block, masks, selected, output, start, count, window, token);
                start += count;
            }
        }

        private static double RestoreResidual(float[] original, float[][] output, CancellationToken token)
        {
            double maximumError = 0;
            for (int sample = 0; sample < original.Length; sample++)
            {
                if ((sample & 65535) == 0) token.ThrowIfCancellationRequested();
                double sum = 0;
                for (int stem = 0; stem < output.Length - 1; stem++) sum += output[stem][sample];
                output[^1][sample] = (float) (original[sample] - sum);
                if (!float.IsFinite(output[^1][sample])) throw new ArithmeticException("Non-finite residual reconstruction.");
                maximumError = Math.Max(maximumError, Math.Abs(original[sample] - (sum + output[^1][sample])));
            }
            return maximumError;
        }

        private static IReadOnlyList<AudioObj> CreateStems(DeterministicSeparationAnalysis analysis,
            int[] selected, float[][] output, CancellationToken token)
        {
            List<AudioObj> stems = [];
            try
            {
                for (int index = 0; index < output.Length; index++)
                {
                    token.ThrowIfCancellationRequested();
                    string name = index < selected.Length ? analysis.Sources[selected[index]].Name : "Residual";
                    AudioObj stem = new();
                    stems.Add(stem);
                    PopulateStem(stem, analysis.Source, output[index], name);
                }
                return stems.AsReadOnly();
            }
            catch
            {
                foreach (AudioObj stem in stems) stem.Dispose();
                throw;
            }
        }

        private static void PopulateStem(AudioObj stem, DeterministicAudioSnapshot source, float[] data, string name)
        {
            stem.Data = data;
            stem.SampleRate = source.SampleRate;
            stem.Channels = source.Channels;
            stem.BitDepth = 32;
            stem.Length = data.LongLength;
            stem.Duration = TimeSpan.FromSeconds(data.LongLength / ((double) source.SampleRate * source.Channels));
            stem.Bpm = source.Bpm;
            stem.Key = source.Key;
            stem.Rename($"{source.Name} - {name}");
        }
    }
}
