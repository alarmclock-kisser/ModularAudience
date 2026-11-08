using System.Net;
using ModularAudience.CoOp.Protocol;

namespace ModularAudience.CoOp.Synchronization;

public sealed class CoOpSessionOptions
{
    public string DisplayName { get; init; } = Environment.MachineName;

    public int PreferredPort { get; init; } = CoOpDefaults.DefaultPort;

    public int PortSearchCount { get; init; } = CoOpDefaults.PortSearchCount;

    public IPAddress BindAddress { get; init; } = IPAddress.Any;

    public bool EnableDiscoveryResponder { get; init; } = true;

    public int DiscoveryPort { get; init; } = CoOpDefaults.DefaultDiscoveryPort;

    public int DiscoveryPortSearchCount { get; init; } = CoOpDefaults.DiscoveryPortCount;

    public string RemoteCollectionsRoot { get; init; } =
        Path.Combine(Path.GetTempPath(), "ModularAudience", "CoOp");

    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(8);

    public ICoOpMessageSerializer Serializer { get; init; } = new JsonCoOpMessageSerializer();
}
