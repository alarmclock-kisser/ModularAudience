using ModularAudience.Audio.Vocal;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;

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