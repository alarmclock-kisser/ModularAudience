namespace ModularAudience.CoOp.Protocol;

/// <summary>
/// Complete state of a shared collection at a point in time.
/// </summary>
public sealed record CoOpCollectionSnapshot(
    Guid CollectionId,
    string Name,
    Guid OwnerPeerId,
    string OwnerDisplayName,
    IReadOnlyList<CoOpAudioSnapshot> Audios);
