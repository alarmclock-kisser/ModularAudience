using System.IO.Compression;
using System.Text.Json;
using ModularAudience.Audio;

namespace ModularAudience.Generators;

public sealed record BeatSampleSlot
{
    public string Name { get; init; } = "Sample";
    public int SampleRate { get; init; }
    public int Channels { get; init; }
    public int SampleLength { get; init; }
    public string? AudioEntry { get; init; }
    public float Volume { get; init; } = 1;
}

public sealed record BeatTrainingDocument
{
    public int Version { get; init; } = 1;
    public BeatEngineSnapshot Model { get; init; } = new();
    public string ActiveNodeId { get; init; } = "root";
    public float Bpm { get; init; } = 120;
    public float Mutation { get; init; } = 1.2f;
    public bool LoopPlayback { get; init; } = true;
    public bool LoopFeedback { get; init; }
    public List<BeatSampleSlot> Slots { get; init; } = [];
}

public sealed class LoadedBeatTraining : IDisposable
{
    public required BeatTrainingDocument Document { get; init; }
    public required AsyncBeatGraphEngine Engine { get; init; }
    public List<AudioObj> Samples { get; } = [];
    public void Dispose() { Engine.Dispose(); foreach (var sample in Samples) sample.Dispose(); }
}

/// <summary>Versioned ZIP: readable model.json plus optional lossless float PCM sample payloads.</summary>
public static class BeatTrainingArchive
{
    private const long MaximumJson = 256L * 1024 * 1024;
    private const long MaximumPcm = 512L * 1024 * 1024;

    public static async Task SaveAsync(string path, BeatTrainingDocument document, IReadOnlyList<AudioObj> samples,
        bool includeSamples, CancellationToken token = default)
    {
        ValidateDocument(document);
        using (var validation = AsyncBeatGraphEngine.Restore(document.Model)) { }
        if (includeSamples && samples.Count != document.Model.Config.SampleCount)
            throw new InvalidOperationException("Embedded audio requires the exact session sample-slot count.");
        string full = Path.GetFullPath(path);
        string temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        // Capture owned PCM copies before background IO; replacing slots cannot affect the saved data.
        var pcm = includeSamples ? samples.Select(sample => sample.Data?.ToArray() ?? throw new InvalidDataException("Sample has no audio data.")).ToArray() : [];
        if (pcm.Sum(data => (long)data.Length * sizeof(float)) > MaximumPcm) throw new InvalidDataException("Embedded audio exceeds 512 MiB.");
        var slots = document.Slots.Select((slot, index) => slot with { AudioEntry = includeSamples ? $"samples/{index:D3}.f32" : null }).ToList();
        var saved = document with { Slots = slots };
        try
        {
            await Task.Run(async () =>
            {
                using var archive = ZipFile.Open(temporary, ZipArchiveMode.Create);
                await using (var stream = archive.CreateEntry("model.json", CompressionLevel.Optimal).Open())
                    await JsonSerializer.SerializeAsync(stream, saved, cancellationToken: token);
                for (int index = 0; index < pcm.Length; index++)
                {
                    token.ThrowIfCancellationRequested();
                    if (pcm[index].Length != slots[index].SampleLength || samples[index].SampleRate != slots[index].SampleRate || samples[index].Channels != slots[index].Channels)
                        throw new InvalidDataException("Sample-slot metadata does not match the embedded audio.");
                    await using var target = archive.CreateEntry(slots[index].AudioEntry!, CompressionLevel.Fastest).Open();
                    using var writer = new BinaryWriter(target, System.Text.Encoding.UTF8, leaveOpen: true);
                    foreach (float value in pcm[index])
                    {
                        if (!float.IsFinite(value)) throw new InvalidDataException("Audio contains non-finite samples.");
                        writer.Write(value);
                    }
                }
            }, token);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, full, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static Task<LoadedBeatTraining> LoadAsync(string path, CancellationToken token = default) => Task.Run(async () =>
    {
        using var archive = ZipFile.OpenRead(path);
        if (archive.Entries.Count > 65 || archive.Entries.Select(entry => entry.FullName).Distinct(StringComparer.Ordinal).Count() != archive.Entries.Count)
            throw new InvalidDataException("Invalid or duplicated training archive entries.");
        var metadata = archive.GetEntry("model.json") ?? throw new InvalidDataException("Missing model.json.");
        if (metadata.Length > MaximumJson) throw new InvalidDataException("Training metadata exceeds 256 MiB.");
        BeatTrainingDocument document;
        await using (var stream = metadata.Open())
            document = await JsonSerializer.DeserializeAsync<BeatTrainingDocument>(stream, cancellationToken: token)
                ?? throw new InvalidDataException("Empty training archive.");
        ValidateDocument(document);
        var loaded = new LoadedBeatTraining { Document = document, Engine = AsyncBeatGraphEngine.Restore(document.Model) };
        try
        {
            long total = 0;
            bool embedded = document.Slots.Any(slot => slot.AudioEntry is not null);
            for (int index = 0; index < document.Slots.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                var slot = document.Slots[index];
                if (!embedded) continue;
                if (slot.AudioEntry != $"samples/{index:D3}.f32") throw new InvalidDataException("Invalid embedded sample entry.");
                var entry = archive.GetEntry(slot.AudioEntry) ?? throw new InvalidDataException("Missing embedded sample.");
                long length = (long)slot.SampleLength * sizeof(float);
                total += length;
                if (length != entry.Length || total > MaximumPcm) throw new InvalidDataException("Invalid embedded audio size.");
                var data = new float[slot.SampleLength];
                using (var stream = entry.Open())
                using (var reader = new BinaryReader(stream))
                    for (int sample = 0; sample < data.Length; sample++)
                    {
                        if ((sample & 65535) == 0) token.ThrowIfCancellationRequested();
                        data[sample] = reader.ReadSingle();
                        if (!float.IsFinite(data[sample])) throw new InvalidDataException("Invalid embedded PCM.");
                    }
                var audio = new AudioObj { Data = data, SampleRate = slot.SampleRate, Channels = slot.Channels,
                    Length = data.LongLength, Duration = TimeSpan.FromSeconds(data.Length / (double)slot.Channels / slot.SampleRate),
                    Volume = slot.Volume, BitDepth = 32 };
                audio.Rename(slot.Name);
                loaded.Samples.Add(audio);
            }
            return loaded;
        }
        catch { loaded.Dispose(); throw; }
    }, token);

    private static void ValidateDocument(BeatTrainingDocument document)
    {
        if (document.Version != 1 || document.Model is null) throw new InvalidDataException("Unsupported training archive version.");
        document.Model.Config.Validate();
        if (!float.IsFinite(document.Bpm) || document.Bpm is < 20 or > 300 || !float.IsFinite(document.Mutation) || document.Mutation is < 0 or > 5 ||
            document.Slots is null || document.Slots.Count != document.Model.Config.SampleCount || document.Model.Branches is null ||
            !document.Model.Branches.Any(branch => branch.Id == document.ActiveNodeId)) throw new InvalidDataException("Invalid training session settings.");
        foreach (var slot in document.Slots)
            if (string.IsNullOrWhiteSpace(slot.Name) || slot.Name.Length > 1024 || slot.SampleRate is < 8000 or > 384000 ||
                slot.Channels is < 1 or > 32 || slot.SampleLength <= 0 || slot.SampleLength % slot.Channels != 0 || !float.IsFinite(slot.Volume) || slot.Volume is < 0 or > 100)
                throw new InvalidDataException("Invalid sample-slot metadata.");
    }
}
