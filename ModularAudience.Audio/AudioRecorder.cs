using ModularAudience.Audio;
using ModularAudience.Core;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

public static class AudioRecorder
{
    public static string RecordsPath { get; set; } = string.Empty;

    private static WasapiLoopbackCapture? _capture;
    private static MMDevice? _mmDevice;
    public static string CaptureDeviceName => _capture?.WaveFormat.ToString() ?? "N/A";
    public static string MMDeviceName => _mmDevice?.FriendlyName ?? "N/A";
    private static WaveFileWriter? _writer;

    private static bool normalizeOnStop = false;

    public static bool IsRecording { get; private set; } = false;
    public static string? RecordedFile { get; private set; } = null;

    public static DateTime? RecordingStartTime { get; private set; } = null;
    public static TimeSpan? RecordingTime => RecordingStartTime != null ? DateTime.UtcNow - RecordingStartTime : null;

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
            return (float) rate;
        }
    }


    public static async Task StartRecording(string filePath, MMDevice? mmDevice = null)
    {
        if (IsRecording)
        {
            Console.WriteLine("Aufnahme läuft bereits.");
            return;
        }

        RecordingStartTime = DateTime.UtcNow;

        RecordedFile = Path.GetFullPath(filePath);

        try
        {
            MMDevice? captureDevice = null;
            if (mmDevice != null)
            {
                captureDevice = mmDevice;
            }

            // Nutze das gefundene Gerät oder das Standardgerät als Fallback
            _capture = captureDevice != null ? new WasapiLoopbackCapture(captureDevice) : new WasapiLoopbackCapture();

            _writer = new WaveFileWriter(filePath, _capture.WaveFormat);

            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += async (s, e) => await Task.Run(() => OnRecordingStopped(s, e));

            _capture.StartRecording();
            IsRecording = true;
            _mmDevice = captureDevice ?? GetDefaultPlaybackDevice();
            Console.WriteLine($"Aufnahme gestartet. Gerät: {captureDevice?.FriendlyName ?? "Standard"}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fehler beim Starten der Aufnahme: {ex.Message}");
            await Cleanup();
        }
    }

    public static void StopRecording(bool normalizeOutput = false)
    {
        if (!IsRecording)
        {
            Console.WriteLine("Keine Aufnahme aktiv.");
            return;
        }

        IsRecording = false;

        // Optional: Normalisieren
        normalizeOnStop = normalizeOutput;

        Console.WriteLine("Aufnahme wird gestoppt...");
        _capture?.StopRecording();
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
            _capture?.Dispose();
            _capture = new WasapiLoopbackCapture(device);
            Console.WriteLine($"Gerät auf {device.FriendlyName} gesetzt.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fehler beim Setzen des Geräts: {ex.Message}");
        }
    }

    private static void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        _writer?.Write(e.Buffer, 0, e.BytesRecorded);
    }

    private static async void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        Console.WriteLine("Aufnahme gestoppt.");

        if (e.Exception != null)
        {
            Console.WriteLine($"Fehler während der Aufnahme: {e.Exception.Message}");
        }

        // Finalize and cleanup
        _writer?.Flush();


        await Cleanup();
    }

    private static async Task Cleanup()
    {
        _writer?.Dispose();
        _writer = null;
        _capture?.Dispose();
        _capture = null;
        IsRecording = false;

        if (normalizeOnStop && RecordedFile != null && File.Exists(RecordedFile))
        {
            try
            {
                Console.WriteLine("Normalisiere Aufnahme...");
                var obj = new AudioObj(RecordedFile, true);
                if (obj.Data.LongLength > 0)
                {
                    await obj.NormalizeAsync();
                    // Create temp AudioExporter to save normalized file
                    var exporter = new AudioExporter();
                    string outDir = Path.GetDirectoryName(RecordedFile) ?? RecordsPath;
                    await exporter.ExportWavAsync(obj, 24, outDir);
                }
                Console.WriteLine("Normalisierung abgeschlossen.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fehler bei der Normalisierung: {ex.Message}");
            }
            finally
            {
                normalizeOnStop = false;
            }
        }

        normalizeOnStop = false;
    }
}