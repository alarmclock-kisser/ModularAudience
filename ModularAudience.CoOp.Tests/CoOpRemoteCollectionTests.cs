using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio;
using ModularAudience.CoOp.Protocol;
using ModularAudience.CoOp.Synchronization;

namespace ModularAudience.CoOp.Tests;

[TestClass]
public sealed class CoOpRemoteCollectionTests
{
    [TestMethod]
    public async Task Reshare_ReconcilesMembershipWithoutDisposingRetainedAudio()
    {
        await using var scope = new SessionTestScope();
        using CoOpRemoteCollection remote = CreateRemote(scope.Root);
        using AudioObj first = TestAudio.Create("First");
        using AudioObj second = TestAudio.Create("Second");
        AudioObj retained = remote.ApplyAdd(CoOpAudioSnapshotMapper.Capture(first))!;
        AudioObj kept = remote.ApplyAdd(CoOpAudioSnapshotMapper.Capture(second))!;

        remote.MarkUnshared();
        remote.BeginSynchronization(replaceExisting: true);
        remote.ApplyAdd(CoOpAudioSnapshotMapper.Capture(second) with { Name = "Second updated" });
        remote.CompleteSynchronization();

        Assert.AreSame(retained, remote.Collection[first.Id]);
        Assert.AreSame(kept, remote.Collection[second.Id]);
        Assert.IsTrue(remote.IsDeleted(first.Id));
        Assert.IsFalse(remote.IsDeleted(second.Id));
        CollectionAssert.AreEqual(first.Data, retained.Data);
        Assert.AreEqual("Second updated", kept.Name);
        CollectionAssert.AreEqual(new[] { second.Id }, remote.GetLateJoinSnapshots().Select(audio => audio.AudioId).ToArray());
    }

    [TestMethod]
    public async Task StaleUpdate_DoesNotOverwriteTombstonesLastData()
    {
        await using var scope = new SessionTestScope();
        using CoOpRemoteCollection remote = CreateRemote(scope.Root);
        using AudioObj original = TestAudio.Create("Last synchronized version");
        CoOpAudioSnapshot snapshot = CoOpAudioSnapshotMapper.Capture(original);
        AudioObj retained = remote.ApplyAdd(snapshot)!;
        remote.ApplyRemove(original.Id);

        AudioObj? applied = remote.ApplyUpdate(snapshot with { Name = "Stale edit", SampleData = [] });

        Assert.IsNull(applied);
        Assert.AreEqual(original.Name, retained.Name);
        CollectionAssert.AreEqual(original.Data, retained.Data);
        Assert.IsTrue(remote.IsDeleted(original.Id));
    }

    [TestMethod]
    public async Task LocalDiscard_KeepsSessionSnapshotWithoutRecreatingLocalAudio()
    {
        await using var scope = new SessionTestScope();
        using CoOpRemoteCollection remote = CreateRemote(scope.Root);
        using AudioObj original = TestAudio.Create("Kick");
        CoOpAudioSnapshot snapshot = CoOpAudioSnapshotMapper.Capture(original);
        remote.ApplyAdd(snapshot);
        remote.Tracker.ItemRemoved += (_, e) => remote.MarkLocallyDiscarded(e.Audio.Id);
        await remote.Collection.RemoveAsync(original.Id);

        remote.ApplyUpdate(snapshot with { Name = "Owner updated" });

        Assert.IsNull(remote.Collection[original.Id]);
        CoOpAudioSnapshot lateJoin = remote.GetLateJoinSnapshots().Single();
        Assert.AreEqual("Owner updated", lateJoin.Name);
        CollectionAssert.AreEqual(snapshot.SampleData, lateJoin.SampleData);
    }

    [TestMethod]
    public async Task Unshare_ClearsPendingIndicatorsButKeepsSamples()
    {
        await using var scope = new SessionTestScope();
        using CoOpRemoteCollection remote = CreateRemote(scope.Root);
        using AudioObj original = TestAudio.Create("Kick");
        remote.ApplyAdd(CoOpAudioSnapshotMapper.Capture(original));
        remote.MarkPending(original.Id, 7);

        remote.MarkUnshared();

        Assert.IsTrue(remote.IsUnshared);
        Assert.IsFalse(remote.IsAudioPending(original.Id));
        CollectionAssert.AreEqual(original.Data, remote.Collection[original.Id]!.Data);
    }

