using System.Reflection;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Forms;
using ModularAudience.Forms.Hints;
using ModularAudience.Forms.Modules;

namespace ModularAudience.Audio.Tests
{
    [TestClass]
    public sealed class AudioCollectionViewPreviewTests
    {
        [STATestMethod]
        public void CheckedPreviewShowsStackedSelectionWhenGlobalHintsAreDisabled()
        {
            using AudioTestScope scope = new();
            AudioObj first = scope.Create([0.1f, 0.2f, 0.3f], 8000);
            AudioObj second = scope.Create([0.3f, 0.2f, 0.1f], 8000);
            bool originalHintsEnabled = HintPolicy.Enabled;

            try
            {
                using AudioCollectionView collection = new([first, second], "Preview regression");
                try
                {
                    HintPolicy.SetEnabled(false);
                    GetField<CheckBox>(collection, "checkBox_preview").Checked = true;

                    ListBox list = GetField<ListBox>(collection, "listBox_audios");
                    list.SetSelected(0, true);
                    list.SetSelected(1, true);
                    Application.DoEvents();

                    Rectangle itemBounds = list.GetItemRectangle(0);
                    MouseEventArgs mouseArgs = new(
                        MouseButtons.None,
                        0,
                        itemBounds.Left + 2,
                        itemBounds.Top + 2,
                        0);
                    Invoke(collection, "ListBox_audios_MouseMove_WaveformPreview", list, mouseArgs);
                    Invoke(collection, "WaveformPreviewTimer_Tick", null, EventArgs.Empty);

                    WaveformPreview preview = GetField<WaveformPreview>(collection, "waveformPreviewForm");
                    Assert.IsTrue(preview.Visible, "The checked Preview option should show a waveform even when global hints are disabled.");
                    PictureBox waveform = GetField<PictureBox>(preview, "pictureBox_waveform");
                    Assert.AreEqual(320, waveform.Image!.Height, "The selected waveforms should be stacked in the preview.");
                }
                finally
                {
                    collection.Close();
                }
            }
            finally
            {
                HintPolicy.SetEnabled(originalHintsEnabled);
            }
        }

        private static T GetField<T>(object instance, string name)
            where T : class
            => (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;

        private static void Invoke(AudioCollectionView collection, string methodName, object? sender, EventArgs args)
            => typeof(AudioCollectionView)
                .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(collection, [sender, args]);
    }
}
