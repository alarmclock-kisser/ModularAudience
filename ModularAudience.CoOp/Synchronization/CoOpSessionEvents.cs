using ModularAudience.Audio;
using ModularAudience.CoOp.Protocol;

namespace ModularAudience.CoOp.Synchronization;

public enum CoOpSessionState
{
    Disconnected,
    Hosting,
    Connected
}

public sealed record CoOpHostInfo(string Address, int Port, Guid PeerId)
{
    public override string ToString() => $"{this.Address}:{this.Port}";
}

public sealed class CoOpStateChangedEventArgs(CoOpSessionState state, string? address = null, string? message = null) : EventArgs
{
    public CoOpSessionState State { get; } = state;

    public string? Address { get; } = address;

    public string? Message { get; } = message;
}

public sealed class CoOpPeerEventArgs(CoOpPeerInfo peer) : EventArgs
{
    public CoOpPeerInfo Peer { get; } = peer;
}

public sealed class CoOpLogEventArgs(string message, Exception? exception = null) : EventArgs
{
    public string Message { get; } = message;

    public Exception? Exception { get; } = exception;
}

public sealed class CoOpRemoteCollectionEventArgs(CoOpRemoteCollection collection) : EventArgs
{
    public CoOpRemoteCollection Collection { get; } = collection;
}

public sealed class CoOpRemoteCollectionRemovedEventArgs(Guid collectionId) : EventArgs
{
    public Guid CollectionId { get; } = collectionId;
}

public sealed class CoOpRemoteAudioEventArgs(
    CoOpRemoteCollection collection,
    AudioObj audio,
    CoOpAudioSnapshot snapshot) : EventArgs
{
    public CoOpRemoteCollection Collection { get; } = collection;

    public AudioObj Audio { get; } = audio;

    public CoOpAudioSnapshot Snapshot { get; } = snapshot;

    public bool IsDeleted => this.Collection.IsDeleted(this.Audio.Id);
}

public sealed class CoOpAudioSyncStateEventArgs(Guid collectionId, Guid audioId, bool isPending) : EventArgs
{
    public Guid CollectionId { get; } = collectionId;

    public Guid AudioId { get; } = audioId;

    public bool IsPending { get; } = isPending;
}
