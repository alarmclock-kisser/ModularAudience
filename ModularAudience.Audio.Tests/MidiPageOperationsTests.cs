using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Midi;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class MidiPageOperationsTests
{
    [TestMethod]
    public void SelectTracksUsesTrackIdentityAndSnapshotsNoteListsWithoutChangingTiming()
    {
        MidiFileData source = CreateMidi();
        MidiFileData selected = source.SelectTracks([11, 2, 11]);

        CollectionAssert.AreEqual(new[] { 2, 11 }, selected.Tracks.Select(track => track.Index).ToArray());
        AssertMetadata(source, selected);
        Assert.AreEqual(source.Tracks[2].LengthTicks, selected.Tracks[1].LengthTicks);
        CollectionAssert.AreEqual(NoteSignatures(source.Tracks[2]), NoteSignatures(selected.Tracks[1]));
        selected.Tracks[0].Notes.Clear();
        Assert.AreEqual(1, source.Tracks[0].Notes.Count, "Output snapshots must not edit the source note list.");
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => source.SelectTracks([0]));
    }

    [TestMethod]
    public void MergeKeepsPolyphonyChannelsVelocitiesAndTrailingRestWithoutChangingSources()
    {
        MidiFileData source = CreateMidi();
        // Even identical notes from different tracks remain separate voices.
        source.Tracks[1].Notes.Add(source.Tracks[0].Notes[0]);
        MidiFileData merged = source.MergeTracks(trackIndex: 12);

        AssertMetadata(source, merged);
        Assert.AreEqual(1, merged.Tracks.Count);
        Assert.AreEqual(12, merged.Tracks[0].Index);
        Assert.AreEqual("Merged", merged.Tracks[0].Name);
        Assert.AreEqual(source.LengthTicks, merged.LengthTicks);
        CollectionAssert.AreEqual(AllNoteSignatures(source), AllNoteSignatures(merged));
        Assert.AreEqual(4, merged.Tracks[0].Notes.Count);
        CollectionAssert.AreEqual(new[] { 2, 7, 11 }, source.Tracks.Select(track => track.Index).ToArray());
        merged.Tracks[0].Notes.Clear();
        Assert.AreEqual(4, source.Tracks.Sum(track => track.Notes.Count));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CurrentAndMergedPagesExportAsOneRealMidiTrack(bool merge)
    {
        MidiFileData source = CreateMidi();
        MidiFileData output = merge ? source.MergeTracks() : source.SelectTracks([7]);
        string path = Path.Combine(Path.GetTempPath(), $"midi-pages-{Guid.NewGuid():N}.mid");
        try
        {
            Assert.AreEqual(path, await output.ExportAsync(path));
            var raw = new NAudio.Midi.MidiFile(path, strictChecking: false);
            Assert.AreEqual(1, raw.Events.Count());
            MidiFileData loaded = MidiFileData.Load(path);
            Assert.AreEqual(1, loaded.Tracks.Count);
            Assert.AreEqual(output.Tracks[0].Name, loaded.Tracks[0].Name);
            Assert.AreEqual(output.LengthTicks, loaded.LengthTicks);
            Assert.AreEqual(output.TicksPerQuarterNote, loaded.TicksPerQuarterNote);
            Assert.AreEqual(output.DefaultBpm, loaded.DefaultBpm, 0.001);
            CollectionAssert.AreEqual(AllNoteSignatures(output), AllNoteSignatures(loaded));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    [DataRow(MidiInstrument.Sine)]
    [DataRow(MidiInstrument.CustomSample)]
    [DataRow(MidiInstrument.CustomCollection)]
    public void SelectedAndEnsembleRenderingUseOnlyTheRequestedNotes(MidiInstrument instrument)
    {
        var first = new MidiTrackData { Index = 2, Name = "First" };
        first.Notes.Add(new() { NoteNumber = 60, Channel = 1, Velocity = 100, DurationTicks = 120 });
        var second = new MidiTrackData { Index = 7, Name = "Second" };
        second.Notes.Add(new() { NoteNumber = 60, Channel = 2, Velocity = 80, StartTick = 480, DurationTicks = 120 });
        MidiFileData source = MidiFileData.CreateGenerated([first, second]);
        MidiFileData selected = source.SelectTracks([7]);
        MidiFileData ensemble = source.MergeTracks();
        using var sample = new AudioObj
        {
            Data = Enumerable.Range(0, 1600).Select(i => (float)(0.2 * Math.Sin(i * 0.1))).ToArray(),
            SampleRate = 16000,
            Channels = 1
        };
        using AudioObj selectedAudio = MidiAudioRenderer.Render(selected, 7, instrument, 120, sample,
            sampleRate: 16000, renderQuality: MidiRenderQuality.Draft, customCollection: [sample]);
        using AudioObj ensembleAudio = MidiAudioRenderer.Render(ensemble, 0, instrument, 120, sample,
            sampleRate: 16000, renderQuality: MidiRenderQuality.Draft, customCollection: [sample]);

        Assert.IsTrue(selectedAudio.Data.Take(4000).All(value => value == 0), "The unselected first page must stay silent.");
        Assert.IsTrue(ensembleAudio.Data.Take(4000).Any(value => Math.Abs(value) > 0.001f));
        Assert.IsTrue(selectedAudio.Data.Skip(8000).Any(value => Math.Abs(value) > 0.001f));
        Assert.IsTrue(ensembleAudio.Data.Skip(8000).Any(value => Math.Abs(value) > 0.001f));
        Assert.IsTrue(ensembleAudio.Data.All(float.IsFinite));
    }

    internal static MidiFileData CreateMidi()
    {
        var first = new MidiTrackData { Index = 2, Name = "Lead" };
        first.Notes.Add(new() { NoteNumber = 60, Channel = 1, Velocity = 100, StartTick = 0, DurationTicks = 240 });
        first.ExtendLengthTo(1920);
        var second = new MidiTrackData { Index = 7, Name = "Bass" };
        second.Notes.Add(new() { NoteNumber = 48, Channel = 3, Velocity = 75, StartTick = 120, DurationTicks = 480 });
        second.ExtendLengthTo(2880);
        var third = new MidiTrackData { Index = 11, Name = "Harmony" };
        third.Notes.Add(new() { NoteNumber = 67, Channel = 5, Velocity = 90, StartTick = 0, DurationTicks = 960 });
        third.ExtendLengthTo(3840);
        return MidiFileData.CreateGenerated([first, second, third], 960, 123, "pages.mid", 432);
    }

    internal static string[] AllNoteSignatures(MidiFileData midi) => midi.Tracks.SelectMany(track => track.Notes)
        .Select(NoteSignature).OrderBy(value => value, StringComparer.Ordinal).ToArray();

    private static string[] NoteSignatures(MidiTrackData track) => track.Notes.Select(NoteSignature).ToArray();

    private static string NoteSignature(MidiNoteData note) =>
        $"{note.StartTick}:{note.DurationTicks}:{note.NoteNumber}:{note.Channel}:{note.Velocity}";

    private static void AssertMetadata(MidiFileData expected, MidiFileData actual)
    {
        Assert.AreEqual(expected.FilePath, actual.FilePath);
        Assert.AreEqual(expected.TicksPerQuarterNote, actual.TicksPerQuarterNote);
        Assert.AreEqual(expected.DefaultBpm, actual.DefaultBpm);
        Assert.AreEqual(expected.PitchFrequency, actual.PitchFrequency);
    }
}
