using System.Reflection;
using ModularAudience.Audio;
using ModularAudience.Forms.Modules.Dialogs;
using NAudio.Wave;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class BreakbeatRecordBeatTests
{
    [TestMethod]
    public void RecordBeatSettingsRoundTripSelectedInputDevice()
    {
        Type settingsType = typeof(BreakbeatPatternEditorDialog).Assembly.GetType(
            "ModularAudience.Forms.Modules.Dialogs.BreakbeatRecordBeatSettings",
            throwOnError: true)!;
        string directory = Path.Combine(Path.GetTempPath(), $"MA_RecordBeatTests_{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "settings.json");

        try
        {
            object settings = Activator.CreateInstance(settingsType, nonPublic: true)!;
            settingsType.GetProperty("InputDeviceId")!.SetValue(settings, "test-input-device");
            settingsType.GetMethod("Save", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(settings, [path]);

            object loaded = settingsType.GetMethod("Load", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [path])!;

            Assert.AreEqual("test-input-device", settingsType.GetProperty("InputDeviceId")!.GetValue(loaded));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            if (Directory.Exists(directory))
            {
                Directory.Delete(directory);
            }
        }
    }

    [TestMethod]
    public void MicrophonePeakMeterHandlesPcmAndFloatInput()
    {
        WaveFormat pcm16 = new(48000, 16, 1);
        Assert.AreEqual(0.5f, MeasurePeak(pcm16, [0x00, 0x40]), 0.0001f);

        WaveFormat pcm24 = new(48000, 24, 1);
        Assert.AreEqual(0.5f, MeasurePeak(pcm24, [0x00, 0x00, 0x40]), 0.0001f);

        WaveFormat float32 = WaveFormat.CreateIeeeFloatWaveFormat(48000, 1);
        Assert.AreEqual(0.75f, MeasurePeak(float32, BitConverter.GetBytes(0.75f)), 0.0001f);
    }

    private static float MeasurePeak(WaveFormat format, byte[] data) =>
        (float)typeof(MicrophoneAudioCapture)
            .GetMethod("MeasurePeak", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [format, data, data.Length])!;
}
