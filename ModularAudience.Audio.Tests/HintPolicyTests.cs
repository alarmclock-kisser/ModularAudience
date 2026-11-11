using System.ComponentModel;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Audio.Midi;
using ModularAudience.Forms;
using ModularAudience.Forms.Effects;
using ModularAudience.Forms.Hints;
using ModularAudience.Forms.Modules;
using ModularAudience.Forms.Modules.Dialogs;
using ModularAudience.Forms.Settings;

namespace ModularAudience.Audio.Tests;

[TestClass]
[DoNotParallelize]
public sealed class HintPolicyTests
{
    private bool previous;
    [TestInitialize] public void Initialize() { previous = HintPolicy.Enabled; HintPolicy.SetEnabled(true); }
    [TestCleanup] public void Cleanup() => HintPolicy.SetEnabled(previous);

    [STATestMethod]
    public void LiveAndFutureHintsToggleWithoutLosingTheirTextAndParameterHelpStaysActive()
    {
        using var root = new Form();
        using var surface = new PictureBox();
        using var flow = new FlowLayoutPanel();
        using var label = new Label { Text = "Frequency" };
        using var number = new NumericUpDown { Minimum = 20, Maximum = 20000, Increment = 5 };
        root.Controls.AddRange([surface, flow]); flow.Controls.AddRange([label, number]);
        using var hints = new AppToolTip();
        using var parameters = new AppToolTip { IsParameterHelp = true };
        hints.SetToolTip(surface, "Drag to select; Ctrl+wheel zooms.");
        parameters.SetToolTip(number, "Oscillator frequency in hertz.");

        HintPolicy.SetEnabled(false);
        Assert.IsFalse(hints.Active);
        Assert.IsTrue(parameters.Active);
        StringAssert.Contains(parameters.GetToolTip(number), "20 to 20000");
        Assert.AreEqual(parameters.GetToolTip(number), parameters.GetToolTip(label));
        using var createdWhileOff = new AppToolTip();
        Assert.IsFalse(createdWhileOff.Active);
        createdWhileOff.SetToolTip(surface, "Created after the global switch was turned off.");
        int popups = 0;
        hints.Popup += (_, _) => popups++;
        hints.Show("Should not appear", surface);
        hints.Show("Should not appear", surface, 1000);
        hints.Show("Should not appear", surface, Point.Empty);
        hints.Show("Should not appear", surface, Point.Empty, 1000);
        hints.Show("Should not appear", surface, 1, 1);
        hints.Show("Should not appear", surface, 1, 1, 1000);
        Assert.AreEqual(0, popups);

        HintPolicy.SetEnabled(true);
        Assert.IsTrue(hints.Active);
        Assert.IsTrue(createdWhileOff.Active);
        Assert.AreEqual("Drag to select; Ctrl+wheel zooms.", hints.GetToolTip(surface));
    }

