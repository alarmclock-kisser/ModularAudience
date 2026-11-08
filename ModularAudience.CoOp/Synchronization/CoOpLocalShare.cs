using ModularAudience.Audio;
using ModularAudience.CoOp.Protocol;

namespace ModularAudience.CoOp.Synchronization;

/// <summary>
/// A collection that the local peer offers to the session. The underlying
/// <see cref="AudioCollection"/> remains owned by the application.
/// </summary>
public sealed class CoOpLocalShare : IDisposable
{
    private readonly HashSet<Guid> removedAudioIds = [];
    private readonly Dictionary<Guid, long> pending = [];
    private long revisionCounter;
    private bool disposed;

    internal CoOpLocalShare(Guid collectionId, string name, AudioCollection collection, string ownerDisplayName)
    {
        this.CollectionId = collectionId;
        this.Name = name;
        this.Collection = collection;
        this.OwnerDisplayName = ownerDisplayName;
        this.Tracker = new CoOpCollectionTracker(collection);
    }

    public Guid CollectionId { get; }

    public string Name { get; }

    public AudioCollection Collection { get; }

    public string OwnerDisplayName { get; }

    internal CoOpCollectionTracker Tracker { get; }

    public event EventHandler<CoOpAudioSyncStateEventArgs>? AudioSyncStateChanged;

    public bool IsAudioPending(Guid audioId)
    {
        lock (this.pending)
        {
            return this.pending.ContainsKey(audioId);
        }
    }

    public CoOpAudioSnapshot Capture(AudioObj audio)
    {
        long revision = Interlocked.Increment(ref this.revisionCounter);
        return CoOpAudioSnapshotMapper.Capture(audio, revision);
    }

    internal bool IsRemoved(Guid audioId)
    {
        lock (this.removedAudioIds)
        {
            return this.removedAudioIds.Contains(audioId);
        }
    }

    internal void MarkAdded(Guid audioId)
    {
        lock (this.removedAudioIds)
        {
            this.removedAudioIds.Remove(audioId);
        }
    }

    internal void MarkRemoved(Guid audioId)
    {
        lock (this.removedAudioIds)
        {
            this.removedAudioIds.Add(audioId);
        }
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
        lock (this.pending)
        {
            this.pending.Clear();
        }
    }

    private void RaiseSyncState(Guid audioId, bool isPending)
    {
        this.AudioSyncStateChanged?.Invoke(
            this,
            new CoOpAudioSyncStateEventArgs(this.CollectionId, audioId, isPending));
    }
}
