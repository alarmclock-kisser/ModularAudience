using ModularAudience.Forms.Theming;

namespace ModularAudience.Forms;

public partial class WindowMain
{
    private ThemeEditor? themeEditor;
    private void InitializeThemeMenu()
    {
        var menu = new ContextMenuStrip(this.components);
        menu.Items.Add("Theme Editor…", null, (_, _) =>
        {
            if (this.themeEditor == null || this.themeEditor.IsDisposed) this.themeEditor = new ThemeEditor();
            this.themeEditor.Show(); this.themeEditor.Activate();
        });
        menu.Items.Add(new ToolStripLabel("Load Theme"));
        var themes = new ToolStripComboBox { Name = "themeSelection", DropDownStyle = ComboBoxStyle.DropDownList, AutoSize = false, Width = 270 };
        bool refreshing = false;
        void RefreshThemes()
        {
            refreshing = true;
            try
            {
                themes.Items.Clear(); themes.Items.Add("No theme — original Forms colors");
                foreach (string path in ThemeFiles.SavedThemes()) themes.Items.Add(new SavedThemeItem(path));
                themes.SelectedIndex = ThemeManager.Current == null ? 0 : -1;
            }
            finally { refreshing = false; }
        }
        menu.Opening += (_, _) => RefreshThemes();
        themes.SelectedIndexChanged += (_, _) =>
        {
            if (refreshing) return;
            try
            {
                ThemeManager.SetTheme(themes.SelectedItem is SavedThemeItem item ? ThemeFiles.Load(item.Path) : null, persist: true);
                menu.Close();
            }
            catch (Exception error) { MessageBox.Show(this, error.Message, "Unable to load theme", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        menu.Items.Add(themes);
        menu.Items.Add("Import Theme JSON…", null, (_, _) =>
        {
            try
            {
                using var dialog = new OpenFileDialog { Filter = "Color themes (*.json)|*.json", InitialDirectory = ThemeFiles.SettingsDirectory };
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                var theme = ThemeFiles.Load(dialog.FileName);
                string destination = Path.Combine(ThemeFiles.SettingsDirectory, Path.GetFileName(dialog.FileName));
                ThemeFiles.Save(destination, theme);
                ThemeManager.SetTheme(theme, persist: true);
            }
            catch (Exception error) { MessageBox.Show(this, error.Message, "Unable to import theme", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        });
        menu.Items.Add("Restore Original Colors", null, (_, _) =>
        {
            try { ThemeManager.SetTheme(null, persist: true); }
            catch (Exception error) { MessageBox.Show(this, error.Message, "Unable to reset theme", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        });
        // Show only for the form background, so existing child-control menus keep their behavior.
        this.MouseUp += (_, e) => { if (e.Button == MouseButtons.Right) { ThemeManager.Apply(menu); menu.Show(this, e.Location); } };
    }
    private sealed record SavedThemeItem(string Path)
    {
        public override string ToString() => System.IO.Path.GetFileNameWithoutExtension(Path);
    }
}
