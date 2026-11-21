#nullable enable

using System.Drawing;
using System.Windows.Forms;

namespace ModularAudience.Forms.Modules.Dialogs;

internal sealed partial class CoOpPasswordDialog
{
    private Label instructionLabel = null!;
    private TextBox passwordTextBox = null!;
    private Button acceptButton = null!;
    private Button cancelButton = null!;

    private void InitializeComponent()
    {
        this.instructionLabel = new Label();
        this.passwordTextBox = new TextBox();
        this.acceptButton = new Button();
        this.cancelButton = new Button();
        this.SuspendLayout();
        this.instructionLabel.AutoSize = true;
        this.instructionLabel.Location = new Point(16, 16);
        this.instructionLabel.MaximumSize = new Size(420, 0);
        this.instructionLabel.Text = "Enter the co-op session password.";
        this.passwordTextBox.Location = new Point(19, 62);
        this.passwordTextBox.Size = new Size(420, 23);
        this.passwordTextBox.UseSystemPasswordChar = true;
        this.acceptButton.DialogResult = DialogResult.OK;
        this.acceptButton.Location = new Point(253, 102);
        this.acceptButton.Size = new Size(90, 30);
        this.acceptButton.Text = "Continue";
        this.acceptButton.UseVisualStyleBackColor = true;
        this.cancelButton.DialogResult = DialogResult.Cancel;
        this.cancelButton.Location = new Point(349, 102);
        this.cancelButton.Size = new Size(90, 30);
        this.cancelButton.Text = "Cancel";
        this.cancelButton.UseVisualStyleBackColor = true;
        this.AcceptButton = this.acceptButton;
        this.CancelButton = this.cancelButton;
        this.ClientSize = new Size(458, 148);
        this.Controls.Add(this.instructionLabel);
        this.Controls.Add(this.passwordTextBox);
        this.Controls.Add(this.acceptButton);
        this.Controls.Add(this.cancelButton);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.ShowInTaskbar = false;
        this.StartPosition = FormStartPosition.CenterParent;
        this.ResumeLayout(false);
        this.PerformLayout();
    }
}
