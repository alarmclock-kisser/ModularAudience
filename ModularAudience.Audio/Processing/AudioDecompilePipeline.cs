using ModularAudience.Audio.Arrangement;
using ModularAudience.Audio.Processors_V1;
using ModularAudience.Audio.Processors_V4;

namespace ModularAudience.Audio.Processing;

public static class AudioDecompilePipeline
{
    private const double SeparationStart = 0.05;
    private const double SeparationEnd = 0.85;
    private const double AtomizeStart = SeparationEnd;
    private const double AtomizeEnd = 0.985;
    private const double OutputEnd = 1.0;

    public static async Task<AudioDecompileResult> RunAsync(
        AudioObj source,
        IAudioStemSeparator separator,
        AudioDecompileOptions options,
        IProgress<AudioDecompileProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(separator);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        ValidateSource(source);
        cancellationToken.ThrowIfCancellationRequested();
        Report(progress, 0.0, "Preparing source");
        AudioDecompileMemoryGuard.Validate(source, separator.EstimatedStemCount);

        (float bpm, string bpmSource) = await ResolveBpmAsync(source, progress, cancellationToken).ConfigureAwait(false);
        AudioStemSeparationResult separated;
        var separationProgress = progress == null
            ? null
            : new Progress<AudioStemSeparationProgress>(update =>
            {
                double fraction = SeparationStart + Math.Clamp(update.Fraction, 0, 1) * (SeparationEnd - SeparationStart);
                Report(progress, fraction, $"{separator.DisplayName}: separating", update.Detail);
            });

        try
        {
            separated = await separator.SeparateAsync(source, separationProgress, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }

        List<AudioDecompileStem> stems = separated.Stems.ToList();
        List<string> warnings = separated.Warnings.ToList();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateStems(stems);
            foreach (AudioDecompileStem stem in stems)
            {
                stem.Audio.Bpm = bpm;
                if (stem.Audio.ScannedBpm <= 0)
                {
                    stem.Audio.ScannedBpm = bpm;
                }
            }

            StemPlan[] plans = await BuildStemPlansAsync(
                stems, options, bpm, progress, cancellationToken).ConfigureAwait(false);
            AudioDecompileResult result = await BuildResultAsync(
                source,
                bpm,
                bpmSource,
                plans,
                warnings,
                options,
                progress,
                cancellationToken).ConfigureAwait(false);

            try
            {
                Report(progress, 1.0, "Decompile complete");
                return result;
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }
        catch
        {
            foreach (AudioDecompileStem stem in stems)
            {
                stem.Audio.Dispose();
            }

            throw;
        }
    }

    public static async Task<AudioDecompileResult> RunAsync(
        AudioObj source,
        IAudioStemSeparator separator,
        AudioDecompileOptions options,
        CancellationToken cancellationToken)
    {
        return await RunAsync(source, separator, options, progress: null, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<StemPlan[]> BuildStemPlansAsync(
        IReadOnlyList<AudioDecompileStem> stems,
        AudioDecompileOptions options,
        float bpm,
        IProgress<AudioDecompileProgress>? progress,
        CancellationToken cancellationToken)
    {
        bool useFixedChunks = options.Output == AudioDecompileOutputKind.Project
            && options.ProjectDetail != AudioDecompileProjectDetail.Transients;
        if (useFixedChunks)
        {
            double chunkBeats = options.ProjectDetail == AudioDecompileProjectDetail.Bars
                ? options.BeatsPerBar
                : 1.0;
            double chunkSeconds = chunkBeats * 60.0 / bpm;
            return stems.Select((stem, index) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                Report(progress, AtomizeStart + (index + 1.0) / stems.Count * (AtomizeEnd - AtomizeStart),
                    $"Creating {options.ProjectDetail.ToString().ToLowerInvariant()} on stem {index + 1}/{stems.Count}",
                    stem.Name);
                return new StemPlan(stem, ChunkStem(stem.Audio, chunkSeconds));
            }).ToArray();
        }

        int stemWorkers = Math.Min(options.MaxWorkers, stems.Count);
        int atomizerWorkers = Math.Max(1, options.MaxWorkers / Math.Max(1, stemWorkers));
        double[] fractions = new double[stems.Count];
        var fractionGate = new object();
        StemPlan[] plans = new StemPlan[stems.Count];
        var parallelOptions = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Math.Max(1, stemWorkers)
        };

        try
        {
            await Parallel.ForEachAsync(
                Enumerable.Range(0, stems.Count),
                parallelOptions,
                async (index, token) =>
                {
                    AudioDecompileStem stem = stems[index];
                    IProgress<double>? atomizeProgress = progress == null
                        ? null
                        : new Progress<double>(fraction =>
                        {
                            lock (fractionGate)
                            {
                                fractions[index] = Math.Clamp(fraction, 0, 1);
                                double average = fractions.Average();
                                Report(progress,
                                    AtomizeStart + average * (AtomizeEnd - AtomizeStart),
                                    $"Atomizing: stem {index + 1}/{stems.Count}",
                                    stem.Name);
                            }
                        });

                    List<AudioDecompileAtomicPlan> atomics = await AtomizeStemAsync(
                        stem.Audio, options, atomizerWorkers, atomizeProgress, token).ConfigureAwait(false);
                    plans[index] = new StemPlan(stem, atomics);
                    fractions[index] = 1.0;
                }).ConfigureAwait(false);
        }
        catch
        {
            foreach (StemPlan? plan in plans)
            {
                if (plan == null)
                {
                    continue;
                }

                DisposePlanAtomics(plan);
            }

            throw;
        }

        return plans;
    }

    private static async Task<List<AudioDecompileAtomicPlan>> AtomizeStemAsync(
        AudioObj stem,
        AudioDecompileOptions options,
        int maxWorkers,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<AudioObj> atomics;
        if (options.Atomizer == AudioDecompileAtomizerKind.LegacyV1)
        {
            progress?.Report(0.0);
            atomics = (await LoopAtomizer.AtomizeLoopAsync(
                stem,
                options.LegacySimilarityThreshold,
                cancellationToken,
                deduplicate: false).ConfigureAwait(false)).ToArray();
            progress?.Report(1.0);
        }
        else
        {
            LoopAtomizerSettings settings = options.V4Settings with
            {
                EnableDeduplication = false,
                MaxWorkers = maxWorkers
            };
            AudioAtomizeResult result = await AudioAtomizerWorkflow
                .AtomizeAsync(stem, settings, progress, cancellationToken)
                .ConfigureAwait(false);
            atomics = result.Atomics;
        }

        List<AudioDecompileAtomicPlan> plans = [];
        try
        {
            foreach (AudioObj atomic in atomics)
            {
                atomic.Bpm = stem.Bpm;
                if (!AudioAtomicTiming.TryGet(atomic, out long startFrame, out long frameCount))
                {
                    if (options.Output.HasFlag(AudioDecompileOutputKind.Project)
                        && options.ProjectDetail == AudioDecompileProjectDetail.Transients)
                    {
                        throw new InvalidDataException(
                            $"The atomizer did not return source timing for '{atomic.Name}'. Project output requires exact source positions.");
                    }

                    startFrame = 0;
                    frameCount = atomic.Data.LongLength / Math.Max(1, atomic.Channels);
                }

                plans.Add(new AudioDecompileAtomicPlan(
                    atomic.Name,
                    startFrame,
                    frameCount,
                    atomic,
                    atomic.SampleTag));
            }
        }
        catch
        {
            foreach (AudioObj atomic in atomics) atomic.Dispose();
            throw;
        }

        return plans;
    }

    private static List<AudioDecompileAtomicPlan> ChunkStem(AudioObj stem, double chunkSeconds)
    {
        if (!double.IsFinite(chunkSeconds) || chunkSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSeconds));
        }

