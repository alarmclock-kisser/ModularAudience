using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class AudioChunkProcessorTests
{
    [TestMethod]
    [DataRow(1)]
    [DataRow(127)]
    [DataRow(4096)]
    [DataRow(8191)]
    public async Task ShortInputIsCopiedIntoOneZeroPaddedChunk(int length)
    {
        float[] data = Enumerable.Range(0, length).Select(i => (i + 1f) / length).ToArray();
        using var sample = new AudioObj { Data = data, Channels = 1, SampleRate = 44100 };
        var chunks = (await sample.GetChunksAsync(8192, 0.75f, keepData: true)).ToArray();
        Assert.HasCount(1, chunks);
        Assert.HasCount(8192, chunks[0]);
        CollectionAssert.AreEqual(data, chunks[0][..length]);
        Assert.IsTrue(chunks[0][length..].All(value => value == 0));
        Assert.AreSame(data, sample.Data);
    }
}
