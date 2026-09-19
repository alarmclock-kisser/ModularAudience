using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ModularAudience.Audio.Tests
{
    [TestClass]
    public sealed class RollingAudioBufferTests
    {
        [TestMethod]
        public void GetLastReturnsNewestBytesInOrderAfterWrapAround()
        {
            RollingAudioBuffer buffer = new(5);
            buffer.Append([1, 2, 3, 4], 0, 4);
            buffer.Append([5, 6, 7], 0, 3);

            CollectionAssert.AreEqual(new byte[] { 3, 4, 5, 6, 7 }, buffer.GetLast(5));
        }

        [TestMethod]
        public void GetLastClampsToAvailableBytesAndClearWipesBuffer()
        {
            RollingAudioBuffer buffer = new(5);
            buffer.Append([10, 11, 12], 0, 3);

            CollectionAssert.AreEqual(new byte[] { 10, 11, 12 }, buffer.GetLast(5));
            buffer.Clear();

            Assert.AreEqual(0, buffer.Count);
            CollectionAssert.AreEqual(Array.Empty<byte>(), buffer.GetLast(5));
        }
    }
}