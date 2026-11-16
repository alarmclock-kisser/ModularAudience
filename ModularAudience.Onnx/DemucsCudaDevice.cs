namespace ModularAudience.Onnx;

public sealed record DemucsCudaDevice(int Id, string Name, long? Luid);

public sealed record DemucsCudaToolkit(int MajorVersion, string LibraryPath);

public sealed record DemucsCudaDeviceCatalog(IReadOnlyList<DemucsCudaDevice> Devices, string? Warning)
{
    public IReadOnlyList<DemucsCudaToolkit> CuFftToolkits { get; init; } = [];
}
