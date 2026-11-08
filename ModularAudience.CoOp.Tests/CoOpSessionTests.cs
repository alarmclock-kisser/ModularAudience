using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio;
using ModularAudience.CoOp.Synchronization;

namespace ModularAudience.CoOp.Tests;

[TestClass]
public sealed class CoOpSessionTests
{
    [TestMethod]
    public async Task HostShare_ClientReceivesTracksAndSamples()
    {
        await using var scope = new SessionTestScope();
        CoOpSessionManager host = scope.CreateManager("Host");
        CoOpSessionManager client = scope.CreateManager("Client");
        using AudioCollection collection = TestAudio.CreateCollection();
        using AudioObj first = TestAudio.Create("Kick", sampleCount: 128);
        using AudioObj second = TestAudio.Create("Snare", sampleCount: 256, fill: 0.5f);
        collection.Audios.Add(first);
        collection.Audios.Add(second);

        CoOpHostInfo hostInfo = await host.StartHostingAsync();
        await client.ConnectAsync("127.0.0.1", hostInfo.Port);

        var remoteAdded = NewCompletionSource<CoOpRemoteCollection>();
        client.RemoteCollectionAdded += (_, e) => remoteAdded.TrySetResult(e.Collection);

        CoOpLocalShare share = await host.ShareCollectionAsync(collection, "Host Set");
        CoOpRemoteCollection remote = await remoteAdded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await TestAudio.WaitUntilAsync(() => remote.Collection.Audios.Count == 2);

        Assert.AreEqual(share.CollectionId, remote.CollectionId);
        Assert.AreEqual("Host Set", remote.Name);
        Assert.AreEqual("Host", remote.OwnerDisplayName);
        Assert.AreEqual(first.Id, remote.Collection.Audios[0].Id);
        CollectionAssert.AreEqual(first.Data, remote.Collection.Audios[0].Data);
        CollectionAssert.AreEqual(second.Data, remote.Collection.Audios[1].Data);
    }

    [TestMethod]
    public async Task HostChanges_PropagateAndLateJoinOmitsTombstones()
    {
        await using var scope = new SessionTestScope();
        CoOpSessionManager host = scope.CreateManager("Host");
        CoOpSessionManager client = scope.CreateManager("Client");
        using AudioCollection collection = TestAudio.CreateCollection();
        using AudioObj first = TestAudio.Create("Kick", sampleCount: 128);
        using AudioObj second = TestAudio.Create("Snare", sampleCount: 256);
        collection.Audios.Add(first);

        CoOpHostInfo hostInfo = await host.StartHostingAsync();
        await client.ConnectAsync("127.0.0.1", hostInfo.Port);
        var remoteAdded = NewCompletionSource<CoOpRemoteCollection>();
        client.RemoteCollectionAdded += (_, e) => remoteAdded.TrySetResult(e.Collection);
        await host.ShareCollectionAsync(collection, "Host Set");
        CoOpRemoteCollection remote = await remoteAdded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await TestAudio.WaitUntilAsync(() => remote.Collection.Audios.Count == 1);

        collection.Audios.Add(second);
        await TestAudio.WaitUntilAsync(() => remote.Collection[second.Id] != null);

        first.Name = "Kick updated";
        first.Data = [0.9f, 0.8f, 0.7f];
        first.Length = first.Data.Length;
        collection.Audios.ResetItem(collection.Audios.IndexOf(first));
        await TestAudio.WaitUntilAsync(() => remote.Collection[first.Id]?.Name == "Kick updated");
        CollectionAssert.AreEqual(first.Data, remote.Collection[first.Id]!.Data);

        collection.Audios.Remove(first);
        await TestAudio.WaitUntilAsync(() => remote.IsDeleted(first.Id));
        Assert.IsNotNull(remote.Collection[first.Id]);
        Assert.AreEqual("Kick updated", remote.Collection[first.Id]!.Name);

        CoOpSessionManager lateClient = scope.CreateManager("Late Client");
        var lateCollection = NewCompletionSource<CoOpRemoteCollection>();
        lateClient.RemoteCollectionAdded += (_, e) => lateCollection.TrySetResult(e.Collection);
        await lateClient.ConnectAsync("127.0.0.1", hostInfo.Port);
        CoOpRemoteCollection lateRemote = await lateCollection.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await TestAudio.WaitUntilAsync(() => lateRemote.Collection.Audios.Count == 1);

        Assert.IsNull(lateRemote.Collection[first.Id]);
        Assert.IsNotNull(lateRemote.Collection[second.Id]);
        Assert.IsFalse(lateRemote.IsDeleted(first.Id));
    }

