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
		public static readonly float[] SemitoneSteps = [2.0f, 1.0f, 0.5f, 0.3333f, 0.25f, 0.2f, 0.1667f, 0.1429f, 0.125f, 0.1111f, 0.1f];

		private readonly AudioCollection AudioC = new();

		private int SamplesPerAudio => (int) ((float) this.numericUpDown_range.Value / (float.TryParse(this.domainUpDown_step.SelectedItem?.ToString(), out var flt) ? flt : 1.0f));


		internal AudioCollectionView? CollectionView { get; private set; } = null;


		private int ShiftKeysRange => (int) this.numericUpDown_range.Value;
		private float SemitoneDelta => float.TryParse(this.domainUpDown_step.SelectedItem?.ToString(), out float val) ? val : 1.0f;
		private bool useFftPv => this.checkBox_fftPv.Checked;


		public PitchShiftDialog(IEnumerable<AudioObj> samples)
		{
			this.InitializeComponent();

			this.domainUpDown_step.Items.AddRange(Array.ConvertAll(SemitoneSteps, s => s.ToString()));
			this.domainUpDown_step.SelectedIndex = this.domainUpDown_step.Items.IndexOf("0,5");

			foreach (AudioObj obj in samples)
			{
				this.AudioC.Audios.Add(obj.Clone());
			}

			this.listBox_samples.Items.Clear();
			this.listBox_samples.DataSource = this.AudioC.Audios;
			this.listBox_samples.DisplayMember = "Name";

			this.numericUpDown_take.Maximum = this.SamplesPerAudio;

			this.StartPosition = FormStartPosition.Manual;
			this.Location = WindowsScreenHelper.GetCornerPosition(this, false, false);

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
				int take = Math.Clamp((int) this.numericUpDown_take.Value, 1, samples.Count());

				// Snapshot als IList für effizienten Indexzugriff (falls bereits IList vorhanden, wiederverwenden)
				IList<AudioObj> list = samples is IList<AudioObj> l ? l : [.. samples];
				for (int i = 0; i < list.Count; i++)
				{
					if (i % take == 0)
					{
						this.CollectionView.AudioC.Audios.Add(list[i]);
					}
				}
			}

			this.CollectionView.Show();
			this.CollectionView.Rename("Pitch-Shifted " + (this.AudioC.Audios.Count == 1 ? ("'" + this.AudioC.Audios.FirstOrDefault()?.Name + "'") : "Samples"));
			this.progressBar_processing.Value = 0;

			if (!ctrlFlag)
			{
				this.Close();
			}
		}

		private void numericUpDown_range_ValueChanged(object sender, EventArgs e)
		{
			this.numericUpDown_take.Maximum = this.SamplesPerAudio;
		}

		private void domainUpDown_step_SelectedItemChanged(object sender, EventArgs e)
		{
			this.numericUpDown_take.Maximum = this.SamplesPerAudio;
		}
	}
}
