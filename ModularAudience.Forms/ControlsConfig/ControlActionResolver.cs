using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace ModularAudience.Forms.ControlsConfig
{
    /// <summary>
    /// Maps a concrete input event (mouse button + phase, wheel direction, key + modifiers) onto
    /// the action id it is bound to. This is the bridge between the persisted control scheme and
    /// the Breakbeat Pattern Editor input handling.
    ///
    /// Matching is scope-aware: an action is only triggered when the current interaction context
    /// (grid / note / track header / bar header / paste preview) is covered by the binding.
    /// </summary>
    public sealed class ControlActionResolver
    {
        private readonly ControlScheme _scheme;
        private readonly Dictionary<GestureKey, string> _winners = [];

        public ControlActionResolver(ControlScheme scheme)
        {
            _scheme = scheme ?? throw new ArgumentNullException(nameof(scheme));
            _scheme.RebuildIndex();
            RebuildLookup();
        }

        public ControlScheme Scheme => _scheme;

        /// <summary>
        /// Pre-computes, per scope, which action owns each gesture. Later entries in display order
        /// win, which mirrors the conflict resolution applied when the scheme is saved.
        /// </summary>
        private void RebuildLookup()
        {
            _winners.Clear();
            foreach (ControlBinding binding in _scheme.Bindings
                         .OrderBy(b => b.SortOrder)
                         .ThenBy(b => b.ActionId, StringComparer.Ordinal))
            {
                Register(binding.PrimaryInput, binding.PrimaryKey, binding.PrimaryModifiers, binding.PrimaryMouseAction, binding);
                Register(binding.SecondaryInput, binding.SecondaryKey, binding.SecondaryModifiers, binding.SecondaryMouseAction, binding);
            }
        }

        private void Register(InputType input, Keys key, ModifierKey mods, MouseAction action, ControlBinding binding)
        {
            if (input == InputType.None)
            {
                return;
            }

            foreach (BindingScope scope in EnumerateScopes(binding.ValidScopes))
            {
                _winners[GestureKey(scope, input, key, mods, action)] = binding.ActionId;
            }
        }

        private static IEnumerable<BindingScope> EnumerateScopes(BindingScope scopes)
        {
            foreach (BindingScope flag in Enum.GetValues<BindingScope>())
            {
                if ((scopes & flag) != 0)
                {
                    yield return flag;
                }
            }
        }

        private static GestureKey GestureKey(BindingScope scope, InputType input, Keys key, ModifierKey mods, MouseAction action)
        {
            // InputType.Key and InputType.KeyCombo describe the same thing, so they must be
            // canonicalised - otherwise a binding saved as "Key" would never match a lookup.
            InputType normalizedInput = input is InputType.Key or InputType.KeyCombo
                ? InputType.KeyCombo
                : input;

            // Wheel up/down are distinct gestures; for everything else the raw action is part of
            // the identity (click vs. double-click vs. drag are different gestures).
            MouseAction normalizedAction = input == InputType.MouseWheel
                ? (action == MouseAction.WheelDown ? MouseAction.WheelDown : MouseAction.WheelUp)
                : action;

            Keys normalizedKey = normalizedInput == InputType.KeyCombo ? key : Keys.None;
            return new GestureKey(scope, normalizedInput, normalizedKey, mods, normalizedAction);
        }

        public ControlBinding? Find(string actionId) => _scheme.Find(actionId);

        public string? Resolve(BindingScope scope, InputType input, Keys key, ModifierKey mods, MouseAction action)
        {
            if (input == InputType.None)
            {
                return null;
            }

            return _winners.TryGetValue(GestureKey(scope, input, key, mods, action), out string? actionId)
                ? actionId
                : null;
        }

        public string? Resolve(BindingScope scope, MouseButtons button, MouseAction action)
        {
            return Resolve(scope, ToInputType(button), Keys.None, CurrentModifiers(), action);
        }

        public string? ResolveKey(BindingScope scope, Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            if (key == Keys.None)
            {
                return null;
            }

            return Resolve(scope, InputType.KeyCombo, key, FromModifierKeys(keyData), MouseAction.Click);
        }

        /// <summary>True when the gesture bound to this action matches the given mouse input.</summary>
        public bool GestureMatches(string actionId, BindingScope scope, MouseButtons button, MouseAction action)
        {
            string? resolved = Resolve(scope, button, action);
            return resolved != null && resolved == actionId;
        }

        public bool GestureMatches(string actionId, BindingScope scope, Keys keyData)
        {
            string? resolved = ResolveKey(scope, keyData);
            return resolved != null && resolved == actionId;
        }

        public static InputType ToInputType(MouseButtons button) => button switch
        {
            MouseButtons.Left => InputType.MouseLeft,
            MouseButtons.Right => InputType.MouseRight,
            MouseButtons.Middle => InputType.MouseMiddle,
            _ => InputType.None
        };

        public static ModifierKey FromModifierKeys(Keys keyData)
        {
            ModifierKey mods = ModifierKey.None;
            if ((keyData & Keys.Control) == Keys.Control) mods |= ModifierKey.Control;
            if ((keyData & Keys.Shift) == Keys.Shift) mods |= ModifierKey.Shift;
            if ((keyData & Keys.Alt) == Keys.Alt) mods |= ModifierKey.Alt;
            if ((keyData & Keys.LWin) == Keys.LWin || (keyData & Keys.RWin) == Keys.RWin) mods |= ModifierKey.Win;
            return mods;
        }

        public static ModifierKey CurrentModifiers() => FromModifierKeys(Control.ModifierKeys);
    }

    internal readonly record struct GestureKey(
        BindingScope Scope,
        InputType Input,
        Keys Key,
        ModifierKey Modifiers,
        MouseAction Action);
}
