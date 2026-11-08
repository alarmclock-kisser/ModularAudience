using System.Buffers.Binary;
using ModularAudience.CoOp.Protocol;

namespace ModularAudience.CoOp.Networking;

/// <summary>
/// Length-prefixed framing for the co-op TCP stream. Each frame carries exactly
/// one serialized <see cref="CoOpMessage"/>.
/// </summary>
public static class CoOpFrameCodec
{
    public const int HeaderSize = sizeof(int);

    public static async ValueTask WriteFrameAsync(
        Stream stream,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (payload.Length <= 0 || payload.Length > CoOpDefaults.MaxFrameBytes)
        {
            throw new CoOpProtocolException($"The co-op frame size {payload.Length} is outside the supported range.");
        }

        byte[] header = new byte[HeaderSize];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask<byte[]> ReadFrameAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        byte[] header = new byte[HeaderSize];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);

        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > CoOpDefaults.MaxFrameBytes)
        {
            throw new CoOpProtocolException($"The co-op frame header announced an invalid size ({length}).");
        }

        byte[] payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        return payload;
    }
}
