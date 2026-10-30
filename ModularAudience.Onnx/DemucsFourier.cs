using MathNet.Numerics.Providers.FourierTransform;
using System.Numerics;
using System.Runtime.InteropServices;

namespace ModularAudience.Onnx;

public sealed class DemucsFourier
{
    private readonly DemucsLoadOptions options;
    private bool cudaFailed;
    public string Backend { get; private set; }
    public int Workers => Math.Clamp(this.options.FourierWorkers, 1, Environment.ProcessorCount);
    public DemucsFourier(DemucsLoadOptions? options = null)
    {
        this.options = options ?? new DemucsLoadOptions { PreferCudaFourier = false };
        this.Backend = $"Parallel CPU FFT ({this.Workers} workers)";
    }

    internal void Transform(Complex[][] frames, bool inverse, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (this.options.PreferCudaFourier && !this.cudaFailed)
        {
            try
            {
                Cuda12Fourier.Transform(frames, inverse, this.options.CudaDeviceId, this.options.CudaToolkitDirectory);
                this.Backend = $"CUDA 12 CuFFT (device {this.options.CudaDeviceId})";
                token.ThrowIfCancellationRequested();
                return;
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                this.cudaFailed = true;
                this.Backend = $"Parallel CPU FFT ({this.Workers} workers); CUDA unavailable: {error.Message}";
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

// Batched CUDA 12 CuFFT. Like CudaFourier in AsynCUDA13.Runtime, but uses the
// stable CUDA driver/CuFFT ABI to coexist with the application's ManagedCuda-13 packages.
// Every context, plan and buffer is scoped to the calling background thread.
internal static class Cuda12Fourier
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Init(uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int DeviceGet(out int device, int ordinal);
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

    internal static void Transform(Complex[][] frames, bool inverse, int deviceId, string directory)
    {
        if (frames.Length == 0) return;
        if (Environment.GetEnvironmentVariable("DEMUS_CUDA_DISABLE") == "1")
            throw new InvalidOperationException("CUDA disabled by DEMUS_CUDA_DISABLE.");
        string? toolkit = string.IsNullOrWhiteSpace(directory) ? Environment.GetEnvironmentVariable("CUDA_PATH_V12_8") : directory;
        if (string.IsNullOrWhiteSpace(toolkit))
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA GPU Computing Toolkit", "CUDA");
            if (Directory.Exists(root)) toolkit = Directory.EnumerateDirectories(root, "v12.*").OrderDescending().FirstOrDefault();
        }
        string libraryPath = string.IsNullOrWhiteSpace(toolkit) ? "cufft64_11.dll" : Path.Combine(toolkit, "bin", "cufft64_11.dll");
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
}
