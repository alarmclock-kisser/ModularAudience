using ModularAudience.Audio.Arrangement;
using ModularAudience.Audio.Processors_V4;

namespace ModularAudience.Audio.Processing;

public enum AudioDecompileAtomizerKind
{
    TransientV4,
    LegacyV1
}

public enum AudioDecompileProjectDetail
{
    Transients,
    Beats,
    Bars
}

[Flags]
public enum AudioDecompileOutputKind
{
    Project = 1,
    Collections = 2
}

public enum AudioDecompileOverlapPolicy
{
    Allow,
    AdditionalLanes
}

public sealed record AudioDecompileOptions
{
    public AudioDecompileAtomizerKind Atomizer { get; init; } = AudioDecompileAtomizerKind.TransientV4;
    public LoopAtomizerSettings V4Settings { get; init; } = LoopAtomizerSettings.Default;
    public float? LegacySimilarityThreshold { get; init; }
    public AudioDecompileOutputKind Output { get; init; } = AudioDecompileOutputKind.Project;
    public AudioDecompileProjectDetail ProjectDetail { get; init; } = AudioDecompileProjectDetail.Transients;
    public AudioDecompileOverlapPolicy ProjectOverlap { get; init; } = AudioDecompileOverlapPolicy.Allow;
    public int ProjectAttackMs { get; init; } = 1;
    public bool DeduplicateCollections { get; init; } = true;
    public int MaxWorkers { get; init; } = Math.Max(1, Environment.ProcessorCount / 2);
    public int BeatsPerBar { get; init; } = 4;

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(this.V4Settings);
        if (this.MaxWorkers is < 1 || !Enum.IsDefined(this.Atomizer)
            || this.Output == 0
            || (this.Output & ~(AudioDecompileOutputKind.Project | AudioDecompileOutputKind.Collections)) != 0
            || !Enum.IsDefined(this.ProjectDetail)
            || !Enum.IsDefined(this.ProjectOverlap))
        {
            throw new ArgumentException("The audio decompile options are invalid.");
        }

        if (this.MaxWorkers > Environment.ProcessorCount)
        {
            throw new ArgumentOutOfRangeException(nameof(this.MaxWorkers), "The worker limit cannot exceed Environment.ProcessorCount.");
        }

        if (this.BeatsPerBar is < 1 or > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(this.BeatsPerBar));
        }

        if (this.ProjectAttackMs is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(this.ProjectAttackMs));
        }

        if (this.LegacySimilarityThreshold is float threshold
            && (!float.IsFinite(threshold) || threshold is < 0.6f or > 0.98f))
        {
            throw new ArgumentOutOfRangeException(nameof(this.LegacySimilarityThreshold));
        }
    }
}

public sealed record AudioStemSeparationProgress(double Fraction, string Detail)
{
    public AudioStemSeparationProgress(double fraction)
        : this(fraction, string.Empty)
    {
    }
}

public sealed record AudioDecompileStem(string Name, AudioObj Audio);

public sealed record AudioStemSeparationResult(
    IReadOnlyList<AudioDecompileStem> Stems,
    IReadOnlyList<string> Warnings)
{
    public void Dispose()
    {
        foreach (AudioDecompileStem stem in this.Stems)
        {
            stem.Audio.Dispose();
        }
    }
}

public interface IAudioStemSeparator
{
    string DisplayName { get; }
    int EstimatedStemCount { get; }

    Task<AudioStemSeparationResult> SeparateAsync(
        AudioObj source,
        IProgress<AudioStemSeparationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public static class AudioDecompileAudio
{
    public static AudioObj CreateStem(
        AudioObj source,
        float[] data,
        string name,
        int? channels = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(data);
        int outputChannels = channels ?? source.Channels;
        if (outputChannels <= 0 || data.Length % outputChannels != 0)
        {
            throw new ArgumentException("Derived stem audio has an invalid sample format.", nameof(data));
        }

        var stem = new AudioObj
        {
            Name = name,
            OriginalName = name,
            FilePath = source.FilePath,
            Data = data,
            SampleRate = source.SampleRate,
            SampleRateFactor = source.SampleRateFactor,
            ManualSampleRateFactor = source.ManualSampleRateFactor,
            SyncNudgeSampleRateFactor = source.SyncNudgeSampleRateFactor,
            Channels = outputChannels,
            BitDepth = source.BitDepth,
            Length = data.LongLength,
            Duration = TimeSpan.FromSeconds(
                data.LongLength / (double)(Math.Max(1, source.SampleRate) * outputChannels)),
            Bpm = source.Bpm,
            ScannedBpm = source.ScannedBpm,
            Timing = source.Timing,
            ScannedTiming = source.ScannedTiming,
            Key = source.Key,
            ScannedKey = source.ScannedKey,
            Volume = source.Volume,
            StretchFactor = source.StretchFactor,
            ChunkSize = source.ChunkSize,
            OverlapSize = source.OverlapSize,
            SampleTag = source.SampleTag,
            DrawBeatGrid = source.DrawBeatGrid,
            BeatGrid = (bool[])source.BeatGrid.Clone()
        };
        foreach (KeyValuePair<string, double> metric in source.Metrics)
        {
            stem.Metrics[metric.Key] = metric.Value;
        }

        foreach (KeyValuePair<string, string> tag in source.CustomTags.Values)
        {
            stem.CustomTags[tag.Key] = tag.Value;
        }

        return stem;
    }
}

public sealed record AudioDecompileProgress(double Fraction, string Stage, string? Detail = null);

public sealed record AudioDecompileCollection(string StemName, IReadOnlyList<AudioObj> Atomics);

public sealed record AudioDecompileStemSummary(string StemName, int AtomicCount);

public sealed class AudioDecompileResult : IDisposable
{
    private List<AudioDecompileCollection> collections;
    private MixArrangement? project;

    internal AudioDecompileResult(
        string sourceName,
        float bpm,
        string bpmSource,
        IReadOnlyList<AudioDecompileStemSummary> stems,
        IReadOnlyList<AudioDecompileCollection> collections,
        MixArrangement? project,
        IReadOnlyList<string> warnings)
    {
        this.SourceName = sourceName;
        this.Bpm = bpm;
        this.BpmSource = bpmSource;
        this.Stems = stems.ToArray();
        this.collections = collections.ToList();
        this.project = project;
        this.Warnings = warnings.ToArray();
    }

    public string SourceName { get; }
    public float Bpm { get; }
    public string BpmSource { get; }
    public IReadOnlyList<AudioDecompileStemSummary> Stems { get; }
    public IReadOnlyList<string> Warnings { get; }
    public bool HasProject => this.project is not null;
    public bool HasCollections => this.collections.Count > 0;

    public MixArrangement? TakeProject()
    {
        MixArrangement? result = this.project;
        this.project = null;
        return result;
    }

    public IReadOnlyList<AudioDecompileCollection> TakeCollections()
    {
        AudioDecompileCollection[] result = this.collections.ToArray();
        this.collections.Clear();
        return result;
    }

    public void Dispose()
    {
        this.project?.Dispose();
        this.project = null;
        foreach (AudioDecompileCollection collection in this.collections)
        {
            foreach (AudioObj atomic in collection.Atomics)
            {
                atomic.Dispose();
            }
        }

        this.collections.Clear();
        GC.SuppressFinalize(this);
    }
}
