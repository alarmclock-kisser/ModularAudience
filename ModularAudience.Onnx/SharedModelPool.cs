namespace ModularAudience.Onnx;

/// <summary>Single-flight loading and reference-counted idle retention, one native model per resource.</summary>
public sealed class SharedModelPool<T> where T : class, IDisposable
{
    private sealed class Slot
    {
        public readonly SemaphoreSlim Gate = new(1, 1);
        public readonly SemaphoreSlim Operation = new(1, 1);
        public T? Model;
        public readonly HashSet<string> Keys = [];
        public int Loaded;
        public int Users;
        public Timer? Expiry;
    }
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Slot> slots = new(StringComparer.OrdinalIgnoreCase);
    public sealed class Lease : IDisposable
    {
        private Action<TimeSpan>? release;
        private readonly Action<string> addKey;
        public T Model { get; }
        public TimeSpan KeepAlive { get; set; } = TimeSpan.FromMinutes(5);
        public SemaphoreSlim Operation { get; }
        internal Lease(T model, SemaphoreSlim operation, Action<TimeSpan> release, Action<string> addKey)
        { Model = model; Operation = operation; this.release = release; this.addKey = addKey; }
        public void AddAlias(string key) => addKey(key);
        public void Dispose() => Interlocked.Exchange(ref release, null)?.Invoke(KeepAlive);
    }
    public async Task<Lease?> AcquireAsync(string resource, string key, Func<T>? load, CancellationToken token = default)
    {
        var slot = slots.GetOrAdd(resource, _ => new());
        await slot.Gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (slot.Model != null && !slot.Keys.Contains(key))
            {
                if (load == null) return null;
                if (slot.Users > 0) throw new InvalidOperationException("This model is already in use with different load options. Close its other separation windows before changing the model configuration.");
                slot.Expiry?.Dispose(); slot.Expiry = null;
                slot.Model.Dispose();
                slot.Model = null;
                slot.Keys.Clear();
                Interlocked.Exchange(ref slot.Loaded, 0);
            }
            if (slot.Model == null)
            {
                if (load == null) return null;
                token.ThrowIfCancellationRequested();
                slot.Model = await Task.Run(load, token).ConfigureAwait(false);
                Interlocked.Exchange(ref slot.Loaded, 1);
                slot.Keys.Add(key);
            }
            slot.Expiry?.Dispose(); slot.Expiry = null; slot.Users++;
            var model = slot.Model;
            return new Lease(model, slot.Operation, keep => Release(slot, model, keep), alias =>
            {
                slot.Gate.Wait();
                try { if (ReferenceEquals(slot.Model, model)) slot.Keys.Add(alias); }
                finally { slot.Gate.Release(); }
            });
        }
        finally { slot.Gate.Release(); }
    }
    public bool AnyLoaded(Func<string, T, bool> predicate)
    {
        foreach (var (resource, slot) in slots)
        {
            if (Volatile.Read(ref slot.Loaded) == 0) continue;
            T? model = slot.Model;
            if (model is not null && predicate(resource, model)) return true;
        }
        return false;
    }

    private static void Release(Slot slot, T model, TimeSpan keepAlive)
    {
        slot.Gate.Wait();
        try
        {
            if (!ReferenceEquals(slot.Model, model) || slot.Users <= 0) return;
            if (--slot.Users > 0) return;
            slot.Expiry?.Dispose(); slot.Expiry = null;
            if (keepAlive <= TimeSpan.Zero)
            {
                slot.Model.Dispose();
                slot.Model = null;
                slot.Keys.Clear();
                Interlocked.Exchange(ref slot.Loaded, 0);
                return;
            }
            Timer? timer = null;
            timer = new Timer(_ =>
            {
                slot.Gate.Wait();
                try
                {
                    if (!ReferenceEquals(slot.Expiry, timer) || slot.Users != 0 || !ReferenceEquals(slot.Model, model)) return;
                    slot.Expiry = null;
                    slot.Model.Dispose();
                    slot.Model = null;
                    slot.Keys.Clear();
                    Interlocked.Exchange(ref slot.Loaded, 0);
                }
                finally { timer?.Dispose(); slot.Gate.Release(); }
            }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            slot.Expiry = timer; timer.Change(keepAlive, Timeout.InfiniteTimeSpan);
        }
        finally { slot.Gate.Release(); }
    }
    public void ClearIdle()
    {
        foreach (var slot in slots.Values)
        {
            slot.Gate.Wait();
            try
            {
                if (slot.Users != 0) continue;
                slot.Expiry?.Dispose();
                slot.Expiry = null;
                slot.Model?.Dispose();
                slot.Model = null;
                slot.Keys.Clear();
                Interlocked.Exchange(ref slot.Loaded, 0);
            }
            finally { slot.Gate.Release(); }
        }
    }
}
