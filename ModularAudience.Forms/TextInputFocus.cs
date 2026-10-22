using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ModularAudience.Forms
{
    /// <summary>
    /// Detects whether the user is currently typing into a control, for example a text
    /// box, a numeric input or an editable combo box. Hotkeys must not be triggered in
    /// that case, otherwise typing a number would trigger the hotkey of that key.
    /// </summary>
    internal static class TextInputFocus
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetFocus();

        public static bool IsActive()
        {
            // When the application is in the background, no form is active and the
            // hotkeys have to keep working.
            if (Form.ActiveForm == null)
            {
                return false;
            }

            Control? focused = GetFocusedControl();
            for (int depth = 0; focused != null && depth < 4; depth++)
            {
                if (IsInputControl(focused))
                {
                    return true;
                }

                focused = focused.Parent;
            }

            return false;
        }

        private static Control? GetFocusedControl()
        {
            IntPtr handle = GetFocus();
            return handle == IntPtr.Zero ? null : Control.FromHandle(handle);
        }

        private static bool IsInputControl(Control control)
        {
            switch (control)
            {
                case TextBoxBase:
                case UpDownBase:
                case DateTimePicker:
                    return true;

                case ComboBox combo:
                    return combo.DropDownStyle != ComboBoxStyle.DropDownList;

                case DataGridView grid:
                    return grid.IsCurrentCellInEditMode;

                default:
                    return false;
            }
        }
    }
}
