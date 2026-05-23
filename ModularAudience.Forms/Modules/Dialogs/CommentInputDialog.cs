using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ModularAudience.Forms.Modules.Dialogs
{
    public class CommentInputDialog : Form
    {
        private readonly TextBox _textBox;
        private readonly Button _okButton;
        private readonly Button _cancelButton;
        private readonly Label _promptLabel;
        private readonly List<string> _historyNewestFirst;
        private string _draft = string.Empty;
        private int _cursor = -1; // -1 = draft, 0 = newest history entry

        public string ResultText => this._textBox.Text;

        public CommentInputDialog(IEnumerable<string> history, string prompt, string initial = "")
        {
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.ClientSize = new Size(520, 180);
            this.MinimizeBox = false;
            this.MaximizeBox = false;
            this.ShowInTaskbar = false;
            this.Text = "Add Log Comment";

            this._promptLabel = new Label() { AutoSize = false, Text = prompt, Location = new Point(10, 8), Size = new Size(500, 30) };
            this.Controls.Add(this._promptLabel);

            this._textBox = new TextBox() { Location = new Point(10, 40), Size = new Size(500, 80), Multiline = true, ScrollBars = ScrollBars.None, AcceptsReturn = true };
            this._textBox.KeyDown += this.TextBox_KeyDown;
            this.Controls.Add(this._textBox);

            this._okButton = new Button() { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(350, 130), Size = new Size(75, 25) };
            this._cancelButton = new Button() { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(435, 130), Size = new Size(75, 25) };
            this.Controls.Add(this._okButton);
            this.Controls.Add(this._cancelButton);

            this._historyNewestFirst = history?.ToList() ?? [];
            this._draft = initial ?? string.Empty;
            this._textBox.Text = this._draft;
            this._textBox.SelectionStart = this._textBox.Text.Length;
            this.AdjustHeight();
        }

        private void AdjustHeight()
        {
            // Limit to 4 lines visible height
            var lines = Math.Max(1, this._textBox.Lines.Length);
            int visibleLines = Math.Min(Math.Max(1, lines), 4);
            using (Graphics g = this._textBox.CreateGraphics())
            {
                var fontHeight = TextRenderer.MeasureText("A", this._textBox.Font).Height;
                int newHeight = fontHeight * visibleLines + 8; // padding
                this._textBox.Height = newHeight;
                // Only show vertical scrollbar when more than 4 lines
                this._textBox.ScrollBars = (lines >= 4) ? ScrollBars.Vertical : ScrollBars.None;
            }
        }

        private void TextBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && !e.Shift)
            {
                // Submit
                e.Handled = true;
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            else if (e.KeyCode == Keys.Enter && e.Shift)
            {
                // Insert newline
                int sel = this._textBox.SelectionStart;
                this._textBox.Text = this._textBox.Text.Insert(sel, Environment.NewLine);
                this._textBox.SelectionStart = sel + Environment.NewLine.Length;
                e.Handled = true;
                this.AdjustHeight();
            }
            else if (e.KeyCode == Keys.Up)
            {
                e.Handled = true;
                // Navigate history (only user comments)
                if (this._cursor < this._historyNewestFirst.Count - 1)
                {
                    if (this._cursor == -1)
                    {
                        this._draft = this._textBox.Text;
                    }
                    this._cursor++;
                    this._textBox.Text = this._historyNewestFirst[this._cursor];
                    this._textBox.SelectionStart = this._textBox.Text.Length;
                    this.AdjustHeight();
                }
            }
            else if (e.KeyCode == Keys.Down)
            {
                e.Handled = true;
                if (this._cursor > -1)
                {
                    this._cursor--;
                    if (this._cursor == -1)
                    {
                        this._textBox.Text = this._draft;
                    }
                    else
                    {
                        this._textBox.Text = this._historyNewestFirst[this._cursor];
                    }
                    this._textBox.SelectionStart = this._textBox.Text.Length;
                    this.AdjustHeight();
                }
            }
        }
    }
}
