using MathNet.Numerics.Providers.FourierTransform;
using System.Collections;
using System.Numerics;
using System.Runtime.InteropServices;

namespace ModularAudience.Onnx;

public sealed class DemucsFourier
{
    private readonly DemucsLoadOptions options;
    private readonly string? cudaLibraryPath;
    private bool cudaFailed;
    public string Backend { get; private set; }
    public int Workers => Math.Clamp(this.options.FourierWorkers, 1, Environment.ProcessorCount);
    public static DemucsCudaDeviceCatalog EnumerateCudaDevices() => CudaToolkitFourier.EnumerateDevices();
    public DemucsFourier(DemucsLoadOptions? options = null)
    {
        this.options = options ?? new DemucsLoadOptions { CudaDeviceId = -1 };
        this.options.NormalizeWorkers();
        this.cudaLibraryPath = this.options.CudaDeviceId >= 0 ? CudaToolkitFourier.ResolveCuFftLibrary() : null;
        this.cudaFailed = this.options.CudaDeviceId >= 0 && this.cudaLibraryPath is null;
        this.Backend = this.cudaFailed
            ? $"Parallel CPU FFT ({this.Workers} workers); no CuFFT library found in CUDA toolkits or environment paths"
            : this.cudaLibraryPath is null
                ? $"Parallel CPU FFT ({this.Workers} workers)"
                : $"CUDA CuFFT ({Path.GetFileName(this.cudaLibraryPath)}; device {this.options.CudaDeviceId})";
    }

    internal void Transform(Complex[][] frames, bool inverse, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (this.options.CudaDeviceId >= 0 && !this.cudaFailed)
        {
            try
            {
                CudaToolkitFourier.Transform(frames, inverse, this.options.CudaDeviceId, this.cudaLibraryPath!);
                this.Backend = $"CUDA CuFFT ({Path.GetFileName(this.cudaLibraryPath)}; device {this.options.CudaDeviceId})";
                token.ThrowIfCancellationRequested();
                return;
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                this.cudaFailed = true;
                this.Backend = $"Parallel CPU FFT ({this.Workers} workers); CUDA CuFFT unavailable: {error.Message}";
            }
        }
        Parallel.For(0, frames.Length, new ParallelOptions { MaxDegreeOfParallelism = this.Workers, CancellationToken = token },
            () => new ManagedFourierTransformProvider(),
            (i, _, provider) =>
            {
                if (inverse) provider.Backward(frames[i], FourierTransformScaling.BackwardScaling);
                else provider.Forward(frames[i], FourierTransformScaling.NoScaling);
                return provider;
            }, _ => { });
    }
}

