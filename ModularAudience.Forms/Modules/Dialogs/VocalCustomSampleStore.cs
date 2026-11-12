using ModularAudience.Audio;
using ModularAudience.Audio.Vocal;

namespace ModularAudience.Forms.Modules.Dialogs;

internal sealed record VocalCustomSampleEntrySnapshot(
    Guid Id, string IpaSubstring, Guid? AudioChoiceId, string? AudioName);

internal sealed record VocalCustomSampleCachedChoice(Guid ChoiceId, Guid EntryId, string DisplayName);

internal static class VocalCustomSampleStore
{
    private sealed class Entry(Guid id)
    {
        internal Guid Id { get; } = id;
        internal string IpaSubstring { get; set; } = string.Empty;
        internal Guid? AudioChoiceId { get; set; }
        internal AudioObj? Audio { get; set; }
    }

    private static readonly object Gate = new();
    private static readonly List<Entry> Entries = [];

    internal static Guid Add()
    {
        lock (Gate)
        {
            Guid id = Guid.NewGuid();
            Entries.Add(new Entry(id));
            return id;
        }
    }

    internal static IReadOnlyList<VocalCustomSampleEntrySnapshot> GetEntries()
    {
        lock (Gate)
        {
            return Entries.Select(entry => new VocalCustomSampleEntrySnapshot(
                entry.Id,
                entry.IpaSubstring,
                entry.AudioChoiceId,
                entry.Audio?.Name)).ToArray();
        }
    }

    internal static IReadOnlyList<VocalCustomSampleCachedChoice> GetCachedChoices()
    {
        lock (Gate)
        {
            return Entries
                .Where(entry => entry.Audio is not null && entry.AudioChoiceId.HasValue)
                .Select(entry => new VocalCustomSampleCachedChoice(
                    entry.AudioChoiceId!.Value,
                    entry.Id,
                    $"[Cached] {entry.Audio!.Name}"))
                .ToArray();
        }
    }

    internal static IReadOnlyList<VocalCustomSampleMapping> GetMappings()
    {
        lock (Gate)
        {
            return Entries
                .Where(entry => entry.Audio is not null
                    && VocalSynth.IsValidCustomSampleIpa(entry.IpaSubstring))
                .Select(entry =>
                {
                    AudioObj audio = entry.Audio!;
                    return new VocalCustomSampleMapping(
                        entry.IpaSubstring,
                        (float[])audio.Data.Clone(),
                        audio.AdjustedSampleRate,
                        audio.Channels);
                })
                .ToArray();
        }
    }

    internal static int GetActiveMappingCount()
    {
        lock (Gate)
        {
            return Entries.Count(entry => entry.Audio is not null
                && VocalSynth.IsValidCustomSampleIpa(entry.IpaSubstring));
        }
    }

    internal static void SetIpaSubstring(Guid id, string value)
    {
        lock (Gate)
            FindEntry(id).IpaSubstring = value;
    }

    internal static void SetAudio(Guid id, AudioObj source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidateAudio(source);
        AudioObj cached = source.Clone();
        AudioObj? previous;
        try
        {
            lock (Gate)
            {
                Entry entry = FindEntry(id);
                previous = entry.Audio;
                entry.Audio = cached;
                entry.AudioChoiceId = Guid.NewGuid();
            }
        }
        catch
        {
            cached.Dispose();
            throw;
        }
        previous?.Dispose();
    }

    internal static void SetAudioFromEntry(Guid targetId, Guid sourceId)
    {
        if (targetId == sourceId) return;
        AudioObj cached;
        AudioObj? previous;
        lock (Gate)
        {
            Entry source = FindEntry(sourceId);
            Entry target = FindEntry(targetId);
            if (source.Audio is null)
                throw new InvalidOperationException("The selected cached sample is no longer available.");
            ValidateAudio(source.Audio);
            cached = source.Audio.Clone();
            previous = target.Audio;
            target.Audio = cached;
            target.AudioChoiceId = Guid.NewGuid();
        }
        previous?.Dispose();
    }

    internal static void ClearAudio(Guid id)
    {
        AudioObj? previous;
        lock (Gate)
        {
            Entry entry = FindEntry(id);
            previous = entry.Audio;
            entry.Audio = null;
            entry.AudioChoiceId = null;
        }
        previous?.Dispose();
    }

    internal static void Remove(Guid id)
    {
        AudioObj? previous;
        lock (Gate)
        {
            Entry? entry = Entries.FirstOrDefault(candidate => candidate.Id == id);
            if (entry is null) return;
            Entries.Remove(entry);
            previous = entry.Audio;
        }
        previous?.Dispose();
    }

    internal static bool IsEligibleAudio(AudioObj audio)
    {
        if (audio.Channels is < 1 or > 2 || audio.AdjustedSampleRate <= 0) return false;
        float[] samples = audio.Data;
        if (samples.Length == 0 || samples.Length % audio.Channels != 0) return false;
        double durationSeconds = samples.Length / (double)(audio.Channels * audio.AdjustedSampleRate);
        return double.IsFinite(durationSeconds) && durationSeconds > 0 && durationSeconds < 60;
    }

    private static void ValidateAudio(AudioObj audio)
    {
        if (!IsEligibleAudio(audio))
            throw new ArgumentException("Custom sample audio must be valid and shorter than one minute.", nameof(audio));
    }

    private static Entry FindEntry(Guid id) =>
        Entries.FirstOrDefault(entry => entry.Id == id)
        ?? throw new InvalidOperationException("The custom sample mapping no longer exists.");
}
