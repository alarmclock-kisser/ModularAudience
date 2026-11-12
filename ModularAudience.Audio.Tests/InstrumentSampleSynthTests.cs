using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Synthesis;
using ModularAudience.Forms;
using ModularAudience.Forms.Modules.Dialogs;
using ModularAudience.Generators;

namespace ModularAudience.Audio.Tests;

[TestClass]
[DoNotParallelize]
public sealed class InstrumentSampleSynthTests
{
    private string storageDirectory = "";
    private InstrumentSynthStore store = null!;
    [TestInitialize]
    public void InitializeStore()
    {
        storageDirectory = Path.Combine(Path.GetTempPath(), "synth-tests-" + Guid.NewGuid().ToString("N"));
        store = new InstrumentSynthStore(storageDirectory);
    }
    [TestCleanup]
    public void CleanupStore() { if (Directory.Exists(storageDirectory)) Directory.Delete(storageDirectory, true); }
    private static InstrumentSynthSettings Short(SynthFamily family) => new()
    {
        Family = family, SampleRate = 22050, Channels = 1, NoteBeats = 0.25, ReleaseSeconds = 0.08,
        RoomPercent = 0, AttackMs = 2, CutoffHz = 10000, SustainPercent = 60
    };

    [TestMethod]
    public void EveryFamilyAndVoiceProducesDistinctFiniteAudibleSamples()
    {
        foreach (var family in Enum.GetValues<SynthFamily>().Where(f => f != SynthFamily.DrumSet))
        {
            var hashes = new HashSet<string>();
            for (int i = 0; i < InstrumentSynthCatalog.Voices(family).Count; i++)
            {
                var s = Short(family); s.VoiceIndex = i; s.ApplyVoiceDefaults(); s.ReleaseSeconds = 0.08;
                using var audio = InstrumentSampleSynth.Render(s);
                Assert.IsTrue(audio.Data.All(float.IsFinite), s.VoiceName);
                Assert.IsTrue(audio.Data.Max(Math.Abs) is > 0.005f and <= 0.951f, s.VoiceName);
                Assert.AreEqual((int)Math.Ceiling((s.GateSeconds + s.ReleaseSeconds) * s.SampleRate), audio.Data.Length);
                Assert.IsTrue(hashes.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                    System.Runtime.InteropServices.MemoryMarshal.AsBytes(audio.Data.AsSpan())))), s.VoiceName);
            }
        }
    }

    [TestMethod]
    public void NineteenDrumKitsSupportEveryDrumAndHaveDistinctCharacter()
    {
        Assert.HasCount(19, InstrumentSynthCatalog.DrumKits);
        var kicks = new HashSet<string>();
        for (int kit = 0; kit < InstrumentSynthCatalog.DrumKits.Count; kit++)
        {
            foreach (var drum in Enum.GetValues<SynthDrum>())
            {
                var s = Short(SynthFamily.DrumSet); s.KitIndex = kit; s.Drum = drum;
                using var audio = InstrumentSampleSynth.Render(s);
                Assert.IsTrue(audio.Data.All(float.IsFinite), s.VoiceName);
                Assert.IsTrue(audio.Data.Any(v => Math.Abs(v) > 0.003f), s.VoiceName);
                if (drum == SynthDrum.Kick)
                    Assert.IsTrue(kicks.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                        System.Runtime.InteropServices.MemoryMarshal.AsBytes(audio.Data.AsSpan())))));
            }
        }
    }

    [STATestMethod]
    public void PedalHiHatUsesClosedHiHatBreakbeatTag()
    {
        var mapping = typeof(InstrumentSampleSynthDialog).GetMethod(
            "GetDrumsetElement",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        DrumsetElement tag = (DrumsetElement)mapping.Invoke(null, [SynthDrum.PedalHiHat])!;

        Assert.AreEqual(DrumsetElement.HiHatClosed, tag);
    }

    [STATestMethod]
    public void BothSampleSynthMenusListEveryInstrumentFamily()
    {
        using var menu = WindowMain.CreateSampleSynthesizerMenu(_ => { });
        string?[] actual = menu.DropDownItems.Cast<ToolStripItem>().Select(item => item.Text).ToArray();
        string[] expected = Enum.GetValues<SynthFamily>()
            .Select(InstrumentSynthCatalog.FamilyName)
            .ToArray();

        CollectionAssert.AreEqual(expected, actual);
    }

    [TestMethod]
    public void MusicalDurationPitchSeedAndEnvelopeControlsWork()
    {
        var s = Short(SynthFamily.SynthBassAndLead); s.VoiceIndex = 0; s.ApplyVoiceDefaults();
        s.ReleaseSeconds = 0.1; s.NoteBeats = 1; s.Bpm = 120; s.DurationMultiplier = 1.5;
        Assert.AreEqual(0.75, s.GateSeconds, 1e-10);
        s.DurationMultiplier = 2d / 3; Assert.AreEqual(1d / 3, s.GateSeconds, 1e-10);
        s.DurationMultiplier = 1; s.FilterEnvelope = 0; s.CutoffHz = 20000; s.Brightness = 100;
        s.MidiNote = 48; s.VibratoCents = 0; s.DetuneCents = 0; s.StereoWidth = 0; s.Drive = 0;
        using var root = InstrumentSampleSynth.Render(s);
        s.PitchSemitones = 12;
        using var octave = InstrumentSampleSynth.Render(s);
        int Crossings(AudioObj a) => Enumerable.Range(1, a.Data.Length / 2 - 1).Count(i => a.Data[i - 1] < 0 && a.Data[i] >= 0);
        Assert.IsTrue(Crossings(octave) > Crossings(root) * 1.7);
        s.GainPercent = 0; using var silent = InstrumentSampleSynth.Render(s); Assert.IsTrue(silent.Data.All(v => v == 0));
        var drum = Short(SynthFamily.DrumSet); drum.Drum = SynthDrum.Snare;
        using var first = InstrumentSampleSynth.Render(drum); using var repeated = InstrumentSampleSynth.Render(drum);
        CollectionAssert.AreEqual(first.Data, repeated.Data);
        drum.Seed++; using var changed = InstrumentSampleSynth.Render(drum);
        Assert.IsFalse(first.Data.SequenceEqual(changed.Data));
        using var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => InstrumentSampleSynth.Render(drum, cts.Token));
    }

    [TestMethod]
    public void SampleNamesIncludeVoiceKeyDurationBpmAndPitchOffset()
    {
        var s = Short(SynthFamily.SynthBassAndLead); s.VoiceIndex = 0; s.ApplyVoiceDefaults();
        s.MidiNote = 44; s.NoteBeats = 1; s.DurationMultiplier = 1; s.Bpm = 120; s.PitchSemitones = 0;
        using (var straight = InstrumentSampleSynth.Render(s))
            Assert.AreEqual("Sub Anchor · G#2 · 1/4 [120]", straight.Name);
        s.DurationMultiplier = 1.5; s.Bpm = 137.5;
        using (var dotted = InstrumentSampleSynth.Render(s))
            Assert.AreEqual("Sub Anchor · G#2 · 1/4 Dotted [137.5]", dotted.Name);
        s.DurationMultiplier = 2d / 3;
        using (var triplet = InstrumentSampleSynth.Render(s))
            Assert.AreEqual("Sub Anchor · G#2 · 1/4 Triplet [137.5]", triplet.Name);
        s.DurationMultiplier = 1; s.NoteBeats = 4; s.PitchSemitones = 2;
        using (var bar = InstrumentSampleSynth.Render(s))
            Assert.AreEqual("Sub Anchor · G#2 +2 st · 1 Bar [137.5]", bar.Name);
    }

    [TestMethod]
    public void PatternInstrumentRenderingUsesEditorTempoDurationAndPitch()
    {
        InstrumentSynthSettings source = Short(SynthFamily.Keyboard);
        source.MidiNote = 60;
        source.PitchSemitones = 0.25;
        source.NoteBeats = 2;
        source.DurationMultiplier = 1.5;
        BreakbeatPatternNote note = new(0, 0, 128, PitchSemitones: 3.5f);
        BreakbeatTrackSettings track = new(DefaultPitchSemitones: 1f);

        InstrumentSynthSettings renderedSettings = BreakbeatPatternInstrumentRenderer.CreateSettingsForNote(
            source, note, track, 80f);

        Assert.AreEqual(80d, renderedSettings.Bpm);
        Assert.AreEqual(0.5d, renderedSettings.NoteBeats, 1e-12);
        Assert.AreEqual(1d, renderedSettings.DurationMultiplier);
        Assert.AreEqual(65, renderedSettings.MidiNote);
        Assert.AreEqual(-0.25d, renderedSettings.PitchSemitones, 1e-12);
        Assert.AreEqual(0.375d, renderedSettings.GateSeconds, 1e-12);
        using AudioObj rendered = InstrumentSampleSynth.Render(renderedSettings);
        Assert.AreEqual((int)Math.Ceiling((0.375 + source.ReleaseSeconds) * source.SampleRate), rendered.Data.Length);
        Assert.AreEqual(80f, rendered.Bpm);

        InstrumentSynthSettings drum = Short(SynthFamily.DrumSet);
        drum.PitchSemitones = 2;
        InstrumentSynthSettings pitchedDrum = BreakbeatPatternInstrumentRenderer.CreateSettingsForNote(
            drum,
            note with { PitchSemitones = 3f },
            new BreakbeatTrackSettings(DefaultPitchSemitones: -1f),
            80f);
        Assert.AreEqual(60, pitchedDrum.MidiNote);
        Assert.AreEqual(4d, pitchedDrum.PitchSemitones);
        Assert.AreEqual(0.5d, pitchedDrum.NoteBeats, 1e-12);
        Assert.AreEqual(80f, pitchedDrum.Bpm);
    }

    [TestMethod]
    public async Task PatternRendererMatchesDirectSynthesisAtTheNotePitch()
    {
        InstrumentSynthSettings source = Short(SynthFamily.Keyboard);
        source.MidiNote = 60;
        source.PitchSemitones = 0.25;
        BreakbeatPatternNote note = new(0, 0, 128, PitchSemitones: 3.5f);
        BreakbeatTrackSettings track = new(DefaultPitchSemitones: 1f);
        InstrumentSynthSettings exactSettings = BreakbeatPatternInstrumentRenderer.CreateSettingsForNote(
            source, note, track, 80f);
        using AudioObj exactSample = InstrumentSampleSynth.Render(exactSettings);
        using AudioObj placeholder = new()
        {
            Data = [],
            SampleRate = exactSettings.SampleRate,
            Channels = exactSettings.Channels,
            Duration = exactSample.Duration,
            Bpm = 80f
        };
        using BreakbeatInstrumentSampleCache cache = new(0);

        AudioObj? instrumentPattern = await BreakbeatPatternInstrumentRenderer.RenderAsync(
            [note],
            [placeholder],
            [source],
            [track],
            1,
            80f,
            4,
            0,
            "Instrument",
            CancellationToken.None,
            cache);
        using (instrumentPattern)
        using (AudioObj directPattern = await ModularAudience.Generators.BreakbeatGenerator_V2.RenderPatternNotesAsync(
                   [note with { PitchSemitones = 0f }],
                   [exactSample],
                   1,
                   80f,
                   4,
                   0,
                   "Direct"))
        {
            Assert.IsNotNull(instrumentPattern);
            CollectionAssert.AreEqual(directPattern.Data, instrumentPattern!.Data);
        }
    }

    [TestMethod]
    public void OctaveTranspositionsIncludeBothEndpointsAndExactThirdSteps()
    {
        double[] semitones = InstrumentSampleSynthDialog.GetOctaveTranspositions(1);
        Assert.HasCount(13, semitones);
        Assert.AreEqual(0d, semitones[0]);
        Assert.AreEqual(12d, semitones[^1]);

        double[] thirds = InstrumentSampleSynthDialog.GetOctaveTranspositions(1d / 3);
        Assert.HasCount(37, thirds);
        Assert.AreEqual(1d / 3, thirds[1], 1e-12);
        Assert.AreEqual(12d, thirds[^1]);

        Assert.HasCount(97, InstrumentSampleSynthDialog.GetOctaveTranspositions(0.125));
    }

    [STATestMethod]
    public void PatternEditorOkAddsConfiguredInstrumentAndUsesC4ByDefault()
    {
        InstrumentSynthSettings? confirmed = null;
        using var dialog = new InstrumentSampleSynthDialog(
            SynthFamily.Keyboard,
            store,
            settings => confirmed = settings);
        var note = (DomainUpDown)typeof(InstrumentSampleSynthDialog)
            .GetField("note", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
        Assert.AreEqual("C4", note.Text);
        var ok = dialog.Controls.Find("button_ok", true).OfType<Button>().Single();
        Assert.AreEqual("OK", ok.Text);

        dialog.Show();
        ok.PerformClick();
        Application.DoEvents();

        Assert.IsNotNull(confirmed);
        Assert.AreEqual(60, confirmed.MidiNote);
        Assert.AreEqual("Concert Grand", confirmed.VoiceName);
    }

    [STATestMethod]
    public void PreviewLoopsAndSwitchesToUpdatedSample()
    {
        var type = typeof(InstrumentSampleSynthDialog).GetNestedType("SynthPreview", BindingFlags.NonPublic)!;
        var provider = (NAudio.Wave.ISampleProvider)Activator.CreateInstance(type, [1000, 1])!;
        type.GetProperty("Loop")!.SetValue(provider, true);
        type.GetMethod("Update")!.Invoke(provider, [new float[] { 0.4f, 0.4f, 0.4f }]);
        var buffer = new float[100]; provider.Read(buffer.AsSpan());
        Assert.IsTrue(buffer.All(v => v > 0));
        type.GetMethod("Update")!.Invoke(provider, [new float[] { -0.4f, -0.4f }]);
        provider.Read(buffer.AsSpan()); Assert.IsTrue(buffer.All(v => v < 0));
        type.GetProperty("Loop")!.SetValue(provider, false);
        provider.Read(buffer.AsSpan()); Assert.IsTrue((bool)type.GetProperty("Finished")!.GetValue(provider)!);
    }

    [STATestMethod]
    public void PrehearDelayDefaultsTo200DebouncesEverySoundControlAndZeroDisablesIt()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        using var dialog = new InstrumentSampleSynthDialog(SynthFamily.Keyboard, store);
        object Field(string name) => typeof(InstrumentSampleSynthDialog).GetField(name, flags)!.GetValue(dialog)!;
        var delay = (NumericUpDown)Field("prehearDelay");
        var timer = (System.Windows.Forms.Timer)Field("autoHearTimer");
        Assert.AreEqual(200m, delay.Value);
        Assert.IsFalse(timer.Enabled, "Opening the window must not automatically play.");
        var numbers = (Dictionary<string, NumericUpDown>)Field("numbers");
        numbers["Brightness"].Value = 33;
        Assert.IsTrue(timer.Enabled); Assert.AreEqual(200, timer.Interval);
        Assert.AreEqual(true, Field("autoHearPending"));
        delay.Value = 0;
        Assert.IsFalse(timer.Enabled); Assert.AreEqual(SystemColors.GrayText, delay.ForeColor);
        numbers["Brightness"].Value = 34; Assert.IsFalse(timer.Enabled);
        delay.Value = 350;
        Assert.AreEqual(SystemColors.WindowText, delay.ForeColor);
        ((ComboBox)Field("voice")).SelectedIndex = 1;
        Assert.IsTrue(timer.Enabled); Assert.AreEqual(350, timer.Interval);
        delay.Value = 0; delay.Value = 350;
        ((DomainUpDown)Field("note")).SelectedIndex = 40;
        Assert.IsTrue(timer.Enabled);
        delay.Value = 0; delay.Value = 350;
        ((ComboBox)Field("duration")).SelectedIndex = 3;
        Assert.IsTrue(timer.Enabled);
        delay.Value = 0;
        Assert.AreEqual(false, Field("autoHearPending"));
    }

    [STATestMethod]
    public void SpaceShortcutIsFormWideAndTriggersOnlyOncePerKeyPress()
    {
        using var dialog = new InstrumentSampleSynthDialog(SynthFamily.Keyboard, store);
        Assert.IsTrue(dialog.KeyPreview);
        Assert.IsTrue(InstrumentSampleSynthDialog.ShouldStartSpacePrehear(Keys.Space, false, false));
        Assert.IsFalse(InstrumentSampleSynthDialog.ShouldStartSpacePrehear(Keys.Space, false, true));
        Assert.IsFalse(InstrumentSampleSynthDialog.ShouldStartSpacePrehear(Keys.Space, true, false));
        Assert.IsFalse(InstrumentSampleSynthDialog.ShouldStartSpacePrehear(Keys.Enter, false, false));
    }

    [STATestMethod]
    public void InitialHeightFitsEachFamilyAndManualShrinkingRestoresParameterScrolling()
    {
        using WinFormsTestContext uiContext = new();
        store.SavePrehearDelay(0);
        foreach (var family in Enum.GetValues<SynthFamily>())
        {
            using var dialog = new InstrumentSampleSynthDialog(family, store);
            ((System.Windows.Forms.Timer)typeof(InstrumentSampleSynthDialog)
                .GetField("debounce", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!).Stop();
            dialog.Show();
            Application.DoEvents();
            var parameters = (FlowLayoutPanel)dialog.Controls.Find("panel_parameters", true).Single();
            int preferredHeight = parameters.GetPreferredSize(new Size(parameters.Width, 0)).Height;
            int availableHeight = Screen.FromControl(dialog).WorkingArea.Height;
            int maximumParameterHeight = parameters.ClientSize.Height + Math.Max(0, availableHeight - dialog.Height) / 2;
            Assert.AreEqual(FormBorderStyle.Sizable, dialog.FormBorderStyle);
            if (preferredHeight <= maximumParameterHeight)
                Assert.IsFalse(parameters.VerticalScroll.Visible, $"{family}: all initial parameters must fit without scrolling.");
            else
                Assert.AreEqual(Math.Max(dialog.MinimumSize.Height, availableHeight), dialog.Height);

            dialog.Height = dialog.MinimumSize.Height;
            dialog.PerformLayout();
            Assert.IsTrue(parameters.VerticalScroll.Visible, $"{family}: manually shrinking must allow scrolling.");
            int resizedHeight = dialog.Height;
            dialog.Hide();
            dialog.Show();
            Application.DoEvents();
            Assert.AreEqual(resizedHeight, dialog.Height, "Reopening must not override a manual resize.");
        }
    }

    [STATestMethod]
    public void DialogsAreModelessAndCreateIntoTheirOwnFamilyCollection()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        foreach (var family in Enum.GetValues<SynthFamily>())
        {
            using var dialog = new InstrumentSampleSynthDialog(family, store);
            object Field(string name) => typeof(InstrumentSampleSynthDialog).GetField(name, flags)!.GetValue(dialog)!;
            object? Call(string name, params object[] args) => typeof(InstrumentSampleSynthDialog).GetMethod(name, flags)!.Invoke(dialog, args);
            ((NumericUpDown)Field("prehearDelay")).Value = 0;
            dialog.Show(); Assert.IsFalse(dialog.Modal);
            var numbers = (Dictionary<string, NumericUpDown>)Field("numbers");
            Assert.AreEqual(0.25m, numbers["PitchSemitones"].Increment);
            Assert.AreEqual(-16m, numbers["PitchSemitones"].Minimum);
            Assert.AreEqual(16m, numbers["PitchSemitones"].Maximum);
            numbers["ReleaseSeconds"].Value = 0.05m;
            ((ComboBox)Field("duration")).SelectedIndex = 2;
            ((System.Windows.Forms.Timer)Field("debounce")).Stop();
            Task render = (Task)Call("RenderAsync")!; DateTime deadline = DateTime.UtcNow.AddSeconds(15);
            while (!render.IsCompleted && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(2); }
            render.GetAwaiter().GetResult();
            Call("AddToCollection"); Call("AddToCollection");
            var collection = (AudioCollectionView)Field("collection");
            try
            {
                var audioCollection = (AudioCollection)typeof(AudioCollectionView).GetField("AudioC", flags)!.GetValue(collection)!;
                Assert.HasCount(2, audioCollection.Audios);
                Assert.AreEqual(audioCollection.Audios[0].Name + " 2", audioCollection.Audios[1].Name);
                Assert.AreEqual("Synthesized " + InstrumentSynthCatalog.FamilyName(family), collection.Text);
                if (family == SynthFamily.DrumSet)
                {
                    Assert.HasCount(19, ((ComboBox)Field("kit")).Items);
                    Assert.AreEqual(DrumsetElement.Kick, audioCollection.Audios[0].Tag);
                }
            }
            finally
            {
                collection.Close(); collection.Dispose();
                ((System.Collections.IList)typeof(WindowMain).GetField("CollectionViews", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!).Remove(collection);
            }
        }
    }
}
