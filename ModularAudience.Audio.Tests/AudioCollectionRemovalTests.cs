using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio;

namespace ModularAudience.Audio.Tests
{
    [TestClass]
    public sealed class AudioCollectionRemovalTests
    {
        [TestMethod]
        public async Task RemoveAsync_ByObjectRemovesTheRequestedClone()
        {
            string workingDirectory = Path.Combine(Path.GetTempPath(), nameof(AudioCollectionRemovalTests), Guid.NewGuid().ToString("N"));
            using AudioCollection collection = new(workingDirectory);
            AudioObj first = new() { Name = "First" };
            AudioObj selected = first.Clone();
            selected.Name = "Selected";
            Assert.AreNotEqual(first.Id, selected.Id);
            collection.Audios.Add(first);
            collection.Audios.Add(selected);

            AudioObj? removed = await collection.RemoveAsync(selected, dispose: false);

            Assert.AreSame(selected, removed);
            Assert.AreEqual(1, collection.Audios.Count);
            Assert.AreSame(first, collection.Audios[0]);
            selected.Dispose();
        }
    }
}