    [TestMethod]
    public async Task ClientEdit_PropagatesToHostAndIsAcknowledged()
    {
        await using var scope = new SessionTestScope();
        CoOpSessionManager host = scope.CreateManager("Host");
        CoOpSessionManager client = scope.CreateManager("Client");
        using AudioCollection collection = TestAudio.CreateCollection();
        using AudioObj audio = TestAudio.Create("Kick", sampleCount: 128);
        collection.Audios.Add(audio);

        CoOpHostInfo hostInfo = await host.StartHostingAsync();
        await client.ConnectAsync("127.0.0.1", hostInfo.Port);
        var remoteAdded = NewCompletionSource<CoOpRemoteCollection>();
        client.RemoteCollectionAdded += (_, e) => remoteAdded.TrySetResult(e.Collection);
        await host.ShareCollectionAsync(collection, "Host Set");
        CoOpRemoteCollection remote = await remoteAdded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await TestAudio.WaitUntilAsync(() => remote.Collection.Audios.Count == 1);

        AudioObj remoteAudio = remote.Collection[audio.Id]!;
        remoteAudio.Name = "Kick from client";
        remoteAudio.Data = [0.11f, 0.22f, 0.33f];
        remoteAudio.Length = remoteAudio.Data.Length;
        remote.Collection.Audios.ResetItem(0);

        await TestAudio.WaitUntilAsync(() => collection[audio.Id]?.Name == "Kick from client");
        CollectionAssert.AreEqual(new[] { 0.11f, 0.22f, 0.33f }, collection[audio.Id]!.Data);
        await TestAudio.WaitUntilAsync(() => !client.IsAudioPending(remote.CollectionId, audio.Id));
    }

    [TestMethod]
    public async Task ClientRemoval_DoesNotDeleteOnHostOrOtherPeers()
    {
        await using var scope = new SessionTestScope();
        CoOpSessionManager host = scope.CreateManager("Host");
        CoOpSessionManager firstClient = scope.CreateManager("Client 1");
        CoOpSessionManager secondClient = scope.CreateManager("Client 2");
        using AudioCollection collection = TestAudio.CreateCollection();
        using AudioObj audio = TestAudio.Create("Kick", sampleCount: 128);
        collection.Audios.Add(audio);

        CoOpHostInfo hostInfo = await host.StartHostingAsync();
        await firstClient.ConnectAsync("127.0.0.1", hostInfo.Port);
        await secondClient.ConnectAsync("127.0.0.1", hostInfo.Port);

        var firstRemoteAdded = NewCompletionSource<CoOpRemoteCollection>();
        firstClient.RemoteCollectionAdded += (_, e) => firstRemoteAdded.TrySetResult(e.Collection);
        var secondRemoteAdded = NewCompletionSource<CoOpRemoteCollection>();
        secondClient.RemoteCollectionAdded += (_, e) => secondRemoteAdded.TrySetResult(e.Collection);

        await host.ShareCollectionAsync(collection, "Host Set");
        CoOpRemoteCollection firstRemote = await firstRemoteAdded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        CoOpRemoteCollection secondRemote = await secondRemoteAdded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await TestAudio.WaitUntilAsync(() => firstRemote.Collection.Audios.Count == 1);
        await TestAudio.WaitUntilAsync(() => secondRemote.Collection.Audios.Count == 1);

        firstRemote.Collection.Audios.RemoveAt(0);
        await Task.Delay(300);

        Assert.IsNotNull(collection[audio.Id]);
        Assert.IsNotNull(secondRemote.Collection[audio.Id]);
        Assert.IsFalse(secondRemote.IsDeleted(audio.Id));
    }

    [TestMethod]
    public async Task Unshare_KeepsRemoteDataAndMarksCollectionUnshared()
    {
        await using var scope = new SessionTestScope();
        CoOpSessionManager host = scope.CreateManager("Host");
        CoOpSessionManager client = scope.CreateManager("Client");
        using AudioCollection collection = TestAudio.CreateCollection();
        using AudioObj audio = TestAudio.Create("Kick", sampleCount: 128);
        collection.Audios.Add(audio);

        CoOpHostInfo hostInfo = await host.StartHostingAsync();
        await client.ConnectAsync("127.0.0.1", hostInfo.Port);
        var remoteAdded = NewCompletionSource<CoOpRemoteCollection>();
        client.RemoteCollectionAdded += (_, e) => remoteAdded.TrySetResult(e.Collection);
        CoOpLocalShare share = await host.ShareCollectionAsync(collection, "Host Set");
        CoOpRemoteCollection remote = await remoteAdded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await TestAudio.WaitUntilAsync(() => remote.Collection.Audios.Count == 1);

        await host.UnshareCollectionAsync(share.CollectionId);
        await TestAudio.WaitUntilAsync(() => remote.IsUnshared);

        Assert.IsNotNull(remote.Collection[audio.Id]);
        Assert.IsNull(host.FindLocalShare(share.CollectionId));
    }

