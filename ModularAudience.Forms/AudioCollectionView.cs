using ModularAudience.Audio;
using ModularAudience.Forms.Modules;
using NAudience.Core;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace ModularAudience.Forms
{
    public partial class AudioCollectionView : Form
    {
        internal readonly AudioCollection AudioC = new();

        public int AudioCount => this.AudioC.Audios.Count;

        public AudioCollectionView(IEnumerable<AudioObj> audios)
        {
            this.InitializeComponent();
            this.StartPosition = FormStartPosition.Manual;

            this.Text = "Audio Collection #" + (WindowMain.CollectionViews.Count + 1).ToString("D2");

            foreach (AudioObj audio in audios)
            {
                this.AudioC.Audios.Add(audio);
            }

            this.listBox_audios.Items.Clear();
            this.listBox_audios.DataSource = this.AudioC.Audios;
            this.listBox_audios.DisplayMember = "Name";

            this.listBox_audios.SelectedIndex = -1;
            this.listBox_audios.MouseDown += this.listBox_audios_MouseDown;
            this.listBox_audios.DoubleClick += this.listBox_audios_DoubleClick;

            this.FormClosing += async (s, e) =>
            {
                e.Cancel = true;
                this.Hide();
                await this.AudioC.ClearAsync();
            };
        }




        // ListBox entry richt-click event to show context menu for rename and delete
        private void listBox_audios_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                int index = this.listBox_audios.IndexFromPoint(e.Location);
                if (index != ListBox.NoMatches)
                {
                    this.listBox_audios.SelectedIndex = index;
                    ContextMenuStrip contextMenu = new();
                    ToolStripMenuItem renameItem = new("Rename");
                    renameItem.Click += (s, ev) =>
                    {
                        AudioObj? selectedAudio = (AudioObj?) this.listBox_audios.SelectedItem;
                        if (selectedAudio != null)
                        {
                            string input = Microsoft.VisualBasic.Interaction.InputBox("Enter new name:", "Rename Audio", selectedAudio.Name);
                            if (!string.IsNullOrWhiteSpace(input))
                            {
                                selectedAudio.Name = input;
                                this.listBox_audios.Refresh();
                            }
                        }
                    };
                    ToolStripMenuItem deleteItem = new("Delete");
                    deleteItem.Click += async (s, ev) =>
                    {
                        AudioObj? selectedAudio = (AudioObj?) this.listBox_audios.SelectedItem;
                        if (selectedAudio != null)
                        {
                            var result = MessageBox.Show($"Are you sure you want to delete '{selectedAudio.Name}'?", "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                            if (result == DialogResult.Yes)
                            {
                                await this.AudioC.RemoveAsync(selectedAudio.Id);
                                this.listBox_audios.DataSource = null;
                                this.listBox_audios.DataSource = this.AudioC.Audios;
                                this.listBox_audios.DisplayMember = "Name";
                            }
                        }
                    };
                    contextMenu.Items.AddRange([renameItem, deleteItem]);
                    contextMenu.Show(this.listBox_audios, e.Location);
                }
            }
        }

        // ListBox entry double-click event to create TrackView from selected audio
        private void listBox_audios_DoubleClick(object? sender, EventArgs e)
        {
            AudioObj? selectedAudio = (AudioObj?) this.listBox_audios.SelectedItem;
            if (selectedAudio != null)
            {
                WindowMain.TrackViews.Add(new TrackView(selectedAudio));
            }
        }
    }
}
