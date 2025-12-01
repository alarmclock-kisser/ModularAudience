using ModularAudience.Audio;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace ModularAudience.Forms.Modules
{
    public partial class LoopControl : Form
    {
        private TrackView? CurrentTrackView => WindowMain.LastSelectedTrackView;
        private AudioObj? OriginalAudio => this.CurrentTrackView?.OriginalAudio;
        private float Bpm => this.OriginalAudio?.Bpm > 0 ? this.OriginalAudio.Bpm : this.OriginalAudio?.ScannedBpm > 0 ? this.OriginalAudio.ScannedBpm : 120f;

        private int SampleRangePerBeat => this.OriginalAudio != null ? (int)(this.OriginalAudio.SampleRate * 60f / this.Bpm) : 44100;

        private float CurrentLoopFraction
        {
            get
            {
                // Efficient single lookup (avoids multiple enumerations / First calls)
                var btn = this.panel_buttons.Controls.OfType<Button>().FirstOrDefault(b => b.BackColor == Color.LightBlue);
                if (btn == null)
                {
                    return 0f;
                }

                string tag = btn.Tag?.ToString() ?? "0";
                // Try invariant parse first, fallback to current culture
                if (float.TryParse(tag, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float val))
                {
                    return val;
                }
                if (float.TryParse(tag, out val))
                {
                    return val;
                }
                return 0f;
            }
        }


        public LoopControl()
        {
            this.InitializeComponent();

            this.BuildLoopControlButtons();

            this.StartPosition = FormStartPosition.Manual;
            this.Location = new Point(0, 0);

        }



        private void BuildLoopControlButtons()
        {
            // Configure template button (hidden in UI)
            this.button_loop.Font = new Font("Bahnschrift Light Condensed", 5.5F, FontStyle.Regular, GraphicsUnit.Point, 0);
            this.button_loop.Enabled = false;
            this.button_loop.Visible = false;

            // Labels (left -> right)
            string[] buttonLabels = new[] { "4", "2", "1", "/2", "/4", "/8", "/16", "/32", "/32", "/16", "/8", "/4", "/2", "1", "2", "4" };

            // Corresponding numeric fractions (left half negative = "lookback", right half positive = "forward")
            float[] fractions = new[]
            {
        -4f, -2f, -1f, -0.5f, -0.25f, -0.125f, -0.0625f, -0.03125f,
         0.03125f, 0.0625f, 0.125f, 0.25f, 0.5f, 1f, 2f, 4f
    };

            const int buttonCount = 16;

            // Ensure panel padding/margin set so buttons align nicely
            try
            {
                this.panel_buttons.SuspendLayout();
                this.panel_buttons.Padding = new Padding(6);
                this.panel_buttons.Margin = Padding.Empty;
                this.panel_buttons.AutoScroll = false; // we force-fit, no horizontal scroll
            }
            catch { }

            // Remove existing child buttons previously created (keep other controls if any)
            var existing = this.panel_buttons.Controls.OfType<Button>().ToList();
            foreach (var b in existing)
            {
                try { this.panel_buttons.Controls.Remove(b); b.Dispose(); } catch { }
            }

            // Create buttons
            var created = new List<Button>(buttonCount);
            for (int i = 0; i < buttonCount; i++)
            {
                var copy = new Button
                {
                    Text = buttonLabels[i],
                    Tag = fractions[i].ToString(System.Globalization.CultureInfo.InvariantCulture),
                    BackColor = SystemColors.Control,
                    FlatStyle = FlatStyle.Standard,
                    TextAlign = ContentAlignment.MiddleCenter,
                    UseCompatibleTextRendering = true,
                    Font = this.button_loop.Font,
                    Margin = Padding.Empty,
                    Anchor = AnchorStyles.Top | AnchorStyles.Left
                };

                // Click handler shared
                copy.Click += this.LoopButton_Click;

                // Optional tooltip
                try
                {
                    var tt = new ToolTip();
                    tt.SetToolTip(copy, (fractions[i] < 0 ? "Look back " : "Loop forward ") + Math.Abs(fractions[i]) + "× beat");
                }
                catch { }

                this.panel_buttons.Controls.Add(copy);
                created.Add(copy);
            }

            // Layout helper that fits exactly 16 buttons into the client area (respecting panel padding)
            void LayoutButtons()
            {
                try
                {
                    if (this.panel_buttons.ClientSize.Width <= 0 || created.Count == 0)
                    {
                        return;
                    }

                    int paddingLeft = this.panel_buttons.Padding.Left;
                    int paddingRight = this.panel_buttons.Padding.Right;
                    int paddingTop = this.panel_buttons.Padding.Top;
                    int paddingBottom = this.panel_buttons.Padding.Bottom;

                    int spacing = 4; // gap between buttons
                    int availableWidth = Math.Max(0, this.panel_buttons.ClientSize.Width - paddingLeft - paddingRight);
                    int availableHeight = Math.Max(1, this.panel_buttons.ClientSize.Height - paddingTop - paddingBottom);

                    // Compute base width per button and distribute remainder to last buttons to avoid rounding gaps
                    int totalSpacing = Math.Max(0, (created.Count - 1) * spacing);
                    int baseWidth = created.Count > 0 ? Math.Max(8, (availableWidth - totalSpacing) / created.Count) : 0;
                    int used = baseWidth * created.Count + totalSpacing;
                    int remainder = Math.Max(0, availableWidth - used);

                    int x = paddingLeft;
                    for (int i = 0; i < created.Count; i++)
                    {
                        int w = baseWidth;
                        // distribute remainder (one pixel each to first 'remainder' buttons)
                        if (i < remainder)
                        {
                            w += 1;
                        }

                        var btn = created[i];
                        int h = Math.Max(1, availableHeight); // full height of panel content
                        btn.SetBounds(x, paddingTop, w, h);
                        x += w + spacing;
                    }
                }
                catch { }
            }

            // Initial layout
            LayoutButtons();

            // Re-layout on resize so the 16 buttons always fit exactly
            this.panel_buttons.Resize -= (s, e) => LayoutButtons();
            this.panel_buttons.Resize += (s, e) => LayoutButtons();

            try { this.panel_buttons.ResumeLayout(); } catch { }
        }




        private void LoopButton_Click(object? sender, EventArgs e)
        {
            Button? clickedButton = (Button?) sender;
            if (clickedButton == null)
            {
                return;
            }

            clickedButton.BackColor = Color.LightBlue;

            // Untoggle all other buttons
            this.UntoggleAllOtherButtons(clickedButton);

            // Set loop range
            this.SetLoopRange();
        }

        private void UntoggleAllOtherButtons(Button? sender)
        {
            var buttons = this.panel_buttons.Controls.OfType<Button>().Where(b => b != sender);
            foreach (var button in buttons)
            {
                button.BackColor = SystemColors.Control;
            }
        }

        private void SetLoopRange()
        {
            // Guard
            if (this.CurrentTrackView == null || this.OriginalAudio == null)
            {
                return;
            }

            float fraction = this.CurrentLoopFraction;
            // 0 => disable loop (consistent with existing behavior)
            if (fraction == 0f)
            {
                this.OriginalAudio.UpdateLoopFraction(0, 0, 0, false, false);
                return;
            }

            try
            {
                // Units:
                // - TrackView / AudioObj.Position is in frames (frames == samples per channel)
                // - AudioObj.SelectionStart/End and UpdateLoopFraction expect sample indices (interleaved floats)
                int channels = Math.Max(1, this.OriginalAudio.Channels);

                // frames per beat (SampleRangePerBeat already returns frames per beat)
                long framesPerBeat = Math.Max(1, this.SampleRangePerBeat);

                // compute distance in frames (at least 1 frame)
                long deltaFrames = Math.Max(1L, (long) Math.Round(Math.Abs(fraction) * framesPerBeat));

                // current playback frame (Position returns frames)
                long currentFrame = this.OriginalAudio.Position;

                long startFrame;
                long endFrame;

                if (fraction < 0f)
                {
                    // negative: go back by deltaFrames and loop that distance
                    startFrame = currentFrame - deltaFrames;
                    endFrame = startFrame + deltaFrames;
                }
                else
                {
                    // positive: loop forward from current position for deltaFrames
                    startFrame = currentFrame;
                    endFrame = currentFrame + deltaFrames;
                }

                // clamp to valid frame bounds
                long totalFrames = Math.Max(0L, this.OriginalAudio.Length / Math.Max(1, channels));
                startFrame = Math.Clamp(startFrame, 0L, Math.Max(0L, totalFrames - 1));
                endFrame = Math.Clamp(endFrame, startFrame + 1L, Math.Max(1L, totalFrames));

                // convert frames -> interleaved samples (AudioObj expects sample indices)
                long baseStartSamples = startFrame * channels;
                long baseEndSamples = endFrame * channels;
                long fractionSamples = Math.Max(1L, deltaFrames * (long) channels);

                // Apply loop and snap playback position if necessary (adjustPosition = true)
                this.OriginalAudio.UpdateLoopFraction(baseStartSamples, baseEndSamples, fractionSamples, true, true);
            }
            catch
            {
                // swallow errors to preserve UX (consistent with existing style in class)
            }
        }




    }
}