    [TestMethod]
    public async Task ClientShare_IsRelayedToHostAndOtherClients()
    {
        await using var scope = new SessionTestScope();
        CoOpSessionManager host = scope.CreateManager("Host");
        CoOpSessionManager firstClient = scope.CreateManager("Client 1");
        CoOpSessionManager secondClient = scope.CreateManager("Client 2");
        using AudioCollection collection = TestAudio.CreateCollection();
        using AudioObj audio = TestAudio.Create("Kick", sampleCount: 128);
        collection.Audios.Add(audio);

        CoOpHostInfo hostInfo = await host.StartHostingAsync();
        await firstClient.ConnectAsync("127.0.0.1", hostInfo.Port);
        await secondClient.ConnectAsync("127.0.0.1", hostInfo.Port);

        var hostRemoteAdded = NewCompletionSource<CoOpRemoteCollection>();
        host.RemoteCollectionAdded += (_, e) => hostRemoteAdded.TrySetResult(e.Collection);
        var secondRemoteAdded = NewCompletionSource<CoOpRemoteCollection>();
        secondClient.RemoteCollectionAdded += (_, e) => secondRemoteAdded.TrySetResult(e.Collection);

        await firstClient.ShareCollectionAsync(collection, "Client Set");

        CoOpRemoteCollection hostRemote = await hostRemoteAdded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        CoOpRemoteCollection secondRemote = await secondRemoteAdded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await TestAudio.WaitUntilAsync(() => hostRemote.Collection[audio.Id] != null);
        await TestAudio.WaitUntilAsync(() => secondRemote.Collection[audio.Id] != null);

        Assert.AreEqual(firstClient.LocalPeerId, hostRemote.OwnerPeerId);
        Assert.AreEqual("Client Set", secondRemote.Name);
        CollectionAssert.AreEqual(audio.Data, secondRemote.Collection[audio.Id]!.Data);
    }

