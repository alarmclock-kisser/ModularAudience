using System;
using System.Drawing;
using System.Windows.Forms;

namespace ModularAudience.Forms.Hotkeys
{
    public sealed class HotkeyVarispeedStepDialog : Form
    {
        private readonly NumericUpDown _numericUnits;

        public HotkeyVarispeedStepDialog(
            string actionName,
            int currentUnitsPerWheelTick)
        {
            this.Text = "Varispeed per Wheel Tick";
            this.Name = "HotkeyVarispeedStepDialog";
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
                Text = "Rate change per mouse-wheel tick:"
            };

            this._numericUnits = new NumericUpDown
            {
                DecimalPlaces = 0,
                Increment = 5,
                Location = new Point(12, 82),
                Maximum = HotkeyDefaults.MaxLoopRateUnitsPerWheelTick,
                Minimum = HotkeyDefaults.MinLoopRateUnitsPerWheelTick,
                Size = new Size(140, 28),
                TextAlign = HorizontalAlignment.Right,
                ThousandsSeparator = false,
                Value = Math.Clamp(
                    currentUnitsPerWheelTick,
                    HotkeyDefaults.MinLoopRateUnitsPerWheelTick,
                    HotkeyDefaults.MaxLoopRateUnitsPerWheelTick)
            };

            Label hintLabel = new()
            {
                AutoSize = false,
                ForeColor = SystemColors.GrayText,
                Location = new Point(166, 80),
                Size = new Size(282, 48),
                Text = "Whole number from " +
                       $"{HotkeyDefaults.MinLoopRateUnitsPerWheelTick} to " +
                       $"{HotkeyDefaults.MaxLoopRateUnitsPerWheelTick}. The tempo is scaled " +
                       $"logarithmically, so {HotkeyDefaults.LoopRateUnitsPerWheelTick} units are " +
                       "roughly three percent per wheel tick."
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
            this.Controls.Add(this._numericUnits);
            this.Controls.Add(hintLabel);
            this.Controls.Add(saveButton);
            this.Controls.Add(cancelButton);
        }

        public int UnitsPerWheelTick { get; private set; } = HotkeyDefaults.LoopRateUnitsPerWheelTick;

        private void SaveButton_Click(object? sender, EventArgs e)
        {
            if (!int.TryParse(this._numericUnits.Text, out int value)
                || value < HotkeyDefaults.MinLoopRateUnitsPerWheelTick
                || value > HotkeyDefaults.MaxLoopRateUnitsPerWheelTick)
            {
                MessageBox.Show(
                    this,
                    $"Enter a whole number between {HotkeyDefaults.MinLoopRateUnitsPerWheelTick} " +
                    $"and {HotkeyDefaults.MaxLoopRateUnitsPerWheelTick} units.",
                    "Varispeed per Wheel Tick",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                this._numericUnits.Focus();
                return;
            }

            this.UnitsPerWheelTick = value;
            this.DialogResult = DialogResult.OK;
        }
    }
}
