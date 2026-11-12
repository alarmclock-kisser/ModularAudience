using System.Reflection;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Synthesis;
using ModularAudience.Forms.Modules.Dialogs;

namespace ModularAudience.Audio.Tests;

[TestClass]
[DoNotParallelize]
public sealed class InstrumentSynthPersistenceTests
{
    private string directory = "";
    private InstrumentSynthStore store = null!;
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object Field(object target, string name) => target.GetType().GetField(name, Private)!.GetValue(target)!;
    private static object? Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private)!.Invoke(target, args);
    [TestInitialize]
    public void Initialize()
    { directory = Path.Combine(Path.GetTempPath(), "synth-persistence-" + Guid.NewGuid().ToString("N")); store = new(directory); }
    [TestCleanup]
    public void Cleanup() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

    [TestMethod]
    public void SharedDelayPersistsIncludingZeroAndValidatesItsRange()
    {
        Assert.AreEqual(200, store.LoadPrehearDelay());
        store.SavePrehearDelay(575);
        Assert.AreEqual(575, new InstrumentSynthStore(directory).LoadPrehearDelay());
        store.SavePrehearDelay(0);
        Assert.AreEqual(0, new InstrumentSynthStore(directory).LoadPrehearDelay());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => store.SavePrehearDelay(-1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => store.SavePrehearDelay(10001));
    }

    [TestMethod]
    public void EveryFamilyRoundTripsCompletePresetsAndSupportsOverwriteRenameDelete()
    {
        foreach (var family in Enum.GetValues<SynthFamily>())
        {
            var settings = new InstrumentSynthSettings
            {
                Family = family, VoiceIndex = 2, KitIndex = 7, Drum = SynthDrum.OpenHiHat,
                MidiNote = 73, PitchSemitones = -3.25, Bpm = 137.5, NoteBeats = 0.125, DurationMultiplier = 2d / 3,
                AttackMs = 14, DecaySeconds = 1.37, SustainPercent = 34, ReleaseSeconds = 0.79,
                Brightness = 28, Body = 41, Noise = 19, Drive = 21, DetuneCents = 12.25, VibratoCents = 23,
                VibratoHz = 6.7, CutoffHz = 5400, Resonance = 2.3, FilterEnvelope = 67, StrikePosition = 31,
                DrumSweep = 78, DrumSnap = 56, RoomPercent = 22, StereoWidth = 81, GainPercent = 63,
                Seed = 12345, SampleRate = 48000, Channels = 2
            };
            store.Save(family, "My sound", settings, true);
            var loaded = new InstrumentSynthStore(directory).Load(family, "My sound");
            Assert.AreEqual(JsonSerializer.Serialize(settings), JsonSerializer.Serialize(loaded.Settings));
            Assert.AreEqual(InstrumentSynthCatalog.Voices(family)[settings.VoiceIndex].Name, loaded.VoiceType);
            Assert.AreEqual(InstrumentSynthCatalog.DrumKits[settings.KitIndex].Name, loaded.DrumKitType);
            Assert.IsTrue(loaded.Loop);
            Assert.IsTrue(File.Exists(store.PresetsPath(family)));
            settings.Brightness = 77; store.Save(family, "My sound", settings, false);
            Assert.HasCount(1, store.Names(family)); Assert.AreEqual(77d, store.Load(family, "My sound").Settings.Brightness);
            store.Rename(family, "My sound", "Renamed");
            CollectionAssert.AreEqual(new[] { "Renamed" }, store.Names(family));
            store.Save(family, "Other", settings, false);
            Assert.ThrowsExactly<ArgumentException>(() => store.Rename(family, "Renamed", "Other"));
            store.Delete(family, "Renamed"); Assert.HasCount(1, store.Names(family));
            store.Delete(family, "Other"); Assert.HasCount(0, store.Names(family));
        }
    }

    [TestMethod]
    public void FamilyMismatchAndForeignPresetFilesAreRejected()
    {
        var keyboard = new InstrumentSynthSettings { Family = SynthFamily.Keyboard };
        Assert.ThrowsExactly<ArgumentException>(() => store.Save(SynthFamily.Brass, "Wrong", keyboard, false));
        store.Save(SynthFamily.Keyboard, "Keys", keyboard, false);
        File.Copy(store.PresetsPath(SynthFamily.Keyboard), store.PresetsPath(SynthFamily.Brass));
        Assert.ThrowsExactly<InvalidDataException>(() => store.Names(SynthFamily.Brass));
        Assert.ThrowsExactly<ArgumentException>(() => store.Save(SynthFamily.Keyboard, "   ", keyboard, false));
    }

    [STATestMethod]
    public void LegacyPresetWithoutStableTypeNamesRestoresTheSavedKeyboardSubtype()
    {
        string path = store.PresetsPath(SynthFamily.Keyboard);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path,
            """{"Version":1,"Schema":"ModularAudience.SampleSynthesizer.Keyboard.v1","Family":"Keyboard","Presets":[{"Name":"Legacy keyboard","Settings":{"Family":"Keyboard","VoiceIndex":2,"KitIndex":0}}]}""");

        using var dialog = new InstrumentSampleSynthDialog(SynthFamily.Keyboard, store);
        ((ComboBox)Field(dialog, "savedPresets")).SelectedItem = "Legacy keyboard";
        Call(dialog, "LoadPreset");

        var voice = (ComboBox)Field(dialog, "voice");
        Assert.AreEqual(2, voice.SelectedIndex);
        Assert.AreEqual(InstrumentSynthCatalog.Voices(SynthFamily.Keyboard)[2].Name,
            ((InstrumentSynthSettings)Call(dialog, "CaptureSettings")!).VoiceName);
    }

    [STATestMethod]
    public void DelaySynchronizesAcrossFamiliesAndRestoresInNewDialogs()
    {
        using var keys = new InstrumentSampleSynthDialog(SynthFamily.Keyboard, store);
        using var drums = new InstrumentSampleSynthDialog(SynthFamily.DrumSet, store);
        var keyDelay = (NumericUpDown)Field(keys, "prehearDelay");
        var drumDelay = (NumericUpDown)Field(drums, "prehearDelay");
        keyDelay.Value = 675;
        Assert.AreEqual(675m, drumDelay.Value);
        Assert.AreEqual(675, new InstrumentSynthStore(directory).LoadPrehearDelay());
        drumDelay.Value = 0;
        Assert.AreEqual(0m, keyDelay.Value); Assert.AreEqual(SystemColors.GrayText, keyDelay.ForeColor);
        using var brass = new InstrumentSampleSynthDialog(SynthFamily.Brass, new InstrumentSynthStore(directory));
        Assert.AreEqual(0m, ((NumericUpDown)Field(brass, "prehearDelay")).Value);
        Assert.IsFalse(((System.Windows.Forms.Timer)Field(brass, "autoHearTimer")).Enabled);
    }

    [STATestMethod]
    public void PresetUiRestoresTypeTimingNoteFormatLoopAndAllControlsWithoutResettingCustomValues()
    {
        store.SavePrehearDelay(0);
        foreach (var family in Enum.GetValues<SynthFamily>())
        {
            using var dialog = new InstrumentSampleSynthDialog(family, store);
            var numbers = (Dictionary<string, NumericUpDown>)Field(dialog, "numbers");
            var type = (ComboBox)Field(dialog, family == SynthFamily.DrumSet ? "kit" : "voice");
            type.SelectedIndex = 2;
            if (family == SynthFamily.DrumSet) ((ComboBox)Field(dialog, "drum")).SelectedIndex = (int)SynthDrum.OpenHiHat;
            ((DomainUpDown)Field(dialog, "note")).SelectedItem = "F#5";
            ((ComboBox)Field(dialog, "duration")).SelectedIndex = 3;
            ((ComboBox)Field(dialog, "articulation")).SelectedIndex = 2;
            ((ComboBox)Field(dialog, "format")).SelectedIndex = 1;
            ((CheckBox)Field(dialog, "loop")).Checked = true;
            numbers["Brightness"].Value = 17; numbers["Bpm"].Value = 139.5m; numbers["PitchSemitones"].Value = -4.25m;
            numbers["ReleaseSeconds"].Value = 0.27m;
            ((TextBox)Field(dialog, "presetName")).Text = "My preset";
            var expected = (InstrumentSynthSettings)Call(dialog, "CaptureSettings")!;
            Call(dialog, "SavePreset");
            type.SelectedIndex = 0; numbers["Brightness"].Value = 99;
            ((ComboBox)Field(dialog, "duration")).SelectedIndex = 4;
            ((ComboBox)Field(dialog, "format")).SelectedIndex = 0;
            ((CheckBox)Field(dialog, "loop")).Checked = false;
            Call(dialog, "LoadPreset");
            var actual = (InstrumentSynthSettings)Call(dialog, "CaptureSettings")!;
            Assert.AreEqual(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual), family.ToString());
            Assert.IsTrue(((CheckBox)Field(dialog, "loop")).Checked);
            Assert.AreEqual(0m, ((NumericUpDown)Field(dialog, "prehearDelay")).Value, "Loading a sound must not override the shared prehear preference.");
            ((TextBox)Field(dialog, "presetName")).Text = "Renamed"; Call(dialog, "RenamePreset");
            CollectionAssert.AreEqual(new[] { "Renamed" }, store.Names(family));
            Call(dialog, "DeletePreset"); Assert.HasCount(0, ((ComboBox)Field(dialog, "savedPresets")).Items);
        }
    }
}
