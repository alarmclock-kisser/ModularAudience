using ModularAudience.Audio;
using ModularAudience.Audio.Processors_V1;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace ModularAudience.Forms.Modules.Dialogs
{
    public partial class PitchShiftDialog : Form
    {
        private readonly AudioCollection AudioC = new();


        internal AudioCollectionView? CollectionView { get; private set; } = null;


		private int ShiftKeysRange => (int) this.numericUpDown_range.Value;
        private float SemitoneDelta => (float) this.numericUpDown_delta.Value;
        private bool useFftPv => this.checkBox_fftPv.Checked;


		public PitchShiftDialog(IEnumerable<AudioObj> samples)
        {
            this.InitializeComponent();

            foreach (AudioObj obj in samples)
            {
                this.AudioC.Audios.Add(obj.Clone());
            }

            this.listBox_samples.Items.Clear();
            this.listBox_samples.DataSource = this.AudioC.Audios;
            this.listBox_samples.DisplayMember = "Name";

            this.FormClosing += (s, e) =>
            {
                this.AudioC.Dispose();
            };
        }

        private async void button_create_Click(object sender, EventArgs e)
        {
            
            bool ctrlFlag = ModifierKeys.HasFlag(Keys.Control);

			IProgress<double> progress = new Progress<double>(p =>
            {
                int percent = (int) (p * this.progressBar_processing.Maximum);
                
                this.progressBar_processing.Value = Math.Min(percent, this.progressBar_processing.Maximum);
			});

			var pitchedSamples = await PitchShifter.CreatePitchShiftsBatchAsync(this.AudioC.Audios, this.ShiftKeysRange, this.SemitoneDelta, this.useFftPv, progress);

            this.CollectionView ??= new AudioCollectionView([]);
            foreach (var samples in pitchedSamples)
            {
                foreach (var sample in samples)
                {
                    this.CollectionView.AudioC.Audios.Add(sample);
				}
			}

            this.CollectionView.Show();
            this.CollectionView.Rename("Pitch-Shifted Samples");
            this.progressBar_processing.Value = 0;

            if (!ctrlFlag)
            {
                this.Close();
			}
		}
    }
}
