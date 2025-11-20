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
    public partial class TrackView : Form
    {
        public readonly AudioObj OriginalAudio;

        public TrackView(AudioObj audio)
        {
            this.InitializeComponent();
            this.OriginalAudio = audio;


            this.FormClosing += async (s, e) =>
            {
                e.Cancel = true;
                this.Hide();
                this.OriginalAudio.Dispose();
            };
		}
    }
}
