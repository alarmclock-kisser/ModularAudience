using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace ModularAudience.Audio.Arrangement;

/// <summary>Single-file journal: immutable PCM assets once, small metadata snapshots thereafter.
/// A record becomes visible only after its complete footer has been written.</summary>
public static class MixProjectFile
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("MAMIXJ02");
    private const long Footer = 0x324A58494D414D;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, Index> Indices = new(StringComparer.OrdinalIgnoreCase);
    private sealed class Index
    {
        public long Length = Magic.Length;
        public Dictionary<Guid, (MixSource Source, long Offset, int Count)> Assets = [];
        public string? State;
    }
    private sealed class Snapshot
    {
        public string Arrangement { get; set; } = "";
        public Guid[] Sources { get; set; } = [];
    }
    public static Task SaveAsync(string path, MixArrangement mix, CancellationToken token = default)
    {
        // Capture on the caller's thread; subsequent UI changes cannot affect this save.
        string state = mix.Capture();
        var sources = mix.Lanes.SelectMany(l => l.Clips).Select(c => c.SourceId).Distinct().Select(id => mix.Sources[id]).ToArray();
        return SaveSnapshotAsync(path, state, sources, token);
    }
    public static async Task SaveSnapshotAsync(string path, string state, IReadOnlyList<MixSource> sources, CancellationToken token = default)
    {
        path = Path.GetFullPath(path); var gate = Locks.GetOrAdd(path, _ => new(1, 1));
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                using var file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
                if (file.Length == 0) { file.Write(Magic); Indices.TryRemove(path, out _); }
                Index index;
                if (!Indices.TryGetValue(path, out index!) || file.Length != index.Length)
                    index = Scan(file, token);
                // Discard any interrupted record before appending the next complete transaction.
                file.SetLength(index.Length); file.Position = index.Length;
                using var writer = new BinaryWriter(file, Encoding.UTF8, true);
                foreach (var source in sources)
                {
                    token.ThrowIfCancellationRequested();
                    if (index.Assets.ContainsKey(source.Id)) continue;
                    if (source.SampleRate is < 1000 or > 768000 || source.Channels is < 1 or > 64
                        || source.Data.Length % source.Channels != 0 || source.Data.Any(v => !float.IsFinite(v)))
                        throw new InvalidDataException("Invalid source audio.");
                    byte[] metadata = JsonSerializer.SerializeToUtf8Bytes(source);
                    long length = 4L + metadata.Length + source.Data.LongLength * 4;
                    writer.Write((byte)1); writer.Write(length); writer.Write(metadata.Length); writer.Write(metadata);
                    long offset = file.Position;
                    byte[] buffer = new byte[65536 * 4];
                    for (int i = 0; i < source.Data.Length; i += 65536)
                    {
                        token.ThrowIfCancellationRequested(); int count = Math.Min(65536, source.Data.Length - i);
                        Buffer.BlockCopy(source.Data, i * 4, buffer, 0, count * 4); writer.Write(buffer, 0, count * 4);
                    }
                    writer.Write(Footer);
                    index.Assets.Add(source.Id, (new MixSource { Id = source.Id, Name = source.Name, SampleRate = source.SampleRate, Channels = source.Channels, Bpm = source.Bpm }, offset, source.Data.Length));
                    index.Length = file.Position;
                }
                token.ThrowIfCancellationRequested();
                var snapshot = new Snapshot { Arrangement = state, Sources = sources.Select(s => s.Id).ToArray() };
                byte[] json = JsonSerializer.SerializeToUtf8Bytes(snapshot);
                writer.Write((byte)2); writer.Write((long)json.Length); writer.Write(json); writer.Write(Footer);
                writer.Flush(); file.Flush(true);
                index.Length = file.Position; index.State = Encoding.UTF8.GetString(json); Indices[path] = index;
            }, token).ConfigureAwait(false);
        }
        catch { Indices.TryRemove(path, out _); throw; }
        finally { gate.Release(); }
    }
    private static Index Scan(FileStream file, CancellationToken token)
    {
        file.Position = 0; byte[] header = new byte[Magic.Length]; file.ReadExactly(header);
        if (!header.SequenceEqual(Magic)) throw new InvalidDataException("Unsupported mix project format. Save using the current journal format.");
        var index = new Index(); using var reader = new BinaryReader(file, Encoding.UTF8, true);
        while (file.Position + 9 <= file.Length)
        {
            token.ThrowIfCancellationRequested();
            byte type = reader.ReadByte(); long length = reader.ReadInt64(), start = file.Position;
            if (length < 0 || length > file.Length - start - 8) break;
            file.Position = start + length;
            if (reader.ReadInt64() != Footer) break;
            long end = file.Position; file.Position = start;
            if (type == 1)
            {
                int size = reader.ReadInt32();
                if (size <= 0 || size > length - 4 || size > 1024 * 1024) throw new InvalidDataException("Invalid project asset metadata.");
                var source = JsonSerializer.Deserialize<MixSource>(reader.ReadBytes(size)) ?? throw new InvalidDataException("Invalid source.");
                long count = (length - 4 - size) / 4;
                if ((length - 4 - size) % 4 != 0 || count > int.MaxValue) throw new InvalidDataException("Invalid project audio size.");
                index.Assets[source.Id] = (source, file.Position, (int)count);
            }
            else if (type == 2)
            {
                if (length > int.MaxValue) throw new InvalidDataException("Project metadata is too large.");
                index.State = Encoding.UTF8.GetString(reader.ReadBytes((int)length));
            }
            else throw new InvalidDataException("Unknown mix project record.");
            file.Position = end; index.Length = end;
        }
        return index;
    }
    public static async Task<MixArrangement> LoadAsync(string path, CancellationToken token = default)
    {
        path = Path.GetFullPath(path); var gate = Locks.GetOrAdd(path, _ => new(1, 1)); await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                var index = Scan(file, token);
                var snapshot = JsonSerializer.Deserialize<Snapshot>(index.State ?? throw new InvalidDataException("No complete project snapshot."))!;
                var mix = new MixArrangement(); mix.Restore(snapshot.Arrangement);
                foreach (var id in snapshot.Sources)
                {
                    token.ThrowIfCancellationRequested();
                    if (!index.Assets.TryGetValue(id, out var asset)) throw new InvalidDataException("Missing project audio.");
                    var source = JsonSerializer.Deserialize<MixSource>(JsonSerializer.Serialize(asset.Source))!;
                    source.Data = new float[asset.Count]; file.Position = asset.Offset;
                    byte[] buffer = new byte[65536 * 4];
                    for (int i = 0; i < source.Data.Length; i += 65536)
                    {
                        token.ThrowIfCancellationRequested(); int count = Math.Min(65536, source.Data.Length - i);
                        file.ReadExactly(buffer.AsSpan(0, count * 4)); Buffer.BlockCopy(buffer, 0, source.Data, i * 4, count * 4);
                    }
                    mix.Sources.Add(id, source);
                }
                mix.Validate(); Indices[path] = index; return mix;
            }, token).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }
}
