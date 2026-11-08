using System.Reflection;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Forms;

namespace ModularAudience.Audio.Tests;

[TestClass]
[DoNotParallelize]
public sealed class CoOpCollectionViewRegressionTests
{
    [STATestMethod]
    public void BackgroundContentEdit_DoesNotAccessBoundControlsFromWorkerThread()
    {
        bool previousCheck = Control.CheckForIllegalCrossThreadCalls;
        Control.CheckForIllegalCrossThreadCalls = true;
        try
        {
            using var scope = new AudioTestScope();
            AudioObj audio = scope.Create([0.1f, 0.2f, 0.3f], 8000);
            using var collection = new AudioCollection();
            collection.Audios.Add(audio);
            using var view = new AudioCollectionView(collection, "Background edit regression");
            try
            {
                Application.DoEvents();
                Task.Run(() => audio.Name = "Edited on worker thread").GetAwaiter().GetResult();
                Application.DoEvents();

                var list = (ListBox)typeof(AudioCollectionView)
                    .GetField("listBox_audios", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(view)!;
                Assert.AreEqual(1, list.Items.Count);
                Assert.AreEqual("Edited on worker thread", list.GetItemText(list.Items[0]));
            }
            finally
            {
                view.Close();
            }
        }
        finally
        {
            Control.CheckForIllegalCrossThreadCalls = previousCheck;
        }
    }

    [STATestMethod]
    public void ClosingBorrowedCollectionView_DoesNotDisposeItsAudioCollection()
    {
        using var scope = new AudioTestScope();
        AudioObj audio = scope.Create([0.1f, 0.2f, 0.3f], 8000);
        using var collection = new AudioCollection();
        collection.Audios.Add(audio);
        using var view = new AudioCollectionView(collection, "Borrowed collection regression");

        view.Close();
        Application.DoEvents();

        Assert.AreSame(audio, collection.Audios.Single());
        CollectionAssert.AreEqual(new[] { 0.1f, 0.2f, 0.3f }, audio.Data);
    }
}