        long totalFrames = stem.Data.LongLength / Math.Max(1, stem.Channels);
        long framesPerChunk = Math.Max(1, (long)Math.Round(chunkSeconds * stem.SampleRate));
        List<AudioDecompileAtomicPlan> result = [];
        for (long frame = 0, index = 1; frame < totalFrames; frame += framesPerChunk, index++)
        {
            long count = Math.Min(framesPerChunk, totalFrames - frame);
            result.Add(new AudioDecompileAtomicPlan(
                $"{stem.Name}_Segment{index:D3}",
                frame,
                count,
                null,
                null));
        }

        return result;
    }

    private static async Task<AudioDecompileResult> BuildResultAsync(
        AudioObj source,
        float bpm,
        string bpmSource,
        IReadOnlyList<StemPlan> plans,
        IReadOnlyList<string> warnings,
        AudioDecompileOptions options,
        IProgress<AudioDecompileProgress>? progress,
        CancellationToken cancellationToken)
    {
        bool createProject = options.Output.HasFlag(AudioDecompileOutputKind.Project);
        bool createCollections = options.Output.HasFlag(AudioDecompileOutputKind.Collections);
        MixArrangement? project = null;
        IReadOnlyList<AudioDecompileCollection> collections = [];
        int[] projectCounts = new int[plans.Count];
        List<string> effectiveWarnings = warnings.ToList();
        try
        {
            if (createProject)
            {
                project = BuildProjectArrangement(
                    source,
                    bpm,
                    plans,
                    effectiveWarnings,
                    projectCounts,
                    options,
                    retainAtomics: createCollections);
                Report(progress, 0.995, "Building project");
            }

            if (createCollections)
            {
                collections = await BuildCollectionsAsync(
                    plans,
                    options,
                    progress,
                    cancellationToken).ConfigureAwait(false);
            }

            AudioDecompileStemSummary[] summaries = plans.Select((plan, index) =>
                new AudioDecompileStemSummary(
                    plan.Stem.Name,
                    createCollections ? collections[index].Atomics.Count : projectCounts[index])).ToArray();
            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, OutputEnd, "Opening output");
            return new AudioDecompileResult(
                GetSourceName(source),
                bpm,
                bpmSource,
                summaries,
                collections,
                project,
                effectiveWarnings);
        }
        catch
        {
            project?.Dispose();
            foreach (AudioDecompileCollection collection in collections)
            {
                foreach (AudioObj atomic in collection.Atomics)
                {
                    atomic.Dispose();
                }
            }

            DisposePlans(plans);
            throw;
        }
    }

    private static MixArrangement BuildProjectArrangement(
        AudioObj source,
        float bpm,
        IReadOnlyList<StemPlan> plans,
        List<string> warnings,
        int[] projectCounts,
        AudioDecompileOptions options,
        bool retainAtomics)
    {
        var arrangement = new MixArrangement
        {
            Name = $"{GetSourceName(source)} - Decompiled",
            Bpm = bpm,
            Snap = options.SnapToGrid,
            SnapDivision = options.SnapDivision,
            OverlapMode = MixOverlapMode.MixOverlap,
            LoopEnd = source.Duration.TotalSeconds > 0
                ? source.Duration.TotalSeconds
                : source.Data.LongLength / (double)(Math.Max(1, source.Channels) * Math.Max(1, source.SampleRate))
        };
        try
        {
            for (int index = 0; index < plans.Count; index++)
            {
                StemPlan plan = plans[index];
                AudioObj stem = plan.Stem.Audio;
                long totalFrames = stem.Data.LongLength / Math.Max(1, stem.Channels);
                MixSource projectSource = arrangement.AddSource(stem);
                IReadOnlyList<AudioDecompileAtomicPlan> atomics = options.ProjectDetail == AudioDecompileProjectDetail.Transients
                    ? plan.Atomics
                    : ChunkStem(stem, GetProjectChunkSeconds(options, bpm));
                if (atomics.Count == 0)
                {
                    atomics = [new AudioDecompileAtomicPlan($"{stem.Name}_Full", 0, totalFrames, null, null)];
                    warnings.Add($"No atomic boundaries were found for '{stem.Name}'; the complete stem was placed as one clip.");
                }

                int[] laneAssignments = options.ProjectOverlap == AudioDecompileOverlapPolicy.AdditionalLanes
                    ? AssignAdditionalLanes(atomics)
                    : new int[atomics.Count];
                int laneCount = laneAssignments.Length == 0
                    ? 1
                    : laneAssignments.Max(laneIndex => laneIndex) + 1;
                MixLane[] stemLanes = Enumerable.Range(0, laneCount)
                    .Select(laneIndex => new MixLane
                    {
                        Name = laneIndex == 0
                            ? plan.Stem.Name
                            : $"{plan.Stem.Name} - overlap {laneIndex + 1}"
                    })
                    .ToArray();
                arrangement.Lanes.AddRange(stemLanes);

                for (int atomicIndex = 0; atomicIndex < atomics.Count; atomicIndex++)
                {
                    AudioDecompileAtomicPlan atomic = atomics[atomicIndex];
                    if (atomic.SourceStartFrame > totalFrames
                        || atomic.SourceFrameCount <= 0
                        || atomic.SourceFrameCount > totalFrames - atomic.SourceStartFrame)
                    {
                        throw new InvalidDataException(
                            $"Atomic '{atomic.Name}' has a source range outside stem '{stem.Name}'.");
                    }

                    double offset = atomic.SourceStartFrame / (double)stem.SampleRate;
                    double duration = atomic.SourceFrameCount / (double)stem.SampleRate;
                    MixClip clip = arrangement.AddClip(
                        stemLanes[laneAssignments[atomicIndex]],
                        projectSource,
                        arrangement.SnapTime(offset),
                        offset,
                        duration);
                    clip.Name = atomic.Name;
                    clip.FadeIn = Math.Min(options.ProjectAttackMs / 1000.0, duration);
                }

                projectCounts[index] = atomics.Count;
                if (!retainAtomics)
                {
                    DisposePlanAtomics(plan);
                }

                stem.Dispose();
            }

            arrangement.Validate();
            return arrangement;
        }
        catch
        {
            arrangement.Dispose();
            throw;
        }
    }

    internal static int[] AssignAdditionalLanes(IReadOnlyList<AudioDecompileAtomicPlan> atomics)
    {
        ArgumentNullException.ThrowIfNull(atomics);
        var orderedIndices = Enumerable.Range(0, atomics.Count)
            .OrderBy(index => atomics[index].SourceStartFrame)
            .ThenBy(index => atomics[index].SourceFrameCount)
            .ToArray();
        var laneEndFrames = new List<long>();
        var laneSignatures = new List<List<AtomicToneSignature>>();
        int[] assignments = new int[atomics.Count];
        foreach (int atomicIndex in orderedIndices)
        {
            AudioDecompileAtomicPlan atomic = atomics[atomicIndex];
            if (atomic.SourceStartFrame < 0 || atomic.SourceFrameCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(atomics), "Invalid atomic source range.");
            }

            long endFrame = checked(atomic.SourceStartFrame + atomic.SourceFrameCount);
            int[] availableLanes = laneEndFrames
                .Select((lastEndFrame, laneIndex) => (lastEndFrame, laneIndex))
                .Where(item => item.lastEndFrame <= atomic.SourceStartFrame)
                .Select(item => item.laneIndex)
                .ToArray();
            AtomicToneSignature? signature = DescribeTone(atomic);
            int laneIndex;
            if (availableLanes.Length == 0)
            {
                laneIndex = laneEndFrames.Count;
                laneEndFrames.Add(endFrame);
                laneSignatures.Add(signature is null ? [] : [signature]);
            }
            else
            {
                laneIndex = signature is null
                    ? availableLanes[0]
                    : availableLanes
                        .OrderBy(candidate => LaneToneDistance(laneSignatures[candidate], signature))
                        .ThenBy(candidate => candidate)
                        .First();
                laneEndFrames[laneIndex] = endFrame;
                if (signature is not null)
                {
                    laneSignatures[laneIndex].Add(signature);
                }
            }

            assignments[atomicIndex] = laneIndex;
        }

        return assignments;
    }

    private static AtomicToneSignature? DescribeTone(AudioDecompileAtomicPlan atomic)
    {
        if (atomic.Audio is null)
        {
            return null;
        }

        AtomicToneSignature signature = AtomicSampleDeduplicator.DescribeTone(atomic.Audio);
        return string.IsNullOrWhiteSpace(signature.SampleTag) && signature.DominantFrequencyHz <= 0
            ? null
            : signature;
    }

    private static double LaneToneDistance(
        IReadOnlyList<AtomicToneSignature> laneSignatures,
        AtomicToneSignature candidate)
    {
        if (laneSignatures.Count == 0)
        {
            return double.MaxValue;
        }

        double best = double.MaxValue;
        foreach (AtomicToneSignature existing in laneSignatures)
        {
            double distance = ToneDistance(existing, candidate);
            best = Math.Min(best, distance);
        }

        return best;
    }

    private static double ToneDistance(AtomicToneSignature left, AtomicToneSignature right)
    {
        bool leftTagged = !string.IsNullOrWhiteSpace(left.SampleTag);
        bool rightTagged = !string.IsNullOrWhiteSpace(right.SampleTag);
        if (leftTagged && rightTagged)
        {
            return string.Equals(left.SampleTag, right.SampleTag, StringComparison.OrdinalIgnoreCase)
                ? 0
                : 4;
        }

        double peakDistance = RelativeFrequencyDistance(
            left.DominantFrequencyHz,
            right.DominantFrequencyHz);
        double centroidDistance = RelativeFrequencyDistance(
            left.SpectralCentroidHz,
            right.SpectralCentroidHz);
        return (peakDistance * 0.7) + (centroidDistance * 0.3);
    }

    private static double RelativeFrequencyDistance(double left, double right)
    {
        if (left <= 0 || right <= 0 || !double.IsFinite(left) || !double.IsFinite(right))
        {
            return 8;
        }

        return Math.Abs(Math.Log2(left / right));
    }

    private static async Task<IReadOnlyList<AudioDecompileCollection>> BuildCollectionsAsync(
        IReadOnlyList<StemPlan> plans,
        AudioDecompileOptions options,
        IProgress<AudioDecompileProgress>? progress,
        CancellationToken cancellationToken)
    {
        List<AudioDecompileCollection> collections = [];
        try
        {
            for (int index = 0; index < plans.Count; index++)
            {
                StemPlan plan = plans[index];
                cancellationToken.ThrowIfCancellationRequested();
                AudioObj[] atomics = plan.Atomics
                    .Select(atomic => atomic.Audio ?? throw new InvalidDataException(
                        "Collection output requires materialized atomic audio."))
                    .ToArray();
                if (options.DeduplicateCollections && atomics.Length > 1)
                {
                    LoopAtomizerSettings settings = options.V4Settings with
                    {
                        EnableDeduplication = true,
                        MaxWorkers = options.MaxWorkers
                    };
                    atomics = await Task.Run(() => AtomicSampleDeduplicator.Deduplicate(
                        atomics.ToList(),
                        settings.ClusterSimilarityThreshold,
                        progress: null,
                        settings.MaxVariantsPerCluster,
                        settings.MaxWorkers,
                        cancellationToken).ToArray(), cancellationToken).ConfigureAwait(false);
                }

                collections.Add(new AudioDecompileCollection(plan.Stem.Name, atomics));
                if (!plan.Stem.Audio.IsDisposed)
                {
                    plan.Stem.Audio.Dispose();
                }

                Report(progress, AtomizeEnd + 0.012 * ((index + 1.0) / plans.Count),
                    $"Building collections: stem {index + 1}/{plans.Count}",
                    plan.Stem.Name);
            }

            return collections;
        }
        catch
        {
            foreach (AudioDecompileCollection collection in collections)
            {
                foreach (AudioObj atomic in collection.Atomics)
                {
                    atomic.Dispose();
                }
            }

            throw;
        }
    }

    private static double GetProjectChunkSeconds(AudioDecompileOptions options, float bpm)
    {
        double beats = options.ProjectDetail == AudioDecompileProjectDetail.Bars
            ? options.BeatsPerBar
            : 1.0;
        return beats * 60.0 / bpm;
    }

    private static async Task<(float Bpm, string Source)> ResolveBpmAsync(
        AudioObj source,
        IProgress<AudioDecompileProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (IsPlausibleBpm(source.Bpm))
        {
            Report(progress, SeparationStart * 0.5, "Resolving BPM", "Using tagged BPM");
            return (source.Bpm, "audio tag");
        }

        if (IsPlausibleBpm(source.ScannedBpm))
        {
            Report(progress, SeparationStart * 0.5, "Resolving BPM", "Using scanned BPM");
            return (source.ScannedBpm, "previous scan");
        }

        Report(progress, SeparationStart * 0.5, "Resolving BPM", "Estimating BPM with BeatScanner");
        double estimated = await BeatScanner.ScanBpmAsync(source).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!double.IsFinite(estimated) || estimated is < 10 or > 400)
        {
            throw new InvalidDataException("BPM could not be estimated from the source audio.");
        }

        float bpm = (float)estimated;
        source.ScannedBpm = bpm;
        return (bpm, "estimated");
    }

    private static void ValidateSource(AudioObj source)
    {
        if (source.IsDisposed || source.Data.Length == 0 || source.SampleRate <= 0 || source.Channels <= 0
            || source.Data.LongLength % source.Channels != 0
            || source.Data.Any(sample => !float.IsFinite(sample)))
        {
            throw new ArgumentException("The decompile source is not valid finite audio.", nameof(source));
        }
    }

    private static void ValidateStems(IReadOnlyList<AudioDecompileStem> stems)
    {
        if (stems.Count == 0)
        {
            throw new InvalidDataException("Source separation produced no stems.");
        }

        foreach (AudioDecompileStem stem in stems)
        {
            ValidateSource(stem.Audio);
            if (string.IsNullOrWhiteSpace(stem.Name))
            {
                throw new InvalidDataException("A separated stem has no name.");
            }
        }
    }

    private static bool IsPlausibleBpm(float value) => float.IsFinite(value) && value is >= 10 and <= 400;

    private static string GetSourceName(AudioObj source) =>
        string.IsNullOrWhiteSpace(source.Name) ? "Audio" : source.Name.Trim();

    private static void DisposePlanAtomics(StemPlan plan)
    {
        foreach (AudioDecompileAtomicPlan atomic in plan.Atomics)
        {
            atomic.Audio?.Dispose();
        }
    }

    private static void DisposePlans(IEnumerable<StemPlan> plans)
    {
        foreach (StemPlan plan in plans)
        {
            DisposePlanAtomics(plan);
            if (!plan.Stem.Audio.IsDisposed)
            {
                plan.Stem.Audio.Dispose();
            }
        }
    }

    private static void Report(
        IProgress<AudioDecompileProgress>? progress,
        double fraction,
        string stage,
        string? detail = null)
    {
        progress?.Report(new AudioDecompileProgress(Math.Clamp(fraction, 0, 1), stage, detail));
    }

    private sealed record StemPlan(AudioDecompileStem Stem, IReadOnlyList<AudioDecompileAtomicPlan> Atomics);

    public sealed record AudioDecompileAtomicPlan(
        string Name,
        long SourceStartFrame,
        long SourceFrameCount,
        AudioObj? Audio,
        string? SampleTag);
}

internal static class AudioDecompileMemoryGuard
{
    public static void Validate(AudioObj source, int estimatedStemCount)
    {
        int stems = Math.Max(1, estimatedStemCount);
        const int transientCopies = 2;
        long sourceBytes = checked(source.Data.LongLength * sizeof(float));
        long estimatedBytes = checked(sourceBytes * (1L + stems * transientCopies));
        GCMemoryInfo memory = GC.GetGCMemoryInfo();
        long available = memory.TotalAvailableMemoryBytes > 0
            ? (long)(memory.TotalAvailableMemoryBytes * 0.75)
            : long.MaxValue;
        if (estimatedBytes > available)
        {
            throw new InsufficientMemoryException(
                $"The decompile working set is estimated at {FormatBytes(estimatedBytes)}, but the safe managed-memory budget is {FormatBytes(available)}. Use a shorter source or fewer estimated stems.");
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return FormattableString.Invariant($"{value:F1} {units[unit]}");
    }
}
