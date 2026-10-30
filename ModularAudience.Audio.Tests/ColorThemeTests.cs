using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Forms.Theming;
using System.Drawing;
using System.Reflection;

namespace ModularAudience.Audio.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ColorThemeTests
{
    [TestCleanup]
    public void Reset() => ThemeManager.SetTheme(null);

    [STATestMethod]
    public void RulesOverrideTypesAndIndividualControlsAndResetRestoresOriginals()
    {
        using var form = new Form { Name = "testForm", BackColor = Color.Beige };
        var first = new Button { Name = "first", BackColor = Color.Coral, ForeColor = Color.Navy, UseVisualStyleBackColor = true };
        var second = new Button { Name = "second" };
        var input = new TextBox { Name = "input" };
        form.Controls.AddRange([first, second, input]);
        ThemeManager.Apply(form);
        var theme = ColorTheme.Dark();
        theme.Rules.Add(new() { ControlType = "ButtonBase", Colors = new() { ["ForeColor"] = "#123456" } });
        theme.Rules.Add(new() { FormType = "Form", ElementPath = "testForm/first", Colors = new() { ["BackColor"] = "#456789" } });
        ThemeManager.SetTheme(theme); ThemeManager.Apply(form);
        Assert.AreEqual(Color.FromArgb(0x20, 0x21, 0x24).ToArgb(), form.BackColor.ToArgb());
        Assert.AreEqual(Color.FromArgb(0x45, 0x67, 0x89).ToArgb(), first.BackColor.ToArgb());
        Assert.AreEqual(Color.FromArgb(0x12, 0x34, 0x56).ToArgb(), second.ForeColor.ToArgb());
        Assert.AreEqual(ThemeFiles.ParseColor(theme.Palette.InputBackground!).ToArgb(), input.BackColor.ToArgb());
        Assert.IsFalse(first.UseVisualStyleBackColor);
        ThemeManager.SetTheme(null); ThemeManager.Apply(form);
        Assert.AreEqual(Color.Beige, form.BackColor);
        Assert.AreEqual(Color.Coral, first.BackColor);
        Assert.AreEqual(Color.Navy, first.ForeColor);
        Assert.IsTrue(first.UseVisualStyleBackColor);
    }

    [STATestMethod]
    public void ControlsAddedAfterThemeAreAutomaticallyApplied()
    {
        using var form = new Form { Name = "dynamic" };
        ThemeManager.SetTheme(ColorTheme.Light()); ThemeManager.Apply(form);
        var panel = new Panel(); form.Controls.Add(panel);
        var text = new TextBox(); panel.Controls.Add(text);
        Assert.AreEqual(Color.White.ToArgb(), text.BackColor.ToArgb());
        Assert.AreEqual(ThemeFiles.ParseColor("#202124").ToArgb(), text.ForeColor.ToArgb());
        ThemeManager.SetTheme(null); ThemeManager.Apply(form);
        Assert.AreEqual(SystemColors.Control.ToArgb(), panel.BackColor.ToArgb());
        Assert.AreEqual(SystemColors.Window.ToArgb(), text.BackColor.ToArgb());
    }

    [STATestMethod]
    public void InheritedColorsAndNativeDrawingModesRestoreAfterThemeSwitches()
    {
        using var form = new Form { BackColor = Color.Beige, ForeColor = Color.Navy };
        var panel = new Panel(); var label = new Label(); panel.Controls.Add(label); form.Controls.Add(panel);
        var list = new ListBox(); list.Items.Add("Item"); form.Controls.Add(list);
        var combo = new ComboBox(); combo.Items.Add("Choice"); form.Controls.Add(combo);
        ThemeManager.Apply(form);
        ThemeManager.SetTheme(ColorTheme.Dark()); ThemeManager.Apply(form);
        Assert.AreEqual(DrawMode.OwnerDrawFixed, list.DrawMode);
        Assert.AreEqual(DrawMode.OwnerDrawFixed, combo.DrawMode);
        ThemeManager.SetTheme(ColorTheme.Light()); ThemeManager.Apply(form);
        ThemeManager.SetTheme(null); ThemeManager.Apply(form);
        Assert.AreEqual(Color.Beige.ToArgb(), label.BackColor.ToArgb());
        Assert.AreEqual(Color.Navy.ToArgb(), label.ForeColor.ToArgb());
        form.BackColor = Color.Coral;
        Assert.AreEqual(Color.Coral.ToArgb(), panel.BackColor.ToArgb());
        Assert.AreEqual(DrawMode.Normal, list.DrawMode);
        Assert.AreEqual(DrawMode.Normal, combo.DrawMode);
    }

