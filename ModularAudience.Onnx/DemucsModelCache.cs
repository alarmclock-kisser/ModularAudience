using System.Text.Json;

namespace ModularAudience.Onnx;

/// <summary>Idle sessions only. Taking a session transfers exclusive ownership to one dialog.</summary>
public static class DemucsModelCache
{
    private static readonly SharedModelPool<OnnxService> Shared = new();
    public static bool HasDirectMLSessions =>
        Shared.AnyLoaded((_, service) => service.EffectiveOptions?.UseDirectML == true);

    public static async Task<SharedModelPool<OnnxService>.Lease?> AcquireAsync(string path, DemucsLoadOptions options,
        IEnumerable<string> directories, bool allowLoad, CancellationToken token = default)
    {
        string resource = ResourceKey(path, options);
        string key = Key(path, options);
        var copy = JsonSerializer.Deserialize<DemucsLoadOptions>(JsonSerializer.Serialize(options))!;
        string[] folders = directories.ToArray();
        var lease = await Shared.AcquireAsync(resource, key, allowLoad ? () =>
        {
            var service = new OnnxService(folders, loadImmediately: false);
            try { service.LoadModel(path, copy); return service; }
            catch { service.Dispose(); throw; }
        } : null, token).ConfigureAwait(false);
        // Provider fallback/DirectML normalization must reuse the same native session.
        if (lease?.Model.EffectiveOptions is { } effective) lease.AddAlias(Key(path, effective));
        return lease;
    }

    internal static string ResourceKey(string modelPath, DemucsLoadOptions options)
    {
        string model = Path.GetFullPath(modelPath).ToUpperInvariant();
        string target = options.UseDirectML
            ? $"DML:{options.DeviceId}"
            : "CPU";
        return model + "|" + target;
    }
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
        copy.NormalizeForProvider(copy.UseDirectML);
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
        Shared.ClearIdle();
        Entry[] entries;
        lock (Gate) { entries = Idle.Values.ToArray(); Idle.Clear(); }
        foreach (var entry in entries) { entry.Timer?.Dispose(); entry.Service.Dispose(); }
    }
}
