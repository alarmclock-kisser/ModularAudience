using System.Runtime.CompilerServices;

namespace ModularAudience.Forms.Hints;

/// <summary>UI-thread policy for optional interaction hints; parameter explanations remain available.</summary>
public static class HintPolicy
{
    private sealed class State
    {
        public bool? Original;
        public bool ParameterHelp;
        public readonly List<Action> Dismiss = [];
    }
    private static readonly ConditionalWeakTable<Control, State> states = new();
    private static readonly List<WeakReference<Control>> controls = [];
    private static readonly List<WeakReference<AppToolTip>> tips = [];
    private static bool installed;
    public static bool Enabled { get; private set; } = true;

    public static void Install()
    {
        if (installed) return;
        installed = true;
        Application.Idle += (_, _) => DiscoverForms();
    }

    public static void SetEnabled(bool enabled)
    {
        Enabled = enabled;
        DiscoverForms();
        tips.RemoveAll(reference => !reference.TryGetTarget(out _));
        foreach (var reference in tips.ToArray())
            if (reference.TryGetTarget(out var tip)) tip.ApplyHintPolicy();
        controls.RemoveAll(reference => !reference.TryGetTarget(out var control) || control.IsDisposed);
        foreach (var reference in controls.ToArray())
            if (reference.TryGetTarget(out var control)) Apply(control);
    }

    internal static void Register(AppToolTip tip)
    {
        Install();
        tips.Add(new(tip));
        tip.ApplyHintPolicy();
    }

    public static void DiscoverForms()
    {
        foreach (Form form in Application.OpenForms.Cast<Form>().ToArray()) Watch(form);
    }

    public static void RegisterPopup(Control owner, Action dismiss)
    {
        Watch(owner);
        states.GetOrCreateValue(owner).Dismiss.Add(dismiss);
        if (!Enabled) dismiss();
    }

    public static void Watch(Control root)
    {
        if (root.IsDisposed) return;
        if (!states.TryGetValue(root, out var state))
        {
            state = new();
            states.Add(root, state);
            controls.Add(new(root));
            root.ControlAdded += (_, e) => { if (e.Control is not null) Watch(e.Control); };
            root.ContextMenuStripChanged += (_, _) => { if (root.ContextMenuStrip is { } menu) Watch(menu); };
            root.HandleCreated += (_, _) => Apply(root);
            if (root is ToolStrip strip)
            {
                state.Original = strip.ShowItemToolTips;
                strip.ItemAdded += (_, e) => { if (e.Item is { } item) WatchItem(strip, item); };
                foreach (ToolStripItem item in strip.Items) WatchItem(strip, item);
            }
            else state.Original = root switch
            {
                ListView list => list.ShowItemToolTips, TreeView tree => tree.ShowNodeToolTips,
                TabControl tabs => tabs.ShowToolTips, DataGridView grid => grid.ShowCellToolTips, _ => null
            };
            Apply(root);
        }
        if (root.ContextMenuStrip is { } context) Watch(context);
        foreach (Control child in root.Controls) Watch(child);
    }

    private static void WatchItem(ToolStrip owner, ToolStripItem item)
    {
        if (item is ToolStripDropDownItem dropdown)
            dropdown.DropDownOpening += (_, _) => RefreshStrip(dropdown.DropDown,
                states.GetOrCreateValue(owner).ParameterHelp || IsParameterContext(owner));
        if (item is ToolStripControlHost host) Watch(host.Control);
    }

    internal static void RefreshStrip(ToolStrip strip, bool parameterHelp)
    {
        Watch(strip);
        states.GetOrCreateValue(strip).ParameterHelp = parameterHelp;
        Apply(strip);
    }

    private static void Apply(Control control)
    {
        if (control.IsDisposed) return;
        if (control.InvokeRequired)
        {
            try { control.BeginInvoke((Action)(() => Apply(control))); } catch (InvalidOperationException) { }
            return;
        }
        State state = states.GetOrCreateValue(control);
        bool allowed = Enabled || state.ParameterHelp || IsParameterContext(control);
        if (state.Original is { } original)
            switch (control)
            {
                case ToolStrip strip: strip.ShowItemToolTips = original && allowed; break;
                case ListView list: list.ShowItemToolTips = original && allowed; break;
                case TreeView tree: tree.ShowNodeToolTips = original && allowed; break;
                case TabControl tabs: tabs.ShowToolTips = original && allowed; break;
                case DataGridView grid: grid.ShowCellToolTips = original && allowed; break;
            }
        if (!Enabled) foreach (Action dismiss in state.Dismiss.ToArray()) dismiss();
    }

    internal static bool IsParameterContext(Control? control)
    {
        if (control is System.Windows.Forms.ContextMenuStrip menu)
        {
            if (menu is AppContextMenuStrip { IsParameterHelp: true }) return true;
            control = menu.SourceControl;
        }
        string? name = (control as Form ?? control?.FindForm())?.GetType().Name;
        return name is not null && (name.EndsWith("Settings", StringComparison.Ordinal)
            || name.EndsWith("SettingsDialog", StringComparison.Ordinal) || name.EndsWith("OptionsDialog", StringComparison.Ordinal)
            || name is "InstrumentSampleSynthDialog" or "WaveformModeller" or "SampleWaveformModeller"
                or "AutoTuneDialog" or "DeterministicSeparationDialog" or "HotkeyConfigWindow" or "ControlsConfigDialog"
                or "BreakbeatPatternGenerateDialog" or "BreakbeatPatternRemixDialog" or "ThemeEditor" or "MidiControllerDialog");
    }
}
