using ModularAudience.Audio.Vocal;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Globalization;
using System.IO;
using System.Speech.Synthesis;
using System.Xml.Linq;

namespace ModularAudience.Audio.Tests;

[TestClass]
public class VocalSynthTests
{
    [TestMethod]
    public void Render_English_ProducesAudio()
    {
        var s = VocalSynthSettings.FromPreset(VocalVoicePreset.BuiltIn[2], VocalLanguage.English, "Hello world");
        var audio = VocalSynth.Render(s);
        Assert.IsTrue(audio.Data.Length > 0);
        Assert.AreEqual(44100, audio.SampleRate);
    }

    [TestMethod]
    public void Render_German_SchSoundsNotSKH()
    {
        var phonemes = VocalPhonemizer.Phonemize(VocalLanguage.German, "schule");
        Assert.IsTrue(phonemes.Contains("sh"));
    }

    [TestMethod]
    public void Render_SpeedAffectsDuration()
    {
        var s1 = VocalSynthSettings.FromPreset(VocalVoicePreset.BuiltIn[0], VocalLanguage.English, "Hi");
        s1.Speed = 1.0;
        var a1 = VocalSynth.Render(s1);
        var s2 = VocalSynthSettings.FromPreset(VocalVoicePreset.BuiltIn[0], VocalLanguage.English, "Hi");
        s2.Speed = 2.0;
        var a2 = VocalSynth.Render(s2);
        Assert.IsTrue(a2.Data.Length < a1.Data.Length);
    }

    [TestMethod]
    public void Parser_WrappingStrong_ProducesStrongTokens()
    {
        var syntax = VocalEmphasisSyntax.Default();
        var tokens = VocalTextParser.Parse(VocalLanguage.English, "*very* loud", syntax);
        Assert.IsTrue(tokens.OfType<VocalPhonemeToken>().Any(t => t.Emphasis == VocalEmphasis.Strong));
    }

    [TestMethod]
    public void Parser_PauseMarker_ProducesPause()
    {
        var syntax = VocalEmphasisSyntax.Default();
        var tokens = VocalTextParser.Parse(VocalLanguage.English, "hi - there", syntax);
        Assert.IsTrue(tokens.OfType<VocalPauseToken>().Any());
    }

    [TestMethod]
    public void Parser_MultipleSpaces_ProducePause()
    {
        var syntax = VocalEmphasisSyntax.Default();
        var tokens = VocalTextParser.Parse(VocalLanguage.English, "hi   there", syntax);
        Assert.IsTrue(tokens.OfType<VocalPauseToken>().Any());
    }

    [TestMethod]
    public void Parser_QuestionMark_ProducesPitchUp()
    {
        var syntax = VocalEmphasisSyntax.Default();
        var tokens = VocalTextParser.Parse(VocalLanguage.English, "are you ok?", syntax);
        Assert.IsTrue(tokens.OfType<VocalPhonemeToken>().Any(t => t.Emphasis == VocalEmphasis.PitchUp));
    }

    [TestMethod]
    [DataRow(VocalLanguage.English, "name", "n ay m")]
    [DataRow(VocalLanguage.English, "time", "t eye m")]
    [DataRow(VocalLanguage.English, "chat", "tsh ae t")]
    [DataRow(VocalLanguage.English, "this", "dh i s")]
    [DataRow(VocalLanguage.English, "hi", "h eye")]
    [DataRow(VocalLanguage.English, "she", "sh ee")]
    [DataRow(VocalLanguage.English, "know", "n oh")]
    [DataRow(VocalLanguage.English, "hello", "h uh l oh")]
    [DataRow(VocalLanguage.German, "mein", "m eye n")]
    [DataRow(VocalLanguage.German, "heute", "h oy t uh")]
    [DataRow(VocalLanguage.German, "ich", "i ich")]
    [DataRow(VocalLanguage.German, "buch", "b u ch")]
    [DataRow(VocalLanguage.German, "schule", "sh oo l uh")]
    public void Phonemizer_DistinguishesCommonVowelsAndConsonants(VocalLanguage language, string text, string expected)
    {
        var phonemes = VocalPhonemizer.Phonemize(language, text);
        CollectionAssert.AreEqual(expected.Split(' '), phonemes.ToArray());
        Assert.IsTrue(phonemes.All(VocalPhonemeInventory.Contains));
    }