    [TestMethod]
    public async Task ClientEdit_PropagatesBackToCollectionOwner()
    {
        await using var scope = new SessionTestScope();
        CoOpSessionManager host = scope.CreateManager("Host");
        CoOpSessionManager owner = scope.CreateManager("Owner");
        CoOpSessionManager editor = scope.CreateManager("Editor");
        using AudioCollection collection = TestAudio.CreateCollection();
        using AudioObj audio = TestAudio.Create("Kick", sampleCount: 128);
        collection.Audios.Add(audio);

        CoOpHostInfo hostInfo = await host.StartHostingAsync();
        await owner.ConnectAsync("127.0.0.1", hostInfo.Port);
        await editor.ConnectAsync("127.0.0.1", hostInfo.Port);

        var hostRemoteAdded = NewCompletionSource<CoOpRemoteCollection>();
        host.RemoteCollectionAdded += (_, e) => hostRemoteAdded.TrySetResult(e.Collection);
        var editorRemoteAdded = NewCompletionSource<CoOpRemoteCollection>();
        editor.RemoteCollectionAdded += (_, e) => editorRemoteAdded.TrySetResult(e.Collection);

        await owner.ShareCollectionAsync(collection, "Owner Set");

        CoOpRemoteCollection hostRemote = await hostRemoteAdded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        CoOpRemoteCollection editorRemote = await editorRemoteAdded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await TestAudio.WaitUntilAsync(() => hostRemote.Collection[audio.Id] != null);
        await TestAudio.WaitUntilAsync(() => editorRemote.Collection[audio.Id] != null);

        AudioObj mirrored = editorRemote.Collection[audio.Id]!;
        mirrored.Name = "Edited by peer";
        mirrored.Data = [0.12f, 0.34f, 0.56f];
        mirrored.Length = mirrored.Data.Length;
        editorRemote.Collection.Audios.ResetItem(editorRemote.Collection.Audios.IndexOf(mirrored));

        await TestAudio.WaitUntilAsync(() => collection[audio.Id]?.Name == "Edited by peer");
        CollectionAssert.AreEqual(new[] { 0.12f, 0.34f, 0.56f }, collection[audio.Id]!.Data);
        await TestAudio.WaitUntilAsync(() => !editor.IsAudioPending(editorRemote.CollectionId, audio.Id));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task NonOwnerAdditionsAndEdits_SynchronizeWithOwnerHostAndAllClients(bool clientOwnsCollection)
    {
        await using var scope = new SessionTestScope();
        CoOpSessionManager host = scope.CreateManager("Host");
        CoOpSessionManager owner = clientOwnsCollection ? scope.CreateManager("Owner") : host;
        CoOpSessionManager editor = scope.CreateManager("Editor");
        CoOpSessionManager observer = scope.CreateManager("Observer");
        using AudioCollection collection = TestAudio.CreateCollection();
        using AudioObj original = TestAudio.Create("Original", sampleCount: 128);
        collection.Audios.Add(original);
        CoOpHostInfo hostInfo = await host.StartHostingAsync();
        if (clientOwnsCollection)
        {
            await owner.ConnectAsync("127.0.0.1", hostInfo.Port);
        }

        await editor.ConnectAsync("127.0.0.1", hostInfo.Port);
        await observer.ConnectAsync("127.0.0.1", hostInfo.Port);
        var hostRemoteAdded = NewCompletionSource<CoOpRemoteCollection>();
        host.RemoteCollectionAdded += (_, e) => hostRemoteAdded.TrySetResult(e.Collection);
        var editorRemoteAdded = NewCompletionSource<CoOpRemoteCollection>();
        editor.RemoteCollectionAdded += (_, e) => editorRemoteAdded.TrySetResult(e.Collection);
        var observerRemoteAdded = NewCompletionSource<CoOpRemoteCollection>();
        observer.RemoteCollectionAdded += (_, e) => observerRemoteAdded.TrySetResult(e.Collection);

        await owner.ShareCollectionAsync(collection, "Shared Set");
        CoOpRemoteCollection editorRemote = await editorRemoteAdded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        CoOpRemoteCollection observerRemote = await observerRemoteAdded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        CoOpRemoteCollection? hostRemote = clientOwnsCollection
            ? await hostRemoteAdded.Task.WaitAsync(TimeSpan.FromSeconds(5))
            : null;
        await TestAudio.WaitUntilAsync(() => editorRemote.Collection[original.Id] != null);
        await TestAudio.WaitUntilAsync(() => observerRemote.Collection[original.Id] != null);

        using AudioObj addedByClient = TestAudio.Create("Created by connected client", sampleCount: 96, fill: 0.6f);
        editorRemote.Collection.Audios.Add(addedByClient);

        await TestAudio.WaitUntilAsync(() => collection[addedByClient.Id] != null);
        await TestAudio.WaitUntilAsync(() => observerRemote.Collection[addedByClient.Id] != null);
        if (hostRemote != null)
        {
            await TestAudio.WaitUntilAsync(() => hostRemote.Collection[addedByClient.Id] != null);
            CollectionAssert.AreEqual(addedByClient.Data, hostRemote.Collection[addedByClient.Id]!.Data);
        }

        CollectionAssert.AreEqual(addedByClient.Data, collection[addedByClient.Id]!.Data);
        CollectionAssert.AreEqual(addedByClient.Data, observerRemote.Collection[addedByClient.Id]!.Data);

        addedByClient.Name = "Edited again by connected client";
        addedByClient.Data = [0.2f, 0.4f, 0.6f];
        addedByClient.Length = addedByClient.Data.Length;
        await TestAudio.WaitUntilAsync(() => collection[addedByClient.Id]?.Name == addedByClient.Name);
        await TestAudio.WaitUntilAsync(() => observerRemote.Collection[addedByClient.Id]?.Name == addedByClient.Name);
        CollectionAssert.AreEqual(addedByClient.Data, collection[addedByClient.Id]!.Data);
        CollectionAssert.AreEqual(addedByClient.Data, observerRemote.Collection[addedByClient.Id]!.Data);

        using AudioObj addedByHost = TestAudio.Create("Created by host", sampleCount: 64, fill: 0.3f);
        (hostRemote?.Collection ?? collection).Audios.Add(addedByHost);
        await TestAudio.WaitUntilAsync(() => collection[addedByHost.Id] != null);
        await TestAudio.WaitUntilAsync(() => editorRemote.Collection[addedByHost.Id] != null);
        await TestAudio.WaitUntilAsync(() => observerRemote.Collection[addedByHost.Id] != null);
        CollectionAssert.AreEqual(addedByHost.Data, collection[addedByHost.Id]!.Data);
        CollectionAssert.AreEqual(addedByHost.Data, editorRemote.Collection[addedByHost.Id]!.Data);
        CollectionAssert.AreEqual(addedByHost.Data, observerRemote.Collection[addedByHost.Id]!.Data);
        await TestAudio.WaitUntilAsync(() => !editor.IsAudioPending(editorRemote.CollectionId, addedByClient.Id));
    }

    [TestMethod]
    public async Task PropertyEdit_PropagatesWithoutResetItem()
    {
        await using var scope = new SessionTestScope();
        CoOpSessionManager host = scope.CreateManager("Host");
        CoOpSessionManager client = scope.CreateManager("Client");
        using AudioCollection collection = TestAudio.CreateCollection();
        using AudioObj audio = TestAudio.Create("Kick", sampleCount: 128);
        collection.Audios.Add(audio);

        CoOpHostInfo hostInfo = await host.StartHostingAsync();
        await client.ConnectAsync("127.0.0.1", hostInfo.Port);
        var remoteAdded = NewCompletionSource<CoOpRemoteCollection>();
        client.RemoteCollectionAdded += (_, e) => remoteAdded.TrySetResult(e.Collection);
        await host.ShareCollectionAsync(collection, "Host Set");
        CoOpRemoteCollection remote = await remoteAdded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await TestAudio.WaitUntilAsync(() => remote.Collection.Audios.Count == 1);

        AudioObj mirrored = remote.Collection[audio.Id]!;
        mirrored.Name = "Renamed by property";
        mirrored.Bpm = 133;

        await TestAudio.WaitUntilAsync(() => collection[audio.Id]?.Name == "Renamed by property");
        Assert.AreEqual(133f, collection[audio.Id]!.Bpm);
    }

    [TestMethod]
    public async Task OwnerReAdd_ClearsTombstoneOnPeers()
    {
        await using var scope = new SessionTestScope();
        CoOpSessionManager host = scope.CreateManager("Host");
        CoOpSessionManager client = scope.CreateManager("Client");
        using AudioCollection collection = TestAudio.CreateCollection();
        using AudioObj audio = TestAudio.Create("Kick", sampleCount: 128);
        collection.Audios.Add(audio);

        CoOpHostInfo hostInfo = await host.StartHostingAsync();
        await client.ConnectAsync("127.0.0.1", hostInfo.Port);
        var remoteAdded = NewCompletionSource<CoOpRemoteCollection>();
        client.RemoteCollectionAdded += (_, e) => remoteAdded.TrySetResult(e.Collection);
        await host.ShareCollectionAsync(collection, "Host Set");
        CoOpRemoteCollection remote = await remoteAdded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await TestAudio.WaitUntilAsync(() => remote.Collection[audio.Id] != null);

        collection.Audios.Remove(audio);
        await TestAudio.WaitUntilAsync(() => remote.IsDeleted(audio.Id));

        collection.Audios.Add(audio);
        await TestAudio.WaitUntilAsync(() => !remote.IsDeleted(audio.Id));
        Assert.IsNotNull(remote.Collection[audio.Id]);
    }

    [TestMethod]
    public async Task ClientUnshare_IsRelayedToHostAndOtherClients()
    {
        await using var scope = new SessionTestScope();
        CoOpSessionManager host = scope.CreateManager("Host");
        CoOpSessionManager owner = scope.CreateManager("Owner");
        CoOpSessionManager observer = scope.CreateManager("Observer");
        using AudioCollection collection = TestAudio.CreateCollection();
        using AudioObj audio = TestAudio.Create("Kick", sampleCount: 128);
        collection.Audios.Add(audio);

        CoOpHostInfo hostInfo = await host.StartHostingAsync();
        await owner.ConnectAsync("127.0.0.1", hostInfo.Port);
        await observer.ConnectAsync("127.0.0.1", hostInfo.Port);

        var hostRemoteAdded = NewCompletionSource<CoOpRemoteCollection>();
        host.RemoteCollectionAdded += (_, e) => hostRemoteAdded.TrySetResult(e.Collection);
        var observerRemoteAdded = NewCompletionSource<CoOpRemoteCollection>();
        observer.RemoteCollectionAdded += (_, e) => observerRemoteAdded.TrySetResult(e.Collection);

        CoOpLocalShare share = await owner.ShareCollectionAsync(collection, "Owner Set");
        CoOpRemoteCollection hostRemote = await hostRemoteAdded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        CoOpRemoteCollection observerRemote = await observerRemoteAdded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await TestAudio.WaitUntilAsync(() => hostRemote.Collection[audio.Id] != null);
        await TestAudio.WaitUntilAsync(() => observerRemote.Collection[audio.Id] != null);

        await owner.UnshareCollectionAsync(share.CollectionId);

        await TestAudio.WaitUntilAsync(() => hostRemote.IsUnshared);
        await TestAudio.WaitUntilAsync(() => observerRemote.IsUnshared);
        Assert.IsNotNull(hostRemote.Collection[audio.Id]);
        Assert.IsNotNull(observerRemote.Collection[audio.Id]);
    }

    [TestMethod]
    public async Task ClientReshare_RetainsRemovedAudioAsTombstone()
    {
        await using var scope = new SessionTestScope();
        CoOpSessionManager host = scope.CreateManager("Host");
        CoOpSessionManager owner = scope.CreateManager("Owner");
        using AudioCollection collection = TestAudio.CreateCollection();
        using AudioObj removedWhileUnshared = TestAudio.Create("Removed", sampleCount: 128);
        using AudioObj kept = TestAudio.Create("Kept", sampleCount: 128);
        collection.Audios.Add(removedWhileUnshared);
        collection.Audios.Add(kept);

        CoOpHostInfo hostInfo = await host.StartHostingAsync();
        await owner.ConnectAsync("127.0.0.1", hostInfo.Port);
        var hostRemoteAdded = NewCompletionSource<CoOpRemoteCollection>();
        host.RemoteCollectionAdded += (_, e) => hostRemoteAdded.TrySetResult(e.Collection);

        CoOpLocalShare share = await owner.ShareCollectionAsync(collection, "Owner Set");
        CoOpRemoteCollection hostRemote = await hostRemoteAdded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await TestAudio.WaitUntilAsync(() => hostRemote.Collection.Audios.Count == 2);

        await owner.UnshareCollectionAsync(share.CollectionId);
        await TestAudio.WaitUntilAsync(() => hostRemote.IsUnshared);
        collection.Audios.Remove(removedWhileUnshared);

        await owner.ShareCollectionAsync(collection, "Owner Set");

        await TestAudio.WaitUntilAsync(() => !hostRemote.IsUnshared);
        await TestAudio.WaitUntilAsync(() => hostRemote.IsDeleted(removedWhileUnshared.Id));
        Assert.AreEqual(2, hostRemote.Collection.Audios.Count);
        Assert.IsNotNull(hostRemote.Collection[removedWhileUnshared.Id]);
        CollectionAssert.AreEqual(removedWhileUnshared.Data, hostRemote.Collection[removedWhileUnshared.Id]!.Data);
        Assert.IsNotNull(hostRemote.Collection[kept.Id]);

        CoOpSessionManager lateClient = scope.CreateManager("Late Client");
        var lateCollection = NewCompletionSource<CoOpRemoteCollection>();
        lateClient.RemoteCollectionAdded += (_, e) => lateCollection.TrySetResult(e.Collection);
        await lateClient.ConnectAsync("127.0.0.1", hostInfo.Port);
        CoOpRemoteCollection lateRemote = await lateCollection.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await TestAudio.WaitUntilAsync(() => lateRemote.Collection[kept.Id] != null);
        Assert.IsNull(lateRemote.Collection[removedWhileUnshared.Id]);
    }

    [TestMethod]
    public async Task HostStop_ClientKeepsDataAndMarksCollectionsUnshared()
    {
        await using var scope = new SessionTestScope();
        CoOpSessionManager host = scope.CreateManager("Host");
        CoOpSessionManager client = scope.CreateManager("Client");
        using AudioCollection collection = TestAudio.CreateCollection();
        using AudioObj audio = TestAudio.Create("Kick", sampleCount: 128);
        collection.Audios.Add(audio);

        CoOpHostInfo hostInfo = await host.StartHostingAsync();
        await client.ConnectAsync("127.0.0.1", hostInfo.Port);
        var remoteAdded = NewCompletionSource<CoOpRemoteCollection>();
        client.RemoteCollectionAdded += (_, e) => remoteAdded.TrySetResult(e.Collection);
        await host.ShareCollectionAsync(collection, "Host Set");
        CoOpRemoteCollection remote = await remoteAdded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await TestAudio.WaitUntilAsync(() => remote.Collection.Audios.Count == 1);

        await host.StopAsync();

        await TestAudio.WaitUntilAsync(() => client.State == CoOpSessionState.Disconnected);
        await TestAudio.WaitUntilAsync(() => remote.IsUnshared);
        Assert.IsNotNull(remote.Collection[audio.Id]);
    }

    [TestMethod]
    public async Task LateJoin_ReceivesCurrentStateOnly()
    {
        await using var scope = new SessionTestScope();
        CoOpSessionManager host = scope.CreateManager("Host");
        using AudioCollection collection = TestAudio.CreateCollection();
        using AudioObj removed = TestAudio.Create("Removed", sampleCount: 128);
        using AudioObj kept = TestAudio.Create("Kept", sampleCount: 128);
        collection.Audios.Add(removed);
        collection.Audios.Add(kept);

        CoOpHostInfo hostInfo = await host.StartHostingAsync();
        var share = await host.ShareCollectionAsync(collection, "Host Set");
        collection.Audios.Remove(removed);
        kept.Name = "Kept updated";
        collection.Audios.ResetItem(collection.Audios.IndexOf(kept));

        CoOpSessionManager lateClient = scope.CreateManager("Late Client");
        var lateCollection = NewCompletionSource<CoOpRemoteCollection>();
        lateClient.RemoteCollectionAdded += (_, e) => lateCollection.TrySetResult(e.Collection);
        await lateClient.ConnectAsync("127.0.0.1", hostInfo.Port);
        CoOpRemoteCollection remote = await lateCollection.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await TestAudio.WaitUntilAsync(() => remote.Collection.Audios.Count == 1);

        Assert.AreEqual(share.CollectionId, remote.CollectionId);
        Assert.IsNull(remote.Collection[removed.Id]);
        Assert.IsNotNull(remote.Collection[kept.Id]);
        Assert.AreEqual("Kept updated", remote.Collection[kept.Id]!.Name);
    }

    [TestMethod]
    public async Task FirstClientJoiningLater_ReceivesAllPreSharedCollectionsWithCurrentContent()
    {
        await using var scope = new SessionTestScope();
        CoOpSessionManager host = scope.CreateManager("Host");
        using AudioCollection firstCollection = TestAudio.CreateCollection();
        using AudioCollection secondCollection = TestAudio.CreateCollection();
        using AudioCollection emptyCollection = TestAudio.CreateCollection();
        using AudioObj removedBeforeJoin = TestAudio.Create("Removed before joining", sampleCount: 64);
        using AudioObj firstAudio = TestAudio.Create("First original", sampleCount: 128);
        using AudioObj secondAudio = TestAudio.Create("Second", sampleCount: 96, fill: 0.6f);
        firstCollection.Audios.Add(removedBeforeJoin);
        firstCollection.Audios.Add(firstAudio);
        secondCollection.Audios.Add(secondAudio);

        CoOpHostInfo hostInfo = await host.StartHostingAsync();
        CoOpLocalShare firstShare = await host.ShareCollectionAsync(firstCollection, "First Set");
        CoOpLocalShare secondShare = await host.ShareCollectionAsync(secondCollection, "Second Set");
        CoOpLocalShare emptyShare = await host.ShareCollectionAsync(emptyCollection, "Empty Set");
        firstCollection.Audios.Remove(removedBeforeJoin);
        firstAudio.Name = "Changed before anyone joined";
        firstAudio.Data = [0.2f, 0.3f, 0.4f];
        firstAudio.Length = firstAudio.Data.Length;

        CoOpSessionManager lateClient = scope.CreateManager("First client joining later");
        await lateClient.ConnectAsync("127.0.0.1", hostInfo.Port);
        await TestAudio.WaitUntilAsync(() => lateClient.RemoteCollections.Count == 3);
        CoOpRemoteCollection firstRemote = lateClient.FindRemoteCollection(firstShare.CollectionId)!;
        CoOpRemoteCollection secondRemote = lateClient.FindRemoteCollection(secondShare.CollectionId)!;
        CoOpRemoteCollection emptyRemote = lateClient.FindRemoteCollection(emptyShare.CollectionId)!;
        await TestAudio.WaitUntilAsync(() => firstRemote.Collection[firstAudio.Id] != null);
        await TestAudio.WaitUntilAsync(() => secondRemote.Collection[secondAudio.Id] != null);

        Assert.AreEqual("Changed before anyone joined", firstRemote.Collection[firstAudio.Id]!.Name);
        CollectionAssert.AreEqual(firstAudio.Data, firstRemote.Collection[firstAudio.Id]!.Data);
        CollectionAssert.AreEqual(secondAudio.Data, secondRemote.Collection[secondAudio.Id]!.Data);
        Assert.IsNull(firstRemote.Collection[removedBeforeJoin.Id]);
        Assert.IsFalse(firstRemote.IsDeleted(removedBeforeJoin.Id));
        Assert.AreEqual(0, emptyRemote.Collection.Audios.Count);
        Assert.IsTrue(lateClient.RemoteCollections.All(collection => !collection.IsUnshared));
    }

    [TestMethod]
    public async Task TombstoneEdit_RemainsLocalAndDoesNotRestoreOwnersAudio()
    {
        await using var scope = new SessionTestScope();
        CoOpSessionManager host = scope.CreateManager("Host");
        CoOpSessionManager client = scope.CreateManager("Client");
        using AudioCollection collection = TestAudio.CreateCollection();
        using AudioObj audio = TestAudio.Create("Kick", sampleCount: 128);
        collection.Audios.Add(audio);

        CoOpHostInfo hostInfo = await host.StartHostingAsync();
        await client.ConnectAsync("127.0.0.1", hostInfo.Port);
        var remoteAdded = NewCompletionSource<CoOpRemoteCollection>();
        client.RemoteCollectionAdded += (_, e) => remoteAdded.TrySetResult(e.Collection);
        await host.ShareCollectionAsync(collection, "Host Set");
        CoOpRemoteCollection remote = await remoteAdded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await TestAudio.WaitUntilAsync(() => remote.Collection[audio.Id] != null);

        collection.Audios.Remove(audio);
        await TestAudio.WaitUntilAsync(() => remote.IsDeleted(audio.Id));
        remote.Collection[audio.Id]!.Name = "Local copy";
        await Task.Delay(400);

        Assert.IsNull(collection[audio.Id]);
        Assert.AreEqual("Local copy", remote.Collection[audio.Id]!.Name);
        Assert.IsFalse(remote.IsAudioPending(audio.Id));
    }

    [TestMethod]
    public async Task HostLocalDiscard_DoesNotRemoveClientOwnedAudioFromLateJoinState()
    {
        await using var scope = new SessionTestScope();
        CoOpSessionManager host = scope.CreateManager("Host");
        CoOpSessionManager owner = scope.CreateManager("Owner");
        using AudioCollection collection = TestAudio.CreateCollection();
        using AudioObj audio = TestAudio.Create("Kick", sampleCount: 128);
        collection.Audios.Add(audio);

        CoOpHostInfo hostInfo = await host.StartHostingAsync();
        await owner.ConnectAsync("127.0.0.1", hostInfo.Port);
        var hostRemoteAdded = NewCompletionSource<CoOpRemoteCollection>();
        host.RemoteCollectionAdded += (_, e) => hostRemoteAdded.TrySetResult(e.Collection);
        await owner.ShareCollectionAsync(collection, "Owner Set");
        CoOpRemoteCollection hostRemote = await hostRemoteAdded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await TestAudio.WaitUntilAsync(() => hostRemote.Collection[audio.Id] != null);
        await hostRemote.Collection.RemoveAsync(audio.Id);

        CoOpSessionManager lateClient = scope.CreateManager("Late Client");
        var lateCollection = NewCompletionSource<CoOpRemoteCollection>();
        lateClient.RemoteCollectionAdded += (_, e) => lateCollection.TrySetResult(e.Collection);
        await lateClient.ConnectAsync("127.0.0.1", hostInfo.Port);
        CoOpRemoteCollection lateRemote = await lateCollection.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await TestAudio.WaitUntilAsync(() => lateRemote.Collection[audio.Id] != null);

        Assert.IsNotNull(collection[audio.Id]);
        Assert.IsNull(hostRemote.Collection[audio.Id]);
        CollectionAssert.AreEqual(audio.Data, lateRemote.Collection[audio.Id]!.Data);
    }

    [TestMethod]
    public async Task Reconnect_DoesNotDiscardPreviouslyReceivedCollections()
    {
        await using var scope = new SessionTestScope();
        CoOpSessionManager firstHost = scope.CreateManager("First Host");
        CoOpSessionManager secondHost = scope.CreateManager("Second Host");
        CoOpSessionManager client = scope.CreateManager("Client");
        using AudioCollection collection = TestAudio.CreateCollection();
        using AudioObj audio = TestAudio.Create("Kick", sampleCount: 128);
        collection.Audios.Add(audio);

        CoOpHostInfo firstInfo = await firstHost.StartHostingAsync();
        await client.ConnectAsync("127.0.0.1", firstInfo.Port);
        var remoteAdded = NewCompletionSource<CoOpRemoteCollection>();
        client.RemoteCollectionAdded += (_, e) => remoteAdded.TrySetResult(e.Collection);
        await firstHost.ShareCollectionAsync(collection, "First Host Set");
        CoOpRemoteCollection remote = await remoteAdded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await TestAudio.WaitUntilAsync(() => remote.Collection[audio.Id] != null);
        await client.StopAsync();

        CoOpHostInfo secondInfo = await secondHost.StartHostingAsync();
        await client.ConnectAsync("127.0.0.1", secondInfo.Port);

        Assert.IsTrue(remote.IsUnshared);
        Assert.IsTrue(client.RemoteCollections.Contains(remote));
        Assert.IsNotNull(remote.Collection[audio.Id]);
        CollectionAssert.AreEqual(audio.Data, remote.Collection[audio.Id]!.Data);
    }

    private static TaskCompletionSource<T> NewCompletionSource<T>()
    {
        return new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
