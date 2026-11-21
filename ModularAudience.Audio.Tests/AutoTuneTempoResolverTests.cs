using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Forms.Effects;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class AutoTuneTempoResolverTests
{
    [TestMethod]
    [DataRow(128.5, 150.25, 135.5, 128.5)]
    [DataRow(0d, 150.25, 135.5, 150.25)]
    [DataRow(double.NaN, 150.25, 135.5, 150.25)]
    [DataRow(double.PositiveInfinity, 150.25, 135.5, 150.25)]
    [DataRow(401d, 150.25, 135.5, 150.25)]
    [DataRow(19d, 150.25, 135.5, 150.25)]
    [DataRow(20d, 150.25, 135.5, 20d)]
    [DataRow(400d, 150.25, 135.5, 400d)]
    [DataRow(0d, 0d, 135.5, 135.5)]
    [DataRow(0d, double.NaN, 135.5, 135.5)]
    [DataRow(-1d, -10d, 135.5, 135.5)]
    [DataRow(0d, 401d, 0d, 100d)]
    [DataRow(0d, 0d, double.NaN, 100d)]
    [DataRow(0d, 0d, 401d, 100d)]
    public void Resolve_PrefersValidTaggedThenScannedThenPresetTempoOtherwiseDefault(
        double bpm, double scannedBpm, double presetBpm, double expected)
    {
        Assert.AreEqual(expected, AutoTuneTempoResolver.Resolve(bpm, scannedBpm, presetBpm));
    }

    [TestMethod]
    public void ResolveWithoutMetadataOrPreset_Uses100Bpm()
    {
        Assert.AreEqual(100d, AutoTuneTempoResolver.Resolve(0, 0));
    }
}