    [TestMethod]
    [Timeout(3000)]
    public void Parser_UnmatchedAndEmptyMarkers_DoNotBlockSpeech()
    {
        var syntax = VocalEmphasisSyntax.Default();
        syntax.Rules.Add(new VocalEmphasisRule { Marker = "", Wrapping = true });
        var tokens = VocalTextParser.Parse(VocalLanguage.English, "*hello", syntax);
        CollectionAssert.AreEqual(
            new[] { "h", "uh", "l", "oh" },
            tokens.OfType<VocalPhonemeToken>().Select(t => t.Phoneme).ToArray());
    }

    [TestMethod]
    public void Render_AdjacentVoicedVowels_KeepTheirPhaseAtTheBoundary()
    {
        var settings = CleanVoice(VocalLanguage.English, "ae");
        using var audio = VocalSynth.Render(settings);
        int boundary = (int)Math.Round(VocalPhonemeInventory.Get("ae").DurationMs * 1.12 * settings.SampleRate / 1000);
        Assert.IsTrue(Math.Abs(audio.Data[boundary]) > 1e-6,
            "The second vowel must not restart at a zero-amplitude boundary.");
    }

    [TestMethod]
    public void Render_LowPitchVowels_RetainTheirDistinctUpperFormants()
    {
        var settings = CleanVoice(VocalLanguage.English, "ee");
        settings.BasePitchHz = 95;
        using var front = VocalSynth.Render(settings);
        settings.Text = "oo";
        using var back = VocalSynth.Render(settings);
        double frontRatio = BandEnergy(front.Data, settings.SampleRate, 1800, 2600)
            / BandEnergy(front.Data, settings.SampleRate, 600, 1100);
        double backRatio = BandEnergy(back.Data, settings.SampleRate, 1800, 2600)
            / BandEnergy(back.Data, settings.SampleRate, 600, 1100);
        Assert.IsTrue(frontRatio > backRatio * 2,
            $"Expected a stronger upper formant for 'ee': {frontRatio} versus {backRatio}.");
    }

    [TestMethod]
    public void Render_SAndSh_HaveDistinctNoiseSpectra()
    {
        var settings = CleanVoice(VocalLanguage.German, "s");
        settings.Noise = 0.6;
        using var s = VocalSynth.Render(settings);
        settings.Text = "sch";
        using var sh = VocalSynth.Render(settings);
        double sBrightness = DifferenceEnergyRatio(s.Data);
        double shBrightness = DifferenceEnergyRatio(sh.Data);
        Assert.IsTrue(sBrightness > shBrightness * 1.15,
            $"Expected 's' to have brighter frication than 'sh': {sBrightness} versus {shBrightness}.");
    }

    [TestMethod]
    [DataRow("...")]
    [DataRow("\u2026")]
    public void AnimalCrossing_Ellipsis_RendersThreeSeparatedPops(string text)
    {
        var settings = CleanVoice(VocalLanguage.AnimalCrossing, text);
        settings.SampleRate = 8000;
        settings.Noise = 0.5;
        using var audio = VocalSynth.Render(settings);
        int[] lengths = [600, 440, 800, 440, 920, 440];
        int offset = 0;
        for (int i = 0; i < lengths.Length; i++)
        {
            var segment = audio.Data.Skip(offset).Take(lengths[i]);
            if (i % 2 == 0) Assert.IsTrue(segment.All(x => x == 0), $"Pause {i / 2} was not silent.");
            else Assert.IsTrue(segment.Any(x => Math.Abs(x) > 0.001), $"Pop {i / 2} was inaudible.");
            offset += lengths[i];
        }
        Assert.AreEqual(offset, audio.Data.Length);
        settings.Speed = 2;
        using var fast = VocalSynth.Render(settings);
        Assert.AreEqual(audio.Data.Length / 2, fast.Data.Length);
    }

    [TestMethod]
    public void AnimalCrossing_AllVoicePresetsAndCustomProfiles_RenderDistinctAudibleSpeech()
    {
        var rendered = new List<float[]>();
        foreach (var preset in VocalVoicePreset.BuiltIn)
        {
            var settings = VocalSynthSettings.FromPreset(preset, VocalLanguage.AnimalCrossing, "Hi...");
            settings.SampleRate = 8000;
            using var audio = VocalSynth.Render(settings);
            Assert.IsTrue(audio.Data.Any(x => Math.Abs(x) > 0.001), $"{preset.Name} was silent.");
            Assert.IsTrue(audio.Data.All(float.IsFinite), $"{preset.Name} produced invalid samples.");
            rendered.Add(audio.Data.ToArray());
        }
        Assert.IsFalse(rendered[0].SequenceEqual(rendered[4]), "Different voice presets produced identical audio.");

        var profile = VocalVoiceProfile.FromPreset(VocalVoicePreset.BuiltIn[0], "Custom");
        profile.BasePitchHz = 110;
        var custom = VocalSynthSettings.FromProfile(profile, VocalLanguage.AnimalCrossing, "Hi...");
        custom.SampleRate = 8000;
        using var customAudio = VocalSynth.Render(custom);
        Assert.IsTrue(customAudio.Data.Any(x => Math.Abs(x) > 0.001));
        Assert.IsFalse(customAudio.Data.SequenceEqual(rendered[0]));
    }

