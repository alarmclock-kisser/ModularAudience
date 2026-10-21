using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Windows.Forms;

namespace ModularAudience.Forms.Hotkeys
{
    public static class HotkeyDefaults
    {
        public const int HoldThresholdMs = 250;
        public const int MinHoldThresholdMs = 50;
        public const int MaxHoldThresholdMs = 10000;
        public const int DefaultScrollTickPercent = 10;
        public const int MinScrollTickDistance = 1;
        public const int MaxScrollTickDistance = 100000;

        /// <summary>
        /// Rate units that one wheel notch changes when a varispeed hotkey is used.
        /// The rate scale is logarithmic, so 25 units are roughly three percent.
        /// </summary>
        public const int LoopRateUnitsPerWheelTick = 25;
        public const int MinLoopRateUnitsPerWheelTick = 1;
        public const int MaxLoopRateUnitsPerWheelTick = 1000;
    }

    [Flags]
    public enum HotkeyModifiers
    {
        None = 0,
        Control = 1,
        Shift = 2,
        Alt = 4,
        Win = 8
    }

    public enum HotkeyMouseGesture
    {
        LeftClick,
        LeftDoubleClick,
        RightClick,
        MiddleClick
    }

    public sealed class HotkeyBinding
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string ActionName { get; set; } = string.Empty;
        public Keys Key { get; set; } = Keys.None;
        public HotkeyModifiers Modifiers { get; set; } = HotkeyModifiers.None;
        public string KeyboardHardwareId { get; set; } = string.Empty;
        public string KeyboardDisplayName { get; set; } = string.Empty;
        public int HoldThresholdMs { get; set; } = HotkeyDefaults.HoldThresholdMs;
        public HotkeyActionTarget Target { get; set; } = new();
        public bool ToggleOnRelease { get; set; }
        public bool InvertWhileHeld { get; set; } = true;

        [JsonIgnore]
        public bool IsUniversal => string.IsNullOrWhiteSpace(this.KeyboardHardwareId);

        public string GetHotkeyDisplay()
        {
            return HotkeyFormatter.FormatCombo(this.Key, this.Modifiers);
        }

        public string GetGestureDisplay()
        {
            if (this.Target.ActionKind == HotkeyActionKinds.ContextMenuItem)
            {
                string text = this.Target.MenuItemPath.Count > 0
                    ? this.Target.MenuItemPath[^1].Text
                    : string.Empty;
                return string.IsNullOrWhiteSpace(text)
                    ? "Context menu item"
                    : "Context menu: " + text;
            }

            if (this.Target.ActionKind == HotkeyActionKinds.NumericUpDown)
            {
                return this.Target.NumericUpDownIncrement ? "Increment" : "Decrement";
            }

            if (this.Target.ActionKind == HotkeyActionKinds.ScrollBar)
            {
                return "Mouse wheel";
            }

            if (this.Target.ActionKind == HotkeyActionKinds.LoopTrackRate)
            {
                return this.Target.LoopRateShiftAll
                    ? "Varispeed wheel (all tracks)"
                    : "Varispeed wheel";
            }

            return HotkeyFormatter.FormatGesture(this.Target.Gesture, this.Target.ActionModifiers);
        }

