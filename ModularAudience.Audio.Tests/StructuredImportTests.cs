using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Forms;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class StructuredImportTests
{
    [STATestMethod]
    public void RepeatedImports_ReusesFullDirectoryIdentityEvenAfterRenameFileChangesOrEmptyingCollection()
    {
        var initial = WindowMain.CollectionViews.ToHashSet();
        string directory = Path.Combine(Path.GetTempPath(), "Imports-" + Guid.NewGuid().ToString("N"), "Drums");
        using var first = Audio("One"); using var second = Audio("Two"); using var third = Audio("Three");
        try
        {
            WindowMain.PlaceStructuredImports([(Path.Combine(directory, "one.wav"), first)]);
            AudioCollectionView collection = WindowMain.CollectionViews.Single(view => !initial.Contains(view));
            Assert.AreEqual("Drums", collection.Text);
            Assert.AreEqual(Path.GetFullPath(directory), collection.ImportDirectoryPath);
            collection.Rename("My custom name"); first.FilePath = "different path";
            WindowMain.PlaceStructuredImports([(Path.Combine(directory.ToUpperInvariant(), "two.wav"), second)]);
            Assert.AreEqual(1, WindowMain.CollectionViews.Count(view => !initial.Contains(view)));
            CollectionAssert.AreEqual(new[] { first, second }, collection.AudioC.Audios.ToArray());
            Assert.AreEqual("My custom name", collection.Text);
            collection.AudioC.Audios.Clear();
            WindowMain.PlaceStructuredImports([(Path.Combine(directory, ".", "three.wav"), third)]);
            Assert.AreSame(third, collection.AudioC.Audios.Single());
            Assert.AreEqual(WindowMain.GetCollectionNumber(collection), WindowMain.AudioCollectionTags[third.Id]);
        }
        finally { foreach (var view in WindowMain.CollectionViews.Where(view => !initial.Contains(view)).ToArray()) view.Close(); }
    }

    [STATestMethod]
    public void SameFolderNamesInDifferentParents_StaySeparateAndClosedTargetsAreNotReused()
    {
        var initial = WindowMain.CollectionViews.ToHashSet();
        string parent = Path.Combine(Path.GetTempPath(), "Imports-" + Guid.NewGuid().ToString("N"));
        using var first = Audio("One"); using var second = Audio("Two"); using var third = Audio("Three");
        try
        {
            string one = Path.Combine(parent, "A", "Drums", "one.wav"), two = Path.Combine(parent, "B", "Drums", "two.wav");
            WindowMain.PlaceStructuredImports([(one, first), (two, second)]);
            var created = WindowMain.CollectionViews.Where(view => !initial.Contains(view)).ToArray(); Assert.AreEqual(2, created.Length);
            var target = created.Single(view => view.AudioC.Audios.Contains(first)); target.Close();
            WindowMain.PlaceStructuredImports([(one, third)]);
            Assert.IsFalse(target.CanAppendImportedAudio);
            Assert.IsTrue(WindowMain.CollectionViews.Any(view => !initial.Contains(view) && view != target && view.AudioC.Audios.Contains(third)));
        }
        finally { foreach (var view in WindowMain.CollectionViews.Where(view => !initial.Contains(view)).ToArray()) view.Close(); }
    }
    private static AudioObj Audio(string name) => new() { Name = name, Data = [0.1f, 0.2f], Channels = 1, SampleRate = 8000, Length = 2 };
}
