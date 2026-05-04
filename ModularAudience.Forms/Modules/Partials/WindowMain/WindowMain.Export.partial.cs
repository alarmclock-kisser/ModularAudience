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
            var orderedFormats = AudioExporter.AvailableExportFormats.Keys
                .OrderBy(f => f.Equals(".wav", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            this.comboBox_exportFormat.BeginUpdate();
            this.comboBox_exportFormat.Items.Clear();
            foreach (var format in orderedFormats)
            {
                this.comboBox_exportFormat.Items.Add(format);
            }
            this.comboBox_exportFormat.EndUpdate();

            if (orderedFormats.Count == 0)
            {
                this.comboBox_exportFormat.SelectedIndex = -1;
                this.comboBox_exportBits.Items.Clear();
                this.comboBox_exportBits.SelectedIndex = -1;
                return;
            }

            string defaultFormat = orderedFormats.FirstOrDefault(f => f.Equals(".wav", StringComparison.OrdinalIgnoreCase)) ?? orderedFormats[0];
            this.suppressExportFormatEvent = true;
            this.comboBox_exportFormat.SelectedItem = defaultFormat;
            this.suppressExportFormatEvent = false;
            this.UpdateExportBitOptions(selectMiddleOnChange: true);
        }

        private void UpdateExportBitOptions(bool selectMiddleOnChange = false)
        {
            if (AudioExporter.AvailableExportFormats.Count == 0)
            {
                this.comboBox_exportBits.Items.Clear();
                this.comboBox_exportBits.SelectedIndex = -1;
                return;
            }

            string? selectedFormat = this.comboBox_exportFormat.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(selectedFormat) || !AudioExporter.AvailableExportFormats.ContainsKey(selectedFormat))
            {
                string fallback = AudioExporter.AvailableExportFormats.Keys.First();
                this.suppressExportFormatEvent = true;
                this.comboBox_exportFormat.SelectedItem = fallback;
                this.suppressExportFormatEvent = false;
                selectedFormat = fallback;
            }

            if (!AudioExporter.AvailableExportFormats.TryGetValue(selectedFormat!, out var bitOptions) || bitOptions.Length == 0)
            {
                this.comboBox_exportBits.Items.Clear();
                this.comboBox_exportBits.SelectedIndex = -1;
                return;
            }

            int middleIndex = Math.Clamp(bitOptions.Length / 2, 0, bitOptions.Length - 1);
            int middleBit = bitOptions[middleIndex];
            int? preferredBit = (!selectMiddleOnChange && this.comboBox_exportBits.SelectedItem is int existing && bitOptions.Contains(existing))
                ? existing
                : middleBit;

            this.comboBox_exportBits.BeginUpdate();
            this.comboBox_exportBits.Items.Clear();
            this.comboBox_exportBits.Items.AddRange(bitOptions.Cast<object>().ToArray());
            this.comboBox_exportBits.EndUpdate();

            if (preferredBit.HasValue)
            {
                this.comboBox_exportBits.SelectedItem = preferredBit.Value;
            }
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

            string? resultPath = normalizedFormat.Equals("mp3", StringComparison.OrdinalIgnoreCase)
                ? await this.AudioC.Exporter.ExportMp3Async(LastSelectedTrackView.OriginalAudio, bits, Environment.ProcessorCount - 1, exportFilePath)
                : await this.AudioC.Exporter.ExportWavAsync(LastSelectedTrackView.OriginalAudio, bits, exportFilePath);

            if (string.IsNullOrEmpty(resultPath))
            {
                MessageBox.Show(this, "Export failed.", "Export Audio", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            MessageBox.Show(this, $"Exported to:\n{resultPath}", "Export Audio", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void comboBox_exportFormat_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (!this.suppressExportFormatEvent)
            {
                this.UpdateExportBitOptions(selectMiddleOnChange: true);
            }

            GlobalExportFormat = this.comboBox_exportFormat.SelectedItem as string ?? "wav";
        }
    }
}
