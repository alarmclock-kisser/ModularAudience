using ModularAudience.Audio;
using ModularAudience.Audio.Processing;

namespace ModularAudience.Forms.Modules.Dialogs;

public partial class YoutubeBrowserWindow
{
    private PlaylistStretchSettings? stretchSettings;
    private readonly int[] columnWidths = [320, 160, 90, 100, 120, 320];

    private void RestoreSettings()
    {
        restoringSettings = true;
        try
        {
            var settings = YoutubeBrowserSettings.Load(out settingsWarning);
            Size = new Size(settings.Width, settings.Height);
            if (settings.Maximized) WindowState = FormWindowState.Maximized;
            mainSplit.SplitterDistance = Math.Clamp(settings.SplitterDistance, mainSplit.Panel1MinSize,
                Math.Max(mainSplit.Panel1MinSize, mainSplit.Width - mainSplit.Panel2MinSize - mainSplit.SplitterWidth));
            thumbnailCheck.Checked = settings.ShowThumbnails;
            resultLimit.Value = settings.ResultLimit;
            saveCheck.Checked = settings.SaveToDisk;
            formatCombo.SelectedIndex = settings.Format == "mp3" ? 1 : 0;
            bitrateNumber.Value = settings.Mp3Bitrate;
            outputDirectory.Text = settings.OutputDirectory;
            cooldownSeconds.Value = settings.CooldownSeconds;
            maxDurationMinutes.Value = settings.MaxDurationMinutes;
            maxSizeMiB.Value = settings.MaxSizeMiB;
            maxParallelDownloadsNumber.Value = settings.MaxParallelDownloads;
            stretchSettings = settings.Stretch;
            stretchCheck.Checked = settings.StretchEnabled;
            for (int index = 0; index < columnMenuItems.Length; index++)
                columnMenuItems[index].Checked = settings.VisibleColumns[index];
            ApplyColumns();
            UpdateOptionAvailability();
        }
        finally { restoringSettings = false; }
    }

    private void SaveSettings()
    {
        try
        {
            var size = WindowState == FormWindowState.Normal ? Size : RestoreBounds.Size;
            new YoutubeBrowserSettings
            {
                Width = size.Width, Height = size.Height,
                Maximized = WindowState == FormWindowState.Maximized,
                SplitterDistance = mainSplit.SplitterDistance,
                ShowThumbnails = thumbnailCheck.Checked,
                VisibleColumns = columnMenuItems.Select(item => item.Checked).ToArray(),
                ResultLimit = (int)resultLimit.Value, SaveToDisk = saveCheck.Checked,
                Format = formatCombo.SelectedIndex == 1 ? "mp3" : "wav",
                Mp3Bitrate = (int)bitrateNumber.Value, OutputDirectory = outputDirectory.Text.Trim(),
                CooldownSeconds = (int)cooldownSeconds.Value,
                MaxDurationMinutes = (int)maxDurationMinutes.Value, MaxSizeMiB = (int)maxSizeMiB.Value,
                MaxParallelDownloads = (int)maxParallelDownloadsNumber.Value,
                StretchEnabled = stretchCheck.Checked, Stretch = stretchSettings
            }.Save();
        }
        catch (Exception error)
        {
            ReportError("Unable to save YouTube browser settings", error);
        }
    }

    private void Options_Changed(object? sender, EventArgs e) => UpdateOptionAvailability();

    private void UpdateOptionAvailability()
    {
        outputDirectory.Enabled = saveCheck.Checked;
        folderButton.Enabled = saveCheck.Checked;
        bitrateNumber.Enabled = formatCombo.SelectedIndex == 1;
        stretchConfigButton.Enabled = stretchCheck.Checked && !busy && !closing;
        stretchLabel.Text = stretchCheck.Checked && stretchSettings is not null
            ? $"Target: {stretchSettings.TargetBpm:0.##} BPM | Stretch {(stretchSettings.UseV2 ? "V2" : "V1")}"
            : "Time stretch is off";
        storageLabel.Text = saveCheck.Checked
            ? "Saved files are the ORIGINAL converted audio. Stretch affects only the imported RAM AudioObj."
            : "Temporary disk space is required. Files are deleted after loading into RAM; stretch affects RAM only.";
    }

    private void Folder_Click(object? sender, EventArgs e)
    {
        try
        {
            folderDialog.SelectedPath = outputDirectory.Text;
            if (folderDialog.ShowDialog(this) == DialogResult.OK) outputDirectory.Text = folderDialog.SelectedPath;
        }
        catch (Exception error) { ReportError("Unable to choose an output directory", error); }
    }

    private void Stretch_CheckedChanged(object? sender, EventArgs e)
    {
        if (restoringSettings) return;
        if (stretchCheck.Checked && !ConfigureStretch())
        {
            restoringSettings = true;
            stretchCheck.Checked = false;
            restoringSettings = false;
        }
        UpdateOptionAvailability();
    }

    private void StretchConfig_Click(object? sender, EventArgs e)
    {
        ConfigureStretch();
        UpdateOptionAvailability();
    }

    private bool ConfigureStretch()
    {
        try
        {
            using var dummy = new AudioObj { Bpm = stretchSettings?.TargetBpm ?? 120f, ScannedBpm = 120f };
            dummy.Rename("YouTube import configuration");
            using var dialog = new TimeStretchDialog(audios: [dummy]) { IsConfigureMode = true };
            if (dialog.ShowDialog(this) != DialogResult.OK || dialog.ConfirmedSettings is null) return false;
            stretchSettings = dialog.ConfirmedSettings;
            return true;
        }
        catch (Exception error)
        {
            ReportError("Unable to configure time stretch", error);
            return false;
        }
    }

    private void Column_CheckedChanged(object? sender, EventArgs e)
    {
        if (!restoringSettings) ApplyColumns();
    }

    private void ApplyColumns()
    {
        for (int index = 0; index < columnMenuItems.Length; index++)
            resultsList.Columns[index].Width = columnMenuItems[index].Checked ? columnWidths[index] : 0;
    }

    private void Column_WidthChanging(object? sender, ColumnWidthChangingEventArgs e)
    {
        if (!columnMenuItems[e.ColumnIndex].Checked)
        {
            e.Cancel = true;
            e.NewWidth = 0;
        }
        else if (e.NewWidth > 24) columnWidths[e.ColumnIndex] = e.NewWidth;
    }
}
