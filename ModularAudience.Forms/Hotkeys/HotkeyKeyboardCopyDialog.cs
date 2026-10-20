using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ModularAudience.Forms.Hotkeys
{
    public sealed class HotkeyKeyboardCopyDialog : Form
    {
        private readonly ComboBox _comboDestination;

        public HotkeyKeyboardCopyDialog(
            string sourceDisplayName,
            IReadOnlyList<HotkeyKeyboardProfile> destinations)
        {
            this.Text = "Copy Hotkeys";
            this.Name = "HotkeyKeyboardCopyDialog";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.ClientSize = new Size(480, 142);
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;

            Label label = new()
            {
                AutoEllipsis = true,
                Location = new Point(12, 12),
                Size = new Size(456, 32),
                Text = $"Copy all hotkeys from \"{sourceDisplayName}\" to:"
            };

            this._comboDestination = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(12, 48),
                Size = new Size(456, 28)
            };

            HashSet<string> duplicateNames = destinations
                .GroupBy(profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (HotkeyKeyboardProfile profile in destinations)
            {
                string profileLabel = profile.DisplayName;
                if (duplicateNames.Contains(profile.DisplayName))
                {
                    profileLabel += " [" + HotkeyKeyboardIds.GetShortDisplay(profile.HardwareId) + "]";
                }

                if (!profile.IsConnected)
                {
                    profileLabel += " (not connected)";
                }

                this._comboDestination.Items.Add(new DestinationItem(profile, profileLabel));
            }

            if (this._comboDestination.Items.Count > 0)
            {
                this._comboDestination.SelectedIndex = 0;
            }

            Button copyButton = new()
            {
                Location = new Point(278, 96),
                Size = new Size(90, 30),
                Text = "Copy",
                DialogResult = DialogResult.OK,
                UseVisualStyleBackColor = true
            };

            Button cancelButton = new()
            {
                Location = new Point(378, 96),
                Size = new Size(90, 30),
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                UseVisualStyleBackColor = true
            };

            copyButton.Click += this.CopyButton_Click;
            this.AcceptButton = copyButton;
            this.CancelButton = cancelButton;

            this.Controls.Add(label);
            this.Controls.Add(this._comboDestination);
            this.Controls.Add(copyButton);
            this.Controls.Add(cancelButton);
        }

        public HotkeyKeyboardProfile? SelectedProfile { get; private set; }

        private void CopyButton_Click(object? sender, EventArgs e)
        {
            if (this._comboDestination.SelectedItem is DestinationItem item)
            {
                this.SelectedProfile = item.Profile;
            }
        }

        private sealed class DestinationItem
        {
            public DestinationItem(HotkeyKeyboardProfile profile, string label)
            {
                this.Profile = profile;
                this.Label = label;
            }

            public HotkeyKeyboardProfile Profile { get; }
            public string Label { get; }

            public override string ToString()
            {
                return this.Label;
            }
        }
    }
}
