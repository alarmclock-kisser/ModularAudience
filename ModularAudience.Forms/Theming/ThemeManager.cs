using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace ModularAudience.Forms.Theming;

public static class ThemeManager
{
    private sealed class Original
    {
        public Dictionary<string, Color> Colors { get; } = [];
        public bool? VisualStyle;
        public ToolStripRenderer? Renderer;
        public DataGridViewCellStyle? CellStyle, HeaderStyle, RowHeaderStyle;
        public bool HeaderVisualStyles;
        public DrawMode? DrawMode;
    }
    private static readonly ConditionalWeakTable<object, Original> Originals = new();
    private static readonly ConditionalWeakTable<Control, object> Watched = new();
    private static readonly ConditionalWeakTable<ToolStrip, object> WatchedStrips = new();
    private static readonly HashSet<Form> Forms = [];
    private static System.Windows.Forms.Timer? timer;
    public static ColorTheme? Current { get; private set; }
    public static event EventHandler? ThemeChanged;
    public static void Install()
    {
        if (timer != null) return;
        try { if (File.Exists(ThemeFiles.ActiveThemePath)) Current = ThemeFiles.Load(ThemeFiles.ActiveThemePath); }
        catch (Exception error) { ModularAudience.Audio.LogManager.LogError($"Unable to load color theme: {error.Message}"); }
        Application.Idle += OnIdle;
        timer = new() { Interval = 200 };
        timer.Tick += OnIdle;
        timer.Start();
        DiscoverForms();
    }
    private static void OnIdle(object? sender, EventArgs e) => DiscoverForms();
    public static void DiscoverForms()
    {
        Forms.RemoveWhere(f => f.IsDisposed);
        foreach (Form form in Application.OpenForms.Cast<Form>().ToArray())
            if (Forms.Add(form)) Apply(form);
    }
    public static void SetTheme(ColorTheme? theme, bool persist = false)
    {
        // Validate before touching live controls; a malformed import must leave the current theme intact.
        var validated = theme?.Clone();
        if (persist)
        {
            if (validated == null) { if (File.Exists(ThemeFiles.ActiveThemePath)) File.Delete(ThemeFiles.ActiveThemePath); }
            else ThemeFiles.Save(ThemeFiles.ActiveThemePath, validated);
        }
        Current = validated;
        DiscoverForms();
        foreach (var form in Forms.ToArray()) if (!form.IsDisposed) Apply(form);
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }
    public static string ElementPath(Control control)
    {
        var parts = new Stack<string>();
        for (Control? item = control; item != null; item = item.Parent)
            parts.Push(string.IsNullOrEmpty(item.Name) ? $"{item.GetType().Name}[{item.Parent?.Controls.GetChildIndex(item) ?? 0}]" : item.Name);
        return string.Join("/", parts);
    }
    public static bool Matches(string pattern, string value) => Regex.IsMatch(value,
        "\\A" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "\\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static bool TypeMatches(string pattern, Type? type)
    {
        for (; type != null; type = type.BaseType)
            if (Matches(pattern, type.Name) || Matches(pattern, type.FullName ?? type.Name)) return true;
        return false;
    }
    public static Dictionary<string, Color> Resolve(object element, Form? form, string path)
    {
        var colors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
        if (Current == null) return colors;
        var p = Current.Palette;
        bool input = element is TextBoxBase or ListControl or ListView or TreeView or NumericUpDown or DomainUpDown or DataGridView or PropertyGrid;
        void Set(string property, string? value) { if (!string.IsNullOrWhiteSpace(value)) colors[property] = ThemeFiles.ParseColor(value); }
        Set("BackColor", input ? p.InputBackground ?? p.Background : p.Background);
        Set("ForeColor", input ? p.InputForeground ?? p.Foreground : p.Foreground);
        Set("SelectionBackColor", p.SelectionBackground);
        Set("SelectionForeColor", p.SelectionForeground);
        Set("BorderColor", p.Border);
        Set("DisabledForeColor", p.DisabledForeground);
        Set("AccentColor", p.Accent);
        if (element is PropertyGrid)
        {
            Set("ViewBackColor", p.InputBackground ?? p.Background); Set("ViewForeColor", p.InputForeground ?? p.Foreground);
            Set("HelpBackColor", p.Background); Set("HelpForeColor", p.Foreground);
            Set("CategoryForeColor", p.Foreground); Set("CategorySplitterColor", p.Border);
            Set("LineColor", p.Border); Set("SelectedItemWithFocusBackColor", p.SelectionBackground);
            Set("SelectedItemWithFocusForeColor", p.SelectionForeground);
        }
        foreach (var rule in Current.Rules)
            if (rule.Enabled && TypeMatches(rule.FormType, form?.GetType() ?? typeof(Form))
                && TypeMatches(rule.ControlType, element.GetType()) && Matches(rule.ElementPath, path))
                foreach (var (key, value) in rule.Colors) colors[key] = ThemeFiles.ParseColor(value);
        return colors;
    }
    public static void Apply(Control control)
    {
        if (control.IsDisposed) return;
        CaptureTree(control);
        ApplyTree(control);
    }
    private static void CaptureTree(Control control)
    {
        Capture(control);
        foreach (Control child in control.Controls) CaptureTree(child);
    }
    private static Original Capture(object element)
    {
        var original = Originals.GetOrCreateValue(element);
        foreach (PropertyDescriptor property in TypeDescriptor.GetProperties(element))
            if (property.PropertyType == typeof(Color) && !property.IsReadOnly && !original.Colors.ContainsKey(property.Name))
                try
                {
                    // Store Color.Empty for inherited colors, rather than baking in the parent's
                    // themed color. Restoring should resume normal WinForms color inheritance.
                    original.Colors[property.Name] = property.Name is "BackColor" or "ForeColor" && !property.ShouldSerializeValue(element)
                        ? Color.Empty : (Color)property.GetValue(element)!;
                }
                catch { }
        if (element is ButtonBase button) original.VisualStyle ??= button.UseVisualStyleBackColor;
        return original;
    }
    private static void ApplyTree(Control control)
    {
        if (!Watched.TryGetValue(control, out _))
        {
            Watched.Add(control, new());
            control.ControlAdded += (_, e) => { if (e.Control != null) Apply(e.Control); };
            control.ContextMenuStripChanged += (_, _) => Apply(control);
            control.HandleCreated += (_, _) => Apply(control);
            if (control is ListBox list && control is not CheckedListBox)
                list.DrawItem += DrawListItem;
            if (control is ComboBox combo) combo.DrawItem += DrawComboItem;
            if (control is TabControl tabs) tabs.DrawItem += DrawTab;
        }
        Form? form = control as Form ?? control.FindForm();
        string path = ElementPath(control);
        ApplyObject(control, form, path);
        if (control.ContextMenuStrip is { } menu) ApplyStrip(menu, form, path + "/@contextMenu");
        if (control is ToolStrip strip) ApplyStrip(strip, form, path);
        if (control is DataGridView grid) ApplyGrid(grid, Resolve(control, form, path));
        ApplyDrawingMode(control);
        foreach (Control child in control.Controls) ApplyTree(child);
        control.Invalidate();
    }
    public static Color GetColor(Control control, string property, Color fallback) =>
        Resolve(control, control as Form ?? control.FindForm(), ElementPath(control)).GetValueOrDefault(property, fallback);
    public static void DrawItemBackground(Control control, DrawItemEventArgs e)
    {
        if (Current == null) { e.DrawBackground(); return; }
        bool selected = (e.State & DrawItemState.Selected) != 0;
        using var brush = new SolidBrush(GetColor(control, selected ? "SelectionBackColor" : "BackColor", selected ? SystemColors.Highlight : control.BackColor));
        e.Graphics.FillRectangle(brush, e.Bounds);
    }
    public static Color ItemTextColor(Control control, DrawItemEventArgs e) =>
        (e.State & DrawItemState.Selected) != 0 ? GetColor(control, "SelectionForeColor", SystemColors.HighlightText) : control.ForeColor;
    private static void ApplyObject(object element, Form? form, string path)
    {
        var original = Capture(element);
        var properties = TypeDescriptor.GetProperties(element);
        foreach (var (key, value) in original.Colors)
            try { properties[key]?.SetValue(element, value); } catch (ArgumentException) { }
        foreach (var (key, value) in Resolve(element, form, path))
            if (properties[key] is { IsReadOnly: false, PropertyType: var type } property && type == typeof(Color))
                try { property.SetValue(element, value); } catch (ArgumentException) { }
        if (element is ButtonBase button)
            button.UseVisualStyleBackColor = Current == null ? original.VisualStyle!.Value : false;
    }
    private static void ApplyDrawingMode(Control control)
    {
        var original = Originals.GetOrCreateValue(control);
        if (control is ListBox list && list is not CheckedListBox)
        {
            original.DrawMode ??= list.DrawMode;
            if (original.DrawMode == DrawMode.Normal) list.DrawMode = Current == null ? DrawMode.Normal : DrawMode.OwnerDrawFixed;
        }
        if (control is ComboBox combo)
        {
            original.DrawMode ??= combo.DrawMode;
            if (original.DrawMode == DrawMode.Normal) combo.DrawMode = Current == null ? DrawMode.Normal : DrawMode.OwnerDrawFixed;
        }
        if (control is TabControl tabs)
        {
            original.DrawMode ??= tabs.DrawMode == TabDrawMode.Normal ? DrawMode.Normal : DrawMode.OwnerDrawFixed;
            if (original.DrawMode == DrawMode.Normal) tabs.DrawMode = Current == null ? TabDrawMode.Normal : TabDrawMode.OwnerDrawFixed;
        }
    }
    private static void DrawListItem(object? sender, DrawItemEventArgs e)
    {
        if (sender is not ListBox list || Current == null || Originals.GetOrCreateValue(list).DrawMode != DrawMode.Normal) return;
        DrawItemBackground(list, e);
        if (e.Index >= 0 && e.Index < list.Items.Count)
            TextRenderer.DrawText(e.Graphics, list.GetItemText(list.Items[e.Index]), e.Font ?? list.Font, e.Bounds,
                ItemTextColor(list, e), TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        e.DrawFocusRectangle();
    }
    private static void DrawComboItem(object? sender, DrawItemEventArgs e)
    {
        if (sender is not ComboBox combo || Current == null || Originals.GetOrCreateValue(combo).DrawMode != DrawMode.Normal) return;
        DrawItemBackground(combo, e);
        string text = e.Index >= 0 && e.Index < combo.Items.Count ? combo.GetItemText(combo.Items[e.Index]) ?? "" : combo.Text;
        TextRenderer.DrawText(e.Graphics, text, e.Font ?? combo.Font, e.Bounds, ItemTextColor(combo, e),
            TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        e.DrawFocusRectangle();
    }
    private static void DrawTab(object? sender, DrawItemEventArgs e)
    {
        if (sender is not TabControl tabs || Current == null || e.Index < 0 || e.Index >= tabs.TabCount
            || Originals.GetOrCreateValue(tabs).DrawMode != DrawMode.Normal) return;
        bool selected = tabs.SelectedIndex == e.Index;
        Color back = GetColor(tabs, selected ? "SelectionBackColor" : "BackColor", tabs.BackColor);
        Color fore = GetColor(tabs, selected ? "SelectionForeColor" : "ForeColor", tabs.ForeColor);
        using var brush = new SolidBrush(back); e.Graphics.FillRectangle(brush, e.Bounds);
        TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, tabs.Font, e.Bounds, fore,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
    private static void ApplyStrip(ToolStrip strip, Form? form, string path)
    {
        ApplyObject(strip, form, path);
        var original = Originals.GetOrCreateValue(strip);
        original.Renderer ??= strip.Renderer;
        strip.Renderer = Current == null ? original.Renderer : new ThemeRenderer(Current.Palette);
        void Visit(ToolStripItem item, string parent, int index)
        {
            string itemPath = parent + "/" + (string.IsNullOrEmpty(item.Name) ? $"{item.GetType().Name}[{index}]" : item.Name);
            ApplyObject(item, form, itemPath);
            if (item is ToolStripControlHost host) Apply(host.Control);
            if (item is ToolStripDropDownItem drop)
            {
                ApplyObject(drop.DropDown, form, itemPath + "/@dropdown");
                drop.DropDown.Renderer = strip.Renderer;
                for (int i = 0; i < drop.DropDownItems.Count; i++) Visit(drop.DropDownItems[i], itemPath, i);
            }
        }
        for (int i = 0; i < strip.Items.Count; i++) Visit(strip.Items[i], path, i);
        if (!WatchedStrips.TryGetValue(strip, out _))
        {
            WatchedStrips.Add(strip, new());
            if (strip is ToolStripDropDown dropdown) dropdown.Opening += MenuOpening;
            strip.ItemAdded += (_, _) => ApplyStrip(strip, form, path);
        }
    }
    private static void MenuOpening(object? sender, CancelEventArgs e)
    {
        if (sender is ContextMenuStrip menu)
        {
            var source = menu.SourceControl;
            ApplyStrip(menu, source?.FindForm(), source == null ? menu.Name : ElementPath(source) + "/@contextMenu");
        }
    }
    private static void ApplyGrid(DataGridView grid, Dictionary<string, Color> colors)
    {
        var original = Originals.GetOrCreateValue(grid);
        if (original.CellStyle == null)
        {
            original.CellStyle = grid.DefaultCellStyle.Clone(); original.HeaderStyle = grid.ColumnHeadersDefaultCellStyle.Clone();
            original.RowHeaderStyle = grid.RowHeadersDefaultCellStyle.Clone(); original.HeaderVisualStyles = grid.EnableHeadersVisualStyles;
        }
        grid.DefaultCellStyle = original.CellStyle.Clone();
        grid.ColumnHeadersDefaultCellStyle = original.HeaderStyle!.Clone();
        grid.RowHeadersDefaultCellStyle = original.RowHeaderStyle!.Clone();
        grid.EnableHeadersVisualStyles = Current == null && original.HeaderVisualStyles;
        if (Current == null) return;
        foreach (var style in new[] { grid.DefaultCellStyle, grid.ColumnHeadersDefaultCellStyle, grid.RowHeadersDefaultCellStyle })
        {
            if (colors.TryGetValue("BackColor", out var back)) style.BackColor = back;
            if (colors.TryGetValue("ForeColor", out var fore)) style.ForeColor = fore;
            if (colors.TryGetValue("SelectionBackColor", out var selection)) style.SelectionBackColor = selection;
            if (colors.TryGetValue("SelectionForeColor", out var text)) style.SelectionForeColor = text;
        }
    }
    private sealed class ThemeRenderer(ThemeColors palette) : ToolStripProfessionalRenderer(new ThemeTable(palette))
    {
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = !e.Item.Enabled && palette.DisabledForeground != null ? ThemeFiles.ParseColor(palette.DisabledForeground)
                : e.Item.Selected && palette.SelectionForeground != null ? ThemeFiles.ParseColor(palette.SelectionForeground) : e.Item.ForeColor;
            base.OnRenderItemText(e);
        }
    }
    private sealed class ThemeTable(ThemeColors p) : ProfessionalColorTable
    {
        private Color Get(string? value, Color fallback) => value == null ? fallback : ThemeFiles.ParseColor(value);
        public override Color ToolStripDropDownBackground => Get(p.Background, base.ToolStripDropDownBackground);
        public override Color MenuItemSelected => Get(p.SelectionBackground, base.MenuItemSelected);
        public override Color MenuItemBorder => Get(p.Border, base.MenuItemBorder);
        public override Color MenuBorder => Get(p.Border, base.MenuBorder);
        public override Color ImageMarginGradientBegin => ToolStripDropDownBackground;
        public override Color ImageMarginGradientMiddle => ToolStripDropDownBackground;
        public override Color ImageMarginGradientEnd => ToolStripDropDownBackground;
        public override Color ToolStripGradientBegin => ToolStripDropDownBackground;
        public override Color ToolStripGradientMiddle => ToolStripDropDownBackground;
        public override Color ToolStripGradientEnd => ToolStripDropDownBackground;
        public override Color MenuItemSelectedGradientBegin => MenuItemSelected;
        public override Color MenuItemSelectedGradientEnd => MenuItemSelected;
        public override Color MenuItemPressedGradientBegin => MenuItemSelected;
        public override Color MenuItemPressedGradientMiddle => MenuItemSelected;
        public override Color MenuItemPressedGradientEnd => MenuItemSelected;
    }
}
