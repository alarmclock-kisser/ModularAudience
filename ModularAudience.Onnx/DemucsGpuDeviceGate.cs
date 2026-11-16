using System.Collections.Concurrent;

namespace ModularAudience.Onnx;

internal static class DemucsGpuDeviceGate
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.Ordinal);

    internal static IDisposable? Acquire(DemucsLoadOptions options)
    {
        string? key = DeviceKey(options);
        if (key is null) return null;
        var gate = Gates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        gate.Wait();
        return new Releaser(gate);
    }

    internal static async ValueTask<IDisposable?> AcquireAsync(DemucsLoadOptions? options, CancellationToken token)
    {
        string? key = options is null ? null : DeviceKey(options);
        if (key is null) return null;
        var gate = Gates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(token).ConfigureAwait(false);
        return new Releaser(gate);
    }

    private static string? DeviceKey(DemucsLoadOptions options)
    {
        if (options.DeviceLuid is { } luid) return $"LUID:{luid:X16}";
        if (options.UseDirectML) return $"DML:{options.DeviceId}";
        return options.UseCudaCuFft && options.CudaDeviceId >= 0 ? $"CUDA:{options.CudaDeviceId}" : null;
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        private SemaphoreSlim? heldGate = gate;

        public void Dispose() => Interlocked.Exchange(ref this.heldGate, null)?.Release();
    }
}
