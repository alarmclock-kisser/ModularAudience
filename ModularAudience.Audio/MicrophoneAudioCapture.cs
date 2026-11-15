using System.Buffers.Binary;
using NAudio.CoreAudioApi;
using NAudio.Wave;

#pragma warning disable CS0618

namespace ModularAudience.Audio;

public sealed record AudioInputDevice(string Id, string Name)
{
    public override string ToString() => this.Name;
}

public sealed class MicrophoneAudioCapture : IDisposable
{
    private static readonly Guid IeeeFloatSubFormat = new("00000003-0000-0010-8000-00aa00389b71");
    private readonly object writerLock = new();
    private readonly MMDevice device;
    private readonly WasapiCapture capture;
    private readonly TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private WaveFileWriter? writer;
    private Task? stopTask;
    private Exception? failure;
    private float currentPeak;

    private MicrophoneAudioCapture(MMDevice device, WasapiCapture capture, WaveFileWriter writer, string filePath)
    {
        this.device = device;
        this.capture = capture;
        this.writer = writer;
        this.FilePath = filePath;
        this.capture.DataAvailable += this.CaptureDataAvailable;
        this.capture.RecordingStopped += this.CaptureRecordingStopped;
    }

    public string FilePath { get; }

    public float PeakLevel => Volatile.Read(ref this.currentPeak);

    public Exception? Failure => Volatile.Read(ref this.failure);

    public static IReadOnlyList<AudioInputDevice> GetInputDevices()
    {
        using MMDeviceEnumerator enumerator = new();
        MMDeviceCollection endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
        List<AudioInputDevice> devices = new(endpoints.Count);
        foreach (MMDevice endpoint in endpoints)
        {
            devices.Add(new AudioInputDevice(endpoint.ID, endpoint.FriendlyName));
            endpoint.Dispose();
        }

        return devices;
    }

    public static MicrophoneAudioCapture Start(string deviceId, string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        using MMDeviceEnumerator enumerator = new();
        MMDevice device = enumerator.GetDevice(deviceId);
        WasapiCapture? capture = null;
        WaveFileWriter? writer = null;
        try
        {
            capture = new WasapiCapture(device);
            string fullPath = Path.GetFullPath(filePath);
            writer = new WaveFileWriter(fullPath, capture.WaveFormat);
            MicrophoneAudioCapture recording = new(device, capture, writer, fullPath);
            capture.StartRecording();
            return recording;
        }
        catch
        {
            writer?.Dispose();
            capture?.Dispose();
            device.Dispose();
            throw;
        }
    }

    #pragma warning restore CS0618

    public Task StopAsync()
    {
        lock (this.writerLock)
        {
            this.stopTask ??= this.StopCoreAsync();
            return this.stopTask;
        }
    }

    public void Dispose() => this.StopAsync().GetAwaiter().GetResult();

    private async Task StopCoreAsync()
    {
        try
        {
            this.capture.StopRecording();
            await this.stopped.Task.ConfigureAwait(false);
        }
        finally
        {
            this.capture.DataAvailable -= this.CaptureDataAvailable;
            this.capture.RecordingStopped -= this.CaptureRecordingStopped;
            lock (this.writerLock)
            {
                this.writer?.Flush();
                this.writer?.Dispose();
                this.writer = null;
            }

            this.capture.Dispose();
            this.device.Dispose();
        }

        if (this.Failure is Exception failure)
        {
            throw new IOException("Microphone recording failed.", failure);
        }
    }

    private void CaptureDataAvailable(object? sender, WaveInEventArgs e)
    {
        try
        {
            float peak = MeasurePeak(this.capture.WaveFormat, e.Buffer, e.BytesRecorded);
            lock (this.writerLock)
            {
                this.writer?.Write(e.Buffer, 0, e.BytesRecorded);
            }

            Volatile.Write(ref this.currentPeak, peak);
        }
        catch (Exception exception)
        {
            Volatile.Write(ref this.failure, exception);
        }
    }

    private void CaptureRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            Volatile.Write(ref this.failure, e.Exception);
        }

        this.stopped.TrySetResult();
    }

    private static float MeasurePeak(WaveFormat format, byte[] data, int bytesRecorded)
    {
        bool isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat
            || format is WaveFormatExtensible extensible && extensible.SubFormat == IeeeFloatSubFormat;
        if (!isFloat && format.Encoding is not (WaveFormatEncoding.Pcm or WaveFormatEncoding.Extensible))
        {
            throw new NotSupportedException($"Microphone sample format '{format.Encoding}' is not supported.");
        }

        int bytesPerSample = (format.BitsPerSample + 7) / 8;
        if (bytesPerSample is < 1 or > 4)
        {
            throw new NotSupportedException($"Microphone bit depth '{format.BitsPerSample}' is not supported.");
        }

        int sampleCount = bytesRecorded / bytesPerSample;
        float peak = 0;
        for (int index = 0; index < sampleCount; index++)
        {
            int offset = index * bytesPerSample;
            float sample = isFloat
                ? BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4)))
                : format.BitsPerSample switch
                {
                    8 => (data[offset] - 128) / 128f,
                    16 => BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(offset, 2)) / 32768f,
                    24 => ReadInt24(data, offset) / 8388608f,
                    32 => BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4)) / 2147483648f,
                    _ => throw new NotSupportedException($"Microphone bit depth '{format.BitsPerSample}' is not supported.")
                };
            if (float.IsFinite(sample))
            {
                peak = Math.Max(peak, Math.Abs(sample));
            }
        }

        return Math.Clamp(peak, 0, 1);
    }

    private static int ReadInt24(byte[] data, int offset)
    {
        int value = data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16);
        return (value & 0x800000) == 0 ? value : value | unchecked((int)0xff000000);
    }
}
