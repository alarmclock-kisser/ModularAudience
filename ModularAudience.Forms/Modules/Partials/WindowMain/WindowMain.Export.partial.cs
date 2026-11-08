using ModularAudience.Audio;
using ModularAudience.Forms.Helpers;

namespace ModularAudience.Forms
{
    public partial class WindowMain
    {
        public static string GlobalExportFormat { get; private set; } = "wav";
        public static int GlobalExportBits { get; private set; } = 16;

        public bool AllInOneBag => this.checkBox_oneBag.Checked;

        private void InitializeExportControls()
        {
            this._exportOptions = new ExportOptionsController(this.comboBox_exportFormat, this.comboBox_exportBits,
                this._applicationPreferences.ExportFormat, this._applicationPreferences.ExportBits);
            GlobalExportFormat = this._exportOptions.Format.TrimStart('.');
            GlobalExportBits = this._exportOptions.Bits;
            this._exportOptions.Changed += (_, _) =>
            {
                this.SaveApplicationPreference(() => this._preferencesStore.SetExport(this._exportOptions.Format, this._exportOptions.Bits));
            };
        }

        private async void button_export_Click(object sender, EventArgs e)
        {
            if (LastSelectedTrackView == null || LastSelectedTrackView.IsDisposed)
            {
                MessageBox.Show(this, "No track selected.", "Export Audio", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            bool ctrlFlag = ModifierKeys.HasFlag(Keys.Control);
            string formatKey = WindowMainFormatHelpers.NormalizeFormatExtension(this.comboBox_exportFormat.SelectedItem as string);
            string normalizedFormat = formatKey.TrimStart('.');
            int bits = WindowMainFormatHelpers.ResolveBitSelection(formatKey, this.comboBox_exportBits.SelectedItem);

            string exportFilePath = this.AudioC.ExportPath;
            if (ctrlFlag)
            {
                SaveFileDialog saveFileDialog = new()
                {
                    Filter = $"{normalizedFormat.ToUpperInvariant()} files|*{formatKey}",
                    FileName = Path.GetFileName(exportFilePath),
                    InitialDirectory = Path.GetDirectoryName(exportFilePath) ?? this.AudioC.ExportPath,
                    OverwritePrompt = true,
                    Title = "Select Export File Location",
                    DefaultExt = normalizedFormat
                };

                if (saveFileDialog.ShowDialog() != DialogResult.OK)
                {
                    return;
                }

                exportFilePath = saveFileDialog.FileName;
            }

            string? resultPath = AudioExporter.IsMp3Format(normalizedFormat)
                ? await this.AudioC.Exporter.ExportMp3Async(LastSelectedTrackView.OriginalAudio, bits, Environment.ProcessorCount - 1, exportFilePath)
                : await this.AudioC.Exporter.ExportWavAsync(LastSelectedTrackView.OriginalAudio, bits, exportFilePath);

            if (string.IsNullOrEmpty(resultPath))
            {
                MessageBox.Show(this, "Export failed.", "Export Audio", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            MessageBox.Show(this, $"Exported to:\n{resultPath}", "Export Audio", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

    }
}
