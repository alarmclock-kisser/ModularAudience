using ModularAudience.Audio;
using ModularAudience.Audio.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace ModularAudience.Forms
{
    public partial class OnnxDemucsDialog : Form
    {

        private readonly AudioObj OriginalAudio;
        private readonly DemucsOnnxService Onnx = new();


        public OnnxDemucsDialog(AudioObj audioObj)
        {
            this.InitializeComponent();

            this.OriginalAudio = audioObj;

            this.UpdateOnnxDemucsInfo();
        }




        private void UpdateOnnxDemucsInfo()
        {
            this.checkedListBox_stems.Items.Clear();
            this.checkedListBox_stems.Items.AddRange(this.Onnx.AvailableStems.ToArray());

            this.comboBox_models.Items.Clear();
            this.comboBox_models.Items.AddRange(this.Onnx.ModelPaths.ToArray());
            if (this.Onnx.IsOnline)
            {
                this.comboBox_models.Text = this.Onnx.ModelName;
                this.comboBox_models.Enabled = false;
            }

            this.FormClosing += (s, e) =>
            {
                this.Onnx.Dispose();
            };
        }

        private async void button_Run_Click(object sender, EventArgs e)
        {
            if (!this.Onnx.IsOnline)
            {
                MessageBox.Show("ONNX session is not initialized.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var selectedStems = new List<string>();
            foreach (var item in this.checkedListBox_stems.CheckedItems)
                selectedStems.Add(item.ToString()!);
            if (selectedStems.Count == 0)
            {
                MessageBox.Show("Please select at least one stem to separate.", "No Stems Selected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            this.progressBar_inferencing.Value = 0;
            this.progressBar_inferencing.Maximum = 1000;
            var progress = new Progress<double>(percent =>
            {
                this.progressBar_inferencing.Value = Math.Clamp((int) (percent * this.progressBar_inferencing.Maximum), 0, this.progressBar_inferencing.Maximum);
            });


            AudioCollectionView acv = new([]);
            foreach (var stem in selectedStems)
            {
                var extractor = this.Onnx.GetPartial(stem);
                float[] extraction = await extractor(this.OriginalAudio, progress);

                AudioObj extract = await this.OriginalAudio.CloneAsync();
                extract.Data = extraction;
                extract.Rename($"{this.OriginalAudio.OriginalName}_{stem}");
                acv.AudioC.Audios.Add(extract);

                this.progressBar_inferencing.Value = 0;
            }
        }

        private void comboBox_models_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (this.Onnx.IsOnline) return;

            if (this.comboBox_models.SelectedItem != null)
            {
                string selectedModel = this.comboBox_models.SelectedItem.ToString()!;
                this.Onnx.LoadModel(selectedModel);
                this.UpdateOnnxDemucsInfo();
            }
        }
    }
}
