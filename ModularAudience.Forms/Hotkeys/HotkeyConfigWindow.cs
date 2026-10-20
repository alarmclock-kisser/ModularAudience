using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ModularAudience.Forms.Hotkeys
{
    public sealed class HotkeyConfigWindow : Form
    {
        private const int ReRegisterColumnIndex = 2;
        private const int InvertColumnIndex = 3;
        private const int HoldColumnIndex = 4;
        private const int DeleteColumnIndex = 5;

        private static HotkeyConfigWindow? _openWindow;

        private readonly HotkeyManager _manager;
        private readonly DataGridView _grid;
        private readonly ComboBox _comboKeyboard;
        private readonly Button _buttonRegister;
        private readonly Button _buttonCopy;
        private readonly Button _buttonRemove;
        private readonly Label _labelKeyboardDetails;
        private readonly Label _labelStatus;
        private readonly Label _labelHint;
        private bool _updatingKeyboardProfiles;
        private bool _refreshingBindings;

        private HotkeyConfigWindow(HotkeyManager manager)
        {
            this._manager = manager;

            this.Text = "Control - Hotkeys";
            this.Name = "HotkeyConfigWindow";
            this.StartPosition = FormStartPosition.CenterParent;
            this.ClientSize = new Size(920, 500);
            this.MinimumSize = new Size(820, 410);
            this.ShowInTaskbar = false;
            this.MinimizeBox = false;
            this.FormBorderStyle = FormBorderStyle.Sizable;

            this._grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AutoGenerateColumns = false,
                BackgroundColor = SystemColors.Window,
                BorderStyle = BorderStyle.None,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
                EditMode = DataGridViewEditMode.EditOnEnter,
                MultiSelect = false,
                ReadOnly = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };

            DataGridViewTextBoxColumn actionColumn = new()
            {
                HeaderText = "Action",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 68,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                ReadOnly = true
            };

            DataGridViewTextBoxColumn hotkeyColumn = new()
            {
                HeaderText = "Hotkey",
                Width = 175,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                ReadOnly = true
            };

            DataGridViewButtonColumn reRegisterColumn = new()
            {
                HeaderText = string.Empty,
                Width = 100,
                Text = "Re-register",
                UseColumnTextForButtonValue = true,
                FlatStyle = FlatStyle.Flat,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                ReadOnly = true
            };

            DataGridViewCheckBoxColumn invertColumn = new()
            {
                HeaderText = "Invert",
                Width = 55,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                ReadOnly = false,
                ThreeState = false,
                ToolTipText = "When checked, holding past the debounce delay toggles the action again on release. " +
                    "When unchecked, a long hold releases the action in the off state."
            };

            DataGridViewButtonColumn holdColumn = new()
            {
                HeaderText = string.Empty,
                Width = 42,
                Text = "⏱",
                UseColumnTextForButtonValue = true,
                FlatStyle = FlatStyle.Flat,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                ToolTipText = "Set hold duration (debounce) in milliseconds.",
                ReadOnly = true
            };
            holdColumn.DefaultCellStyle.Font = new Font("Segoe UI Symbol", this.Font.Size);

            DataGridViewButtonColumn deleteColumn = new()
            {
                HeaderText = string.Empty,
                Width = 42,
                Text = "X",
                UseColumnTextForButtonValue = true,
                FlatStyle = FlatStyle.Flat,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                ReadOnly = true
            };
            deleteColumn.DefaultCellStyle.ForeColor = Color.DarkRed;
            deleteColumn.DefaultCellStyle.Font = new Font(this.Font, FontStyle.Bold);

            this._grid.Columns.AddRange(
                actionColumn,
                hotkeyColumn,
                reRegisterColumn,
                invertColumn,
                holdColumn,
                deleteColumn);
            this._grid.CellContentClick += this.Grid_CellContentClick;
            this._grid.CellDoubleClick += this.Grid_CellDoubleClick;
            this._grid.CellValueChanged += this.Grid_CellValueChanged;
            this._grid.CurrentCellDirtyStateChanged += this.Grid_CurrentCellDirtyStateChanged;

            this._comboKeyboard = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Margin = new Padding(4, 9, 8, 9)
            };
            this._comboKeyboard.SelectedIndexChanged += this.ComboBoxKeyboard_SelectedIndexChanged;

            this._buttonCopy = new Button
            {
                AutoSize = true,
                Text = "Copy To...",
                UseVisualStyleBackColor = true,
                Margin = new Padding(0, 8, 6, 8)
            };
            this._buttonCopy.Click += this.ButtonCopy_Click;

            this._buttonRemove = new Button
            {
                AutoSize = true,
                Text = "Remove Profile",
                UseVisualStyleBackColor = true,
                Margin = new Padding(0, 8, 8, 8)
            };
            this._buttonRemove.Click += this.ButtonRemove_Click;

            this._labelKeyboardDetails = new Label
            {
                AutoEllipsis = true,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 0, 0, 0)
            };

            TableLayoutPanel keyboardPanel = new()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 5,
                RowCount = 1,
                Padding = new Padding(12, 0, 12, 0)
            };
            keyboardPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            keyboardPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42f));
            keyboardPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            keyboardPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            keyboardPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58f));
            keyboardPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            Label keyboardLabel = new()
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                Text = "Keyboard:",
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 0, 4, 0)
            };

            keyboardPanel.Controls.Add(keyboardLabel, 0, 0);
            keyboardPanel.Controls.Add(this._comboKeyboard, 1, 0);
            keyboardPanel.Controls.Add(this._buttonCopy, 2, 0);
            keyboardPanel.Controls.Add(this._buttonRemove, 3, 0);
            keyboardPanel.Controls.Add(this._labelKeyboardDetails, 4, 0);

            TableLayoutPanel root = new()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112f));

            Panel bottomPanel = new()
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12, 8, 12, 8)
            };

            this._buttonRegister = new Button
            {
                Location = new Point(12, 10),
                Size = new Size(150, 32),
                Text = "Register Hotkey",
                UseVisualStyleBackColor = true
            };
            this._buttonRegister.Click += this.ButtonRegister_Click;

            this._labelHint = new Label
            {
                AutoSize = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Location = new Point(174, 8),
                Size = new Size(730, 38),
                Text = "Click Register Hotkey, press the key combination, then click the action control, " +
                       "numeric up/down arrow, or context menu item. Esc cancels. The stopwatch button sets each hotkey's hold/debounce duration."
            };

            this._labelStatus = new Label
            {
                AutoEllipsis = true,
                AutoSize = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BorderStyle = BorderStyle.FixedSingle,
                Location = new Point(12, 56),
                Size = new Size(882, 30),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(6, 0, 6, 0)
            };

            bottomPanel.Controls.Add(this._buttonRegister);
            bottomPanel.Controls.Add(this._labelHint);
            bottomPanel.Controls.Add(this._labelStatus);

            root.Controls.Add(keyboardPanel, 0, 0);
            root.Controls.Add(this._grid, 0, 1);
            root.Controls.Add(bottomPanel, 0, 2);

            this.Controls.Add(root);

            this._manager.Changed += this.Manager_Changed;
            this._manager.KeyboardsChanged += this.Manager_KeyboardsChanged;
            this.FormClosing += this.HotkeyConfigWindow_FormClosing;
            this.FormClosed += this.HotkeyConfigWindow_FormClosed;

            this.RefreshKeyboardProfiles();
            this.RefreshBindings();
            this.UpdateKeyboardScope();
        }

        public static HotkeyConfigWindow ShowWindow(IWin32Window? owner)
        {
            if (_openWindow != null && !_openWindow.IsDisposed)
            {
                _openWindow.Activate();
                _openWindow.BringToFront();
                return _openWindow;
            }

            HotkeyManager manager = HotkeyManager.Instance ?? HotkeyManager.Initialize();
            HotkeyConfigWindow window = new(manager);
            _openWindow = window;

            if (owner != null)
            {
                window.Show(owner);
            }
            else
            {
                window.Show();
            }

            return window;
        }

        private KeyboardScopeItem SelectedScope =>
            this._comboKeyboard.SelectedItem as KeyboardScopeItem ?? KeyboardScopeItem.Universal;

        private void Manager_Changed()
        {
            this.RefreshKeyboardProfiles();
            this.RefreshBindings();
            this.UpdateStatus();
            this.UpdateKeyboardScope();
        }

        private void Manager_KeyboardsChanged()
        {
            this.RefreshKeyboardProfiles();
            this.RefreshBindings();
            this.UpdateKeyboardScope();
        }

        private void RefreshKeyboardProfiles()
        {
            string selectedId = this.SelectedScope.HardwareId;
            List<HotkeyKeyboardProfile> profiles = this._manager.GetKeyboardProfiles().ToList();
            HashSet<string> duplicateNames = profiles
                .GroupBy(profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            this._updatingKeyboardProfiles = true;
            try
            {
                this._comboKeyboard.Items.Clear();
                this._comboKeyboard.Items.Add(KeyboardScopeItem.Universal);

                foreach (HotkeyKeyboardProfile profile in profiles)
                {
                    this._comboKeyboard.Items.Add(new KeyboardScopeItem
                    {
                        Profile = profile,
                        Label = FormatProfileLabel(profile, duplicateNames.Contains(profile.DisplayName))
                    });
                }

                int selectedIndex = 0;
                if (selectedId.Length > 0)
                {
                    for (int index = 1; index < this._comboKeyboard.Items.Count; index++)
                    {
                        if (this._comboKeyboard.Items[index] is KeyboardScopeItem item
                            && HotkeyKeyboardIds.Same(item.HardwareId, selectedId))
                        {
                            selectedIndex = index;
                            break;
                        }
                    }
                }

                this._comboKeyboard.SelectedIndex = selectedIndex;
            }
            finally
            {
                this._updatingKeyboardProfiles = false;
            }
        }

        private static string FormatProfileLabel(HotkeyKeyboardProfile profile, bool includeShortId)
        {
            string label = profile.DisplayName;
            if (includeShortId)
            {
                label += " [" + HotkeyKeyboardIds.GetShortDisplay(profile.HardwareId) + "]";
            }

            if (!profile.IsConnected)
            {
                label += " (not connected)";
            }

            return label;
        }

        private void SelectScope(string hardwareId)
        {
            for (int index = 0; index < this._comboKeyboard.Items.Count; index++)
            {
                if (this._comboKeyboard.Items[index] is KeyboardScopeItem item
                    && HotkeyKeyboardIds.Same(item.HardwareId, hardwareId))
                {
                    this._comboKeyboard.SelectedIndex = index;
                    return;
                }
            }
        }

        private void ComboBoxKeyboard_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (this._updatingKeyboardProfiles)
            {
                return;
            }

            this.RefreshBindings();
            this.UpdateKeyboardScope();
        }

        private void RefreshBindings()
        {
            string? selectedId = null;
            if (this._grid.SelectedRows.Count > 0)
            {
                selectedId = this._grid.SelectedRows[0].Tag as string;
            }

            this._refreshingBindings = true;
            try
            {
                this._grid.Rows.Clear();
                foreach (HotkeyBinding binding in this._manager.GetBindingsForScope(this.SelectedScope.HardwareId))
                {
                    int index = this._grid.Rows.Add(
                        binding.ActionName,
                        binding.GetHotkeyDisplay(),
                        "Re-register",
                        binding.InvertWhileHeld,
                        "⏱",
                        "X");

                    this._grid.Rows[index].Tag = binding.Id;
                    this._grid.Rows[index].Cells[1].ToolTipText =
                        $"{binding.GetKeyboardDisplay()} - {binding.GetGestureDisplay()}";
                    this._grid.Rows[index].Cells[InvertColumnIndex].ToolTipText =
                        binding.InvertWhileHeld
                            ? "Hold past the debounce delay to toggle again on release."
                            : "A long hold releases the action in the off state.";
                    this._grid.Rows[index].Cells[HoldColumnIndex].ToolTipText =
                        $"Hold duration: {binding.HoldThresholdMs} ms";

                    if (selectedId != null && string.Equals(selectedId, binding.Id, StringComparison.Ordinal))
                    {
                        this._grid.Rows[index].Selected = true;
                    }
                }
            }
            finally
            {
                this._refreshingBindings = false;
            }
        }

        private void UpdateKeyboardScope()
        {
            KeyboardScopeItem scope = this.SelectedScope;
            int scopeBindingCount = this._manager.GetBindingsForScope(scope.HardwareId).Count;
            int destinationCount = this._manager.GetKeyboardProfiles()
                .Count(profile => !HotkeyKeyboardIds.Same(profile.HardwareId, scope.HardwareId));

            this._buttonCopy.Enabled = !scope.IsUniversal && scopeBindingCount > 0 && destinationCount > 0;
            this._buttonRemove.Enabled = !scope.IsUniversal && scopeBindingCount > 0;
            this._labelKeyboardDetails.Text = scope.IsUniversal
                ? "Hotkeys apply to every keyboard."
                : scope.HardwareId;
            this._labelHint.Text = scope.IsUniversal
                ? "Click Register Hotkey, press the key combination, then click the action control, " +
                  "numeric up/down arrow, or context menu item. Esc cancels. The stopwatch button sets each hotkey's hold/debounce duration."
                : $"New hotkeys are assigned to \"{scope.DisplayName}\". Click Register Hotkey, " +
                  "press the key combination, then click the action control, numeric up/down arrow, " +
                  "or context menu item. The stopwatch button sets each hotkey's hold/debounce duration.";
        }

        private void UpdateStatus()
        {
            this._labelStatus.Text = this._manager.StatusText;
            this._labelStatus.ForeColor = this._manager.IsCapturing ? Color.DarkBlue : SystemColors.ControlText;
        }

        private void ButtonRegister_Click(object? sender, EventArgs e)
        {
            KeyboardScopeItem scope = this.SelectedScope;
            this._manager.BeginRegisterNew(
                scope.IsUniversal ? null : scope.HardwareId,
                scope.Profile?.DisplayName);
        }

        private void ButtonCopy_Click(object? sender, EventArgs e)
        {
            KeyboardScopeItem scope = this.SelectedScope;
            if (scope.IsUniversal || scope.Profile == null)
            {
                return;
            }

            List<HotkeyKeyboardProfile> destinations = this._manager.GetKeyboardProfiles()
                .Where(profile => !HotkeyKeyboardIds.Same(profile.HardwareId, scope.HardwareId))
                .ToList();

            if (destinations.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "No other keyboard profiles are available.",
                    "Copy Hotkeys",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            using HotkeyKeyboardCopyDialog dialog = new(scope.Profile.DisplayName, destinations);
            if (dialog.ShowDialog(this) != DialogResult.OK || dialog.SelectedProfile == null)
            {
                return;
            }

            HotkeyKeyboardProfile destination = dialog.SelectedProfile;
            int existing = this._manager.GetBindingsForScope(destination.HardwareId).Count;
            if (existing > 0)
            {
                DialogResult confirm = MessageBox.Show(
                    this,
                    $"Replace {existing} hotkey(s) for \"{destination.DisplayName}\"?",
                    "Copy Hotkeys",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2);

                if (confirm != DialogResult.Yes)
                {
                    return;
                }
            }

            int copied = this._manager.CopyKeyboardProfile(scope.Profile, destination);
            if (copied > 0)
            {
                this.RefreshKeyboardProfiles();
                this.SelectScope(destination.HardwareId);
            }
        }

        private void ButtonRemove_Click(object? sender, EventArgs e)
        {
            KeyboardScopeItem scope = this.SelectedScope;
            if (scope.IsUniversal || scope.Profile == null)
            {
                return;
            }

            DialogResult confirm = MessageBox.Show(
                this,
                $"Remove all hotkeys for \"{scope.Profile.DisplayName}\"?",
                "Remove Keyboard Profile",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (confirm == DialogResult.Yes)
            {
                this._manager.RemoveKeyboardProfile(scope.Profile);
            }
        }

        private void Grid_CellContentClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0)
            {
                return;
            }

            DataGridViewRow row = this._grid.Rows[e.RowIndex];
            if (row.Tag is not string id)
            {
                return;
            }

            HotkeyBinding? binding = this._manager.GetBindings()
                .FirstOrDefault(item => item.Id == id);
            if (binding == null)
            {
                return;
            }

            if (e.ColumnIndex == ReRegisterColumnIndex)
            {
                this._manager.BeginReRegister(binding);
            }
            else if (e.ColumnIndex == HoldColumnIndex)
            {
                using HotkeyHoldDurationDialog dialog = new(
                    binding.ActionName,
                    binding.HoldThresholdMs);
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    this._manager.UpdateHoldThreshold(id, dialog.HoldThresholdMs);
                }
            }
            else if (e.ColumnIndex == DeleteColumnIndex)
            {
                this._manager.RemoveBinding(id);
            }
        }

        private void Grid_CurrentCellDirtyStateChanged(object? sender, EventArgs e)
        {
            if (this._grid.IsCurrentCellDirty && this._grid.CurrentCell is DataGridViewCheckBoxCell)
            {
                this._grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        }

        private void Grid_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
        {
            if (this._refreshingBindings || e.RowIndex < 0 || e.ColumnIndex != InvertColumnIndex)
            {
                return;
            }

            DataGridViewRow row = this._grid.Rows[e.RowIndex];
            if (row.Tag is not string id)
            {
                return;
            }

            bool invertWhileHeld = row.Cells[e.ColumnIndex].Value is bool value && value;
            this._manager.UpdateInvertWhileHeld(id, invertWhileHeld);
        }

        private void Grid_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return;
            }

            if (e.ColumnIndex is ReRegisterColumnIndex or InvertColumnIndex or HoldColumnIndex or DeleteColumnIndex)
            {
                return;
            }

            if (this._grid.Rows[e.RowIndex].Tag is not string id)
            {
                return;
            }

            HotkeyBinding? binding = this._manager.GetBindings()
                .FirstOrDefault(item => item.Id == id);
            if (binding != null)
            {
                this._manager.BeginReRegister(binding);
            }
        }

        private void HotkeyConfigWindow_FormClosing(object? sender, FormClosingEventArgs e)
        {
            this._manager.CancelCapture();
        }

        private void HotkeyConfigWindow_FormClosed(object? sender, FormClosedEventArgs e)
        {
            this._manager.Changed -= this.Manager_Changed;
            this._manager.KeyboardsChanged -= this.Manager_KeyboardsChanged;
            if (ReferenceEquals(_openWindow, this))
            {
                _openWindow = null;
            }
        }

        private sealed class KeyboardScopeItem
        {
            public static KeyboardScopeItem Universal { get; } = new()
            {
                Label = "Universal (all keyboards)"
            };

            public HotkeyKeyboardProfile? Profile { get; init; }
            public string Label { get; init; } = string.Empty;
            public string HardwareId => this.Profile?.HardwareId ?? string.Empty;
            public bool IsUniversal => this.Profile == null;
            public string DisplayName => this.Profile?.DisplayName ?? "All keyboards";

            public override string ToString()
            {
                return this.Label;
            }
        }
    }
}
