using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Processors_V1;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class BreakbeatTapSequenceAnalyzerTests
{
    [TestMethod]
    public void RepeatedTapsEstimateTempoGridAndSingleDrumElement()
    {
        float[] recording = CreateRecording([100, 100, 100, 100]);

        BreakbeatTapSequence sequence = BreakbeatTapSequenceAnalyzer.Analyze(recording, 22050, 1);
        IReadOnlyList<BreakbeatTapHit> hits = sequence.GroupTaps(sequence.SuggestedDrumElements);

        Assert.AreEqual(4, sequence.HitCount);
        Assert.AreEqual(120d, sequence.EstimatedBpm, 1d);
        Assert.AreEqual(4, sequence.SuggestedStepsPerBar);
        Assert.AreEqual(1, sequence.SuggestedDrumElements);
        Assert.IsTrue(hits.All(hit => hit.GroupIndex == 0));
    }

    [TestMethod]
    public void DistinctTapTimbresAreGroupedAndManualOneGroupMergesThem()
    {
        float[] recording = CreateRecording([100, 4500, 100, 4500, 100, 4500]);

        BreakbeatTapSequence sequence = BreakbeatTapSequenceAnalyzer.Analyze(recording, 22050, 1);
        IReadOnlyList<BreakbeatTapHit> detectedGroups = sequence.GroupTaps(2);
        IReadOnlyList<BreakbeatTapHit> oneGroup = sequence.GroupTaps(1);

        Assert.AreEqual(6, sequence.HitCount);
        Assert.AreEqual(2, sequence.SuggestedDrumElements);
        Assert.AreEqual(2, detectedGroups.Select(hit => hit.GroupIndex).Distinct().Count());
        Assert.IsTrue(oneGroup.All(hit => hit.GroupIndex == 0));
    }

    [TestMethod]
    public void SilentRecordingProducesNoTapsAndSafeDefaults()
    {
        BreakbeatTapSequence sequence = BreakbeatTapSequenceAnalyzer.Analyze(new float[22050], 22050, 1);

        Assert.AreEqual(0, sequence.HitCount);
        Assert.AreEqual(120d, sequence.EstimatedBpm);
        Assert.AreEqual(8, sequence.SuggestedStepsPerBar);
        Assert.AreEqual(1, sequence.SuggestedDrumElements);
        Assert.AreEqual(0, sequence.GroupTaps(1).Count);
    }

    private static float[] CreateRecording(IReadOnlyList<int> frequencies)
    {
        const int sampleRate = 22050;
        const int seconds = 4;
        float[] samples = new float[sampleRate * seconds];
        int pulseFrames = sampleRate / 12;

        for (int hit = 0; hit < frequencies.Count; hit++)
        {
            int start = sampleRate / 4 + hit * sampleRate / 2;
            int frequency = frequencies[hit];
            for (int frame = 0; frame < pulseFrames && start + frame < samples.Length; frame++)
            {
                double time = frame / (double)sampleRate;
                double envelope = Math.Exp(-time * 35);
                samples[start + frame] += (float)(0.75 * envelope * Math.Cos(2 * Math.PI * frequency * time));
            }
        }

        return samples;
    }
}
