using System.ComponentModel;
using ModularAudience.Forms.Theming;
using ModularAudience.Forms.Updates;

namespace ModularAudience.Forms;

public partial class WindowMain
{
    private ThemeEditor? themeEditor;
    private void InitializeThemeMenu()
    {
        var menu = this.contextMenuStrip_main;
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

    private void contextMenuStrip_main_Opening(object? sender, CancelEventArgs e)
    {
        try
        {
            RefreshUpdateChecks(this.toolStripMenuItem_checkForUpdates, new ReleaseUpdateSettingsStore());
        }
        catch (Exception error) when (UpdateDiagnostics.IsExpected(error))
        {
            this.toolStripMenuItem_checkForUpdates.Enabled = false;
            this.toolStripMenuItem_checkForUpdates.Checked = false;
            this.toolStripMenuItem_checkForUpdates.Text = "Update Check Settings Unavailable";
            UpdateDiagnostics.ShowError(this, "Application update", "The update preference could not be read.", error);
        }
    }

    private void toolStripMenuItem_checkForUpdates_Click(object? sender, EventArgs e)
    {
        try
        {
            ToggleUpdateChecks(this.toolStripMenuItem_checkForUpdates, new ReleaseUpdateSettingsStore());
        }
        catch (Exception error) when (UpdateDiagnostics.IsExpected(error))
        {
            UpdateDiagnostics.ShowError(this, "Application update", "The update preference could not be saved.", error);
        }
    }

    internal static void ToggleUpdateChecks(ToolStripMenuItem item, ReleaseUpdateSettingsStore settingsStore)
    {
        bool enabled = !item.Checked;
        settingsStore.SetAskToUpdate(enabled);
        SetUpdateMenuState(item, enabled);
    }

    internal static void RefreshUpdateChecks(ToolStripMenuItem item, ReleaseUpdateSettingsStore settingsStore)
    {
        bool enabled = settingsStore.LoadAndRecordRunningRelease(null).AskToUpdate;
        item.Enabled = true;
        SetUpdateMenuState(item, enabled);
    }

    private static void SetUpdateMenuState(ToolStripMenuItem item, bool enabled)
    {
        item.Checked = enabled;
        item.Text = $"Check for Updates at Startup: {(enabled ? "On" : "Off")}";
    }

    private sealed record SavedThemeItem(string Path)
    {
        public override string ToString() => System.IO.Path.GetFileNameWithoutExtension(Path);
    }
}
