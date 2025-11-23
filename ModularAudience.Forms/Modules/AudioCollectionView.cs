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

        private Point? _dragOrigin;
        private AudioObj? _dragCandidate;
        private int _hoverInsertIndex = -1;
        private int _preDragSelection = -1;

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
            this.listBox_audios.AllowDrop = true;
            this.listBox_audios.MouseDown += this.listBox_audios_MouseDown;
            this.listBox_audios.MouseMove += this.listBox_audios_MouseMove;
            this.listBox_audios.MouseUp += this.listBox_audios_MouseUp;
            this.listBox_audios.DoubleClick += this.listBox_audios_DoubleClick;
            this.listBox_audios.DragEnter += this.listBox_audios_DragEnter;
            this.listBox_audios.DragOver += this.listBox_audios_DragOver;
            this.listBox_audios.DragDrop += this.listBox_audios_DragDrop;
            this.listBox_audios.DragLeave += this.listBox_audios_DragLeave;
            this.listBox_audios.GiveFeedback += this.listBox_audios_GiveFeedback;

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
            if (e.Button == MouseButtons.Left)
            {
                int index = this.listBox_audios.IndexFromPoint(e.Location);
                if (index != ListBox.NoMatches)
                {
                    this.listBox_audios.SelectedIndex = index;
                    this._preDragSelection = index;
                    this._dragOrigin = e.Location;
                    this._dragCandidate = (AudioObj?) this.listBox_audios.SelectedItem;
                }
                else
                {
                    this.ResetDragState();
                }
                return;
            }

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
                            await this.AudioC.RemoveAsync(selectedAudio.Id);
                            this.listBox_audios.DataSource = null;
                            this.listBox_audios.DataSource = this.AudioC.Audios;
                            this.listBox_audios.DisplayMember = "Name";
                        }
                    };
                    contextMenu.Items.AddRange([renameItem, deleteItem]);
                    contextMenu.Show(this.listBox_audios, e.Location);
                }
            }
        }

        private void listBox_audios_MouseMove(object? sender, MouseEventArgs e)
        {
            if (!this._dragOrigin.HasValue || (e.Button & MouseButtons.Left) != MouseButtons.Left || this._dragCandidate == null)
            {
                return;
            }

            Size dragSize = SystemInformation.DragSize;
            Rectangle dragRect = new(
                this._dragOrigin.Value.X - dragSize.Width / 2,
                this._dragOrigin.Value.Y - dragSize.Height / 2,
                dragSize.Width,
                dragSize.Height);

            if (!dragRect.Contains(e.Location))
            {
                this.BeginAudioDrag();
            }
        }

        private void listBox_audios_MouseUp(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                this.ResetDragState();
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

        private void listBox_audios_DragEnter(object? sender, DragEventArgs e)
        {
            if (TryGetDragPayload(e.Data, out _))
            {
                e.Effect = DragDropEffects.Move;
                this.listBox_audios.Cursor = Cursors.Hand;
            }
            else
            {
                e.Effect = DragDropEffects.None;
                this.listBox_audios.Cursor = Cursors.No;
            }
        }

        private void listBox_audios_DragOver(object? sender, DragEventArgs e)
        {
            if (!TryGetDragPayload(e.Data, out _))
            {
                e.Effect = DragDropEffects.None;
                return;
            }

            e.Effect = DragDropEffects.Move;
            Point clientPoint = this.listBox_audios.PointToClient(new Point(e.X, e.Y));
            int insertIndex = this.GetInsertionIndex(clientPoint);
            this.HighlightInsertionIndex(insertIndex);
        }

        private void listBox_audios_DragLeave(object? sender, EventArgs e)
        {
            this.listBox_audios.Cursor = Cursors.Default;
            this.HighlightInsertionIndex(-1);
        }

        private void listBox_audios_DragDrop(object? sender, DragEventArgs e)
        {
            this.listBox_audios.Cursor = Cursors.Default;
            if (!TryGetDragPayload(e.Data, out var payload) || payload.Audio == null)
            {
                this.HighlightInsertionIndex(-1);
                return;
            }

            Point clientPoint = this.listBox_audios.PointToClient(new Point(e.X, e.Y));
            int insertIndex = this.GetInsertionIndex(clientPoint);
            this.MoveAudio(payload, insertIndex);
            this.HighlightInsertionIndex(-1);
        }

        private void listBox_audios_GiveFeedback(object? sender, GiveFeedbackEventArgs e)
        {
            e.UseDefaultCursors = false;
            Cursor.Current = e.Effect == DragDropEffects.Move ? Cursors.SizeAll : Cursors.No;
        }

        private void BeginAudioDrag()
        {
            if (this._dragCandidate == null)
            {
                return;
            }

            var payload = new AudioDragPayload(this, this._dragCandidate);
            try
            {
                this.listBox_audios.DoDragDrop(payload, DragDropEffects.Move);
            }
            finally
            {
                this.ResetDragState();
            }
        }

        private void ResetDragState()
        {
            this._dragOrigin = null;
            this._dragCandidate = null;
            this._hoverInsertIndex = -1;
            this.listBox_audios.Cursor = Cursors.Default;
        }

        private int GetInsertionIndex(Point clientPoint)
        {
            int index = this.listBox_audios.IndexFromPoint(clientPoint);
            if (index == ListBox.NoMatches)
            {
                return this.AudioC.Audios.Count;
            }

            Rectangle itemRect = this.listBox_audios.GetItemRectangle(index);
            bool insertAfter = clientPoint.Y > itemRect.Top + itemRect.Height / 2;
            return insertAfter ? index + 1 : index;
        }

        private void HighlightInsertionIndex(int insertIndex)
        {
            if (insertIndex < 0)
            {
                if (this._preDragSelection >= 0 && this._preDragSelection < this.AudioC.Audios.Count)
                {
                    this.listBox_audios.SelectedIndex = this._preDragSelection;
                }
                this._hoverInsertIndex = -1;
                return;
            }

            if (this.AudioC.Audios.Count == 0)
            {
                this.listBox_audios.ClearSelected();
                this._hoverInsertIndex = 0;
                return;
            }

            int highlightIndex = Math.Clamp(insertIndex, 0, Math.Max(0, this.AudioC.Audios.Count - 1));
            if (highlightIndex >= 0 && highlightIndex < this.AudioC.Audios.Count)
            {
                this.listBox_audios.SelectedIndex = highlightIndex;
            }
            this._hoverInsertIndex = insertIndex;
        }

        private void MoveAudio(AudioDragPayload payload, int insertIndex)
        {
            if (payload.Audio == null)
            {
                return;
            }

            var sourceList = payload.SourceView.AudioC.Audios;
            var targetList = this.AudioC.Audios;
            int sourceIndex = sourceList.IndexOf(payload.Audio);
            if (sourceIndex < 0)
            {
                return;
            }

            if (payload.SourceView == this)
            {
                if (insertIndex == sourceIndex || insertIndex == sourceIndex + 1)
                {
                    return;
                }

                if (insertIndex > sourceIndex)
                {
                    insertIndex--;
                }

                sourceList.Remove(payload.Audio);
                insertIndex = Math.Clamp(insertIndex, 0, targetList.Count);
                targetList.Insert(insertIndex, payload.Audio);
            }
            else
            {
                sourceList.Remove(payload.Audio);
                insertIndex = Math.Clamp(insertIndex, 0, targetList.Count);
                targetList.Insert(insertIndex, payload.Audio);
            }

            WindowMain.UpdateCollectionTag(payload.Audio, this);
            if (targetList.Count > 0)
            {
                int selectIndex = Math.Clamp(insertIndex, 0, targetList.Count - 1);
                this.listBox_audios.SelectedIndex = selectIndex;
                this._preDragSelection = selectIndex;
            }
        }

        private static bool TryGetDragPayload(IDataObject? data, out AudioDragPayload payload)
        {
            payload = default!;
            if (data?.GetDataPresent(typeof(AudioDragPayload)) == true)
            {
                if (data.GetData(typeof(AudioDragPayload)) is AudioDragPayload dragPayload && dragPayload.Audio != null)
                {
                    payload = dragPayload;
                    return true;
                }
            }
            return false;
        }

        private sealed class AudioDragPayload
        {
            public AudioCollectionView SourceView { get; }
            public AudioObj Audio { get; }

            public AudioDragPayload(AudioCollectionView sourceView, AudioObj audio)
            {
                this.SourceView = sourceView;
                this.Audio = audio;
            }
        }
    }
}
