using System.Reflection;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Forms;

namespace ModularAudience.Audio.Tests
{
    [TestClass]
    public sealed class AudioCollectionViewSelectionTests
    {
        [STATestMethod]
        public void DragWithoutControlKeepsTheFullSelectionFromMouseDownThroughMouseUp()
        {
            AudioObj[] audios = CreateAudios(out AudioTestScope scope);
            using (scope)
            using (AudioCollectionView collection = new(audios, "Selection drag regression"))
            {
                try
                {
                    ListBox list = GetListBox(collection);
                    SelectAllAndSettle(list);
                    SimulateNativeMouseDownSelection(list, 1);

                    InvokeMouseHandler(collection, "listBox_audios_MouseDown", list, MouseArgsForItem(list, 1));
                    CollectionAssert.AreEquivalent(audios, GetSelectedAudios(list),
                        "MouseDown on a selected row must not collapse a multiselection before drag begins.");

                    SetField(collection, "_dragStarted", true);
                    InvokeMouseHandler(collection, "listBox_audios_MouseUp", list, MouseArgsForItem(list, 1));
                    CollectionAssert.AreEquivalent(audios, GetSelectedAudios(list),
                        "MouseUp after a drag must leave every selected row selected.");
                }
                finally
                {
                    collection.Close();
                }
            }
        }

        [STATestMethod]
        public void ClickWithoutDraggingCollapsesSelectionOnlyOnMouseUp()
        {
            AudioObj[] audios = CreateAudios(out AudioTestScope scope);
            using (scope)
            using (AudioCollectionView collection = new(audios, "Selection click regression"))
            {
                try
                {
                    ListBox list = GetListBox(collection);
                    SelectAllAndSettle(list);
                    SimulateNativeMouseDownSelection(list, 1);

                    InvokeMouseHandler(collection, "listBox_audios_MouseDown", list, MouseArgsForItem(list, 1));
                    CollectionAssert.AreEquivalent(audios, GetSelectedAudios(list),
                        "The original multiselection must remain intact while the mouse is still down.");

                    InvokeMouseHandler(collection, "listBox_audios_MouseUp", list, MouseArgsForItem(list, 1));
                    CollectionAssert.AreEqual(new[] { audios[1] }, GetSelectedAudios(list),
                        "A completed click without dragging must collapse selection on mouse-up.");
                }
                finally
                {
                    collection.Close();
                }
            }
        }

        [STATestMethod]
        public void AddIndexMenuReflectsSelectedNamesWithoutRenamingOnOpen_AndUncheckRemovesPrefixes()
        {
            AudioObj[] audios = CreateAudios(out AudioTestScope scope);
            audios[0].Rename("00 - First");
            audios[1].Rename("01 - Second");
            audios[2].Rename("02 - Third");

            using (scope)
            using (AudioCollectionView collection = new(audios, "Index name regression"))
            {
                try
                {
                    SelectAllAndSettle(GetListBox(collection));

                    collection.UpdateContextMenuState();
                    ToolStripMenuItem addIndex = GetAddIndexMenuItem(collection);

                    Assert.IsTrue(addIndex.Checked);
                    Assert.IsFalse(collection.AudioC.AddIndexToNames);
                    CollectionAssert.AreEqual(
                        new[] { "00 - First", "01 - Second", "02 - Third" },
                        audios.Select(audio => audio.Name).ToArray());

                    addIndex.Checked = false;

                    CollectionAssert.AreEqual(
                        new[] { "First", "Second", "Third" },
                        audios.Select(audio => audio.Name).ToArray());
                }
                finally
                {
                    collection.Close();
                }
            }
        }

        private static AudioObj[] CreateAudios(out AudioTestScope scope)
        {
            scope = new AudioTestScope();
            AudioObj first = scope.Create([0.1f, 0.2f, 0.3f], 8000);
            AudioObj second = scope.Create([0.2f, 0.3f, 0.4f], 8000);
            AudioObj third = scope.Create([0.3f, 0.4f, 0.5f], 8000);
            first.Name = "First";
            second.Name = "Second";
            third.Name = "Third";
            return [first, second, third];
        }

        private static void SelectAllAndSettle(ListBox list)
        {
            for (int index = 0; index < list.Items.Count; index++)
            {
                list.SetSelected(index, true);
            }

            Application.DoEvents();
        }

        private static void SimulateNativeMouseDownSelection(ListBox list, int index)
        {
            list.ClearSelected();
            list.SetSelected(index, true);
        }

        private static MouseEventArgs MouseArgsForItem(ListBox list, int index)
        {
            Rectangle bounds = list.GetItemRectangle(index);
            return new MouseEventArgs(MouseButtons.Left, 1, bounds.Left + 2, bounds.Top + 2, 0);
        }

        private static AudioObj[] GetSelectedAudios(ListBox list)
            => list.SelectedItems.Cast<AudioObj>().ToArray();

        private static ListBox GetListBox(AudioCollectionView collection)
            => (ListBox)typeof(AudioCollectionView).GetField("listBox_audios", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(collection)!;

        private static ToolStripMenuItem GetAddIndexMenuItem(AudioCollectionView collection)
            => (ToolStripMenuItem)typeof(AudioCollectionView).GetField("menuToolStripItem_addIndexToNames", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(collection)!;

        private static void InvokeMouseHandler(AudioCollectionView collection, string name, ListBox list, MouseEventArgs args)
            => typeof(AudioCollectionView).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(collection, [list, args]);

        private static void SetField(AudioCollectionView collection, string name, object value)
            => typeof(AudioCollectionView).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(collection, value);
    }
}