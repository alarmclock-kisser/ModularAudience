using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Vocal;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class VocalSynthAudioObjectNameTests
{
    [TestMethod]
    public void CreatedAudioNameIncludesLanguageVoiceAndAtMost48TextCharacters()
    {
        string spokenText = new('x', 49);
        var settings = new VocalSynthSettings
        {
            Language = VocalLanguage.German,
            VoiceName = "Test Voice",
            Text = spokenText
        };

        using var audio = VocalSynth.CreateAudio(new float[1], settings, CancellationToken.None);

        Assert.AreEqual($"[DE] Test Voice \u00b0 \"{spokenText[..48]}\"", audio.Name);
    }

    [TestMethod]
    public void ShortCodesUseExpectedLanguageCodes()
    {
        Assert.AreEqual("EN", VocalLanguage.English.ShortCode());
        Assert.AreEqual("DE", VocalLanguage.German.ShortCode());
        Assert.AreEqual("TK", VocalLanguage.Turkish.ShortCode());
        Assert.AreEqual("CN", VocalLanguage.Chinese.ShortCode());
    }
}
