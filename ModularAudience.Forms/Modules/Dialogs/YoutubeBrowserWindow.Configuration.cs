using ModularAudience.Audio;
using ModularAudience.Audio.Processing;

namespace ModularAudience.Forms.Modules.Dialogs;

public partial class YoutubeBrowserWindow
{
    private PlaylistStretchSettings? stretchSettings;
    private readonly int[] columnWidths = [320, 160, 90, 100, 120, 320, 110];
    private int mp3Bitrate = 192;
    private int wavBitDepth = 16;

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
            mp3Bitrate = settings.Mp3Bitrate;
            wavBitDepth = settings.WavBitDepth;
            formatCombo.SelectedIndex = settings.Format == "mp3" ? 1 : 0;
            UpdateAudioQuality();
            ipVersionCombo.SelectedIndex = (int)settings.IpVersion;
            proxyModeCombo.SelectedIndex = settings.ProxyMode;
            proxyText.Text = settings.ProxyUrl;
            sortColumn = settings.SortColumn;
            sortDescending = settings.SortDescending;
            ApplySort();
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
                Mp3Bitrate = mp3Bitrate, WavBitDepth = wavBitDepth,
                SortColumn = sortColumn, SortDescending = sortDescending,
                IpVersion = (ModularAudience.Ytdlp.YtdlpIpVersion)ipVersionCombo.SelectedIndex,
                ProxyMode = proxyModeCombo.SelectedIndex, ProxyUrl = proxyText.Text.Trim(),
                OutputDirectory = outputDirectory.Text.Trim(),
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

    private void Options_Changed(object? sender, EventArgs e)
    {
        if (sender == formatCombo) UpdateAudioQuality();
        UpdateOptionAvailability();
    }

    private void UpdateAudioQuality()
    {
        bool mp3 = formatCombo.SelectedIndex == 1;
        audioQuality.SelectedItemChanged -= AudioQuality_Changed;
        audioQuality.Items.Clear();
        int[] values = mp3 ? [64, 96, 128, 192, 256, 320] : [8, 16, 24, 32];
        foreach (int value in values) audioQuality.Items.Add(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        audioQuality.SelectedItem = (mp3 ? mp3Bitrate : wavBitDepth).ToString(System.Globalization.CultureInfo.InvariantCulture);
        audioQualityLabel.Text = mp3 ? "kbps" : "PCM bits";
        audioQuality.AccessibleName = mp3 ? "MP3 bitrate in kbps" : "WAV PCM bit depth (32-bit signed integer)";
        audioQuality.SelectedItemChanged += AudioQuality_Changed;
    }

    private void AudioQuality_Changed(object? sender, EventArgs e)
    {
        if (!int.TryParse(audioQuality.SelectedItem?.ToString(), out int value)) return;
        if (formatCombo.SelectedIndex == 1) mp3Bitrate = value;
        else wavBitDepth = value;
    }

    private void UpdateOptionAvailability()
    {
        outputDirectory.Enabled = saveCheck.Checked;
        folderButton.Enabled = saveCheck.Checked;
        stretchConfigButton.Enabled = stretchCheck.Checked && !busy && !closing;
        stretchLabel.Text = stretchCheck.Checked && stretchSettings is not null
            ? $"Target: {stretchSettings.TargetBpm:0.##} BPM | Stretch {(stretchSettings.UseV2 ? "V2" : "V1")}"
            : "Time stretch is off";
        storageLabel.Text = saveCheck.Checked
            ? "Saved files are the ORIGINAL converted audio. Stretch affects only the imported RAM AudioObj."
            : "Temporary disk space is required. Files are deleted after loading into RAM; stretch affects RAM only.";
    }

    private ModularAudience.Ytdlp.YtdlpNetworkOptions CaptureNetworkOptions()
    {
        if (proxyModeCombo.SelectedIndex == 2 && string.IsNullOrWhiteSpace(proxyText.Text))
            throw new ArgumentException("Enter an HTTP(S) proxy URL or select Environment / default. An explicit proxy cannot be empty.");
        var options = new ModularAudience.Ytdlp.YtdlpNetworkOptions
        {
            IpVersion = (ModularAudience.Ytdlp.YtdlpIpVersion)ipVersionCombo.SelectedIndex,
            ProxyUrl = proxyModeCombo.SelectedIndex switch { 1 => string.Empty, 2 => proxyText.Text.Trim(), _ => null }
        };
        options.Validate();
        return options;
    }

    private void ResetNetwork_Click(object? sender, EventArgs e)
    {
        ipVersionCombo.SelectedIndex = 0;
        proxyModeCombo.SelectedIndex = 0;
        proxyText.Clear();
        AppendLog("Network reset: automatic IP selection and environment/default proxy routing. TLS verification remains enabled.");
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
        FitResultColumns();
    }

    private void Column_WidthChanging(object? sender, ColumnWidthChangingEventArgs e)
    {
        if (fittingColumns) return;
        if (!columnMenuItems[e.ColumnIndex].Checked)
        {
            e.Cancel = true;
            e.NewWidth = 0;
        }
        else if (e.NewWidth > 24) columnWidths[e.ColumnIndex] = e.NewWidth;
    }
}
