namespace ModularAudience.Onnx;

public sealed record DemucsCudaDevice(int Id, string Name, long? Luid);

public sealed record DemucsCudaDeviceCatalog(IReadOnlyList<DemucsCudaDevice> Devices, string? Warning);
