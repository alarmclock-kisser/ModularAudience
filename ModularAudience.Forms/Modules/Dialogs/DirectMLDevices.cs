using System.Globalization;
using System.Runtime.InteropServices;
using ModularAudience.Onnx;

namespace ModularAudience.Forms;

internal static class DirectMLDevices
{
    private const int Adapter3QueryVideoMemoryInfoSlot = 14;
    private const ulong GiB = 1024UL * 1024 * 1024;
    private const ulong MinimumEstimatedWorkingSet = 8 * GiB;
    private const ulong ModelSizeMultiplier = 12;
    private const int MaximumBudgetPercent = 70;

    internal sealed record Adapter(int Id, string Name, long? Luid = null)
    {
        public override string ToString() => $"{Id}: {Name}";
    }

    internal readonly record struct VideoMemoryInfo(ulong Budget, ulong CurrentUsage, ulong AvailableForReservation);

    [StructLayout(LayoutKind.Sequential)]
    private struct DxgiVideoMemoryInfo
    {
        public ulong Budget;
        public ulong CurrentUsage;
        public ulong AvailableForReservation;
        public ulong CurrentReservation;
    }

    // Enumerate in exactly the DXGI order used by AppendExecutionProvider_DML(deviceId).
    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(in Guid iid, out IntPtr factory);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumAdapters(IntPtr factory, uint index, out IntPtr adapter);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDescription(IntPtr adapter, out AdapterDescription description);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int QueryVideoMemoryInfo(IntPtr adapter, uint nodeIndex, int segmentGroup, out DxgiVideoMemoryInfo info);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct AdapterDescription
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint VendorId, DeviceId, SubSysId, Revision;
        public UIntPtr DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public long Luid;
        public uint Flags;
    }

    internal static Adapter[] GetAdapters()
    {
        var adapters = new List<Adapter>();
        Guid iid = new("770aae78-f26f-4dba-a829-253c83d1b387");
        if (CreateDXGIFactory1(iid, out IntPtr factory) < 0) return [];
        try
        {
            var enumerate = Marshal.GetDelegateForFunctionPointer<EnumAdapters>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(factory), 12 * IntPtr.Size));
            for (uint index = 0; enumerate(factory, index, out IntPtr adapter) >= 0; index++)
            {
                try
                {
                    var describe = Marshal.GetDelegateForFunctionPointer<GetDescription>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(adapter), 10 * IntPtr.Size));
                    if (describe(adapter, out var description) >= 0 && (description.Flags & 2) == 0)
                        adapters.Add(new Adapter((int)index, description.Description, description.Luid));
                }
                finally { Marshal.Release(adapter); }
            }
        }
        finally { Marshal.Release(factory); }
        return adapters.ToArray();
    }

    internal static VideoMemoryInfo GetVideoMemoryInfo(int adapterId)
    {
        if (adapterId < 0) throw new ArgumentOutOfRangeException(nameof(adapterId));
        Guid factoryId = new("770aae78-f26f-4dba-a829-253c83d1b387");
        int result = CreateDXGIFactory1(factoryId, out IntPtr factory);
        if (result < 0) throw new InvalidOperationException($"DXGI factory creation failed (0x{unchecked((uint)result):X8}).");

        try
        {
            var enumerate = Marshal.GetDelegateForFunctionPointer<EnumAdapters>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(factory), 12 * IntPtr.Size));
            for (uint index = 0; enumerate(factory, index, out IntPtr adapter) >= 0; index++)
            {
                try
                {
                    if (index != adapterId) continue;
                    Guid adapter3Id = new("645967A4-1392-4310-A798-8053CE3E93FD");
                    result = Marshal.QueryInterface(adapter, in adapter3Id, out IntPtr adapter3);
                    if (result < 0)
                        throw new InvalidOperationException($"DXGI adapter {adapterId} does not expose video-memory budgets (0x{unchecked((uint)result):X8}).");

                    try
                    {
                        var query = Marshal.GetDelegateForFunctionPointer<QueryVideoMemoryInfo>(
                            Marshal.ReadIntPtr(Marshal.ReadIntPtr(adapter3), Adapter3QueryVideoMemoryInfoSlot * IntPtr.Size));
                        result = query(adapter3, 0, 0, out var info);
                        if (result < 0)
                            throw new InvalidOperationException($"DXGI video-memory query failed for adapter {adapterId} (0x{unchecked((uint)result):X8}).");
                        return new VideoMemoryInfo(info.Budget, info.CurrentUsage, info.AvailableForReservation);
                    }
                    finally { Marshal.Release(adapter3); }
                }
                finally { Marshal.Release(adapter); }
            }
        }
        finally { Marshal.Release(factory); }

        throw new InvalidOperationException($"DirectML adapter {adapterId} is no longer available.");
    }

    internal static string? GetLoadMemoryWarning(VideoMemoryInfo info, long modelBytes)
    {
        if (info.Budget == 0) return "DXGI did not report a usable local video-memory budget.";
        if (modelBytes <= 0) return "The ONNX model size could not be determined for the DirectML memory check.";

        ulong modelSize = (ulong)modelBytes;
        ulong estimated = modelSize > ulong.MaxValue / ModelSizeMultiplier
            ? ulong.MaxValue
            : modelSize * ModelSizeMultiplier;
        estimated = Math.Max(estimated, MinimumEstimatedWorkingSet);
        ulong safeLimit = SafeBudgetLimit(info.Budget);
        ulong available = info.CurrentUsage >= safeLimit ? 0 : safeLimit - info.CurrentUsage;
        if (estimated > available)
            return $"DirectML load blocked: the model needs an estimated {FormatBytes(estimated)} of headroom, but only {FormatBytes(available)} remains below the 70% safety limit (current usage {FormatBytes(info.CurrentUsage)} of {FormatBytes(info.Budget)}).";
        return null;
    }

    internal static string? GetRuntimeMemoryWarning(VideoMemoryInfo info)
    {
        if (info.Budget == 0) return "DXGI did not report a usable local video-memory budget.";
        ulong safeLimit = SafeBudgetLimit(info.Budget);
        if (info.CurrentUsage > safeLimit)
            return $"DirectML GPU usage is {FormatBytes(info.CurrentUsage)} of a {FormatBytes(info.Budget)} budget, above the 70% safety limit. Close GPU-heavy applications or use CPU execution.";
        return null;
    }

    internal static Adapter? MatchCudaDevice(DemucsCudaDevice cuda, IEnumerable<Adapter> adapters) =>
        cuda.Luid is { } luid ? adapters.FirstOrDefault(adapter => adapter.Luid == luid) : null;

    private static ulong SafeBudgetLimit(ulong budget) => budget / 100 * MaximumBudgetPercent
        + budget % 100 * MaximumBudgetPercent / 100;

    private static string FormatBytes(ulong bytes) =>
        $"{((double)bytes / GiB).ToString("F1", CultureInfo.InvariantCulture)} GiB";
}
