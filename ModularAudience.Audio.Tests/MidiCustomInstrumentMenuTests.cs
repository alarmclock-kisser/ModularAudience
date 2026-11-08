using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Midi;
using ModularAudience.Forms;
using ModularAudience.Forms.Modules.Dialogs;

namespace ModularAudience.Audio.Tests;

[TestClass]
[DoNotParallelize]
public sealed class MidiCustomInstrumentMenuTests
{
    [STATestMethod]
    public void CollectionAndSampleMenuSurviveItemClickAndCanReopenUntilWindowDisposal()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        using var sample = new AudioObj { Data = new float[128], Channels = 1, SampleRate = 44100, Name = "Menu sample" };
        using var collection = new AudioCollectionView([sample], "MIDI menu regression");
        var midi = MidiFileData.CreateGenerated([new MidiTrackData { Index = 0 }], 960, 120);
        using var window = new MidiWindow(null, midi);
        object Field(string name) => typeof(MidiWindow).GetField(name, flags)!.GetValue(window)!;
        var instruments = (ComboBox)Field("comboBox_instrument");
        var menu = (ContextMenuStrip)Field("customInstrumentMenu");
        try
        {
            window.Show();
            foreach (int instrument in new[] { 7, 7, 6, 6 })
            {
                instruments.SelectedIndex = instrument;
                typeof(MidiWindow).GetMethod("button_customInstrument_Click", flags)!.Invoke(window, [window, EventArgs.Empty]);
                var item = menu.Items.Cast<ToolStripMenuItem>().Single(i => i.Text?.Contains(instrument == 7 ? collection.Text : sample.Name) == true);
                item.PerformClick();
                // Exercise the same item-click dismissal sequence as a real mouse selection.
                typeof(ToolStripDropDown).GetMethod("OnItemClicked", flags)!.Invoke(menu, [new ToolStripItemClickedEventArgs(item)]);
                Assert.IsFalse(menu.IsDisposed);
                if (instrument == 7) Assert.AreSame(collection, Field("customCollection"));
                else Assert.IsNotNull(Field("customSample"));
                menu.Close();
            }
            window.Dispose();
            Assert.IsTrue(menu.IsDisposed);
        }
        finally
        {
            var views = (System.Collections.IList)typeof(WindowMain).GetField("CollectionViews", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            views.Remove(collection);
        }
    }
}
