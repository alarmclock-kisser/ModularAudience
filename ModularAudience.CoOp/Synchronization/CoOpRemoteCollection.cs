using ModularAudience.Audio;
using ModularAudience.CoOp.Protocol;

namespace ModularAudience.CoOp.Synchronization;

/// <summary>
/// The local mirror of a collection owned by another peer. Deleted tracks are
/// intentionally kept as tombstones so users can still export or inspect the
/// last synchronized state.
/// </summary>
public sealed class CoOpRemoteCollection : IDisposable
{
    private readonly object stateGate = new();
    private readonly HashSet<Guid> deleted = [];
    private readonly HashSet<Guid> locallyDiscarded = [];
    private readonly Dictionary<Guid, CoOpAudioSnapshot> synchronizedAudios = [];
    private readonly Dictionary<Guid, long> pending = [];
    private HashSet<Guid>? incomingAudioIds;
    private long revisionCounter;
    private bool disposed;

    internal CoOpRemoteCollection(
        Guid collectionId,
        string name,
        Guid ownerPeerId,
        string ownerDisplayName,
        string remoteCollectionsRoot)
    {
        this.CollectionId = collectionId;
        this.Name = name;
        this.OwnerPeerId = ownerPeerId;
        this.OwnerDisplayName = ownerDisplayName;

        string workingDirectory = Path.Combine(remoteCollectionsRoot, collectionId.ToString("N"));
        this.Collection = new AudioCollection(workingDirectory);
        this.Tracker = new CoOpCollectionTracker(this.Collection);
    }

    public Guid CollectionId { get; }

    public string Name { get; internal set; }

    public Guid OwnerPeerId { get; }

    public string OwnerDisplayName { get; internal set; }

    public AudioCollection Collection { get; }

    public bool IsUnshared { get; internal set; }

    internal CoOpCollectionTracker Tracker { get; }

    public event EventHandler? StateChanged;

    public event EventHandler<CoOpAudioSyncStateEventArgs>? AudioSyncStateChanged;

    public bool IsDeleted(Guid audioId)
    {
        lock (this.stateGate)
        {
            return this.deleted.Contains(audioId);
        }
    }

    public bool IsAudioPending(Guid audioId)
    {
        lock (this.pending)
        {
            return this.pending.ContainsKey(audioId);
        }
    }

    public IReadOnlyList<CoOpAudioSnapshot> GetLateJoinSnapshots()
    {
        List<CoOpAudioSnapshot> snapshots;
        lock (this.stateGate)
        {
            snapshots = this.synchronizedAudios.Values
                .Where(snapshot => !this.deleted.Contains(snapshot.AudioId))
                .ToList();
        }

        var result = new List<CoOpAudioSnapshot>();
        foreach (CoOpAudioSnapshot snapshot in snapshots)
        {
            // A local discard must not remove the authoritative session data.
            // Live, non-discarded items also include edits awaiting the debounce.
            AudioObj? audio = this.Collection[snapshot.AudioId];
            result.Add(audio == null ? snapshot : CoOpAudioSnapshotMapper.Capture(audio));
        }

        return result;
    }

