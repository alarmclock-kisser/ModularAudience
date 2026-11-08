namespace ModularAudience.CoOp.Protocol;

public sealed class CoOpProtocolException : Exception
{
    public CoOpProtocolException(string message)
        : base(message)
    {
    }

    public CoOpProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
