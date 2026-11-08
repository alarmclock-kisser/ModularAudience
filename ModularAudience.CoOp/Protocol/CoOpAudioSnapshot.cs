namespace ModularAudience.CoOp.Protocol;

/// <summary>
/// Serialized state of a single audio object. Samples are stored as raw
/// little-endian IEEE-754 floats and become base64 on the JSON wire format.
/// </summary>
public sealed record CoOpAudioSnapshot
{
    public Guid AudioId { get; init; }
    public long Revision { get; init; }
    public string Name { get; init; } = string.Empty;
    public string OriginalName { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;

    public byte[] SampleData { get; init; } = [];
    public int SampleRate { get; init; }
    public int Channels { get; init; }
    public int BitDepth { get; init; }
    public long Length { get; init; }
    public TimeSpan Duration { get; init; }

    public float Bpm { get; init; }
    public float ScannedBpm { get; init; }
    public float Timing { get; init; } = 1.0f;
    public float ScannedTiming { get; init; } = 1.0f;
    public string Key { get; init; } = string.Empty;
    public string ScannedKey { get; init; } = string.Empty;
    public float Volume { get; init; } = 1.0f;
    public string SampleTag { get; init; } = string.Empty;

    public double SampleRateFactor { get; init; } = 1.0;
    public double ManualSampleRateFactor { get; init; } = 1.0;
    public double SyncNudgeSampleRateFactor { get; init; } = 1.0;
    public double StretchFactor { get; init; } = 1.0;

    public bool DrawBeatGrid { get; init; }
    public bool[] BeatGrid { get; init; } = [];
    public Dictionary<string, double> Metrics { get; init; } = [];
}
