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
    public partial class LlmBeatBreakerDialog : Form
    {
        private AudioCollection Audios = new();


        public LlmBeatBreakerDialog(IEnumerable<AudioObj> samples)
        {
            this.InitializeComponent();
            foreach (AudioObj audio in samples)
            {
                this.Audios.Audios.Add(audio.Clone());
            }
        }
    }
}
