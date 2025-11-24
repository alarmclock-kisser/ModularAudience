using ModularAudience.Audio;
using NAudience.Core;
using System;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace ModularAudience.Forms.Modules
{
    public partial class TrackView : Form
    {
        private const int DragThresholdPx = 4;
        private static readonly int[] LoopSteps = { 1, 2, 4, 8, 16, 32, 64 };
        private const int MaxSamplesPerPixel = 16384;
        private const int MinSamplesPerPixel = 8;
        private static int selectionCopySeed;

        public readonly AudioObj OriginalAudio;
        public readonly TrackViewSettings Settings;

        private readonly Timer frameTimer;
        private bool frameBusy;

        private CancellationTokenSource? waveformRenderCts;
        private Bitmap? currentWaveformBitmap;
        private long renderTickCount;

        private CancellationTokenSource? playbackCts;

        private int samplesPerPixel = 128;
        private long offsetFrames;
        private long selectStartFrame = -1;
        private long selectEndFrame = -1;
        private long lastClickFrame = -1;
        private bool pendingSelect;
        private bool dragSelecting;
        private int mouseDownX;
        private bool mouseOverWave;
        private int mouseX;

        private bool loopEnabled;
        private int loopDenominator;
        private long loopBaseStartSamples;
        private long loopBaseEndSamples;
        private long loopFractionSamples;
        private bool suppressSettingsCheckbox;
        private readonly int designerClientWidth;
        private readonly int designerWaveWidth;

        public TrackView(AudioObj audio)
        {
            this.InitializeComponent();
            this.StartPosition = FormStartPosition.Manual;
            this.designerClientWidth = this.ClientSize.Width;
            this.designerWaveWidth = this.pictureBox_waveform.Width;
            this.OriginalAudio = audio.Clone();
            this.Settings = new TrackViewSettings(this)
            {
                Owner = this
            };

            this.KeyPreview = true;
            this.KeyDown += this.TrackView_KeyDown;

            this.ApplySettingsAppearance();

            // Setze LastSelectedTrackView bei Aktivierung, Fokus oder Klick auf die Form
            this.Activated += (_, __) => this.SetAsLastSelected();
            this.GotFocus += (_, __) => this.SetAsLastSelected();
            this.MouseDown += (_, __) => this.SetAsLastSelected();
            this.RegisterInteractionEvents(this);
            this.SizeChanged += this.TrackView_SizeChanged;

            this.Text = "#" + WindowMain.TrackViews.Count.ToString("D2") + " - " + audio.Name;
            this.OriginalAudio.SelectionStart = -1;
            this.OriginalAudio.SelectionEnd = -1;
            this.OriginalAudio.LoopEnabled = false;

            this.EnablePictureBoxDoubleBuffering();
            this.InitializeTrackControls();
            this.ApplyInitialTrackSizing();
            this.PositionSettingsWindow();
            this.RecalculateLoopFraction();
            this.ApplyLoopFractionToAudio();

            this.frameTimer = new Timer();
            this.UpdateFrameTimerInterval();
            this.frameTimer.Tick += async (_, __) => await this.FrameTickAsync();
            this.frameTimer.Start();

            this.Shown += (_, __) =>
            {
                this.PositionSettingsWindow();
                this.frameTimer.Start();
            };
            this.VisibleChanged += (_, __) =>
            {
                if (this.Visible)
                {
                    this.frameTimer.Start();
                }
                else
                {
                    this.frameTimer.Stop();
                    this.CancelPendingRender();
                }
            };
            this.LocationChanged += (_, __) => this.PositionSettingsWindow();
            this.SizeChanged += (_, __) => this.PositionSettingsWindow();

            this.FormClosing += async (s, e) =>
            {
                e.Cancel = true;
                this.Settings.Hide();
                this.Hide();
                this.frameTimer.Stop();
                this.CancelPendingRender();
                this.DisposeCurrentBitmap();
                await this.StopPlaybackAsync().ConfigureAwait(false);
                try { this.OriginalAudio.Dispose(); } catch { }
                // Setze LastSelectedTrackView auf null, falls diese Instanz die aktuelle ist
                if (WindowMain.LastSelectedTrackView == this)
                {
                    WindowMain.LastSelectedTrackView = null;
                }
				// Remove from TrackViews collection
                WindowMain.InvokeIfRequired(() =>
                {
                    WindowMain.TrackViews.Remove(this);
                });
			};
        }

        // Rekursiv alle relevanten Controls für Interaktion registrieren
        private void RegisterInteractionEvents(Control parent)
        {
            foreach (Control ctrl in parent.Controls)
            {
                ctrl.Click += (_, __) => this.SetAsLastSelected();
                ctrl.GotFocus += (_, __) => this.SetAsLastSelected();
                ctrl.MouseDown += (_, __) => this.SetAsLastSelected();
                if (ctrl.HasChildren)
                {
                    this.RegisterInteractionEvents(ctrl);
                }
            }
        }

        // Setzt die aktuelle Instanz als zuletzt ausgewählte TrackView
        private void SetAsLastSelected()
        {
            WindowMain.LastSelectedTrackView = this;
        }

        private void EnablePictureBoxDoubleBuffering()
        {
            try
            {
                typeof(Control)
                    .GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic)?
                    .SetValue(this.pictureBox_waveform, true);
            }
            catch { }
        }

        private void InitializeTrackControls()
        {
            this.vScrollBar_volume.Minimum = 0;
            this.vScrollBar_volume.Maximum = Math.Max(1, this.vScrollBar_volume.Maximum);
            this.vScrollBar_volume.Value = (int) Math.Clamp(this.vScrollBar_volume.Maximum * 0.2f, this.vScrollBar_volume.Minimum, this.vScrollBar_volume.Maximum - 1);
            this.ApplyVolumeFromScrollbar();

            this.UpdateOffsetScrollbar();

            this.pictureBox_waveform.MouseDown += this.Wave_MouseDown;
            this.pictureBox_waveform.MouseMove += this.Wave_MouseMove;
            this.pictureBox_waveform.MouseUp += this.Wave_MouseUp;
            this.pictureBox_waveform.MouseWheel += this.Wave_MouseWheel;
            this.pictureBox_waveform.MouseEnter += (_, __) => this.mouseOverWave = true;
            this.pictureBox_waveform.MouseLeave += (_, __) => this.mouseOverWave = false;

            this.button_loop.MouseDown += (_, e) => this.ToggleLoop(e);
        }

        private void ApplyInitialTrackSizing()
        {
            int pictureHeight = Math.Max(1, this.pictureBox_waveform.Height);
            int minWidth = pictureHeight; // enforce square minimum
            long totalFrames = this.GetTotalFrames();
            int baseSamplesPerPixel = 128;
            this.samplesPerPixel = baseSamplesPerPixel;

            int desiredWidth = totalFrames > 0
                ? (int) Math.Ceiling(totalFrames / (double) this.samplesPerPixel)
                : minWidth;

            Rectangle workingArea = Screen.FromControl(this).WorkingArea;
            int maxWidth = Math.Max(minWidth, Math.Min(this.designerWaveWidth, workingArea.Width - 20));
            desiredWidth = Math.Clamp(desiredWidth, minWidth, maxWidth);

            if (totalFrames > 0)
            {
                int requiredSamplesPerPixel = (int) Math.Ceiling(totalFrames / (double) Math.Max(1, desiredWidth));
                this.samplesPerPixel = Math.Clamp(requiredSamplesPerPixel, MinSamplesPerPixel, MaxSamplesPerPixel);
            }

            this.ApplyWaveformWidth(desiredWidth);
            this.UpdateOffsetScrollbar();
            this.RequestWaveformRender();
        }

        private void ApplyWaveformWidth(int desiredWidth)
        {
            desiredWidth = Math.Max(1, desiredWidth);
            int nonWaveWidth = Math.Max(0, this.designerClientWidth - this.designerWaveWidth);

            int chromeWidth = Math.Max(0, this.Width - this.ClientSize.Width);
            int minClientWidth = this.MinimumSize.Width > 0
                ? Math.Max(1, this.MinimumSize.Width - chromeWidth)
                : 1;
            int maxClientWidth = this.MaximumSize.Width > 0
                ? Math.Max(minClientWidth, this.MaximumSize.Width - chromeWidth)
                : int.MaxValue;

            int newClientWidth = Math.Clamp(desiredWidth + nonWaveWidth, minClientWidth, maxClientWidth);
            this.ClientSize = new Size(newClientWidth, this.ClientSize.Height);

            int availableWidth = Math.Max(1, newClientWidth - nonWaveWidth);
            this.pictureBox_waveform.Width = availableWidth;
            this.hScrollBar_offset.Left = this.pictureBox_waveform.Left;
            this.hScrollBar_offset.Width = Math.Max(0, availableWidth);
        }

        private void PositionSettingsWindow()
        {
            if (this.Settings == null || this.Settings.IsDisposed)
            {
                return;
            }

            var location = new Point(this.Location.X + this.Width + 5, this.Location.Y);
            this.Settings.Location = location;
        }

        internal void ApplySettingsAppearance()
        {
            try
            {
                this.pictureBox_waveform.BackColor = this.Settings.ColorBack;
                this.BackColor = this.Settings.GetShadedColor(this.Settings.ColorBack, 0.95f);
            }
            catch { }
        }

        internal void HandleSettingsChanged()
        {
            this.InvokeIfRequired(() =>
            {
                this.UpdateFrameTimerInterval();
                this.RequestWaveformRender();
            });
        }

        private void UpdateFrameTimerInterval()
        {
            int desiredInterval = (int) Math.Max(1, 1000.0 / this.Settings.FrameRate);
            this.frameTimer.Interval = desiredInterval;
        }

        public void RequestWaveformRender()
        {
            _ = this.RefreshWaveformAsync();
        }

        private async Task FrameTickAsync()
        {
            if (this.frameBusy)
            {
                return;
            }

            this.frameBusy = true;
            try
            {
                this.UpdateTimeDisplay();
                await this.RefreshWaveformAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                try { LogCollection.Log(ex); } catch { }
            }
            finally
            {
                this.frameBusy = false;
            }
        }

        private void UpdateTimeDisplay()
        {
            long sampleRate = Math.Max(1, this.OriginalAudio.SampleRate);
            TimeSpan current;
            if (this.OriginalAudio.Playing)
            {
                current = TimeSpan.FromSeconds(this.OriginalAudio.Position / (double) sampleRate);
                this.textBox_time.ForeColor = Color.Black;
            }
            else if (this.mouseOverWave)
            {
                long frame = this.offsetFrames + (long) Math.Clamp(this.mouseX, 0, Math.Max(1, this.pictureBox_waveform.Width)) * this.samplesPerPixel;
                current = TimeSpan.FromSeconds(frame / (double) sampleRate);
                this.textBox_time.ForeColor = Color.RoyalBlue;
            }
            else
            {
                long frame = Math.Clamp(this.OriginalAudio.Position, 0, Math.Max(0, this.GetTotalFrames()));
                current = TimeSpan.FromSeconds(frame / (double) sampleRate);
                this.textBox_time.ForeColor = Color.Black;
            }
            this.textBox_time.Text = string.Format("{0}:{1:D2}:{2:D2}.{3:D3}", (int) current.TotalHours, current.Minutes, current.Seconds, current.Milliseconds);
        }

        private async Task RefreshWaveformAsync()
        {
            if (this.pictureBox_waveform.Width <= 0 || this.pictureBox_waveform.Height <= 0)
            {
                return;
            }

            if (this.OriginalAudio.Playing)
            {
                this.AlignViewToCurrentPosition();
            }

            long maxOffset = this.GetMaxOffsetFrames();
            if (this.offsetFrames > maxOffset)
            {
                this.offsetFrames = maxOffset;
            }
            this.UpdateOffsetScrollbar();

            float caretNorm = this.GetCaretNormalizedPosition(maxOffset);
            Color waveColor = this.Settings.ResolveWaveColor();
            Color backColor = this.Settings.ColorBack;
            Color caretColor = this.Settings.ColorCaret;
            Color selectionColor = this.Settings.ColorSelection;

            await this.RenderWaveformBitmapAsync(waveColor, backColor, caretColor, selectionColor, caretNorm).ConfigureAwait(false);
        }

        private async Task RenderWaveformBitmapAsync(Color waveColor, Color backColor, Color caretColor, Color selectionColor, float caretPosition)
        {
            this.CancelPendingRender();
            var cts = new CancellationTokenSource();
            this.waveformRenderCts = cts;
            var token = cts.Token;
            long tick = Interlocked.Increment(ref this.renderTickCount);

            bool drawChannels = this.Settings.DrawChannelsSeparately;
            bool smooth = this.Settings.SmoothWaveform;
            double markerInterval = this.Settings.ShowTimeMarkers ? this.Settings.TimeMarkersInterval : 0.0;
            int caretWidth = this.Settings.CaretWidth;

            try
            {
                var bmp = await this.OriginalAudio.DrawWaveformAsync(
                    width: Math.Max(1, this.pictureBox_waveform.Width),
                    height: Math.Max(1, this.pictureBox_waveform.Height),
                    samplesPerPixel: this.samplesPerPixel,
                    drawEachChannel: drawChannels,
                    caretWidth: caretWidth,
                    offset: this.offsetFrames,
                    waveColor: waveColor,
                    backColor: backColor,
                    caretColor: caretColor,
                    smoothen: smooth,
                    timingMarkersInterval: markerInterval,
                    caretPosition: caretPosition,
                    maxWorkers: Math.Max(1, Environment.ProcessorCount / 2)
                ).ConfigureAwait(false);

                if (token.IsCancellationRequested)
                {
                    bmp.Dispose();
                    return;
                }

                this.DrawSelectionOverlay(bmp, selectionColor);

                if (this.pictureBox_waveform.IsHandleCreated)
                {
                    this.pictureBox_waveform.Invoke((Action)(() =>
                    {
                        this.DisposeCurrentBitmap();
                        this.currentWaveformBitmap = bmp;
                        this.pictureBox_waveform.Image = bmp;
                    }));
                }
                else
                {
                    bmp.Dispose();
                }
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
            catch (Exception ex)
            {
                try { LogCollection.Log(ex); } catch { }
            }
            finally
            {
                if (ReferenceEquals(this.waveformRenderCts, cts))
                {
                    this.waveformRenderCts = null;
                }
            }
        }

        private void DrawSelectionOverlay(Bitmap bmp, Color selectionColor)
        {
            long selectionStart = this.OriginalAudio.SelectionStart;
            long selectionEnd = this.OriginalAudio.SelectionEnd;
            if (selectionStart < 0 || selectionEnd <= selectionStart)
            {
                return;
            }

            int channels = Math.Max(1, this.OriginalAudio.Channels);
            long selStartFrames = selectionStart / channels;
            long selEndFrames = selectionEnd / channels;
            long visibleStart = this.offsetFrames;
            long visibleEnd = visibleStart + (long) bmp.Width * this.samplesPerPixel;

            long drawStart = Math.Max(selStartFrames, visibleStart);
            long drawEnd = Math.Min(selEndFrames, visibleEnd);
            if (drawEnd <= drawStart)
            {
                return;
            }

            float pxStart = (drawStart - visibleStart) / (float) this.samplesPerPixel;
            float pxEnd = (drawEnd - visibleStart) / (float) this.samplesPerPixel;
            using var g = Graphics.FromImage(bmp);
            using var brush = new SolidBrush(Color.FromArgb(80, selectionColor));
            g.FillRectangle(brush, pxStart, 0, Math.Max(1f, pxEnd - pxStart), bmp.Height);
        }

        private void CancelPendingRender()
        {
            var cts = Interlocked.Exchange(ref this.waveformRenderCts, null);
            if (cts == null)
            {
                return;
            }

            try { cts.Cancel(); } catch { }
            try { cts.Dispose(); } catch { }
        }

        private void DisposeCurrentBitmap()
        {
            try
            {
                if (this.currentWaveformBitmap != null)
                {
                    this.currentWaveformBitmap.Dispose();
                    this.currentWaveformBitmap = null;
                }
            }
            catch { }
        }

        private void ApplyVolumeFromScrollbar()
        {
            float vol = this.CurrentVolume;
            this.label_volume.Text = (vol * 100f).ToString("F1") + "%";
            try { this.OriginalAudio.SetVolume(vol); } catch { }
        }

        private float CurrentVolume => 1f - (float) this.vScrollBar_volume.Value / Math.Max(1, this.vScrollBar_volume.Maximum);

        private void vScrollBar_volume_Scroll(object? sender, ScrollEventArgs e)
        {
            this.ApplyVolumeFromScrollbar();
        }

        private void hScrollBar_offset_Scroll(object? sender, ScrollEventArgs e)
        {
            this.OffsetScrollbarScrolled();
        }

        private void OffsetScrollbarScrolled()
        {
            if (this.OriginalAudio.Playing)
            {
                return;
            }

            long newOffset = this.hScrollBar_offset.Value;
            long maxOffset = this.GetMaxOffsetFrames();
            this.offsetFrames = Math.Min(maxOffset, newOffset);
            _ = this.RefreshWaveformAsync();
        }

        private void UpdateOffsetScrollbar()
        {
            long maxOffset = this.GetMaxOffsetFrames();
            long visibleFrames = (long) Math.Max(1, this.pictureBox_waveform.Width) * this.samplesPerPixel;
            var sb = this.hScrollBar_offset;
            int large = (int) Math.Min(int.MaxValue / 4, Math.Max(1, visibleFrames));
            int small = Math.Max(1, large / 10);

            sb.Minimum = 0;
            sb.LargeChange = large;
            sb.SmallChange = small;
            long maxValue = Math.Min(int.MaxValue - large, maxOffset);
            sb.Maximum = (int) (maxValue + large);
            int desired = (int) Math.Clamp(this.offsetFrames, sb.Minimum, sb.Maximum - sb.LargeChange);
            if (sb.Value != desired)
            {
                sb.Value = desired;
            }
            sb.Enabled = maxOffset > 0;
        }

        private long GetTotalFrames()
        {
            return Math.Max(0, this.OriginalAudio.Length / Math.Max(1, this.OriginalAudio.Channels));
        }

        private long GetMaxOffsetFrames()
        {
            long totalFrames = this.GetTotalFrames();
            long visibleFrames = (long) Math.Max(1, this.pictureBox_waveform.Width) * this.samplesPerPixel;
            return Math.Max(0, totalFrames - visibleFrames);
        }

        private long MapPixelToFrameInView(int x)
        {
            int width = Math.Max(1, this.pictureBox_waveform.Width);
            x = Math.Clamp(x, 0, width);
            return this.offsetFrames + (long) x * this.samplesPerPixel;
        }

        private void Wave_MouseWheel(object? sender, MouseEventArgs e)
        {
            if ((ModifierKeys & Keys.Control) != 0)
            {
                int current = this.samplesPerPixel;
                if (e.Delta > 0)
                {
                    int step = Math.Max(1, current / 8);
                    this.samplesPerPixel = Math.Max(MinSamplesPerPixel, current - step);
                }
                else
                {
                    int step = Math.Max(1, current / 6);
                    this.samplesPerPixel = Math.Min(MaxSamplesPerPixel, current + step);
                }
                this.UpdateOffsetScrollbar();
                _ = this.RefreshWaveformAsync();
                return;
            }

            if (this.OriginalAudio.Playing)
            {
                return;
            }

            long visibleFrames = (long) Math.Max(1, this.pictureBox_waveform.Width) * this.samplesPerPixel;
            long stepFrames = Math.Max(1, (long) (visibleFrames * 0.15));
            if (e.Delta > 0)
            {
                this.offsetFrames = Math.Max(0, this.offsetFrames - stepFrames);
            }
            else
            {
                this.offsetFrames = Math.Min(this.GetMaxOffsetFrames(), this.offsetFrames + stepFrames);
            }
            this.UpdateOffsetScrollbar();
            _ = this.RefreshWaveformAsync();
        }

        private void Wave_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                this.dragSelecting = false;
                this.pendingSelect = false;
                return;
            }

            if (this.OriginalAudio.Playing)
            {
                return;
            }

            if (e.Button == MouseButtons.Left)
            {
                long frame = this.MapPixelToFrameInView(e.X);
                this.mouseDownX = e.X;
                this.pendingSelect = true;
                this.dragSelecting = false;
                this.selectStartFrame = frame;
                this.selectEndFrame = frame;
                this.lastClickFrame = frame;
            }
        }

        private void Wave_MouseMove(object? sender, MouseEventArgs e)
        {
            this.mouseX = e.X;
            if (this.OriginalAudio.Playing)
            {
                return;
            }

            if (this.pendingSelect && !this.dragSelecting)
            {
                if (Math.Abs(e.X - this.mouseDownX) >= DragThresholdPx)
                {
                    this.dragSelecting = true;
                }
            }

            if (this.dragSelecting)
            {
                this.selectEndFrame = this.MapPixelToFrameInView(e.X);
                this.UpdateSelection();
                _ = this.RefreshWaveformAsync();
            }
        }

        private void Wave_MouseUp(object? sender, MouseEventArgs e)
        {
            if (this.OriginalAudio.Playing)
            {
                this.pendingSelect = false;
                this.dragSelecting = false;
                return;
            }

            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            if (!this.dragSelecting && this.pendingSelect)
            {
                long frame = this.MapPixelToFrameInView(e.X);
                this.OriginalAudio.SelectionStart = -1;
                this.OriginalAudio.SelectionEnd = -1;
                this.OriginalAudio.SetPosition(frame);
                int channels = Math.Max(1, this.OriginalAudio.Channels);
                this.OriginalAudio.StartingOffset = frame * channels;
                long desiredOffset = frame - this.GetCaretAnchorFrame();
                desiredOffset = Math.Max(0, desiredOffset);
                this.offsetFrames = Math.Min(this.GetMaxOffsetFrames(), desiredOffset);
                this.UpdateOffsetScrollbar();
                _ = this.RefreshWaveformAsync();
            }
            else if (this.dragSelecting)
            {
                this.selectEndFrame = this.MapPixelToFrameInView(e.X);
                this.UpdateSelection();
                _ = this.RefreshWaveformAsync();
            }

            this.pendingSelect = false;
            this.dragSelecting = false;
        }

        private void UpdateSelection()
        {
            long previousSamples = this.GetCurrentSamplePosition();
            long start = Math.Min(this.selectStartFrame, this.selectEndFrame);
            long end = Math.Max(this.selectStartFrame, this.selectEndFrame);
            int channels = Math.Max(1, this.OriginalAudio.Channels);
            if (end - start > 0)
            {
                this.OriginalAudio.SelectionStart = start * channels;
                this.OriginalAudio.SelectionEnd = end * channels;
            }
            else
            {
                this.OriginalAudio.SelectionStart = -1;
                this.OriginalAudio.SelectionEnd = -1;
            }
            this.RecalculateLoopFraction();
            this.ApplyLoopFractionToAudio();
            if (this.OriginalAudio.Playing)
            {
                bool snapped = this.EnsureLoopPosition(previousSamples, false);
                if (snapped)
                {
                    this.AlignViewToCurrentPosition();
                }
                else
                {
                    this.RestoreLoopPlaybackPosition(previousSamples);
                }
            }
        }

        private async void button_playback_Click(object? sender, EventArgs e)
        {
            await this.TogglePlayAsync();
        }

        private async Task TogglePlayAsync()
        {
            if (this.OriginalAudio.Paused)
            {
                await this.OriginalAudio.PauseAsync();
                this.button_playback.Text = "■";
                return;
            }

            if (!this.OriginalAudio.Playing)
            {
                this.ApplyLoopFractionToAudio();
                long startFrame = 0;
                if (this.loopEnabled && this.OriginalAudio.SelectionStart >= 0 && this.OriginalAudio.SelectionEnd > this.OriginalAudio.SelectionStart)
                {
                    startFrame = this.OriginalAudio.SelectionStart / Math.Max(1, this.OriginalAudio.Channels);
                }
                else if (this.OriginalAudio.StartingOffset > 0)
                {
                    startFrame = this.OriginalAudio.StartingOffset / Math.Max(1, this.OriginalAudio.Channels);
                }
                else if (this.lastClickFrame >= 0)
                {
                    startFrame = this.lastClickFrame;
                }
                this.OriginalAudio.SetPosition(startFrame);
                float volume = this.CurrentVolume;
                Action onStopped = () => this.InvokeIfRequired(() => this.button_playback.Text = "▶");
                await this.OriginalAudio.PlayAsync(CancellationToken.None, onStopped, volume);
                this.button_playback.Text = "■";
            }
            else
            {
                await this.OriginalAudio.StopAsync();
                this.button_playback.Text = "▶";
            }
        }

        private async void button_pause_Click(object? sender, EventArgs e)
        {
            await this.TogglePauseAsync();
        }

        private async Task TogglePauseAsync()
        {
            if (this.OriginalAudio.Playing || this.OriginalAudio.Paused)
            {
                await this.OriginalAudio.PauseAsync();
                if (this.OriginalAudio.Paused)
                {
                    this.button_playback.Text = "▶";
                }
            }
            else
            {
                this.ApplyLoopFractionToAudio();
                Action onStopped = () => this.InvokeIfRequired(() => this.button_playback.Text = "▶");
                await this.OriginalAudio.PlayAsync(CancellationToken.None, onStopped, this.CurrentVolume);
                this.button_playback.Text = "■";
            }
        }

        private void ToggleLoop(MouseEventArgs? e)
        {
            long previousSamples = this.GetCurrentSamplePosition();
            if (e != null && (ModifierKeys & Keys.Control) != 0)
            {
                this.loopEnabled = false;
                this.loopDenominator = 0;
                this.button_loop.Text = "↺";
                this.button_loop.ForeColor = Color.Black;
                this.button_loop.Font = new Font("Segoe UI Symbol", 9f, FontStyle.Bold);
                this.OriginalAudio.LoopEnabled = false;
                this.RecalculateLoopFraction();
                this.ApplyLoopFractionToAudio();
                this.ReanchorPlaybackPosition(previousSamples);
                this.AlignViewToCurrentPosition();
                this.RequestWaveformRender();
                return;
            }

            int direction = (e != null && (ModifierKeys & Keys.Shift) != 0) ? -1 : 1;
            if (!this.loopEnabled)
            {
                this.loopEnabled = true;
                if (this.loopDenominator <= 0)
                {
                    this.loopDenominator = 1;
                }
                this.button_loop.Font = new Font("Segoe UI Symbol", 6f, FontStyle.Bold);
            }
            else
            {
                int idx = Array.IndexOf(LoopSteps, this.loopDenominator);
                if (idx < 0)
                {
                    idx = 0;
                }

                idx = (idx + direction) % LoopSteps.Length;
                if (idx < 0)
                {
                    idx += LoopSteps.Length;
                }

                this.loopDenominator = LoopSteps[idx];
            }

            this.button_loop.Text = this.loopDenominator.ToString();
            this.button_loop.ForeColor = Color.Green;
            this.RecalculateLoopFraction();
            this.ApplyLoopFractionToAudio();
            bool snapped = this.EnsureLoopPosition(previousSamples, true);
            if (!snapped)
            {
                this.RestoreLoopPlaybackPosition(previousSamples);
                this.AlignViewToCurrentPosition();
            }
            this.RequestWaveformRender();
        }

        private void RecalculateLoopFraction()
        {
            long totalSamples = this.OriginalAudio.Length;
            long regionStart = 0;
            long regionEnd = Math.Max(1, totalSamples);
            if (this.OriginalAudio.SelectionStart >= 0 && this.OriginalAudio.SelectionEnd > this.OriginalAudio.SelectionStart)
            {
                regionStart = this.OriginalAudio.SelectionStart;
                regionEnd = Math.Min(totalSamples, this.OriginalAudio.SelectionEnd);
            }

            this.loopBaseStartSamples = regionStart;
            this.loopBaseEndSamples = Math.Max(regionStart + 1, regionEnd);

            long regionLen = Math.Max(1, this.loopBaseEndSamples - this.loopBaseStartSamples);
            if (this.loopEnabled && this.loopDenominator > 0)
            {
                this.loopFractionSamples = this.CalculateLoopFractionLength(regionLen);
            }
            else
            {
                this.loopFractionSamples = regionLen;
            }
        }

        private void ApplyLoopFractionToAudio()
        {
            bool enableLoop = this.loopEnabled && this.loopDenominator > 0;
            long baseStart = enableLoop ? this.loopBaseStartSamples : 0;
            long baseEnd = enableLoop ? this.loopBaseEndSamples : this.OriginalAudio.Length;
            long fraction = enableLoop ? Math.Max(1, this.loopFractionSamples) : 0;
            this.OriginalAudio.UpdateLoopFraction(baseStart, baseEnd, fraction, enableLoop, false);
        }

        private bool EnsureLoopPosition(long? sampleToCheck, bool snapViewToLoopStart)
        {
            if (!this.loopEnabled || this.loopDenominator <= 0)
            {
                return false;
            }

            long loopStart = this.loopBaseStartSamples;
            long loopEnd = Math.Min(this.loopBaseEndSamples, loopStart + Math.Max(1, this.loopFractionSamples));
            if (loopEnd <= loopStart)
            {
                return false;
            }

            int channels = Math.Max(1, this.OriginalAudio.Channels);
            long currentSamples = sampleToCheck ?? this.GetCurrentSamplePosition();
            if (currentSamples >= loopStart && currentSamples < loopEnd)
            {
                return false;
            }

            this.OriginalAudio.JumpToSamples(loopStart);
            long loopStartFrame = loopStart / channels;
            this.lastClickFrame = loopStartFrame;

            if (snapViewToLoopStart)
            {
                long desiredOffset = Math.Max(0, loopStartFrame - this.GetCaretAnchorFrame());
                this.offsetFrames = Math.Min(this.GetMaxOffsetFrames(), desiredOffset);
                this.UpdateOffsetScrollbar();
            }

            return true;
        }

        private long GetCurrentSamplePosition()
        {
            int channels = Math.Max(1, this.OriginalAudio.Channels);
            return this.OriginalAudio.Position * channels;
        }

        private void RestoreLoopPlaybackPosition(long? sampleToRestore)
        {
            if (!sampleToRestore.HasValue || !this.loopEnabled || this.loopDenominator <= 0)
            {
                return;
            }

            if (!this.OriginalAudio.Playing)
            {
                return;
            }

            long loopStart = this.loopBaseStartSamples;
            long loopEnd = Math.Min(this.loopBaseEndSamples, loopStart + Math.Max(1, this.loopFractionSamples));
            if (loopEnd <= loopStart)
            {
                return;
            }

            long desiredSamples = Math.Clamp(sampleToRestore.Value, loopStart, loopEnd - 1);
            this.OriginalAudio.JumpToSamples(desiredSamples);
        }

        private void ReanchorPlaybackPosition(long sampleToRestore)
        {
            if (sampleToRestore < 0)
            {
                return;
            }

            this.OriginalAudio.JumpToSamples(sampleToRestore);
            int channels = Math.Max(1, this.OriginalAudio.Channels);
            this.lastClickFrame = sampleToRestore / channels;
        }

        private long CalculateLoopFractionLength(long regionLength)
        {
            if (regionLength <= 0)
            {
                return 0;
            }

            if (this.loopDenominator <= 1)
            {
                return regionLength;
            }

            double fraction = Math.Ceiling(regionLength / (double) this.loopDenominator);
            return Math.Max(1, (long) fraction);
        }

        private void AlignViewToCurrentPosition()
        {
            long frame = this.OriginalAudio.Position;
            long desiredOffset = frame - this.GetCaretAnchorFrame();
            desiredOffset = Math.Max(0, desiredOffset);
            this.offsetFrames = Math.Min(this.GetMaxOffsetFrames(), desiredOffset);
            this.UpdateOffsetScrollbar();
        }

        private long GetCaretAnchorFrame()
        {
            int width = Math.Max(1, this.pictureBox_waveform.Width - 1);
            float normalized = this.GetCaretAnchorNormalized();
            int caretPx = (int) Math.Round(width * normalized);
            return (long) caretPx * this.samplesPerPixel;
        }

        private float GetCaretAnchorNormalized()
        {
            float raw = this.Settings.CaretPosition; // -1 .. 1
            float normalized = (raw + 1f) * 0.5f;
            return Math.Clamp(normalized, 0f, 1f);
        }

        private float GetCaretNormalizedPosition(long maxOffset)
        {
            int width = Math.Max(1, this.pictureBox_waveform.Width - 1);
            long position = this.OriginalAudio.Position;
            long visibleStart = this.offsetFrames;
            long visibleEnd = visibleStart + (long) width * this.samplesPerPixel;

            if (position <= visibleStart)
            {
                return 0f;
            }

            if (position >= visibleEnd)
            {
                return 1f;
            }

            double px = (position - visibleStart) / (double) Math.Max(1, this.samplesPerPixel);
            return (float) Math.Clamp(px / width, 0.0, 1.0);
        }

        private void TrackView_SizeChanged(object? sender, EventArgs e)
        {
            int left = this.pictureBox_waveform.Left;
            int nonWaveWidth = Math.Max(0, this.designerClientWidth - this.designerWaveWidth);
            int desiredWidth = Math.Max(1, this.ClientSize.Width - nonWaveWidth);

            this.pictureBox_waveform.Width = desiredWidth;
            this.hScrollBar_offset.Left = left;
            this.hScrollBar_offset.Width = Math.Max(0, desiredWidth);

            this.UpdateOffsetScrollbar();
            this.RequestWaveformRender();
        }

        private async Task StopPlaybackAsync()
        {
            var cts = Interlocked.Exchange(ref this.playbackCts, null);
            if (cts != null)
            {
                try { cts.Cancel(); } catch { }
                try { cts.Dispose(); } catch { }
            }

            try { await this.OriginalAudio.StopAsync().ConfigureAwait(false); } catch { }
            this.InvokeIfRequired(() => this.button_playback.Text = "▶");
        }

        private async void TrackView_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.C)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                await this.CopySelectionAsync();
                return;
            }

            if (e.KeyCode == Keys.Delete)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                await this.RemoveSelectionAsync();
                return;
            }

            if (e.KeyCode == Keys.Space)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                this.button_pause.PerformClick();
            }
            else if (e.KeyCode == Keys.Back)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                if (e.Control)
                {
                    await this.ResetStartingPointAsync();
                }
                else
                {
                    await this.RestartPlaybackFromStartAsync();
                }
            }
        }

        private async Task ResetStartingPointAsync()
        {
            await this.OriginalAudio.StopAsync();
            this.OriginalAudio.StartingOffset = 0;
            this.OriginalAudio.SetPosition(0);
            this.lastClickFrame = 0;
            this.offsetFrames = 0;
            this.UpdateOffsetScrollbar();
            await this.RefreshWaveformAsync();
            this.InvokeIfRequired(() => this.button_playback.Text = "▶");
        }

        private async Task RestartPlaybackFromStartAsync()
        {
            await this.OriginalAudio.StopAsync();
            int channels = Math.Max(1, this.OriginalAudio.Channels);
            long startFrame = this.OriginalAudio.StartingOffset > 0
                ? this.OriginalAudio.StartingOffset / channels
                : 0;
            this.OriginalAudio.SetPosition(startFrame);
            this.lastClickFrame = startFrame;

            long desiredOffset = Math.Max(0, startFrame - this.GetCaretAnchorFrame());
            this.offsetFrames = Math.Min(this.GetMaxOffsetFrames(), desiredOffset);
            this.UpdateOffsetScrollbar();
            await this.RefreshWaveformAsync();
            await this.TogglePlayAsync();
        }

        private void checkBox_settings_CheckedChanged(object? sender, EventArgs e)
        {
            if (this.suppressSettingsCheckbox)
            {
                return;
            }

            bool visible = this.checkBox_settings.Checked;
            if (visible)
            {
                this.PositionSettingsWindow();
                this.Settings.Show();
            }
            else
            {
                this.Settings.Hide();
            }

            this.RequestWaveformRender();
        }

        internal void SyncSettingsCheckbox(bool visible)
        {
            if (this.checkBox_settings.Checked == visible)
            {
                return;
            }

            this.suppressSettingsCheckbox = true;
            this.checkBox_settings.Checked = visible;
            this.suppressSettingsCheckbox = false;
        }

        private async Task CopySelectionAsync()
        {
            if (!this.HasValidSelection())
            {
                LogCollection.Log("No active selection to copy.");
                return;
            }

            AudioObj? clip = await this.OriginalAudio.CloneFromSelectionAsync().ConfigureAwait(true);
            if (clip == null)
            {
                LogCollection.Log("Copy selection failed.");
                return;
            }

            clip.Name = this.GenerateClipName();

            WindowMain.InvokeIfRequired(() =>
            {
                global::ModularAudience.Forms.AudioCollectionView? targetView = WindowMain.CollectionViews.FirstOrDefault(cv => cv != null && !cv.IsDisposed);
                if (targetView == null)
                {
                    targetView = new global::ModularAudience.Forms.AudioCollectionView(new[] { clip });
                    WindowMain.CollectionViews.Add(targetView);
                    targetView.Show();
                }
                else
                {
                    targetView.AudioC.Audios.Add(clip);
                }

                WindowMain.UpdateCollectionTag(clip, targetView);
                LogCollection.Log($"Selection copied to '{clip.Name}'.");
            });
        }

        private async Task RemoveSelectionAsync()
        {
            if (!this.HasValidSelection())
            {
                return;
            }

            if (this.OriginalAudio.Playing)
            {
                LogCollection.Log("Stop playback before removing audio.");
                return;
            }

            await this.OriginalAudio.EraseSelectionAsync().ConfigureAwait(true);
            this.ClearSelectionMarkers();
            this.RecalculateLoopFraction();
            this.ApplyLoopFractionToAudio();
            this.AlignViewToCurrentPosition();
            this.UpdateOffsetScrollbar();
            this.RequestWaveformRender();
            LogCollection.Log("Selection removed from track view.");
        }

        private void ClearSelectionMarkers()
        {
            this.selectStartFrame = -1;
            this.selectEndFrame = -1;
            this.OriginalAudio.SelectionStart = -1;
            this.OriginalAudio.SelectionEnd = -1;
            this.pendingSelect = false;
            this.dragSelecting = false;
        }

        private bool HasValidSelection()
        {
            return this.OriginalAudio.SelectionStart >= 0 && this.OriginalAudio.SelectionEnd > this.OriginalAudio.SelectionStart;
        }

        private string GenerateClipName()
        {
            string baseName = string.IsNullOrWhiteSpace(this.OriginalAudio.Name) ? "clip" : this.OriginalAudio.Name;
            int number = Interlocked.Increment(ref selectionCopySeed);
            return $"{baseName}_clip_{number:D3}";
        }

        private void contextMenu_waveform_Opening(object? sender, CancelEventArgs e)
        {
            bool hasSelection = this.HasValidSelection();
            this.menuItem_copySelection.Enabled = hasSelection;
            this.menuItem_removeSelection.Enabled = hasSelection && !this.OriginalAudio.Playing;
        }

        private async void menuItem_copySelection_Click(object? sender, EventArgs e)
        {
            await this.CopySelectionAsync();
        }

        private async void menuItem_removeSelection_Click(object? sender, EventArgs e)
        {
            await this.RemoveSelectionAsync();
        }

        private void InvokeIfRequired(Action action)
        {
            if (this.IsDisposed)
            {
                return;
            }

            if (this.InvokeRequired)
            {
                try { this.BeginInvoke(action); } catch { }
            }
            else
            {
                try { action(); } catch { }
            }
        }

        public async Task ApplyStretchedAudioAsync(AudioObj result)
        {
            if (result == null)
            {
                return;
            }

            await this.StopPlaybackAsync();
            this.CancelPendingRender();

            var original = this.OriginalAudio;
            float[] newData = result.Data ?? [];
            original.Data = newData;
            original.SampleRate = result.SampleRate;
            original.Channels = result.Channels;
            original.BitDepth = result.BitDepth;
            original.Bpm = result.Bpm;
            original.ScannedBpm = result.ScannedBpm;
            original.ScannedTiming = result.ScannedTiming;
            original.ScannedKey = result.ScannedKey;
            original.Timing = result.Timing;
            original.Volume = result.Volume;
            original.ChunkSize = result.ChunkSize;
            original.OverlapSize = result.OverlapSize;
            original.StretchFactor = result.StretchFactor;
            original.SampleTag = result.SampleTag;
            original.ScrollOffset = 0;
            original.StartingOffset = 0;
            original.SelectionStart = -1;
            original.SelectionEnd = -1;
            original.LoopEnabled = false;

            long sampleCount = newData.LongLength;
            original.Length = sampleCount;
            int channels = Math.Max(1, original.Channels);
            int sampleRate = Math.Max(1, original.SampleRate);
            original.Duration = TimeSpan.FromSeconds(sampleCount / (double) (sampleRate * channels));

            original.Metrics.Clear();
            foreach (var metric in result.Metrics)
            {
                original.Metrics[metric.Key] = metric.Value;
            }

            this.OriginalAudio.SetPosition(0);
            this.offsetFrames = 0;
            this.lastClickFrame = 0;
            this.selectStartFrame = -1;
            this.selectEndFrame = -1;
            this.pendingSelect = false;
            this.dragSelecting = false;
            this.loopEnabled = false;
            this.loopDenominator = 0;
            this.loopFractionSamples = 0;
            this.button_loop.Text = "↺";
            this.button_loop.ForeColor = Color.Black;
            this.button_loop.Font = new Font("Segoe UI Symbol", 9f, FontStyle.Bold);

            this.RecalculateLoopFraction();
            this.ApplyLoopFractionToAudio();
            this.UpdateOffsetScrollbar();
            await this.RefreshWaveformAsync();
            this.UpdateTimeDisplay();
        }
    }
}
