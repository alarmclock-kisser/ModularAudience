using ModularAudience.Audio.Processors_V4;

namespace ModularAudience.Audio.Processing
{
    public sealed record AudioAtomizeResult(
        IReadOnlyList<AudioObj> Atomics,
        bool IsLikelyDrumLoop,
        string? SummaryLog);

    /// <summary>Application-independent orchestration for loop atomization.</summary>
    public static class AudioAtomizerWorkflow
    {
        public static async Task<AudioAtomizeResult> AtomizeAsync(
            AudioObj source,
            LoopAtomizerSettings settings,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            cancellationToken.ThrowIfCancellationRequested();

            LoopAtomizerResult result = await LoopAtomizer_V4
                .AtomizeAsync(source, settings, progress)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            IReadOnlyList<AudioObj> cleanedAtomics = CleanAtomics(result.Atomics);
            string? summary = result.IsLikelyDrumLoop
                ? string.Join(", ", cleanedAtomics
                    .Where(atomic => !string.IsNullOrWhiteSpace(atomic.SampleTag))
                    .Select(atomic => $"{atomic.Name}={atomic.SampleTag}"))
                : null;

            return new AudioAtomizeResult(cleanedAtomics, result.IsLikelyDrumLoop, summary);
        }

        private static IReadOnlyList<AudioObj> CleanAtomics(IReadOnlyList<AudioObj> atomics)
        {
            List<(AudioObj Audio, float Quality, float[] Signature)> candidates = [];
            foreach (AudioObj atomic in atomics)
            {
                if (TryMeasureQuality(atomic, out float quality, out float[] signature))
                {
                    candidates.Add((atomic, quality, signature));
                }
            }

            List<(AudioObj Audio, float Quality, float[] Signature)> kept = [];
            foreach ((AudioObj Audio, float Quality, float[] Signature) candidate in candidates)
            {
                int similarIndex = -1;
                for (int i = 0; i < kept.Count; i++)
                {
                    if (AreNearDuplicates(candidate.Audio, candidate.Signature, kept[i].Audio, kept[i].Signature))
                    {
                        similarIndex = i;
                        break;
                    }
                }

                if (similarIndex < 0)
                {
                    kept.Add(candidate);
                }
                else if (candidate.Quality > kept[similarIndex].Quality)
                {
                    kept[similarIndex] = candidate;
                }
            }

            return kept.Select(item => item.Audio).ToArray();
        }

        private static bool TryMeasureQuality(AudioObj audio, out float quality, out float[] signature)
        {
            quality = 0f;
            signature = [];
            if (audio.Data == null || audio.Data.Length == 0 || audio.SampleRate <= 0 || audio.Channels <= 0)
            {
                return false;
            }

            int channels = audio.Channels;
            int frameCount = audio.Data.Length / channels;
            if (frameCount < 8)
            {
                return false;
            }

            float peak = 0f;
            for (int i = 0; i < audio.Data.Length; i++)
            {
                peak = Math.Max(peak, Math.Abs(audio.Data[i]));
            }

            if (peak < 0.0005f)
            {
                return false;
            }

            float activeThreshold = Math.Max(0.00035f, peak * 0.035f);
            int firstActive = frameCount;
            int lastActive = -1;
            int activeRuns = 0;
            int silentRun = 0;
            int largestInternalGap = 0;
            bool active = false;
            const int blockCount = 32;
            int blockSize = Math.Max(1, (frameCount + blockCount - 1) / blockCount);

            for (int block = 0; block < blockCount && block * blockSize < frameCount; block++)
            {
                int start = block * blockSize;
                int end = Math.Min(frameCount, start + blockSize);
                float blockPeak = 0f;
                for (int frame = start; frame < end; frame++)
                {
                    int offset = frame * channels;
                    for (int channel = 0; channel < channels; channel++)
                    {
                        blockPeak = Math.Max(blockPeak, Math.Abs(audio.Data[offset + channel]));
                    }
                }

                bool blockActive = blockPeak >= activeThreshold;
                if (blockActive)
                {
                    if (!active)
                    {
                        activeRuns++;
                    }

                    active = true;
                    silentRun = 0;
                    firstActive = Math.Min(firstActive, start);
                    lastActive = end - 1;
                }
                else if (active)
                {
                    silentRun++;
                    largestInternalGap = Math.Max(largestInternalGap, silentRun);
                }
            }

            if (lastActive < firstActive || activeRuns > 1 && largestInternalGap >= 2)
            {
                return false;
            }

            float activeRatio = (lastActive - firstActive + 1) / (float) frameCount;
            if (activeRatio < 0.08f)
            {
                return false;
            }

            signature = BuildSignature(audio.Data, channels, firstActive, lastActive + 1, 96);
            quality = activeRatio / (1f + (largestInternalGap * 0.15f));
            return true;
        }

        private static bool AreNearDuplicates(AudioObj left, float[] leftSignature, AudioObj right, float[] rightSignature)
        {
            int leftFrames = left.Data!.Length / Math.Max(1, left.Channels);
            int rightFrames = right.Data!.Length / Math.Max(1, right.Channels);
            float durationRatio = leftFrames / (float) Math.Max(1, rightFrames);
            if (durationRatio < 0.72f || durationRatio > 1.39f)
            {
                return false;
            }

            double error = 0.0;
            for (int i = 0; i < leftSignature.Length; i++)
            {
                double difference = leftSignature[i] - rightSignature[i];
                error += difference * difference;
            }

            return Math.Sqrt(error / leftSignature.Length) <= 0.075;
        }

        private static float[] BuildSignature(float[] data, int channels, int startFrame, int endFrame, int sampleCount)
        {
            float[] signature = new float[sampleCount];
            int frameCount = Math.Max(1, endFrame - startFrame);
            for (int i = 0; i < sampleCount; i++)
            {
                int frame = startFrame + Math.Min(frameCount - 1, (i * frameCount) / sampleCount);
                int offset = frame * channels;
                float sum = 0f;
                for (int channel = 0; channel < channels; channel++)
                {
                    sum += data[offset + channel];
                }

                signature[i] = sum / channels;
            }

            float signaturePeak = signature.Max(value => Math.Abs(value));
            if (signaturePeak > 0f)
            {
                for (int i = 0; i < signature.Length; i++)
                {
                    signature[i] /= signaturePeak;
                }
            }

            return signature;
        }
    }
}
