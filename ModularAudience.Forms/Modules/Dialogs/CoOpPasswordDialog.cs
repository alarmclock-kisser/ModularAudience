using System.Windows.Forms;

namespace ModularAudience.Forms.Modules.Dialogs;

internal sealed partial class CoOpPasswordDialog : Form
{
    internal CoOpPasswordDialog(string title, string instruction)
    {
        this.InitializeComponent();
        this.Text = title;
        this.instructionLabel.Text = instruction;
        this.passwordTextBox.MaxLength = 128;
    }

    internal string Password => this.passwordTextBox.Text;
}