    [TestMethod]
    public async Task Acknowledgement_DoesNotClearNewerPendingRevision()
    {
        await using var scope = new SessionTestScope();
        using CoOpRemoteCollection remote = CreateRemote(scope.Root);
        Guid audioId = Guid.NewGuid();
        remote.MarkPending(audioId, 8);

        remote.Acknowledge(audioId, 7);
        Assert.IsTrue(remote.IsAudioPending(audioId));

        remote.Acknowledge(audioId, 8);
        Assert.IsFalse(remote.IsAudioPending(audioId));
    }

    private static CoOpRemoteCollection CreateRemote(string root)
    {
        return new CoOpRemoteCollection(Guid.NewGuid(), "Shared collection", Guid.NewGuid(), "Owner", root);
    }

    [TestMethod]
    public void DefaultReceivedCollectionWorkspace_IsTheNormalMusicWorkspaceNotATemporaryCoOpCache()
    {
        var options = new CoOpSessionOptions();

        Assert.AreEqual(AudioStoragePaths.WorkingDirectory, options.RemoteCollectionsRoot);
        Assert.AreNotEqual(Path.Combine(Path.GetTempPath(), "ModularAudience", "CoOp"), options.RemoteCollectionsRoot);
    }

    [TestMethod]
    public async Task ReceivedSamples_ExportToRecipientWorkspaceWithoutGuidOrOwnerTemporaryPaths()
    {
        await using var scope = new SessionTestScope();
        using CoOpRemoteCollection remote = CreateRemote(scope.Root);
        using AudioObj original = TestAudio.Create("Received sample", sampleCount: 128);
        original.FilePath = Path.Combine(Path.GetTempPath(), "RemoteOwnerCache", "source.wav");
        AudioObj received = remote.ApplyAdd(CoOpAudioSnapshotMapper.Capture(original))!;

        string? exported = await remote.Collection.Exporter.ExportWavAsync(received, bitDepth: 16, writeBpmTag: false);

        Assert.AreEqual(scope.Root, remote.Collection.WorkingDirectory);
        Assert.AreEqual(Path.Combine(scope.Root, "_Exports"), remote.Collection.ExportPath);
        Assert.AreEqual(Path.Combine(scope.Root, "_Records"), remote.Collection.RecordPath);
        Assert.IsFalse(Directory.Exists(Path.Combine(scope.Root, "_Recordings")));
        Assert.IsNotNull(exported);
        Assert.AreEqual(remote.Collection.ExportPath, Path.GetDirectoryName(exported));
        Assert.IsTrue(File.Exists(exported));
        Assert.IsTrue(new FileInfo(exported).Length > 44);
        Assert.IsFalse(exported.Contains(remote.CollectionId.ToString("N"), StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(exported.Contains("RemoteOwnerCache", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task DefaultMirrors_FollowChangedWorkspaceWhileExplicitSessionDirectoriesStayIndependent()
    {
        await using var scope = new SessionTestScope();
        try
        {
            var defaults = new CoOpSessionOptions();
            AudioStoragePaths.SetWorkingDirectory(Path.Combine(scope.Root, "First"));
            using var mirror = new CoOpRemoteCollection(Guid.NewGuid(), "Shared", Guid.NewGuid(), "Owner", defaults.RemoteCollectionsRoot,
                followDefaultWorkingDirectory: defaults.UsesDefaultWorkingDirectory);
            using CoOpRemoteCollection custom = CreateRemote(Path.Combine(scope.Root, "Custom"));

            string next = Path.Combine(scope.Root, "Next");
            AudioStoragePaths.SetWorkingDirectory(next);

            Assert.AreEqual(next, defaults.RemoteCollectionsRoot);
            Assert.AreEqual(next, mirror.Collection.WorkingDirectory);
            Assert.AreEqual(Path.Combine(next, "_Exports"), mirror.Collection.Exporter.ExportDirectory);
            Assert.AreEqual(Path.Combine(scope.Root, "Custom"), custom.Collection.WorkingDirectory);
        }
        finally { AudioStoragePaths.SetWorkingDirectory(null); }
    }
}
