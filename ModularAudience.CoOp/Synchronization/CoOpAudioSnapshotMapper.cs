using System.Runtime.InteropServices;
using ModularAudience.Audio;
using ModularAudience.CoOp.Protocol;

namespace ModularAudience.CoOp.Synchronization;

/// <summary>
/// Converts between live <see cref="AudioObj"/> instances and the immutable
/// wire snapshots. Sample data is transported as raw little-endian f32 bytes.
/// Received samples are materialized directly in RAM; FilePath is metadata
/// only and is never loaded, created, or used as an audio cache here.
/// </summary>
public static class CoOpAudioSnapshotMapper
{
    public static CoOpAudioSnapshot Capture(AudioObj audio, long revision = 0)
    {
        ArgumentNullException.ThrowIfNull(audio);
        float[] data = audio.Data ?? [];
        return new CoOpAudioSnapshot
        {
            AudioId = audio.Id,
            Revision = revision,
            Name = audio.Name ?? string.Empty,
            OriginalName = audio.OriginalName ?? string.Empty,
            FilePath = audio.FilePath ?? string.Empty,
            SampleData = MemoryMarshal.AsBytes(data.AsSpan()).ToArray(),
            SampleRate = audio.SampleRate,
            Channels = audio.Channels,
            BitDepth = audio.BitDepth,
            Length = audio.Length,
            Duration = audio.Duration,
            Bpm = audio.Bpm,
            ScannedBpm = audio.ScannedBpm,
            Timing = audio.Timing,
            ScannedTiming = audio.ScannedTiming,
            Key = audio.Key ?? string.Empty,
            ScannedKey = audio.ScannedKey ?? string.Empty,
            Volume = audio.Volume,
            SampleTag = audio.SampleTag ?? string.Empty,
            SampleRateFactor = audio.SampleRateFactor,
            ManualSampleRateFactor = audio.ManualSampleRateFactor,
            SyncNudgeSampleRateFactor = audio.SyncNudgeSampleRateFactor,
            StretchFactor = audio.StretchFactor,
            DrawBeatGrid = audio.DrawBeatGrid,
            BeatGrid = audio.BeatGrid?.ToArray() ?? [],
            Metrics = audio.Metrics.ToDictionary(pair => pair.Key, pair => pair.Value)
        };
    }

    public static AudioObj Create(CoOpAudioSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var audio = new AudioObj
        {
            Id = snapshot.AudioId,
            Name = snapshot.Name ?? string.Empty,
            OriginalName = snapshot.OriginalName ?? string.Empty,
            FilePath = snapshot.FilePath ?? string.Empty,
            Data = ToSamples(snapshot.SampleData),
            SampleRate = snapshot.SampleRate,
            Channels = snapshot.Channels,
            BitDepth = snapshot.BitDepth,
            Length = snapshot.Length,
            Duration = snapshot.Duration,
            Bpm = snapshot.Bpm,
            ScannedBpm = snapshot.ScannedBpm,
            Timing = snapshot.Timing,
            ScannedTiming = snapshot.ScannedTiming,
            Key = snapshot.Key ?? string.Empty,
            ScannedKey = snapshot.ScannedKey ?? string.Empty,
            Volume = snapshot.Volume,
            SampleTag = snapshot.SampleTag ?? string.Empty,
            SampleRateFactor = snapshot.SampleRateFactor,
            ManualSampleRateFactor = snapshot.ManualSampleRateFactor,
            SyncNudgeSampleRateFactor = snapshot.SyncNudgeSampleRateFactor,
            StretchFactor = snapshot.StretchFactor,
            DrawBeatGrid = snapshot.DrawBeatGrid,
            BeatGrid = snapshot.BeatGrid?.ToArray() ?? []
        };

        foreach (KeyValuePair<string, double> metric in snapshot.Metrics ?? [])
        {
            audio.Metrics[metric.Key] = metric.Value;
        }

        return audio;
    }

    public static void ApplyTo(AudioObj target, CoOpAudioSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(snapshot);

        AudioObj replacement = Create(snapshot);
        try
        {
            target.ReplaceWith(replacement);
            target.Name = snapshot.Name ?? string.Empty;
            target.OriginalName = snapshot.OriginalName ?? string.Empty;
            target.FilePath = snapshot.FilePath ?? string.Empty;
            target.SampleRateFactor = snapshot.SampleRateFactor;
            target.ManualSampleRateFactor = snapshot.ManualSampleRateFactor;
            target.SyncNudgeSampleRateFactor = snapshot.SyncNudgeSampleRateFactor;
            target.StretchFactor = snapshot.StretchFactor;
            target.DrawBeatGrid = snapshot.DrawBeatGrid;
            target.BeatGrid = snapshot.BeatGrid?.ToArray() ?? [];
        }
        finally
        {
            try { replacement.Dispose(); } catch { }
        }
    }

    public static float[] ToSamples(byte[]? bytes)
    {
        if (bytes == null || bytes.Length == 0)
        {
            return [];
        }

        if (bytes.Length % sizeof(float) != 0)
        {
            throw new CoOpProtocolException("The audio sample payload is not aligned to 32-bit floats.");
        }

        return MemoryMarshal.Cast<byte, float>(bytes.AsSpan()).ToArray();
    }
}
