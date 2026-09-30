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
        PastePreview = 32,

        // A note can be grabbed in different places, which distinguishes gestures that would
        // otherwise look identical (e.g. "LMB Drag" in the middle moves a note, on the left or
        // right third it resizes it).
        NoteBody = 64,
        NoteEdge = 128
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

        /// <summary>
        /// False while the editor still handles this action with hard-coded input logic.
        /// The dialog greys those out so the user is never offered a binding that has no effect.
        /// </summary>
        public bool Implemented { get; set; } = true;

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
            if (input == InputType.None)
            {
                return "—";
            }

            var parts = new List<string>();

            if (mods != ModifierKey.None)
            {
                if (mods.HasFlag(ModifierKey.Control)) parts.Add("Ctrl");
                if (mods.HasFlag(ModifierKey.Shift)) parts.Add("Shift");
                if (mods.HasFlag(ModifierKey.Alt)) parts.Add("Alt");
                if (mods.HasFlag(ModifierKey.Win)) parts.Add("Win");
            }

            string gesture = input switch
            {
                InputType.Key or InputType.KeyCombo => key == Keys.None ? "—" : key.ToString(),
                InputType.MouseLeft => FormatMouseGesture("LMB", mouseAction),
                InputType.MouseRight => FormatMouseGesture("RMB", mouseAction),
                InputType.MouseMiddle => FormatMouseGesture("MMB", mouseAction),
                InputType.MouseWheel => mouseAction == MouseAction.WheelDown ? "Wheel ↓" : "Wheel ↑",
                _ => "—"
            };

            if (gesture == "—")
            {
                return "—";
            }

            parts.Add(gesture);
            return string.Join(" + ", parts);
        }

        private static string FormatMouseGesture(string button, MouseAction action) => action switch
        {
            MouseAction.Click => button,
            MouseAction.DoubleClick => button + " DblClick",
            MouseAction.Drag => button + " Drag",
            MouseAction.Press => button + " Press",
            MouseAction.Release => button + " Release",
            MouseAction.WheelUp => "Wheel ↑",
            MouseAction.WheelDown => "Wheel ↓",
            _ => button
        };

        /// <summary>
        /// True when both bindings can be active in the same place. NoteBody and NoteEdge are
        /// mutually exclusive refinements of BindingScope.Note, so "LMB Drag" in the middle of a
        /// note (NoteBody) and "LMB Drag" on its edge (NoteEdge) are not a conflict.
        /// </summary>
        public static bool ScopesOverlap(BindingScope a, BindingScope b)
        {
            const BindingScope regions = BindingScope.NoteBody | BindingScope.NoteEdge;

            BindingScope aRegions = a & regions;
            BindingScope bRegions = b & regions;
            if (aRegions != 0 && bRegions != 0 && (aRegions & bRegions) == 0)
            {
                return false;
            }

            return ((a & ~regions) & (b & ~regions)) != 0;
        }

        public bool ConflictsWith(ControlBinding other)
        {
            if (other is null || ReferenceEquals(other, this) || other.ActionId == this.ActionId)
            {
                return false;
            }

            if (!ScopesOverlap(this.ValidScopes, other.ValidScopes))
            {
                return false;
            }

            return GesturesEqual(this.PrimaryInput, this.PrimaryKey, this.PrimaryModifiers, this.PrimaryMouseAction,
                                 other.PrimaryInput, other.PrimaryKey, other.PrimaryModifiers, other.PrimaryMouseAction)
                || GesturesEqual(this.PrimaryInput, this.PrimaryKey, this.PrimaryModifiers, this.PrimaryMouseAction,
                                 other.SecondaryInput, other.SecondaryKey, other.SecondaryModifiers, other.SecondaryMouseAction)
                || GesturesEqual(this.SecondaryInput, this.SecondaryKey, this.SecondaryModifiers, this.SecondaryMouseAction,
                                 other.PrimaryInput, other.PrimaryKey, other.PrimaryModifiers, other.PrimaryMouseAction)
                || GesturesEqual(this.SecondaryInput, this.SecondaryKey, this.SecondaryModifiers, this.SecondaryMouseAction,
                                 other.SecondaryInput, other.SecondaryKey, other.SecondaryModifiers, other.SecondaryMouseAction);
        }

        /// <summary>
        /// Two gestures clash only when both are actually assigned and identical.
        /// Unassigned (InputType.None) slots never clash with anything.
        /// </summary>
        private static bool GesturesEqual(InputType aInput, Keys aKey, ModifierKey aMods, MouseAction aMouse,
                                          InputType bInput, Keys bKey, ModifierKey bMods, MouseAction bMouse)
        {
            if (aInput == InputType.None || bInput == InputType.None)
            {
                return false;
            }

            if (aInput != bInput || aMods != bMods)
            {
                return false;
            }

            // Wheel direction is part of the gesture; for keys the key code is.
            if (aInput == InputType.MouseWheel)
            {
                return NormalizeWheel(aMouse) == NormalizeWheel(bMouse);
            }

            if (aInput is InputType.Key or InputType.KeyCombo)
            {
                return aKey == bKey;
            }

            // For mouse buttons the action must match too (click vs. drag are different gestures).
            return NormalizeMouseAction(aMouse) == NormalizeMouseAction(bMouse);
        }

        private static MouseAction NormalizeWheel(MouseAction action)
            => action == MouseAction.WheelDown ? MouseAction.WheelDown : MouseAction.WheelUp;

        private static MouseAction NormalizeMouseAction(MouseAction action)
            => action is MouseAction.WheelUp or MouseAction.WheelDown ? MouseAction.Click : action;

        /// <summary>Clears both slots of this binding.</summary>
        public void ClearBindings()
        {
            PrimaryInput = InputType.None;
            PrimaryKey = Keys.None;
            PrimaryModifiers = ModifierKey.None;
            PrimaryMouseAction = MouseAction.Click;
            SecondaryInput = InputType.None;
            SecondaryKey = Keys.None;
            SecondaryModifiers = ModifierKey.None;
            SecondaryMouseAction = MouseAction.Click;
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

        // Derived lookup - must never be serialized (it would duplicate/overwrite Bindings).
        [System.Text.Json.Serialization.JsonIgnore]
        public Dictionary<string, ControlBinding> BindingsById { get; } = new();

        public void RebuildIndex()
        {
            BindingsById.Clear();
            foreach (var b in Bindings)
            {
                BindingsById[b.ActionId] = b;
            }
        }

        public ControlScheme Clone()
        {
            ControlScheme clone = new() { Name = Name };
            foreach (ControlBinding b in Bindings)
            {
                clone.Bindings.Add(b.Clone());
            }
            clone.RebuildIndex();
            return clone;
        }

        public ControlBinding? GetBinding(string actionId)
        {
            return BindingsById.TryGetValue(actionId, out var b) ? b : null;
        }

        public ControlBinding? Find(string actionId)
        {
            if (BindingsById.Count != Bindings.Count)
            {
                RebuildIndex();
            }
            return GetBinding(actionId);
        }

        public List<ControlBinding> GetConflicts(ControlBinding binding)
        {
            var conflicts = new List<ControlBinding>();
            if (binding == null)
            {
                return conflicts;
            }

            foreach (var other in Bindings)
            {
                if (binding.ConflictsWith(other))
                {
                    conflicts.Add(other);
                }
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