// Batched CUDA CuFFT. The library is discovered from CUDA environment variables,
// installed toolkit directories, or PATH; the driver ABI coexists with ManagedCuda.
// Every context, plan and buffer is scoped to the calling background thread.
internal static class CudaToolkitFourier
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Init(uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DeviceGetCount(out int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DeviceGet(out int device, int ordinal);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DeviceGetName(IntPtr name, int length, int device);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DeviceGetLuid(IntPtr luid, out uint nodeMask, int device);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ContextCreate(out IntPtr context, uint flags, int device);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ContextDestroy(IntPtr context);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Allocate(out ulong pointer, nuint bytes);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Free(ulong pointer);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CopyToDevice(ulong destination, [In] float[] source, nuint bytes);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int CopyToHost([Out] float[] destination, ulong source, nuint bytes);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Plan(out int plan, int length, int type, int batch);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Execute(int plan, ulong input, ulong output, int direction);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DestroyPlan(int plan);
    private static T Function<T>(IntPtr library, string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
    private static void Check(int code, string operation)
    {
        if (code != 0) throw new InvalidOperationException($"{operation} returned CUDA error {code}.");
    }

    internal static string? FindCuFftLibrary(IEnumerable<string> toolkitRoots)
    {
        foreach (string root in toolkitRoots.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (string directory in new[] { Path.Combine(root, "bin"), root }.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(directory)) continue;
                string? library = Directory.GetFiles(directory, "cufft64_*.dll", SearchOption.TopDirectoryOnly)
                    .OrderByDescending(CuFftLibraryVersion)
                    .FirstOrDefault();
                if (library != null) return Path.GetFullPath(library);
            }
        }
        return null;
    }

    internal static string? ResolveCuFftLibrary()
    {
        var environmentRoots = Environment.GetEnvironmentVariables().Cast<DictionaryEntry>()
            .Select(entry => (Name: entry.Key?.ToString() ?? "", Path: entry.Value?.ToString() ?? ""))
            .Where(entry => entry.Name.StartsWith("CUDA_PATH", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(entry.Path))
            .OrderByDescending(entry => CudaVersion(entry.Name))
            .Select(entry => entry.Path);

        var roots = environmentRoots.ToList();
        string installationRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "NVIDIA GPU Computing Toolkit", "CUDA");
        if (Directory.Exists(installationRoot))
            roots.AddRange(Directory.GetDirectories(installationRoot, "v*")
                .OrderByDescending(CudaVersionFromPath));

        roots.AddRange((Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => path.Trim().Trim('"')));
        return FindCuFftLibrary(roots);
    }

    internal static DemucsCudaDeviceCatalog EnumerateDevices()
    {
        IntPtr driver = IntPtr.Zero;
        var devices = new List<DemucsCudaDevice>();
        var warnings = new List<string>();
        try
        {
            driver = NativeLibrary.Load("nvcuda.dll");
            Check(Function<Init>(driver, "cuInit")(0), "cuInit");
            Check(Function<DeviceGetCount>(driver, "cuDeviceGetCount")(out int count), "cuDeviceGetCount");
            if (count < 0 || count > 128) throw new InvalidOperationException($"CUDA reported an invalid device count: {count}.");
            var getDevice = Function<DeviceGet>(driver, "cuDeviceGet");
            var getName = Function<DeviceGetName>(driver, "cuDeviceGetName");
            DeviceGetLuid? getLuid = NativeLibrary.TryGetExport(driver, "cuDeviceGetLuid", out IntPtr luidAddress)
                ? Marshal.GetDelegateForFunctionPointer<DeviceGetLuid>(luidAddress) : null;
            for (int ordinal = 0; ordinal < count; ordinal++)
            {
                Check(getDevice(out int device, ordinal), "cuDeviceGet");
                IntPtr name = Marshal.AllocHGlobal(256);
                string deviceName;
                try
                {
                    if (getName(name, 256, device) != 0)
                    {
                        deviceName = $"CUDA device {ordinal}";
                        warnings.Add($"Could not read the name of CUDA device {ordinal}.");
                    }
                    else deviceName = Marshal.PtrToStringAnsi(name) ?? $"CUDA device {ordinal}";
                }
                finally { Marshal.FreeHGlobal(name); }

                long? luid = null;
                if (getLuid is null)
                    warnings.Add("The NVIDIA driver does not expose CUDA device LUIDs; CUDA devices cannot be matched to DirectML adapters.");
                else
                {
                    IntPtr luidBuffer = Marshal.AllocHGlobal(sizeof(long));
                    try
                    {
                        if (getLuid(luidBuffer, out _, device) == 0) luid = Marshal.ReadInt64(luidBuffer);
                        else warnings.Add($"Could not read the LUID of CUDA device {ordinal}; it cannot be matched to DirectML.");
                    }
                    finally { Marshal.FreeHGlobal(luidBuffer); }
                }
                devices.Add(new DemucsCudaDevice(ordinal, deviceName, luid));
            }
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or
            BadImageFormatException or InvalidOperationException)
        {
            warnings.Add($"CUDA device enumeration failed: {error.Message}");
        }
        finally
        {
            if (driver != IntPtr.Zero) NativeLibrary.Free(driver);
        }
        return new DemucsCudaDeviceCatalog(devices.AsReadOnly(),
            warnings.Count == 0 ? null : string.Join(" ", warnings.Distinct(StringComparer.Ordinal)));
    }

    internal static void Transform(Complex[][] frames, bool inverse, int deviceId, string libraryPath)
    {
        if (frames.Length == 0) return;
        if (Environment.GetEnvironmentVariable("DEMUS_CUDA_DISABLE") == "1")
            throw new InvalidOperationException("CUDA disabled by DEMUS_CUDA_DISABLE.");
        IntPtr driver = NativeLibrary.Load("nvcuda.dll");
        IntPtr library = IntPtr.Zero;
        IntPtr context = IntPtr.Zero;
        ulong memory = 0;
        int plan = 0;
        try
        {
            library = NativeLibrary.Load(libraryPath);
            Check(Function<Init>(driver, "cuInit")(0), "cuInit");
            Check(Function<DeviceGet>(driver, "cuDeviceGet")(out int device, deviceId), "cuDeviceGet");
            Check(Function<ContextCreate>(driver, "cuCtxCreate_v2")(out context, 0, device), "cuCtxCreate");
            int length = frames[0].Length;
            var packed = new float[checked(frames.Length * length * 2)];
            for (int t = 0; t < frames.Length; t++)
                for (int j = 0; j < length; j++)
                {
                    int i = 2 * (t * length + j);
                    packed[i] = (float)frames[t][j].Real;
                    packed[i + 1] = (float)frames[t][j].Imaginary;
                }
            nuint bytes = (nuint)(packed.LongLength * sizeof(float));
            Check(Function<Allocate>(driver, "cuMemAlloc_v2")(out memory, bytes), "cuMemAlloc");
            Check(Function<CopyToDevice>(driver, "cuMemcpyHtoD_v2")(memory, packed, bytes), "cuMemcpyHtoD");
            Check(Function<Plan>(library, "cufftPlan1d")(out plan, length, 0x29, frames.Length), "cufftPlan1d");
            Check(Function<Execute>(library, "cufftExecC2C")(plan, memory, memory, inverse ? 1 : -1), "cufftExecC2C");
            Check(Function<CopyToHost>(driver, "cuMemcpyDtoH_v2")(packed, memory, bytes), "cuMemcpyDtoH");
            double scale = inverse ? 1.0 / length : 1;
            for (int t = 0; t < frames.Length; t++)
                for (int j = 0; j < length; j++)
                {
                    int i = 2 * (t * length + j);
                    frames[t][j] = new Complex(packed[i] * scale, packed[i + 1] * scale);
                }
        }
        finally
        {
            if (plan != 0) Function<DestroyPlan>(library, "cufftDestroy")(plan);
            if (memory != 0) Function<Free>(driver, "cuMemFree_v2")(memory);
            if (context != IntPtr.Zero) Function<ContextDestroy>(driver, "cuCtxDestroy_v2")(context);
            if (library != IntPtr.Zero) NativeLibrary.Free(library);
            NativeLibrary.Free(driver);
        }
    }

    private static int CuFftLibraryVersion(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        int separator = name.LastIndexOf('_');
        return separator >= 0 && int.TryParse(name[(separator + 1)..], out int version) ? version : 0;
    }

    private static Version CudaVersion(string name)
    {
        int marker = name.IndexOf("_V", StringComparison.OrdinalIgnoreCase);
        return marker < 0 || !Version.TryParse(name[(marker + 2)..].Replace('_', '.'), out Version? version)
            ? new Version(0, 0)
            : version;
    }

    private static Version CudaVersionFromPath(string path)
    {
        string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        return name.StartsWith('v') && Version.TryParse(name[1..], out Version? version)
            ? version
            : new Version(0, 0);
    }
}
