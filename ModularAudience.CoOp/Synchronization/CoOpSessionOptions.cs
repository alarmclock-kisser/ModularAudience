using System.Net;
using ModularAudience.Audio;
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

    /// <summary>
    /// User-facing working directory for received collections, including exports
    /// and recordings. This is not a temporary synchronization cache.
    /// </summary>
    private string? remoteCollectionsRoot;
    public string RemoteCollectionsRoot
    {
        get => this.remoteCollectionsRoot ?? AudioStoragePaths.WorkingDirectory;
        init => this.remoteCollectionsRoot = value;
    }

    internal bool UsesDefaultWorkingDirectory => this.remoteCollectionsRoot is null;

    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(8);

    public ICoOpMessageSerializer Serializer { get; init; } = new JsonCoOpMessageSerializer();
}
