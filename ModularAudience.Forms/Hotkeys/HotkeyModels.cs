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
        public int Version { get; set; } = 2;
        public List<HotkeyBinding> Bindings { get; set; } = [];
    }

    public static class HotkeyFormatter
    {
        public static string FormatKey(Keys key)
        {
            key &= Keys.KeyCode;
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
