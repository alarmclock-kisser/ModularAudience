using MathNet.Numerics.Optimization.TrustRegion;
using Microsoft.VisualBasic.Devices;
using ModularAudience.Audio;
using ModularAudience.Audio.Processing;
using ModularAudience.Audio.Processors_V1;
using ModularAudience.Audio.Processors_V2;
using ModularAudience.Audio.Processors_V4;
using ModularAudience.Forms.Helpers;
using System.ComponentModel;
using System.Media;
using System.Reflection;
using System.Runtime;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Timer = System.Windows.Forms.Timer;

namespace ModularAudience.Forms.Modules
{
    public partial class TrackView : Form
    {
        private const int DragThresholdPx = 2;
        private static readonly int[] LoopSteps = [1, 2, 4, 8, 16, 32, 64];
        private const int MaxSamplesPerPixel = 65536;
        private const int MinSamplesPerPixel = 1;
        private static int selectionCopySeed;

        public readonly int TrackViewId;
        public readonly AudioObj OriginalAudio;
        private readonly AudioCollection? sourceCollection;
        internal AudioCollection? SourceCollection => WindowMain.CollectionViews.FirstOrDefault(cv => cv.AudioC != null && cv.AudioC.Audios.Any(a => a.Id == this.OriginalAudio.Id))?.AudioC;
        public readonly TrackViewSettings Settings;


        private float CurrentVolume => 1f - (float) this.vScrollBar_volume.Value / Math.Max(1, this.vScrollBar_volume.Maximum);
        internal bool Synced => this.checkBox_sync.Checked;
        internal bool Muted => this.checkBox_mute.Checked;
        internal bool Soloed => this.checkBox_solo.Checked;

		private readonly Timer frameTimer;
        private bool frameBusy;

        private CancellationTokenSource? waveformRenderCts;
        private Bitmap? currentWaveformBitmap;
        private long renderTickCount;

        private CancellationTokenSource? playbackCts;

        private int samplesPerPixel = 512;
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
        private int _lastRateContextMenuValue;
        private readonly int designerClientWidth;
        private readonly int designerWaveWidth;

        public TrackView(AudioObj audio, AudioCollection? sourceCollection = null)
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

            this.sourceCollection = sourceCollection;

            this.KeyPreview = true;
            this.KeyDown += this.TrackView_KeyDown;

            this.ApplySettingsAppearance();

            // Setze LastSelectedTrackView bei Aktivierung, Fokus oder Klick auf die Form
            this.Activated += (_, __) => this.SetAsLastSelected();
            this.GotFocus += (_, __) => this.SetAsLastSelected();
            this.MouseDown += (_, __) => this.SetAsLastSelected();
            this.RegisterInteractionEvents(this);
            this.SizeChanged += this.TrackView_SizeChanged;
            this.TrackViewId = WindowMain.TrackViewIds.OrderBy(id => id).Select((id, index) => new { id, index }).FirstOrDefault(pair => pair.id > pair.index)?.index ?? WindowMain.TrackViewIds.Count;

            WindowMain.TrackViewIds.Add(this.TrackViewId);
            this.Text = "#" + this.TrackViewId.ToString("D2") + " - " + audio.Name;
            this.OriginalAudio.SelectionStart = -1;
            this.OriginalAudio.SelectionEnd = -1;
            this.OriginalAudio.LoopEnabled = false;

            this.EnablePictureBoxDoubleBuffering();
            this.InitializeTrackControls();
            // this.ApplyInitialTrackSizing();
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
            WindowMainStaticHelpers.InvokeIfRequired(WindowMain.Instance, () =>
                {
                    WindowMain.TrackViews.Remove(this);
                    WindowMain.TrackViewIds.Remove(this.TrackViewId);
                });
            };

            this.FormClosed += (_, __) =>
            {
                this.DisposeCurrentBitmap();
                // Erzwingt eine Kompaktierung und einen tiefen GC-Sweep
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(2, GCCollectionMode.Forced, true);
            };

            WindowMain.TrackViews.Add(this);

