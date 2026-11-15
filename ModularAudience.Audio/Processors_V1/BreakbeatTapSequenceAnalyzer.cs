namespace ModularAudience.Audio.Processors_V1;

public sealed record BreakbeatTapHit(int FrameIndex, int GroupIndex, float Peak, float Rms);

public sealed class BreakbeatTapSequence
{
    private readonly float[][] normalizedFeatures;
    private readonly BreakbeatTapHit[] detectedHits;

    internal BreakbeatTapSequence(float[][] normalizedFeatures, BreakbeatTapHit[] detectedHits, double bpm, int stepsPerBar)
    {
        this.normalizedFeatures = normalizedFeatures;
        this.detectedHits = detectedHits;
        this.EstimatedBpm = bpm;
        this.SuggestedStepsPerBar = stepsPerBar;
        this.SuggestedDrumElements = EstimateGroupCount(normalizedFeatures);
    }

    public double EstimatedBpm { get; }

    public int SuggestedStepsPerBar { get; }

    public int SuggestedDrumElements { get; }

    public int HitCount => this.detectedHits.Length;

    public IReadOnlyList<BreakbeatTapHit> GroupTaps(int drumElementCount)
    {
        if (drumElementCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(drumElementCount), "At least one drum element is required.");
        }

        if (this.detectedHits.Length == 0)
        {
            return [];
        }

        (int[] labels, _) = Cluster(this.normalizedFeatures, Math.Min(drumElementCount, this.detectedHits.Length));
        Dictionary<int, int> groupOrder = [];
        BreakbeatTapHit[] grouped = new BreakbeatTapHit[this.detectedHits.Length];
        for (int index = 0; index < grouped.Length; index++)
        {
            if (!groupOrder.TryGetValue(labels[index], out int group))
            {
                group = groupOrder.Count;
                groupOrder.Add(labels[index], group);
            }

            grouped[index] = this.detectedHits[index] with { GroupIndex = group };
        }

        return grouped;
    }

    private static int EstimateGroupCount(float[][] features)
    {
        int maximumGroups = Math.Min(8, features.Length);
        if (maximumGroups <= 1)
        {
            return Math.Max(1, maximumGroups);
        }

        double previousInertia = Cluster(features, 1).Inertia;
        if (previousInertia <= features.Length * 0.0025)
        {
            return 1;
        }

        int selectedGroups = 1;
        for (int groups = 2; groups <= maximumGroups; groups++)
        {
            double inertia = Cluster(features, groups).Inertia;
            double improvement = (previousInertia - inertia) / previousInertia;
            if (improvement < 0.2)
            {
                break;
            }

            selectedGroups = groups;
            previousInertia = inertia;
            if (inertia <= features.Length * 0.0025)
            {
                break;
            }
        }

        return selectedGroups;
    }

    private static (int[] Labels, double Inertia) Cluster(float[][] features, int requestedGroups)
    {
        int count = features.Length;
        int dimensions = features[0].Length;
        int groups = Math.Clamp(requestedGroups, 1, count);
        double[][] centers = new double[groups][];
        centers[0] = features[0].Select(value => (double)value).ToArray();

        for (int group = 1; group < groups; group++)
        {
            int farthestIndex = 0;
            double farthestDistance = double.NegativeInfinity;
            for (int item = 0; item < count; item++)
            {
                double nearestDistance = centers
                    .Take(group)
                    .Min(center => SquaredDistance(features[item], center));
                if (nearestDistance > farthestDistance)
                {
                    farthestDistance = nearestDistance;
                    farthestIndex = item;
                }
            }

            centers[group] = features[farthestIndex].Select(value => (double)value).ToArray();
        }

        int[] labels = new int[count];
        double[] distances = new double[count];
        for (int iteration = 0; iteration < 32; iteration++)
        {
            bool changed = false;
            for (int item = 0; item < count; item++)
            {
                int nearestGroup = 0;
                double nearestDistance = SquaredDistance(features[item], centers[0]);
                for (int group = 1; group < groups; group++)
                {
                    double distance = SquaredDistance(features[item], centers[group]);
                    if (distance < nearestDistance)
                    {
                        nearestDistance = distance;
                        nearestGroup = group;
                    }
                }

                changed |= labels[item] != nearestGroup;
                labels[item] = nearestGroup;
                distances[item] = nearestDistance;
            }

            double[][] sums = Enumerable.Range(0, groups).Select(_ => new double[dimensions]).ToArray();
            int[] sizes = new int[groups];
            for (int item = 0; item < count; item++)
            {
                int group = labels[item];
                sizes[group]++;
                for (int dimension = 0; dimension < dimensions; dimension++)
                {
                    sums[group][dimension] += features[item][dimension];
                }
            }

            for (int group = 0; group < groups; group++)
            {
                if (sizes[group] == 0)
                {
                    continue;
                }

                for (int dimension = 0; dimension < dimensions; dimension++)
                {
                    centers[group][dimension] = sums[group][dimension] / sizes[group];
                }
            }

            if (!changed)
            {
                break;
            }
        }

        double inertia = 0;
        for (int item = 0; item < count; item++)
        {
            inertia += SquaredDistance(features[item], centers[labels[item]]);
        }

        return (labels, inertia);
    }

    private static double SquaredDistance(float[] first, double[] second)
    {
        double distance = 0;
        for (int dimension = 0; dimension < first.Length; dimension++)
        {
            double delta = first[dimension] - second[dimension];
            distance += delta * delta;
        }

        return distance;
    }
}

