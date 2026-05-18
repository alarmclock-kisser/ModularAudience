using ModularAudience.Audio;
using ModularAudience.Forms.Helpers;

namespace ModularAudience.Forms
{
    public partial class AudioCollectionView
    {
        private async void button_export_Click(object sender, EventArgs e)
        {
            this.button_export.Enabled = false;

            try
            {
                bool ctrlDown = (ModifierKeys & Keys.Control) != 0;
                bool shiftDown = (ModifierKeys & Keys.Shift) != 0;

                List<AudioObj> selectedAudios = this.listBox_audios.SelectedItems.Cast<AudioObj>().OfType<AudioObj>().ToList();
                if (shiftDown)
                {
                    selectedAudios = this.AudioC.Audios.ToList();
                }

                if (selectedAudios.Count == 0)
                {
                    MessageBox.Show(this, "Please select at least one audio first.", "Export", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                if (ctrlDown && !shiftDown && selectedAudios.Count == 1)
                {
                    AudioObj audio = selectedAudios[0];
                    string defaultName = AudioCollectionViewHelpers.SanitizePathSegment(audio.Name) + "." + WindowMain.GlobalExportFormat;

                    using SaveFileDialog saveDialog = new()
                    {
                        Filter = $"{WindowMain.GlobalExportFormat.ToUpperInvariant()} files|*{WindowMain.GlobalExportFormat}|All Files (*.*)|*.*",
                        DefaultExt = WindowMain.GlobalExportFormat,
                        FileName = defaultName,
                        AddExtension = true,
                        InitialDirectory = Directory.Exists(this.AudioC.ExportPath) ? this.AudioC.ExportPath : Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)
                    };

                    if (saveDialog.ShowDialog(this) != DialogResult.OK)
                    {
                        return;
                    }

                    string finalFile = AudioCollectionViewHelpers.EnsureAudioFileExtension(saveDialog.FileName);
                    string? directory = Path.GetDirectoryName(finalFile);
                    if (string.IsNullOrEmpty(directory))
                    {
                        MessageBox.Show(this, "Invalid file name.", "Export", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    Directory.CreateDirectory(directory);
                    if (WindowMain.GlobalExportFormat.Contains("3", StringComparison.OrdinalIgnoreCase))
                    {
                        await this.AudioC.Exporter.ExportMp3Async(audio, WindowMain.GlobalExportBits, Environment.ProcessorCount / 2, finalFile);
                    }
                    else
                    {
                        await this.AudioC.Exporter.ExportWavAsync(audio, WindowMain.GlobalExportBits, directory, writeBpmTag: true, customFilePath: finalFile);
                    }
                    return;
                }

                string exportFolder = this.AudioC.ExportPath;
                Directory.CreateDirectory(exportFolder);

                bool ctrlDownOnly = ctrlDown && !shiftDown;
                bool ctrlShift = ctrlDown && shiftDown;

                if (ctrlDownOnly && selectedAudios.Count == 1)
                {
                    AudioObj audio = selectedAudios[0];
                    string defaultName = AudioCollectionViewHelpers.SanitizePathSegment(audio.Name) + "." + WindowMain.GlobalExportFormat;

                    using SaveFileDialog saveDialog = new()
                    {
                        Filter = $"{WindowMain.GlobalExportFormat.ToUpperInvariant()} files|*{WindowMain.GlobalExportFormat}|All Files (*.*)|*.*",
                        DefaultExt = WindowMain.GlobalExportFormat,
                        FileName = defaultName,
                        AddExtension = true,
                        InitialDirectory = Directory.Exists(this.AudioC.ExportPath) ? this.AudioC.ExportPath : Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)
                    };

                    if (saveDialog.ShowDialog(this) != DialogResult.OK)
                    {
                        return;
                    }

                    string finalFile = AudioCollectionViewHelpers.EnsureAudioFileExtension(saveDialog.FileName);
                    string? directory = Path.GetDirectoryName(finalFile);
                    if (string.IsNullOrEmpty(directory))
                    {
                        MessageBox.Show(this, "Invalid file name.", "Export", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    Directory.CreateDirectory(directory);
                    if (AudioExporter.IsMp3Format(WindowMain.GlobalExportFormat))
                    {
                        await this.AudioC.Exporter.ExportMp3Async(audio, WindowMain.GlobalExportBits, Environment.ProcessorCount / 2, finalFile);
                    }
                    else
                    {
                        await this.AudioC.Exporter.ExportWavAsync(audio, WindowMain.GlobalExportBits, directory, writeBpmTag: true, customFilePath: finalFile);
                    }
                    return;
                }

                if ((ctrlDownOnly && selectedAudios.Count > 1) || ctrlShift)
                {
                    using FolderBrowserDialog folderDialog = new()
                    {
                        Description = "Select export folder",
                        SelectedPath = exportFolder,
                        ShowNewFolderButton = true,
                        AutoUpgradeEnabled = false
                    };

                    if (folderDialog.ShowDialog(this) != DialogResult.OK)
                    {
                        return;
                    }

                    exportFolder = folderDialog.SelectedPath;
                }
                else if (!ctrlDown && (selectedAudios.Count > 1 || shiftDown))
                {
                    string folderName = this.BuildExportFolderName(exportFolder);
                    exportFolder = Path.Combine(exportFolder, folderName);
                    Directory.CreateDirectory(exportFolder);
                }

                var tasks = selectedAudios.Select(a => AudioExporter.IsMp3Format(WindowMain.GlobalExportFormat)
                    ? this.AudioC.Exporter.ExportMp3Async(a, WindowMain.GlobalExportBits, Environment.ProcessorCount / 2, Path.Combine(exportFolder, AudioCollectionViewHelpers.SanitizePathSegment(a.Name) + ".mp3"))
                    : this.AudioC.Exporter.ExportWavAsync(a, WindowMain.GlobalExportBits, exportFolder, writeBpmTag: true, customFilePath: Path.Combine(exportFolder, AudioCollectionViewHelpers.SanitizePathSegment(a.Name) + ".wav")));
                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Export failed: " + ex.Message, "Export", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                this.button_export.Enabled = true;
            }
        }

        internal void RefreshList()
        {
            this.listBox_audios.Invalidate();
        }

        private void AudioCollectionView_Resize(object? sender, EventArgs e)
        {
            this.SuspendLayout();
            this.AdjustLayout();
            this.ResumeLayout();
            this.listBox_audios.Invalidate();
        }

        private void AudioCollectionView_ResizeBegin(object? sender, EventArgs e)
        {
            this._isUserResizing = true;
            this._resizeStartHeight = this.Height;
            this._resizeStartWidth = this.Width;
        }

        private void AudioCollectionView_ResizeEnd(object? sender, EventArgs e)
        {
            if (!this._isUserResizing)
            {
                return;
            }

            try
            {
                int deltaH = this.Height - this._resizeStartHeight;
                int deltaW = this.Width - this._resizeStartWidth;

                this._lastUserResizeWasHorizontal = Math.Abs(deltaW) > Math.Abs(deltaH) && deltaW != 0;

                if (deltaH != 0)
                {
                    int newClearance = this.FormListBoxClearance + deltaH;
                    newClearance = Math.Clamp(newClearance, -2000, 2000);
                    this.FormListBoxClearance = newClearance;
                }

                this.SuspendLayout();
                this.AdjustLayout();
                this.ResumeLayout();
                this.listBox_audios.Invalidate();
            }
            finally
            {
                this._isUserResizing = false;
            }
        }

        private void AdjustLayout()
        {
            this.checkBox_autoPlay.Location = new Point(this.ClientSize.Width - this.checkBox_autoPlay.Width - 10, 10);

            int top = this.button_export.Bottom + 5;
            int bottom = this.ClientSize.Height - 10;
            int left = 10;
            int right = this.ClientSize.Width - 10;

            if (bottom < top)
            {
                bottom = top;
            }

            this.listBox_audios.Location = new Point(left, top);
            this.listBox_audios.Size = new Size(Math.Max(0, right - left), Math.Max(0, bottom - top));
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_NCLBUTTONDBLCLK = 0x00A3;
            if (m.Msg == WM_NCLBUTTONDBLCLK)
            {
                if (this.isPinned)
                {
                    return;
                }
                try
                {
                    this.BeginInvoke(new Action(this.ShowCollectionRenameDialog));
                }
                catch { }
                return;
            }

            base.WndProc(ref m);
        }

        private void menuToolStripItem_pinWindow_CheckedChanged(object? sender, EventArgs e)
        {
            this.isPinned = this.menuToolStripItem_pinWindow.Checked;
            this.TopMost = this.isPinned;
            this.Move += this.AudioCollectionView_Move;
            if (this.isPinned)
            {
                this.Move -= this.AudioCollectionView_Move;
            }
        }

        private void AudioCollectionView_Move(object? sender, EventArgs e)
        {
            if (this.isPinned)
            {
                return;
            }
        }

        private void ShowCollectionRenameDialog()
        {
            string current = this.Text ?? string.Empty;
            string input = Microsoft.VisualBasic.Interaction.InputBox("Enter new name for this collection:", "Rename Collection", current);
            if (string.IsNullOrEmpty(input))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(input) && input != current)
            {
                this.Text = input;
            }
        }

        internal void Rename(string newName)
        {
            this.Text = newName;
        }
    }
}
