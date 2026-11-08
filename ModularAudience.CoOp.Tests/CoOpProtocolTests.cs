using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.CoOp.Networking;
using ModularAudience.CoOp.Protocol;
using ModularAudience.CoOp.Synchronization;

namespace ModularAudience.CoOp.Tests;

[TestClass]
public sealed class CoOpProtocolTests
{
    [TestMethod]
    public void Serializer_RoundTripsPolymorphicMessages()
    {
        var serializer = new JsonCoOpMessageSerializer();
        var message = new AudioAddedMessage(
            Guid.NewGuid(),
            new CoOpAudioSnapshot
            {
                AudioId = Guid.NewGuid(),
                Revision = 7,
                Name = "Kick",
                OriginalName = "Kick original",
                SampleData = [1, 2, 3, 4, 5, 6, 7, 8],
                SampleRate = 44100,
                Channels = 2,
                Metrics = new Dictionary<string, double> { ["Attack"] = 12.5 }
            });

        byte[] payload = serializer.Serialize(message);
        CoOpMessage deserialized = serializer.Deserialize(payload);

        var roundTripped = deserialized as AudioAddedMessage;
        Assert.IsNotNull(roundTripped);
        Assert.AreEqual(message.CollectionId, roundTripped.CollectionId);
        Assert.AreEqual(message.Audio.AudioId, roundTripped.Audio.AudioId);
        Assert.AreEqual(7, roundTripped.Audio.Revision);
        Assert.AreEqual("Kick", roundTripped.Audio.Name);
        CollectionAssert.AreEqual(message.Audio.SampleData, roundTripped.Audio.SampleData);
        Assert.AreEqual(12.5, roundTripped.Audio.Metrics["Attack"]);
    }

    [TestMethod]
    public async Task FrameCodec_RoundTripsPayload()
    {
        byte[] payload = Encoding.UTF8.GetBytes("hello co-op");
        using var stream = new MemoryStream();

        await CoOpFrameCodec.WriteFrameAsync(stream, payload);
        stream.Position = 0;
        byte[] received = await CoOpFrameCodec.ReadFrameAsync(stream);

        CollectionAssert.AreEqual(payload, received);
    }

    [TestMethod]
    public async Task FrameCodec_RejectsInvalidLengthHeader()
    {
        using var stream = new MemoryStream();
        await stream.WriteAsync(new byte[] { 0, 0, 0, 0 });
        stream.Position = 0;

        await Assert.ThrowsExactlyAsync<CoOpProtocolException>(
            async () => await CoOpFrameCodec.ReadFrameAsync(stream));
    }

    [TestMethod]
    public void SnapshotMapper_RoundTripsAudioDataAndMetadata()
    {
        using var original = TestAudio.Create("Snare", sampleRate: 48000, channels: 2, sampleCount: 128);
        original.Bpm = 128.5f;
        original.Timing = 0.75f;
        original.Key = "A";
        original.BeatGrid = [true, false, true];
        original.DrawBeatGrid = true;
        original.Metrics["Peak"] = 0.9;

        CoOpAudioSnapshot snapshot = CoOpAudioSnapshotMapper.Capture(original, 42);
        using var restored = CoOpAudioSnapshotMapper.Create(snapshot);

        Assert.AreEqual(original.Id, restored.Id);
        Assert.AreEqual(42, snapshot.Revision);
        Assert.AreEqual(original.Name, restored.Name);
        Assert.AreEqual(original.OriginalName, restored.OriginalName);
        CollectionAssert.AreEqual(original.Data, restored.Data);
        Assert.AreEqual(original.SampleRate, restored.SampleRate);
        Assert.AreEqual(original.Channels, restored.Channels);
        Assert.AreEqual(128.5f, restored.Bpm);
        Assert.AreEqual(0.75f, restored.Timing);
        Assert.AreEqual("A", restored.Key);
        CollectionAssert.AreEqual(original.BeatGrid, restored.BeatGrid);
        Assert.IsTrue(restored.DrawBeatGrid);
        Assert.AreEqual(0.9, restored.Metrics["Peak"]);
    }

    [TestMethod]
    public void SnapshotMapper_ApplyToUpdatesExistingAudio()
    {
        using var target = TestAudio.Create("Old", sampleCount: 64, fill: 0.1f);
        using var source = TestAudio.Create("New", sampleCount: 128, fill: 0.5f);
        source.Bpm = 99f;
        source.BeatGrid = [true, false];
        Guid targetId = target.Id;

        CoOpAudioSnapshot snapshot = CoOpAudioSnapshotMapper.Capture(source);
        CoOpAudioSnapshotMapper.ApplyTo(target, snapshot);

        Assert.AreEqual("New", target.Name);
        Assert.AreEqual(128, target.Data.Length);
        Assert.AreEqual(99f, target.Bpm);
        CollectionAssert.AreEqual(source.Data, target.Data);
        CollectionAssert.AreEqual(source.BeatGrid, target.BeatGrid);
        Assert.AreEqual(targetId, target.Id);
    }
}
