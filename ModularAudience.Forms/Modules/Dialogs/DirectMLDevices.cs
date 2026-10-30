using System.Runtime.InteropServices;

namespace ModularAudience.Forms;

internal static class DirectMLDevices
{
    internal sealed record Adapter(int Id, string Name)
    {
        public override string ToString() => $"{Id}: {Name}";
    }

    // Enumerate in exactly the DXGI order used by AppendExecutionProvider_DML(deviceId).
    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(in Guid iid, out IntPtr factory);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumAdapters(IntPtr factory, uint index, out IntPtr adapter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDescription(IntPtr adapter, out AdapterDescription description);
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
                        adapters.Add(new Adapter((int)index, description.Description));
                }
                finally { Marshal.Release(adapter); }
            }
        }
        finally { Marshal.Release(factory); }
        return adapters.ToArray();
    }
}
