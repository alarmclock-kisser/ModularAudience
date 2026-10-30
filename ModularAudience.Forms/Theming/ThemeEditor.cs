using System.ComponentModel;
using System.Text.Json;

namespace ModularAudience.Forms.Theming;

public sealed class ThemeEditor : Form
{
    private ColorTheme draft;
    private ColorTheme? before;
    private readonly TextBox themeName = new() { Dock = DockStyle.Fill };
    private readonly TreeView elements = new() { Dock = DockStyle.Fill, HideSelection = false };
    private readonly ListBox rules = new() { Dock = DockStyle.Fill, DisplayMember = nameof(ThemeRule.Description) };
    private readonly DataGridView colors = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false, AllowUserToAddRows = false,
        AllowUserToDeleteRows = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect };
    private readonly TextBox formType = new() { Dock = DockStyle.Fill };
    private readonly TextBox controlType = new() { Dock = DockStyle.Fill };
    private readonly TextBox elementPath = new() { Dock = DockStyle.Fill };
    private readonly TextBox description = new() { Dock = DockStyle.Fill };
    private readonly CheckBox enabled = new() { Text = "Rule enabled", AutoSize = true };
    private readonly CheckBox live = new() { Text = "Live preview", Checked = true, AutoSize = true };
    private readonly Label status = new() { Dock = DockStyle.Fill, AutoSize = true };
    private readonly ComboBox scope = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
    private readonly TextBox json = new() { Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true, AcceptsTab = true,
        ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font(FontFamily.GenericMonospace, 10) };
    private bool updating;
    private bool committed;
    private readonly System.Windows.Forms.Timer editTimer = new() { Interval = 180 };
    private TextBox? editingText;
    private ElementPicker? picker;
    private readonly ComboBox savedThemes = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240 };
    private string? editingPath;
    private bool refreshingThemes;
    private static readonly string[] PaletteProperties = typeof(ThemeColors).GetProperties().Select(p => p.Name).ToArray();
    private static readonly string[] StandardProperties = ["BackColor", "ForeColor", "SelectionBackColor", "SelectionForeColor", "BorderColor", "AccentColor", "DisabledForeColor"];

    public ThemeEditor()
    {
        this.Text = "Theme Editor";
        this.Name = "ThemeEditor";
        this.Size = new Size(1180, 800);
        this.MinimumSize = new Size(900, 620);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.before = ThemeManager.Current?.Clone();
        this.draft = ThemeManager.Current?.Clone() ?? new ColorTheme { Name = "No theme" };
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(10) };
        root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.AutoSize));
        var toolbar = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        toolbar.Controls.Add(new Label { Text = "Load Theme", AutoSize = true, Margin = new Padding(3, 8, 3, 3) });
        toolbar.Controls.Add(this.savedThemes);
        AddButton(toolbar, "Delete theme", this.DeleteTheme);
        AddButton(toolbar, "Load JSON…", this.LoadTheme);
        AddButton(toolbar, "Save JSON…", this.SaveTheme);
        AddButton(toolbar, "Dark preset", () => this.ReplaceDraft(ColorTheme.Dark()));
        AddButton(toolbar, "Light preset", () => this.ReplaceDraft(ColorTheme.Light()));
        toolbar.Controls.Add(this.live);
        AddButton(toolbar, "Apply & remember", this.Commit);
        AddButton(toolbar, "Restore original colors", () =>
        {
            ThemeManager.SetTheme(null, persist: true); this.before = null; this.committed = true;
            this.status.Text = "Original application colors restored. Automatic theming is disabled.";
        });
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var editorTab = new TabPage("Palette and element rules");
        var jsonTab = new TabPage("JSON editor");
        tabs.TabPages.AddRange([editorTab, jsonTab]);
        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 350, Width = 1100, Panel1MinSize = 240, Panel2MinSize = 440 };
        var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        left.RowStyles.Add(new(SizeType.AutoSize)); left.RowStyles.Add(new(SizeType.Percent, 65));
        left.RowStyles.Add(new(SizeType.AutoSize)); left.RowStyles.Add(new(SizeType.Percent, 35));
        var treeBar = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        AddButton(treeBar, "Refresh open windows", this.RefreshElements);
        AddButton(treeBar, "Pick element on screen", this.StartPicker);
        var ruleBar = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        this.scope.Items.AddRange(["Selected element", "Control type", "Selected window", "Global rule"]);
        this.scope.SelectedIndex = 0;
        ruleBar.Controls.Add(this.scope);
        AddButton(ruleBar, "Add rule", this.AddRule);
        AddButton(ruleBar, "Remove", () =>
        {
            if (this.rules.SelectedItem is ThemeRule rule) this.draft.Rules.Remove(rule);
            this.RefreshRules(); this.ShowPalette(); this.Preview();
        });
        AddButton(ruleBar, "Duplicate", () =>
        {
            if (this.rules.SelectedItem is not ThemeRule rule) return;
            var copy = JsonSerializer.Deserialize<ThemeRule>(JsonSerializer.Serialize(rule))!;
            copy.Description += " (copy)"; this.draft.Rules.Add(copy); this.RefreshRules(); this.rules.SelectedItem = copy; this.Preview();
        });
        AddButton(ruleBar, "↑", () => this.MoveRule(-1));
        AddButton(ruleBar, "↓", () => this.MoveRule(1));
        left.Controls.Add(treeBar, 0, 0); left.Controls.Add(this.elements, 0, 1);
        left.Controls.Add(ruleBar, 0, 2); left.Controls.Add(this.rules, 0, 3);
        split.Panel1.Controls.Add(left);
        var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 9, Padding = new Padding(8) };
        right.ColumnStyles.Add(new(SizeType.Absolute, 115)); right.ColumnStyles.Add(new(SizeType.Percent, 100));
        void Field(string caption, Control control, int row)
        {
            right.Controls.Add(new Label { Text = caption, AutoSize = true, Margin = new Padding(3, 7, 3, 3) }, 0, row);
            right.Controls.Add(control, 1, row);
            right.RowStyles.Add(new(SizeType.AutoSize));
        }
        Field("Theme name", this.themeName, 0);
        Field("Rule description", this.description, 1);
        Field("Form type", this.formType, 2);
        Field("Control type", this.controlType, 3);
        Field("Element path", this.elementPath, 4);
        Field("", this.enabled, 5);
        var hint = new Label { Text = "Blank color = inherit. Select a color cell and click Choose color.\nSelectors accept * and ?. Rules apply in list order; later rules override earlier rules.", AutoSize = true, Dock = DockStyle.Fill };
        right.Controls.Add(hint, 0, 6); right.SetColumnSpan(hint, 2); right.RowStyles.Add(new(SizeType.AutoSize));
        this.colors.Columns.Add(new DataGridViewTextBoxColumn { Name = "Property", HeaderText = "Color property", ReadOnly = true, Width = 200 });
        this.colors.Columns.Add(new DataGridViewTextBoxColumn { Name = "Value", HeaderText = "Color (#RRGGBB / name)", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        this.colors.Columns.Add(new DataGridViewTextBoxColumn { Name = "Swatch", HeaderText = "Preview", ReadOnly = true, Width = 70 });
        right.Controls.Add(this.colors, 0, 7); right.SetColumnSpan(this.colors, 2); right.RowStyles.Add(new(SizeType.Percent, 100));
        var colorBar = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        AddButton(colorBar, "Global palette", this.ShowPalette);
        AddButton(colorBar, "Choose color…", this.ChooseColor);
        AddButton(colorBar, "Inherit / clear", () => { if (this.colors.CurrentRow is { } row) { row.Cells[1].Value = ""; this.StoreColors(); } });
        right.Controls.Add(colorBar, 0, 8); right.SetColumnSpan(colorBar, 2); right.RowStyles.Add(new(SizeType.AutoSize));
        split.Panel2.Controls.Add(right);
        editorTab.Controls.Add(split);
        var jsonLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        jsonLayout.RowStyles.Add(new(SizeType.AutoSize)); jsonLayout.RowStyles.Add(new(SizeType.Percent, 100));
        var jsonBar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        AddButton(jsonBar, "Refresh JSON from editor", this.RefreshJson);
        AddButton(jsonBar, "Validate & use JSON", () => this.Run(() => this.ReplaceDraft(ThemeFiles.Deserialize(this.json.Text))));
        jsonLayout.Controls.Add(jsonBar, 0, 0); jsonLayout.Controls.Add(this.json, 0, 1); jsonTab.Controls.Add(jsonLayout);
        root.Controls.Add(toolbar, 0, 0); root.Controls.Add(tabs, 0, 1); root.Controls.Add(this.status, 0, 2);
        root.Controls.Add(new Label { Text = $"Themes directory: {ThemeFiles.SettingsDirectory}", AutoSize = true }, 0, 3);
        this.Controls.Add(root);
        this.rules.SelectedIndexChanged += (_, _) => this.ShowSelectedRule();
        this.savedThemes.DropDown += (_, _) => this.RefreshSavedThemes();
        this.savedThemes.SelectedIndexChanged += (_, _) =>
        {
            if (this.refreshingThemes) return;
            this.Run(() =>
            {
                if (this.savedThemes.SelectedItem is SavedTheme item)
                {
                    var theme = ThemeFiles.Load(item.Path); this.editingPath = item.Path; this.ReplaceDraft(theme);
                }
                else if (this.savedThemes.SelectedIndex == 0)
                {
                    this.editingPath = null;
                    this.ReplaceDraft(new ColorTheme { Name = "No theme" });
                    ThemeManager.SetTheme(null);
                    this.status.Text = "No theme — original application colors. Apply & remember to keep this choice.";
                }
            });
        };
        this.colors.CellEndEdit += (_, _) => this.StoreColors();
        this.colors.EditingControlShowing += (_, e) =>
        {
            if (this.editingText != null) this.editingText.TextChanged -= this.EditTextChanged;
            this.editingText = e.Control as TextBox;
            if (this.editingText != null) this.editingText.TextChanged += this.EditTextChanged;
        };
        this.editTimer.Tick += (_, _) =>
        {
            this.editTimer.Stop();
            if (this.editingText == null || !this.colors.IsCurrentCellInEditMode || this.colors.CurrentCell?.ColumnIndex != 1) return;
            string value = this.editingText.Text.Trim();
            try
            {
                if (value.Length > 0) ThemeFiles.ParseColor(value);
                this.colors.CurrentCell.Value = value;
                this.StoreColors();
            }
            catch (FormatException) { this.status.Text = "Enter #RRGGBB, #AARRGGBB or a named color. Partial input is not applied."; }
        };
        this.colors.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex == 1) this.ChooseColor(); };
        foreach (var box in new[] { this.description, this.formType, this.controlType, this.elementPath })
            box.TextChanged += (_, _) => this.StoreSelectors();
        this.enabled.CheckedChanged += (_, _) => this.StoreSelectors();
        this.themeName.TextChanged += (_, _) => { if (!this.updating) { this.draft.Name = this.themeName.Text; this.Preview(); } };
        this.live.CheckedChanged += (_, _) => { if (this.live.Checked) this.Preview(); else ThemeManager.SetTheme(this.before); };
        this.FormClosed += (_, _) =>
        {
            this.editTimer.Dispose();
            if (this.picker != null) Application.RemoveMessageFilter(this.picker);
            if (!this.committed) ThemeManager.SetTheme(this.before);
        };
        this.ReplaceDraft(this.draft);
        this.RefreshSavedThemes();
        this.Shown += (_, _) => this.RefreshElements();
    }
    private static void AddButton(Control parent, string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true };
        button.Click += (_, _) => action(); parent.Controls.Add(button);
    }
    private void Run(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or FormatException or ArgumentException)
        { this.status.Text = error.Message; MessageBox.Show(this, error.Message, "Theme editor", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
    private void ReplaceDraft(ColorTheme theme)
    {
        this.draft = theme.Clone();
        this.updating = true; this.themeName.Text = this.draft.Name; this.updating = false;
        this.RefreshRules(); this.ShowPalette(); this.RefreshJson(); this.Preview();
    }
    private void RefreshRules()
    {
        this.updating = true;
        this.rules.Items.Clear(); this.rules.Items.AddRange(this.draft.Rules.Cast<object>().ToArray());
        this.updating = false;
    }
    private void ShowPalette()
    {
        this.updating = true; this.rules.SelectedIndex = -1;
        foreach (var control in new Control[] { this.description, this.formType, this.controlType, this.elementPath, this.enabled }) control.Enabled = false;
        this.colors.Rows.Clear();
        foreach (string key in PaletteProperties) this.colors.Rows.Add(key, typeof(ThemeColors).GetProperty(key)!.GetValue(this.draft.Palette) ?? "");
        this.updating = false; this.PaintSwatches();
    }
    private void ShowSelectedRule()
    {
        if (this.updating || this.rules.SelectedItem is not ThemeRule rule) return;
        this.updating = true;
        foreach (var control in new Control[] { this.description, this.formType, this.controlType, this.elementPath, this.enabled }) control.Enabled = true;
        this.description.Text = rule.Description; this.formType.Text = rule.FormType; this.controlType.Text = rule.ControlType;
        this.elementPath.Text = rule.ElementPath; this.enabled.Checked = rule.Enabled;
        this.colors.Rows.Clear();
        var selected = this.elements.SelectedNode?.Tag;
        string[] actual = selected == null ? [] : TypeDescriptor.GetProperties(selected).Cast<PropertyDescriptor>()
            .Where(p => p.PropertyType == typeof(Color) && !p.IsReadOnly).Select(p => p.Name).ToArray();
        foreach (string key in StandardProperties.Concat(actual).Concat(rule.Colors.Keys).Distinct(StringComparer.OrdinalIgnoreCase))
            this.colors.Rows.Add(key, rule.Colors.GetValueOrDefault(key) ?? "");
        this.updating = false; this.PaintSwatches();
    }
    private void StoreSelectors()
    {
        if (this.updating || this.rules.SelectedItem is not ThemeRule rule) return;
        rule.Description = this.description.Text; rule.FormType = this.formType.Text; rule.ControlType = this.controlType.Text;
        rule.ElementPath = this.elementPath.Text; rule.Enabled = this.enabled.Checked;
        this.rules.Refresh(); this.Preview();
    }
    private void StoreColors()
    {
        if (this.updating) return;
        this.Run(() =>
        {
            var values = new Dictionary<string, string>();
            foreach (DataGridViewRow row in this.colors.Rows)
            {
                string text = row.Cells[1].Value?.ToString()?.Trim() ?? "";
                if (text.Length > 0) { ThemeFiles.ParseColor(text); values[row.Cells[0].Value!.ToString()!] = text; }
            }
            if (this.rules.SelectedItem is ThemeRule rule) rule.Colors = values;
            else foreach (string key in PaletteProperties) typeof(ThemeColors).GetProperty(key)!.SetValue(this.draft.Palette, values.GetValueOrDefault(key));
            this.PaintSwatches(); this.Preview();
        });
    }
    private void PaintSwatches()
    {
        foreach (DataGridViewRow row in this.colors.Rows)
        {
            if (row.Cells[1].Value is not string text || string.IsNullOrWhiteSpace(text)) { row.Cells[2].Style = new(); row.Cells[2].Value = "inherit"; continue; }
            try
            {
                Color color = ThemeFiles.ParseColor(text);
                var style = row.Cells[2].Style;
                style.BackColor = color; style.SelectionBackColor = color;
                style.ForeColor = color.GetBrightness() < 0.5 ? Color.White : Color.Black;
                style.SelectionForeColor = style.ForeColor;
                row.Cells[2].Value = "■";
            }
            catch (FormatException) { }
        }
    }
    private void ChooseColor()
    {
        if (this.colors.CurrentRow is not { } row) return;
        using var picker = new ColorDialog { FullOpen = true, CustomColors = this.draft.Rules.SelectMany(r => r.Colors.Values)
            .Concat(PaletteProperties.Select(p => typeof(ThemeColors).GetProperty(p)!.GetValue(this.draft.Palette) as string).OfType<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => ColorTranslator.ToOle(ThemeFiles.ParseColor(s))).Distinct().Take(16).ToArray() };
        try { if (row.Cells[1].Value is string color && color.Length > 0) picker.Color = ThemeFiles.ParseColor(color); } catch (FormatException) { }
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        row.Cells[1].Value = ThemeFiles.FormatColor(picker.Color); this.StoreColors();
    }
    private void Preview()
    {
        if (!this.live.Checked) return;
        this.committed = false;
        this.Run(() => { ThemeManager.SetTheme(this.IsEmptyTheme() ? null : this.draft); this.PaintSwatches(); this.status.Text = "Live preview — Apply & remember to keep this theme. Closing discards unapplied changes."; });
    }
    private void Commit() => this.Run(() =>
    {
        this.colors.EndEdit();
        bool empty = this.IsEmptyTheme();
        ThemeManager.SetTheme(empty ? null : this.draft, persist: true);
        if (this.editingPath != null && !empty) ThemeFiles.Save(this.editingPath, this.draft);
        this.before = empty ? null : this.draft.Clone(); this.committed = false;
        this.status.Text = "Theme applied and saved as active-theme.json.";
    });
    private void RefreshJson() => this.json.Text = JsonSerializer.Serialize(this.draft, ThemeFiles.JsonOptions);
    private void LoadTheme() => this.Run(() =>
    {
        Directory.CreateDirectory(ThemeFiles.SettingsDirectory);
        using var dialog = new OpenFileDialog { Filter = "Color themes (*.json)|*.json", InitialDirectory = ThemeFiles.SettingsDirectory };
        if (dialog.ShowDialog(this) == DialogResult.OK) { this.editingPath = dialog.FileName; this.ReplaceDraft(ThemeFiles.Load(dialog.FileName)); this.RefreshSavedThemes(); }
    });
    private void SaveTheme() => this.Run(() =>
    {
        this.colors.EndEdit(); Directory.CreateDirectory(ThemeFiles.SettingsDirectory);
        using var dialog = new SaveFileDialog { Filter = "Color themes (*.json)|*.json", InitialDirectory = ThemeFiles.SettingsDirectory,
            FileName = "theme.json", DefaultExt = "json" };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            ThemeFiles.Save(dialog.FileName, this.draft); this.editingPath = dialog.FileName;
            this.RefreshSavedThemes(); this.status.Text = $"Saved {dialog.FileName}";
        }
    });
    private void RefreshElements()
    {
        this.elements.Nodes.Clear();
        TreeNode Visit(Control control)
        {
            var node = new TreeNode($"{control.GetType().Name}: {(string.IsNullOrEmpty(control.Name) ? control.Text : control.Name)}") { Tag = control };
            foreach (Control child in control.Controls) node.Nodes.Add(Visit(child));
            if (control is ToolStrip strip) AddItems(node, strip.Items);
            if (control.ContextMenuStrip is { } menu)
            {
                var menuNode = new TreeNode("@contextMenu") { Tag = menu }; AddItems(menuNode, menu.Items); node.Nodes.Add(menuNode);
            }
            return node;
        }
        foreach (Form form in Application.OpenForms.Cast<Form>().ToArray()) if (!ReferenceEquals(form, this)) this.elements.Nodes.Add(Visit(form));
        void AddItems(TreeNode parent, ToolStripItemCollection items)
        {
            foreach (ToolStripItem item in items)
            {
                var node = new TreeNode($"{item.GetType().Name}: {(string.IsNullOrEmpty(item.Name) ? item.Text : item.Name)}") { Tag = item };
                parent.Nodes.Add(node); if (item is ToolStripDropDownItem drop) AddItems(node, drop.DropDownItems);
            }
        }
    }
    private void AddRule()
    {
        object? target = this.elements.SelectedNode?.Tag;
        var control = target as Control;
        Form? form = control as Form ?? control?.FindForm();
        if (target is ToolStripItem item) form = item.Owner?.FindForm();
        if (this.scope.SelectedIndex < 3 && target == null) { this.status.Text = "Select an element in the open windows tree first."; return; }
        string path = control == null ? "*" : ThemeManager.ElementPath(control);
        if (target is ToolStripItem)
        {
            // Tree nodes mirror the menu hierarchy, including the context-menu path segment.
            var parts = new Stack<string>();
            for (TreeNode? node = this.elements.SelectedNode; node != null; node = node.Parent)
            {
                if (node.Tag is ToolStripItem menuItem) parts.Push(string.IsNullOrEmpty(menuItem.Name) ? $"{menuItem.GetType().Name}[{menuItem.Owner?.Items.IndexOf(menuItem) ?? 0}]" : menuItem.Name);
                else if (node.Text == "@contextMenu") parts.Push("@contextMenu");
                else if (node.Tag is Control parentControl) { path = ThemeManager.ElementPath(parentControl) + "/" + string.Join("/", parts); break; }
            }
            TreeNode? root = this.elements.SelectedNode;
            while (root?.Parent != null) root = root.Parent;
            form ??= root?.Tag as Form;
        }
        var rule = new ThemeRule { Description = this.scope.Text + ": " + (target?.GetType().Name ?? "all controls"),
            FormType = this.scope.SelectedIndex is 0 or 2 ? form?.GetType().FullName ?? "*" : "*",
            ControlType = this.scope.SelectedIndex is 0 or 1 ? target?.GetType().FullName ?? "*" : "*",
            ElementPath = this.scope.SelectedIndex == 0 ? path : "*" };
        this.draft.Rules.Add(rule); this.RefreshRules(); this.rules.SelectedItem = rule;
    }
    private void MoveRule(int delta)
    {
        if (this.rules.SelectedItem is not ThemeRule rule) return;
        int index = this.draft.Rules.IndexOf(rule), next = Math.Clamp(index + delta, 0, this.draft.Rules.Count - 1);
        this.draft.Rules.RemoveAt(index); this.draft.Rules.Insert(next, rule);
        this.RefreshRules(); this.rules.SelectedItem = rule; this.Preview();
    }
    private void EditTextChanged(object? sender, EventArgs e) { this.editTimer.Stop(); this.editTimer.Start(); }
    private bool IsEmptyTheme() => this.draft.Rules.Count == 0 && typeof(ThemeColors).GetProperties().All(p => string.IsNullOrWhiteSpace(p.GetValue(this.draft.Palette) as string));
    private sealed record SavedTheme(string Path)
    {
        public override string ToString() => System.IO.Path.GetFileNameWithoutExtension(Path);
    }
    private void RefreshSavedThemes()
    {
        this.refreshingThemes = true;
        try
        {
            this.savedThemes.Items.Clear(); this.savedThemes.Items.Add("No theme — original Forms colors");
            foreach (string path in ThemeFiles.SavedThemes()) this.savedThemes.Items.Add(new SavedTheme(path));
            this.savedThemes.SelectedItem = this.savedThemes.Items.OfType<SavedTheme>()
                .FirstOrDefault(t => string.Equals(t.Path, this.editingPath, StringComparison.OrdinalIgnoreCase));
            if (this.IsEmptyTheme()) this.savedThemes.SelectedIndex = 0;
        }
        finally { this.refreshingThemes = false; }
    }
    private void DeleteTheme() => this.Run(() =>
    {
        if (this.savedThemes.SelectedItem is not SavedTheme item) { this.status.Text = "Select a saved theme to delete."; return; }
        if (MessageBox.Show(this, $"Delete '{item}'?", "Delete theme", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        File.Delete(item.Path);
        ThemeManager.SetTheme(null, persist: true); this.before = null;
        this.editingPath = null; this.ReplaceDraft(new ColorTheme { Name = "No theme" });
        ThemeManager.SetTheme(null); this.RefreshSavedThemes();
        this.status.Text = "Theme deleted. Original application colors restored.";
    });
    private void StartPicker()
    {
        if (this.picker != null) Application.RemoveMessageFilter(this.picker);
        this.status.Text = "Click a control in any application window to select it. Press Escape to cancel picking.";
        this.picker = new ElementPicker(control =>
        {
            this.RefreshElements();
            TreeNode? Find(TreeNodeCollection nodes)
            {
                foreach (TreeNode node in nodes)
                {
                    if (ReferenceEquals(node.Tag, control)) return node;
                    if (Find(node.Nodes) is { } result) return result;
                }
                return null;
            }
            if (Find(this.elements.Nodes) is { } selected)
            {
                this.elements.SelectedNode = selected; selected.EnsureVisible();
                this.status.Text = $"Selected {ThemeManager.ElementPath(control)}. Click Add rule to customize this element.";
            }
            this.Activate();
        });
        Application.AddMessageFilter(this.picker);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.editTimer.Dispose();
            if (this.picker != null) Application.RemoveMessageFilter(this.picker);
        }
        base.Dispose(disposing);
    }
    private sealed class ElementPicker(Action<Control> selected) : IMessageFilter
    {
        private bool finishOnMouseUp;
        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg == 0x100 && (Keys)m.WParam == Keys.Escape) { Application.RemoveMessageFilter(this); return true; }
            if (finishOnMouseUp && m.Msg == 0x202) { Application.RemoveMessageFilter(this); return true; }
            if (m.Msg != 0x201) return false;
            if (Control.FromChildHandle(m.HWnd) is not { } control || control.FindForm() is ThemeEditor) return false;
            this.finishOnMouseUp = true; selected(control); return true;
        }
    }
}