    [STATestMethod]
    public void EveryTooltipComponentInTheFormsAssemblyUsesTheCentralPolicy()
    {
        var fields = typeof(WindowMain).Assembly.GetTypes()
            .SelectMany(type => type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(field => typeof(System.Windows.Forms.ToolTip).IsAssignableFrom(field.FieldType)).ToArray();
        Assert.IsTrue(fields.Length >= 20, "The audit must include the editor, settings and main-window tooltip components.");
        foreach (FieldInfo field in fields)
            Assert.AreEqual(typeof(AppToolTip), field.FieldType, $"{field.DeclaringType?.FullName}.{field.Name} bypasses the global hint policy.");
    }

    [STATestMethod]
    public void FullLogMessageTooltipsRemainActiveAndUnchangedWithGlobalHintsDisabled()
    {
        using var root = new Form();
        using var log = new ListBox();
        root.Controls.Add(log);
        using var diagnostics = new AppToolTip { AlwaysAvailable = true };
        const string message = "Detailed diagnostic message: a clipped row must still expose the complete original text.";
        diagnostics.SetToolTip(log, message);
        HintPolicy.SetEnabled(false);
        Assert.IsTrue(diagnostics.Active);
        Assert.IsFalse(diagnostics.IsParameterHelp, "Log messages are a separate exception, not parameter descriptions.");
        Assert.AreEqual(message, diagnostics.GetToolTip(log));
        HintPolicy.SetEnabled(true);
        HintPolicy.SetEnabled(false);
        Assert.IsTrue(diagnostics.Active);
        Assert.AreEqual(message, diagnostics.GetToolTip(log));
    }

    [STATestMethod]
    public void NativeListsTabsTreesAndGridsRespectTheSwitchAndKeepTheirOriginalOptOut()
    {
        using var root = new Form();
        var list = new ListView { ShowItemToolTips = true };
        var noHints = new ListView { ShowItemToolTips = false };
        var tabs = new TabControl { ShowToolTips = true };
        var tree = new TreeView { ShowNodeToolTips = true };
        var grid = new DataGridView { ShowCellToolTips = true };
        root.Controls.AddRange([list, noHints, tabs, tree, grid]);
        HintPolicy.Watch(root);
        HintPolicy.SetEnabled(false);
        Assert.IsFalse(list.ShowItemToolTips);
        Assert.IsFalse(tabs.ShowToolTips);
        Assert.IsFalse(tree.ShowNodeToolTips);
        Assert.IsFalse(grid.ShowCellToolTips);
        var late = new ListView { ShowItemToolTips = true };
        root.Controls.Add(late);
        Assert.IsFalse(late.ShowItemToolTips, "Dynamically added controls must inherit the current policy immediately.");
        HintPolicy.SetEnabled(true);
        Assert.IsTrue(list.ShowItemToolTips);
        Assert.IsTrue(late.ShowItemToolTips);
        Assert.IsFalse(noHints.ShowItemToolTips, "Enabling global hints must not invent native tooltips that were deliberately off.");
        Assert.IsTrue(grid.ShowCellToolTips);
    }

    [STATestMethod]
    public void DynamicMenusAndSubmenusAreCoveredButParameterMenusRemainAvailable()
    {
        HintPolicy.SetEnabled(false);
        using var menu = new AppContextMenuStrip { ShowItemToolTips = true };
        var nested = new ToolStripMenuItem("Nested") { ToolTipText = "Optional action hint" };
        nested.DropDownItems.Add(new ToolStripMenuItem("Child") { ToolTipText = "Another hint" });
        menu.Items.Add(nested);
        Open(menu);
        Assert.IsFalse(menu.ShowItemToolTips, "An object initializer must not bypass the policy at menu opening.");
        OpenDropdown(nested);
        Assert.IsFalse(nested.DropDown.ShowItemToolTips);
        using var parameters = new AppContextMenuStrip { IsParameterHelp = true };
        var setting = new ToolStripMenuItem("Temperature") { ToolTipText = "Controls generation randomness." };
        setting.DropDownItems.Add(new ToolStripTextBox { ToolTipText = "Numeric generation parameter." });
        parameters.Items.Add(setting);
        Open(parameters);
        OpenDropdown(setting);
        Assert.IsTrue(parameters.ShowItemToolTips);
        Assert.IsTrue(setting.DropDown.ShowItemToolTips);
        HintPolicy.SetEnabled(true);
        Assert.IsTrue(menu.ShowItemToolTips);
        Assert.IsTrue(nested.DropDown.ShowItemToolTips);
    }

    [STATestMethod]
    public void ParameterDialogCellExplanationsRemainVisibleWhileInteractionListsAreOff()
    {
        HintPolicy.SetEnabled(false);
        using var options = new TestOptionsDialog();
        var grid = new DataGridView { ShowCellToolTips = true };
        options.Controls.Add(grid);
        HintPolicy.Watch(options);
        Assert.IsTrue(grid.ShowCellToolTips);
        HintPolicy.SetEnabled(false);
        Assert.IsTrue(grid.ShowCellToolTips);
    }

    [STATestMethod]
    public void WaveformAndThumbnailPopupWindowsCloseImmediatelyAndCannotReappearWhileOff()
    {
        using var preview = new WaveformPreview();
        preview.ShowWaveform(new Bitmap(32, 24), new Point(50, 50));
        Assert.IsTrue(preview.Visible);
        HintPolicy.SetEnabled(false);
        Assert.IsFalse(preview.Visible);
        preview.ShowWaveform(new Bitmap(32, 24), new Point(50, 50));
        Assert.IsFalse(preview.Visible);
        preview.ShowConcatenatedWaveforms([new Bitmap(32, 24), new Bitmap(32, 24)], new Point(50, 50));
        Assert.IsFalse(preview.Visible);
        HintPolicy.SetEnabled(true);
        preview.ShowWaveform(new Bitmap(32, 24), new Point(50, 50));
        Assert.IsTrue(preview.Visible);
        preview.ClearImage();
    }

    [STATestMethod]
    public void RealMidiLoopMixAndModellerComponentsUseTheGlobalPolicyAndItsParameterException()
    {
        HintPolicy.SetEnabled(false);
        using var midi = new MidiWindow(null, MidiFileData.CreateGenerated([new MidiTrackData { Index = 0 }]));
        using var loops = new LoopControl();
        using var mix = new MixTrackView();
        using var modeller = new WaveformModeller();
        using var tuning = new AutoTuneParameterEditor();
        foreach (var (owner, field) in new (object, string)[]
        { (midi, "toolTip_midi"), (loops, "toolTip_playlistTracks"), (mix, "interactionHints"), (modeller, "interactionHints") })
        {
            var tip = Field<AppToolTip>(owner, field);
            Assert.IsFalse(tip.Active, field);
        }
        Assert.IsTrue(Field<AppToolTip>(modeller, "parameterHelp").Active);
        Assert.IsTrue(Field<AppToolTip>(tuning, "tips").Active);
        StringAssert.Contains(Field<AppToolTip>(midi, "toolTip_midi").GetToolTip(Field<Button>(midi, "button_export")), "ONE .mid file");
        HintPolicy.SetEnabled(true);
        Assert.IsTrue(Field<AppToolTip>(mix, "interactionHints").Active);
        Assert.IsTrue(Field<AppToolTip>(midi, "toolTip_midi").Active);
    }

    [STATestMethod]
    public void CollectionWaveformHintsAndPreviewsFollowTheSwitchWithoutChangingLocalPreviewChoice()
    {
        HintPolicy.SetEnabled(false);
        using var collection = new AudioCollectionView([], "Hint regression");
        try
        {
            var tips = Field<AppToolTip>(collection, "toolTip_audios");
            var preview = Field<CheckBox>(collection, "checkBox_preview");
            preview.Checked = true;
            Assert.IsFalse(tips.Active);
            bool EffectivePreview() => (bool)typeof(AudioCollectionView).GetProperty("ShowPreview", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(collection)!;
            Assert.IsFalse(EffectivePreview());
            HintPolicy.SetEnabled(true);
            Assert.IsTrue(EffectivePreview());
            Assert.IsTrue(tips.Active);
            HintPolicy.SetEnabled(false);
            Assert.IsTrue(preview.Checked, "The global switch must not overwrite the collection's own preference.");
            StringAssert.Contains(tips.GetToolTip(Field<ListBox>(collection, "listBox_audios")), "Double-click");
        }
        finally { collection.Close(); }
    }

    [STATestMethod]
    public void BackgroundMenuIsCheckOnClickAndPersistsOnlyItsOwnJsonField()
    {
        string directory = Path.Combine(Path.GetTempPath(), "opencode", "hints-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "application-settings.json");
            var store = new ApplicationPreferencesStore(path);
            store.SetExport("mp3", 256);
            ApplicationPreferences settings = store.Load();
            using var item = WindowMain.CreateHintMenuItem(store, () => settings.EnableToolTips, save =>
            {
                settings = save();
                HintPolicy.SetEnabled(settings.EnableToolTips);
            });
            Assert.AreEqual("Enable ToolTips (hints)", item.Text);
            Assert.IsTrue(item.CheckOnClick);
            Assert.IsTrue(item.Checked);
            item.PerformClick();
            Assert.IsFalse(item.Checked);
            Assert.IsFalse(HintPolicy.Enabled);
            Assert.IsFalse(new ApplicationPreferencesStore(path).Load().EnableToolTips);
            Assert.AreEqual(256, store.Load().ExportBits);
            Assert.AreEqual(".mp3", store.Load().ExportFormat);
            item.PerformClick();
            Assert.IsTrue(HintPolicy.Enabled);
            Assert.IsTrue(store.Load().EnableToolTips);
            File.WriteAllText(path, "{\"ExportFormat\":\".wav\",\"ExportBits\":24}");
            Assert.IsTrue(store.Load().EnableToolTips, "Legacy JSON without the new field keeps hints enabled.");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void Open(AppContextMenuStrip menu) => typeof(AppContextMenuStrip)
        .GetMethod("OnOpening", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(menu, [new CancelEventArgs()]);
    private static void OpenDropdown(ToolStripMenuItem item)
    {
        item.ShowDropDown();
        item.HideDropDown();
    }
    private static T Field<T>(object instance, string name) => (T)instance.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
    private sealed class TestOptionsDialog : Form;
}
