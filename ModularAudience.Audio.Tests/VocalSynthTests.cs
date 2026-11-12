using ModularAudience.Audio.Vocal;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Speech.Synthesis;
using System.Windows.Forms;
using System.Xml.Linq;
using ModularAudience.Forms.Modules.Dialogs;
using NAudio.Wave;

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
    [DataRow(VocalLanguage.English, "intrigued", "i n t r ee g d")]
    [DataRow(VocalLanguage.German, "mein", "m eye n")]
    [DataRow(VocalLanguage.German, "heute", "h oy t de-schwa")]
    [DataRow(VocalLanguage.German, "ich", "i ich")]
    [DataRow(VocalLanguage.German, "buch", "b u ch")]
    [DataRow(VocalLanguage.German, "schule", "sh oo l de-schwa")]
    public void Phonemizer_DistinguishesCommonVowelsAndConsonants(VocalLanguage language, string text, string expected)
    {
        var phonemes = VocalPhonemizer.Phonemize(language, text);
        CollectionAssert.AreEqual(expected.Split(' '), phonemes.ToArray());
        Assert.IsTrue(phonemes.All(VocalPhonemeInventory.Contains));
    }

    [TestMethod]
    [DataRow(VocalLanguage.French, "Bonjour, c'est l'été !", "fr-on")]
    [DataRow(VocalLanguage.Spanish, "¡Hola, niño!", "y")]
    [DataRow(VocalLanguage.Italian, "Ciao, città!", "tsh")]
    [DataRow(VocalLanguage.Chinese, "你好，世界！", "ow")]
    [DataRow(VocalLanguage.Japanese, "こんにちは、世界！", "tsh")]
    [DataRow(VocalLanguage.Turkish, "Merhaba, çalışmak!", "tr-i")]
    [DataRow(VocalLanguage.Arabic, "مرحبا، جهاد", "arab-h")]
    [DataRow(VocalLanguage.Russian, "Привет, мир!", "tap-r")]
    public void MultilingualPhrases_UseLanguageSoundsAndRenderWithVoiceProfiles(
        VocalLanguage language, string text, string expectedSound)
    {
        var tokens = VocalTextParser.Parse(language, text, VocalEmphasisSyntax.Default())
            .OfType<VocalPhonemeToken>().ToArray();
        Assert.IsTrue(tokens.Any(token => token.Phoneme == expectedSound), $"{language}: {string.Join(" ", tokens.Select(t => t.Phoneme))}");
        Assert.IsTrue(tokens.All(token => VocalPhonemeInventory.Contains(token.Phoneme)));
        var profiles = VocalVoicePreset.BuiltIn.Select(preset =>
            (preset.Name, Settings: VocalSynthSettings.FromPreset(preset, language, text))).ToList();
        var custom = VocalVoiceProfile.FromPreset(VocalVoicePreset.BuiltIn[2], "Custom");
        profiles.Add((custom.Name, VocalSynthSettings.FromProfile(custom, language, text)));
        foreach (var (name, settings) in profiles)
        {
            settings.SampleRate = 8000;
            using var audio = VocalSynth.Render(settings);
            Assert.IsTrue(audio.Data.Any(sample => Math.Abs(sample) > 0.001), $"{language}, {name} was silent.");
            Assert.IsTrue(audio.Data.All(float.IsFinite));
        }
    }

    [TestMethod]
    [DataRow(VocalLanguage.French, "été", "e\u0301te\u0301")]
    [DataRow(VocalLanguage.Japanese, "食べる", "taberu")]
    [DataRow(VocalLanguage.Japanese, "コーヒー", "koohii")]
    [DataRow(VocalLanguage.Japanese, "がっこう", "gakkou")]
    [DataRow(VocalLanguage.Japanese, "学校", "gakkou")]
    [DataRow(VocalLanguage.Russian, "привет", "privet")]
    [DataRow(VocalLanguage.Russian, "мир", "mir")]
    [DataRow(VocalLanguage.Russian, "елена", "yelena")]
    public void MultilingualSpellings_ProduceEquivalentSounds(VocalLanguage language, string native, string romanized)
    {
        CollectionAssert.AreEqual(
            VocalPhonemizer.Phonemize(language, native).ToArray(),
            VocalPhonemizer.Phonemize(language, romanized).ToArray());
    }

    [TestMethod]
    [DataRow(VocalLanguage.Spanish, "canción", "o")]
    [DataRow(VocalLanguage.Italian, "città", "a")]
    [DataRow(VocalLanguage.Turkish, "kitap", "a")]
    [DataRow(VocalLanguage.Russian, "привет", "e")]
    public void MultilingualStress_UsesTheLanguageSpecificSyllable(VocalLanguage language, string word, string stressedSound)
    {
        var phones = VocalTextParser.Parse(language, word, VocalEmphasisSyntax.Default())
            .OfType<VocalPhonemeToken>().ToArray();
        var stressed = phones.Single(phone => phone.Stressed);
        Assert.AreEqual(stressedSound, stressed.Phoneme);
        Assert.AreNotSame(phones.First(phone => VocalPhonemeInventory.Get(phone.Phoneme).IsVowel), stressed);
    }

    [TestMethod]
    public void Mandarin_TonesAndContextualHanziReadingsMatchPinyin()
    {
        var syntax = VocalEmphasisSyntax.Default();
        var hanzi = VocalTextParser.Parse(VocalLanguage.Chinese, "你好", syntax)
            .OfType<VocalPhonemeToken>().ToArray();
        var romanized = VocalTextParser.Parse(VocalLanguage.Chinese, "ni2hao3", syntax)
            .OfType<VocalPhonemeToken>().ToArray();
        CollectionAssert.AreEqual(hanzi.Select(p => (p.Phoneme, p.Tone)).ToArray(),
            romanized.Select(p => (p.Phoneme, p.Tone)).ToArray());
        Assert.IsTrue(hanzi.Any(p => p.Tone == 2));
        Assert.IsTrue(hanzi.Any(p => p.Tone == 3));
        CollectionAssert.AreEqual(
            VocalPhonemizer.Phonemize(VocalLanguage.Chinese, "银行").ToArray(),
            VocalPhonemizer.Phonemize(VocalLanguage.Chinese, "yin2hang2").ToArray());
        CollectionAssert.AreEqual(
            VocalPhonemizer.Phonemize(VocalLanguage.Chinese, "nǐ").ToArray(),
            VocalPhonemizer.Phonemize(VocalLanguage.Chinese, "ni3").ToArray());
        CollectionAssert.AreEqual(new[] { "n" }, VocalPhonemizer.Phonemize(VocalLanguage.Chinese, "嗯").ToArray());
    }

    [TestMethod]
    public void Arabic_VowelMarksAndSunLettersDoNotCreateExtraSounds()
    {
        CollectionAssert.AreEqual(
            new[] { "m", "de-a-short", "tap-r", "arab-h", "de-a-short", "b", "de-a-short", "n" },
            VocalPhonemizer.Phonemize(VocalLanguage.Arabic, "مَرْحَبًا").ToArray());
        var article = VocalPhonemizer.Phonemize(VocalLanguage.Arabic, "السَّلَامُ");
        Assert.AreEqual("s", article[1]);
        Assert.AreEqual("s", article[2]);
        Assert.AreEqual(1, article.Count(sound => sound == "l"));
    }

    [TestMethod]
    public void Japanese_FinalSmallTsuRemainsAnAudibleClosure()
    {
        CollectionAssert.AreEqual(new[] { "a", "sil" },
            VocalPhonemizer.Phonemize(VocalLanguage.Japanese, "あっ").ToArray());
    }

    [TestMethod]
    public async Task MultilingualPhonemizers_AreSafeForConcurrentRendersAndCancellation()
    {
        var samples = new[]
        {
            (VocalLanguage.French, "bonjour"), (VocalLanguage.Spanish, "niño"),
            (VocalLanguage.Chinese, "你好世界"), (VocalLanguage.Japanese, "私は学生です"),
        };
        var expected = samples.Select(sample => VocalPhonemizer.Phonemize(sample.Item1, sample.Item2).ToArray()).ToArray();
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < samples.Length; i++)
                CollectionAssert.AreEqual(expected[i], VocalPhonemizer.Phonemize(samples[i].Item1, samples[i].Item2).ToArray());
        })));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            VocalPhonemizer.Phonemize(VocalLanguage.Japanese, "日本語", cancellation.Token));
    }

    [TestMethod]
    [DataRow(VocalLanguage.French, "bonjour", "bonjourrrrrrr", "de-r")]
    [DataRow(VocalLanguage.Spanish, "perro", "perrrrrro", "trill-r")]
    [DataRow(VocalLanguage.Italian, "caro", "carrrrrro", "trill-r")]
    [DataRow(VocalLanguage.Turkish, "merhaba", "merrrrrhaba", "tap-r")]
    [DataRow(VocalLanguage.Russian, "мир", "миррррр", "tap-r")]
    public void MultilingualRepeatedLetters_ProduceSustainedSounds(
        VocalLanguage language, string normal, string extended, string sound)
    {
        Assert.IsTrue(VocalPhonemizer.Phonemize(language, extended).Count(phone => phone == sound) >= 3);
        var settings = CleanVoice(language, normal);
        settings.SampleRate = 8000;
        using var plain = VocalSynth.Render(settings);
        settings.Text = extended;
        using var longer = VocalSynth.Render(settings);
        Assert.IsTrue(longer.Duration > plain.Duration + TimeSpan.FromMilliseconds(120));
    }

    [TestMethod]
    public void Mandarin_FallingToneChangesRenderedPitch()
    {
        var settings = CleanVoice(VocalLanguage.Chinese, "ma4");
        settings.SampleRate = 16000;
        settings.BasePitchHz = 180;
        settings.Speed = 0.5;
        using var audio = VocalSynth.Render(settings);
        double early = PitchInRange(audio.Data, settings.SampleRate, 0.17, 0.23, 120, 260);
        double late = PitchInRange(audio.Data, settings.SampleRate, 0.28, 0.34, 120, 260);
        Assert.IsTrue(early > late * 1.08, $"The falling Mandarin tone did not fall: {early} to {late} Hz.");
    }

    [TestMethod]
    public void MultilingualInput_CapitalizationAndUnsupportedCharactersUseBestEffortFallback()
    {
        foreach (var (language, text) in new[]
        {
            (VocalLanguage.French, "BONJOUR"), (VocalLanguage.Turkish, "IĞDIR"),
            (VocalLanguage.Russian, "ПРИВЕТ"),
        })
            Assert.IsTrue(VocalTextParser.Parse(language, text, VocalEmphasisSyntax.Default())
                .OfType<VocalPhonemeToken>().All(token => token.Capitalized));
        Assert.IsTrue(VocalPhonemizer.Phonemize(VocalLanguage.Turkish, "IĞDIR").Contains("tr-i"));
        foreach (var (language, text) in new[]
        {
            (VocalLanguage.Chinese, "你好🙂"),
            (VocalLanguage.Japanese, "こんにちは🙂"),
            (VocalLanguage.Arabic, "مرحبا🙂"),
        })
        {
            var fallback = VocalPhonemizer.Phonemize(language, text);
            Assert.IsTrue(fallback.Count > 0, $"{language} produced no best-effort pronunciation.");
            Assert.IsTrue(fallback.Contains("unsupported"), $"{language} did not retain the unsupported fragment.");
        }
        CollectionAssert.AreEqual(
            VocalPhonemizer.Phonemize(VocalLanguage.Russian, "привет").ToArray(),
            VocalPhonemizer.Phonemize(VocalLanguage.Russian, "«привет»").ToArray());
    }

    [TestMethod]
    public void IpaNotationAndClicks_RenderIndependentlyOfSelectedLanguage()
    {
        const string notation = "[ˈʘŋǃ]";
        foreach (var language in new[]
        {
            VocalLanguage.English, VocalLanguage.German, VocalLanguage.French, VocalLanguage.Spanish,
            VocalLanguage.Italian, VocalLanguage.Chinese, VocalLanguage.Japanese, VocalLanguage.Turkish,
            VocalLanguage.Arabic, VocalLanguage.Russian,
        })
        {
            var tokens = VocalTextParser.Parse(language, notation, VocalEmphasisSyntax.Default())
                .OfType<VocalPhonemeToken>().ToArray();
            CollectionAssert.AreEqual(new[] { "beat-p", "ng", "beat-k" },
                tokens.Select(token => token.Phoneme).ToArray(), language.ToString());
            CollectionAssert.AreEqual(new[] { "fr-an" },
                VocalTextParser.Parse(language, "ã", VocalEmphasisSyntax.Default())
                    .OfType<VocalPhonemeToken>().Select(token => token.Phoneme).ToArray(), language.ToString());

            var settings = VocalSynthSettings.FromPreset(VocalVoicePreset.BuiltIn[2], language, notation);
            settings.SampleRate = 8000;
            using var audio = VocalSynth.Render(settings);
            Assert.IsTrue(audio.Data.Any(sample => Math.Abs(sample) > 0.001), $"{language} rendered silence.");
        }
    }

    [TestMethod]
    public void UnsupportedSounds_AreSkippedByDefaultOrCensoredAt100Hz()
    {
        var settings = VocalSynthSettings.FromPreset(VocalVoicePreset.BuiltIn[2], VocalLanguage.Arabic, "🙂");
        settings.SampleRate = 8000;
        using var skipped = VocalSynth.Render(settings);
        Assert.AreEqual(8, skipped.Data.Length);
        Assert.IsTrue(skipped.Data.All(sample => sample == 0));

        settings.BeepForUnsupportedSounds = true;
        using var censored = VocalSynth.Render(settings);
        Assert.AreEqual(1200, censored.Data.Length);
        int[] risingCrossings = Enumerable.Range(1, censored.Data.Length - 1)
            .Where(index => censored.Data[index - 1] <= 0 && censored.Data[index] > 0)
            .ToArray();
        double averagePeriod = risingCrossings.Zip(risingCrossings.Skip(1),
            (first, second) => second - first).Average();
        Assert.AreEqual(80, averagePeriod, 1.0, "The censor tone should be 100 Hz at 8 kHz.");
    }

    [TestMethod]
    public void MixedScriptsAndUnknownFragments_RenderWithSpeechAndBeatboxFallbacks()
    {
        const string text = "جهادʘ🙂";
        foreach (var language in new[] { VocalLanguage.English, VocalLanguage.French, VocalLanguage.Arabic })
        {
            var tokens = VocalTextParser.Parse(language, text, VocalEmphasisSyntax.Default())
                .OfType<VocalPhonemeToken>().ToArray();
            Assert.IsTrue(tokens.Any(token => token.Phoneme == "beat-p"), language.ToString());
            Assert.IsTrue(tokens.Any(token => token.Phoneme == "unsupported"), language.ToString());

            var settings = VocalSynthSettings.FromPreset(VocalVoicePreset.BuiltIn[2], language, text);
            settings.SampleRate = 8000;
            using var audio = VocalSynth.Render(settings);
            Assert.IsTrue(audio.Data.Any(sample => Math.Abs(sample) > 0.001), $"{language} rendered silence.");
        }
    }

    [TestMethod]
    [DataRow("der", "d de-e-long de-r-glide")]
    [DataRow("her", "h de-e-long de-r-glide")]
    [DataRow("gehst", "g de-e-long s t")]
    [DataRow("beschaffenheit", "b de-schwa sh de-a-short f de-schwa n h eye t")]
    [DataRow("schwer", "sh v de-e-long de-r-glide")]
    [DataRow("wasser", "v de-a-short s de-er")]
    [DataRow("lieblingsfrau", "l ee b l i ng s f de-r ow")]
    [DataRow("horrorfilm", "h de-o-short de-r o de-r-glide f i l m")]
    [DataRow("affenscheide", "de-a-short f de-schwa n sh eye d de-schwa")]
    [DataRow("eisenschmiede", "eye z de-schwa n sh m ee d de-schwa")]
    [DataRow("den", "d de-e-long n")]
    [DataRow("dem", "d de-e-long m")]
    [DataRow("jemandem", "y e m a n d de-schwa m")]
    [DataRow("problem", "p de-r o b l de-e-long m")]
    [DataRow("Regelversto\u00df", "de-r de-e-long g de-schwa l f e de-r-glide sh t de-o-long s")]
    [DataRow("segel", "z de-e-long g de-schwa l")]
    [DataRow("riegel", "de-r ee g de-schwa l")]
    [DataRow("verstehen", "f e de-r-glide sh t de-e-long de-schwa n")]
    [DataRow("verspielt", "f e de-r-glide sh p ee l t")]
    [DataRow("wespe", "v e s p de-schwa")]
    [DataRow("fu\u00df", "f oo s")]
    [DataRow("rosen", "de-r de-o-long z de-schwa n")]
    [DataRow("rollen", "de-r de-o-short l de-schwa n")]
    [DataRow("gestern", "g e s t e de-r-glide n")]
    [DataRow("feuer", "f oy de-er")]
    [DataRow("mir", "m ee de-r-glide")]
    [DataRow("dir", "d ee de-r-glide")]
    [DataRow("wir", "v ee de-r-glide")]
    [DataRow("ihr", "ee de-r-glide")]
    [DataRow("wird", "v i de-r-glide t")]
    public void Phonemizer_GermanFeedbackWords_UseDistinctVowelsAndR(string word, string expected)
    {
        var phonemes = VocalPhonemizer.Phonemize(VocalLanguage.German, word);
        CollectionAssert.AreEqual(expected.Split(' '), phonemes.ToArray());
        Assert.IsTrue(phonemes.All(VocalPhonemeInventory.Contains));
    }

    [TestMethod]
    public void Parser_GermanUnstressedPrefix_DoesNotTakeWordStress()
    {
        var phonemes = VocalTextParser.Parse(VocalLanguage.German, "Beschaffenheit", VocalEmphasisSyntax.Default())
            .OfType<VocalPhonemeToken>().ToArray();
        Assert.IsFalse(phonemes.First(p => p.Phoneme == "de-schwa").Stressed);
        Assert.AreEqual("de-a-short", phonemes.Single(p => p.Stressed).Phoneme);
    }

    [TestMethod]
    [DataRow("Versto\u00df", "de-o-long")]
    [DataRow("verstehen", "de-e-long")]
    [DataRow("verspielt", "ee")]
    [DataRow("Vers", "e")]
    [DataRow("Regelversto\u00df", "de-e-long")]
    public void Parser_GermanVerPrefix_StressesTheRootWhenPresent(string word, string expected)
    {
        var phonemes = VocalTextParser.Parse(VocalLanguage.German, word, VocalEmphasisSyntax.Default())
            .OfType<VocalPhonemeToken>().ToArray();
        Assert.AreEqual(expected, phonemes.Single(p => p.Stressed).Phoneme);
    }

    [TestMethod]
    [DataRow("Affe", "Afe")]
    [DataRow("Masse", "Mase")]
    [DataRow("Backe", "Bake")]
    [DataRow("denn", "den")]
    public void Render_GermanShortVowels_AreAudiblyShorterAtHalfSpeed(string shortWord, string longWord)
    {
        var settings = VocalSynthSettings.FromPreset(
            VocalVoicePreset.BuiltIn[2], VocalLanguage.German, shortWord);
        settings.Speed = 0.5;
        using var shorter = VocalSynth.Render(settings);
        settings.Text = longWord;
        using var longer = VocalSynth.Render(settings);
        Assert.IsTrue(longer.Duration - shorter.Duration > TimeSpan.FromMilliseconds(70),
            $"{shortWord}: {shorter.Duration}; {longWord}: {longer.Duration}.");
        Assert.IsTrue(shorter.Data.Any(sample => Math.Abs(sample) > 0.001));
    }

    [TestMethod]
    [DataRow("Von der Beschaffenheit her")]
    [DataRow("Lieblingsfrau")]
    [DataRow("Horrorfilm")]
    [DataRow("Affenscheide")]
    [DataRow("Eisenschmiede")]
    [DataRow("Gehst du mir mal den K\u00e4se holen?")]
    [DataRow("Regelversto\u00df")]
    [DataRow("Shaitaan")]
    public void Render_GermanFeedbackPhrases_AtRequestedVoiceAndSpeed(string phrase)
    {
        var settings = VocalSynthSettings.FromPreset(
            VocalVoicePreset.BuiltIn[2], VocalLanguage.German, phrase);
        settings.Speed = 0.5;
        using var audio = VocalSynth.Render(settings);
        Assert.IsTrue(audio.Data.Any(sample => Math.Abs(sample) > 0.001));
        Assert.IsTrue(audio.Data.All(float.IsFinite));
    }

    [TestMethod]
    public void Render_GermanR_HasDistinctFricationFromEnglishR()
    {
        var settings = VocalSynthSettings.FromPreset(VocalVoicePreset.BuiltIn[2], VocalLanguage.German, "r");
        settings.SampleRate = 16000;
        settings.Speed = 0.5;
        using var german = VocalSynth.Render(settings);
        settings.Language = VocalLanguage.English;
        using var english = VocalSynth.Render(settings);
        double germanBrightness = DifferenceEnergyRatio(german.Data);
        double englishBrightness = DifferenceEnergyRatio(english.Data);
        Assert.IsTrue(germanBrightness > englishBrightness * 5.5,
            $"Expected German uvular frication: {germanBrightness} versus {englishBrightness}.");
    }

    [TestMethod]
    public void Render_GermanR_GentlyFluttersEvenWithoutRepeatedLetters()
    {
        var settings = CleanVoice(VocalLanguage.German, "r");
        settings.SampleRate = 16000;
        settings.BasePitchHz = 200;
        settings.Speed = 0.5;
        using var german = VocalSynth.Render(settings);
        settings.Language = VocalLanguage.English;
        using var english = VocalSynth.Render(settings);
        double germanVariation = ShortTimeLevelVariation(german.Data, settings.SampleRate);
        double englishVariation = ShortTimeLevelVariation(english.Data, settings.SampleRate);
        Assert.IsTrue(germanVariation > englishVariation + 0.035 && germanVariation < 0.25,
            $"Expected a gentle German R flutter: German {germanVariation}, English {englishVariation}.");
    }

    [TestMethod]
    public void Render_GermanVocalicR_GlidesFromPrecedingVowel()
    {
        var settings = CleanVoice(VocalLanguage.German, "mir");
        settings.SampleRate = 16000;
        settings.BasePitchHz = 150;
        settings.Speed = 0.5;
        using var audio = VocalSynth.Render(settings);
        int glideFrames = (int)Math.Round(VocalPhonemeInventory.Get("de-r-glide").DurationMs
            * settings.SampleRate / (1000 * settings.Speed));
        int start = audio.Data.Length - glideFrames;
        int window = settings.SampleRate / 40;
        var early = audio.Data.Skip(start + glideFrames / 5).Take(window).ToArray();
        var late = audio.Data.Skip(start + glideFrames * 3 / 5).Take(window).ToArray();
        double earlyRatio = BandEnergy(early, settings.SampleRate, 1200, 1600)
            / BandEnergy(early, settings.SampleRate, 1750, 2100);
        double lateRatio = BandEnergy(late, settings.SampleRate, 1200, 1600)
            / BandEnergy(late, settings.SampleRate, 1750, 2100);
        Assert.IsTrue(lateRatio > earlyRatio * 2,
            $"Expected the vocalic R to move away from the preceding vowel: {earlyRatio} to {lateRatio}.");
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
    [DataRow(VocalLanguage.German, "Shaitaan", "a")]
    [DataRow(VocalLanguage.German, "Shaitaanrrrr", "a")]
    [DataRow(VocalLanguage.German, "Zoo", "de-o-long")]
    [DataRow(VocalLanguage.German, "taaoooo", "a")]
    [DataRow(VocalLanguage.English, "tataat", "ae")]
    [DataRow(VocalLanguage.English, "tataar", "ae")]
    [DataRow(VocalLanguage.Turkish, "saati", "a")]
    [DataRow(VocalLanguage.Japanese, "sataa", "a")]
    [DataRow(VocalLanguage.Russian, "моот", "o")]
    [DataRow(VocalLanguage.Russian, "моотаа", "ah")]
    [DataRow(VocalLanguage.Russian, "меет", "e")]
    public void Render_DoubleVowels_SustainWithoutRestartingPitch(
        VocalLanguage language, string text, string sound)
    {
        var settings = CleanVoice(language, text);
        settings.SampleRate = 48000;
        settings.BasePitchHz = 200;
        settings.Speed = 0.5;
        var phones = VocalTextParser.Parse(language, text, settings.EmphasisSyntax)
            .OfType<VocalPhonemeToken>().ToArray();
        int pair = Enumerable.Range(0, phones.Length - 1).FirstOrDefault(i =>
            phones[i].Phoneme == sound && phones[i + 1].Phoneme == sound, -1);
        Assert.IsTrue(pair >= 0, $"Expected a double {sound}: {string.Join(" ", phones.Select(p => p.Phoneme))}");

        using var audio = VocalSynth.Render(settings);
        double boundary = TimeAtVowelBoundary(phones, pair + 1, settings);
        double before = PitchInRange(audio.Data, settings.SampleRate, boundary - 0.085, boundary - 0.025, 150, 260);
        double after = PitchInRange(audio.Data, settings.SampleRate, boundary + 0.025, boundary + 0.085, 150, 260);
        Assert.IsTrue(after < before * 1.005,
            $"{language} '{text}' restarted its vowel: {before} to {after} Hz.");
    }

    [TestMethod]
    public void Render_ThreeVowels_StillRepeatAndExtendTheSound()
    {
        var settings = CleanVoice(VocalLanguage.German, "tata");
        settings.SampleRate = 48000;
        settings.BasePitchHz = 200;
        settings.Speed = 0.5;
        using var single = VocalSynth.Render(settings);
        settings.Text = "tataa";
        using var doubled = VocalSynth.Render(settings);
        settings.Text = "tataaa";
        using var tripled = VocalSynth.Render(settings);
        Assert.IsTrue(doubled.Duration > single.Duration + TimeSpan.FromMilliseconds(190));
        Assert.IsTrue(tripled.Duration > doubled.Duration + TimeSpan.FromMilliseconds(190));

        var phones = VocalTextParser.Parse(settings.Language, settings.Text, settings.EmphasisSyntax)
            .OfType<VocalPhonemeToken>().ToArray();
        int run = Enumerable.Range(0, phones.Length - 2).First(i =>
            phones[i].Phoneme == "a" && phones[i + 1].Phoneme == "a" && phones[i + 2].Phoneme == "a");
        double boundary = TimeAtVowelBoundary(phones, run + 2, settings);
        double before = PitchInRange(tripled.Data, settings.SampleRate, boundary - 0.085, boundary - 0.025, 150, 260);
        double after = PitchInRange(tripled.Data, settings.SampleRate, boundary + 0.025, boundary + 0.085, 150, 260);
        Assert.IsTrue(after > before * 1.01, $"The repeated vowel did not restart: {before} to {after} Hz.");
    }

    [TestMethod]
    [DataRow(VocalLanguage.German, "mauuu", "oo")]
    [DataRow(VocalLanguage.English, "mouuu", "uh")]
    [DataRow(VocalLanguage.Japanese, "touuu", "oo")]
    [DataRow(VocalLanguage.Italian, "quuuà", "oo")]
    [DataRow(VocalLanguage.Chinese, "shiii", "ee")]
    public void Render_ThreeVowels_AfterDiphthongRemainExpressive(
        VocalLanguage language, string text, string sound)
    {
        var settings = CleanVoice(language, text);
        settings.SampleRate = 48000;
        settings.BasePitchHz = 200;
        settings.Speed = 0.5;
        var phones = VocalTextParser.Parse(settings.Language, settings.Text, settings.EmphasisSyntax)
            .OfType<VocalPhonemeToken>().ToArray();
        int pair = Enumerable.Range(0, phones.Length - 1).First(i =>
            phones[i].Phoneme == sound && phones[i + 1].Phoneme == sound);
        using var audio = VocalSynth.Render(settings);
        double boundary = TimeAtVowelBoundary(phones, pair + 1, settings);
        double before = PitchInRange(audio.Data, settings.SampleRate, boundary - 0.085, boundary - 0.025, 150, 260);
        double after = PitchInRange(audio.Data, settings.SampleRate, boundary + 0.025, boundary + 0.085, 150, 260);
        Assert.IsTrue(after > before * 1.004, $"The repeated vowel after the diphthong did not restart: {before} to {after} Hz.");
    }

    [TestMethod]
    [DataRow(VocalLanguage.French, "sa", "saaa")]
    [DataRow(VocalLanguage.Spanish, "sa", "saaa")]
    [DataRow(VocalLanguage.Italian, "sa", "saaa")]
    public void Render_ThreeVowels_ExtendAcrossLanguagePhonemizers(
        VocalLanguage language, string normal, string extended)
    {
        var settings = CleanVoice(language, normal);
        settings.SampleRate = 8000;
        using var plain = VocalSynth.Render(settings);
        settings.Text = extended;
        using var longer = VocalSynth.Render(settings);
        Assert.IsTrue(longer.Duration > plain.Duration + TimeSpan.FromMilliseconds(150),
            $"{language}: {plain.Duration} versus {longer.Duration}.");
    }

    [TestMethod]
    public void Render_DistinctVowelClusters_KeepTheirArticulations()
    {
        var settings = CleanVoice(VocalLanguage.English, "tateaea");
        settings.SampleRate = 48000;
        settings.BasePitchHz = 200;
        settings.Speed = 0.5;
        var phones = VocalTextParser.Parse(settings.Language, settings.Text, settings.EmphasisSyntax)
            .OfType<VocalPhonemeToken>().ToArray();
        int pair = Enumerable.Range(0, phones.Length - 1).First(i =>
            phones[i].Phoneme == "ee" && phones[i + 1].Phoneme == "ee");
        using var audio = VocalSynth.Render(settings);
        double boundary = TimeAtVowelBoundary(phones, pair + 1, settings);
        double before = PitchInRange(audio.Data, settings.SampleRate, boundary - 0.085, boundary - 0.025, 150, 260);
        double after = PitchInRange(audio.Data, settings.SampleRate, boundary + 0.025, boundary + 0.085, 150, 260);
        Assert.IsTrue(after > before * 1.01, $"Distinct vowel clusters were merged: {before} to {after} Hz.");
    }

    [TestMethod]
    public void Render_SeparateMandarinTones_AreNotMerged()
    {
        var settings = CleanVoice(VocalLanguage.Chinese, "a4a4");
        settings.SampleRate = 48000;
        settings.BasePitchHz = 200;
        settings.Speed = 0.5;
        var phones = VocalTextParser.Parse(settings.Language, settings.Text, settings.EmphasisSyntax)
            .OfType<VocalPhonemeToken>().ToArray();
        Assert.AreEqual(2, phones.Length);
        Assert.IsTrue(phones.All(phone => phone.Tone == 4));
        using var audio = VocalSynth.Render(settings);
        double boundary = TimeAtVowelBoundary(phones, 1, settings);
        double before = PitchInRange(audio.Data, settings.SampleRate, boundary - 0.085, boundary - 0.025, 120, 270);
        double after = PitchInRange(audio.Data, settings.SampleRate, boundary + 0.025, boundary + 0.085, 120, 270);
        Assert.IsTrue(after > before * 1.15, $"The second falling tone did not restart: {before} to {after} Hz.");
    }

    [TestMethod]
    public void Render_SeparateNeutralMandarinTones_AreNotMerged()
    {
        var settings = CleanVoice(VocalLanguage.Chinese, "a5a5");
        settings.SampleRate = 48000;
        settings.BasePitchHz = 200;
        settings.Speed = 0.5;
        var phones = VocalTextParser.Parse(settings.Language, settings.Text, settings.EmphasisSyntax)
            .OfType<VocalPhonemeToken>().ToArray();
        Assert.AreEqual(2, phones.Length);
        using var audio = VocalSynth.Render(settings);
        double boundary = TimeAtVowelBoundary(phones, 1, settings);
        double before = PitchInRange(audio.Data, settings.SampleRate, boundary - 0.085, boundary - 0.025, 150, 260);
        double after = PitchInRange(audio.Data, settings.SampleRate, boundary + 0.025, boundary + 0.085, 150, 260);
        Assert.IsTrue(after > before * 1.01, $"The second neutral syllable did not restart: {before} to {after} Hz.");
    }

    [TestMethod]
    public void Render_AdjacentVowels_KeepSeparateEmphasis()
    {
        var settings = CleanVoice(VocalLanguage.Turkish, "a*a*");
        settings.SampleRate = 48000;
        settings.Speed = 0.5;
        var phones = VocalTextParser.Parse(settings.Language, settings.Text, settings.EmphasisSyntax)
            .OfType<VocalPhonemeToken>().ToArray();
        Assert.AreEqual(2, phones.Length);
        Assert.AreEqual(VocalEmphasis.Strong, phones[1].Emphasis);
        using var audio = VocalSynth.Render(settings);
        int boundary = (int)Math.Round(TimeAtVowelBoundary(phones, 1, settings) * settings.SampleRate);
        int window = settings.SampleRate / 20;
        double before = Math.Sqrt(audio.Data.Skip(boundary - window - window / 2).Take(window)
            .Average(sample => (double)sample * sample));
        double after = Math.Sqrt(audio.Data.Skip(boundary + window / 2).Take(window)
            .Average(sample => (double)sample * sample));
        Assert.IsTrue(after > before * 1.1, $"The second vowel lost its emphasis: {before} to {after}.");
    }

    [TestMethod]
    public void Render_AnimalCrossing_RepeatedVowelsStaySeparate()
    {
        var settings = CleanVoice(VocalLanguage.AnimalCrossing, "aa");
        settings.SampleRate = 16000;
        settings.Speed = 0.5;
        using var audio = VocalSynth.Render(settings);
        int boundary = (int)Math.Round(VocalPhonemeInventory.Get("animal-a").DurationMs
            * settings.SampleRate / (1000 * settings.Speed));
        Assert.AreEqual(0f, audio.Data[boundary]);
        Assert.IsTrue(audio.Data.Skip(boundary + 1).Any(sample => Math.Abs(sample) > 0.001));
    }

    private static double TimeAtVowelBoundary(
        IReadOnlyList<VocalPhonemeToken> phones, int count, VocalSynthSettings settings)
    {
        int frames = 0;
        foreach (var phone in phones.Take(count))
        {
            double duration = VocalPhonemeInventory.Get(phone.Phoneme).DurationMs * (phone.Stressed ? 1.12 : 1);
            frames += (int)Math.Round(duration * settings.SampleRate / (1000 * settings.Speed));
        }
        return frames / (double)settings.SampleRate;
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

    [TestMethod]
    [DataRow(VocalLanguage.German, "Klenker", "Klenkerrrrrr", "de-r")]
    [DataRow(VocalLanguage.English, "hiss", "hissssss", "s")]
    [DataRow(VocalLanguage.English, "cool", "coooool", "oo")]
    public void RepeatedLetters_SustainSuitableSounds_WithoutStretchingNormalDoubles(
        VocalLanguage language, string normal, string extended, string sound)
    {
        var plainPhones = VocalPhonemizer.Phonemize(language, normal);
        var extendedPhones = VocalPhonemizer.Phonemize(language, extended);
        Assert.IsTrue(plainPhones.Count(phone => phone == sound) <= 1);
        Assert.IsTrue(extendedPhones.Count(phone => phone == sound) >= 3);
        var settings = CleanVoice(language, normal);
        settings.SampleRate = 8000;
        using var plain = VocalSynth.Render(settings);
        settings.Text = extended;
        using var stretched = VocalSynth.Render(settings);
        Assert.IsTrue(stretched.Duration > plain.Duration + TimeSpan.FromMilliseconds(150));
    }

    [TestMethod]
    public void RepeatedR_ProducesAnAudibleTrill()
    {
        var settings = CleanVoice(VocalLanguage.German, "rrrrrrrrrrrr");
        settings.SampleRate = 16000;
        using var audio = VocalSynth.Render(settings);
        int block = settings.SampleRate / 100;
        double sum = 0, real = 0, imaginary = 0;
        for (int start = settings.SampleRate / 10; start + block < settings.SampleRate * 7 / 10; start += block)
        {
            double energy = 0;
            for (int i = start; i < start + block; i++) energy += audio.Data[i] * audio.Data[i];
            double level = Math.Sqrt(energy / block);
            double phase = 2 * Math.PI * 22 * (start + block / 2.0) / settings.SampleRate;
            sum += level;
            real += level * Math.Cos(phase);
            imaginary += level * Math.Sin(phase);
        }
        double modulation = 2 * Math.Sqrt(real * real + imaginary * imaginary) / sum;
        Assert.IsTrue(modulation > 0.35, $"The repeated R had insufficient trill modulation: {modulation}.");
    }

    [TestMethod]
    public void WindowsSpeech_UsesPronunciationOverridesForIntriguedAndExtendedSounds()
    {
        var syntax = VocalEmphasisSyntax.Default();
        var tokens = VocalTextParser.ParseWindowsSpeech(
            VocalLanguage.English, "intrigued coooool rrrrr", syntax, CancellationToken.None);
        var xml = XDocument.Parse(VocalWindowsSpeech.BuildSsml(tokens, CultureInfo.GetCultureInfo("en-US"), 1));
        XNamespace ns = "http://www.w3.org/2001/10/synthesis";
        var phones = xml.Descendants(ns + "phoneme").ToArray();
        Assert.IsTrue(phones.Any(e => e.Value == "intrigued" && (string?)e.Attribute("ph") == "ih n t r iy g d"));
        Assert.IsTrue(phones.Any(e => e.Value == "ooooo" && (string?)e.Attribute("ph") == "uw uw uw uw uw"));
        Assert.IsTrue(phones.Any(e => e.Value == "rrrrr" && (string?)e.Attribute("ph") == "r r r r r"));
    }

    [TestMethod]
    [Timeout(30000)]
    [DataRow(VocalLanguage.German, "Klenker", "Klenkerrrrrr")]
    [DataRow(VocalLanguage.English, "hiss", "hissssss")]
    public void WindowsSpeech_RepeatedLettersProduceLongerAudibleSounds(VocalLanguage language, string normal, string extended)
    {
        using var probe = new SpeechSynthesizer();
        string code = language == VocalLanguage.German ? "de" : "en";
        if (!probe.GetInstalledVoices().Any(v => v.Enabled && v.VoiceInfo.Culture.TwoLetterISOLanguageName == code))
            Assert.Inconclusive($"A {code} Windows speech voice is not installed.");
        var settings = CleanVoice(language, normal);
        settings.UseWindowsSpeech = true;
        settings.BasePitchHz = 195;
        settings.SampleRate = 16000;
        using var plain = VocalSynth.Render(settings);
        settings.Text = extended;
        using var stretched = VocalSynth.Render(settings);
        Assert.IsTrue(stretched.Duration > plain.Duration + TimeSpan.FromMilliseconds(150));
        Assert.IsTrue(stretched.Data.Any(sample => Math.Abs(sample) > 0.001));
    }

    [TestMethod]
    public void Capitals_AddEmphasisWithoutLosingQuestionOrWhisper()
    {
        var syntax = VocalEmphasisSyntax.Default();
        var tokens = VocalTextParser.Parse(VocalLanguage.English, "HELLO? ~WORLD?~ I", syntax)
            .OfType<VocalPhonemeToken>().ToArray();
        Assert.IsTrue(tokens.Any(t => t.Capitalized && t.Emphasis == VocalEmphasis.PitchUp));
        Assert.IsTrue(tokens.Any(t => t.Capitalized && t.Emphasis == VocalEmphasis.Whisper && t.Question));
        Assert.IsFalse(tokens[^1].Capitalized);

        var windows = VocalTextParser.ParseWindowsSpeech(
            VocalLanguage.English, "HELLO? ~WORLD?~", syntax, CancellationToken.None);
        var xml = XDocument.Parse(VocalWindowsSpeech.BuildSsml(windows, CultureInfo.GetCultureInfo("en-US"), 1));
        XNamespace ns = "http://www.w3.org/2001/10/synthesis";
        Assert.IsTrue(xml.Descendants(ns + "prosody").Any(e =>
            e.Value == "HELLO" && (string?)e.Attribute("pitch") == "high" && (string?)e.Attribute("volume") == "x-loud"));
        Assert.IsTrue(xml.Descendants(ns + "prosody").Any(e =>
            e.Value == "WORLD" && (string?)e.Attribute("volume") == "medium" && (string?)e.Attribute("pitch") == "high"));
        foreach (var language in new[] { VocalLanguage.German, VocalLanguage.AnimalCrossing, VocalLanguage.Beatboxing })
        {
            string text = language == VocalLanguage.Beatboxing ? "B" : "HALLO";
            Assert.IsTrue(VocalTextParser.Parse(language, text, syntax).OfType<VocalPhonemeToken>().All(t => t.Capitalized));
        }
    }

    [TestMethod]
    public void Capitals_AreAudiblyLouderInFormantAndBeatboxModes()
    {
        foreach (var (language, normal, emphasized) in new[]
        {
            (VocalLanguage.English, "hello hello", "HELLO hello"),
            (VocalLanguage.Beatboxing, "b b", "B b"),
        })
        {
            var settings = CleanVoice(language, normal);
            settings.SampleRate = 8000;
            using var plain = VocalSynth.Render(settings);
            settings.Text = emphasized;
            using var loud = VocalSynth.Render(settings);
            int samples = Math.Min(2000, Math.Min(plain.Data.Length, loud.Data.Length));
            double normalEnergy = plain.Data.Take(samples).Sum(x => x * x);
            double loudEnergy = loud.Data.Take(samples).Sum(x => x * x);
            Assert.IsTrue(loudEnergy > normalEnergy * 1.15,
                $"{language}: capitalized energy {loudEnergy} was not higher than {normalEnergy}.");
        }
    }

    [TestMethod]
    [Timeout(30000)]
    public void WindowsSpeech_CapitalsAreLouder()
    {
        using var probe = new SpeechSynthesizer();
        if (!probe.GetInstalledVoices().Any(v => v.Enabled && v.VoiceInfo.Culture.TwoLetterISOLanguageName == "en"))
            Assert.Inconclusive("An English Windows speech voice is not installed.");
        var settings = CleanVoice(VocalLanguage.English, "hello");
        settings.UseWindowsSpeech = true;
        settings.BasePitchHz = 195;
        settings.SampleRate = 16000;
        using var plain = VocalSynth.Render(settings);
        settings.Text = "HELLO";
        using var loud = VocalSynth.Render(settings);
        double normalEnergy = plain.Data.Sum(x => x * x);
        double loudEnergy = loud.Data.Sum(x => x * x);
        Assert.IsTrue(loudEnergy > normalEnergy * 1.15,
            $"Capitalized energy {loudEnergy} was not higher than {normalEnergy}.");
    }

    [TestMethod]
    public void Beatboxing_RendersNotationOnEvenSteps_WithRestsAndSpeed()
    {
        var settings = CleanVoice(VocalLanguage.Beatboxing, "B t K ts pf .");
        settings.SampleRate = 8000;
        settings.Noise = 0.5;
        var tokens = VocalTextParser.Parse(settings.Language, settings.Text, settings.EmphasisSyntax);
        CollectionAssert.AreEqual(new[] { "beat-b", "beat-t", "beat-k", "beat-ts", "beat-pf" },
            tokens.OfType<VocalPhonemeToken>().Select(t => t.Phoneme).ToArray());
        using var audio = VocalSynth.Render(settings);
        Assert.AreEqual(12000, audio.Data.Length);
        Assert.AreEqual(120f, audio.Bpm);
        Assert.IsTrue(audio.Data.Take(10000).Any(x => Math.Abs(x) > 0.001));
        Assert.IsTrue(audio.Data.Skip(10000).All(x => x == 0));
        settings.Speed = 2;
        using var fast = VocalSynth.Render(settings);
        Assert.AreEqual(audio.Data.Length / 2, fast.Data.Length);
        Assert.AreEqual(240f, fast.Bpm);
        foreach (var preset in VocalVoicePreset.BuiltIn)
        {
            var voice = VocalSynthSettings.FromPreset(preset, VocalLanguage.Beatboxing, "B t K");
            voice.SampleRate = 8000;
            using var beat = VocalSynth.Render(voice);
            Assert.IsTrue(beat.Data.Any(x => Math.Abs(x) > 0.001), $"{preset.Name} produced silence.");
            Assert.IsTrue(beat.Data.All(float.IsFinite), $"{preset.Name} produced invalid samples.");
        }
        settings.Text = "B X";
        using var fallback = VocalSynth.Render(settings);
        Assert.IsTrue(fallback.Data.Any(sample => Math.Abs(sample) > 0.001));
    }

    [TestMethod]
    public void Beatboxing_RendersRequestedNotationAcrossVoicePresets()
    {
        const string text = "Bt't tr tr tch - bz kz b'bp'bp - k'ts' kp'p kp'p";
        var tokens = VocalTextParser.Parse(VocalLanguage.Beatboxing, text, VocalEmphasisSyntax.Default());
        CollectionAssert.AreEqual(
            new[] { "beat-b", "beat-t", "beat-t", "beat-tr", "beat-tr", "beat-tch", "beat-bz", "beat-kz",
                "beat-b", "beat-b", "beat-p", "beat-b", "beat-p", "beat-k", "beat-ts",
                "beat-k", "beat-p", "beat-p", "beat-k", "beat-p", "beat-p" },
            tokens.OfType<VocalPhonemeToken>().Select(t => t.Phoneme).ToArray());
        foreach (var preset in VocalVoicePreset.BuiltIn)
        {
            var settings = VocalSynthSettings.FromPreset(preset, VocalLanguage.Beatboxing, text);
            settings.SampleRate = 8000;
            using var audio = VocalSynth.Render(settings);
            int step = settings.SampleRate / 4;
            Assert.AreEqual(23 * step, audio.Data.Length, preset.Name);
            for (int i = 0; i < 23; i++)
            {
                var samples = audio.Data.Skip(i * step).Take(step);
                bool audible = samples.Any(sample => Math.Abs(sample) > 0.001);
                Assert.AreEqual(i is not (6 or 14), audible, $"{preset.Name}: step {i}.");
            }
            Assert.IsTrue(audio.Data.All(float.IsFinite), $"{preset.Name} produced invalid audio.");
            if (preset.Id == "adult-female")
            {
                settings.Speed = 2;
                using var fast = VocalSynth.Render(settings);
                Assert.AreEqual(audio.Data.Length / 2, fast.Data.Length);
            }
        }
    }

    [TestMethod]
    public void Beatboxing_SeparatorsAndEmphasisRemainIndependent()
    {
        var syntax = VocalEmphasisSyntax.Default();
        var tokens = VocalTextParser.Parse(VocalLanguage.Beatboxing, "*TR tch* b\u2019z BZ KZ", syntax)
            .OfType<VocalPhonemeToken>().ToArray();
        CollectionAssert.AreEqual(new[] { "beat-tr", "beat-tch", "beat-b", "beat-z", "beat-bz", "beat-kz" },
            tokens.Select(t => t.Phoneme).ToArray());
        Assert.AreEqual(VocalEmphasis.Strong, tokens[0].Emphasis);
        Assert.IsTrue(tokens[0].Capitalized);
        Assert.IsTrue(tokens[4].Capitalized);

        syntax.Rules.Add(new VocalEmphasisRule { Marker = "'", Mode = VocalEmphasisMode.Strong, Wrapping = false });
        var custom = VocalTextParser.Parse(VocalLanguage.Beatboxing, "b'b", syntax)
            .OfType<VocalPhonemeToken>().ToArray();
        Assert.AreEqual(VocalEmphasis.Strong, custom[1].Emphasis);
        CollectionAssert.AreEqual(new[] { "beat-b", "beat-s" },
            VocalTextParser.Parse(VocalLanguage.Beatboxing, "B X", VocalEmphasisSyntax.Default())
                .OfType<VocalPhonemeToken>().Select(token => token.Phoneme).ToArray());
    }

    [TestMethod]
    public void Beatboxing_TongueRoll_ProducesRepeatedPulses()
    {
        var settings = CleanVoice(VocalLanguage.Beatboxing, "tr");
        settings.SampleRate = 16000;
        settings.BasePitchHz = 200;
        using var audio = VocalSynth.Render(settings);
        int block = settings.SampleRate / 200;
        double sum = 0, real = 0, imaginary = 0;
        for (int start = settings.SampleRate / 100; start + block <= settings.SampleRate * 14 / 100; start += block)
        {
            double energy = 0;
            for (int i = start; i < start + block; i++) energy += audio.Data[i] * audio.Data[i];
            double level = Math.Sqrt(energy / block);
            double phase = 2 * Math.PI * 24 * (start + block / 2.0) / settings.SampleRate;
            sum += level;
            real += level * Math.Cos(phase);
            imaginary += level * Math.Sin(phase);
        }
        double modulation = 2 * Math.Sqrt(real * real + imaginary * imaginary) / sum;
        Assert.IsTrue(modulation > 0.3, $"Expected a pulsing tongue roll, got modulation {modulation}.");
    }

    [TestMethod]
    public void SingingPreset_RendersMelodicSpeechAcrossLanguageModes()
    {
        var preset = VocalVoicePreset.BuiltIn.Single(p => p.Id == "singing");
        foreach (var language in Enum.GetValues<VocalLanguage>())
        {
            string text = language == VocalLanguage.Beatboxing ? "B t K" : "ma ma";
            var settings = VocalSynthSettings.FromPreset(preset, language, text);
            settings.SampleRate = 8000;
            using var audio = VocalSynth.Render(settings);
            Assert.IsTrue(audio.Data.Any(x => Math.Abs(x) > 0.001), $"{language} produced silence.");
            Assert.IsTrue(audio.Data.All(float.IsFinite), $"{language} produced invalid samples.");
        }

        var singer = CleanVoice(VocalLanguage.English, "ma ma");
        singer.SampleRate = 8000;
        singer.BasePitchHz = 150;
        singer.Singing = true;
        using var melody = VocalSynth.Render(singer);
        double first = PitchInRange(melody.Data, 8000, 0.10, 0.23, 120, 270);
        double second = PitchInRange(melody.Data, 8000, 0.44, 0.57, 120, 270);
        Assert.IsTrue(second > first * 1.15, $"Expected rising sung notes, got {first} and {second} Hz.");
    }

    [STATestMethod]
    public void Dialog_CanSelectBeatboxingAndSaveTheSingingVoice()
    {
        string directory = Path.Combine(Path.GetTempPath(), "vocal_ui_" + Guid.NewGuid());
        var profiles = new VocalVoiceProfileStore(directory);
        try
        {
            using var dialog = new VocalSynthDialog(profiles, new VocalEmphasisSyntaxStore(Path.Combine(directory, "syntax.json")));
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            object Field(string name) => typeof(VocalSynthDialog).GetField(name, flags)!.GetValue(dialog)!;
            var language = (ComboBox)Field("languageCombo");
            var voice = (ComboBox)Field("voiceCombo");
            var unsupportedBeep = (CheckBox)Field("unsupportedBeepCheckBox");
            Assert.IsFalse(unsupportedBeep.Checked);
            unsupportedBeep.Checked = true;
            language.SelectedItem = "Beatboxing";
            voice.SelectedItem = "Built-in: Singing";
            var settings = (VocalSynthSettings)Field("settings");
            Assert.AreEqual(VocalLanguage.Beatboxing, settings.Language);
            Assert.IsTrue(settings.Singing);
            Assert.IsFalse(settings.UseWindowsSpeech);
            Assert.IsTrue(settings.BeepForUnsupportedSounds);
            unsupportedBeep.Checked = false;
            Assert.IsFalse(((VocalSynthSettings)Field("settings")).BeepForUnsupportedSounds);

            ((TextBox)Field("profileName")).Text = "Singing profile";
            typeof(VocalSynthDialog).GetMethod("SaveProfile", flags)!.Invoke(dialog, null);
            Assert.IsTrue(profiles.LoadAll().Single(p => p.Name == "Singing profile").Singing);
            voice.SelectedItem = "Built-in: Adult Female";
            Assert.IsFalse(((VocalSynthSettings)Field("settings")).Singing);
            voice.SelectedItem = "Profile: Singing profile";
            settings = (VocalSynthSettings)Field("settings");
            Assert.IsTrue(settings.Singing);
            Assert.AreEqual(VocalLanguage.Beatboxing, settings.Language);
            settings.Text = "B t K";
            settings.SampleRate = 8000;
            using var audio = VocalSynth.Render(settings);
            Assert.IsTrue(audio.Data.Any(x => Math.Abs(x) > 0.001));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [STATestMethod]
    public void Dialog_DefaultsToFormantSpeech_AndKeepsProfileButtonsOnOneRow()
    {
        string directory = Path.Combine(Path.GetTempPath(), "vocal_layout_" + Guid.NewGuid());
        var profiles = new VocalVoiceProfileStore(directory);
        try
        {
            using var dialog = new VocalSynthDialog(profiles, new VocalEmphasisSyntaxStore(Path.Combine(directory, "syntax.json")));
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            object Field(string name) => typeof(VocalSynthDialog).GetField(name, flags)!.GetValue(dialog)!;
            var naturalSpeech = (CheckBox)Field("windowsSpeechCheckBox");
            var settings = (VocalSynthSettings)Field("settings");
            Assert.IsFalse(naturalSpeech.Checked);
            Assert.IsFalse(settings.UseWindowsSpeech);
            ((ComboBox)Field("languageCombo")).SelectedItem = "German";
            Assert.IsFalse(settings.UseWindowsSpeech);
            naturalSpeech.Checked = true;
            Assert.IsTrue(settings.UseWindowsSpeech);

            var group = (FlowLayoutPanel)Field("profileRow");
            var nameField = (TextBox)Field("profileName");
            dialog.CreateControl();
            Assert.IsFalse(group.Parent!.Controls.OfType<Button>().Any(button => button.Text == "Render"));
            var buttons = new[]
            {
                (Button)Field("saveProfileBtn"), (Button)Field("loadProfileBtn"),
                (Button)Field("renameProfileBtn"), (Button)Field("deleteProfileBtn"),
            };
            foreach (int width in new[] { 900, 700 })
            {
                dialog.ClientSize = new System.Drawing.Size(width, dialog.ClientSize.Height);
                dialog.PerformLayout();
                group.Parent!.PerformLayout();
                group.PerformLayout();
                Assert.AreSame(group, nameField.Parent, "Profile name must be in the profile action row.");
                Assert.AreSame(nameField, group.Controls[0], "Profile name must be the first profile control.");
                Assert.AreEqual("Profile name", nameField.PlaceholderText);
                Assert.AreEqual("Profile name", nameField.AccessibleName);
                Assert.AreEqual(group.Parent.Padding.Left, group.Left, "The profile row must start at the left.");
                Assert.IsTrue(nameField.Right <= buttons[0].Left
                    && nameField.Top < buttons[0].Bottom && nameField.Bottom > buttons[0].Top,
                    $"Profile name is not to the left of Save Profile at width {width}.");
                if (width == 900)
                {
                    var lastAction = (Button)Field("emphasisBtn");
                    Assert.IsTrue(lastAction.Top < group.Bottom && lastAction.Bottom > group.Top,
                        "The profile row should fit on the second toolbar row at the default width.");
                }
                foreach (var button in buttons)
                {
                    Assert.AreEqual(buttons[0].Top, button.Top, $"{button.Text} wrapped at width {width}.");
                    Assert.IsTrue(button.Left >= 0 && button.Right <= group.ClientSize.Width,
                        $"{button.Text} is clipped at width {width}.");
                }
                for (int i = 1; i < buttons.Length; i++)
                    Assert.IsTrue(buttons[i].Left >= buttons[i - 1].Right, "Profile buttons overlap or are out of order.");
                Assert.IsTrue(group.Right <= group.Parent.ClientSize.Width - group.Parent.Padding.Right,
                    $"The profile button row extends beyond the window at width {width}.");
            }

            nameField.Text = "Layout profile";
            var click = typeof(Button).GetMethod("OnClick", flags)!;
            click.Invoke(buttons[0], [EventArgs.Empty]);
            Assert.IsTrue(profiles.LoadAll().Any(p => p.Name == "Layout profile"));
            click.Invoke(buttons[1], [EventArgs.Empty]);
            Assert.AreEqual("Profile: Layout profile", ((ComboBox)Field("voiceCombo")).SelectedItem);
            click.Invoke(buttons[3], [EventArgs.Empty]);
            Assert.IsFalse(profiles.LoadAll().Any(p => p.Name == "Layout profile"));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [STATestMethod]
    public void Dialog_UndoRedoShortcuts_UseTextHistoryFromOtherControls()
    {
        string directory = Path.Combine(Path.GetTempPath(), "vocal_history_" + Guid.NewGuid());
        var profiles = new VocalVoiceProfileStore(directory);
        try
        {
            using var dialog = new VocalSynthDialog(profiles, new VocalEmphasisSyntaxStore(Path.Combine(directory, "syntax.json")));
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            object Field(string name) => typeof(VocalSynthDialog).GetField(name, flags)!.GetValue(dialog)!;
            var editor = (TextBox)Field("textEditor");
            var profile = (TextBox)Field("profileName");
            var voice = (ComboBox)Field("voiceCombo");
            editor.AppendText("first ");
            editor.AppendText("second");
            profile.Text = "Profile name";
            profile.SelectionStart = profile.TextLength;
            SendMessage(profile.Handle, 0x0102, (IntPtr)'!', IntPtr.Zero);
            Assert.IsTrue(profile.CanUndo);

            dialog.ActiveControl = profile;
            Assert.IsTrue(SendCommand(profile, Keys.Control | Keys.Z));
            Assert.AreEqual("first ", editor.Text);
            Assert.AreEqual("Profile name!", profile.Text);
            Assert.AreSame(profile, dialog.ActiveControl);
            dialog.ActiveControl = voice;
            Assert.IsTrue(SendCommand(voice, Keys.Control | Keys.Z));
            Assert.AreEqual(string.Empty, editor.Text);
            Assert.IsTrue(SendCommand(voice, Keys.Control | Keys.Y));
            Assert.AreEqual("first ", editor.Text);
            dialog.ActiveControl = editor;
            Assert.IsTrue(SendCommand(editor, Keys.Control | Keys.Y));
            Assert.AreEqual("first second", editor.Text);
            Assert.IsTrue(SendCommand(editor, Keys.Control | Keys.Z));
            Assert.AreEqual("first ", editor.Text);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [STATestMethod]
    public void Dialog_EnterShortcuts_TogglePreviewAndPreserveEditorNewlines()
    {
        string directory = Path.Combine(Path.GetTempPath(), "vocal_keys_" + Guid.NewGuid());
        var profiles = new VocalVoiceProfileStore(directory);
        try
        {
            using var dialog = new VocalSynthDialog(profiles, new VocalEmphasisSyntaxStore(Path.Combine(directory, "syntax.json")));
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            FieldInfo Field(string name) => typeof(VocalSynthDialog).GetField(name, flags)!;
            var editor = (TextBox)Field("textEditor").GetValue(dialog)!;
            var profile = (TextBox)Field("profileName").GetValue(dialog)!;
            var voice = (ComboBox)Field("voiceCombo").GetValue(dialog)!;
            var speed = (NumericUpDown)Field("speed").GetValue(dialog)!;
            var speedEditor = speed.Controls.OfType<TextBoxBase>().Single();
            var button = (Button)Field("designProfileBtn").GetValue(dialog)!;
            var prehear = (Button)Field("prehear").GetValue(dialog)!;
            var parent = prehear.Parent!;
            parent.Controls.Remove(prehear);
            try
            {
                editor.Text = "Hello";
                editor.SelectionStart = editor.TextLength;
                dialog.ActiveControl = editor;
                Assert.IsFalse(SendCommand(editor, Keys.Enter));
                SendMessage(editor.Handle, 0x0102, (IntPtr)'\r', IntPtr.Zero);
                Assert.AreEqual("Hello\r\n", editor.Text);
                Assert.IsTrue(SendCommand(editor, Keys.Control | Keys.Z));
                Assert.AreEqual("Hello", editor.Text);
                Assert.IsTrue(SendCommand(editor, Keys.Control | Keys.Y));
                Assert.AreEqual("Hello\r\n", editor.Text);

                Field("ready").SetValue(dialog, Field("revision").GetValue(dialog));
                int clicks = 0;
                prehear.Click += (_, _) => clicks++;
                Assert.IsTrue(SendCommand(editor, Keys.Shift | Keys.Enter));
                Assert.AreEqual("Hello\r\n", editor.Text);
                Assert.AreEqual(1, clicks);

                dialog.ActiveControl = profile;
                Assert.IsTrue(SendCommand(profile, Keys.Enter));
                dialog.ActiveControl = voice;
                Assert.IsTrue(SendCommand(voice, Keys.Shift | Keys.Enter));
                dialog.ActiveControl = speed;
                Assert.IsTrue(SendCommand(speedEditor, Keys.Enter));
                dialog.ActiveControl = button;
                Assert.IsTrue(SendCommand(button, Keys.Shift | Keys.Enter));
                Assert.AreEqual(5, clicks);
                SendCommand(button, Keys.Control | Keys.Enter);
                Assert.AreEqual(5, clicks);

                Field("output").SetValue(dialog, new WaveOut());
                prehear.Text = "Stop";
                editor.Text = string.Empty;
                Assert.IsTrue(prehear.Enabled, "Stop must remain available when the text is cleared during playback.");
                dialog.ActiveControl = editor;
                Assert.IsTrue(SendCommand(editor, Keys.Shift | Keys.Enter));
                Assert.IsNull(Field("output").GetValue(dialog));
                Assert.AreEqual("Prehear", prehear.Text);
                Assert.IsFalse(prehear.Enabled);
                Assert.AreEqual(6, clicks);
                Assert.IsTrue(SendCommand(editor, Keys.Shift | Keys.Enter));
                Assert.AreEqual(6, clicks);
            }
            finally
            {
                parent.Controls.Add(prehear);
            }
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [STATestMethod]
    [Timeout(15000)]
    public void Dialog_Beatboxing_RendersOnDemandAndApproximatesUnknownNotation()
    {
        string directory = Path.Combine(Path.GetTempPath(), "vocal_beatbox_" + Guid.NewGuid());
        var profiles = new VocalVoiceProfileStore(directory);
        try
        {
            using var dialog = new VocalSynthDialog(profiles, new VocalEmphasisSyntaxStore(Path.Combine(directory, "syntax.json")));
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            object Field(string name) => typeof(VocalSynthDialog).GetField(name, flags)!.GetValue(dialog)!;
            dialog.CreateControl();
            ((ComboBox)Field("languageCombo")).SelectedItem = "Beatboxing";
            ((VocalSynthSettings)Field("settings")).SampleRate = 8000;
            var editor = (TextBox)Field("textEditor");
            var timer = (System.Windows.Forms.Timer)Field("debounce");
            var status = (Label)Field("status");
            var ensure = typeof(VocalSynthDialog).GetMethod("EnsureRenderedAsync", flags)!;
            editor.Text = "Bt't tr tr tch - bz kz b'bp'bp - k'ts' kp'p kp'p";
            Assert.IsTrue(timer.Enabled);
            var task = (Task<bool>)ensure.Invoke(dialog, null)!;
            Assert.IsFalse(timer.Enabled, "On-demand rendering must cancel the pending debounce timer.");
            WaitForRender(task);
            Assert.IsTrue(task.GetAwaiter().GetResult(), status.Text);
            using var audio = (ModularAudience.Audio.AudioObj)Field("audio");
            Assert.IsTrue(audio.Data.Any(sample => Math.Abs(sample) > 0.001));
            StringAssert.StartsWith(status.Text, "Rendered");

            editor.Text = "B X";
            task = (Task<bool>)ensure.Invoke(dialog, null)!;
            WaitForRender(task);
            Assert.IsTrue(task.GetAwaiter().GetResult(), status.Text);
            StringAssert.StartsWith(status.Text, "Rendered");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static void WaitForRender(Task task)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
        Assert.IsTrue(task.IsCompleted, "Rendering did not finish in time.");
    }

    private static bool SendCommand(Control control, Keys keyData)
    {
        MethodInfo method = control.GetType().GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic)!;
        object[] arguments = [Message.Create(control.Handle, 0x0100, (IntPtr)(keyData & Keys.KeyCode), IntPtr.Zero), keyData];
        return (bool)method.Invoke(control, arguments)!;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);

    [TestMethod]
    [Timeout(30000)]
    public void Singing_WindowsPitchProcessingCreatesDistinctNotes()
    {
        var settings = CleanVoice(VocalLanguage.English, "sing");
        settings.SampleRate = 16000;
        settings.BasePitchHz = 175;
        settings.Singing = true;
        float[] source = Enumerable.Range(0, 16000 * 2)
            .Select(i => (float)(0.2 * Math.Sin(2 * Math.PI * 180 * i / settings.SampleRate))).ToArray();
        float[] sung = VocalSinging.RenderWindows(source, settings, CancellationToken.None);
        double first = PitchInRange(sung, 16000, 0.18, 0.28, 120, 320);
        double second = PitchInRange(sung, 16000, 0.50, 0.60, 120, 320);
        double third = PitchInRange(sung, 16000, 0.82, 0.92, 120, 320);
        Assert.IsTrue(second > first * 1.15 && third > second * 1.1,
            $"Expected three rising sung notes, got {first}, {second}, {third} Hz.");
    }

    [TestMethod]
    [Timeout(30000)]
    [DataRow(VocalLanguage.English, "en")]
    [DataRow(VocalLanguage.German, "de")]
    public void Singing_DesktopWindowsVoicesFollowTheMelody(VocalLanguage language, string languageCode)
    {
        using var probe = new SpeechSynthesizer();
        var selectedVoice = probe.GetInstalledVoices()
            .Where(v => v.Enabled && v.VoiceInfo.Culture.TwoLetterISOLanguageName == languageCode)
            .OrderByDescending(v => v.VoiceInfo.Gender == VoiceGender.Female)
            .Select(v => v.VoiceInfo).FirstOrDefault();
        if (selectedVoice is null)
            Assert.Inconclusive($"A {languageCode} Windows speech voice is not installed.");
        string expectedVoice = language == VocalLanguage.German ? "Microsoft Hedda Desktop" : "Microsoft Zira Desktop";
        if (selectedVoice.Name != expectedVoice)
            Assert.Inconclusive($"The fixed timing fixture requires {expectedVoice}; the selected voice is {selectedVoice.Name}.");
        var settings = CleanVoice(language, "aaaaaa aaaaaa");
        settings.SampleRate = 16000;
        settings.BasePitchHz = 175;
        settings.Singing = true;
        settings.UseWindowsSpeech = true;
        using var audio = VocalSynth.Render(settings);
        Assert.IsTrue(audio.Data.Any(x => Math.Abs(x) > 0.001));
        Assert.IsTrue(audio.Data.All(float.IsFinite));
        double first = PitchInRange(audio.Data, 16000, 0.18, 0.28, 120, 320);
        double second = PitchInRange(audio.Data, 16000, 0.50, 0.60, 120, 320);
        double third = PitchInRange(audio.Data, 16000, 0.82, 0.92, 120, 320);
        Assert.IsTrue(second > first * 1.12 && third > second * 1.08,
            $"Expected rising notes from the Windows voice, got {first}, {second}, {third} Hz.");
    }

    private static double PitchInRange(float[] samples, int rate, double fromSeconds, double toSeconds, int minHz, int maxHz)
    {
        int start = (int)(fromSeconds * rate), end = Math.Min(samples.Length, (int)(toSeconds * rate));
        double best = double.NegativeInfinity;
        int bestLag = 0;
        for (int lag = rate / maxHz; lag <= rate / minHz; lag++)
        {
            double cross = 0, first = 0, second = 0;
            for (int i = start + lag; i < end; i++)
            {
                double a = samples[i], b = samples[i - lag];
                cross += a * b;
                first += a * a;
                second += b * b;
            }
            double correlation = cross / Math.Sqrt(first * second + 1e-20);
            if (correlation > best) { best = correlation; bestLag = lag; }
        }
        Assert.IsTrue(best > 0.5, "No voiced pitch was detected in the requested interval.");
        return rate / (double)bestLag;
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

    private static double ShortTimeLevelVariation(float[] samples, int rate)
    {
        int block = rate / 100;
        var levels = new List<double>();
        for (int start = block; start + block <= samples.Length - block; start += block)
        {
            double energy = 0;
            for (int i = start; i < start + block; i++) energy += samples[i] * samples[i];
            levels.Add(Math.Sqrt(energy / block));
        }
        double mean = levels.Average();
        return Math.Sqrt(levels.Sum(level => (level - mean) * (level - mean)) / levels.Count) / mean;
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
        var singer = VocalVoiceProfile.FromPreset(VocalVoicePreset.BuiltIn.Single(p => p.Id == "singing"), "Singer");
        store.Save(singer);
        var loaded = store.LoadAll();
        Assert.IsTrue(loaded.Any(p => p.Name == "MyProfile"));
        var loadedSinger = loaded.Single(p => p.Name == "Singer");
        Assert.IsTrue(loadedSinger.Clone().ToPreset("saved").Singing);
        Assert.IsTrue(VocalSynthSettings.FromProfile(loadedSinger).Clone().Singing);
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