    [STATestMethod]
    public void ExistingAndFutureOpenWindowsReceiveLiveThemeChanges()
    {
        using var first = new Form { ShowInTaskbar = false, Opacity = 0 };
        first.Show();
        ThemeManager.DiscoverForms(); ThemeManager.SetTheme(ColorTheme.Dark());
        Assert.AreEqual(ThemeFiles.ParseColor("#202124").ToArgb(), first.BackColor.ToArgb());
        using var later = new Form { ShowInTaskbar = false, Opacity = 0 };
        later.Show(); ThemeManager.DiscoverForms();
        Assert.AreEqual(first.BackColor, later.BackColor);
        ThemeManager.SetTheme(null);
        Assert.AreEqual(SystemColors.Control.ToArgb(), later.BackColor.ToArgb());
    }

    [STATestMethod]
    public void MenusAndDataGridsUseThemeColorsAndRestoreRenderers()
    {
        using var form = new Form();
        using var menu = new ContextMenuStrip();
        var item = new ToolStripMenuItem("Nested"); item.DropDownItems.Add("Child"); menu.Items.Add(item);
        var originalRenderer = menu.Renderer;
        var grid = new DataGridView { Name = "grid", ContextMenuStrip = menu };
        grid.Columns.Add("first", "First"); form.Controls.Add(grid);
        ThemeManager.Apply(form); ThemeManager.SetTheme(ColorTheme.Dark()); ThemeManager.Apply(form);
        Assert.AreEqual(ThemeFiles.ParseColor("#375A88").ToArgb(), grid.DefaultCellStyle.SelectionBackColor.ToArgb());
        Assert.AreEqual(ThemeFiles.ParseColor("#E8EAED").ToArgb(), item.ForeColor.ToArgb());
        Assert.IsFalse(grid.EnableHeadersVisualStyles);
        menu.Items.Add(new ToolStripMenuItem("Later"));
        Assert.AreEqual(item.ForeColor, menu.Items[1].ForeColor);
        ThemeManager.SetTheme(null); ThemeManager.Apply(form);
        Assert.AreSame(originalRenderer, menu.Renderer);
        Assert.IsTrue(grid.EnableHeadersVisualStyles);
    }

    [TestMethod]
    public void JsonRoundTripPreservesSelectorsColorsAndRejectsBadImports()
    {
        string path = Path.Combine(Path.GetTempPath(), "theme-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var theme = ColorTheme.Dark();
            theme.Rules.Add(new() { ElementPath = "*/button_*", Colors = new() { ["ForeColor"] = "#FFEEDD" } });
            ThemeFiles.Save(path, theme);
            var loaded = ThemeFiles.Load(path);
            Assert.AreEqual("*/button_*", loaded.Rules[0].ElementPath);
            Assert.AreEqual("#FFEEDD", loaded.Rules[0].Colors["ForeColor"]);
            Assert.Throws<FormatException>(() => ThemeFiles.Deserialize("{\"Palette\":{\"Background\":\"invalid-color\"}}"));
            Assert.AreEqual(Color.FromArgb(128, 10, 20, 30).ToArgb(), ThemeFiles.ParseColor("#800A141E").ToArgb());
            Assert.IsTrue(ThemeManager.Matches("*/button_?", "Window/panel/button_1"));
        }
        finally { File.Delete(path); }
    }

    [STATestMethod]
    public void EditorDefaultsToNoThemeAndExposesPickerHexAndSavedThemeControls()
    {
        ThemeManager.SetTheme(null);
        using var editor = new ThemeEditor();
        Assert.IsNull(ThemeManager.Current);
        object Field(string name) => typeof(ThemeEditor).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;
        var colors = (DataGridView)Field("colors");
        Assert.AreEqual(3, colors.Columns.Count);
        Assert.AreEqual("Value", colors.Columns[1].Name);
        Assert.AreEqual("Swatch", colors.Columns[2].Name);
        Assert.AreEqual("No theme — original Forms colors", ((ComboBox)Field("savedThemes")).SelectedItem);
        Assert.IsTrue(ThemeFiles.SettingsDirectory.EndsWith(Path.Combine("ModularAudience", "Themes")));
    }

    [STATestMethod]
    public void HexEditingUpdatesLiveWindowsAndPreviewSwatch()
    {
        using var form = new Form { ShowInTaskbar = false, Opacity = 0 };
        form.Show(); ThemeManager.DiscoverForms();
        using var editor = new ThemeEditor();
        var colors = (DataGridView)typeof(ThemeEditor).GetField("colors", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;
        var row = colors.Rows.Cast<DataGridViewRow>().Single(r => Equals(r.Cells[0].Value, "Background"));
        row.Cells[1].Value = "#123456";
        typeof(ThemeEditor).GetMethod("StoreColors", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(editor, null);
        Assert.AreEqual(ThemeFiles.ParseColor("#123456").ToArgb(), form.BackColor.ToArgb());
        Assert.AreEqual(form.BackColor.ToArgb(), row.Cells[2].Style.BackColor.ToArgb());
        row.Cells[1].Value = "";
        typeof(ThemeEditor).GetMethod("StoreColors", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(editor, null);
        Assert.IsNull(ThemeManager.Current);
        Assert.AreEqual(SystemColors.Control.ToArgb(), form.BackColor.ToArgb());
    }
}
