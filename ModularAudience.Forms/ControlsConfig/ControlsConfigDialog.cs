using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ModularAudience.Forms.ControlsConfig
{
    /// <summary>
    /// Editor for the Breakbeat Pattern Editor control scheme.
    /// Gestures (mouse button + action, wheel direction, key + modifiers) can be recorded for a
    /// primary and an alternative slot of every action. Duplicate gestures are reported live and
    /// resolved deterministically on save (the action further down the list keeps the gesture).
    /// </summary>
    public sealed class ControlsConfigDialog : Form, IMessageFilter
    {
        private const int WM_MOUSEWHEEL = 0x020A;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_LBUTTONDBLCLK = 0x0203;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_RBUTTONDBLCLK = 0x0206;
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_MBUTTONUP = 0x0208;
        private const int WM_MBUTTONDBLCLK = 0x0209;

        private static readonly Color Background = Color.FromArgb(28, 30, 34);
        private static readonly Color Surface = Color.FromArgb(35, 38, 44);
        private static readonly Color SurfaceAlt = Color.FromArgb(45, 49, 57);
        private static readonly Color Border = Color.FromArgb(70, 76, 86);
        private static readonly Color TextMain = Color.FromArgb(222, 226, 232);
        private static readonly Color TextDim = Color.FromArgb(150, 158, 170);
        private static readonly Color Accent = Color.FromArgb(75, 190, 155);
        private static readonly Color Danger = Color.FromArgb(235, 110, 110);
        private static readonly Color Warn = Color.FromArgb(240, 180, 90);
        private static readonly Color Locked = Color.FromArgb(120, 200, 140);

        private readonly ControlsSettingsManager _settingsManager;
        private readonly ControlScheme _workingScheme;
        private readonly List<Font> _ownedFonts = [];

        /// <summary>Action ids the user actually touched - they win any conflict on save.</summary>
        private readonly HashSet<string> _editedActionIds = new(StringComparer.Ordinal);

        private TreeView _tree = null!;
        private TextBox _searchBox = null!;
        private Panel _detailHost = null!;
        private Label _statusLabel = null!;
        private Button _saveButton = null!;
        private Button _resetAllButton = null!;

        private ControlBinding? _current;
        private bool _recording;
        private Slot _recordSlot;
        private Button? _recordButtonPrimary;
        private Button? _recordButtonSecondary;
        private Label? _valueLabelPrimary;
        private Label? _valueLabelSecondary;
        private Point _pressOrigin;
        private int _pressedButton;
        private Button? _cancelButton;
        private Panel? _buttonHost;

        private enum Slot { Primary, Secondary }

        public ControlsConfigDialog(ControlsSettingsManager settingsManager)
        {
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _workingScheme = settingsManager.CreateWorkingCopy();

            BuildLayout();
            PopulateTree();
            UpdateFooter();
        }

        private Font UiFont(float size, FontStyle style = FontStyle.Regular)
        {
            Font font = new("Segoe UI", size, style);
            _ownedFonts.Add(font);
            return font;
        }

        private static Font MonoFont(float size)
        {
            return new Font("Consolas", size, FontStyle.Regular);
        }

        // ------------------------------------------------------------------ layout

        private void BuildLayout()
        {
            Text = "Steuerung konfigurieren – Breakbeat Pattern Editor";
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Background;
            ForeColor = TextMain;
            Font = UiFont(9f);
            ClientSize = new Size(980, 680);
            MinimumSize = new Size(860, 560);
            KeyPreview = true;
            ShowInTaskbar = false;

            TableLayoutPanel root = new()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 3,
                BackColor = Background,
                Padding = new Padding(12)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340f));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));

            // ---- row 0: search
            _searchBox = new TextBox
            {
                Dock = DockStyle.Fill,
                BackColor = Surface,
                ForeColor = TextMain,
                BorderStyle = BorderStyle.FixedSingle,
                PlaceholderText = "Aktionen filtern …"
            };
            _searchBox.TextChanged += (_, _) => PopulateTree();
            Label searchLabel = new()
            {
                Text = "Filter",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = TextDim,
                Margin = new Padding(0, 4, 8, 4)
            };

            Panel searchHost = new() { Dock = DockStyle.Fill, BackColor = Background, Margin = new Padding(0, 0, 0, 8) };
            searchHost.Controls.Add(_searchBox);
            searchHost.Controls.Add(searchLabel);
            searchLabel.Location = new Point(0, 0);
            searchLabel.Size = new Size(56, 28);
            _searchBox.Location = new Point(60, 1);
            _searchBox.Size = new Size(searchHost.Width - 60, 26);
            searchHost.Resize += (_, _) => _searchBox.Width = Math.Max(40, searchHost.Width - 60);
            root.Controls.Add(searchLabel, 0, 0);
            root.Controls.Add(searchHost, 1, 0);

            // ---- row 1: tree | detail
            _tree = new TreeView
            {
                Dock = DockStyle.Fill,
                BackColor = Surface,
                ForeColor = TextMain,
                BorderStyle = BorderStyle.None,
                HideSelection = false,
                FullRowSelect = true,
                ShowLines = false,
                ShowPlusMinus = true,
                ShowRootLines = false,
                Indent = 16
            };
            _tree.AfterSelect += Tree_AfterSelect;
            Panel treeHost = new() { Dock = DockStyle.Fill, BackColor = Surface, Padding = new Padding(1), Margin = new Padding(0, 0, 8, 0) };
            treeHost.Controls.Add(_tree);
            root.Controls.Add(treeHost, 0, 1);

            _detailHost = new Panel { Dock = DockStyle.Fill, BackColor = Background, Padding = new Padding(8, 0, 0, 0) };
            root.Controls.Add(_detailHost, 1, 1);

            // ---- row 2: status (left) + buttons (right)
            _statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = TextDim,
                AutoEllipsis = true,
                Margin = new Padding(0, 0, 12, 0)
            };
            root.Controls.Add(_statusLabel, 0, 2);

            _saveButton = FlatButton("Speichern & schließen", 170, Color.FromArgb(35, 80, 62), Accent, Color.White);
            _saveButton.Anchor = AnchorStyles.Right;
            _saveButton.Click += (_, _) => SaveAndClose();

            _cancelButton = FlatButton("Abbrechen", 110, SurfaceAlt, SurfaceAlt, TextMain);
            _cancelButton.Anchor = AnchorStyles.Right;
            _cancelButton.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

            _resetAllButton = FlatButton("Alles zurücksetzen", 150, SurfaceAlt, SurfaceAlt, TextMain);
            _resetAllButton.Anchor = AnchorStyles.Right;
            _resetAllButton.Click += (_, _) => ResetAll();

            _buttonHost = new Panel { Dock = DockStyle.Fill, BackColor = Background };
            _buttonHost.Controls.Add(_saveButton);
            _buttonHost.Controls.Add(_cancelButton);
            _buttonHost.Controls.Add(_resetAllButton);
            _buttonHost.Resize += (_, _) => LayoutFooterButtons();
            root.Controls.Add(_buttonHost, 1, 2);

            Controls.Add(root);
            Shown += (_, _) => LayoutFooterButtons();
        }

        private void LayoutFooterButtons()
        {
            if (_buttonHost == null || _cancelButton == null)
            {
                return;
            }

            int right = _buttonHost.ClientSize.Width;
            int y = Math.Max(0, (_buttonHost.ClientSize.Height - 34) / 2);

            _saveButton.Location = new Point(Math.Max(0, right - _saveButton.Width), y);
            _cancelButton.Location = new Point(Math.Max(0, right - _saveButton.Width - 8 - _cancelButton.Width), y);
            _resetAllButton.Location = new Point(Math.Max(0, _cancelButton.Left - 8 - _resetAllButton.Width), y);
        }

        private Button FlatButton(string text, int width, Color back, Color border, Color fore)
        {
            Button b = new()
            {
                Text = text,
                Size = new Size(width, 34),
                FlatStyle = FlatStyle.Flat,
                BackColor = back,
                ForeColor = fore,
                Font = UiFont(9f),
                UseVisualStyleBackColor = false,
                TabStop = true
            };
            b.FlatAppearance.BorderColor = border;
            b.FlatAppearance.MouseOverBackColor = ControlPaint.Light(back, 0.08f);
            return b;
        }

        // ------------------------------------------------------------------ tree

        private void PopulateTree()
        {
            string filter = _searchBox.Text.Trim();
            ControlBinding? previous = _current;

            _tree.BeginUpdate();
            _tree.Nodes.Clear();

            foreach (var category in Enum.GetValues(typeof(ActionCategory)).Cast<ActionCategory>().OrderBy(c => c))
            {
                List<ControlBinding> items = _workingScheme.Bindings
                    .Where(b => b.Category == category)
                    .Where(b => filter.Length == 0
                                || b.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                                || b.ActionId.Contains(filter, StringComparison.OrdinalIgnoreCase)
                                || b.Description.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(b => b.SortOrder)
                    .ThenBy(b => b.ActionId, StringComparer.Ordinal)
                    .ToList();

                if (items.Count == 0)
                {
                    continue;
                }

                TreeNode categoryNode = new(GetCategoryName(category))
                {
                    ForeColor = TextDim,
                    NodeFont = UiFont(9f, FontStyle.Bold)
                };

                foreach (ControlBinding binding in items)
                {
                    TreeNode node = new(MakeNodeText(binding))
                    {
                        Tag = binding,
                        ToolTipText = binding.Description,
                        ForeColor = NodeColor(binding)
                    };
                    categoryNode.Nodes.Add(node);
                }

                categoryNode.Expand();
                _tree.Nodes.Add(categoryNode);
            }

            _tree.EndUpdate();

            if (previous != null)
            {
                SelectBinding(previous.ActionId);
            }
            else if (_tree.Nodes.Count > 0)
            {
                _tree.SelectedNode = _tree.Nodes[0].Nodes.Count > 0 ? _tree.Nodes[0].Nodes[0] : _tree.Nodes[0];
            }

            RefreshAllNodeTexts();
        }

        private void SelectBinding(string actionId)
        {
            TreeNode? found = FindNode(_tree.Nodes, actionId);
            if (found != null)
            {
                _tree.SelectedNode = found;
            }
        }

        private static TreeNode? FindNode(TreeNodeCollection nodes, string actionId)
        {
            foreach (TreeNode node in nodes)
        {
            if (node.Tag is ControlBinding b && b.ActionId == actionId)
            {
                return node;
            }

            TreeNode? child = FindNode(node.Nodes, actionId);
            if (child != null)
            {
                return child;
            }
        }
            return null;
        }

        private void RefreshAllNodeTexts()
        {
            foreach (TreeNode category in _tree.Nodes)
            {
                foreach (TreeNode node in category.Nodes)
                {
                    if (node.Tag is ControlBinding binding)
                    {
                        node.Text = MakeNodeText(binding);
                        node.ForeColor = NodeColor(binding);
                    }
                }
            }
        }

        private string MakeNodeText(ControlBinding binding)
        {
            string marker = !binding.Implemented ? "⚠ " : binding.IsHardcoded && !binding.AllowRemapping ? "🔒 " : string.Empty;
            return $"{marker}{binding.DisplayName}  —  {binding.GetBindingString()}";
        }

        private static Color NodeColor(ControlBinding binding)
        {
            if (!binding.Implemented)
            {
                return Color.FromArgb(130, 138, 150);
            }
            if (binding.IsHardcoded && !binding.AllowRemapping)
            {
                return Locked;
            }
            return binding.PrimaryInput == InputType.None ? TextDim : TextMain;
        }

        private static string GetCategoryName(ActionCategory category) => category switch
        {
            ActionCategory.NoteEditing => "Noten bearbeiten",
            ActionCategory.Selection => "Selektion",
            ActionCategory.Clipboard => "Zwischenablage",
            ActionCategory.Navigation => "Navigation",
            ActionCategory.View => "Ansicht",
            ActionCategory.Playback => "Wiedergabe",
            ActionCategory.LoopControl => "Loop-Bereiche",
            ActionCategory.TrackManagement => "Tracks",
            ActionCategory.Settings => "Einstellungen",
            _ => category.ToString()
        };

        // ------------------------------------------------------------------ detail pane

        private void Tree_AfterSelect(object? sender, TreeViewEventArgs e)
        {
            _current = e.Node?.Tag as ControlBinding;
            RebuildDetailPane();
        }

        private void ClearDetailPane()
        {
            foreach (Control control in _detailHost.Controls.Cast<Control>().ToList())
            {
                _detailHost.Controls.Remove(control);
                DisposeControlTree(control);
            }
        }

        /// <summary>Disposes a control and its children so GDI handles (fonts/brushes) are released.</summary>
        private static void DisposeControlTree(Control control)
        {
            foreach (Control child in control.Controls.Cast<Control>().ToList())
            {
                DisposeControlTree(child);
            }

            if (control is Label or Button or CheckBox or ComboBox or TextBox)
            {
                Font? font = control.Font;
                control.Dispose();
                font?.Dispose();
            }
            else
            {
                control.Dispose();
            }
        }

        private void RebuildDetailPane()
        {
            ClearDetailPane();
            _recordButtonPrimary = null;
            _recordButtonSecondary = null;
            _valueLabelPrimary = null;
            _valueLabelSecondary = null;

            if (_current == null)
            {
                Label hint = new()
                {
                    Dock = DockStyle.Top,
                    Height = 60,
                    Text = "Wähle links eine Aktion, um ihre Belegung zu ändern.",
                    ForeColor = TextDim,
                    TextAlign = ContentAlignment.TopLeft
                };
                _detailHost.Controls.Add(hint);
                return;
            }

            ControlBinding binding = _current;
            bool locked = binding.IsHardcoded && !binding.AllowRemapping;
            bool readOnly = locked || !binding.Implemented;

            // Explicit rows keep the pane stable - no AutoSize/GroupBox guessing games.
            TableLayoutPanel pane = new()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                BackColor = Background,
                Padding = new Padding(14, 10, 14, 10)
            };
            pane.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            void AddRow(Control control, int height)
            {
                int row = pane.RowCount++;
                pane.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
                pane.Controls.Add(control, 0, row);
            }

            AddRow(new Label
            {
                Text = binding.DisplayName,
                Font = UiFont(12f, FontStyle.Bold),
                ForeColor = TextMain,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            }, 26);

            AddRow(new Label
            {
                Text = binding.Description,
                Font = UiFont(8.5f),
                ForeColor = TextDim,
                Dock = DockStyle.Fill,
                AutoEllipsis = true
            }, 34);

            AddRow(Gap(), 10);

            // ---- primary slot
            AddRow(SlotHeader("Primäre Belegung"), 20);
            AddRow(BuildSlotRow(binding, Slot.Primary, readOnly), 34);
            AddRow(new Label
            {
                Text = "Taste, Maustaste (LMB/RMB/MMB), Doppelklick, Drag oder Mausrad aufnehmen – " +
                       "Modifier (Ctrl/Shift/Alt/Win) werden automatisch mit erfasst.",
                Font = UiFont(8f),
                ForeColor = Color.FromArgb(118, 126, 138),
                Dock = DockStyle.Fill,
                AutoEllipsis = true
            }, 18);
            AddRow(Gap(), 12);

            // ---- alternative slot
            AddRow(SlotHeader("Alternative Belegung (optional)"), 20);
            AddRow(BuildSlotRow(binding, Slot.Secondary, readOnly), 34);
            AddRow(Gap(), 12);

            if (readOnly)
            {
                AddRow(new Label
                {
                    Text = locked
                        ? "🔒 Diese Aktion ist fest verdrahtet und lässt sich nicht ändern."
                        : "⚠ Diese Aktion wertet der Editor noch fest verdrahtet aus – eine Änderung hätte noch keine Wirkung.",
                    Font = UiFont(8.5f),
                    ForeColor = locked ? Locked : Warn,
                    Dock = DockStyle.Fill,
                    AutoSize = false
                }, 34);
            }

            Button resetThis = FlatButton("Standardbelegung wiederherstellen", 210, SurfaceAlt, SurfaceAlt, TextMain);
            resetThis.Enabled = !locked;
            resetThis.Click += (_, _) => ResetThisAction();
            Panel resetHost = new() { Dock = DockStyle.Fill, BackColor = Background };
            resetHost.Controls.Add(resetThis);
            resetThis.Location = new Point(0, 0);
            AddRow(resetHost, 34);

            // Let the description label have any leftover vertical space.
            pane.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            pane.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = Background }, 0, pane.RowCount - 1);

            _detailHost.Controls.Add(pane);
            RefreshConflictBanner();
        }

        private static Control Gap() => new Panel { Dock = DockStyle.Fill, BackColor = Background, Height = 1 };

        private static Label SlotHeader(string text) => new()
        {
            Text = text,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = TextDim,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };

        /// <summary>One row: [current value] [record] [clear] [mouse action]</summary>
        private Control BuildSlotRow(ControlBinding binding, Slot slot, bool readOnly)
        {
            TableLayoutPanel row = new()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                BackColor = Background,
                Margin = Padding.Empty
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 138f));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104f));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 124f));

            Label value = new()
            {
                Text = slot == Slot.Primary ? binding.GetBindingString() : binding.GetSecondaryBindingString(),
                Font = MonoFont(10f),
                ForeColor = slot == Slot.Primary ? Accent : TextDim,
                Dock = DockStyle.Fill,
                BackColor = Surface,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 0, 6, 0),
                AutoEllipsis = true,
                Margin = new Padding(0, 0, 8, 0)
            };
            if (slot == Slot.Primary)
            {
                _valueLabelPrimary = value;
            }
            else
            {
                _valueLabelSecondary = value;
            }

            Button record = FlatButton("Aufnehmen …", 130, Color.FromArgb(40, 74, 60), Accent, Color.White);
            record.Dock = DockStyle.Fill;
            record.Margin = new Padding(0, 0, 6, 0);
            record.Enabled = !readOnly;
            record.Click += (_, _) => StartRecording(slot);
            if (slot == Slot.Primary)
            {
                _recordButtonPrimary = record;
            }
            else
            {
                _recordButtonSecondary = record;
            }

            Button clear = FlatButton("Entfernen", 96, Color.FromArgb(70, 40, 40), Color.FromArgb(150, 60, 60), Danger);
            clear.Dock = DockStyle.Fill;
            clear.Margin = new Padding(0, 0, 6, 0);
            clear.Click += (_, _) => ClearSlot(slot);
            clear.Enabled = !readOnly && SlotValue(binding, slot) != InputType.None;

            ComboBox mouseAction = new()
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = Surface,
                ForeColor = TextMain,
                Font = UiFont(8.5f),
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Enabled = !readOnly
            };
            foreach (MouseAction action in Enum.GetValues(typeof(MouseAction)))
            {
                mouseAction.Items.Add(action);
            }
            mouseAction.SelectedItem = slot == Slot.Primary ? binding.PrimaryMouseAction : binding.SecondaryMouseAction;
            mouseAction.SelectedIndexChanged += (_, _) =>
            {
                if (mouseAction.SelectedItem is not MouseAction chosen || _recording)
                {
                    return;
                }

                if (slot == Slot.Primary)
                {
                    binding.PrimaryMouseAction = chosen;
                }
                else
                {
                    binding.SecondaryMouseAction = chosen;
                }
                OnBindingEdited();
            };

            row.Controls.Add(value, 0, 0);
            row.Controls.Add(record, 1, 0);
            row.Controls.Add(clear, 2, 0);
            row.Controls.Add(mouseAction, 3, 0);
            return row;
        }

        private static InputType SlotValue(ControlBinding binding, Slot slot)
            => slot == Slot.Primary ? binding.PrimaryInput : binding.SecondaryInput;

        private void OnBindingEdited()
        {
            if (_current == null)
            {
                return;
            }

            _editedActionIds.Add(_current.ActionId);

            if (_valueLabelPrimary != null)
            {
                _valueLabelPrimary.Text = _current.GetBindingString();
            }
            if (_valueLabelSecondary != null)
            {
                _valueLabelSecondary.Text = _current.GetSecondaryBindingString();
            }

            RefreshConflictBanner();
            RefreshAllNodeTexts();
            UpdateFooter();
        }

        private Label? _conflictBanner;

        private void RefreshConflictBanner()
        {
            if (_conflictBanner != null)
            {
                _detailHost.Controls.Remove(_conflictBanner);
                _conflictBanner.Dispose();
                _conflictBanner = null;
            }

            if (_current == null)
            {
                return;
            }

            List<ControlBinding> conflicts = _workingScheme.GetConflicts(_current);
            if (conflicts.Count == 0)
            {
                return;
            }

            _conflictBanner = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 24,
                BackColor = Color.FromArgb(70, 48, 24),
                ForeColor = Warn,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 0, 10, 0),
                Text = "⚠ Doppelbelegung mit: " + string.Join(", ", conflicts.Select(c => c.DisplayName))
                       + " – beim Speichern bleibt die zuletzt gelistete Aktion übrig."
            };
            _detailHost.Controls.Add(_conflictBanner);
            _conflictBanner.BringToFront();
        }

        private void UpdateFooter()
        {
            int total = _workingScheme.Bindings.Count;
            int unbound = _workingScheme.Bindings.Count(b => b.PrimaryInput == InputType.None);
            int conflicting = _workingScheme.GetAllConflicts().Count;

            _statusLabel.Text = conflicting > 0
                ? $"{total} Aktionen · {unbound} ohne Primärbelegung · ⚠ {conflicting} mit Doppelbelegung"
                : $"{total} Aktionen · {unbound} ohne Primärbelegung";

            _statusLabel.ForeColor = conflicting > 0 ? Warn : TextDim;
        }

        // ------------------------------------------------------------------ editing

        private void ClearSlot(Slot slot)
        {
            if (_current == null)
            {
                return;
            }

            if (slot == Slot.Primary)
            {
                _current.PrimaryInput = InputType.None;
                _current.PrimaryKey = Keys.None;
                _current.PrimaryModifiers = ModifierKey.None;
            }
            else
            {
                _current.SecondaryInput = InputType.None;
                _current.SecondaryKey = Keys.None;
                _current.SecondaryModifiers = ModifierKey.None;
            }

            OnBindingEdited();
        }

        private void ResetThisAction()
        {
            if (_current == null)
            {
                return;
            }

            ControlBinding? def = DefaultControlScheme.Create().Find(_current.ActionId);
            if (def == null)
            {
                return;
            }

            _current.PrimaryInput = def.PrimaryInput;
            _current.PrimaryKey = def.PrimaryKey;
            _current.PrimaryModifiers = def.PrimaryModifiers;
            _current.PrimaryMouseAction = def.PrimaryMouseAction;
            _current.SecondaryInput = def.SecondaryInput;
            _current.SecondaryKey = def.SecondaryKey;
            _current.SecondaryModifiers = def.SecondaryModifiers;
            _current.SecondaryMouseAction = def.SecondaryMouseAction;

            RebuildDetailPane();
            RefreshAllNodeTexts();
            UpdateFooter();
        }

        private void ResetAll()
        {
            if (MessageBox.Show(this,
                    "Wirklich alle Belegungen auf die Standardwerte zurücksetzen?\n" +
                    "Das wird erst nach 'Speichern & schließen' übernommen.",
                    "Alles zurücksetzen", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            _workingScheme.Bindings.Clear();
            foreach (ControlBinding def in DefaultControlScheme.Create().Bindings)
            {
                _workingScheme.Bindings.Add(def.Clone());
            }
            _workingScheme.RebuildIndex();

            _current = null;
            PopulateTree();
            UpdateFooter();
        }

        // ------------------------------------------------------------------ gesture recording

        private void StartRecording(Slot slot)
        {
            if (_current == null)
            {
                return;
            }

            StopRecording();

            _recording = true;
            _recordSlot = slot;
            _pressedButton = 0;

            Button? button = slot == Slot.Primary ? _recordButtonPrimary : _recordButtonSecondary;
            if (button != null)
            {
                button.Text = "… Esc abbrechen";
                button.BackColor = Color.FromArgb(90, 130, 60);
            }

            _statusLabel.Text = "Aufnahme läuft: Taste / Maustaste / Mausrad / Doppelklick / Drag drücken. Esc bricht ab.";
            _statusLabel.ForeColor = Accent;

            // A message filter is required so we also see mouse input that happens anywhere
            // on screen (the pointer usually leaves the button while recording).
            Application.AddMessageFilter(this);
        }

        private void StopRecording()
        {
            if (!_recording)
            {
                return;
            }

            _recording = false;
            Application.RemoveMessageFilter(this);

            foreach (Button? button in new[] { _recordButtonPrimary, _recordButtonSecondary })
            {
                if (button == null)
                {
                    continue;
                }

                bool primary = ReferenceEquals(button, _recordButtonPrimary);
                button.Text = "Aufnehmen …";
                button.BackColor = primary ? Color.FromArgb(40, 74, 60) : SurfaceAlt;
            }
        }

        bool IMessageFilter.PreFilterMessage(ref Message msg) => PreFilterMessage(ref msg);

        private const int DragThresholdPixels = 4;

        private bool PreFilterMessage(ref Message msg)
        {
            if (!_recording)
            {
                return false;
            }

            switch (msg.Msg)
            {
                case WM_MOUSEWHEEL:
                {
                    // Wheel delta lives in the high word of wParam.
                    int delta = (short)((msg.WParam.ToInt64() >> 16) & 0xFFFF);
                    ApplyRecorded(InputType.MouseWheel,
                        delta < 0 ? MouseAction.WheelDown : MouseAction.WheelUp,
                        Keys.None);
                    return true;
                }

                case WM_LBUTTONDOWN:
                case WM_RBUTTONDOWN:
                case WM_MBUTTONDOWN:
                    _pressOrigin = Cursor.Position;
                    _pressedButton = msg.Msg;
                    return true;

                case WM_LBUTTONDBLCLK:
                case WM_RBUTTONDBLCLK:
                case WM_MBUTTONDBLCLK:
                    ApplyRecorded(ButtonToInput(msg.Msg), MouseAction.DoubleClick, Keys.None);
                    return true;

                case WM_LBUTTONUP:
                case WM_RBUTTONUP:
                case WM_MBUTTONUP:
                {
                    if (_pressedButton != msg.Msg)
                    {
                        return true;
                    }

                    _pressedButton = 0;
                    bool dragged = Math.Abs(Cursor.Position.X - _pressOrigin.X) >= DragThresholdPixels
                                   || Math.Abs(Cursor.Position.Y - _pressOrigin.Y) >= DragThresholdPixels;

                    ApplyRecorded(ButtonToInput(msg.Msg), dragged ? MouseAction.Drag : MouseAction.Click, Keys.None);
                    return true;
                }
            }

            return false;
        }

        private static InputType ButtonToInput(int mouseMessage) => mouseMessage switch
        {
            WM_LBUTTONDOWN or WM_LBUTTONUP or WM_LBUTTONDBLCLK => InputType.MouseLeft,
            WM_RBUTTONDOWN or WM_RBUTTONUP or WM_RBUTTONDBLCLK => InputType.MouseRight,
            _ => InputType.MouseMiddle
        };

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (_recording)
            {
                if (keyData == Keys.Escape)
                {
                    StopRecording();
                    RebuildDetailPane();
                    UpdateFooter();
                    return true;
                }

                Keys rawKey = keyData & Keys.KeyCode;
                ModifierKey mods = TranslateModifiers(keyData);

                // Ignore the bare modifier presses so "Ctrl + S" is recorded as Ctrl+S.
                if (rawKey is Keys.ControlKey or Keys.LControlKey or Keys.RControlKey
                    or Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey
                    or Keys.Menu or Keys.LMenu or Keys.RMenu
                    or Keys.LWin or Keys.RWin)
                {
                    return true;
                }

                if (mods != ModifierKey.None && rawKey == Keys.None)
                {
                    return true;
                }

                if (rawKey != Keys.None)
                {
                    ApplyRecorded(InputType.KeyCombo, MouseAction.Click, rawKey, mods);
                    return true;
                }

                return true;
            }

            if (keyData == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private static ModifierKey TranslateModifiers(Keys keyData)
        {
            ModifierKey mods = ModifierKey.None;
            if ((keyData & Keys.Control) == Keys.Control) mods |= ModifierKey.Control;
            if ((keyData & Keys.Shift) == Keys.Shift) mods |= ModifierKey.Shift;
            if ((keyData & Keys.Alt) == Keys.Alt) mods |= ModifierKey.Alt;
            if ((keyData & Keys.LWin) == Keys.LWin || (keyData & Keys.RWin) == Keys.RWin) mods |= ModifierKey.Win;
            return mods;
        }

        private void ApplyRecorded(InputType input, MouseAction mouseAction, Keys key, ModifierKey? modifiers = null)
        {
            if (_current == null)
            {
                StopRecording();
                return;
            }

            ModifierKey mods = modifiers ?? TranslateModifiers(ModifierKeys);

            if (_recordSlot == Slot.Primary)
            {
                _current.PrimaryInput = input;
                _current.PrimaryKey = input is InputType.Key or InputType.KeyCombo ? key : Keys.None;
                _current.PrimaryModifiers = mods;
                _current.PrimaryMouseAction = mouseAction;
            }
            else
            {
                _current.SecondaryInput = input;
                _current.SecondaryKey = input is InputType.Key or InputType.KeyCombo ? key : Keys.None;
                _current.SecondaryModifiers = mods;
                _current.SecondaryMouseAction = mouseAction;
            }

            StopRecording();
            RebuildDetailPane();
            _editedActionIds.Add(_current.ActionId);
            RefreshAllNodeTexts();
            UpdateFooter();
        }

        // ------------------------------------------------------------------ save

        private void SaveAndClose()
        {
            IReadOnlyList<ControlBinding> unbound = _settingsManager.Apply(_workingScheme, _editedActionIds);

            if (unbound.Count > 0)
            {
                MessageBox.Show(this,
                    "Folgende Aktionen haben ihre Belegung verloren, weil sie doppelt vergeben war:\n\n"
                    + string.Join("\n", unbound.Select(b => "  • " + b.DisplayName)),
                    "Doppelbelegung aufgelöst", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            StopRecording();
            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopRecording();
                foreach (Font font in _ownedFonts)
                {
                    font.Dispose();
                }
                _ownedFonts.Clear();
            }

            base.Dispose(disposing);
        }
    }
}
