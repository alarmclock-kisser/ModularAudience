using System;
using System.Drawing;
using System.Windows.Forms;

namespace ModularAudience.Forms.Hotkeys
{
    public sealed class HotkeyHoldDurationDialog : Form
    {
        private readonly NumericUpDown _numericDuration;

        public HotkeyHoldDurationDialog(string actionName, int currentHoldThresholdMs)
        {
            this.Text = "Hold Duration";
            this.Name = "HotkeyHoldDurationDialog";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.ClientSize = new Size(440, 190);
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;

            Label actionLabel = new()
            {
                AutoEllipsis = true,
                Location = new Point(12, 12),
                Size = new Size(416, 34),
                Text = $"Hotkey: {actionName}"
            };

            Label promptLabel = new()
            {
                AutoSize = true,
                Location = new Point(12, 58),
                Text = "Hold threshold (debounce), ms:"
            };

            this._numericDuration = new NumericUpDown
            {
                DecimalPlaces = 0,
                Increment = 10,
                Location = new Point(12, 82),
                Maximum = HotkeyDefaults.MaxHoldThresholdMs,
                Minimum = HotkeyDefaults.MinHoldThresholdMs,
                Size = new Size(140, 28),
                TextAlign = HorizontalAlignment.Right,
                ThousandsSeparator = false,
                Value = Math.Clamp(
                    currentHoldThresholdMs,
                    HotkeyDefaults.MinHoldThresholdMs,
                    HotkeyDefaults.MaxHoldThresholdMs)
            };

            Label hintLabel = new()
            {
                AutoSize = false,
                ForeColor = SystemColors.GrayText,
                Location = new Point(166, 80),
                Size = new Size(262, 32),
                Text = $"Whole number from {HotkeyDefaults.MinHoldThresholdMs} to " +
                       $"{HotkeyDefaults.MaxHoldThresholdMs}. Default: {HotkeyDefaults.HoldThresholdMs} ms."
            };

            Button saveButton = new()
            {
                Location = new Point(238, 144),
                Size = new Size(90, 32),
                Text = "Save",
                UseVisualStyleBackColor = true
            };
            saveButton.Click += this.SaveButton_Click;

            Button cancelButton = new()
            {
                Location = new Point(338, 144),
                Size = new Size(90, 32),
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                UseVisualStyleBackColor = true
            };

            this.AcceptButton = saveButton;
            this.CancelButton = cancelButton;

            this.Controls.Add(actionLabel);
            this.Controls.Add(promptLabel);
            this.Controls.Add(this._numericDuration);
            this.Controls.Add(hintLabel);
            this.Controls.Add(saveButton);
            this.Controls.Add(cancelButton);
        }

        public int HoldThresholdMs { get; private set; } = HotkeyDefaults.HoldThresholdMs;

        private void SaveButton_Click(object? sender, EventArgs e)
        {
            if (!int.TryParse(this._numericDuration.Text, out int value)
                || value < HotkeyDefaults.MinHoldThresholdMs
                || value > HotkeyDefaults.MaxHoldThresholdMs)
            {
                MessageBox.Show(
                    this,
                    $"Enter a whole number between {HotkeyDefaults.MinHoldThresholdMs} " +
                    $"and {HotkeyDefaults.MaxHoldThresholdMs} ms.",
                    "Hold Duration",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                this._numericDuration.Focus();
                return;
            }

            this.HoldThresholdMs = value;
            this.DialogResult = DialogResult.OK;
        }
    }
}
