using System;
using System.Drawing;
using System.Windows.Forms;

namespace ModularAudience.Forms.Hotkeys
{
    public sealed class HotkeyScrollDistanceDialog : Form
    {
        private readonly NumericUpDown _numericDistance;

        public HotkeyScrollDistanceDialog(
            string actionName,
            int currentTickDistance,
            int scrollRange,
            bool horizontal)
        {
            this.Text = "Mouse-Wheel Scroll Distance";
            this.Name = "HotkeyScrollDistanceDialog";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.ClientSize = new Size(460, 190);
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;

            Label actionLabel = new()
            {
                AutoEllipsis = true,
                Location = new Point(12, 12),
                Size = new Size(436, 34),
                Text = $"Hotkey: {actionName}"
            };

            Label promptLabel = new()
            {
                AutoSize = true,
                Location = new Point(12, 58),
                Text = "Value change per mouse-wheel tick:"
            };

            this._numericDistance = new NumericUpDown
            {
                DecimalPlaces = 0,
                Increment = 10,
                Location = new Point(12, 82),
                Maximum = HotkeyDefaults.MaxScrollTickDistance,
                Minimum = HotkeyDefaults.MinScrollTickDistance,
                Size = new Size(140, 28),
                TextAlign = HorizontalAlignment.Right,
                ThousandsSeparator = false,
                Value = Math.Clamp(
                    currentTickDistance,
                    HotkeyDefaults.MinScrollTickDistance,
                    HotkeyDefaults.MaxScrollTickDistance)
            };

            string orientation = horizontal ? "horizontal" : "vertical";
            string hintText = scrollRange > 0
                ? $"Whole number from {HotkeyDefaults.MinScrollTickDistance} to " +
                  $"{HotkeyDefaults.MaxScrollTickDistance}. This {orientation} scrollbar spans " +
                  $"{scrollRange} units; the default ({HotkeyDefaults.DefaultScrollTickPercent}%) " +
                  $"is {Math.Max(1, (int)Math.Round(scrollRange * (HotkeyDefaults.DefaultScrollTickPercent / 100.0), MidpointRounding.AwayFromZero))} units " +
                  "per wheel tick."
                : $"Whole number from {HotkeyDefaults.MinScrollTickDistance} to " +
                  $"{HotkeyDefaults.MaxScrollTickDistance}. The default is " +
                  $"{HotkeyDefaults.DefaultScrollTickPercent}% of the scrollbar range.";

            Label hintLabel = new()
            {
                AutoSize = false,
                ForeColor = SystemColors.GrayText,
                Location = new Point(166, 80),
                Size = new Size(282, 48),
                Text = hintText
            };

            Button saveButton = new()
            {
                Location = new Point(258, 144),
                Size = new Size(90, 32),
                Text = "Save",
                UseVisualStyleBackColor = true
            };
            saveButton.Click += this.SaveButton_Click;

            Button cancelButton = new()
            {
                Location = new Point(358, 144),
                Size = new Size(90, 32),
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                UseVisualStyleBackColor = true
            };

            this.AcceptButton = saveButton;
            this.CancelButton = cancelButton;

            this.Controls.Add(actionLabel);
            this.Controls.Add(promptLabel);
            this.Controls.Add(this._numericDistance);
            this.Controls.Add(hintLabel);
            this.Controls.Add(saveButton);
            this.Controls.Add(cancelButton);
        }

        public int TickDistance { get; private set; } = HotkeyDefaults.MinScrollTickDistance;

        private void SaveButton_Click(object? sender, EventArgs e)
        {
            if (!int.TryParse(this._numericDistance.Text, out int value)
                || value < HotkeyDefaults.MinScrollTickDistance
                || value > HotkeyDefaults.MaxScrollTickDistance)
            {
                MessageBox.Show(
                    this,
                    $"Enter a whole number between {HotkeyDefaults.MinScrollTickDistance} " +
                    $"and {HotkeyDefaults.MaxScrollTickDistance} units.",
                    "Mouse-Wheel Scroll Distance",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                this._numericDistance.Focus();
                return;
            }

            this.TickDistance = value;
            this.DialogResult = DialogResult.OK;
        }
    }
}
