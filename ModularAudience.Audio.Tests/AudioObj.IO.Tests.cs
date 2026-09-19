using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Threading.Tasks;
using ModularAudience.Audio;

namespace ModularAudience.Audio.Tests
{
    [assembly: DoNotParallelize]

    [TestClass]
    internal sealed class AudioObjIOTests
    {
        [TestMethod]
        public void Test_AudioObj_Dispose()
        {
            using (var scope = new AudioTestScope())
            {
                var audio = scope.Create(new float[] { 0.1f, 0.2f }, 44100, 1);
                audio.Dispose();
                
                Assert.IsFalse(audio.Playing);
                Assert.IsFalse(audio.Paused);
                Assert.AreEqual(0, audio.Data.Length);
            }
        }

        [TestMethod]
        public void Test_AudioObj_Dispose_MultipleTimes()
        {
            using (var scope = new AudioTestScope())
            {
                var audio = scope.Create(new float[] { 0.1f, 0.2f }, 44100, 1);
                audio.Dispose();
                audio.Dispose(); // Should not throw
            }
        }

        [TestMethod]
        public void Test_AudioObj_LoadNonExistentFile()
        {
            var audio = new AudioObj();
            audio.FilePath = "non_existent_file.wav";
            bool result = audio.LoadAudioFile();
            Assert.IsFalse(result);
        }
    }
}