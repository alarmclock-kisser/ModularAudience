using System;
using System.Drawing;
using System.Windows.Forms;

namespace ModularAudience.Forms.Modules.Dialogs
{
    public class TrackRenameDialog : Form
    {
        private readonly TextBox _textBox;
        private readonly Button _okButton;
        private readonly Button _cancelButton;
        private readonly Label _promptLabel;

        public string InputText => this._textBox.Text;

        public TrackRenameDialog(string currentName)
        {
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.ClientSize = new Size(400, 150);
            this.MinimizeBox = false;
            this.MaximizeBox = false;
            this.ShowInTaskbar = false;
            this.Text = "Rename Track";

            this._promptLabel = new Label() { AutoSize = false, Text = "Enter new name for this track:", Location = new Point(10, 8), Size = new Size(380, 20) };
            this.Controls.Add(this._promptLabel);

            this._textBox = new TextBox() { Location = new Point(10, 35), Size = new Size(380, 25) };
            this._textBox.Text = currentName ?? string.Empty;
            this.Controls.Add(this._textBox);

            this._okButton = new Button() { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(230, 80), Size = new Size(75, 25) };
            this._cancelButton = new Button() { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(315, 80), Size = new Size(75, 25) };
            this.Controls.Add(this._okButton);
            this.Controls.Add(this._cancelButton);

            this.AcceptButton = this._okButton;
            this.CancelButton = this._cancelButton;

            // Select all text when the dialog opens
            this.Shown += (s, e) =>
            {
                this._textBox.Focus();
                this._textBox.SelectAll();
            };
        }
    }
}
