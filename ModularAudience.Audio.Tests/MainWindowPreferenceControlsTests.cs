using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Forms;
using ModularAudience.Forms.Settings;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class MainWindowPreferenceControlsTests
{
    [STATestMethod]
    public void ExportControls_RestoreSavedPairWithoutPublishingUserChange()
    {
        using var format = new ComboBox();
        using var bits = new ComboBox();
        using var controls = new ExportOptionsController(format, bits, ".wav", 16);
        int changes = 0;
        controls.Changed += (_, _) => changes++;

        controls.Apply(".mp3", 320);

        Assert.AreEqual(".mp3", format.SelectedItem);
        Assert.AreEqual(320, bits.SelectedItem);
        Assert.AreEqual(0, changes);
    }

    [STATestMethod]
    public void ExportFormatChange_PublishesOnlyOneCoherentFormatAndBitPair()
    {
        using var format = new ComboBox();
        using var bits = new ComboBox();
        using var controls = new ExportOptionsController(format, bits, ".wav", 24);
        var changes = new List<(string, int)>();
        controls.Changed += (_, _) => changes.Add((controls.Format, controls.Bits));

        format.SelectedItem = ".mp3";
        bits.SelectedItem = 320;
        format.SelectedItem = ".wav";

        CollectionAssert.AreEqual(new[] { (".mp3", 192), (".mp3", 320), (".wav", 16) }, changes);
    }

    [STATestMethod]
    public void ControlChanges_AreSavedImmediatelyAndRestoredByAnotherBinding()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ModularAudience-ui-preferences-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ApplicationPreferencesStore(Path.Combine(directory, "application-settings.json"));
            store.Load();
            using var format = new ComboBox();
            using var bits = new ComboBox();
            using var controls = new ExportOptionsController(format, bits, ".wav", 16);
            using var list = new ListBox { IntegralHeight = false };
            using var zoom = new LogListZoomController(list, 8f);
            controls.Changed += (_, _) => store.SetExport(controls.Format, controls.Bits);
            zoom.Changed += (_, _) => store.SetLogFontSize(zoom.Size);

            format.SelectedItem = ".mp3";
            bits.SelectedItem = 256;
            zoom.Zoom(120);

            ApplicationPreferences saved = new ApplicationPreferencesStore(store.FilePath).Load();
            using var restoredFormat = new ComboBox();
            using var restoredBits = new ComboBox();
            using var restored = new ExportOptionsController(restoredFormat, restoredBits, saved.ExportFormat, saved.ExportBits);
            Assert.AreEqual(".mp3", restoredFormat.SelectedItem);
            Assert.AreEqual(256, restoredBits.SelectedItem);
            Assert.AreEqual(9f, saved.LogFontSize);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [STATestMethod]
    public void LogZoom_RestoresAndChangesWithoutResizingListBox()
    {
        using var list = new ListBox { Bounds = new Rectangle(12, 20, 300, 240), IntegralHeight = false, DrawMode = DrawMode.OwnerDrawFixed };
        Rectangle bounds = list.Bounds;
        using var zoom = new LogListZoomController(list, 12f);
        int changes = 0;
        zoom.Changed += (_, _) => changes++;

        zoom.Zoom(120);

        Assert.AreEqual(13f, list.Font.SizeInPoints);
        Assert.AreEqual(bounds, list.Bounds);
        Assert.AreEqual(1, changes);
        zoom.Apply(9f);
        Assert.AreEqual(9f, zoom.Size);
        Assert.AreEqual(1, changes, "Applying persisted settings must not write them again.");
    }

    [STATestMethod]
    public void LogZoom_ClampsAndDoesNotSaveUnchangedBoundary()
    {
        using var list = new ListBox { IntegralHeight = false };
        using var zoom = new LogListZoomController(list, 20f);
        int changes = 0;
        zoom.Changed += (_, _) => changes++;

        zoom.Zoom(120);

        Assert.AreEqual(20f, zoom.Size);
        Assert.AreEqual(0, changes);
    }

    [TestMethod]
    public void NormalBrowse_UsesFolderPickerStartingAtCurrentWorkspace()
    {
        string current = Path.Combine(Path.GetTempPath(), "CurrentWorkspace");
        string selected = Path.Combine(Path.GetTempPath(), "SelectedWorkspace");
        string? initial = null;

        string? result = WorkingDirectoryBrowser.Browse(current, false,
            path => { initial = path; return selected; },
            _ => throw new AssertFailedException("Normal click must not open Explorer."));

        Assert.AreEqual(current, initial);
        Assert.AreEqual(selected, result);
    }

    [TestMethod]
    public void CtrlBrowse_OnlyOpensCurrentWorkspaceAndDoesNotChangeSelection()
    {
        string current = Path.Combine(Path.GetTempPath(), "Audio with spaces");
        string? opened = null;

        string? result = WorkingDirectoryBrowser.Browse(current, true,
            _ => throw new AssertFailedException("Ctrl-click must not open a folder picker."), path => opened = path);

        Assert.IsNull(result);
        Assert.AreEqual(current, opened);
        var start = WorkingDirectoryBrowser.ExplorerStartInfo(current);
        Assert.AreEqual(current, start.FileName);
        Assert.IsTrue(start.UseShellExecute);
        Assert.AreEqual("open", start.Verb);
    }

    [TestMethod]
    public void CancelledFolderSelection_DoesNotProvideANewWorkspace()
    {
        Assert.IsNull(WorkingDirectoryBrowser.Browse(Path.GetTempPath(), false, _ => null,
            _ => throw new AssertFailedException("Cancelled selection must not open Explorer.")));
    }

    [TestMethod]
    public void PlaylistPlayText_UsesTheTriangleCodePointInsteadOfMojibake()
    {
        string text = PlaylistUiText.ButtonText(false);

        Assert.AreEqual("\u25B6 List", text);
        Assert.AreEqual('\u25B6', text[0]);
        Assert.AreEqual("|| List", PlaylistUiText.ButtonText(true));
    }
}
