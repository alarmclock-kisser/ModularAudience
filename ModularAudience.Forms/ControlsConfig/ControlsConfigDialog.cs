using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ModularAudience.Forms.ControlsConfig;

namespace ModularAudience.Forms.ControlsConfig
{
    public sealed class ControlsConfigDialog : Form
    {
        private readonly ControlsSettingsManager _settingsManager;
        private readonly ControlScheme _originalScheme;
        private ControlScheme _workingScheme;
        private TreeView _treeView;
        private Panel _detailPanel;
        private Label _conflictLabel;
        private Button _btnSave;
        private Button _btnCancel;
        private Button _btnResetDefaults;
        private ControlBinding _editingBinding;
        private bool _isCapturingKey;
        private Keys _capturedKey;
        private ModifierKey _capturedModifiers;

        public ControlsConfigDialog(ControlsSettingsManager settingsManager)
        {
            _settingsManager = settingsManager;
            _originalScheme = settingsManager.CurrentScheme.Clone();
            _workingScheme = settingsManager.CurrentScheme.Clone();

            InitializeComponent();
            PopulateTree();
        }

        private void InitializeComponent()
        {
            this.Text = "Steuerungskonfiguration – Breakbeat Pattern Editor";
            this.Size = new Size(900, 650);
            this.MinimumSize = new Size(700, 500);
            this.StartPosition = FormStartPosition.CenterParent;
            this.BackColor = Color.FromArgb(28, 30, 34);
            this.ForeColor = Color.FromArgb(220, 222, 226);
            this.Font = new Font("Segoe UI", 9f);

            // Main layout: Tree on left, details on right
            var splitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                FixedPanel = FixedPanel.Panel1,
                Panel1MinSize = 300,
                Panel2MinSize = 350,
                SplitterDistance = 350,
                BackColor = Color.FromArgb(28, 30, 34)
            };

