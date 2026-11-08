using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio;
using ModularAudience.CoOp.Synchronization;

namespace ModularAudience.CoOp.Tests;

[TestClass]
public sealed class CoOpCollectionTrackerTests
{
    [TestMethod]
    public async Task Tracker_ReportsAddChangeAndRemove()
    {
        using var collection = TestAudio.CreateCollection();
        using var tracker = new CoOpCollectionTracker(collection);
        var added = new List<Guid>();
        var changed = new List<Guid>();
        var removed = new List<Guid>();
        tracker.ItemAdded += (_, e) => added.Add(e.Audio.Id);
        tracker.ItemChanged += (_, e) => changed.Add(e.Audio.Id);
        tracker.ItemRemoved += (_, e) => removed.Add(e.Audio.Id);

        using var audio = TestAudio.Create("Kick");
        collection.Audios.Add(audio);
        audio.Name = "Kick changed";
        audio.OriginalName = "Kick changed";
        collection.Audios.ResetItem(0);
        await TestAudio.WaitUntilAsync(() => changed.Count > 0);
        collection.Audios.RemoveAt(0);

        CollectionAssert.AreEqual(new[] { audio.Id }, added);
        CollectionAssert.AreEqual(new[] { audio.Id }, changed);
        CollectionAssert.AreEqual(new[] { audio.Id }, removed);
    }

    [TestMethod]
    public async Task Tracker_DebouncesBatchedPropertyChanges()
    {
        using var collection = TestAudio.CreateCollection();
        using var tracker = new CoOpCollectionTracker(collection);
        int changed = 0;
        tracker.ItemChanged += (_, _) => Interlocked.Increment(ref changed);

        using var audio = TestAudio.Create("Kick");
        collection.Audios.Add(audio);
        audio.Name = "Kick changed";
        audio.Bpm = 128;
        audio.Key = "Am";
        audio.Volume = 0.5f;

        await TestAudio.WaitUntilAsync(() => Volatile.Read(ref changed) > 0);
        await Task.Delay(300);

        Assert.AreEqual(1, Volatile.Read(ref changed));
    }

    [TestMethod]
    public void Tracker_ReportsResetRemovalsAndAdditions()
    {
        using var collection = TestAudio.CreateCollection();
        using var first = TestAudio.Create("First");
        using var second = TestAudio.Create("Second");
        collection.Audios.Add(first);

        using var tracker = new CoOpCollectionTracker(collection);
        var added = new List<Guid>();
        var removed = new List<Guid>();
        tracker.ItemAdded += (_, e) => added.Add(e.Audio.Id);
        tracker.ItemRemoved += (_, e) => removed.Add(e.Audio.Id);

        collection.Audios.Clear();
        collection.Audios.Add(second);

        CollectionAssert.AreEqual(new[] { first.Id }, removed);
        CollectionAssert.AreEqual(new[] { second.Id }, added);
    }

    [TestMethod]
    public async Task Tracker_SuppressionPreventsEcho()
    {
        using var collection = TestAudio.CreateCollection();
        using var tracker = new CoOpCollectionTracker(collection);
        int events = 0;
        tracker.ItemAdded += (_, _) => events++;
        tracker.ItemChanged += (_, _) => events++;
        tracker.ItemRemoved += (_, _) => events++;

        using var audio = TestAudio.Create("Kick");
        using (tracker.SuppressNotifications())
        {
            collection.Audios.Add(audio);
            audio.Name = "Changed";
            collection.Audios.ResetItem(0);
            collection.Audios.RemoveAt(0);
        }

        await Task.Delay(300);
        Assert.AreEqual(0, events);
    }

    [TestMethod]
    public async Task Tracker_IndexReplacementReportsRemovalAndAddition()
    {
        using var collection = TestAudio.CreateCollection();
        using var first = TestAudio.Create("First");
        using var second = TestAudio.Create("Second");
        collection.Audios.Add(first);
        using var tracker = new CoOpCollectionTracker(collection);
        var added = new List<Guid>();
        var removed = new List<Guid>();
        int changed = 0;
        tracker.ItemAdded += (_, e) => added.Add(e.Audio.Id);
        tracker.ItemRemoved += (_, e) => removed.Add(e.Audio.Id);
        tracker.ItemChanged += (_, _) => Interlocked.Increment(ref changed);

        collection.Audios[0] = second;
        first.Name = "Detached";
        await Task.Delay(300);

        CollectionAssert.AreEqual(new[] { first.Id }, removed);
        CollectionAssert.AreEqual(new[] { second.Id }, added);
        Assert.AreEqual(0, Volatile.Read(ref changed));

        second.Name = "Tracked";
        await TestAudio.WaitUntilAsync(() => Volatile.Read(ref changed) == 1);
    }

    [TestMethod]
    public async Task Tracker_ResetBindingsReportsInPlaceChanges()
    {
        using var collection = TestAudio.CreateCollection();
        using var audio = TestAudio.Create("Kick");
        collection.Audios.Add(audio);
        using var tracker = new CoOpCollectionTracker(collection);
        int changed = 0;
        tracker.ItemChanged += (_, _) => Interlocked.Increment(ref changed);

        audio.Data[0] = 0.9f;
        collection.Audios.ResetBindings();

        await TestAudio.WaitUntilAsync(() => Volatile.Read(ref changed) == 1);
    }

    [TestMethod]
    public async Task Tracker_DisposeCancelsPendingPropertyChanges()
    {
        using var collection = TestAudio.CreateCollection();
        using var audio = TestAudio.Create("Kick");
        collection.Audios.Add(audio);
        using var tracker = new CoOpCollectionTracker(collection);
        int changed = 0;
        tracker.ItemChanged += (_, _) => Interlocked.Increment(ref changed);

        audio.Name = "Pending edit";
        tracker.Dispose();
        await Task.Delay(300);

        Assert.AreEqual(0, Volatile.Read(ref changed));
    }
}
