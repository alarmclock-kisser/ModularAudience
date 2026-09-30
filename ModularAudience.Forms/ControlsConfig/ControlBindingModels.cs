using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ModularAudience.Forms.ControlsConfig
{
    public enum InputType
    {
        None,
        MouseLeft,
        MouseRight,
        MouseMiddle,
        MouseWheel,
        Key,
        KeyCombo
    }

    public enum ModifierKey
    {
        None = 0,
        Shift = 1,
        Control = 2,
        Alt = 4,
        Win = 8
    }

    public enum MouseAction
    {
        Click,
        DoubleClick,
        Drag,
        Press,
        Release,
        WheelUp,
        WheelDown
    }

    public enum ActionCategory
    {
        NoteEditing,
        Selection,
        Navigation,
        Playback,
        View,
        TrackManagement,
        Clipboard,
        LoopControl,
        Settings
    }

    [System.Flags]
    public enum BindingScope
    {
        Grid = 1,
        Note = 2,
        TrackHeader = 4,
        BarHeader = 8,
        Global = 16,
        PastePreview = 32
    }

    public sealed class ControlBinding
    {
        public string ActionId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Description { get; set; } = "";
        public ActionCategory Category { get; set; }
        public BindingScope ValidScopes { get; set; } = BindingScope.Global;

        public InputType PrimaryInput { get; set; } = InputType.None;
        public Keys PrimaryKey { get; set; } = Keys.None;
        public ModifierKey PrimaryModifiers { get; set; } = ModifierKey.None;
        public MouseAction PrimaryMouseAction { get; set; } = MouseAction.Click;

        public InputType SecondaryInput { get; set; } = InputType.None;
        public Keys SecondaryKey { get; set; } = Keys.None;
        public ModifierKey SecondaryModifiers { get; set; } = ModifierKey.None;
        public MouseAction SecondaryMouseAction { get; set; } = MouseAction.Click;

        public bool IsHardcoded { get; set; } = false;
        public bool AllowRemapping { get; set; } = true;
        public int SortOrder { get; set; } = 0;

        public string GetBindingString()
        {
            return FormatBinding(PrimaryInput, PrimaryKey, PrimaryModifiers, PrimaryMouseAction);
        }

        public string GetSecondaryBindingString()
        {
            if (SecondaryInput == InputType.None) return "—";
            return FormatBinding(SecondaryInput, SecondaryKey, SecondaryModifiers, SecondaryMouseAction);
        }

        private static string FormatBinding(InputType input, Keys key, ModifierKey mods, MouseAction mouseAction)
        {
            var parts = new List<string>();

            if (mods != ModifierKey.None)
            {
                if (mods.HasFlag(ModifierKey.Control)) parts.Add("Ctrl");
                if (mods.HasFlag(ModifierKey.Shift)) parts.Add("Shift");
                if (mods.HasFlag(ModifierKey.Alt)) parts.Add("Alt");
                if (mods.HasFlag(ModifierKey.Win)) parts.Add("Win");
            }

            switch (input)
            {
                case InputType.Key:
                case InputType.KeyCombo:
                    if (key != Keys.None)
                        parts.Add(key.ToString());
                    break;
                case InputType.MouseLeft:
                    parts.Add(mouseAction == MouseAction.DoubleClick ? "LMB DblClick" : mouseAction == MouseAction.Drag ? "LMB Drag" : "LMB");
                    break;
                case InputType.MouseRight:
                    parts.Add(mouseAction == MouseAction.Drag ? "RMB Drag" : "RMB");
                    break;
                case InputType.MouseMiddle:
                    parts.Add("MMB");
                    break;
                case InputType.MouseWheel:
                    parts.Add(mouseAction == MouseAction.WheelUp ? "Wheel ↑" : "Wheel ↓");
                    break;
            }

            return parts.Count > 0 ? string.Join(" + ", parts) : "—";
        }

        public bool ConflictsWith(ControlBinding other)
        {
            if (other == this || other.ActionId == this.ActionId) return false;
            if (!this.ValidScopes.HasFlag(other.ValidScopes) && !other.ValidScopes.HasFlag(this.ValidScopes)) return false;

            return BindingsEqual(this.PrimaryInput, this.PrimaryKey, this.PrimaryModifiers, this.PrimaryMouseAction,
                               other.PrimaryInput, other.PrimaryKey, other.PrimaryModifiers, other.PrimaryMouseAction) ||
                   BindingsEqual(this.PrimaryInput, this.PrimaryKey, this.PrimaryModifiers, this.PrimaryMouseAction,
                               other.SecondaryInput, other.SecondaryKey, other.SecondaryModifiers, other.SecondaryMouseAction) ||
                   BindingsEqual(this.SecondaryInput, this.SecondaryKey, this.SecondaryModifiers, this.SecondaryMouseAction,
                               other.PrimaryInput, other.PrimaryKey, other.PrimaryModifiers, other.PrimaryMouseAction) ||
                   BindingsEqual(this.SecondaryInput, this.SecondaryKey, this.SecondaryModifiers, this.SecondaryMouseAction,
                               other.SecondaryInput, other.SecondaryKey, other.SecondaryModifiers, other.SecondaryMouseAction);
        }

        private static bool BindingsEqual(InputType aInput, Keys aKey, ModifierKey aMods, MouseAction aMouse,
                                          InputType bInput, Keys bKey, ModifierKey bMods, MouseAction bMouse)
        {
            return aInput == bInput && aKey == bKey && aMods == bMods && aMouse == bMouse;
        }

        public ControlBinding Clone()
        {
            return (ControlBinding)this.MemberwiseClone();
        }
    }

    public sealed class ControlScheme
    {
        public string Name { get; set; } = "Default";
        public List<ControlBinding> Bindings { get; set; } = new();
        public Dictionary<string, ControlBinding> BindingsById { get; } = new();

        public void RebuildIndex()
        {
            BindingsById.Clear();
            foreach (var b in Bindings)
                BindingsById[b.ActionId] = b;
        }

        public ControlBinding? GetBinding(string actionId)
        {
            BindingsById.TryGetValue(actionId, out var b);
            return b;
        }

        public List<ControlBinding> GetConflicts(ControlBinding binding)
        {
            var conflicts = new List<ControlBinding>();
            foreach (var other in Bindings)
            {
                if (binding.ConflictsWith(other))
                    conflicts.Add(other);
            }
            return conflicts;
        }

        public List<ControlBinding> GetAllConflicts()
        {
            var allConflicts = new HashSet<ControlBinding>();
            for (int i = 0; i < Bindings.Count; i++)
            {
                for (int j = i + 1; j < Bindings.Count; j++)
                {
                    if (Bindings[i].ConflictsWith(Bindings[j]))
                    {
                        allConflicts.Add(Bindings[i]);
                        allConflicts.Add(Bindings[j]);
                    }
                }
            }
            return allConflicts.ToList();
        }
    }
}