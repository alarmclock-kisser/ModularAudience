namespace ModularAudience.CoOp;

/// <summary>
/// Central defaults for the co-op transport, discovery and synchronization protocol.
/// </summary>
public static class CoOpDefaults
{
    public const int ProtocolVersion = 1;

    public const int DefaultPort = 47823;
    public const int PortSearchCount = 20;

    public const int DefaultDiscoveryPort = 47824;
    public const int DiscoveryPortCount = 5;
    public const int DefaultDiscoveryTimeoutMilliseconds = 900;

    public const int MaxMessageBytes = 768 * 1024 * 1024;
    public const int MaxFrameBytes = MaxMessageBytes;

    public const string DiscoveryProbe = "MODULARAUDIENCE-COOP-DISCOVER-V1";
}
