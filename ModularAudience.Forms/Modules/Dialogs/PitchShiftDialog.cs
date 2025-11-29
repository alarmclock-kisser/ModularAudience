using ModularAudience.Audio;
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

        internal readonly AudioCollection AudioC_results = new();


        private int ShiftKeysRange => (int) this.numericUpDown_range.Value;


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

        }
    }
}
