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
        private AudioCollectionView? CollectionView = null;

        private TrackView? CurrentTrackView => WindowMain.LastSelectedTrackView;
        private AudioObj? OriginalAudio => this.CurrentTrackView?.OriginalAudio;
        private float Bpm => this.OriginalAudio?.Bpm > 0 ? this.OriginalAudio.Bpm : this.OriginalAudio?.ScannedBpm > 0 ? this.OriginalAudio.ScannedBpm : 120f;
        private int SampleRangePerBeat => this.OriginalAudio != null ? (int) (this.OriginalAudio.SampleRate * 60f / this.Bpm) * this.OriginalAudio.Channels : 44100;
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


        private readonly HashSet<Control> autoRefocusAttached = [];
        private readonly HashSet<Control> containerMonitored = [];



        public LoopControl()
        {
            this.InitializeComponent();

            this.StartPosition = FormStartPosition.Manual;
            this.Location = WindowsScreenHelper.GetCenterStartingPoint(this);

            this.BuildLoopControlButtons();
            this.EnableAutoRefocusForContainer(this);


            this.UpdateLoopButtonsState();

            this.FormClosing += (s, e) =>
            {
                // Hide instead of close
                WindowMain.LoopControlWindow = null;
                this.Hide();
            };

        }



        private void BuildLoopControlButtons()
        {
            var template = this.button_loop;
            if (template == null || this.panel_buttons == null)
            {
                return;
            }

            template.Visible = false;

            string[] buttonLabels = ["16", "8", "4", "2", "1", "/2", "/4", "/8", "/8", "/4", "/2", "1", "2", "4", "8", "16"];
            float[] fractions =
            [
                -8f, -4f, -2f, -1f, -0.5f, -0.25f, -0.125f, -0.0625f,
                 0.0625f, 0.125f, 0.25f, 0.5f, 1f, 2f, 4f, 8f
            ];

            const int buttonCount = 16;

            this.panel_buttons.SuspendLayout();

            var toRemove = this.panel_buttons.Controls.OfType<Button>()
                .Where(b => !ReferenceEquals(b, template))
                .ToList();
            foreach (var b in toRemove)
            {
                this.panel_buttons.Controls.Remove(b);
                b.Dispose();
            }

            var created = new List<Button>(buttonCount);

            for (int i = 0; i < buttonCount; i++)
            {
                var copy = new Button
                {
                    Font = template.Font,
                    Size = template.Size,
                    BackColor = template.BackColor,
                    ForeColor = template.ForeColor,
                    FlatStyle = template.FlatStyle,
                    Image = template.Image,
                    ImageAlign = template.ImageAlign,
                    TextAlign = template.TextAlign,
                    Padding = template.Padding,
                    Margin = template.Margin,
                    UseVisualStyleBackColor = template.UseVisualStyleBackColor
                };
                copy.FlatAppearance.BorderSize = template.FlatAppearance.BorderSize;
                copy.FlatAppearance.MouseDownBackColor = template.FlatAppearance.MouseDownBackColor;
                copy.FlatAppearance.MouseOverBackColor = template.FlatAppearance.MouseOverBackColor;

                copy.Text = buttonLabels[i];
                copy.Tag = fractions[i].ToString(System.Globalization.CultureInfo.InvariantCulture);
                copy.TabStop = false;
                copy.Anchor = AnchorStyles.Top | AnchorStyles.Left;

                copy.Click += this.LoopButton_Click;

                this.panel_buttons.Controls.Add(copy);
                created.Add(copy);
            }

            void LayoutButtons()
            {
                if (this.panel_buttons.ClientSize.Width <= 0 || created.Count == 0)
                {
                    return;
                }

                int panelWidth = this.panel_buttons.ClientSize.Width;
                int panelHeight = this.panel_buttons.ClientSize.Height;

                int spacing = Math.Max(0, template.Margin.Right);
                int totalSpacing = spacing * (created.Count - 1);
                int availableWidth = Math.Max(1, panelWidth - totalSpacing);
                int btnWidth = Math.Max(8, availableWidth / created.Count);

                int btnHeight = template.Height;
                int contentWidth = btnWidth * created.Count + totalSpacing;
                int startX = Math.Max(0, (panelWidth - contentWidth) / 2);
                int y = Math.Max(0, (panelHeight - btnHeight) / 2);

                int x = startX;
                for (int i = 0; i < created.Count; i++)
                {
                    var btn = created[i];
                    btn.SetBounds(x, y, btnWidth, btnHeight);
                    x += btnWidth + spacing;
                }
            }

            LayoutButtons();
            this.panel_buttons.Resize += (s, e) => LayoutButtons();

            this.panel_buttons.ResumeLayout();
        }



        private void LoopButton_Click(object? sender, EventArgs e)
        {
            Button? clickedButton = (Button?) sender;
            if (clickedButton == null)
            {
                return;
            }

            clickedButton.BackColor = clickedButton.BackColor != Color.LightBlue ? Color.LightBlue : SystemColors.Control;

            // Untoggle all other buttons
            this.UntoggleAllOtherButtons(clickedButton);

            // Set loop range
            this.SetLoopRange();

            // Focus TrackView
            this.CurrentTrackView?.Focus();
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



        internal void UpdateLoopButtonsState()
        {
            // Guard
            if (this.CurrentTrackView == null || this.OriginalAudio == null)
            {
                // Disable all buttons
                foreach (var btn in this.panel_buttons.Controls.OfType<Button>())
                {
                    btn.Enabled = false;
                }
                return;
            }

            // Enable all buttons
            foreach (var btn in this.panel_buttons.Controls.OfType<Button>())
            {
                btn.Enabled = true;
            }

            if (this.OriginalAudio.LoopFraction != 0)
            {
                // Find the button that matches the current loop fraction and toggle it
                float targetFraction = this.OriginalAudio.LoopFraction;
                Button? matchingButton = this.panel_buttons.Controls.OfType<Button>()
                    .FirstOrDefault(b =>
                    {
                        string tag = b.Tag?.ToString() ?? "0";
                        // Try invariant parse first, fallback to current culture
                        if (float.TryParse(tag, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float val))
                        {
                            return Math.Abs(val - targetFraction) < 0.0001f;
                        }
                        if (float.TryParse(tag, out val))
                        {
                            return Math.Abs(val - targetFraction) < 0.0001f;
                        }
                        return false;
                    });
                if (matchingButton != null)
                {
                    matchingButton.BackColor = Color.LightBlue;
                    // Untoggle all other buttons
                    this.UntoggleAllOtherButtons(matchingButton);
                }
            }
            else
            {
                // No loop active, untoggle all buttons
                this.UntoggleAllOtherButtons(null);
            }
        }

        private async void button_copy_Click(object sender, EventArgs e)
        {
            if (this.OriginalAudio == null)
            {
                return;
            }

            var copiedLoop = await this.OriginalAudio.CreateLoopAsync();

            if (copiedLoop != null)
            {
                this.CollectionView ??= new AudioCollectionView([]);
                this.CollectionView.AudioC.Audios.Add(copiedLoop);
                this.CollectionView.Rename("Loop" + (this.CollectionView.AudioC.Audios.Count == 1 ? "" : "(s)") + " - '" + this.OriginalAudio.OriginalName + "'");
            }
        }





        // Hilfsmethode: hängt Click/MouseUp-Handler an ein Control, der danach die TrackView refokussiert
        private void AttachAutoRefocusToControl(Control ctrl)
        {
            if (ctrl == null) return;

            // Verhindere Mehrfach-Anhänge
            if (this.autoRefocusAttached.Contains(ctrl)) return;
            this.autoRefocusAttached.Add(ctrl);

            // Handler, der nach Abschluss des aktuellen UI-Event-Dispatchs den Fokus zurücksetzt
            void RestoreFocusDeferred()
            {
                try
                {
                    // BeginInvoke stellt sicher, dass das auslösende Event komplett ausgeführt wurde
                    this.BeginInvoke((Action) (() =>
                    {
                        try { this.CurrentTrackView?.Focus(); } catch { }
                    }));
                }
                catch { }
            }

            try
            {
                // Klick / MouseUp abdecken
                ctrl.Click += (s, e) => RestoreFocusDeferred();
                ctrl.MouseUp += (s, e) => RestoreFocusDeferred();

                // Manche Controls (z.B. ToolStripItem) sind keine Controls — hier behandeln wir normale Controls.
                // Falls ein Control Kinder hat, werden diese beim initialen Rekursionslauf ohnehin angehängt.
            }
            catch { }
        }

        private void EnableAutoRefocusForContainer(Control container)
        {
            if (container == null) return;

            // Verhindere mehrfaches Registrieren desselben Containers
            if (this.containerMonitored.Contains(container)) return;
            this.containerMonitored.Add(container);

            try
            {
                // Existierende Kinder anhängen (rekursiv)
                foreach (Control c in container.Controls)
                {
                    try
                    {
                        AttachAutoRefocusToControl(c);

                        if (c.HasChildren)
                        {
                            // Rekursiv auf Unter-Container anwenden
                            EnableAutoRefocusForContainer(c);
                        }
                    }
                    catch { }
                }

                // Event, damit zukünftige hinzugefügte Controls ebenfalls automatisch abgedeckt werden
                container.ControlAdded += (s, e) =>
                {
                    try
                    {
                        if (e.Control == null) return;
                        AttachAutoRefocusToControl(e.Control);
                        if (e.Control.HasChildren)
                        {
                            EnableAutoRefocusForContainer(e.Control);
                        }
                    }
                    catch { }
                };
            }
            catch { }
        }
    }
}