            // TreeView
            _treeView = new TreeView
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(35, 38, 44),
                ForeColor = Color.FromArgb(220, 222, 226),
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 9f),
                HideSelection = false,
                FullRowSelect = true,
                ShowLines = true,
                ShowPlusMinus = true,
                ShowRootLines = true,
                Indent = 18,
                ItemHeight = 22
            };
            _treeView.AfterSelect += TreeView_AfterSelect;
            _treeView.DrawMode = TreeViewDrawMode.OwnerDrawText;
            _treeView.DrawNode += TreeView_DrawNode;
            splitContainer.Panel1.Controls.Add(_treeView);

            // Detail Panel (right side)
            _detailPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(28, 30, 34),
                Padding = new Padding(12)
            };
            splitContainer.Panel2.Controls.Add(_detailPanel);

            // Conflict label at top of detail panel
            _conflictLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 60,
                ForeColor = Color.FromArgb(255, 180, 80),
                Font = new Font("Segoe UI", 8.5f),
                TextAlign = ContentAlignment.MiddleLeft,
                Visible = false,
                BackColor = Color.FromArgb(45, 35, 25),
                Padding = new Padding(8)
            };
            _detailPanel.Controls.Add(_conflictLabel);

            // Buttons at bottom
            var buttonPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 50,
                BackColor = Color.FromArgb(28, 30, 34),
                Padding = new Padding(12, 8, 12, 8)
            };

            _btnResetDefaults = new Button
            {
                Text = "↶ Standardwerte",
                Size = new Size(130, 34),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(55, 60, 70),
                ForeColor = Color.FromArgb(220, 222, 226),
                Font = new Font("Segoe UI", 8.5f)
            };
            _btnResetDefaults.FlatAppearance.BorderColor = Color.FromArgb(85, 90, 100);
            _btnResetDefaults.Click += (s, e) => ResetToDefaults();

            _btnCancel = new Button
            {
                Text = "Abbrechen",
                Size = new Size(100, 34),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(55, 60, 70),
                ForeColor = Color.FromArgb(220, 222, 226),
                Font = new Font("Segoe UI", 8.5f)
            };
            _btnCancel.FlatAppearance.BorderColor = Color.FromArgb(85, 90, 100);
            _btnCancel.Click += (s, e) => this.DialogResult = DialogResult.Cancel;

            _btnSave = new Button
            {
                Text = "Speichern & Schließen",
                Size = new Size(140, 34),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(45, 100, 65),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };
            _btnSave.FlatAppearance.BorderColor = Color.FromArgb(75, 190, 155);
            _btnSave.Click += (s, e) => SaveAndClose();

            buttonPanel.Controls.AddRange(new Control[] { _btnResetDefaults, _btnCancel, _btnSave });
            _detailPanel.Controls.Add(buttonPanel);

            this.Controls.Add(splitContainer);

            // Key preview capture
            this.KeyPreview = true;
            this.KeyDown += ControlsConfigDialog_KeyDown;
            this.KeyUp += ControlsConfigDialog_KeyUp;
        }

        private void PopulateTree()
        {
            _treeView.BeginUpdate();
            _treeView.Nodes.Clear();

            var categories = Enum.GetValues(typeof(ActionCategory)).Cast<ActionCategory>().OrderBy(c => c);
            foreach (var category in categories)
            {
                var bindings = _workingScheme.Bindings.Where(b => b.Category == category).OrderBy(b => b.SortOrder).ToList();
                if (bindings.Count == 0) continue;

                var catNode = new TreeNode(GetCategoryDisplayName(category))
                {
                    Tag = category,
                    ForeColor = Color.FromArgb(155, 165, 176),
                    NodeFont = new Font("Segoe UI", 9f, FontStyle.Bold)
                };

                foreach (var binding in bindings)
                {
                    var actionNode = new TreeNode(binding.DisplayName)
                    {
                        Tag = binding,
                        ToolTipText = binding.Description
                    };
                    UpdateNodeAppearance(actionNode, binding);
                    catNode.Nodes.Add(actionNode);
                }

                catNode.Expand();
                _treeView.Nodes.Add(catNode);
            }

            _treeView.EndUpdate();
        }

        private static string GetCategoryDisplayName(ActionCategory category)
        {
            return category switch
            {
                ActionCategory.NoteEditing => "🎵 Noten bearbeiten",
                ActionCategory.Selection => "🔲 Selektion",
                ActionCategory.Clipboard => "📋 Zwischenablage",
                ActionCategory.Navigation => "🧭 Navigation",
                ActionCategory.View => "👁 Ansicht",
                ActionCategory.Playback => "▶ Wiedergabe",
                ActionCategory.LoopControl => "🔁 Loop-Bereiche",
                ActionCategory.TrackManagement => "🎚 Tracks",
                ActionCategory.Settings => "⚙ Einstellungen",
                _ => category.ToString()
            };
        }

        private void UpdateNodeAppearance(TreeNode node, ControlBinding binding)
        {
            var conflicts = _workingScheme.GetConflicts(binding);
            bool hasConflict = conflicts.Count > 0;
            bool isUnbound = binding.PrimaryInput == InputType.None;

            if (hasConflict)
            {
                node.ForeColor = Color.FromArgb(255, 120, 120);
                node.Text = $"⚠ {binding.DisplayName}";
            }
            else if (isUnbound)
            {
                node.ForeColor = Color.FromArgb(120, 120, 120);
                node.Text = $"○ {binding.DisplayName}";
            }
            else if (binding.IsHardcoded && !binding.AllowRemapping)
            {
                node.ForeColor = Color.FromArgb(100, 200, 100);
                node.Text = $"🔒 {binding.DisplayName}";
            }
            else
            {
                node.ForeColor = Color.FromArgb(220, 222, 226);
            }
        }

        private void TreeView_DrawNode(object sender, DrawTreeNodeEventArgs e)
        {
            e.DrawDefault = true;
        }

        private void TreeView_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (e.Node.Tag is ControlBinding binding)
            {
                ShowBindingDetails(binding);
            }
            else
            {
                ClearDetailPanel();
            }
        }

        private void ShowBindingDetails(ControlBinding binding)
        {
            _editingBinding = binding;
            _detailPanel.SuspendLayout();

            // Clear existing controls (except conflict label and button panel)
            var controlsToRemove = new List<Control>();
            foreach (Control c in _detailPanel.Controls)
            {
                if (c != _conflictLabel && c != _detailPanel.Controls.OfType<Panel>().FirstOrDefault(p => p.Dock == DockStyle.Bottom))
                    controlsToRemove.Add(c);
            }
            foreach (var c in controlsToRemove) _detailPanel.Controls.Remove(c);

            int y = 80; // Below conflict label

            // Action name
            var lblName = new Label
            {
                Text = binding.DisplayName,
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(245, 248, 255),
                AutoSize = true,
                Location = new Point(12, y)
            };
            _detailPanel.Controls.Add(lblName);
            y += 30;

            // Description
            var lblDesc = new Label
            {
                Text = binding.Description,
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(155, 165, 176),
                AutoSize = false,
                Size = new Size(_detailPanel.ClientSize.Width - 24, 40),
                Location = new Point(12, y)
            };
            _detailPanel.Controls.Add(lblDesc);
            y += 50;

            // Primary binding
            var gbPrimary = CreateBindingGroupBox("Primäre Belegung", binding, true, ref y);
            _detailPanel.Controls.Add(gbPrimary);

            // Secondary binding (if applicable)
            if (binding.SecondaryInput != InputType.None || binding.AllowRemapping)
            {
                var gbSecondary = CreateBindingGroupBox("Sekundäre / Alternative Belegung", binding, false, ref y);
                _detailPanel.Controls.Add(gbSecondary);
            }

            // Hardcoded notice
            if (binding.IsHardcoded && !binding.AllowRemapping)
            {
                var lblHardcoded = new Label
                {
                    Text = "🔒 Diese Aktion ist fest verdrahtet und kann nicht geändert werden.",
                    Font = new Font("Segoe UI", 8f, FontStyle.Italic),
                    ForeColor = Color.FromArgb(100, 200, 100),
                    AutoSize = true,
                    Location = new Point(12, y)
                };
                _detailPanel.Controls.Add(lblHardcoded);
                y += 25;
            }

            // Conflict warning
            var conflicts = _workingScheme.GetConflicts(binding);
            if (conflicts.Count > 0)
            {
                _conflictLabel.Text = "⚠ KONFLIKT: Diese Belegung wird auch verwendet von:\n" +
                    string.Join("\n", conflicts.Select(c => $"  • {c.DisplayName} ({c.GetBindingString()})")) +
                    "\n\nWenn Sie speichern, verlieren die anderen Aktionen ihre Belegung.";
                _conflictLabel.Visible = true;
            }
            else
            {
                _conflictLabel.Visible = false;
            }

            _detailPanel.ResumeLayout();
        }

        private GroupBox CreateBindingGroupBox(string title, ControlBinding binding, bool isPrimary, ref int y)
        {
            var gb = new GroupBox
            {
                Text = title,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(155, 165, 176),
                Size = new Size(_detailPanel.ClientSize.Width - 24, 110),
                Location = new Point(12, y),
                FlatStyle = FlatStyle.Flat
            };
            y += 120;

            // Current binding display
            var lblCurrent = new Label
            {
                Text = isPrimary ? binding.GetBindingString() : binding.GetSecondaryBindingString(),
                Font = new Font("Consolas", 9.5f),
                ForeColor = isPrimary ? Color.FromArgb(75, 190, 155) : Color.FromArgb(155, 165, 176),
                AutoSize = false,
                Size = new Size(gb.ClientSize.Width - 20, 24),
                Location = new Point(10, 24),
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.FromArgb(35, 38, 44),
                Padding = new Padding(8, 0, 8, 0)
            };
            gb.Controls.Add(lblCurrent);

            // Capture button
            var btnCapture = new Button
            {
                Text = isPrimary ? "🎹 Neue Taste drücken…" : "🎹 Alternative festlegen…",
                Size = new Size(180, 30),
                Location = new Point(10, 54),
                FlatStyle = FlatStyle.Flat,
                BackColor = isPrimary ? Color.FromArgb(45, 80, 65) : Color.FromArgb(50, 55, 65),
                ForeColor = isPrimary ? Color.White : Color.FromArgb(180, 180, 180),
                Font = new Font("Segoe UI", 8.5f),
                Tag = new { Binding = binding, IsPrimary = isPrimary, Label = lblCurrent },
                Enabled = binding.AllowRemapping
            };
            btnCapture.FlatAppearance.BorderColor = isPrimary ? Color.FromArgb(75, 190, 155) : Color.FromArgb(85, 90, 100);
            btnCapture.Click += BtnCapture_Click;
            gb.Controls.Add(btnCapture);

            // Clear button
            var btnClear = new Button
            {
                Text = "✕ Entfernen",
                Size = new Size(100, 30),
                Location = new Point(200, 54),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(70, 40, 40),
                ForeColor = Color.FromArgb(255, 120, 120),
                Font = new Font("Segoe UI", 8.5f),
                Tag = new { Binding = binding, IsPrimary = isPrimary, Label = lblCurrent },
                Enabled = binding.AllowRemapping && binding.PrimaryInput != InputType.None
            };
            btnClear.FlatAppearance.BorderColor = Color.FromArgb(180, 60, 60);
            btnClear.Click += BtnClear_Click;
            gb.Controls.Add(btnClear);

            // Mouse action dropdown (for mouse bindings)
            if (binding.PrimaryInput != InputType.Key && binding.PrimaryInput != InputType.KeyCombo)
            {
                var cbMouseAction = new ComboBox
                {
                    Size = new Size(180, 26),
                    Location = new Point(10, 84),
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    BackColor = Color.FromArgb(35, 38, 44),
                    ForeColor = Color.FromArgb(220, 222, 226),
                    FlatStyle = FlatStyle.Flat,
                    Font = new Font("Segoe UI", 8.5f),
                    Tag = new { Binding = binding, IsPrimary = isPrimary, Label = lblCurrent }
                };
                cbMouseAction.Items.AddRange(new[] { "Click", "DoubleClick", "Drag", "Press", "Release", "Wheel ↑", "Wheel ↓" });
                cbMouseAction.SelectedItem = isPrimary ? binding.PrimaryMouseAction.ToString() : binding.SecondaryMouseAction.ToString();
                cbMouseAction.SelectedIndexChanged += CbMouseAction_SelectedIndexChanged;
                gb.Controls.Add(cbMouseAction);
            }

            return gb;
        }

        private void BtnCapture_Click(object sender, EventArgs e)
        {
            if (sender is not Button btn) return;
            dynamic tag = btn.Tag;
            _editingBinding = tag.Binding;
            bool isPrimary = tag.IsPrimary;
            _isCapturingKey = true;
            _capturedKey = Keys.None;
            _capturedModifiers = ModifierKey.None;

            btn.Text = "⌨ Drücken Sie Taste/Kombination…";
            btn.BackColor = Color.FromArgb(80, 120, 60);
            btn.ForeColor = Color.White;
            this.Focus();
        }

        private void ControlsConfigDialog_KeyDown(object sender, KeyEventArgs e)
        {
            if (!_isCapturingKey) return;

            _capturedKey = e.KeyCode;
            _capturedModifiers = ModifierKey.None;
            if (e.Control) _capturedModifiers |= ModifierKey.Control;
            if (e.Shift) _capturedModifiers |= ModifierKey.Shift;
            if (e.Alt) _capturedModifiers |= ModifierKey.Alt;
            if (e.KeyCode == Keys.LWin || e.KeyCode == Keys.RWin) _capturedModifiers |= ModifierKey.Win;

            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        private void ControlsConfigDialog_KeyUp(object sender, KeyEventArgs e)
        {
            if (!_isCapturingKey) return;

            _isCapturingKey = false;

            // Find the capture button and update
            foreach (Control c in _detailPanel.Controls)
            {
                if (c is GroupBox gb)
                {
                    foreach (Control gc in gb.Controls)
                    {
                        if (gc is Button btn && btn.Text.Contains("Drücken Sie"))
                        {
                            dynamic tag = btn.Tag;
                            var binding = tag.Binding;
                            bool isPrimary = tag.IsPrimary;
                            var lblCurrent = tag.Label;

                            // Apply the captured key
                            if (isPrimary)
                            {
                                binding.PrimaryInput = InputType.KeyCombo;
                                binding.PrimaryKey = _capturedKey;
                                binding.PrimaryModifiers = _capturedModifiers;
                            }
                            else
                            {
                                binding.SecondaryInput = InputType.KeyCombo;
                                binding.SecondaryKey = _capturedKey;
                                binding.SecondaryModifiers = _capturedModifiers;
                            }

                            lblCurrent.Text = isPrimary ? binding.GetBindingString() : binding.GetSecondaryBindingString();
                            btn.Text = isPrimary ? "🎹 Neue Taste drücken…" : "🎹 Alternative festlegen…";
                            btn.BackColor = isPrimary ? Color.FromArgb(45, 80, 65) : Color.FromArgb(50, 55, 65);
                            btn.ForeColor = isPrimary ? Color.White : Color.FromArgb(180, 180, 180);

                            // Check conflicts
                            var conflicts = _workingScheme.GetConflicts(binding);
                            if (conflicts.Count > 0)
                            {
                                string[] conflictLines = new string[conflicts.Count];
                                for (int i = 0; i < conflicts.Count; i++)
                                {
                                    var cb = conflicts[i];
                                    conflictLines[i] = $"  • {cb.DisplayName} ({cb.GetBindingString()})";
                                }
                                _conflictLabel.Text = "⚠ KONFLIKT: Diese Belegung wird auch verwendet von:\n" +
                                    string.Join("\n", conflictLines) +
                                    "\n\nWenn Sie speichern, verlieren die anderen Aktionen ihre Belegung.";
                                _conflictLabel.Visible = true;
                            }
                            else
                            {
                                _conflictLabel.Visible = false;
                            }

                            UpdateNodeAppearance(_treeView.SelectedNode, binding);
                            break;
                        }
                    }
                }
            }
        }

        private void BtnClear_Click(object sender, EventArgs e)
        {
            if (sender is not Button btn) return;
            dynamic tag = btn.Tag;
            var binding = tag.Binding;
            bool isPrimary = tag.IsPrimary;
            var lblCurrent = tag.Label;

            if (isPrimary)
            {
                binding.PrimaryInput = InputType.None;
                binding.PrimaryKey = Keys.None;
                binding.PrimaryModifiers = ModifierKey.None;
                binding.PrimaryMouseAction = MouseAction.Click;
            }
            else
            {
                binding.SecondaryInput = InputType.None;
                binding.SecondaryKey = Keys.None;
                binding.SecondaryModifiers = ModifierKey.None;
                binding.SecondaryMouseAction = MouseAction.Click;
            }

            lblCurrent.Text = isPrimary ? binding.GetBindingString() : binding.GetSecondaryBindingString();
            _conflictLabel.Visible = false;
            UpdateNodeAppearance(_treeView.SelectedNode, binding);
        }

        private void CbMouseAction_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (sender is not ComboBox cb) return;
            dynamic tag = cb.Tag;
            var binding = tag.Binding;
            bool isPrimary = tag.IsPrimary;

            if (Enum.TryParse<MouseAction>(cb.SelectedItem?.ToString(), out var action))
            {
                if (isPrimary)
                    binding.PrimaryMouseAction = action;
                else
                    binding.SecondaryMouseAction = action;

                var lblCurrent = tag.Label;
                lblCurrent.Text = isPrimary ? binding.GetBindingString() : binding.GetSecondaryBindingString();
            }
        }

        private void ClearDetailPanel()
        {
            _editingBinding = null;
            _conflictLabel.Visible = false;

            var controlsToRemove = new List<Control>();
            foreach (Control c in _detailPanel.Controls)
            {
                if (c != _conflictLabel && c != _detailPanel.Controls.OfType<Panel>().FirstOrDefault(p => p.Dock == DockStyle.Bottom))
                    controlsToRemove.Add(c);
            }
            foreach (var c in controlsToRemove) _detailPanel.Controls.Remove(c);
        }

        private void ResetToDefaults()
        {
            var result = MessageBox.Show(this,
                "Alle Belegungen auf Standardwerte zurücksetzen?",
                "Standardwerte wiederherstellen",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                _workingScheme = DefaultControlScheme.Create();
                PopulateTree();
                ClearDetailPanel();
            }
        }

        private void SaveAndClose()
        {
            // Apply working scheme to settings manager
            foreach (var binding in _workingScheme.Bindings)
            {
                var original = _originalScheme.BindingsById[binding.ActionId];
                var conflicts = _workingScheme.GetConflicts(binding);

                if (conflicts.Count > 0)
                {
                    // Force update, clearing conflicts
                    _settingsManager.ForceUpdateBinding(binding.ActionId, binding, conflicts);
                }
                else
                {
                    _settingsManager.TryUpdateBinding(binding.ActionId, binding, out _);
                }
            }

            this.DialogResult = DialogResult.OK;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape && !_isCapturingKey)
            {
                this.DialogResult = DialogResult.Cancel;
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}