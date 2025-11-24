using ModularAudience.Audio;
using NAudience.Core;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace ModularAudience.Forms.Modules
{
    public partial class DrumRollEditor : Form
    {
        private readonly AudioCollection AudioC = new();

        public float Bpm => (float) this.numericUpDown_bpm.Value;
        public int Hits => this.domainUpDown_hits.SelectedItem is null ? 16 : int.Parse(this.domainUpDown_hits.SelectedItem.ToString() ?? "16");


        internal BindingList<Panel> Panels = [];


        public DrumRollEditor(IEnumerable<AudioObj>? samples = null)
        {
            this.InitializeComponent();
            this.panel_pattern.Visible = false;
            this.button_hit.Visible = false;
            this.domainUpDown_hits.SelectedIndex = this.domainUpDown_hits.Items.IndexOf("16");

            if (samples != null)
            {
                foreach (AudioObj sample in samples)
                {
                    this.AudioC.Audios.Add(sample.Clone());
                }
            }

            this.StartPosition = FormStartPosition.Manual;
            this.Location = WindowsScreenHelper.GetCenterStartingPoint(this);

            this.AllowDrop = true;
            this.DragEnter += this.DrumRollEditor_DragEnter;
            this.DragDrop += this.DrumRollEditor_DragDrop;
            this.FormClosing += this.Form_Closing;

            // Event für Änderungen an der Audios-Liste
            this.AudioC.Audios.ListChanged += this.AudioC_Audios_ListChanged;
            // Event für Änderung der Hits (Beats)
            this.domainUpDown_hits.SelectedItemChanged += this.domainUpDown_hits_SelectedItemChanged;

            // Initial Panels bauen, falls Samples vorhanden
            _ = this.RebuildPatternPanelsAsync();
        }

        private void Form_Closing(object? sender, FormClosingEventArgs e)
        {
            // Events entfernen
            this.AudioC.Audios.ListChanged -= this.AudioC_Audios_ListChanged;
            this.domainUpDown_hits.SelectedItemChanged -= this.domainUpDown_hits_SelectedItemChanged;
            this.DragEnter -= this.DrumRollEditor_DragEnter;
            this.DragDrop -= this.DrumRollEditor_DragDrop;
            WindowMain.DrumRoll = null;
        }

        private void button_playback_Click(object sender, EventArgs e)
        {

        }

        private async void AudioC_Audios_ListChanged(object? sender, ListChangedEventArgs e)
        {
            await this.RebuildPatternPanelsAsync();
        }

        private async void domainUpDown_hits_SelectedItemChanged(object? sender, EventArgs e)
        {
            await this.RebuildPatternPanelsAsync();
        }

        private void DrumRollEditor_DragEnter(object? sender, DragEventArgs e)
        {
            if (e.Data != null)
            {
                if (e.Data.GetDataPresent(typeof(AudioObj)) ||
                    e.Data.GetDataPresent(typeof(List<AudioObj>)) ||
                    e.Data.GetDataPresent(typeof(IEnumerable<AudioObj>)) ||
                    e.Data.GetDataPresent(typeof(ListBox.SelectedObjectCollection)))
                {
                    e.Effect = DragDropEffects.Copy;
                    return;
                }
                if (e.Data.GetDataPresent(DataFormats.Serializable))
                {
                    var data = e.Data.GetData(DataFormats.Serializable);
                    if (data is AudioObj || data is IEnumerable<AudioObj>)
                    {
                        e.Effect = DragDropEffects.Copy;
                        return;
                    }
                }
            }
            e.Effect = DragDropEffects.None;
        }

        private void DrumRollEditor_DragDrop(object? sender, DragEventArgs e)
        {
            if (e.Data == null)
            {
                return;
            }

            // Einzelnes AudioObj
            if (e.Data.GetDataPresent(typeof(AudioObj)))
            {
                var audio = e.Data.GetData(typeof(AudioObj)) as AudioObj;
                if (audio != null && !this.AudioC.Audios.Contains(audio))
                {
                    this.AudioC.Audios.Add(audio);
                }
                return;
            }
            // Liste von AudioObj
            if (e.Data.GetDataPresent(typeof(List<AudioObj>)))
            {
                var audioList = e.Data.GetData(typeof(List<AudioObj>)) as List<AudioObj>;
                if (audioList != null)
                {
                    foreach (var audio in audioList)
                    {
                        if (!this.AudioC.Audios.Contains(audio))
                        {
                            this.AudioC.Audios.Add(audio);
                        }
                    }
                }
                return;
            }
            // IEnumerable<AudioObj>
            if (e.Data.GetDataPresent(typeof(IEnumerable<AudioObj>)))
            {
                var enumerable = e.Data.GetData(typeof(IEnumerable<AudioObj>)) as IEnumerable<AudioObj>;
                if (enumerable != null)
                {
                    foreach (var audio in enumerable)
                    {
                        if (audio is AudioObj a && !this.AudioC.Audios.Contains(a))
                        {
                            this.AudioC.Audios.Add(a);
                        }
                    }
                }
                return;
            }
            // Drag aus ListBox.SelectedObjectCollection
            if (e.Data.GetDataPresent(typeof(ListBox.SelectedObjectCollection)))
            {
                var selected = e.Data.GetData(typeof(ListBox.SelectedObjectCollection)) as ListBox.SelectedObjectCollection;
                if (selected != null)
                {
                    foreach (var item in selected)
                    {
                        if (item is AudioObj audio && !this.AudioC.Audios.Contains(audio))
                        {
                            this.AudioC.Audios.Add(audio);
                        }
                    }
                }
                return;
            }
            // Drag als Serializable
            if (e.Data.GetDataPresent(DataFormats.Serializable))
            {
                var data = e.Data.GetData(DataFormats.Serializable);
                if (data is AudioObj audio && !this.AudioC.Audios.Contains(audio))
                {
                    this.AudioC.Audios.Add(audio);
                    return;
                }
                if (data is IEnumerable<AudioObj> list)
                {
                    foreach (var a in list)
                    {
                        if (!this.AudioC.Audios.Contains(a))
                        {
                            this.AudioC.Audios.Add(a);
                        }
                    }
                    return;
                }
            }
        }

        private async Task RebuildPatternPanelsAsync()
        {
            // Panels entfernen
            foreach (var panel in this.Panels)
            {
                if (panel.Parent != null)
                {
                    panel.Parent.Controls.Remove(panel);
                }
                panel.Dispose();
            }
            this.Panels.Clear();

            int audioCount = this.AudioC.Audios.Count;
            if (audioCount == 0)
            {
                this.panel_pattern.Visible = false;
                return;
            }

            // Layout-Parameter
            int availableHeight = this.ClientSize.Height - this.panel_pattern.Top - 20; // ClientSize für exakte Fläche
            int minPanelHeight = 20;
            int maxPanelHeight = 75;
            int panelSpacing = 2; // Abstand zwischen Panels
            int totalSpacing = (audioCount - 1) * panelSpacing;
            int panelHeight = Math.Max(minPanelHeight, Math.Min(maxPanelHeight, (availableHeight - totalSpacing) / audioCount));
            int width = this.panel_pattern.Width;
            int hits = this.Hits;

            // Panel-Vorlage entfernen (Designer-Button und alles aus panel_pattern löschen)
            this.panel_pattern.Controls.Clear();

            int y = this.panel_pattern.Top;
            for (int i = 0; i < audioCount; i++)
            {
                var audio = this.AudioC.Audios[i];
                Panel panel = new()
                {
                    Size = new Size(width, panelHeight),
                    Location = new Point(this.panel_pattern.Left, y),
                    Visible = true,
                    BackColor = (i % 2 == 0) ? Color.FromArgb(245, 245, 245) : Color.FromArgb(230, 230, 230)
                };

                // Rechtsklick-Menü für Entfernen und Edit Sample
                ContextMenuStrip cms = new();
                var removeItem = new ToolStripMenuItem("Remove");
                removeItem.Click += async (s, e) =>
                {
                    this.AudioC.Audios.Remove(audio);
                    await this.RebuildPatternPanelsAsync();
                };
                var editItem = new ToolStripMenuItem("Edit Sample");
                editItem.Click += async (s, e) =>
                {
                    // TrackView öffnen
                    using var tv = new TrackView(audio);
                    var dlgResult = tv.ShowDialog(this);
                    if (dlgResult == DialogResult.OK || dlgResult == DialogResult.None)
                    {
                        // Ersetztes Audio holen (ggf. Property OriginalAudio oder bearbeitetes Audio)
                        var edited = tv.OriginalAudio ?? audio;
                        int idx = this.AudioC.Audios.IndexOf(audio);
                        if (idx >= 0 && edited != null && !ReferenceEquals(audio, edited))
                        {
                            this.AudioC.Audios[idx] = edited;
                            await this.RebuildPatternPanelsAsync();
                        }
                    }
                };
                cms.Items.Add(editItem);
                cms.Items.Add(removeItem);
                panel.ContextMenuStrip = cms;

                // Label für Audio-Namen
                Label label = new()
                {
                    Text = audio.Name,
                    AutoSize = false,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Location = new Point(5, 0),
                    Size = new Size(Math.Min(120, width / 4), panelHeight),
                    Font = new Font(this.Font.FontFamily, Math.Max(8, panelHeight / 3), FontStyle.Bold)
                };
                panel.Controls.Add(label);

                // Buttons für jeden Hit
                int buttonAreaLeft = label.Right + 5;
                int buttonAreaWidth = width - buttonAreaLeft - 5;
                int buttonWidth = Math.Max(12, (buttonAreaWidth - (hits - 1) * 3) / hits);
                int buttonHeight = Math.Max(12, panelHeight - 10);
                for (int h = 0; h < hits; h++)
                {
                    Button button = new()
                    {
                        Size = new Size(buttonWidth, buttonHeight),
                        Location = new Point(buttonAreaLeft + h * (buttonWidth + 3), 5),
                        BackColor = Color.LightGray
                    };
                    // Button-Beschriftung: ab 21 Hits keine Beschriftung mehr
                    if (hits > 20)
                    {
                        button.Text = string.Empty;
                    }
                    else
                    {
                        string numStr = (h + 1).ToString();
                        if (buttonWidth < 22 && numStr.Length == 2)
                        {
                            button.Text = numStr;
                            button.Font = new Font(this.Font.FontFamily, 6, FontStyle.Regular);
                        }
                        else if (buttonWidth < 30)
                        {
                            button.Text = numStr;
                            button.Font = new Font(this.Font.FontFamily, 7, FontStyle.Regular);
                        }
                        else
                        {
                            button.Text = numStr;
                            button.Font = new Font(this.Font.FontFamily, 8, FontStyle.Regular);
                        }
                        button.UseCompatibleTextRendering = true;
                    }
                    button.Click += (s, e) =>
                    {
                        button.BackColor = button.BackColor == Color.LightGray ? Color.Green : Color.LightGray;
                    };
                    panel.Controls.Add(button);
                }

                this.Controls.Add(panel);
                this.Panels.Add(panel);
                y += panelHeight + panelSpacing;
            }

            this.panel_pattern.Visible = false;
        }




    }
}