    [TestMethod]
    public void WindowsSpeech_PreservesPunctuationMarkersAndEscapesText()
    {
        var tokens = VocalTextParser.ParseWindowsSpeech(
            VocalLanguage.English, "Hello *world* - <tag>?", VocalEmphasisSyntax.Default(), CancellationToken.None);
        string ssml = VocalWindowsSpeech.BuildSsml(tokens, CultureInfo.GetCultureInfo("en-US"), 1);
        var xml = XDocument.Parse(ssml);
        XNamespace ns = "http://www.w3.org/2001/10/synthesis";
        Assert.IsTrue(xml.Descendants(ns + "prosody").Any(e => (string?)e.Attribute("volume") == "loud" && e.Value == "world"));
        Assert.IsTrue(xml.Descendants(ns + "break").Any(e => (string?)e.Attribute("time") == "120ms"));
        Assert.IsTrue(xml.Descendants(ns + "prosody").Any(e => (string?)e.Attribute("pitch") == "high" && e.Value == "<tag>"));
        Assert.IsTrue(xml.Root!.Value.Contains("<tag>?"));
        Assert.IsFalse(xml.Descendants("tag").Any());
    }

    [TestMethod]
    [Timeout(30000)]
    public void WindowsSpeech_ProducesStereoAudio_AndHonorsSpeedAndVoicePitch()
    {
        using var probe = new SpeechSynthesizer();
        if (!probe.GetInstalledVoices().Any(v => v.Enabled && v.VoiceInfo.Culture.TwoLetterISOLanguageName == "en"))
            Assert.Inconclusive("An English Windows speech voice is not installed.");

        var settings = VocalSynthSettings.FromPreset(VocalVoicePreset.BuiltIn[2], VocalLanguage.English, "Hello world.");
        settings.UseWindowsSpeech = true;
        settings.SampleRate = 16000;
        settings.Channels = 2;
        using var normal = VocalSynth.Render(settings);
        Assert.IsTrue(normal.Data.Any(x => Math.Abs(x) > 0.001));
        Assert.IsTrue(normal.Data.All(float.IsFinite));
        for (int i = 0; i < normal.Data.Length; i += 2)
            Assert.AreEqual(normal.Data[i], normal.Data[i + 1]);

        settings.Speed = 2;
        using var fast = VocalSynth.Render(settings);
        Assert.IsTrue(fast.Data.Length < normal.Data.Length);

        settings.Speed = 1;
        settings.BasePitchHz = 110;
        using var low = VocalSynth.Render(settings);
        settings.BasePitchHz = 260;
        using var high = VocalSynth.Render(settings);
        double lowPitch = MedianPitch(low.Data, settings.SampleRate, settings.Channels);
        double highPitch = MedianPitch(high.Data, settings.SampleRate, settings.Channels);
        Assert.IsTrue(highPitch > lowPitch * 1.15, $"Expected a higher voice pitch: {highPitch} versus {lowPitch} Hz.");
    }

    [TestMethod]
    [Timeout(10000)]
    public void WindowsSpeech_LongRender_CanBeCanceled()
    {
        using var probe = new SpeechSynthesizer();
        if (!probe.GetInstalledVoices().Any(v => v.Enabled && v.VoiceInfo.Culture.TwoLetterISOLanguageName == "en"))
            Assert.Inconclusive("An English Windows speech voice is not installed.");

        var settings = CleanVoice(VocalLanguage.English, string.Concat(Enumerable.Repeat("Hello world. ", 5000)));
        settings.UseWindowsSpeech = true;
        using var cancellation = new CancellationTokenSource();
        cancellation.CancelAfter(30);
        Assert.Throws<OperationCanceledException>(() => VocalSynth.Render(settings, cancellation.Token));
    }

    [TestMethod]
    public void AnimalCrossing_ConcurrentRenders_AreDeterministic()
    {
        var settings = CleanVoice(VocalLanguage.AnimalCrossing, "Hello...");
        settings.SampleRate = 8000;
        settings.Noise = 0.5;
        var jobs = Enumerable.Range(0, 3).Select(_ => Task.Run(() =>
        {
            using var audio = VocalSynth.Render(settings);
            return audio.Data.ToArray();
        })).ToArray();
        Task.WaitAll(jobs);
        Assert.IsTrue(jobs[0].Result.SequenceEqual(jobs[1].Result));
        Assert.IsTrue(jobs[0].Result.SequenceEqual(jobs[2].Result));
    }

