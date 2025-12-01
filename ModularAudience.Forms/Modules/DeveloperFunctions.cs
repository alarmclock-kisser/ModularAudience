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
    public partial class DeveloperFunctions : Form
    {
        private TrackView? CurrentTrackView => WindowMain.LastSelectedTrackView;
        private AudioObj? SelectedAudio => this.comboBox_track.SelectedIndex <= 0 ? this.CurrentTrackView?.OriginalAudio : WindowMain.TrackViews.Where(tv => !tv.IsDisposed && tv.OriginalAudio != null).Select(tv => tv.OriginalAudio).Where(a => a.Duration > TimeSpan.Zero && a.Data.LongLength > 0).ElementAtOrDefault(this.comboBox_track.SelectedIndex - 1);


        internal string? SelectedMethod => this.comboBox_methods.SelectedItem as string;



        public DeveloperFunctions()
        {
            this.InitializeComponent();

            this.StartPosition = FormStartPosition.Manual;
            this.Location = WindowsScreenHelper.GetCornerPosition(this, true, false);

            this.FillComboBoxMethods(this.comboBox_methods);
            this.UpdateControlStates();
        }




        internal void UpdateControlStates()
        {
            this.FillComboBoxTracks(this.comboBox_track);


        }





        private void FillComboBoxMethods(ComboBox comboBox)
        {
            comboBox.Items.Clear();


        }

        private void FillComboBoxTracks(ComboBox comboBox)
        {
            int selectedIndex = comboBox.SelectedIndex;

            comboBox.SuspendLayout();

            comboBox.Items.Clear();
            comboBox.Items.Add("Auto last focussed track");

            var openTracks = WindowMain.TrackViews.Where(tv => !tv.IsDisposed && tv.OriginalAudio != null).Select(tv => tv.OriginalAudio).Where(a => a.Duration > TimeSpan.Zero && a.Data.LongLength > 0).ToList();
            string[] trackNames = openTracks.Select(a => a.OriginalName).ToArray();
            comboBox.Items.AddRange(trackNames);

            comboBox.SelectedIndex = comboBox.Items.Count > selectedIndex ? selectedIndex : 0;

            comboBox.ResumeLayout();
        }

        private void comboBox_track_SelectedIndexChanged(object sender, EventArgs e)
        {
            this.label_trackName.Text = this.SelectedAudio != null ? $"'{this.SelectedAudio.OriginalName}'" : "No track currently selected.";
        }
    }
}
