namespace ModularAudience.CoOp.Protocol;

public interface ICoOpMessageSerializer
{
    byte[] Serialize(CoOpMessage message);

    CoOpMessage Deserialize(ReadOnlySpan<byte> data);
}
