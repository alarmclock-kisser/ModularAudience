using ModularAudience.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;

#pragma warning disable CS0618 // WasapiLoopbackCapture is deprecated - use WasapiRecorderBuilder.WithProcessLoopback() instead

public static class AudioRecorder
{
    private const int RollingBufferSeconds = 5 * 60;
    private static readonly object StateLock = new();

    public static string RecordsPath { get; set; } = string.Empty;

    private static WasapiLoopbackCapture? _capture;
    private static MMDevice? _mmDevice;
    public static string CaptureDeviceName => _capture?.WaveFormat.ToString() ?? "N/A";
    public static string MMDeviceName => _mmDevice?.FriendlyName ?? "N/A";
    private static WaveFileWriter? _writer;

    private static System.Threading.Timer? _silenceTimer;
    private static int _lastDataWritten = 0;
    private static TaskCompletionSource? _stopCompletion;
    private static RollingAudioBuffer? _rollingBuffer;

    public static bool IsRecording { get; private set; } = false;
    public static string? RecordedFile { get; private set; } = null;

    public static DateTime? RecordingStartTime { get; private set; } = null;
    public static DateTime? RecordingStopTime { get; private set; } = null;
    public static TimeSpan RecordingPreRoll { get; private set; } = TimeSpan.Zero;
    public static TimeSpan? RecordingTime =>
        RecordingStartTime != null
            ? RecordingPreRoll + ((RecordingStopTime ?? DateTime.UtcNow) - RecordingStartTime.Value)
            : null;

    /// <summary>Raised synchronously when the capture is stopped, before any post-processing (e.g. 24-bit re-export).</summary>
    public static event Action? RecordingStopped;

