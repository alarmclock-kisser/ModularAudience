using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Forms.Modules.Dialogs;
using ModularAudience.Ytdlp;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace ModularAudience.Audio.Tests;

[TestClass]
[DoNotParallelize]
public sealed class YoutubeBrowserResponsiveTests
{
    [STATestMethod]
    public void Resize_AllControlsAndColumnsRemainInsideWindow_WithoutOuterScrolling()
    {
        using var window = new YoutubeBrowserWindow(_ => Assert.Fail("No import was requested."));
        _ = window.Handle;
        _ = Field<TabControl>(window, "browsingTabs").Handle;
        var root = Field<TableLayoutPanel>(window, "rootLayout");
        var list = Field<ListView>(window, "resultsList");
        _ = list.Handle;
        YtdlpVideo[] videos =
        [
            new() { Id = "v586fXgj50Y", Title = "Flat search result", Url = "https://www.youtube.com/watch?v=v586fXgj50Y" },
            new() { Id = "bqwhPlX7daw", Title = "Known upload", Url = "https://www.youtube.com/watch?v=bqwhPlX7daw", UploadDate = new(2024, 1, 2) }
        ];
        typeof(YoutubeBrowserWindow).GetMethod("PopulateResults", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [videos]);
        foreach (var item in Field<ToolStripMenuItem[]>(window, "columnMenuItems")) item.Checked = true;
        CreateHandles(window);
        window.WindowState = FormWindowState.Normal;
        foreach (var size in new[] { new Size(1000, 780), new Size(1180, 820), new Size(1500, 1000), new Size(1000, 780) })
        {
            window.Size = size;
            window.PerformLayout();
            Assert.IsTrue(root.Bottom <= root.Parent!.ClientSize.Height,
                $"Root bottom {root.Bottom} exceeds visible client height {root.Parent!.ClientSize.Height} at {size}.");
            foreach (string name in new[] { "queryText", "resultTypeCombo", "resultLimit", "resultsList", "thumbnailCheck", "descriptionText", "metadataButton", "formatCombo", "audioQuality", "saveCheck", "outputDirectory", "folderButton", "cooldownSeconds", "maxDurationMinutes", "maxSizeMiB", "maxParallelDownloadsNumber", "stretchCheck", "stretchConfigButton", "ipVersionCombo", "proxyModeCombo", "proxyText", "downloadButton", "relatedButton", "updateButton", "cancelButton", "activityLog", "statusLabel", "elapsedLabel", "etaLabel" })
            {
                var control = Field<Control>(window, name);
                var location = Point.Empty;
                for (Control? current = control; current is not null && current != window; current = current.Parent)
                    location.Offset(current.Left, current.Top);
                var bounds = new Rectangle(location, control.Size);
                Assert.IsTrue(window.ClientRectangle.Contains(bounds), $"{name}: {bounds} is outside {window.ClientRectangle} at {size}.");
                Assert.IsTrue(control.Width > 10 && control.Height > 10, $"{name} is collapsed at {size}.");
            }
            Assert.IsTrue(list.Columns.Cast<ColumnHeader>().Sum(column => column.Width) <= list.ClientSize.Width,
                "Visible result columns must fit without a horizontal scrollbar.");
            var thumbnail = Field<PictureBox>(window, "thumbnailPicture");
            Assert.IsTrue(thumbnail.Height >= 80, $"Thumbnail is too small to recognize at {size}: {thumbnail.Size}.");
            var waitCheck = Field<CheckBox>(window, "waitForBpmCheck");
            var waitSeconds = Field<NumericUpDown>(window, "cooldownSeconds");
            Assert.AreSame(waitSeconds.Parent, waitCheck.Parent);
            Assert.IsTrue(Math.Abs(waitSeconds.Top - waitCheck.Top) < 10, "BPM checkbox must remain beside Wait (s).");
            Assert.IsTrue(waitCheck.Left >= waitSeconds.Right);
        }
        Assert.IsNull(videos[0].UploadDate, "The flat result really lacks a date; the parser must not invent one.");
        foreach (var narrow in new[] { new Size(800, 600), new Size(600, 400) })
        {
            window.Size = narrow;
            window.PerformLayout();
            Assert.IsFalse(Field<SplitContainer>(window, "mainSplit").Panel2Collapsed, "Normal results/details remain until below 600x400.");
            Assert.IsTrue(list.Width > 200 && list.Height > 100, $"Results table too small at {narrow}: {list.Size}");
            Assert.IsTrue(Field<TabControl>(window, "browsingTabs").TabPages.Cast<TabPage>().Any(tab => tab.Text == "Options / actions"));
            Assert.IsFalse(window.AutoScroll);
        }
        window.Size = window.MinimumSize;
        window.PerformLayout();
        Assert.IsTrue(Field<SplitContainer>(window, "mainSplit").Panel2Collapsed);
        Assert.IsFalse(Field<GroupBox>(window, "optionsGroup").Visible);
        Assert.IsTrue(list.Width > 300 && list.Height >= 100, $"Mini table {list.Size}; root {root.Size}");
        Assert.IsTrue(Field<ComboBox>(window, "resultTypeCombo").Width >= 80);
        Assert.IsTrue(Field<TextBox>(window, "queryText").Width >= 80, "The result filter must leave a usable search field in mini layout.");
        window.Size = new Size(1180, 820);
        window.PerformLayout();
        Assert.IsFalse(Field<SplitContainer>(window, "mainSplit").Panel2Collapsed);
    }

    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;

    private static void CreateHandles(Control parent)
    {
        _ = parent.Handle;
        foreach (Control child in parent.Controls) CreateHandles(child);
    }
}