public static class BreakbeatTapSequenceAnalyzer
{
    private const int MinimumTapDistanceMilliseconds = 80;
    private const int FeatureWindowMilliseconds = 80;

    private sealed record Detection(int FrameIndex, float Peak, float Rms, float[] Features);

    public static BreakbeatTapSequence Analyze(float[] interleavedSamples, int sampleRate, int channels)
    {
        ArgumentNullException.ThrowIfNull(interleavedSamples);
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        if (channels <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(channels));
        }

        int frameCount = interleavedSamples.Length / channels;
        List<Detection> detections = DetectTaps(interleavedSamples, sampleRate, channels, frameCount);
        float[][] features = NormalizeFeatures(detections.Select(detection => detection.Features).ToArray());
        double bpm = EstimateBpm(detections, sampleRate);
        int stepsPerBar = EstimateStepsPerBar(detections, sampleRate, bpm);
        BreakbeatTapHit[] hits = detections
            .Select(detection => new BreakbeatTapHit(detection.FrameIndex, 0, detection.Peak, detection.Rms))
            .ToArray();
        return new BreakbeatTapSequence(features, hits, bpm, stepsPerBar);
    }

    private static List<Detection> DetectTaps(float[] samples, int sampleRate, int channels, int frameCount)
    {
        if (frameCount < 3)
        {
            return [];
        }

        int windowFrames = Math.Max(1, sampleRate / 500);
        int windowCount = (frameCount + windowFrames - 1) / windowFrames;
        float[] envelope = new float[windowCount];
        float maximum = 0;
        for (int window = 0; window < windowCount; window++)
        {
            int firstFrame = window * windowFrames;
            int lastFrame = Math.Min(frameCount, firstFrame + windowFrames);
            double sumSquares = 0;
            long sampleCount = 0;
            for (int frame = firstFrame; frame < lastFrame; frame++)
            {
                for (int channel = 0; channel < channels; channel++)
                {
                    float value = samples[(frame * channels) + channel];
                    if (!float.IsFinite(value))
                    {
                        continue;
                    }

                    sumSquares += value * value;
                    sampleCount++;
                }
            }

            float rms = sampleCount == 0 ? 0 : (float)Math.Sqrt(sumSquares / sampleCount);
            envelope[window] = rms;
            maximum = Math.Max(maximum, rms);
        }

        if (maximum <= 0)
        {
            return [];
        }

        float[] sortedEnvelope = (float[])envelope.Clone();
        Array.Sort(sortedEnvelope);
        float background = sortedEnvelope[(sortedEnvelope.Length - 1) / 5];
        float threshold = Math.Max(0.00001f, Math.Max(background * 2.5f, maximum * 0.06f));
        if (maximum <= background * 2.5f)
        {
            return [];
        }

        List<(int PeakFrame, float Peak)> peaks = [];
        int minimumDistance = Math.Max(1, sampleRate * MinimumTapDistanceMilliseconds / 1000);
        for (int window = 1; window < windowCount - 1; window++)
        {
            float value = envelope[window];
            if (value < threshold || value < envelope[window - 1] || value <= envelope[window + 1])
            {
                continue;
            }

            int peakFrame = Math.Min(frameCount - 1, window * windowFrames + (windowFrames / 2));
            if (peaks.Count > 0 && peakFrame - peaks[^1].PeakFrame < minimumDistance)
            {
                if (value > peaks[^1].Peak)
                {
                    peaks[^1] = (peakFrame, value);
                }

                continue;
            }

            peaks.Add((peakFrame, value));
        }

        List<Detection> result = new(peaks.Count);
        int featureWindowFrames = Math.Max(1, sampleRate * FeatureWindowMilliseconds / 1000);
        int onsetLeadFrames = Math.Max(1, sampleRate * 4 / 1000);
        foreach ((int peakFrame, float peak) in peaks)
        {
            int analysisStart = Math.Max(0, peakFrame - onsetLeadFrames);
            int analysisEnd = Math.Min(frameCount, analysisStart + featureWindowFrames);
            float[] features = ExtractFeatures(samples, sampleRate, channels, analysisStart, analysisEnd);
            double sumSquares = 0;
            long sampleCount = 0;
            for (int frame = analysisStart; frame < analysisEnd; frame++)
            {
                for (int channel = 0; channel < channels; channel++)
                {
                    float sample = samples[(frame * channels) + channel];
                    if (!float.IsFinite(sample))
                    {
                        continue;
                    }

                    sumSquares += sample * sample;
                    sampleCount++;
                }
            }

            float rms = sampleCount == 0 ? 0 : (float)Math.Sqrt(sumSquares / sampleCount);
            result.Add(new Detection(Math.Max(0, peakFrame - onsetLeadFrames), peak, rms, features));
        }

        return result;
    }

    private static float[] ExtractFeatures(float[] samples, int sampleRate, int channels, int firstFrame, int lastFrame)
    {
        double lowCoefficient = Math.Exp(-2 * Math.PI * Math.Min(250, sampleRate * 0.1) / sampleRate);
        double midCoefficient = Math.Exp(-2 * Math.PI * Math.Min(2500, sampleRate * 0.4) / sampleRate);
        double previousLow = 0;
        double previousMid = 0;
        double[] energies = new double[4];
        int attackFrames = Math.Max(1, sampleRate / 100);
        int frameCount = Math.Max(1, lastFrame - firstFrame);

        for (int frame = 0; frame < frameCount; frame++)
        {
            int sourceFrame = firstFrame + frame;
            double value = 0;
            int validChannels = 0;
            for (int channel = 0; channel < channels; channel++)
            {
                float sample = samples[(sourceFrame * channels) + channel];
                if (!float.IsFinite(sample))
                {
                    continue;
                }

                value += sample;
                validChannels++;
            }

            if (validChannels == 0)
            {
                continue;
            }

            value /= validChannels;
            previousLow = (1 - lowCoefficient) * value + lowCoefficient * previousLow;
            previousMid = (1 - midCoefficient) * value + midCoefficient * previousMid;
            double low = previousLow;
            double mid = previousMid - previousLow;
            double high = value - previousMid;
            energies[0] += low * low;
            energies[1] += mid * mid;
            energies[2] += high * high;
            energies[3] += value * value;
        }

        double total = Math.Max(1e-20, energies[0] + energies[1] + energies[2]);
        double fullRms = Math.Sqrt(energies[3] / frameCount);
        double attackSum = 0;
        int attackCount = Math.Min(frameCount, attackFrames);
        for (int frame = 0; frame < attackCount; frame++)
        {
            double value = 0;
            int validChannels = 0;
            for (int channel = 0; channel < channels; channel++)
            {
                float sample = samples[((firstFrame + frame) * channels) + channel];
                if (!float.IsFinite(sample))
                {
                    continue;
                }

                value += sample;
                validChannels++;
            }

            if (validChannels > 0)
            {
                value /= validChannels;
                attackSum += value * value;
            }
        }

        double attackRms = Math.Sqrt(attackSum / Math.Max(1, attackCount));
        return
        [
            (float)(energies[0] / total),
            (float)(energies[1] / total),
            (float)(energies[2] / total),
            (float)(attackRms / Math.Max(1e-10, fullRms))
        ];
    }

    private static float[][] NormalizeFeatures(float[][] rawFeatures)
    {
        if (rawFeatures.Length == 0)
        {
            return [];
        }

        int dimensions = rawFeatures[0].Length;
        double[] means = new double[dimensions];
        foreach (float[] feature in rawFeatures)
        {
            for (int dimension = 0; dimension < dimensions; dimension++)
            {
                means[dimension] += feature[dimension];
            }
        }

        for (int dimension = 0; dimension < dimensions; dimension++)
        {
            means[dimension] /= rawFeatures.Length;
        }

        return rawFeatures.Select(feature => Enumerable.Range(0, dimensions)
            .Select(dimension => (float)(feature[dimension] - means[dimension]))
            .ToArray()).ToArray();
    }

    private static double EstimateBpm(IReadOnlyList<Detection> detections, int sampleRate)
    {
        if (detections.Count < 2)
        {
            return 120;
        }

        double[] intervals = detections
            .Zip(detections.Skip(1), (first, second) => (second.FrameIndex - first.FrameIndex) / (double)sampleRate)
            .Where(interval => interval >= MinimumTapDistanceMilliseconds / 1000d)
            .OrderBy(interval => interval)
            .ToArray();
        if (intervals.Length == 0)
        {
            return 120;
        }

        double median = intervals.Length % 2 == 0
            ? (intervals[(intervals.Length / 2) - 1] + intervals[intervals.Length / 2]) / 2
            : intervals[intervals.Length / 2];
        double bpm = 60 / median;
        while (bpm < 40 && bpm * 2 <= 240)
        {
            bpm *= 2;
        }

        while (bpm > 240 && bpm / 2 >= 40)
        {
            bpm /= 2;
        }

        return Math.Clamp(bpm, 40, 240);
    }

    private static int EstimateStepsPerBar(IReadOnlyList<Detection> detections, int sampleRate, double bpm)
    {
        if (detections.Count < 2)
        {
            return 8;
        }

        double shortestInterval = detections
            .Zip(detections.Skip(1), (first, second) => (second.FrameIndex - first.FrameIndex) / (double)sampleRate)
            .Where(interval => interval >= MinimumTapDistanceMilliseconds / 1000d)
            .DefaultIfEmpty(60 / bpm)
            .Min();
        double idealSteps = 240 / (bpm * shortestInterval);
        int[] supportedSteps = [2, 4, 8, 16, 32, 64, 128, 256];
        return supportedSteps.MinBy(steps => Math.Abs(Math.Log2(idealSteps / steps)));
    }
}