    public void MarkUnshared()
    {
        if (this.IsUnshared)
        {
            return;
        }

        this.IsUnshared = true;
        lock (this.stateGate)
        {
            this.incomingAudioIds = null;
        }

        List<Guid> pendingIds;
        lock (this.pending)
        {
            pendingIds = [.. this.pending.Keys];
            this.pending.Clear();
        }

        foreach (Guid audioId in pendingIds)
        {
            this.RaiseSyncState(audioId, false);
        }

        this.StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void MarkShared()
    {
        if (!this.IsUnshared)
        {
            return;
        }

        this.IsUnshared = false;
        this.StateChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void BeginSynchronization(bool replaceExisting)
    {
        lock (this.stateGate)
        {
            this.incomingAudioIds = replaceExisting ? [] : null;
        }

        this.MarkShared();
    }

    internal void CompleteSynchronization()
    {
        lock (this.stateGate)
        {
            if (this.incomingAudioIds == null)
            {
                return;
            }

            // Re-sharing reconciles membership, never destroys retained samples.
            foreach (Guid audioId in this.synchronizedAudios.Keys)
            {
                if (!this.incomingAudioIds.Contains(audioId))
                {
                    this.deleted.Add(audioId);
                }
            }

            this.incomingAudioIds = null;
        }

        this.StateChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void MarkLocallyDiscarded(Guid audioId)
    {
        lock (this.stateGate)
        {
            this.locallyDiscarded.Add(audioId);
        }
    }

    internal void RecordLocalChange(CoOpAudioSnapshot snapshot)
    {
        lock (this.stateGate)
        {
            this.locallyDiscarded.Remove(snapshot.AudioId);
            this.synchronizedAudios[snapshot.AudioId] = snapshot;
        }
    }

    public AudioObj? ApplyAdd(CoOpAudioSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        bool wasDeleted;
        bool discarded;
        lock (this.stateGate)
        {
            this.synchronizedAudios[snapshot.AudioId] = snapshot;
            this.incomingAudioIds?.Add(snapshot.AudioId);
            wasDeleted = this.deleted.Remove(snapshot.AudioId);
            discarded = this.locallyDiscarded.Contains(snapshot.AudioId);
        }

        if (discarded)
        {
            return null;
        }

        AudioObj? existing = this.Collection[snapshot.AudioId];
        if (existing != null)
        {
            this.ApplyUpdate(snapshot);
            if (wasDeleted)
            {
                this.StateChanged?.Invoke(this, EventArgs.Empty);
            }

            return existing;
        }

        AudioObj audio = CoOpAudioSnapshotMapper.Create(snapshot);
        using (this.Tracker.SuppressNotifications())
        {
            this.Collection.Audios.Add(audio);
        }

        return audio;
    }

    public AudioObj? ApplyUpdate(CoOpAudioSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (this.stateGate)
        {
            if (this.deleted.Contains(snapshot.AudioId))
            {
                return null;
            }

            this.synchronizedAudios[snapshot.AudioId] = snapshot;
            if (this.locallyDiscarded.Contains(snapshot.AudioId))
            {
                return null;
            }
        }

        AudioObj? existing = this.Collection[snapshot.AudioId];
        if (existing == null)
        {
            return this.ApplyAdd(snapshot);
        }

        using (this.Tracker.SuppressNotifications())
        {
            CoOpAudioSnapshotMapper.ApplyTo(existing, snapshot);
            int index = this.Collection.Audios.IndexOf(existing);
            if (index >= 0)
            {
                this.Collection.Audios.ResetItem(index);
            }
        }

        return existing;
    }

    public AudioObj? ApplyRemove(Guid audioId)
    {
        AudioObj? audio = this.Collection[audioId];
        lock (this.stateGate)
        {
            this.deleted.Add(audioId);
        }

        this.StateChanged?.Invoke(this, EventArgs.Empty);
        return audio;
    }

    public long NextRevision(Guid audioId)
    {
        _ = audioId;
        return Interlocked.Increment(ref this.revisionCounter);
    }

    public void MarkPending(Guid audioId, long revision)
    {
        lock (this.pending)
        {
            this.pending[audioId] = revision;
        }

        this.RaiseSyncState(audioId, true);
    }

    public void Acknowledge(Guid audioId, long revision)
    {
        bool removed = false;
        lock (this.pending)
        {
            if (this.pending.TryGetValue(audioId, out long current) && revision >= current)
            {
                removed = this.pending.Remove(audioId);
            }
        }

        if (removed)
        {
            this.RaiseSyncState(audioId, false);
        }
    }

    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.Tracker.Dispose();
        this.Collection.Dispose();
    }

    private void RaiseSyncState(Guid audioId, bool isPending)
    {
        this.AudioSyncStateChanged?.Invoke(
            this,
            new CoOpAudioSyncStateEventArgs(this.CollectionId, audioId, isPending));
    }
}