    [TestMethod]
    public void Render_RejectsInvalidOrPathologicallyLargeOutput_AndHonorsCancellation()
    {
        var settings = CleanVoice(VocalLanguage.English, "hello");
        settings.Speed = double.NaN;
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => VocalSynth.Render(settings));
        settings.Speed = 0.000000001;
        Assert.ThrowsExactly<InvalidOperationException>(() => VocalSynth.Render(settings));
        settings.Speed = 1;
        settings.UseWindowsSpeech = true;
        settings.Language = VocalLanguage.AnimalCrossing;
        Assert.ThrowsExactly<ArgumentException>(() => VocalSynth.Render(settings));
        settings.Language = VocalLanguage.English;
        settings.Speed = 0.49;
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => VocalSynth.Render(settings));
        settings.UseWindowsSpeech = false;
        settings.Speed = 1;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => VocalSynth.Render(settings, cancellation.Token));
    }

    private static VocalSynthSettings CleanVoice(VocalLanguage language, string text)
    {
        var settings = VocalSynthSettings.FromPreset(VocalVoicePreset.BuiltIn[5], language, text);
        settings.Breathiness = 0;
        settings.Noise = 0;
        settings.Roughness = 0;
        settings.VibratoCents = 0;
        settings.Voicing = 1;
        return settings;
    }

    private static double DifferenceEnergyRatio(float[] samples)
    {
        double difference = 0, energy = 0;
        for (int i = 1; i < samples.Length; i++)
        {
            double delta = samples[i] - samples[i - 1];
            difference += delta * delta;
            energy += samples[i] * samples[i];
        }
        return difference / energy;
    }

    private static double BandEnergy(float[] samples, int rate, int lower, int upper)
    {
        double energy = 0;
        for (int frequency = lower; frequency <= upper; frequency += 25)
        {
            double coefficient = 2 * Math.Cos(2 * Math.PI * frequency / rate);
            double previous = 0, previous2 = 0;
            foreach (float sample in samples)
            {
                double current = sample + coefficient * previous - previous2;
                previous2 = previous;
                previous = current;
            }
            energy += previous * previous + previous2 * previous2 - coefficient * previous * previous2;
        }
        return energy;
    }

    private static double MedianPitch(float[] samples, int rate, int channels)
    {
        int frames = samples.Length / channels;
        int size = rate / 25, hop = rate / 50;
        var pitches = new List<double>();
        for (int start = 0; start + size < frames; start += hop)
        {
            double energy = 0;
            for (int i = 0; i < size; i++) energy += samples[(start + i) * channels] * samples[(start + i) * channels];
            if (energy / size < 0.00015) continue;
            double best = 0;
            int bestLag = 0;
            for (int lag = rate / 400; lag <= rate / 70; lag++)
            {
                double cross = 0, first = 0, second = 0;
                for (int i = lag; i < size; i++)
                {
                    double a = samples[(start + i) * channels], b = samples[(start + i - lag) * channels];
                    cross += a * b;
                    first += a * a;
                    second += b * b;
                }
                double correlation = cross / Math.Sqrt(first * second + 1e-20);
                if (correlation > best) { best = correlation; bestLag = lag; }
            }
            if (best > 0.65) pitches.Add(rate / (double)bestLag);
        }
        Assert.IsTrue(pitches.Count > 0, "No voiced pitch was detected.");
        pitches.Sort();
        return pitches[pitches.Count / 2];
    }

    [TestMethod]
    public void VoiceProfile_RoundTripJson()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vsptest_" + Guid.NewGuid());
        var store = new VocalVoiceProfileStore(dir);
        var profile = VocalVoiceProfile.FromPreset(VocalVoicePreset.BuiltIn[1], "MyProfile");
        store.Save(profile);
        var loaded = store.LoadAll();
        Assert.IsTrue(loaded.Any(p => p.Name == "MyProfile"));
        Directory.Delete(dir, true);
    }

    [TestMethod]
    public void EmphasisSyntax_RoundTripJson()
    {
        var path = Path.Combine(Path.GetTempPath(), "estest_" + Guid.NewGuid() + ".json");
        var store = new VocalEmphasisSyntaxStore(path);
        var syntax = VocalEmphasisSyntax.Default();
        store.Save(syntax);
        var loaded = store.Load();
        Assert.AreEqual(syntax.Rules.Count, loaded.Rules.Count);
        File.Delete(path);
    }
}