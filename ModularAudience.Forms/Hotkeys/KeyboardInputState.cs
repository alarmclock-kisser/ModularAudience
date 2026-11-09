using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace ModularAudience.Forms.Hotkeys
{
    public readonly record struct KeyboardKeyEvent(
        Keys Key,
        bool IsDown,
        string HardwareId,
        HotkeyModifiers Modifiers,
        HotkeyModifiers AllKeyboardModifiers,
        bool IsRepeat = false)
    {
        public Keys PhysicalKey { get; init; }
    }

    /// <summary>Raw key state, kept separately for every physical keyboard.</summary>
    internal sealed class KeyboardInputState
    {
        private readonly Dictionary<string, HashSet<Keys>> pressedKeys = new(StringComparer.Ordinal);

        internal KeyboardKeyEvent Update(Keys key, bool isDown, string hardwareId)
        {
            key &= Keys.KeyCode;
            hardwareId = HotkeyKeyboardIds.Normalize(hardwareId);
            if (!this.pressedKeys.TryGetValue(hardwareId, out HashSet<Keys>? keys))
            {
                keys = [];
                this.pressedKeys[hardwareId] = keys;
            }

            // Keep left/right modifiers distinct until after updating the state. Releasing
            // left Shift must not release right Shift on the same keyboard as well.
            bool repeat = isDown && !keys.Add(key);
            if (!isDown)
            {
                keys.Remove(key);
            }

            HotkeyModifiers modifiers = GetModifiers(keys);
            if (keys.Count == 0)
            {
                this.pressedKeys.Remove(hardwareId);
            }

            HotkeyModifiers allModifiers = this.pressedKeys.Values
                .Aggregate(HotkeyModifiers.None, (current, pressed) => current | GetModifiers(pressed));
            return new KeyboardKeyEvent(
                HotkeyFormatter.NormalizeKey(key), isDown, hardwareId, modifiers, allModifiers, repeat)
            {
                PhysicalKey = key
            };
        }

        internal void Clear() => this.pressedKeys.Clear();

        private static HotkeyModifiers GetModifiers(IEnumerable<Keys> keys) => keys
            .Aggregate(HotkeyModifiers.None, (current, key) => current | HotkeyFormatter.GetInherentModifier(key));
    }

    internal enum SyncerHotkeyChange
    {
        None,
        PausingPressed,
        PausingReleased,
        NudgingPressed
    }

    internal sealed class SyncerHotkeyState
    {
        private readonly HashSet<(Keys Key, string HardwareId)> pausingPresses = [];

        internal SyncerHotkeyChange Update(
            KeyboardKeyEvent keyEvent,
            HotkeyCombo pausing,
            HotkeyCombo nudging,
            bool allowKeyDown,
            bool allowPausing = true)
        {
            Keys physicalKey = keyEvent.PhysicalKey != Keys.None ? keyEvent.PhysicalKey : keyEvent.Key;
            var press = (physicalKey & Keys.KeyCode, HotkeyKeyboardIds.Normalize(keyEvent.HardwareId));
            if (!keyEvent.IsDown)
            {
                // Release the physical key that started the hold, even if its modifiers,
                // the application focus, or the configured hotkey have since changed.
                return this.pausingPresses.Remove(press) && this.pausingPresses.Count == 0
                    ? SyncerHotkeyChange.PausingReleased
                    : SyncerHotkeyChange.None;
            }

            if (!allowKeyDown || keyEvent.IsRepeat)
            {
                return SyncerHotkeyChange.None;
            }

            if (nudging.Matches(keyEvent))
            {
                return SyncerHotkeyChange.NudgingPressed;
            }

            if (allowPausing && pausing.Matches(keyEvent)
                && this.pausingPresses.Add(press) && this.pausingPresses.Count == 1)
            {
                return SyncerHotkeyChange.PausingPressed;
            }

            return SyncerHotkeyChange.None;
        }

        internal void ClearPausing() => this.pausingPresses.Clear();
    }
}
