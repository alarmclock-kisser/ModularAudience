using System.Text.Json;

namespace ModularAudience.Onnx;

/// <summary>Idle sessions only. Taking a session transfers exclusive ownership to one dialog.</summary>
public static class DemucsModelCache
{
    private sealed class Entry(OnnxService service, DateTime expires)
    {
        public readonly OnnxService Service = service;
        public readonly DateTime Expires = expires;
        public Timer? Timer;
    }
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Entry> Idle = [];

    static DemucsModelCache() => AppDomain.CurrentDomain.ProcessExit += (_, _) => Clear();

    public static string Key(string modelPath, DemucsLoadOptions options)
    {
        var copy = JsonSerializer.Deserialize<DemucsLoadOptions>(JsonSerializer.Serialize(options))!;
        copy.NormalizeWorkers();
        copy.SessionConfigEntries = copy.SessionConfigEntries.OrderBy(p => p.Key, StringComparer.Ordinal)
            .ToDictionary(p => p.Key, p => p.Value);
        var file = new FileInfo(Path.GetFullPath(modelPath));
        return JsonSerializer.Serialize(new { Path = file.FullName.ToUpperInvariant(), Length = file.Exists ? file.Length : -1,
            Modified = file.Exists ? file.LastWriteTimeUtc.Ticks : 0, Options = copy });
    }

    public static OnnxService? Take(string key)
    {
        Entry? entry;
        lock (Gate)
        {
            if (!Idle.Remove(key, out entry)) return null;
            entry.Timer?.Dispose();
        }
        if (entry.Expires > DateTime.UtcNow && entry.Service.IsOnline) return entry.Service;
        entry.Service.Dispose();
        return null;
    }

    public static void Return(string key, OnnxService service, TimeSpan keepAlive)
    {
        if (!service.IsOnline || keepAlive <= TimeSpan.Zero) { service.Dispose(); return; }
        Entry? replaced = null;
        lock (Gate)
        {
            Idle.Remove(key, out replaced);
            var entry = new Entry(service, DateTime.UtcNow + keepAlive);
            Idle[key] = entry;
            entry.Timer = new Timer(_ => Expire(key, entry), null, keepAlive, Timeout.InfiniteTimeSpan);
        }
        if (replaced != null) { replaced.Timer?.Dispose(); replaced.Service.Dispose(); }
    }

    private static void Expire(string key, Entry expected)
    {
        lock (Gate)
        {
            if (!Idle.TryGetValue(key, out var entry) || !ReferenceEquals(entry, expected)) return;
            Idle.Remove(key);
        }
        expected.Timer?.Dispose();
        expected.Service.Dispose();
    }

    public static void Clear()
    {
        Entry[] entries;
        lock (Gate) { entries = Idle.Values.ToArray(); Idle.Clear(); }
        foreach (var entry in entries) { entry.Timer?.Dispose(); entry.Service.Dispose(); }
    }
}
