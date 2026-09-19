using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ModularAudience.Audio;

namespace ModularAudience.Audio.Tests
{
    [assembly: DoNotParallelize]

    [TestClass]
    internal sealed class AudioObjCoreTests
    {
        [TestMethod]
        public void Test_AudioObj_Initialization()
        {
            using (var scope = new AudioTestScope())
            {
                var audio = scope.Create(new float[] { 0.1f, 0.2f, 0.3f }, 44100, 1);

                Assert.IsNotNull(audio.Id);
                Assert.AreNotEqual(Guid.Empty, audio.Id);
                Assert.AreEqual(44100, audio.SampleRate);
                Assert.AreEqual(1, audio.Channels);
                Assert.AreEqual(3, audio.Length);
                Assert.AreEqual(3, audio.Data.Length);
                Assert.AreEqual(1.0f, audio.Volume);
            }
        }

        [TestMethod]
        public void Test_AudioObj_Clone()
        {
            using (var scope = new AudioTestScope())
            {
                var original = scope.Create(new float[] { 0.1f, 0.2f, 0.3f }, 44100, 1);
                original.Name = "Original";
                original.Volume = 0.5f;

                var clone = original.Clone();

                Assert.AreEqual(original.Id, clone.Id);
                Assert.AreEqual(original.Name, clone.Name);
                Assert.AreEqual(original.Volume, clone.Volume);
                Assert.AreEqual(original.SampleRate, clone.SampleRate);
                Assert.AreEqual(original.Data.Length, clone.Data.Length);
                Assert.AreNotSame(original.Data, clone.Data); // Data should be cloned
                Assert.AreNotSame(original, clone);
            }
        }

        [TestMethod]
        public void Test_AudioObj_UndoRedo()
        {
            using (var scope = new AudioTestScope())
            {
                var audio = scope.Create(new float[] { 0.1f, 0.2f, 0.3f }, 44100, 1);
                audio.Name = "Initial";
                audio.Volume = 1.0f;

                // Create first undo step
                audio.CreateUndoStep();
                audio.Name = "Changed";
                audio.Volume = 0.5f;

                Assert.IsTrue(audio.CanUndo);
                Assert.IsFalse(audio.CanRedo);

                // Undo
                bool undoResult = audio.Undo();
                Assert.IsTrue(undoResult);
                Assert.AreEqual("Initial", audio.Name);
                Assert.AreEqual(1.0f, audio.Volume);
                Assert.IsTrue(audio.CanRedo);

                // Redo
                bool redoResult = audio.Redo();
                Assert.IsTrue(redoResult);
                Assert.AreEqual("Changed", audio.Name);
                Assert.AreEqual(0.5f, audio.Volume);
                Assert.IsFalse(audio.CanRedo);
            }
        }

        [TestMethod]
        public void Test_AudioObj_Metrics()
        {
            using (var scope = new AudioTestScope())
            {
                var audio = scope.Create(new float[] { 0.1f }, 44100, 1);
                
                audio["TestMetric"] = 42.0;
                audio["testmetric"] = 10.0; // Case insensitivity check

                Assert.AreEqual(42.0, audio["TestMetric"]);
                Assert.AreEqual(10.0, audio["testmetric"]);
                Assert.AreEqual(42.0, audio["TESTMETRIC"]);
                
                Assert.AreEqual(0.0, audio["NonExistent"]);
            }
        }

        [TestMethod]
        public void Test_AudioObj_ReplaceWith()
        {
            using (var scope = new AudioTestScope())
            {
                var original = scope.Create(new float[] { 0.1f, 0.2f }, 44100, 1);
                original.Name = "Original";
                original.Volume = 1.0f;

                var replacement = scope.Create(new float[] { 0.3f, 0.4f, 0.5f }, 44100, 1);
                replacement.Name = "Replacement";
                replacement.Volume = 0.5f;

                original.ReplaceWith(replacement);

                Assert.AreEqual("Replacement", original.Name);
                Assert.AreEqual(0.5f, original.Volume);
                Assert.AreEqual(3, original.Data.Length);
                Assert.AreEqual(0.3f, original.Data[0]);
            }
        }

        [TestMethod]
        public async Task Test_AudioObj_InsertAudioAtFrameAsync()
        {
            using (var scope = new AudioTestScope())
            {
                var audio = scope.Create(new float[] { 0.1f, 0.1f, 0.1f, 0.1f }, 44100, 1);
                var clip = scope.Create(new float[] { 0.9f, 0.9f }, 44100, 1);

                // Insert at frame 1 (index 1)
                await audio.InsertAudioAtFrameAsync(clip, 1);

                // Original: [0.1, 0.1, 0.1, 0.1]
                // Clip: [0.9, 0.9]
                // Expected: [0.1, 0.9, 0.9, 0.1, 0.1]
                Assert.AreEqual(5, audio.Data.Length);
                Assert.AreEqual(0.1f, audio.Data[0]);
                Assert.AreEqual(0.9f, audio.Data[1]);
                Assert.AreEqual(0.9f, audio.Data[2]);
                Assert.AreEqual(0.1f, audio.Data[3]);
                Assert.AreEqual(0.1f, audio.Data[4]);
            }
        }
    }
}