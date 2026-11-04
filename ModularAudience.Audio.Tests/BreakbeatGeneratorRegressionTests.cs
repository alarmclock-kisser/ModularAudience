using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Generators;
using ModularAudience.Forms.Modules.Dialogs;
using System.Reflection;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class BreakbeatGeneratorRegressionTests
{
    [TestMethod]
    [DataRow("FunkShuffle")]
    [DataRow("DnBStepper")]
    [DataRow(" - None - ")]
    public async Task ExactlyOnePatternRowPerSampleIncludingDuplicateInstrumentTypes(string preset)
    {
        foreach (bool interleaved in new[] { false, true })
        {
            DrumsetElement[] elements = [DrumsetElement.Snare, DrumsetElement.Snare, DrumsetElement.HiHatClosed];
            var pattern = await BreakbeatGenerator_V2.GenerateBreakPatternAsync(elements, 2, 0.58f, 32, 0, 0.666f, interleaved, 123, preset);
            Assert.HasCount(elements.Length, pattern, preset);
            Assert.IsTrue(pattern.All(row => row.Length == 64));
            if (interleaved) Assert.IsTrue(Enumerable.Range(0, 64).All(step => pattern.Count(row => row[step]) <= 1));
        }
    }

    [TestMethod]
    public void BotRepeatsExactlyTheConfiguredNumberOfFullPasses()
    {
        var repeat = typeof(BreakbeatGeneratorDialog).GetMethod("ShouldRepeatBotPattern", BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (int configured in new[] { 1, 2, 4, 64 })
        {
            int passes = 0;
            do { passes++; } while ((bool)repeat.Invoke(null, [passes, configured])!);
            Assert.AreEqual(configured, passes);
        }
    }
}
