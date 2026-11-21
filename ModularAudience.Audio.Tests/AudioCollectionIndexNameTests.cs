using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio;
using ModularAudience.Forms;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class AudioCollectionIndexNameTests
{
    [TestMethod]
    public void AreAllAudioNamesIndexed_RequiresEveryNonemptySelectionNameToHaveNumericPrefix()
    {
        AudioObj[] allIndexed = [CreateAudio("00 - Alpha"), CreateAudio("01 - Beta")];
        AudioObj[] mixed = [allIndexed[0], CreateAudio("Beta")];

        Assert.IsTrue(AudioCollectionView.AreAllAudioNamesIndexed(allIndexed));
        Assert.IsFalse(AudioCollectionView.AreAllAudioNamesIndexed(mixed));
        Assert.IsFalse(AudioCollectionView.AreAllAudioNamesIndexed([]));
        Assert.IsFalse(AudioCollectionView.AreAllAudioNamesIndexed([CreateAudio("A - Beta")]));
        Assert.IsFalse(AudioCollectionView.AreAllAudioNamesIndexed([CreateAudio("01-Beta")]));
    }

    [TestMethod]
    public void ToggleAddIndexToNames_RemovesExistingPrefixAndReindexesWithoutDuplicatingIt()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ModularAudience-index-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            using AudioCollection collection = new(directory, directory);
            AudioObj first = CreateAudio("00 - Alpha");
            AudioObj second = CreateAudio("01 - Beta");
            collection.Audios.Add(first);
            collection.Audios.Add(second);

            collection.ToggleAddIndexToNames(false);

            Assert.AreEqual("Alpha", first.Name);
            Assert.AreEqual("Alpha", first.OriginalName);
            Assert.AreEqual("Beta", second.Name);
            Assert.AreEqual("Beta", second.OriginalName);

            first.Rename("00 - Alpha");
            second.Rename("01 - Beta");
            collection.ToggleAddIndexToNames(true);

            Assert.AreEqual("0 - Alpha", first.Name);
            Assert.AreEqual("1 - Beta", second.Name);
            Assert.AreEqual("Alpha", first.OriginalName);
            Assert.AreEqual("Beta", second.OriginalName);

            collection.ToggleAddIndexToNames(false);

            Assert.AreEqual("Alpha", first.Name);
            Assert.AreEqual("Beta", second.Name);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static AudioObj CreateAudio(string name) => new()
    {
        Name = name,
        OriginalName = name,
        Data = [0.1f],
        SampleRate = 44100,
        Channels = 1,
        BitDepth = 32,
        Length = 1,
        Duration = TimeSpan.FromSeconds(1d / 44100)
    };
}
