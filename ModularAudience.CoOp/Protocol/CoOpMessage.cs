using System.Text.Json.Serialization;

namespace ModularAudience.CoOp.Protocol;

/// <summary>
/// Base type for every message that travels over a co-op connection.
/// The discriminator keeps the wire format self-describing while the
/// concrete records keep the application code strongly typed.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(HelloMessage), "hello")]
[JsonDerivedType(typeof(WelcomeMessage), "welcome")]
[JsonDerivedType(typeof(ShareCollectionMessage), "shareCollection")]
[JsonDerivedType(typeof(ShareCollectionCompletedMessage), "shareCollectionCompleted")]
[JsonDerivedType(typeof(AudioAddedMessage), "audioAdded")]
[JsonDerivedType(typeof(AudioUpdatedMessage), "audioUpdated")]
[JsonDerivedType(typeof(AudioRemovedMessage), "audioRemoved")]
[JsonDerivedType(typeof(AudioChangeAcknowledgedMessage), "audioChangeAcknowledged")]
[JsonDerivedType(typeof(UnshareCollectionMessage), "unshareCollection")]
[JsonDerivedType(typeof(PeerJoinedMessage), "peerJoined")]
[JsonDerivedType(typeof(PeerLeftMessage), "peerLeft")]
[JsonDerivedType(typeof(SessionEndedMessage), "sessionEnded")]
[JsonDerivedType(typeof(ErrorMessage), "error")]
[JsonDerivedType(typeof(PingMessage), "ping")]
[JsonDerivedType(typeof(PongMessage), "pong")]
public abstract record CoOpMessage;

public sealed record HelloMessage(
    Guid PeerId,
    string DisplayName,
    int ProtocolVersion = CoOpDefaults.ProtocolVersion) : CoOpMessage;

public sealed record WelcomeMessage(
    Guid PeerId,
    string DisplayName,
    int ProtocolVersion = CoOpDefaults.ProtocolVersion) : CoOpMessage;

public sealed record ShareCollectionMessage(
    Guid CollectionId,
    string Name,
    Guid OwnerPeerId,
    string OwnerDisplayName,
    bool ReplaceExisting = true) : CoOpMessage;

public sealed record ShareCollectionCompletedMessage(Guid CollectionId) : CoOpMessage;

public sealed record AudioAddedMessage(Guid CollectionId, CoOpAudioSnapshot Audio) : CoOpMessage;

public sealed record AudioUpdatedMessage(Guid CollectionId, CoOpAudioSnapshot Audio) : CoOpMessage;

public sealed record AudioRemovedMessage(Guid CollectionId, Guid AudioId) : CoOpMessage;

public sealed record AudioChangeAcknowledgedMessage(Guid CollectionId, Guid AudioId, long Revision) : CoOpMessage;

public sealed record UnshareCollectionMessage(Guid CollectionId, string? Reason = null) : CoOpMessage;

public sealed record PeerJoinedMessage(Guid PeerId, string DisplayName) : CoOpMessage;

public sealed record PeerLeftMessage(Guid PeerId, string? Reason = null) : CoOpMessage;

public sealed record SessionEndedMessage(string? Reason = null) : CoOpMessage;

public sealed record ErrorMessage(string Message, string? Code = null) : CoOpMessage;

public sealed record PingMessage(long Timestamp) : CoOpMessage;

public sealed record PongMessage(long Timestamp) : CoOpMessage;
