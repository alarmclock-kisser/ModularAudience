using System.Windows.Forms;

namespace ModularAudience.Forms.Modules.Dialogs;

internal sealed partial class CoOpPasswordDialog : Form
{
    internal CoOpPasswordDialog(string title, string instruction, string initialPassword = "")
    {
        this.InitializeComponent();
        this.Text = title;
        this.instructionLabel.Text = instruction;
        this.passwordTextBox.MaxLength = 128;
        this.passwordTextBox.Text = initialPassword;
    }

    internal string Password => this.passwordTextBox.Text;
}
