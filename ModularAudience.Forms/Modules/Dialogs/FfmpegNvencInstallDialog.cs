using ModularAudience.Cuda.NVenc;
using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ModularAudience.Forms
{
    public partial class FfmpegNvencInstallDialog : Form
    {
        private readonly CancellationTokenSource _cts = new();
        private bool _installCompleted;
        private string? _installedPath;

        public string? InstalledFfmpegPath => _installCompleted ? _installedPath : null;

        public FfmpegNvencInstallDialog()
        {
            this.InitializeComponent();
        }

        private async void button_install_Click(object sender, EventArgs e)
        {
            this.button_install.Enabled = false;
            this.button_cancel.Enabled = true;
            this.progressBar_install.Value = 0;
            this.label_status.Text = "Starting installation...";

            try
            {
                var progress = new Progress<string>(msg =>
                {
                    if (this.IsDisposed)
                    {
                        return;
                    }

                    this.label_status.Text = msg;

                    // Parse percentage from status text: "Downloaded: 45.2 MB / 100.5 MB (44.9%)"
                    var m = System.Text.RegularExpressions.Regex.Match(msg, @"\((\d+(?:\.\d+)?)%\)");
                    if (m.Success)
                    {
                        float pct = float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                        this.progressBar_install.Value = (int)Math.Clamp(pct, 0, 100);
                    }
                    else if (msg.StartsWith("Downloading", StringComparison.OrdinalIgnoreCase))
                    {
                        // No percentage yet - show indeterminate-style partial progress
                        if (this.progressBar_install.Value < 10)
                        {
                            this.progressBar_install.Value = 5;
                        }
                    }
                    else if (msg.StartsWith("Extracting", StringComparison.OrdinalIgnoreCase))
                    {
                        this.progressBar_install.Value = 90;
                    }
                });

                // Clean up any previous incompatible managed installations
                FfmpegNvencInstaller.RemoveManagedNvencFfmpeg();

                this._installedPath = await FfmpegNvencInstaller.InstallNvencFfmpegAsync(progress, this._cts.Token);
                this._installCompleted = true;

                this.progressBar_install.Value = 100;
                this.label_status.Text = "Installation completed successfully!";
                this.button_done.Enabled = true;
            }
            catch (OperationCanceledException)
            {
                this.label_status.Text = "Installation cancelled.";
            }
            catch (Exception ex)
            {
                this.label_status.Text = "Error: " + ex.Message;
                MessageBox.Show(this, "FFmpeg NVENC installation failed:\n\n" + ex.Message, "Installation Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                this.button_install.Enabled = true;
                this.button_cancel.Enabled = false;
            }
        }

        private void button_cancel_Click(object sender, EventArgs e)
        {
            this._cts.Cancel();
        }

        private void button_done_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            this._cts.Dispose();
            base.OnFormClosed(e);
        }

        #region Windows Form Designer generated code

        private IContainer components = null;

        private void InitializeComponent()
        {
            this.label_info = new System.Windows.Forms.Label();
            this.label_status = new System.Windows.Forms.Label();
            this.progressBar_install = new System.Windows.Forms.ProgressBar();
            this.button_install = new System.Windows.Forms.Button();
            this.button_cancel = new System.Windows.Forms.Button();
            this.button_done = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // label_info
            // 
            this.label_info.Location = new System.Drawing.Point(12, 9);
            this.label_info.Name = "label_info";
            this.label_info.Size = new System.Drawing.Size(420, 75);
            this.label_info.TabIndex = 0;
            this.label_info.Text = "Your current FFmpeg build does not support NVIDIA NVENC codecs (h264_nvenc, hevc_nvenc). " +
                "This is required for GPU-accelerated video rendering.\n\n" +
                "Click Install to automatically download and install the latest FFmpeg build with NVENC support from BtbN GitHub releases.";
            // 
            // label_status
            // 
            this.label_status.Location = new System.Drawing.Point(12, 95);
            this.label_status.Name = "label_status";
            this.label_status.Size = new System.Drawing.Size(420, 23);
            this.label_status.TabIndex = 1;
            this.label_status.Text = "Ready.";
            // 
            // progressBar_install
            // 
            this.progressBar_install.Location = new System.Drawing.Point(12, 124);
            this.progressBar_install.Name = "progressBar_install";
            this.progressBar_install.Size = new System.Drawing.Size(420, 23);
            this.progressBar_install.TabIndex = 2;
            // 
            // button_install
            // 
            this.button_install.Location = new System.Drawing.Point(12, 157);
            this.button_install.Name = "button_install";
            this.button_install.Size = new System.Drawing.Size(100, 33);
            this.button_install.TabIndex = 3;
            this.button_install.Text = "Install";
            this.button_install.UseVisualStyleBackColor = true;
            this.button_install.Click += this.button_install_Click;
            // 
            // button_cancel
            // 
            this.button_cancel.Enabled = false;
            this.button_cancel.Location = new System.Drawing.Point(120, 157);
            this.button_cancel.Name = "button_cancel";
            this.button_cancel.Size = new System.Drawing.Size(100, 33);
            this.button_cancel.TabIndex = 4;
            this.button_cancel.Text = "Cancel";
            this.button_cancel.UseVisualStyleBackColor = true;
            this.button_cancel.Click += this.button_cancel_Click;
            // 
            // button_done
            // 
            this.button_done.Enabled = false;
            this.button_done.Location = new System.Drawing.Point(228, 157);
            this.button_done.Name = "button_done";
            this.button_done.Size = new System.Drawing.Size(100, 33);
            this.button_done.TabIndex = 5;
            this.button_done.Text = "Done";
            this.button_done.UseVisualStyleBackColor = true;
            this.button_done.Click += this.button_done_Click;
            // 
            // FfmpegNvencInstallDialog
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(444, 202);
            this.Controls.Add(this.button_done);
            this.Controls.Add(this.button_cancel);
            this.Controls.Add(this.button_install);
            this.Controls.Add(this.progressBar_install);
            this.Controls.Add(this.label_status);
            this.Controls.Add(this.label_info);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "FfmpegNvencInstallDialog";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Install FFmpeg with NVENC Support";
            this.ResumeLayout(false);
        }

        private System.Windows.Forms.Label label_info;
        private System.Windows.Forms.Label label_status;
        private System.Windows.Forms.ProgressBar progressBar_install;
        private System.Windows.Forms.Button button_install;
        private System.Windows.Forms.Button button_cancel;
        private System.Windows.Forms.Button button_done;

        #endregion
    }
}
