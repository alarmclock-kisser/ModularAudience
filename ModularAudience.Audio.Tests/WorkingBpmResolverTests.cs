using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio;
using ModularAudience.Forms;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class WorkingBpmResolverTests
{
    [TestMethod]
    public void AudioTempoPrefersValidMetadataAndFallsBackToScannedTempo()
    {
        using AudioObj audio = new() { Bpm = 0, ScannedBpm = 142.5f };

        Assert.IsTrue(WorkingBpmResolver.TryGetAudioBpm(audio, out double bpm));
        Assert.AreEqual(142.5, bpm, 1e-6);

        audio.Bpm = 138f;
        Assert.IsTrue(WorkingBpmResolver.TryGetAudioBpm(audio, out bpm));
        Assert.AreEqual(138, bpm, 1e-6);

        audio.Bpm = float.NaN;
        audio.ScannedBpm = 401f;
        Assert.IsFalse(WorkingBpmResolver.TryGetAudioBpm(audio, out _));
    }
}
