#nullable enable
using ModularAudience.Audio;
using ModularAudience.Cuda;
using System;
using System.Drawing;
using System.Windows.Forms;
using static ModularAudience.Audio.Processing.AudioVisualizerProcessor;
using static ModularAudience.Cuda.NvencVideoRenderer;

namespace ModularAudience.Forms
{
    public partial class VisualizerDialogPreview : Form
    {
        private PreviewSession? _session;

        private volatile bool _paused;

        public VisualizerDialogPreview(
            AudioObj audio,
            Size resolution,
            double frameRate = 10,
            float amplification = 0.66f,
            Color? graphColor = null,
            Color? backColor = null,
            int thickness = 1,
            float threshold = 0.1f,
            NvencOptions encOptions = null!,
            VisualizerOptions? visualizerOptions = null,
            VisualizerMode? visualizerMode = null)
        {
            this.InitializeComponent();

            this.pictureBox_preview.SizeMode = PictureBoxSizeMode.Zoom;
            this.pictureBox_preview.BackColor = VisualizerDialog.PreviewBackColor;

            this._session = NvencVideoRenderer.CreatePreviewSession(
                audio,
                width: resolution.Width,
                height: resolution.Height,
                frameRate: frameRate,
                amplification: amplification,
                graphColor: graphColor,
                backColor: backColor,
                thickness: thickness,
                threshold: threshold,
                visualizerMode: visualizerMode,
                visualizerOptions: visualizerOptions,
                maxWorkers: Environment.ProcessorCount,
                channelCapacity: 4,
                maxPreviewDuration: TimeSpan.FromSeconds(20));

            // ✅ HIER HIN
            this.Shown += (_, __) =>
            {
                this.ResizeFormToPictureBoxOriginalSize(resolution);
                var screen = Screen.FromControl(this).WorkingArea;
                if (resolution.Width > screen.Width || resolution.Height > screen.Height)
                {
                    this.pictureBox_preview.SizeMode = PictureBoxSizeMode.Zoom;
                    this.ClientSize = new Size(
                        Math.Min(resolution.Width, screen.Width),
                        Math.Min(resolution.Height, screen.Height));
                }

                this._session?.Start(
                    this.pictureBox_preview,
                    startAudio: (ct, isPaused) =>
                    {
                        _ = audio.PlayAsync(ct, initialVolume: audio.Volume);

                        // Audio sofort beim Abbruch stoppen – egal wie das Fenster geschlossen wird.
                        ct.Register(() =>
                        {
                            try
                            {
                                _ = audio.StopAsync();
                            }
                            catch (Exception ex)
                            {
                                LogCollection.Log("Preview audio stop failed: " + ex.Message);
                            }
                        });

                        return Task.Run(async () =>
                        {
                            bool wasPaused = false;
                            while (!ct.IsCancellationRequested)
                            {
                                if (isPaused() && !wasPaused)
                                {
                                    await audio.PauseAsync().ConfigureAwait(false);
                                    wasPaused = true;
                                }
                                else if (!isPaused() && wasPaused)
                                {
                                    await audio.PauseAsync().ConfigureAwait(false); // toggles back to playing when paused
                                    wasPaused = false;
                                }

                                await Task.Delay(30, ct).ConfigureAwait(false);
                            }
                        }, ct);
                    });
            };

            this.FormClosing += (_, __) =>
            {
                try
                {
                    // 1) stop preview pipeline (cancels token)
                    this._session?.Dispose();
                    this._session = null;

                    // 2) stop audio explicitly
                    _ = audio.StopAsync();
                }
                catch { }
            };

            this.KeyPreview = true;
            this.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Space)
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;

                    this._session?.TogglePause();
                    this.Text = (this._session?.IsPaused ?? false) ? "Preview (Paused)" : "Preview";
                }
            };

        }




        private void ResizeFormToPictureBoxOriginalSize(Size contentSize)
        {
            // Suspend layout to avoid flicker
            this.SuspendLayout();

            try
            {
                // Set PictureBox to exact pixel size
                this.pictureBox_preview.Dock = DockStyle.None;
                this.pictureBox_preview.Size = contentSize;
                this.pictureBox_preview.Location = Point.Empty;

                // Calculate required form size so CLIENT area fits the PictureBox
                var desiredClient = contentSize;

                // Current non-client overhead (borders + title bar)
                int extraWidth = this.Width - this.ClientSize.Width;
                int extraHeight = this.Height - this.ClientSize.Height;

                this.ClientSize = new Size(
                    desiredClient.Width,
                    desiredClient.Height
                );

                // Optional: center on screen
                this.StartPosition = FormStartPosition.CenterScreen;
            }
            finally
            {
                this.ResumeLayout(performLayout: true);
            }
        }



        public void TogglePause()
        {
            this._paused = !this._paused;
        }

        public void SetPaused(bool paused)
        {
            this._paused = paused;
        }


    }
}