        public string GetKeyboardDisplay()
        {
            if (this.IsUniversal)
            {
                return "All keyboards";
            }

            return string.IsNullOrWhiteSpace(this.KeyboardDisplayName)
                ? HotkeyKeyboardIds.GetShortDisplay(this.KeyboardHardwareId)
                : this.KeyboardDisplayName;
        }
    }

    public sealed class HotkeyKeyboardProfile
    {
        public string HardwareId { get; init; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public bool IsConnected { get; set; }
    }

    public static class HotkeyKeyboardIds
    {
        public static string Normalize(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().ToUpperInvariant();
        }

        public static bool Same(string? left, string? right)
        {
            return string.Equals(Normalize(left), Normalize(right), StringComparison.Ordinal);
        }

        public static string GetShortDisplay(string? hardwareId)
        {
            string normalized = Normalize(hardwareId);
            if (normalized.Length == 0)
            {
                return "All keyboards";
            }

            int separator = normalized.LastIndexOfAny(['\\', '#']);
            string tail = separator >= 0 && separator < normalized.Length - 1
                ? normalized[(separator + 1)..]
                : normalized;
            return tail.Length <= 16 ? tail : tail[..16];
        }
    }

    public sealed class HotkeyActionTarget
    {
        public string ActionKind { get; set; } = HotkeyActionKinds.ControlGesture;
        public string OwnerType { get; set; } = string.Empty;
        public string OwnerTitle { get; set; } = string.Empty;
        public int? TrackViewId { get; set; }
        public bool UseLastFocusedTrackView { get; set; }
        public List<HotkeyControlPathSegment> ControlPath { get; set; } = [];
        public string ContextMenuName { get; set; } = string.Empty;
        public List<HotkeyMenuItemPathSegment> MenuItemPath { get; set; } = [];
        public bool NumericUpDownIncrement { get; set; } = true;
        public HotkeyMouseGesture Gesture { get; set; } = HotkeyMouseGesture.LeftClick;
        public HotkeyModifiers ActionModifiers { get; set; } = HotkeyModifiers.None;
        public float RelativeX { get; set; } = 0.5f;
        public float RelativeY { get; set; } = 0.5f;
        public int ScrollTickDistance { get; set; }
        public int LoopControlTrackIndex { get; set; } = -1;
        public int LoopRateUnitsPerWheelTick { get; set; }

        /// <summary>
        /// Legacy field of older varispeed hotkeys. It is only used as a fallback
        /// as long as the hotkey was not registered again with a track index.
        /// </summary>
        public string LoopControlAudioId { get; set; } = string.Empty;
        public bool LoopRateShiftAll { get; set; }
    }

    public sealed class HotkeyControlPathSegment
    {
        public string TypeName { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Tag { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public int Index { get; set; }
    }

    public sealed class HotkeyMenuItemPathSegment
    {
        public string TypeName { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public int Index { get; set; }
    }

    public sealed class HotkeySettingsFile
    {
        public int Version { get; set; } = 3;
        public List<HotkeyBinding> Bindings { get; set; } = [];
        public HotkeyCombo PausingSyncerHotkey { get; set; } = new() { Key = Keys.ShiftKey, Modifiers = HotkeyModifiers.None };
        public HotkeyCombo NudgingSyncerHotkey { get; set; } = new() { Key = Keys.CapsLock, Modifiers = HotkeyModifiers.None };
    }

    public sealed class HotkeyCombo
    {
        public Keys Key { get; set; } = Keys.None;
        public HotkeyModifiers Modifiers { get; set; } = HotkeyModifiers.None;

        public bool IsEmpty => (this.Key & Keys.KeyCode) == Keys.None;

        public string GetDisplay()
        {
            if (this.IsEmpty)
            {
                return "None";
            }
            return HotkeyFormatter.FormatCombo(this.Key, this.Modifiers);
        }
    }

    public static class HotkeyFormatter
    {
        /// <summary>
        /// Maps the left/right variants of the modifier keys onto a single canonical key,
        /// so that a hotkey stored as "Shift" also matches the physical left/right shift key.
        /// </summary>
        public static Keys NormalizeKey(Keys key)
        {
            key &= Keys.KeyCode;
            if (key is Keys.LShiftKey or Keys.RShiftKey)
            {
                return Keys.ShiftKey;
            }
            if (key is Keys.LControlKey or Keys.RControlKey)
            {
                return Keys.ControlKey;
            }
            if (key is Keys.LMenu or Keys.RMenu)
            {
                return Keys.Menu;
            }
            return key;
        }

        /// <summary>
        /// Returns the modifier that is inherently part of the given key. A hotkey that consists
        /// of a modifier key (e.g. "Shift") must not be compared against the currently pressed
        /// modifiers, because that modifier is necessarily held down while the key is pressed.
        /// </summary>
        public static HotkeyModifiers GetInherentModifier(Keys key)
        {
            key &= Keys.KeyCode;
            if (key is Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey)
            {
                return HotkeyModifiers.Shift;
            }
            if (key is Keys.ControlKey or Keys.LControlKey or Keys.RControlKey)
            {
                return HotkeyModifiers.Control;
            }
            if (key is Keys.Menu or Keys.LMenu or Keys.RMenu)
            {
                return HotkeyModifiers.Alt;
            }
            if (key is Keys.LWin or Keys.RWin)
            {
                return HotkeyModifiers.Win;
            }
            return HotkeyModifiers.None;
        }

        public static string FormatKey(Keys key)
        {
            key = NormalizeKey(key);
            if (key == Keys.None)
            {
                return "-";
            }

            if (key >= Keys.D0 && key <= Keys.D9)
            {
                return ((char)('0' + (key - Keys.D0))).ToString();
            }

            if (key >= Keys.NumPad0 && key <= Keys.NumPad9)
            {
                return "Num " + (key - Keys.NumPad0);
            }

            return key switch
            {
                Keys.ShiftKey => "Shift",
                Keys.ControlKey => "Ctrl",
                Keys.Menu => "Alt",
                Keys.LWin => "Win",
                Keys.RWin => "Win",
                Keys.CapsLock => "Caps",
                Keys.Oemtilde => "`",
                Keys.OemMinus => "-",
                Keys.Oemplus => "+",
                Keys.OemOpenBrackets => "[",
                Keys.OemCloseBrackets => "]",
                Keys.OemPipe => "\\",
                Keys.OemSemicolon => ";",
                Keys.OemQuotes => "'",
                Keys.Oemcomma => ",",
                Keys.OemPeriod => ".",
                Keys.OemQuestion => "/",
                Keys.Space => "Space",
                Keys.Enter => "Enter",
                Keys.Escape => "Esc",
                Keys.Back => "Backspace",
                Keys.Delete => "Delete",
                Keys.Insert => "Insert",
                Keys.Home => "Home",
                Keys.End => "End",
                Keys.PageUp => "Page Up",
                Keys.PageDown => "Page Down",
                Keys.Up => "Up",
                Keys.Down => "Down",
                Keys.Left => "Left",
                Keys.Right => "Right",
                Keys.Tab => "Tab",
                _ => key.ToString()
            };
        }

        public static string FormatCombo(Keys key, HotkeyModifiers modifiers)
        {
            List<string> parts = [];
            AppendModifiers(parts, modifiers);
            parts.Add(FormatKey(key));
            return string.Join(" + ", parts);
        }

        public static string FormatGesture(HotkeyMouseGesture gesture, HotkeyModifiers modifiers)
        {
            List<string> parts = [];
            AppendModifiers(parts, modifiers);
            parts.Add(gesture switch
            {
                HotkeyMouseGesture.LeftDoubleClick => "Double-Click",
                HotkeyMouseGesture.RightClick => "Right-Click",
                HotkeyMouseGesture.MiddleClick => "Middle-Click",
                _ => "Click"
            });
            return string.Join(" + ", parts);
        }

        public static string FormatModifiers(HotkeyModifiers modifiers)
        {
            List<string> parts = [];
            AppendModifiers(parts, modifiers);
            return string.Join(" + ", parts);
        }

        private static void AppendModifiers(List<string> parts, HotkeyModifiers modifiers)
        {
            if (modifiers.HasFlag(HotkeyModifiers.Control))
            {
                parts.Add("Ctrl");
            }
            if (modifiers.HasFlag(HotkeyModifiers.Shift))
            {
                parts.Add("Shift");
            }
            if (modifiers.HasFlag(HotkeyModifiers.Alt))
            {
                parts.Add("Alt");
            }
            if (modifiers.HasFlag(HotkeyModifiers.Win))
            {
                parts.Add("Win");
            }
        }
    }
}