    /// <summary>Starts the RAM-only five-minute rolling capture used for pre-roll recording.</summary>
    public static async Task StartRollingBufferAsync(MMDevice? mmDevice = null)
    {
        try
        {
            await Task.Run(() => EnsureCaptureStarted(mmDevice)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Rolling recording buffer could not start: {ex.Message}");
        }
    }

    public static float EstimatedBpm => GetPeaksPerMinute();
    public static double MaxDetectionAttention { get; set; } = 4;
    private static readonly List<DateTime> _peakHits = [];
    private static readonly Lock _peakLock = new();
    private static float peakThreshold = 0.95f;
    public static float PeakThreshold
    {
        get => peakThreshold;
        set
        {
            value = Math.Clamp(value, 0.0f, 1.0f);

            if (peakThreshold != value)
            {
                // Reset hits
                _peakHits.Clear();
                Console.WriteLine($"Pegelgrenze auf {value} gesetzt.");
            }

            peakThreshold = value;
        }
    }

    public static float GetPeakVolume(MMDevice? useDevice = null)
    {
        try
        {
            // Nimm übergebenes Gerät oder Standardgerät
            MMDevice? device = useDevice ?? GetDefaultPlaybackDevice();
            if (device == null)
            {
                Console.WriteLine("Kein Gerät ausgewählt.");
                return 0.0f;
            }

            // Mit LoopbackCapture initialisieren (wie in StartRecording)
            using var capture = new WasapiLoopbackCapture(device);

            // Kurze Initialisierung, aber kein echtes Recording starten
            var format = capture.WaveFormat;

            float value = device.AudioMeterInformation.MasterPeakValue;

            CheckPeakHit(value);

            // Pegel über AudioMeterInformation abrufen
            return value;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fehler beim Abrufen der Lautstärke: {ex.Message}");
            return 0.0f;
        }
    }

    private static void CheckPeakHit(float value)
    {
        if (value >= PeakThreshold)
        {
            lock (_peakLock)
            {
                // Add hit if last hit is older than 200ms
                if (_peakHits.Count == 0 || (DateTime.UtcNow - _peakHits.Last()).TotalMilliseconds > 200)
                {
                    _peakHits.Add(DateTime.UtcNow);
                }

                // Alte Einträge (älter als 60s) entfernen
                _peakHits.RemoveAll(t => (DateTime.UtcNow - t).TotalSeconds > MaxDetectionAttention);
            }
        }
    }

    public static float GetPeaksPerMinute()
    {
        lock (_peakLock)
        {
            if (_peakHits.Count < 2)
            {
                return 0.0f;
            }

            // Zeitspanne zwischen erstem und letztem Hit in Sekunden
            double spanSeconds = (_peakHits.Last() - _peakHits.First()).TotalSeconds;
            if (spanSeconds <= 0.0)
            {
                return 0.0f;
            }

            // Rate auf Minuten hochgerechnet
            double rate = (_peakHits.Count - 1) / spanSeconds * 60.0;
            return (float)rate;
        }
    }


    public static Task StartRecording(string filePath, MMDevice? mmDevice = null, TimeSpan? preRoll = null)
    {
        if (IsRecording)
        {
            Console.WriteLine("Aufnahme läuft bereits.");
            return Task.CompletedTask;
        }

        try
        {
            EnsureCaptureStarted(mmDevice);

            lock (StateLock)
            {
                if (IsRecording)
                {
                    return Task.CompletedTask;
                }

                if (_capture == null)
                {
                    throw new InvalidOperationException("The audio capture is unavailable.");
                }

                string fullPath = Path.GetFullPath(filePath);
                WaveFileWriter writer = new(fullPath, _capture.WaveFormat);
                int preRollBytes = GetPreRollBytes(_capture.WaveFormat, preRoll ?? TimeSpan.Zero);
                int availableBytes = _rollingBuffer?.Count ?? 0;
                int actualPreRollBytes = AlignToFrame(Math.Min(preRollBytes, availableBytes), _capture.WaveFormat.BlockAlign);
                byte[] prefix = _rollingBuffer?.GetLast(actualPreRollBytes) ?? [];
                if (prefix.Length > 0)
                {
                    writer.Write(prefix, 0, prefix.Length);
                }

                _writer = writer;
                RecordedFile = fullPath;
                RecordingStartTime = DateTime.UtcNow;
                RecordingStopTime = null;
                RecordingPreRoll = _capture.WaveFormat.AverageBytesPerSecond > 0
                    ? TimeSpan.FromSeconds((double)prefix.Length / _capture.WaveFormat.AverageBytesPerSecond)
                    : TimeSpan.Zero;
                IsRecording = true;
                _lastDataWritten = 0;
                _rollingBuffer?.Clear();
                Console.WriteLine($"Recording started. Device: {_mmDevice?.FriendlyName ?? "Default"}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Recording could not start: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    public static void StopRecording(bool normalizeOutput = false)
    {
        _ = StopRecordingAsync(normalizeOutput);
    }

    public static async Task StopRecordingAsync(bool normalizeOutput = false)
    {
        TaskCompletionSource? completion;
        WaveFileWriter? writer = null;
        string? recordedFile = null;
        bool shouldFinalize = false;

        lock (StateLock)
        {
            if (_stopCompletion != null)
            {
                completion = _stopCompletion;
            }
            else if (!IsRecording)
            {
                return;
            }
            else
            {
                completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _stopCompletion = completion;
                IsRecording = false;
                writer = _writer;
                _writer = null;
                recordedFile = RecordedFile;
                RecordingStopTime = DateTime.UtcNow;
                _rollingBuffer?.Clear();
                shouldFinalize = true;
            }
        }

        if (!shouldFinalize)
        {
            await completion!.Task.ConfigureAwait(false);
            return;
        }

        try
        {
            writer?.Flush();
            writer?.Dispose();
            RecordingStopped?.Invoke();

            if (normalizeOutput && recordedFile != null && File.Exists(recordedFile))
            {
                await NormalizeRecordingAsync(recordedFile).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Recording could not be finalized: {ex.Message}");
        }
        finally
        {
            lock (StateLock)
            {
                if (ReferenceEquals(_stopCompletion, completion))
                {
                    _stopCompletion = null;
                }
            }

            completion.TrySetResult();
        }
    }

    public static async Task ShutdownAsync()
    {
        if (IsRecording)
        {
            await StopRecordingAsync().ConfigureAwait(false);
        }

        WasapiLoopbackCapture? capture;
        System.Threading.Timer? silenceTimer;
        lock (StateLock)
        {
            capture = _capture;
            _capture = null;
            silenceTimer = _silenceTimer;
            _silenceTimer = null;
            _rollingBuffer?.Clear();
            _rollingBuffer = null;
            _lastDataWritten = 0;
        }

        silenceTimer?.Dispose();

        if (capture != null)
        {
            capture.DataAvailable -= OnDataAvailable;
            capture.RecordingStopped -= OnCaptureStopped;
            try
            {
                await Task.Run(capture.StopRecording).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Recording capture shutdown failed: {ex.Message}");
            }
            finally
            {
                capture.Dispose();
            }
        }
    }

    public static MMDevice? GetActivePlaybackDevice()
    {
        var enumerator = new MMDeviceEnumerator();
        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.All, DeviceState.All);

        MMDevice? activeDevice = null;
        float maxPeak = 0.0f;

        foreach (var device in devices)
        {
            float peak = device.AudioMeterInformation.MasterPeakValue;
            if (peak > maxPeak)
            {
                maxPeak = peak;
                activeDevice = device;
            }
        }

        _mmDevice = activeDevice;
        return activeDevice;
    }

    public static MMDevice? GetDefaultPlaybackDevice()
    {
        var enumerator = new MMDeviceEnumerator();
        return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    }

    public static MMDevice[] GetCaptureDevices()
    {
        var enumerator = new MMDeviceEnumerator();
        return enumerator.EnumerateAudioEndPoints(DataFlow.All, DeviceState.All).ToArray();
    }

    public static void SetCaptureDevice(MMDevice? device)
    {
        if (device == null)
        {
            Console.WriteLine("Ungültiges Gerät.");
            return;
        }
        if (_capture != null && IsRecording)
        {
            Console.WriteLine("Aufnahme läuft bereits. Stoppe die Aufnahme, bevor du das Gerät änderst.");
            return;
        }
        try
        {
            lock (StateLock)
            {
                _mmDevice = device;
            }
            Console.WriteLine($"Capture device set to {device.FriendlyName}.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Capture device could not be set: {ex.Message}");
        }
    }

    private static void EnsureCaptureStarted(MMDevice? mmDevice)
    {
        lock (StateLock)
        {
            if (_capture != null)
            {
                return;
            }

            MMDevice? captureDevice = mmDevice ?? _mmDevice;
            WasapiLoopbackCapture capture = captureDevice != null
                ? new WasapiLoopbackCapture(captureDevice)
                : new WasapiLoopbackCapture();
            RollingAudioBuffer rollingBuffer = new(GetPreRollBytes(capture.WaveFormat, TimeSpan.FromSeconds(RollingBufferSeconds)));
            System.Threading.Timer silenceTimer = new(OnSilenceTimerTick, null, Timeout.Infinite, Timeout.Infinite);

            try
            {
                _capture = capture;
                _rollingBuffer = rollingBuffer;
                _silenceTimer = silenceTimer;
                _lastDataWritten = 0;
                _mmDevice = captureDevice ?? GetDefaultPlaybackDevice();
                capture.DataAvailable += OnDataAvailable;
                capture.RecordingStopped += OnCaptureStopped;
                capture.StartRecording();
                silenceTimer.Change(100, 100);
            }
            catch
            {
                silenceTimer.Dispose();
                capture.DataAvailable -= OnDataAvailable;
                capture.RecordingStopped -= OnCaptureStopped;
                capture.Dispose();
                _capture = null;
                _rollingBuffer = null;
                _silenceTimer = null;
                throw;
            }
        }
    }

    private static int GetPreRollBytes(WaveFormat format, TimeSpan duration)
    {
        double seconds = Math.Clamp(duration.TotalSeconds, 0, RollingBufferSeconds);
        long bytes = (long)(format.AverageBytesPerSecond * seconds);
        int boundedBytes = (int)Math.Min(int.MaxValue, bytes);
        return AlignToFrame(boundedBytes, format.BlockAlign);
    }

    private static int AlignToFrame(int bytes, int blockAlign)
    {
        if (blockAlign <= 1)
        {
            return Math.Max(0, bytes);
        }

        return Math.Max(0, bytes - bytes % blockAlign);
    }

    private static void OnCaptureStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
        {
            Console.WriteLine($"Recording capture stopped unexpectedly: {e.Exception.Message}");
        }

        bool recordingWasActive;
        lock (StateLock)
        {
            if (ReferenceEquals(_capture, sender))
            {
                _capture = null;
            }

            recordingWasActive = IsRecording;
        }

        if (recordingWasActive)
        {
            _ = StopRecordingAsync();
        }
    }

    private static void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        lock (StateLock)
        {
            _lastDataWritten = e.BytesRecorded;
            if (IsRecording && _writer != null)
            {
                _writer.Write(e.Buffer, 0, e.BytesRecorded);
            }
            else
            {
                _rollingBuffer?.Append(e.Buffer, 0, e.BytesRecorded);
            }
        }
    }

    private static void OnSilenceTimerTick(object? state)
    {
        try
        {
            lock (StateLock)
            {
                if (_lastDataWritten != 0)
                {
                    _lastDataWritten = 0;
                    return;
                }

                WaveFormat? format = _capture?.WaveFormat;
                if (format == null)
                {
                    return;
                }

                int bytesToWrite = AlignToFrame((int)(format.AverageBytesPerSecond * 0.1), format.BlockAlign);
                byte[] silence = new byte[bytesToWrite];
                if (IsRecording && _writer != null)
                {
                    _writer.Write(silence, 0, silence.Length);
                }
                else
                {
                    _rollingBuffer?.Append(silence, 0, silence.Length);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Silence timer callback failed: {ex.Message}");
        }
    }

    private static async Task NormalizeRecordingAsync(string recordedFile)
    {
        try
        {
            Console.WriteLine("Normalizing recording...");
            var obj = new AudioObj(recordedFile, true);
            if (obj.Data.LongLength > 0)
            {
                await obj.NormalizeAsync();
                var exporter = new AudioExporter();
                string outDir = Path.GetDirectoryName(recordedFile) ?? RecordsPath;
                await exporter.ExportWavAsync(obj, 24, outDir, writeBpmTag: false, customFilePath: recordedFile);
            }
            Console.WriteLine("Recording normalization completed.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Recording normalization failed: {ex.Message}");
        }
    }
}