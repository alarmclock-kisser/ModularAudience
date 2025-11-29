using ModularAudience.Audio;
using ModularAudience.Generators;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace ModularAudience.Forms.Modules.Dialogs
{
    public partial class BreakbeatGeneratorDialog : Form
    {
        internal readonly AudioCollection AudioC = new();
        internal AudioCollectionView? CollectionView { get; private set; } = null;

        internal AudioObj? SelectedTrack => this.listBox_samples.SelectedItem as AudioObj;

        private bool AutoPlayEnabled => this.checkBox_autoPlay.Checked;
        private int Bars => (int) this.numericUpDown_bars.Value;
        private int Bpm => (int) this.numericUpDown_bpm.Value;
        private float Density => (float) this.numericUpDown_density.Value;
        private int Resolution => (int) this.numericUpDown_resolution.Value;
        private float Swing => (float) this.numericUpDown_swing.Value;
        private float Complexity => (float) this.numericUpDown_complexity.Value;
        private int Seed => (int) this.numericUpDown_seed.Value;



        public BreakbeatGeneratorDialog(IEnumerable<AudioObj> samples)
        {
            this.InitializeComponent();

            foreach (AudioObj obj in samples)
            {
                this.AudioC.Audios.Add(obj.Clone());
            }

            this.comboBox_drumset.DataSource = Enum.GetValues<DrumsetElement>();

            this.listBox_samples.DataSource = this.AudioC.Audios;
            this.listBox_samples.DisplayMember = "Name";
            this.listBox_samples.SelectedIndex = this.listBox_samples.Items.Count > 0 ? 0 : -1;
            this.listBox_samples.DrawItem += this.listBox_samples_DrawItem;

            this.numericUpDown_seed.Value = new Random().Next(0, 999999998);

            this.FormClosing += (s, e) =>
            {
                this.AudioC.Dispose();
            };
        }

        private async void listBox_samples_SelectedIndexChanged(object sender, EventArgs e)
        {
            this.comboBox_drumset.SelectedItem = this.SelectedTrack?.Tag ?? null;
            if (this.AutoPlayEnabled && this.SelectedTrack is not null)
            {
                // Only play if mouse is down on item (to avoid playing when changing selection programmatically)
                if (Control.MouseButtons == MouseButtons.Left && this.listBox_samples.SelectedIndex >= 0)
                {
                    await this.SelectedTrack.PlayAsync(CancellationToken.None);
                }
            }
        }

        private void comboBox_drumset_SelectedIndexChanged(object sender, EventArgs e)
        {
            // Set tag to selected audioobj
            if (this.SelectedTrack is not null)
            {
                this.SelectedTrack.Tag = this.comboBox_drumset.SelectedItem;
            }
        }

        private void button_autoMap_Click(object sender, EventArgs e)
        {
            string[] sampleNames = this.AudioC.Audios.Select(a => a.Name).ToArray();
            DrumsetElement[] drumsetMapping = BreakbeatGenerator.MatchSampleNamesToDrumsetElements(sampleNames);

            // Set tag to every audioobj
            for (int i = 0; i < this.AudioC.Audios.Count; i++)
            {
                this.AudioC.Audios[i].Tag = drumsetMapping[i];
            }

            this.listBox_samples.SelectedIndex = -1;
            this.listBox_samples.SelectedIndex = this.listBox_samples.Items.Count > 0 ? 0 : -1;
        }

        private void button_edit_Click(object sender, EventArgs e)
        {
            if (this.SelectedTrack is null)
            {
                return;
            }

            var tv = new TrackView(this.SelectedTrack, this.AudioC);
            tv.Show();
        }




        // Special listBox drawItem event to draw AudioObjs that have a tag in grey instead
        private void listBox_samples_DrawItem(object? sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= this.listBox_samples.Items.Count)
            {
                return;
            }
            AudioObj item = (AudioObj) this.listBox_samples.Items[e.Index];
            // Determine the color based on whether the item has a tag
            Color textColor = item.Tag is not null ? Color.Gray : e.ForeColor;
            // Draw the background
            e.DrawBackground();
            // Draw the text
            using (Brush textBrush = new SolidBrush(textColor))
            {
                if (e.Font != null)
                {
                    e.Graphics.DrawString(item.Name, e.Font, textBrush, e.Bounds);
                }
            }
            // Draw the focus rectangle if the item is focused
            e.DrawFocusRectangle();
        }

        private async void button_go_Click(object sender, EventArgs e)
        {
            bool ctrlFlag = (ModifierKeys & Keys.Control) == Keys.Control;

            DrumsetElement[] mappedDrumset = new DrumsetElement[this.AudioC.Audios.Count];
            for (int i = 0; i < this.AudioC.Audios.Count; i++)
            {
                if (this.AudioC.Audios[i].Tag is DrumsetElement de)
                {
                    mappedDrumset[i] = de;
                }
                else
                {
                    LogCollection.Log($"AudioObj '{this.AudioC.Audios[i].Name}' does not have a DrumsetElement mapping. Using 'Snare'.");
                    mappedDrumset[i] = DrumsetElement.Snare;
                }
            }

            LogCollection.Log("Generating Break-Beat with seed: " + this.Seed);

            List<bool[]> breakbeat = await BreakbeatGenerator.GenerateBreakPatternAsync(
                drumset: mappedDrumset,
                bars: this.Bars,
                density: this.Density,
                resolution: this.Resolution,
                swing: this.Swing,
                complexity: this.Complexity,
                seed: this.Seed
            );

            var audioObj = await BreakbeatGenerator.RenderBreakbeatAsync(breakbeat, this.AudioC.Audios, this.Bpm, this.Resolution, this.Swing);

            if (audioObj == null)
            {
                LogCollection.Log("Failed to generate breakbeat audio.");
                return;
            }

            this.CollectionView ??= new AudioCollectionView([]);
            this.CollectionView.AudioC.Audios.Add(audioObj);
            this.CollectionView.Show();
            this.CollectionView.Rename("Break-Beat(s) Generated");

            if (!ctrlFlag)
            {
                this.numericUpDown_seed.Value = new Random().Next(0, 999999998);
            }
        }

        private void numericUpDown_seed_ValueChanged(object sender, EventArgs e)
        {
            // Rekursion verhindern, indem wir ein Tag-Flag setzen
            if (this.numericUpDown_seed.Tag is bool busy && busy)
            {
                return;
            }

            try
            {
                this.numericUpDown_seed.Tag = true;
                this.numericUpDown_seed.Value = new Random().Next(0, 999999998);
            }
            finally
            {
                this.numericUpDown_seed.Tag = false;
            }
        }
    }
}
