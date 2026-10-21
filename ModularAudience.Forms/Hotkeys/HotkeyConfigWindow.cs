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
        private readonly Button _buttonPausingSyncerHotkey;
        private readonly Button _buttonNudgingSyncerHotkey;
        private readonly Label _labelPausingSyncerHotkey;
        private readonly Label _labelNudgingSyncerHotkey;
        private bool _updatingKeyboardProfiles;
        private bool _refreshingBindings;
        private bool _capturingPausingSyncer;
        private bool _capturingNudgingSyncer;
        private readonly System.Windows.Forms.Timer _registerBlinkTimer;
        private bool _registerBlinkOn;

        private static readonly Color RegisterActiveBackColor = Color.FromArgb(255, 153, 153);
        private static readonly Color RegisterBlinkBackColor = Color.FromArgb(255, 226, 226);

        private HotkeyConfigWindow(HotkeyManager manager)
        {
            this._manager = manager;

            this.Text = "Control - Hotkeys";
            this.Name = "HotkeyConfigWindow";
            this.StartPosition = FormStartPosition.CenterParent;
            this.ClientSize = new Size(920, 560);
            this.MinimumSize = new Size(820, 470);
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
                HeaderText = "Invertible",
                Width = 65,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                ReadOnly = false,
                ThreeState = false,
                ToolTipText = "For buttons: when checked, holding past the debounce delay toggles the action " +
                    "again on release; when unchecked, a short tap toggles the action and holding turns it " +
                    "on while held and off on release. For scrollbars: when checked, the mouse-wheel " +
                    "direction is reversed."
            };

            DataGridViewButtonColumn holdColumn = new()
            {
                HeaderText = string.Empty,
                Width = 42,
                Text = "⏱",
                UseColumnTextForButtonValue = false,
                FlatStyle = FlatStyle.Flat,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                ToolTipText = "Set hold duration (debounce), or the mouse-wheel tick distance for scrollbars.",
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
                RowCount = 4
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 124f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 80f));

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
                UseVisualStyleBackColor = false,
                BackColor = SystemColors.Control
            };
            this._buttonRegister.Click += this.ButtonRegister_Click;

            this._labelHint = new Label
            {
                AutoSize = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Location = new Point(174, 4),
                Size = new Size(722, 62),
                TextAlign = ContentAlignment.TopLeft,
                Text = "Click Register Hotkey, press the key combination (a single modifier such as Ctrl also works), " +
                       "then click the action control, numeric up/down arrow, or context menu item. Esc cancels. " +
                       "The stopwatch button sets each hotkey's hold/debounce duration."
            };

            this._labelStatus = new Label
            {
                AutoEllipsis = true,
                AutoSize = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BorderStyle = BorderStyle.FixedSingle,
                Location = new Point(12, 72),
                Size = new Size(896, 30),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(6, 0, 6, 0)
            };

            bottomPanel.Controls.Add(this._buttonRegister);
            bottomPanel.Controls.Add(this._labelHint);
            bottomPanel.Controls.Add(this._labelStatus);

            Panel syncerPanel = new()
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12, 4, 12, 4)
            };

            Label syncerLabel = new()
            {
                AutoSize = true,
                Location = new Point(12, 8),
                Text = "Syncer Hotkeys (global, not per-keyboard):",
                Font = new Font(this.Font, FontStyle.Bold)
            };

            this._buttonPausingSyncerHotkey = new Button
            {
                Location = new Point(12, 34),
                Size = new Size(180, 28),
                Text = "Set Pausing Syncer Hotkey",
                UseVisualStyleBackColor = true
            };
            this._buttonPausingSyncerHotkey.Click += this.ButtonPausingSyncerHotkey_Click;

            this._labelPausingSyncerHotkey = new Label
            {
                AutoSize = false,
                Location = new Point(200, 34),
                Size = new Size(250, 28),
                TextAlign = ContentAlignment.MiddleLeft,
                BorderStyle = BorderStyle.FixedSingle,
                Padding = new Padding(6, 0, 6, 0)
            };

            this._buttonNudgingSyncerHotkey = new Button
            {
                Location = new Point(460, 34),
                Size = new Size(180, 28),
                Text = "Set Nudging Syncer Hotkey",
                UseVisualStyleBackColor = true
            };
            this._buttonNudgingSyncerHotkey.Click += this.ButtonNudgingSyncerHotkey_Click;

            this._labelNudgingSyncerHotkey = new Label
            {
                AutoSize = false,
                Location = new Point(650, 34),
                Size = new Size(250, 28),
                TextAlign = ContentAlignment.MiddleLeft,
                BorderStyle = BorderStyle.FixedSingle,
                Padding = new Padding(6, 0, 6, 0)
            };

            syncerPanel.Controls.Add(syncerLabel);
            syncerPanel.Controls.Add(this._buttonPausingSyncerHotkey);
            syncerPanel.Controls.Add(this._labelPausingSyncerHotkey);
            syncerPanel.Controls.Add(this._buttonNudgingSyncerHotkey);
            syncerPanel.Controls.Add(this._labelNudgingSyncerHotkey);

            root.Controls.Add(keyboardPanel, 0, 0);
            root.Controls.Add(this._grid, 0, 1);
            root.Controls.Add(bottomPanel, 0, 2);
            root.Controls.Add(syncerPanel, 0, 3);

            this.Controls.Add(root);

            this._manager.Changed += this.Manager_Changed;
            this._manager.KeyboardsChanged += this.Manager_KeyboardsChanged;
            this.FormClosing += this.HotkeyConfigWindow_FormClosing;
            this.FormClosed += this.HotkeyConfigWindow_FormClosed;

            this._registerBlinkTimer = new System.Windows.Forms.Timer { Interval = 450 };
            this._registerBlinkTimer.Tick += this.RegisterBlinkTimer_Tick;

            this.RefreshKeyboardProfiles();
            this.RefreshBindings();
            this.UpdateKeyboardScope();
            this.UpdateSyncerHotkeyLabels();
            this.UpdateRegisterButtonState();
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
            bool wasCapturing = this._manager.IsCapturing;
            bool wasCapturingPausing = this._capturingPausingSyncer;
            bool wasCapturingNudging = this._capturingNudgingSyncer;

            this.RefreshKeyboardProfiles();
            this.RefreshBindings();
            this.UpdateStatus();
            this.UpdateRegisterButtonState();
            this.UpdateKeyboardScope();
            this.UpdateSyncerHotkeyLabels();

            if (!wasCapturing)
            {
                if (wasCapturingPausing)
                {
                    this._capturingPausingSyncer = false;
                    this._buttonPausingSyncerHotkey.Text = "Set Pausing Syncer Hotkey";
                }
                if (wasCapturingNudging)
                {
                    this._capturingNudgingSyncer = false;
                    this._buttonNudgingSyncerHotkey.Text = "Set Nudging Syncer Hotkey";
                }
            }
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

            // Keep the scroll position of the list, so that changing a checkbox or
            // deleting a row does not jump back to the top.
            int firstVisibleRow = this._grid.FirstDisplayedScrollingRowIndex;

            this._refreshingBindings = true;
            try
            {
                this._grid.Rows.Clear();
                foreach (HotkeyBinding binding in this._manager.GetBindingsForScope(this.SelectedScope.HardwareId))
                {
                    bool isScrollBar = string.Equals(
                        binding.Target.ActionKind,
                        HotkeyActionKinds.ScrollBar,
                        StringComparison.OrdinalIgnoreCase);
                    bool isLoopRate = string.Equals(
                        binding.Target.ActionKind,
                        HotkeyActionKinds.LoopTrackRate,
                        StringComparison.OrdinalIgnoreCase);
                    string holdGlyph = isScrollBar
                        ? GetScrollBarGlyph(binding)
                        : isLoopRate
                            ? "⇅"
                            : "⏱";

                    int index = this._grid.Rows.Add(
                        binding.ActionName,
                        binding.GetHotkeyDisplay(),
                        "Re-register",
                        binding.InvertWhileHeld,
                        holdGlyph,
                        "X");

                    this._grid.Rows[index].Tag = binding.Id;
                    this._grid.Rows[index].Cells[1].ToolTipText =
                        $"{binding.GetKeyboardDisplay()} - {binding.GetGestureDisplay()}";
                    this._grid.Rows[index].Cells[InvertColumnIndex].ToolTipText = isScrollBar
                        ? "When checked, the mouse-wheel direction is reversed."
                        : isLoopRate
                            ? "When checked, the mouse-wheel direction is reversed."
                            : binding.InvertWhileHeld
                                ? "Hold past the debounce delay to toggle again on release."
                                : "Tap toggles; hold switches on while held and off on release.";

                    if (isScrollBar)
                    {
                        this._grid.Rows[index].Cells[HoldColumnIndex].ToolTipText =
                            "Mouse-wheel tick distance: " +
                            (binding.Target.ScrollTickDistance > 0
                                ? $"{binding.Target.ScrollTickDistance} units"
                                : $"{HotkeyDefaults.DefaultScrollTickPercent}% of the range") +
                            ". Click to change.";
                    }
                    else if (isLoopRate)
                    {
                        int units = binding.Target.LoopRateUnitsPerWheelTick > 0
                            ? binding.Target.LoopRateUnitsPerWheelTick
                            : HotkeyDefaults.LoopRateUnitsPerWheelTick;
                        this._grid.Rows[index].Cells[HoldColumnIndex].ToolTipText =
                            $"Varispeed per wheel tick: {units} units. Click to change.";
                    }
                    else
                    {
                        this._grid.Rows[index].Cells[HoldColumnIndex].ToolTipText =
                            $"Hold duration: {binding.HoldThresholdMs} ms";
                    }

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

            this.RestoreScrollPosition(firstVisibleRow);
        }

        private void RestoreScrollPosition(int firstVisibleRow)
        {
            if (firstVisibleRow < 0 || this._grid.Rows.Count == 0)
            {
                return;
            }

            try
            {
                this._grid.FirstDisplayedScrollingRowIndex = Math.Min(
                    firstVisibleRow,
                    this._grid.Rows.Count - 1);
            }
            catch (InvalidOperationException)
            {
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
                ? "Click Register Hotkey, press the key combination (a single modifier such as Ctrl also works), " +
                  "then click the action control, numeric up/down arrow, scrollbar, a LoopControl track entry " +
                  "(varispeed), or context menu item. Esc cancels. " +
                  "The stopwatch button sets each hotkey's " +
                  "hold/debounce duration; for scrollbar hotkeys it sets the mouse-wheel tick distance."
                : $"New hotkeys are assigned to \"{scope.DisplayName}\". Click Register Hotkey, " +
                  "press the key combination (a single modifier also works), then click the action control, " +
                  "numeric up/down arrow, scrollbar, a LoopControl track entry (varispeed), or context menu item. " +
                  "The stopwatch button sets each hotkey's hold/debounce duration; for scrollbar hotkeys it sets " +
                  "the mouse-wheel tick distance.";
        }

        private void UpdateStatus()
        {
            this._labelStatus.Text = this._manager.StatusText;
            this._labelStatus.ForeColor = this._manager.IsCapturing ? Color.DarkBlue : SystemColors.ControlText;
        }

        private void UpdateRegisterButtonState()
        {
            bool capturing = this._manager.IsCapturing && !this._manager.IsSyncerCaptureActive;
            bool awaitingAction = capturing && this._manager.IsAwaitingAction;

            if (capturing && awaitingAction)
            {
                if (!this._registerBlinkTimer.Enabled)
                {
                    this._registerBlinkOn = true;
                    this._buttonRegister.BackColor = RegisterActiveBackColor;
                    this._registerBlinkTimer.Start();
                }

                return;
            }

            this._registerBlinkTimer.Stop();
            this._registerBlinkOn = false;
            this._buttonRegister.BackColor = capturing
                ? RegisterActiveBackColor
                : SystemColors.Control;
        }

        private void RegisterBlinkTimer_Tick(object? sender, EventArgs e)
        {
            if (!this._manager.IsCapturing || !this._manager.IsAwaitingAction)
            {
                this.UpdateRegisterButtonState();
                return;
            }

            this._registerBlinkOn = !this._registerBlinkOn;
            this._buttonRegister.BackColor = this._registerBlinkOn
                ? RegisterActiveBackColor
                : RegisterBlinkBackColor;
        }

        private void ButtonRegister_Click(object? sender, EventArgs e)
        {
            KeyboardScopeItem scope = this.SelectedScope;
            this._manager.BeginRegisterNew(
                scope.IsUniversal ? null : scope.HardwareId,
                scope.Profile?.DisplayName);
            this.UpdateRegisterButtonState();
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

        private static bool IsScrollBarBinding(HotkeyBinding binding)
        {
            return string.Equals(
                binding.Target.ActionKind,
                HotkeyActionKinds.ScrollBar,
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLoopRateBinding(HotkeyBinding binding)
        {
            return string.Equals(
                binding.Target.ActionKind,
                HotkeyActionKinds.LoopTrackRate,
                StringComparison.OrdinalIgnoreCase);
        }

        private static string GetScrollBarGlyph(HotkeyBinding binding)
        {
            bool horizontal = binding.Target.ControlPath.Count > 0
                && binding.Target.ControlPath[^1].TypeName
                    .Contains("HScrollBar", StringComparison.Ordinal);
            return horizontal ? "↔" : "↕";
        }

        private void ShowScrollDistanceDialog(HotkeyBinding binding)
        {
            int range = 0;
            int defaultDistance = HotkeyDefaults.MinScrollTickDistance;
            bool horizontal = GetScrollBarGlyph(binding) == "↔";

            if (HotkeyActionResolver.TryResolveScrollBar(binding.Target, out ScrollBar? scrollBar)
                && scrollBar != null)
            {
                range = Math.Max(1, scrollBar.Maximum - scrollBar.Minimum);
                defaultDistance = HotkeyActionResolver.ComputeDefaultScrollTickDistance(scrollBar);
                horizontal = scrollBar is HScrollBar;
            }

            int current = binding.Target.ScrollTickDistance > 0
                ? binding.Target.ScrollTickDistance
                : defaultDistance;

            using HotkeyScrollDistanceDialog dialog = new(
                binding.ActionName,
                current,
                range,
                horizontal);
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                this._manager.UpdateScrollTickDistance(binding.Id, dialog.TickDistance);
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
                if (IsScrollBarBinding(binding))
                {
                    this.ShowScrollDistanceDialog(binding);
                }
                else if (IsLoopRateBinding(binding))
                {
                    using HotkeyVarispeedStepDialog dialog = new(
                        binding.ActionName,
                        binding.Target.LoopRateUnitsPerWheelTick > 0
                            ? binding.Target.LoopRateUnitsPerWheelTick
                            : HotkeyDefaults.LoopRateUnitsPerWheelTick);
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        this._manager.UpdateLoopRateUnitsPerWheelTick(id, dialog.UnitsPerWheelTick);
                    }
                }
                else
                {
                    using HotkeyHoldDurationDialog dialog = new(
                        binding.ActionName,
                        binding.HoldThresholdMs);
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        this._manager.UpdateHoldThreshold(id, dialog.HoldThresholdMs);
                    }
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
            this._registerBlinkTimer.Stop();
            this._registerBlinkTimer.Tick -= this.RegisterBlinkTimer_Tick;
            this._registerBlinkTimer.Dispose();
            this._manager.Changed -= this.Manager_Changed;
            this._manager.KeyboardsChanged -= this.Manager_KeyboardsChanged;
            if (ReferenceEquals(_openWindow, this))
            {
                _openWindow = null;
            }
        }

        private void ButtonPausingSyncerHotkey_Click(object? sender, EventArgs e)
        {
            if (this._capturingPausingSyncer)
            {
                this._manager.CancelCapture();
                this._capturingPausingSyncer = false;
                this._buttonPausingSyncerHotkey.Text = "Set Pausing Syncer Hotkey";
                this.UpdateSyncerHotkeyLabels();
                return;
            }

            this._capturingPausingSyncer = true;
            this._capturingNudgingSyncer = false;
            this._buttonPausingSyncerHotkey.Text = "Press key combination... (Esc to cancel)";
            this._buttonNudgingSyncerHotkey.Text = "Set Nudging Syncer Hotkey";
            this._manager.BeginRegisterNew(null, "Pausing Syncer");
        }

        private void ButtonNudgingSyncerHotkey_Click(object? sender, EventArgs e)
        {
            if (this._capturingNudgingSyncer)
            {
                this._manager.CancelCapture();
                this._capturingNudgingSyncer = false;
                this._buttonNudgingSyncerHotkey.Text = "Set Nudging Syncer Hotkey";
                this.UpdateSyncerHotkeyLabels();
                return;
            }

            this._capturingNudgingSyncer = true;
            this._capturingPausingSyncer = false;
            this._buttonNudgingSyncerHotkey.Text = "Press key combination... (Esc to cancel)";
            this._buttonPausingSyncerHotkey.Text = "Set Pausing Syncer Hotkey";
            this._manager.BeginRegisterNew(null, "Nudging Syncer");
        }

        private void UpdateSyncerHotkeyLabels()
        {
            HotkeyCombo pausing = this._manager.PausingSyncerHotkey;
            HotkeyCombo nudging = this._manager.NudgingSyncerHotkey;

            this._labelPausingSyncerHotkey.Text = pausing.IsEmpty ? "Not set" : pausing.GetDisplay();
            this._labelNudgingSyncerHotkey.Text = nudging.IsEmpty ? "Not set" : nudging.GetDisplay();
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