            this.Show();
        }


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

        private void SetAsLastSelected()
        {
            WindowMain.LastSelectedTrackView = this;

            var collectionView = WindowMain.CollectionViews.FirstOrDefault(cv => cv.AudioC == this.SourceCollection && !cv.IsDisposed && cv.Visible);
            if (collectionView != null)
            {
                // Select only the exact track in the listBox, deselect all others
                // collectionView.SetSelectionToAudio(this.OriginalAudio);
            }
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

            this.hScrollBar_rate.Minimum = -500;
            this.hScrollBar_rate.Maximum = 500;
            this.hScrollBar_rate.SmallChange = 1;
            this.hScrollBar_rate.LargeChange = 1;
            this.SetPlaybackRateFromScrollbar(0, updateScrollbar: true, fireAndForget: false);

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
            int maxWidth = Math.Max(minWidth, Math.Min(900, workingArea.Width - 20)); // 1080 = Designer-Default
            desiredWidth = Math.Clamp(desiredWidth, minWidth, maxWidth);

            // Add 10% clearance on the right end for initial view
            if (desiredWidth == maxWidth)
            {
                desiredWidth = (int) (maxWidth * 0.9);
            }

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

            var location = new Point(this.Location.X + this.Width + 2, this.Location.Y);
            this.Settings.Location = location;
        }

        internal void ApplySettingsAppearance()
        {
            try
            {
                this.pictureBox_waveform.BackColor = this.Settings.ColorBack;
                // this.BackColor = this.Settings.GetShadedColor(this.Settings.ColorBack, 0.95f);
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
                int width = Math.Max(1, this.pictureBox_waveform.Width);
                int height = Math.Max(1, this.pictureBox_waveform.Height);

                // Calculate visible frames and clamp offset so the waveform rendering routine always draws a valid range
                long visibleFrames = (long) width * this.samplesPerPixel;
                long totalFrames = this.GetTotalFrames();
                long maxNormalOffset = Math.Max(0, totalFrames - visibleFrames);

                // clampOffset is where we actually ask the audio renderer to start drawing from
                long clampOffsetFrames = Math.Min(this.offsetFrames, maxNormalOffset);

                // If caller offset is beyond clamp, compute how many pixels we must shift the rendered image to the left
                long extraShiftSamples = Math.Max(0, this.offsetFrames - clampOffsetFrames);
                int extraShiftPixels = (int) Math.Round(extraShiftSamples / (double) Math.Max(1, this.samplesPerPixel));

                // Compute caret position relative to the clamped render offset
                long position = this.OriginalAudio.Position;
                double caretPx = (position - clampOffsetFrames) / (double) Math.Max(1, this.samplesPerPixel);
                float caretNormalizedForRender = (float) Math.Clamp(caretPx / Math.Max(1, width), 0.0, 1.0);

                var bmp = await this.OriginalAudio.DrawWaveformAsync(
                    width: width,
                    height: height,
                    samplesPerPixel: this.samplesPerPixel,
                    drawEachChannel: drawChannels,
                    caretWidth: caretWidth,
                    offset: clampOffsetFrames,
                    waveColor: waveColor,
                    backColor: backColor,
                    caretColor: caretColor,
                    smoothen: smooth,
                    timingMarkersInterval: markerInterval,
                    caretPosition: caretNormalizedForRender,
                    maxWorkers: Math.Max(1, Environment.ProcessorCount / 2)
                ).ConfigureAwait(false);

                if (token.IsCancellationRequested)
                {
                    bmp.Dispose();
                    return;
                }

                Bitmap finalBmp = bmp;

                // If we rendered at a clamped offset but the requested offset is further right, shift the image so the waveform moves left
                if (extraShiftPixels > 0)
                {
                    try
                    {
                        var shifted = new Bitmap(width, height);
                        using (var g = Graphics.FromImage(shifted))
                        {
                            // Fill background with backColor so the extra area on the right remains empty
                            using var backBrush = new SolidBrush(backColor);
                            g.FillRectangle(backBrush, 0, 0, width, height);

                            // Draw the originally rendered waveform shifted to the left
                            g.DrawImage(bmp, -extraShiftPixels, 0);
                        }

                        // Replace final bitmap and dispose the intermediate one
                        bmp.Dispose();
                        finalBmp = shifted;
                    }
                    catch
                    {
                        // If shifting fails, keep original bmp as fallback
                        // ensure bmp remains assigned to finalBmp
                    }
                }

                if (token.IsCancellationRequested)
                {
                    try { finalBmp.Dispose(); } catch { }
                    return;
                }

                // Draw selection overlay on the final bitmap using the actual requested offset (this.offsetFrames)
                try
                {
                    this.DrawSelectionOverlay(finalBmp, selectionColor);
                }
                catch { }

                if (this.pictureBox_waveform.IsHandleCreated)
                {
                    this.pictureBox_waveform.Invoke((Action) (() =>
                    {
                        this.DisposeCurrentBitmap();
                        this.currentWaveformBitmap = finalBmp;
                        this.pictureBox_waveform.Image = finalBmp;
                    }));
                }
                else
                {
                    finalBmp.Dispose();
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

            // Zeichnen der transparenten Auswahl
            // - Innerhalb der Track-Dauer: Standard-Farbe (sättigter, halbtransparenter Bereich)
            // - Außerhalb der Track-Dauer (vor 0 oder nach Ende): hellere / andere Färbung zur Visualisierung
            {
                // Auswahl in Samples im AudioObj gespeichert => in Frames umrechnen
                long selStartSamples = this.OriginalAudio.SelectionStart;
                long selEndSamples = this.OriginalAudio.SelectionEnd;
                if (selStartSamples < 0 || selEndSamples <= selStartSamples)
                {
                    // ungültig (sollte oben bereits abgefangen sein), aber sicherheitshalber nichts zeichnen
                    return;
                }

                long startSamples = Math.Min(selStartSamples, selEndSamples);
                long endSamples = Math.Max(selStartSamples, selEndSamples);

                long startFrame = startSamples / channels;
                long endFrame = endSamples / channels;

                // Gesamtanzahl Frames des Tracks (in Frame-Einheiten)
                long totalFrames = this.GetTotalFrames();

                // Bereiche: vor Track-Start, innerhalb Track, nach Track-Ende
                long preTrackStart = Math.Min(startFrame, 0);
                long preTrackEnd = Math.Min(endFrame, 0);

                long insideStart = Math.Max(startFrame, 0);
                long insideEnd = Math.Min(endFrame, totalFrames);

                long postStart = Math.Max(startFrame, totalFrames);
                long postEnd = Math.Max(endFrame, totalFrames);

                // Hilfsfunktion: Frame-Bereich in sichtbare Pixel (und Clip)
                int clipWidth = this.pictureBox_waveform.Width;
                int clipHeight = bmp.Height; // <-- Fix: 'height' durch 'bmp.Height' ersetzt

                // Brushes: normal und "outside" (heller / dezenter)
                using var insideBrush = new SolidBrush(Color.FromArgb(60, Color.DeepSkyBlue)); // wie zuvor
                using var outsideBrush = new SolidBrush(Color.FromArgb(40, Color.LightSkyBlue)); // dezenter, hellerer Ton

                // Zeichne Bereich innerhalb Track
                if (insideEnd > insideStart)
                {
                    pxStart = this.FrameToPixel(insideStart);
                    pxEnd = this.FrameToPixel(insideEnd);
                    int x = (Int32) pxStart;
                    int w = (Int32) Math.Max(1, pxEnd - pxStart);

                    int drawX = Math.Max(0, x);
                    int drawWidth = Math.Min(clipWidth, x + w) - drawX;
                    if (drawWidth > 0 && drawX < clipWidth)
                    {
                        g.FillRectangle(insideBrush, drawX, 0, drawWidth, clipHeight);
                    }
                }

                // Zeichne Bereich vor Track-Start (falls vorhanden) als "outside"
                if (startFrame < 0 && endFrame > 0)
                {
                    // Teil vor 0 bis min(endFrame,0)
                    int pxPreStart = this.FrameToPixel(Math.Max(startFrame, preTrackStart));
                    int pxPreEnd = this.FrameToPixel(Math.Min(endFrame, 0));
                    int xPre = pxPreStart;
                    int wPre = Math.Max(1, pxPreEnd - pxPreStart);
                    int drawXPre = Math.Max(0, xPre);
                    int drawWidthPre = Math.Min(clipWidth, xPre + wPre) - drawXPre;
                    if (drawWidthPre > 0 && drawXPre < clipWidth)
                    {
                        g.FillRectangle(outsideBrush, drawXPre, 0, drawWidthPre, clipHeight);
                    }
                }
                else if (endFrame <= 0)
                {
                    // komplette Auswahl vor Track-Start
                    int pxPreStart = this.FrameToPixel(startFrame);
                    int pxPreEnd = this.FrameToPixel(endFrame);
                    int xPre = pxPreStart;
                    int wPre = Math.Max(1, pxPreEnd - pxPreStart);
                    int drawXPre = Math.Max(0, xPre);
                    int drawWidthPre = Math.Min(clipWidth, xPre + wPre) - drawXPre;
                    if (drawWidthPre > 0 && drawXPre < clipWidth)
                    {
                        g.FillRectangle(outsideBrush, drawXPre, 0, drawWidthPre, clipHeight);
                    }
                }

                // Zeichne Bereich nach Track-Ende (falls vorhanden) als "outside"
                if (postEnd > postStart)
                {
                    int pxPostStart = this.FrameToPixel(postStart);
                    int pxPostEnd = this.FrameToPixel(postEnd);
                    int xPost = pxPostStart;
                    int wPost = Math.Max(1, pxPostEnd - pxPostStart);
                    int drawXPost = Math.Max(0, xPost);
                    int drawWidthPost = Math.Min(clipWidth, xPost + wPost) - drawXPost;
                    if (drawWidthPost > 0 && drawXPost < clipWidth)
                    {
                        g.FillRectangle(outsideBrush, drawXPost, 0, drawWidthPost, clipHeight);
                    }
                }
            }
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
            this.OriginalAudio.Volume = vol * 100f;
            this.ApplyAudibilityState(vol);
        }

        internal bool IsEffectivelyMuted()
        {
            bool anySoloActive = WindowMain.TrackViews.Any(tv => tv != null && !tv.IsDisposed && tv.Soloed);
            return this.Muted || (anySoloActive && !this.Soloed);
        }

        internal float GetEffectivePlaybackVolume()
        {
            return this.IsEffectivelyMuted() ? 0f : Math.Clamp(this.CurrentVolume, 0f, 1f);
        }

        internal void ApplyAudibilityState()
        {
            this.ApplyAudibilityState(this.GetEffectivePlaybackVolume());
        }

        private void ApplyAudibilityState(float baseVolume)
        {
            try { this.OriginalAudio.SetPlaybackVolume(Math.Clamp(baseVolume, 0f, 1f)); } catch { }
        }

        // Beispiel: neuen Parameter hinzufügen und reentrancy-flag verwenden
        private bool suppressVolumeSync;
        private bool suppressRateSync;
        private int lastAppliedRateScrollbarValue = int.MinValue;

        private async Task FadeInCurrentPlaybackAsync(float targetVolume, int steps = 3, int durationMs = 12)
        {
            targetVolume = Math.Clamp(targetVolume, 0f, 1f);
            steps = Math.Max(1, steps);
            durationMs = Math.Max(1, durationMs);

            this.OriginalAudio.SetVolume(0f);

            for (int i = 1; i <= steps; i++)
            {
                float nextVolume = targetVolume * i / steps;
                this.OriginalAudio.SetVolume(nextVolume);
                await Task.Delay(Math.Max(1, durationMs / steps)).ConfigureAwait(false);
            }
        }

        internal void SetVolumeSynced(int scrollbarValue, bool muted, bool broadcast = true)
        {
            if (this.IsDisposed)
            {
                return;
            }

            if (this.suppressVolumeSync)
            {
                return;
            }

            bool doBroadcast = broadcast && !ModifierKeys.HasFlag(Keys.Control);

            this.suppressVolumeSync = true;
            try
            {
                int clamped = Math.Clamp(scrollbarValue, this.vScrollBar_volume.Minimum, this.vScrollBar_volume.Maximum);
                if (this.vScrollBar_volume.Value != clamped)
                {
                    this.vScrollBar_volume.Value = clamped;
                }

                if (this.checkBox_mute.Checked != muted)
                {
                    this.checkBox_mute.Checked = muted;
                }

                float vol = this.CurrentVolume;
                this.label_volume.Text = (vol * 100f).ToString("F1") + "%";
                this.OriginalAudio.Volume = vol * 100f;
                this.ApplyAudibilityState(vol);

                if (doBroadcast)
                {
                    foreach (var tv in WindowMain.SyncedTrackViews.Where(tv => tv != this && !tv.IsDisposed))
                    {
                        // verhindere, dass Empfänger erneut broadcastet
                        tv.SetVolumeSynced(clamped, muted, broadcast: false);
                    }
                }
            }
            finally
            {
                this.suppressVolumeSync = false;
            }
        }

        private void vScrollBar_volume_Scroll(object? sender, ScrollEventArgs e)
        {
            if (this.OriginalAudio.Playing || this.OriginalAudio.Paused)
            {
				this.SetVolumeSynced(this.vScrollBar_volume.Value, this.checkBox_mute.Checked);
			}
		}

        private void checkBox_mute_CheckedChanged(object sender, EventArgs e)
        {
            this.SetVolumeSynced(this.vScrollBar_volume.Value, this.checkBox_mute.Checked);
            WindowMain.Instance?.RefreshTrackAudibility();
        }

        private void checkBox_solo_CheckedChanged(object? sender, EventArgs e)
        {
            WindowMain.Instance?.RefreshTrackAudibility();
        }

        private void hScrollBar_rate_Scroll(object? sender, ScrollEventArgs e)
        {
            this.SetPlaybackRateSynced(e.NewValue);
        }

        private void hScrollBar_rate_ValueChanged(object? sender, EventArgs e)
        {
            this.SetPlaybackRateSynced(this.hScrollBar_rate.Value);
        }

        private void hScrollBar_rate_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                this._lastRateContextMenuValue = this.GetRateScrollbarValueFromMouseX(e.X);
                return;
            }

            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            if ((ModifierKeys & Keys.Control) == Keys.Control)
            {
                if (this.hScrollBar_rate.Value != 0)
                {
                    this.hScrollBar_rate.Value = 0;
                }
                this.SetPlaybackRateSynced(0);
                return;
            }

            this.SetPlaybackRateSynced(this.GetRateScrollbarValueFromMouseX(e.X));
        }

        private void menuItem_rateResetCenter_Click(object? sender, EventArgs e)
        {
            if (this.hScrollBar_rate.Value != 0)
            {
                this.hScrollBar_rate.Value = 0;
            }

            this.SetPlaybackRateSynced(0);
        }

        private void contextMenu_rate_Opening(object? sender, CancelEventArgs e)
        {
            float factor = MapRateScrollbarToFactor(this._lastRateContextMenuValue);
            this.menuItem_rateJumpHere.Text = $"Jump here ({factor * 100f:F1}%)";
        }

        private void menuItem_rateJumpHere_Click(object? sender, EventArgs e)
        {
            this.SetPlaybackRateSynced(this._lastRateContextMenuValue);
        }

        internal void SetPlaybackRateSynced(int scrollbarValue, bool broadcast = true)
        {
            if (this.IsDisposed)
            {
                return;
            }

            if (this.suppressRateSync)
            {
                return;
            }

            bool doBroadcast = broadcast && this.Synced && !ModifierKeys.HasFlag(Keys.Control);

            this.suppressRateSync = true;
            try
            {
                bool changed = this.SetPlaybackRateFromScrollbar(scrollbarValue, updateScrollbar: true, fireAndForget: true);

                if (doBroadcast && changed)
                {
                    foreach (var tv in WindowMain.SyncedTrackViews.Where(tv => tv != this && !tv.IsDisposed))
                    {
                        tv.SetPlaybackRateSynced(scrollbarValue, broadcast: false);
                    }
                }
            }
            finally
            {
                this.suppressRateSync = false;
            }
        }

        private bool SetPlaybackRateFromScrollbar(int scrollbarValue, bool updateScrollbar, bool fireAndForget)
        {
            int clampedValue = Math.Clamp(scrollbarValue, this.hScrollBar_rate.Minimum, this.hScrollBar_rate.Maximum);
            if (updateScrollbar && this.hScrollBar_rate.Value != clampedValue)
            {
                this.hScrollBar_rate.Value = clampedValue;
            }

            float factor = MapRateScrollbarToFactor(clampedValue);
            this.label_info_rate.Text = $"Rate: {factor * 100f:F1}%";

            bool changed = this.lastAppliedRateScrollbarValue != clampedValue;
            this.lastAppliedRateScrollbarValue = clampedValue;
            this.OriginalAudio.ManualSampleRateFactor = factor;

            if (fireAndForget)
            {
                if (changed)
                {
                    _ = this.ApplyPlaybackRateAsync();
                }

                return changed;
            }

            this.OriginalAudio.SampleRateFactor = Math.Clamp(this.OriginalAudio.ManualSampleRateFactor * this.OriginalAudio.SyncNudgeSampleRateFactor, 0.5, 2.0);
            return changed;
        }

        private static float MapRateScrollbarToFactor(int scrollbarValue)
        {
            double normalized = Math.Clamp(scrollbarValue / 500.0, -1.0, 1.0);
            double factor = Math.Pow(2.0, normalized);
            return (float) factor;
        }

        private int GetRateScrollbarValueFromMouseX(int mouseX)
        {
            int width = Math.Max(1, this.hScrollBar_rate.ClientSize.Width - 1);
            double fraction = Math.Clamp(mouseX / (double) width, 0.0, 1.0);
            int min = this.hScrollBar_rate.Minimum;
            int max = this.hScrollBar_rate.Maximum;
            return min + (int) Math.Round((max - min) * fraction);
        }

        private async Task ApplyPlaybackRateAsync()
        {
            try
            {
                await this.OriginalAudio.ApplyCombinedSampleRateAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                try { LogCollection.Log(ex); } catch { }
            }
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
            // Anzahl der sichtbaren Frames in der PictureBox (wie bisher)
            long visibleFrames = (long) Math.Max(1, this.pictureBox_waveform.Width) * this.samplesPerPixel;

            // Zusätzlicher visueller Puffer am Ende: 20% der sichtbaren Breite
            const double extraFraction = 0.20;
            long extraFrames = (long) Math.Round(visibleFrames * extraFraction);

            // Erlaube Scrolling bis (end-of-samples) + extraFrames, aber never negative
            long max = totalFrames - visibleFrames + extraFrames;
            return Math.Max(0, max);
        }

        private long MapPixelToFrameInView(int x)
        {
            int width = Math.Max(1, this.pictureBox_waveform.Width);
            x = Math.Clamp(x, 0, width);
            return this.offsetFrames + (long) x * this.samplesPerPixel;
        }

        private void Wave_MouseWheel(object? sender, MouseEventArgs e)
        {
            // Zoom mit Ctrl: samplesPerPixel ändern, Zoom um den Cursor herum (Sample unter Cursor bleibt an gleicher Pixel-Position)
            if ((ModifierKeys & Keys.Control) != 0)
            {
                int current = this.samplesPerPixel;
                int newSamplesPerPixel;
                if (e.Delta > 0)
                {
                    int step = Math.Max(1, current / 8);
                    newSamplesPerPixel = Math.Max(MinSamplesPerPixel, current - step);
                }
                else
                {
                    int step = Math.Max(1, current / 6);
                    newSamplesPerPixel = Math.Min(MaxSamplesPerPixel, current + step);
                }

                if (newSamplesPerPixel != current)
                {
                    // Bestimme X relativ zur PictureBox (sicher clamped)
                    int width = Math.Max(1, this.pictureBox_waveform.Width);
                    int localX = Math.Clamp(e.X, 0, width - 1);

                    // Sample unter dem Cursor vor dem Zoom
                    long sampleAtCursor = this.offsetFrames + (long) localX * current;

                    // Setze neuen Zoom
                    this.samplesPerPixel = newSamplesPerPixel;

                    // Berechne offset so dass sampleAtCursor wieder unter localX landet
                    long desiredOffset = sampleAtCursor - (long) localX * this.samplesPerPixel;
                    desiredOffset = Math.Max(0, desiredOffset);
                    this.offsetFrames = Math.Min(this.GetMaxOffsetFrames(), desiredOffset);

                    this.UpdateOffsetScrollbar();
                    this.pictureBox_waveform.Invalidate();
                }

                return;
            }

            // Ohne Ctrl: normaler Scroll (verschiebt die Ansicht)
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
            this.pictureBox_waveform.Invalidate();
        }

        private void Wave_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                this.dragSelecting = false;
                this.pendingSelect = false;
                return;
            }

            if (e.Button == MouseButtons.Left)
            {
                long frame = this.MapPixelToFrameInView(e.X);

                // Clamp frame to valid range [0, totalFrames]
                long totalFrames = this.GetTotalFrames();
                frame = Math.Clamp(frame, 0L, Math.Max(0L, totalFrames));

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

            // 1. Erlaube Dragging auch während Playing
            // (if (this.OriginalAudio.Playing) return;  <-- DAS ENTFERNEN

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

                // --- AUTO-SCROLL LOGIK ---
                // Wenn wir ziehen und der Cursor an den rechten oder linken Rand der PictureBox stößt,
                // schiebe den Viewport mit.
                int width = Math.Max(1, this.pictureBox_waveform.Width);
                if (e.X >= width - 1 || e.X <= 0)
                {
                    // Berechne wie weit wir über den Rand hinaus sind
                    long overflow = (e.X >= width - 1) ? (e.X - (width - 1)) : (0 - e.X);

                    // Wandle das Pixel-Overflow in Frames um
                    long frameOverflow = (long) (overflow * this.samplesPerPixel);

                    // Update den Offset
                    long newOffset = this.offsetFrames + (e.X >= width - 1 ? frameOverflow : -frameOverflow);
                    this.offsetFrames = Math.Clamp(newOffset, 0, this.GetMaxOffsetFrames());

                    this.UpdateOffsetScrollbar();
                }
                // ---------------------------

                this.pictureBox_waveform.Invalidate();
            }
        }

        private void Wave_MouseUp(object? sender, MouseEventArgs e)
        {
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

                // EINZELKLICK: Muss die Wellenform neu rendern, da der Scroll-Offset sich geändert hat.
                this.OriginalAudio.UpdateLoopFraction(0, 0, 0, false, false);
                _ = this.RefreshWaveformAsync();
            }
            else if (this.dragSelecting)
            {
                this.selectEndFrame = this.MapPixelToFrameInView(e.X);
                this.UpdateSelection();

                // SELEKTION BEENDET: Ein finaler Cache-Refresh (falls nötig) nach dem Dragging. 
                // Dieser eine Aufruf verursacht keinen spürbaren Lag.
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

            // Clamp selection to valid frame range
            long totalFrames = this.GetTotalFrames();
            start = Math.Clamp(start, 0L, Math.Max(0L, totalFrames));
            end = Math.Clamp(end, 0L, Math.Max(0L, totalFrames));

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

        internal async Task TogglePlayAsync()
        {
            var group = GetPlaybackGroup(this);
            if (group.Count == 0)
            {
                return;
            }

            bool anyPlaying = group.Any(tv => tv.OriginalAudio.Playing);
            bool anyPaused = group.Any(tv => tv.OriginalAudio.Paused);

            if (!anyPlaying)
            {
                if (anyPaused)
                {
                    var resumeTasks = group
                        .Where(tv => tv.OriginalAudio.Paused)
                        .Select(tv => tv.OriginalAudio.PauseAsync());
                    await Task.WhenAll(resumeTasks);

                    foreach (var tv in group)
                    {
                        tv.InvokeIfRequired(() => tv.button_playback.Text = "■");
                    }

                    return;
                }

                foreach (var tv in group)
                {
                    tv.ApplyLoopFractionToAudio();

                    long startFrame = 0;
                    int channels = Math.Max(1, tv.OriginalAudio.Channels);

                    if (tv.loopEnabled &&
                        tv.OriginalAudio.SelectionStart >= 0 &&
                        tv.OriginalAudio.SelectionEnd > tv.OriginalAudio.SelectionStart)
                    {
                        startFrame = tv.OriginalAudio.SelectionStart / channels;
                    }
                    else if (tv.lastClickFrame >= 0)
                    {
                        startFrame = tv.lastClickFrame;
                    }
                    else if (tv.OriginalAudio.StartingOffset > 0)
                    {
                        startFrame = tv.OriginalAudio.StartingOffset / channels;
                    }

                    long totalFrames = tv.GetTotalFrames();
                    if (totalFrames > 0)
                    {
                        long maxStart = Math.Max(0L, totalFrames - 1);
                        startFrame = Math.Clamp(startFrame, 0L, maxStart);
                    }
                    else
                    {
                        startFrame = 0;
                    }

                    tv.OriginalAudio.SetPosition(startFrame);
                    tv.lastClickFrame = startFrame;
                }

                await StartPlaybackForGroupAsync(group, this);
            }
            else
            {
                var stopTasks = group.Select(tv => tv.OriginalAudio.StopAsync());
                await Task.WhenAll(stopTasks);

                foreach (var tv in group)
                {
                    tv.InvokeIfRequired(() => tv.button_playback.Text = "▶");
                }
            }
        }


        private async void button_pause_Click(object? sender, EventArgs e)
        {
            await this.TogglePauseAsync();
        }

        private async Task TogglePauseAsync()
        {
            var group = GetPlaybackGroup(this);

            // Fall 1: Diese Spur spielt -> nur spielende pausieren
            if (this.OriginalAudio.Playing)
            {
                await this.OriginalAudio.PauseAsync();
                this.button_playback.Text = "▶";

                if (!ModifierKeys.HasFlag(Keys.Control))
                {
                    foreach (var tv in group.Where(tv => tv != this && !tv.IsDisposed && tv.OriginalAudio.Playing))
                    {
                        try { _ = tv.OriginalAudio.PauseAsync(); } catch { }
                    }
                }
                return;
            }

            // Fall 2: Diese Spur ist pausiert -> nur pausierte fortsetzen
            if (this.OriginalAudio.Paused)
            {
                await this.OriginalAudio.PauseAsync(); // toggle = Resume
                this.button_playback.Text = "■";

                if (!ModifierKeys.HasFlag(Keys.Control))
                {
                    foreach (var tv in group.Where(tv => tv != this && !tv.IsDisposed && tv.OriginalAudio.Paused))
                    {
                        try { _ = tv.OriginalAudio.PauseAsync(); } catch { }
                    }
                }
                return;
            }

            // Fall 3: Weder Playing noch Paused -> nur diese Spur starten, 
            // und optional andere pausierte fortsetzen (logisch konsistent mit Resume)
            this.ApplyLoopFractionToAudio();
            Action onStopped = () => this.InvokeIfRequired(() => this.button_playback.Text = "▶");
            await this.OriginalAudio.PlayAsync(CancellationToken.None, onStopped, this.CurrentVolume);
            this.button_playback.Text = "■";

            if (!ModifierKeys.HasFlag(Keys.Control))
            {
                foreach (var tv in group.Where(tv => tv != this && !tv.IsDisposed && tv.OriginalAudio.Paused))
                {
                    try { _ = tv.OriginalAudio.PauseAsync(); } catch { }
                }
            }
        }

        private void ToggleLoop(MouseEventArgs? e, bool forceOff = false)
        {
            long previousSamples = this.GetCurrentSamplePosition();
            if (e != null && (ModifierKeys & Keys.Control) != 0 || forceOff)
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
            if (e.KeyCode == Keys.Tab)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;

                var openTrackViews = WindowMain.TrackViews
                    .Where(tv => tv.Visible && !tv.IsDisposed)
                    .ToList();

                int thisIndex = openTrackViews.IndexOf(this);
                if (thisIndex >= 0 && openTrackViews.Count > 1)
                {
                    int nextIndex = (thisIndex + 1) % openTrackViews.Count;
                    var nextTrackView = openTrackViews[nextIndex];
                    nextTrackView.Focus();
                    nextTrackView.Activate();
                }

                return;
            }

            if (e.Control && e.KeyCode == Keys.C)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                LogCollection.Log($"TrackView: Ctrl+C pressed (Copy) in '{this.OriginalAudio.Name}'");
                await this.CopySelectionAsync();
                return;
            }

            if (e.Control && e.KeyCode == Keys.V)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                LogCollection.Log($"TrackView: Ctrl+V pressed (Paste) in '{this.OriginalAudio.Name}'");
                await this.PasteFromClipboardAsync();
                return;
            }

            if (e.Control && e.KeyCode == Keys.X)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                LogCollection.Log($"TrackView: Ctrl+X pressed (Cut) in '{this.OriginalAudio.Name}'");
                await this.CopySelectionAsync();
                await this.RemoveSelectionAsync();
                return;
            }

            if (e.Control && e.KeyCode == Keys.A)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                LogCollection.Log($"TrackView: Ctrl+A pressed (Select All) in '{this.OriginalAudio.Name}'");
                int channels = Math.Max(1, this.OriginalAudio.Channels);
                this.selectStartFrame = 0;
                this.selectEndFrame = this.GetTotalFrames();
                this.UpdateSelection();
                this.RequestWaveformRender();
                return;
            }

            if (e.KeyCode == Keys.Delete)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                LogCollection.Log($"TrackView: Delete pressed (Remove Selection) in '{this.OriginalAudio.Name}'");
                await this.RemoveSelectionAsync();
                return;
            }

            if (e.KeyCode == Keys.Space)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                this.button_pause.PerformClick();
                return;
            }

            if (e.KeyCode == Keys.Back)
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
                return;
            }

			const Keys LessGreaterPipeKey = (Keys) 226;

			// Ctrl + <  -> Fade In
			if (e.Control && !e.Shift && e.KeyCode == LessGreaterPipeKey)
			{
				e.Handled = true;
				e.SuppressKeyPress = true;

				if (this.OriginalAudio.Playing)
                {
                    await this.StopPlaybackAsync();
                }

                await this.OriginalAudio.CreateUndoStepAsync();
				await AudioFadeProcessor.FadeInAsync(this.OriginalAudio);

				this.RequestWaveformRender();
				return;
			}

			// Ctrl + Shift + <  -> Fade Out
			if (e.Control && e.Shift && e.KeyCode == LessGreaterPipeKey)
			{
				e.Handled = true;
				e.SuppressKeyPress = true;

				if (this.OriginalAudio.Playing)
                {
                    await this.StopPlaybackAsync();
                }

                await this.OriginalAudio.CreateUndoStepAsync();
				await AudioFadeProcessor.FadeOutAsync(this.OriginalAudio);

				this.RequestWaveformRender();
				return;
			}

		}

		private async Task PasteFromClipboardAsync()
		{
			var clip = WindowMain.ClipboardAudioObj;
			if (clip == null)
			{
				LogCollection.Log("Paste failed: Clipboard is empty.");
				return;
			}
			if (this.OriginalAudio.Playing)
			{
				LogCollection.Log("Stop playback before pasting audio.");
				return;
			}

			// Wenn der Track leer ist (keine Samples), dann die Track-Format-Metadaten
			// auf das eingefügte Sample übernehmen (insbesondere SampleRate, Channels, BitDepth),
			// damit InsertAudioAtFrameAsync später konsistent arbeitet.
			bool trackWasEmpty = (this.OriginalAudio.Data == null || this.OriginalAudio.Data.LongLength <= 0);
			if (trackWasEmpty)
			{
				try
				{
					// Nur Metadaten übernehmen, Daten werden durch InsertAudioAtFrameAsync eingefügt.
					this.OriginalAudio.SampleRate = clip.SampleRate;
					this.OriginalAudio.Channels = clip.Channels;
					this.OriginalAudio.BitDepth = clip.BitDepth;
					// Länge / Duration bleiben bis nach dem Einfügen aktuell.
					LogCollection.Log($"Paste: Target track was empty — sample rate set to {clip.SampleRate} Hz, channels set to {clip.Channels}.");
				}
				catch (Exception ex)
				{
					try { LogCollection.Log($"Paste: Failed to set track format metadata: {ex.Message}"); } catch { }
				}
			}

			await this.CreateUndoStep();
			int insertChannels = Math.Max(1, this.OriginalAudio.Channels);
			long insertFrame = 0;
			if (this.HasValidSelection())
			{
				insertFrame = this.OriginalAudio.SelectionStart / insertChannels;
				await this.OriginalAudio.EraseSelectionAsync().ConfigureAwait(true);
				LogCollection.Log($"Cut: AudioObj.Data selection erased in '{this.OriginalAudio.Name}'");
				this.ClearSelectionMarkers();
			}
			else if (this.OriginalAudio.StartingOffset > 0)
			{
				insertFrame = this.OriginalAudio.StartingOffset / insertChannels;
			}
			else if (this.lastClickFrame >= 0)
			{
				insertFrame = this.lastClickFrame;
			}

			await this.OriginalAudio.InsertAudioAtFrameAsync(clip, insertFrame).ConfigureAwait(true);
			LogCollection.Log($"Paste: AudioObj.Data inserted in '{this.OriginalAudio.Name}'");

			// Falls Track zuvor leer war, InsertAudioAtFrame hat nun Daten eingefügt —
			// Länge/Duration ggf. sofort anpassen (InsertAudioAtFrameAsync macht das bereits,
			// dennoch sicherstellen, dass SampleRate/Channels konsistent sind).
			if (trackWasEmpty)
			{
				try
				{
					// Length und Duration wurden im Insert aktualisiert; stelle sicher, dass Duration korrekt ist.
					long sampleCount = this.OriginalAudio.Data?.LongLength ?? 0L;
					this.OriginalAudio.Length = sampleCount;
					int channels = Math.Max(1, this.OriginalAudio.Channels);
					int sampleRate = Math.Max(1, this.OriginalAudio.SampleRate);
					this.OriginalAudio.Duration = TimeSpan.FromSeconds(sampleCount / (double) (sampleRate * channels));
				}
				catch { }
			}

			this.RecalculateLoopFraction();
			this.ApplyLoopFractionToAudio();
            this.AlignViewToCurrentPosition();
            this.UpdateOffsetScrollbar();
            this.RequestWaveformRender();
            this.ApplyInitialTrackSizing();

			LogCollection.Log($"AudioObj '{clip.Name}' pasted into track view.");
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
            AudioObj? clip = null;
            bool wasSelection = this.HasValidSelection();
            if (wasSelection)
            {
                clip = await this.OriginalAudio.CloneFromSelectionAsync().ConfigureAwait(true);
                if (clip != null)
                {
                    clip.Name = this.GenerateClipName();
                }
            }
            else
            {
                // Ganze Spur kopieren
                clip = this.OriginalAudio.Clone();
                if (clip != null)
                {
                    clip.Name = this.GenerateClipName();
                    clip.Rename(clip.Name);
                }
            }

            if (clip == null)
            {
                LogCollection.Log("Copy failed: No audio data available.");
                return;
            }

            // In die statische Zwischenablage legen
            WindowMain.ClipboardAudioObj = clip;
            LogCollection.Log($"TrackView: AudioObj '{clip.Name}' {(wasSelection ? "(Selection)" : "(Full)")} copied to clipboard.");
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

            await this.CreateUndoStep();

            await this.OriginalAudio.EraseSelectionAsync().ConfigureAwait(true);
            LogCollection.Log($"Remove: AudioObj.Data selection erased in '{this.OriginalAudio.Name}'");
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
            this.menuItem_splitEqualParts.Enabled = (this.OriginalAudio.Data?.Length ?? 0) > 0;
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

        private async Task SplitWaveSelectionEvenlyAsync(int partCount)
        {
            if (this.OriginalAudio.Data == null || this.OriginalAudio.Data.Length == 0)
            {
                return;
            }

            bool previousWaitCursor = this.UseWaitCursor;
            this.UseWaitCursor = true;
            this.menuItem_splitEqualParts.Enabled = false;

            try
            {
                long? startSample = this.HasValidSelection() ? this.OriginalAudio.SelectionStart : null;
                long? endSample = this.HasValidSelection() ? this.OriginalAudio.SelectionEnd : null;
                string baseName = string.IsNullOrWhiteSpace(this.OriginalAudio.Name) ? "Audio" : this.OriginalAudio.Name.Trim();
                if (startSample.HasValue && endSample.HasValue)
                {
                    baseName += "_Selection";
                }

                IReadOnlyList<AudioObj> slices = await EqualSliceProcessor_V4.SliceAsync(this.OriginalAudio, partCount, startSample, endSample, baseName);
                if (slices.Count == 0)
                {
                    MessageBox.Show(this, "The current selection/audio cannot be split into that many equal parts.", "Split Into Equal Parts", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                AudioCollectionView slicesView = new(slices);
                slicesView.Rename($"{baseName}_Split{partCount:D2}");
                LogCollection.Log($"TrackView split '{this.OriginalAudio.Name}' into {slices.Count} equal parts.");
            }
            catch (Exception ex)
            {
                LogCollection.Log(ex);
                MessageBox.Show(this, "Split failed: " + ex.Message, "Split Into Equal Parts", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                this.menuItem_splitEqualParts.Enabled = true;
                this.UseWaitCursor = previousWaitCursor;
            }
        }

        private async void menuItem_splitEqualParts2_Click(object? sender, EventArgs e)
        {
            await this.SplitWaveSelectionEvenlyAsync(2);
        }

        private async void menuItem_splitEqualParts4_Click(object? sender, EventArgs e)
        {
            await this.SplitWaveSelectionEvenlyAsync(4);
        }

        private async void menuItem_splitEqualParts8_Click(object? sender, EventArgs e)
        {
            await this.SplitWaveSelectionEvenlyAsync(8);
        }

        private async void menuItem_splitEqualParts16_Click(object? sender, EventArgs e)
        {
            await this.SplitWaveSelectionEvenlyAsync(16);
        }

        private async void menuItem_splitEqualParts32_Click(object? sender, EventArgs e)
        {
            await this.SplitWaveSelectionEvenlyAsync(32);
        }

        private async void menuItem_normalizeSelection_Click(object? sender, EventArgs e)
        {
            // Open Forms or VBasic default Dialog to input a value
            var input = Microsoft.VisualBasic.Interaction.InputBox(
                "Enter normalization level (0.0 - 1.0):",
                "Normalize Audio",
                "0.8");

            // Try parse float from value (work with ',' and '.'), fallback 0.8f
            if (!float.TryParse(input.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float normalizeLevel))
            {
                normalizeLevel = 0.8f;
            }

            // Create Undo Step
            await this.OriginalAudio.CreateUndoStepAsync();

            // Perform NormalizeAsync on OriginalAudio or selection if any
            if (!this.HasValidSelection())
            {
                await AudioAmplitudeProcessor.NormalizeAsync(this.OriginalAudio, normalizeLevel, 4);
            }
            else
            {
                await AudioAmplitudeProcessor.NormalizeAsync(this.OriginalAudio, this.OriginalAudio.SelectionStart, this.OriginalAudio.SelectionEnd, normalizeLevel, 4);
            }
        }

        private async void menuItem_fadeIn_Click(object? sender, EventArgs e)
        {
            // Open VBasic dialog to get targetAmplitude for fade low offset
            var input = Microsoft.VisualBasic.Interaction.InputBox(
                "Enter fade-in target offset amplitude (0.0 - 1.0):",
                "Fade In Audio",
                "0.0");

            // Try parse float from value (work with ',' and '.'), fallback 0.0f
            if (!float.TryParse(input.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float targetAmplitude))
            {
                targetAmplitude = 0.0f;
            }

            // Create Undo Step
            await this.OriginalAudio.CreateUndoStepAsync();

            await AudioFadeProcessor.FadeInAsync(this.OriginalAudio, targetAmplitude);
        }

        private async void menuItem_fadeOut_Click(object? sender, EventArgs e)
        {
            // Open VBasic dialog to get targetAmplitude for fade low offset
            var input = Microsoft.VisualBasic.Interaction.InputBox(
                "Enter fade-out target offset amplitude (0.0 - 1.0):",
                "Fade In Audio",
                "0.0");

            // Try parse float from value (work with ',' and '.'), fallback 0.0f
            if (!float.TryParse(input.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float targetAmplitude))
            {
                targetAmplitude = 0.0f;
            }

            // Create Undo Step
            await this.OriginalAudio.CreateUndoStepAsync();

            await AudioFadeProcessor.FadeOutAsync(this.OriginalAudio, targetAmplitude);
        }

        private async void menuItem_trimSilence_Click(object? sender, EventArgs e)
        {
            // Open VBasic dialog to get silence threshold and minDuration, default empty fields, empty results as null
            var thresholdInput = Microsoft.VisualBasic.Interaction.InputBox(
                "Enter silence threshold (0.0 - 1.0):",
                "Trim Silence",
                "");
            var durationInput = Microsoft.VisualBasic.Interaction.InputBox(
                "Enter minimum silence duration in milliseconds:",
                "Trim Silence",
                "");
            float? silenceThreshold = null;
            int? minDuration = null;
            if (float.TryParse(thresholdInput.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float threshold))
            {
                silenceThreshold = threshold;
            }
            if (int.TryParse(durationInput, out int duration))
            {
                minDuration = duration;
            }

            // Busy cursor
            Cursor cursor = this.Cursor;
            this.Cursor = Cursors.WaitCursor;

            // Create Undo Step
            await this.OriginalAudio.CreateUndoStepAsync();
            await BeatGridFinder.TrimSilenceAsync(this.OriginalAudio, silenceThreshold, minDuration);

            // Restore cursor
            this.Cursor = cursor;
        }

        private async void beatgridV1ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            bool activated = this.beatGridV1ToolStripMenuItem.Checked;
            if (activated)
            {
                if (this.OriginalAudio.Data.LongLength != this.OriginalAudio.BeatGrid.LongLength * 2 || this.beatGridV2ToolStripMenuItem.Checked)
                {
                    // Make cursor for Form busy
                    Cursor previousCursor = this.Cursor;
                    this.Cursor = Cursors.WaitCursor;

                    await BeatGridFinder.GenerateBeatGridAsync(this.OriginalAudio);

                    // Restore previous cursor
                    this.Cursor = previousCursor;
                }

                this.beatGridV2ToolStripMenuItem.Checked = false;
            }

            this.OriginalAudio.DrawBeatGrid = activated;

            if (activated && !this.OriginalAudio.Playing)
            {
                // Redraw waveform
                await this.RefreshWaveformAsync();
            }
        }

        private async void beatGridV2ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            bool activated = this.beatGridV2ToolStripMenuItem.Checked;
            if (activated)
            {
                if (this.OriginalAudio.Data.LongLength != this.OriginalAudio.BeatGrid.LongLength * 2 || this.beatGridV1ToolStripMenuItem.Checked)
                {
                    // Make cursor for Form busy
                    Cursor previousCursor = this.Cursor;
                    this.Cursor = Cursors.WaitCursor;

                    await BeatGridFinder_V2.GenerateBeatGridAsync(this.OriginalAudio);

                    // Restore previous cursor
                    this.Cursor = previousCursor;
                }

                this.beatGridV1ToolStripMenuItem.Checked = false;
            }

            this.OriginalAudio.DrawBeatGrid = activated;

            if (activated && !this.OriginalAudio.Playing)
            {
                // Redraw waveform
                await this.RefreshWaveformAsync();
            }
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

        public async Task ApplyStretchedAudioAsync(AudioObj result, double? stretchFactorOverride = null, bool resumePlaybackAfterReplace = false)
        {
            if (result == null)
            {
                return;
            }

            var original = this.OriginalAudio;
            bool wasPlaying = original.PlayerPlaying;
            bool wasPaused = original.Paused;
            int sourceChannels = Math.Max(1, original.Channels);
            double stretchFactor = stretchFactorOverride
                ?? (Math.Abs(result.StretchFactor) > double.Epsilon ? result.StretchFactor : 1.0);
            if (Math.Abs(stretchFactor) <= double.Epsilon)
            {
                stretchFactor = 1.0;
            }

            DateTime playbackSnapshotUtc = DateTime.UtcNow;
            long sourcePositionSamples = original.Position * sourceChannels;

            await this.StopPlaybackAsync();

            if (wasPlaying)
            {
                double lagSeconds = Math.Max(0.0, (DateTime.UtcNow - playbackSnapshotUtc).TotalSeconds);
                long lagSamples = (long) Math.Round(lagSeconds * Math.Max(1, original.SampleRate) * sourceChannels);
                sourcePositionSamples += Math.Max(0L, lagSamples);
            }

            this.CancelPendingRender();

            float[] newData = result.Data ?? [];
            original.Data = newData;
            LogCollection.Log($"ApplyStretched: AudioObj.Data replaced in '{original.Name}'");
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

            long resumedSamples = (long) Math.Round(sourcePositionSamples * stretchFactor);
            resumedSamples = Math.Clamp(resumedSamples, 0L, Math.Max(0L, sampleCount - channels));
            long resumedFrames = resumedSamples / channels;

            long resumedStartSample = resumedFrames * Math.Max(1, original.Channels);
            this.OriginalAudio.SetPosition(resumedFrames);
            this.OriginalAudio.StartingOffset = resumedStartSample;
            this.offsetFrames = 0;
            this.lastClickFrame = resumedFrames;
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

            bool shouldResumePlayback = resumePlaybackAfterReplace || wasPlaying;
            if (shouldResumePlayback)
            {
                try
                {
                    await original.PlayAsync(
                        CancellationToken.None,
                        () => this.InvokeIfRequired(() => this.button_playback.Text = "▶"),
                        this.CurrentVolume).ConfigureAwait(false);
                    this.InvokeIfRequired(() => this.button_playback.Text = "■");
                }
                catch
                {
                    this.InvokeIfRequired(() => this.button_playback.Text = "▶");
                }
            }
            else if (wasPaused)
            {
                this.InvokeIfRequired(() => this.button_playback.Text = "▶");
            }
        }


        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Tab)
            {
                // Nur sichtbare und nicht-disponierte TrackViews berücksichtigen
                var openTrackViews = WindowMain.TrackViews
                    .Where(tv => tv.Visible && !tv.IsDisposed)
                    .ToList();

                int thisIndex = openTrackViews.IndexOf(this);
                if (thisIndex >= 0 && openTrackViews.Count > 1)
                {
                    int nextIndex = (thisIndex + 1) % openTrackViews.Count;
                    var nextTrackView = openTrackViews[nextIndex];
                    nextTrackView.Focus();
                    nextTrackView.Activate();
                }
                return true;
            }

            if (keyData == (Keys.Control | Keys.Z))
            {
                this.Undo();
                return true;
            }
            if (keyData == (Keys.Control | Keys.Y))
            {
                this.Redo();
                return true;
            }
            if (keyData == (Keys.Control | Keys.S))
            {
                this.button_apply_Click(null, null);
                return true;
            }
            if (keyData == (Keys.Control | Keys.N))
            {
                var cv = WindowMain.CollectionViews.LastOrDefault();
                if (cv == null)
                {
                    cv = new AudioCollectionView([]);
                }

                var track = new AudioObj
                {
                    Name = "New Track #" + cv.AudioCount.ToString("D2"),
                    SampleRate = 44100,
                    Channels = 2,
                    BitDepth = 32
                };
                cv.AudioC.Audios.Add(track);
                var trackView = new TrackView(track);
                trackView.Show();
                cv.Show();
            }
            if (keyData == (Keys.L))
            {
                this.ToggleLoop(null, this.OriginalAudio.LoopEnabled);
                return true;
            }
            if (keyData == (Keys.Control | Keys.Q))
            {
                this.Close();
                return true;
            }
            if (keyData == (Keys.Shift | Keys.ShiftKey))
            {
                this.ShiftDown_Cursor_SnapToBeatGrid();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        public void Undo()
        {
            if (this.OriginalAudio.CanUndo)
            {
                this.OriginalAudio.Undo();
                LogCollection.Log($"Undo: AudioObj.Data reverted in '{this.OriginalAudio.Name}'");
                this.ClearSelectionMarkers();
                this.RecalculateLoopFraction();
                this.ApplyLoopFractionToAudio();
                this.AlignViewToCurrentPosition();
                this.UpdateOffsetScrollbar();
                this.RequestWaveformRender();
            }
            else if (!WindowMain.IsAnyTrackPlaying)
            {
                SystemSounds.Exclamation.Play();
            }
        }

        public void Redo()
        {
            if (this.OriginalAudio.CanRedo)
            {
                this.OriginalAudio.Redo();
                LogCollection.Log($"Redo: AudioObj.Data restored in '{this.OriginalAudio.Name}'");
                this.ClearSelectionMarkers();
                this.RecalculateLoopFraction();
                this.ApplyLoopFractionToAudio();
                this.AlignViewToCurrentPosition();
                this.UpdateOffsetScrollbar();
                this.RequestWaveformRender();
            }
            else if (!WindowMain.IsAnyTrackPlaying)
            {
                SystemSounds.Exclamation.Play();
            }
        }

        public async Task CreateUndoStep()
        {
            await this.OriginalAudio.CreateUndoStepAsync();
        }

        private async void button_apply_Click(object? sender, EventArgs? e)
        {
            await this.ApplyTrackAsync(andClose: false);
        }

        internal void HighlightBorder()
        {
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.FormBorderColor = Color.Red;
        }

        internal void NormalightBorder()
        {
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.FormBorderColor = Color.White;
        }

        internal async Task ApplyTrackAsync(bool andClose = false)
        {
            if (this.SourceCollection == null)
            {
                var cv = new AudioCollectionView([this.OriginalAudio]);
                WindowMain.CollectionViews.Add(cv);
                LogCollection.Log($"Created new collection view and applied changes to '{this.OriginalAudio.Name}'.");
                cv.Show();
                return;
            }

            await Task.Run(() =>
            {
                // Find index of the original source audio in the provided collection find by Id
                int index = this.SourceCollection.Audios.ToList().FindIndex(a => a.Id == this.OriginalAudio.Id);
                if (index >= 0)
                {
                    try
                    {
                        // Copy edited state into the existing collection item so bindings remain valid
                        // Do not dispose the editing clone because TrackView is still using it
                        this.SourceCollection.Audios[index].ReplaceWith(this.OriginalAudio, disposeSource: false);

                        // Notify the BindingList that the item changed so UI updates
                        this.SourceCollection.Audios.ResetItem(index);

                        LogCollection.Log($"Applied changes to '{this.SourceCollection.Audios[index].Name}' in source collection.");
                    }
                    catch (Exception ex)
                    {
                        LogCollection.Log(ex);
                    }
                }
            });

            WindowMain.Instance?.RefreshAllCollectionViews();

            if (andClose)
            {
                this.Close();
            }
        }


        internal void ShiftDown_Cursor_SnapToBeatGrid()
        {
            if (this.OriginalAudio == null
                || this.OriginalAudio.BeatGrid == null
                || this.OriginalAudio.BeatGrid.LongLength <= 0
                || !this.OriginalAudio.DrawBeatGrid
                || this.OriginalAudio.Playing)
            {
                return;
            }

            if (this.samplesPerPixel <= 0)
            {
                return;
            }

            if (this.pictureBox_waveform.Width <= 0)
            {
                return;
            }

            Point cursorScreen = Cursor.Position;
            Point waveClient = this.pictureBox_waveform.PointToClient(cursorScreen);

            int width = Math.Max(1, this.pictureBox_waveform.Width);
            int x = Math.Clamp(waveClient.X, 0, width - 1);

            long totalFrames = this.GetTotalFrames();
            int spp = Math.Max(1, this.samplesPerPixel);

            long frameUnderCursor = this.offsetFrames + (long) x * spp;
            frameUnderCursor = Math.Clamp(frameUnderCursor, 0L, Math.Max(0L, totalFrames - 1));

            long snappedFrame = this.OriginalAudio.GetNearestSnapSamplePosition(frameUnderCursor);

            double invSpp = 1.0 / spp;
            int snappedX = (int) Math.Round((snappedFrame - this.offsetFrames) * invSpp);
            snappedX = Math.Clamp(snappedX, 0, width - 1);

            Point newWaveClient = new(snappedX, waveClient.Y);
            Point newScreenPoint = this.pictureBox_waveform.PointToScreen(newWaveClient);
            Cursor.Position = newScreenPoint;
        }




        private int FrameToPixel(long frameIndex)
        {
            if (this.OriginalAudio == null || this.Settings == null)
            {
                return 0;
            }

            // 1. Relativer Frame-Index (vom linken Rand des sichtbaren Bereichs)
            // Wichtig: Verwende hier die View-Offset `offsetFrames` (nicht OriginalAudio.ScrollOffset),
            // damit Zeichnung, Bitmap-Render und Maus-Logik dieselbe Basis haben.
            long relativeFrame = frameIndex - this.offsetFrames;

            // 2. Umrechnung in Pixel
            int samplesPerPixel = Math.Max(1, this.samplesPerPixel);

            // Division kann negative Werte erzeugen -> cast/rounding zu int ist OK,
            // aber wir clampen das Ergebnis später, wenn nötig.
            return (int) (relativeFrame / samplesPerPixel);
        }

        private long GetCurrentFramePosition()
        {
            if (this.OriginalAudio == null || this.OriginalAudio.Channels == 0)
            {
                return 0;
            }

            // CurrentPlaybackPositionBytes kommt aus AudioObj.Playback.cs
            long bytes = this.OriginalAudio.CurrentPlaybackPositionBytes;

            // Frames = Bytes / (Kanäle * 4 Bytes pro Float)
            int bytesPerFrame = Math.Max(1, this.OriginalAudio.Channels) * sizeof(float);

            return bytes / bytesPerFrame;
        }

        [SupportedOSPlatform("windows")]
        private void waveFormPictureBox_Paint(object sender, PaintEventArgs e)
        {
            if (this.OriginalAudio == null || this.OriginalAudio.Data == null || this.currentWaveformBitmap == null)
            {
                // Wenn der Cache noch nicht fertig ist, nur den Hintergrund zeichnen
                e.Graphics.Clear(Color.Black); // Oder Ihre gewählte Hintergrundfarbe
                return;
            }

            // 1. Statischen Cache als Hintergrund zeichnen (extrem schnell)
            // 'currentWaveformBitmap' muss das Ergebnis eines DrawWaveformCacheAsync-Aufrufs sein.
            e.Graphics.DrawImage(this.currentWaveformBitmap, 0, 0);

            // 2. Selection Rectangle (Auswahl) zeichnen
            this.DrawSelectionOverlay(e.Graphics, e.ClipRectangle.Height);

            // 3. Playback Caret (Abspielposition) zeichnen
            this.DrawPlaybackCaret(e.Graphics, e.ClipRectangle.Height);
        }

        [SupportedOSPlatform("windows")]
        private void DrawSelectionOverlay(Graphics g, int height)
        {
            long selStartFrame = this.OriginalAudio.SelectionStart;
            long selEndFrame = this.OriginalAudio.SelectionEnd;

            // Nur zeichnen, wenn eine Auswahl aktiv ist und gültig ist
            if (selStartFrame < 0 || selEndFrame < 0 || selStartFrame == selEndFrame)
            {
                return;
            }

            // Sicherstellen, dass Start < Ende für die Berechnung
            long start = Math.Min(selStartFrame, selEndFrame);
            long end = Math.Max(selStartFrame, selEndFrame);

            // Umrechnung in Pixel
            int startPixel = this.FrameToPixel(start);
            int endPixel = this.FrameToPixel(end);

            // Das Selection-Rechteck
            int x = startPixel;
            int w = endPixel - startPixel;

            // Optimierung: Nur den sichtbaren Bereich zeichnen
            int clipWidth = this.pictureBox_waveform.Width;
            int clipHeight = height;

            // Clipping-Berechnung (wichtig für Scrolling Performance)
            int drawX = Math.Max(0, x);
            int drawWidth = Math.Min(clipWidth, x + w) - drawX;

            if (drawWidth <= 0 || drawX >= clipWidth)
            {
                return; // Selection ist nicht sichtbar
            }

            // Zeichnen der transparenten Auswahl
            // - Innerhalb der Track-Dauer: Standard-Farbe (sättigter, halbtransparenter Bereich)
            // - Außerhalb der Track-Dauer (vor 0 oder nach Ende): hellere / andere Färbung zur Visualisierung
            {
                int channels = Math.Max(1, this.OriginalAudio.Channels);

                // Auswahl in Samples im AudioObj gespeichert => in Frames umrechnen
                long selStartSamples = this.OriginalAudio.SelectionStart;
                long selEndSamples = this.OriginalAudio.SelectionEnd;
                if (selStartSamples < 0 || selEndSamples <= selStartSamples)
                {
                    // ungültig (sollte oben bereits abgefangen sein), aber sicherheitshalber nichts zeichnen
                    return;
                }

                long startSamples = Math.Min(selStartSamples, selEndSamples);
                long endSamples = Math.Max(selStartSamples, selEndSamples);

                long startFrame = startSamples / channels;
                long endFrame = endSamples / channels;

                // Gesamtanzahl Frames des Tracks (in Frame-Einheiten)
                long totalFrames = this.GetTotalFrames();

                // Bereiche: vor Track-Start, innerhalb Track, nach Track-Ende
                long preTrackStart = Math.Min(startFrame, 0);
                long preTrackEnd = Math.Min(endFrame, 0);

                long insideStart = Math.Max(startFrame, 0);
                long insideEnd = Math.Min(endFrame, totalFrames);

                long postStart = Math.Max(startFrame, totalFrames);
                long postEnd = Math.Max(endFrame, totalFrames);

                // Hilfsfunktion: Frame-Bereich in sichtbare Pixel (und Clip)
                clipWidth = this.pictureBox_waveform.Width;
                clipHeight = height; // <-- Fix: 'height' durch 'bmp.Height' ersetzt

                // Brushes: normal und "outside" (heller / dezenter)
                using var insideBrush = new SolidBrush(Color.FromArgb(60, Color.DeepSkyBlue)); // wie zuvor
                using var outsideBrush = new SolidBrush(Color.FromArgb(40, Color.LightSkyBlue)); // dezenter, hellerer Ton

                // Zeichne Bereich innerhalb Track
                if (insideEnd > insideStart)
                {
                    int pxStart = this.FrameToPixel(insideStart);
                    int pxEnd = this.FrameToPixel(insideEnd);
                    x = pxStart;
                    w = Math.Max(1, pxEnd - pxStart);

                    drawX = Math.Max(0, x);
                    drawWidth = Math.Min(clipWidth, x + w) - drawX;
                    if (drawWidth > 0 && drawX < clipWidth)
                    {
                        g.FillRectangle(insideBrush, drawX, 0, drawWidth, clipHeight);
                    }
                }

                // Zeichne Bereich vor Track-Start (falls vorhanden) als "outside"
                if (startFrame < 0 && endFrame > 0)
                {
                    // Teil vor 0 bis min(endFrame,0)
                    int pxPreStart = this.FrameToPixel(Math.Max(startFrame, preTrackStart));
                    int pxPreEnd = this.FrameToPixel(Math.Min(endFrame, 0));
                    int xPre = pxPreStart;
                    int wPre = Math.Max(1, pxPreEnd - pxPreStart);
                    int drawXPre = Math.Max(0, xPre);
                    int drawWidthPre = Math.Min(clipWidth, xPre + wPre) - drawXPre;
                    if (drawWidthPre > 0 && drawXPre < clipWidth)
                    {
                        g.FillRectangle(outsideBrush, drawXPre, 0, drawWidthPre, clipHeight);
                    }
                }
                else if (endFrame <= 0)
                {
                    // komplette Auswahl vor Track-Start
                    int pxPreStart = this.FrameToPixel(startFrame);
                    int pxPreEnd = this.FrameToPixel(endFrame);
                    int xPre = pxPreStart;
                    int wPre = Math.Max(1, pxPreEnd - pxPreStart);
                    int drawXPre = Math.Max(0, xPre);
                    int drawWidthPre = Math.Min(clipWidth, xPre + wPre) - drawXPre;
                    if (drawWidthPre > 0 && drawXPre < clipWidth)
                    {
                        g.FillRectangle(outsideBrush, drawXPre, 0, drawWidthPre, clipHeight);
                    }
                }

                // Zeichne Bereich nach Track-Ende (falls vorhanden) als "outside"
                if (postEnd > postStart)
                {
                    int pxPostStart = this.FrameToPixel(postStart);
                    int pxPostEnd = this.FrameToPixel(postEnd);
                    int xPost = pxPostStart;
                    int wPost = Math.Max(1, pxPostEnd - pxPostStart);
                    int drawXPost = Math.Max(0, xPost);
                    int drawWidthPost = Math.Min(clipWidth, xPost + wPost) - drawXPost;
                    if (drawWidthPost > 0 && drawXPost < clipWidth)
                    {
                        g.FillRectangle(outsideBrush, drawXPost, 0, drawWidthPost, clipHeight);
                    }
                }
            }
        }

        [SupportedOSPlatform("windows")]
        private void DrawPlaybackCaret(Graphics g, int height)
        {
            // Caret nur zeichnen, wenn Playing oder Paused
            if (!this.OriginalAudio.Playing && !this.OriginalAudio.Paused)
            {
                return;
            }

            // Aktuelle Position in Frames
            long currentFrame = this.GetCurrentFramePosition();

            // Caret-Position in Pixel
            int caretPixelX = this.FrameToPixel(currentFrame);

            // Überprüfen, ob das Caret im sichtbaren Bereich ist
            if (caretPixelX < 0 || caretPixelX > this.pictureBox_waveform.Width)
            {
                return;
            }

            // Caret zeichnen (dicke rote Linie)
            using (var caretPen = new Pen(Color.Red, 2))
            {
                // Optional: Eine leichte Verbesserung der Sichtbarkeit am oberen Rand (kleiner Dreieck-Kopf)
                g.DrawLine(caretPen, caretPixelX, 0, caretPixelX, height);
                g.DrawLine(caretPen, caretPixelX - 3, 0, caretPixelX + 3, 0); // Horizontaler Strich oben
            }
        }



        protected override void WndProc(ref Message m)
        {
            const int WM_NCLBUTTONDBLCLK = 0x00A3; // Non-client left button double-click
            if (m.Msg == WM_NCLBUTTONDBLCLK)
            {
                try
                {
                    // Dialog auf UI-Thread öffnen
                    this.BeginInvoke(new Action(this.ShowTrackRenameDialog));
                }
                catch { }
                // Standardverhalten (Maximieren) unterdrücken
                return;
            }

            base.WndProc(ref m);
        }

        private void ShowTrackRenameDialog()
        {
            string current = this.OriginalAudio.Name;
            // Microsoft.VisualBasic.Interaction.InputBox wird bereits im Projekt genutzt
            string input = Microsoft.VisualBasic.Interaction.InputBox("Enter new name for this track:", "Rename Track", current);
            if (!string.IsNullOrWhiteSpace(input) && input != current)
            {
                this.Text = "#" + this.TrackViewId.ToString("D2") + " - " + input;
                this.OriginalAudio.Rename(input);
            }
        }

        internal void Rename(string newName)
        {
            this.OriginalAudio.Rename(newName);
            this.Text = "#" + this.TrackViewId.ToString("D2") + " - " + newName;
        }

        private void checkBox_sync_CheckedChanged(object sender, EventArgs e)
        {
            if (ModifierKeys.HasFlag(Keys.Control))
            {
                // Alle TrackViews synchronisieren
                foreach (var tv in WindowMain.TrackViews)
                {
                    if (tv != this)
                    {
                        tv.checkBox_sync.Checked = this.checkBox_sync.Checked;
                    }
                }
            }
        }

        private static IReadOnlyList<TrackView> GetSyncedGroup(TrackView origin)
        {
            if (origin == null || origin.IsDisposed)
            {
                return Array.Empty<TrackView>();
            }

            if (!origin.Synced)
            {
                return [origin];
            }

            try
            {
                var group = WindowMain.TrackViews
                    .Where(tv => tv != null && !tv.IsDisposed && tv.Synced)
                    .Distinct()
                    .ToList();

                if (!group.Contains(origin))
                {
                    group.Insert(0, origin);
                }

                return group;
            }
            catch
            {
                return [origin];
            }
        }

        private async Task ResetStartingPointCoreAsync()
        {
            await this.OriginalAudio.StopAsync();
            this.OriginalAudio.StartingOffset = 0;
            this.OriginalAudio.SetPosition(0);
            this.ToggleLoop(null, true);
            this.lastClickFrame = 0;
            this.offsetFrames = 0;
            this.UpdateOffsetScrollbar();
            await this.RefreshWaveformAsync();
            this.InvokeIfRequired(() => this.button_playback.Text = "▶");
        }

        private async Task ResetStartingPointAsync()
        {
            var group = GetPlaybackGroup(this);
            if (group.Count == 0)
            {
                return;
            }

            var stopTasks = group.Select(tv => tv.OriginalAudio.StopAsync());
            await Task.WhenAll(stopTasks);

            var refreshTasks = new List<Task>();

            foreach (var tv in group)
            {
                tv.OriginalAudio.StartingOffset = 0;
                tv.OriginalAudio.SetPosition(0);
                tv.ToggleLoop(null, true);
                tv.lastClickFrame = 0;
                tv.offsetFrames = 0;
                tv.UpdateOffsetScrollbar();
                tv.InvokeIfRequired(() => tv.button_playback.Text = "▶");
                refreshTasks.Add(tv.RefreshWaveformAsync());
            }

            await Task.WhenAll(refreshTasks);
        }


        private async Task PrepareRestartFromStartCoreAsync()
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
        }

        private async Task RestartPlaybackFromStartAsync()
        {
            var group = GetPlaybackGroup(this);
            if (group.Count == 0)
            {
                return;
            }

            var stopTasks = group.Select(tv => tv.OriginalAudio.StopAsync());
            await Task.WhenAll(stopTasks);

            var refreshTasks = new List<Task>();

            foreach (var tv in group)
            {
                int channels = Math.Max(1, tv.OriginalAudio.Channels);
                long startFrame = tv.OriginalAudio.StartingOffset > 0
                    ? tv.OriginalAudio.StartingOffset / channels
                    : 0;

                long totalFrames = tv.GetTotalFrames();
                if (totalFrames > 0)
                {
                    long maxStart = Math.Max(0L, totalFrames - 1);
                    startFrame = Math.Clamp(startFrame, 0L, maxStart);
                }
                else
                {
                    startFrame = 0;
                }

                tv.OriginalAudio.SetPosition(startFrame);
                tv.lastClickFrame = startFrame;

                long desiredOffset = Math.Max(0, startFrame - tv.GetCaretAnchorFrame());
                tv.offsetFrames = Math.Min(tv.GetMaxOffsetFrames(), desiredOffset);
                tv.UpdateOffsetScrollbar();
                refreshTasks.Add(tv.RefreshWaveformAsync());
            }

            await Task.WhenAll(refreshTasks);

            await StartPlaybackForGroupAsync(group, this);
        }

        private static IReadOnlyList<TrackView> GetPlaybackGroup(TrackView origin)
        {
            if (origin == null || origin.IsDisposed)
            {
                return Array.Empty<TrackView>();
            }

            var selfOnly = new[] { origin };

            try
            {
                var synced = WindowMain.SyncedTrackViews;
                if (synced == null)
                {
                    return selfOnly;
                }

                if (!synced.Contains(origin))
                {
                    return selfOnly;
                }

                var group = synced
                    .Where(tv => tv != null && !tv.IsDisposed)
                    .Distinct()
                    .ToList();

                if (group.Count == 0)
                {
                    return selfOnly;
                }

                return group;
            }
            catch
            {
                return selfOnly;
            }
        }

        private static async Task StartPlaybackForGroupAsync(IReadOnlyList<TrackView> group, TrackView initiator)
        {
            if (group == null || group.Count == 0 || initiator == null || initiator.IsDisposed)
            {
                return;
            }

            void SetButtonsStopped()
            {
                foreach (var tv in group)
                {
                    tv.InvokeIfRequired(() => tv.button_playback.Text = "▶");
                }
            }

            foreach (var tv in group)
            {
                tv.InvokeIfRequired(() => tv.button_playback.Text = "■");
            }

            float mainVolume = initiator.GetEffectivePlaybackVolume();
            Action onStopped = SetButtonsStopped;

            foreach (var tv in group)
            {
                if (tv == initiator)
                {
                    continue;
                }

                try
                {
                    float trackVolume = tv.GetEffectivePlaybackVolume();
                    _ = tv.OriginalAudio.PlayAsync(CancellationToken.None, null, trackVolume);
                }
                catch
                {
                }
            }

            try
            {
                await initiator.OriginalAudio.PlayAsync(CancellationToken.None, onStopped, mainVolume);
            }
            catch
            {
                SetButtonsStopped();
            }
        }
    }
}
