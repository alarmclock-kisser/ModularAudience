using System;
using System.Collections.Generic;

namespace ModularAudience.Forms.Hotkeys
{
    public static class HotkeyActionKinds
    {
        public const string ControlGesture = "control-gesture";
        public const string ContextMenuItem = "context-menu-item";
        public const string NumericUpDown = "numeric-updown";
    }

    public interface IHotkeyAction
    {
        string Id { get; }
        bool Execute();
    }

    public interface IModifierAwareHotkeyAction : IHotkeyAction
    {
        bool Execute(HotkeyModifiers additionalModifiers);
    }

    public sealed class ControlGestureHotkeyAction : IModifierAwareHotkeyAction
    {
        private readonly HotkeyActionTarget _target;

        public ControlGestureHotkeyAction(HotkeyActionTarget target)
        {
            this._target = target;
        }

        public string Id => HotkeyActionKinds.ControlGesture;

        public bool Execute()
        {
            return this.Execute(HotkeyModifiers.None);
        }

        public bool Execute(HotkeyModifiers additionalModifiers)
        {
            return HotkeyActionResolver.ExecuteControlGesture(this._target, additionalModifiers);
        }
    }

    public sealed class ContextMenuItemHotkeyAction : IHotkeyAction
    {
        private readonly HotkeyActionTarget _target;

        public ContextMenuItemHotkeyAction(HotkeyActionTarget target)
        {
            this._target = target;
        }

        public string Id => HotkeyActionKinds.ContextMenuItem;

        public bool Execute()
        {
            return HotkeyActionResolver.ExecuteContextMenuItem(this._target);
        }
    }

    public sealed class NumericUpDownHotkeyAction : IHotkeyAction
    {
        private readonly HotkeyActionTarget _target;

        public NumericUpDownHotkeyAction(HotkeyActionTarget target)
        {
            this._target = target;
        }

        public string Id => HotkeyActionKinds.NumericUpDown;

        public bool Execute()
        {
            return HotkeyActionResolver.ExecuteNumericUpDown(this._target);
        }
    }

    public static class HotkeyActionCatalog
    {
        private static readonly object Sync = new();
        private static readonly Dictionary<string, Func<HotkeyActionTarget, IHotkeyAction>> Factories =
            new(StringComparer.OrdinalIgnoreCase)
            {
                [HotkeyActionKinds.ControlGesture] = target => new ControlGestureHotkeyAction(target),
                [HotkeyActionKinds.ContextMenuItem] = target => new ContextMenuItemHotkeyAction(target),
                [HotkeyActionKinds.NumericUpDown] = target => new NumericUpDownHotkeyAction(target)
            };

        public static void Register(string id, Func<HotkeyActionTarget, IHotkeyAction> factory)
        {
            if (string.IsNullOrWhiteSpace(id) || factory == null)
            {
                return;
            }

            lock (Sync)
            {
                Factories[id] = factory;
            }
        }

        public static IHotkeyAction Create(HotkeyActionTarget? target)
        {
            if (target == null)
            {
                return new UnavailableHotkeyAction(string.Empty);
            }

            string id = target.ActionKind ?? string.Empty;
            Func<HotkeyActionTarget, IHotkeyAction>? factory;
            lock (Sync)
            {
                Factories.TryGetValue(id, out factory);
            }

            return factory != null ? factory(target) : new UnavailableHotkeyAction(id);
        }

        private sealed class UnavailableHotkeyAction : IHotkeyAction
        {
            public UnavailableHotkeyAction(string id)
            {
                this.Id = id;
            }

            public string Id { get; }

            public bool Execute()
            {
                System.Diagnostics.Debug.WriteLine($"Hotkeys: no action implementation registered for '{this.Id}'.");
                return false;
            }
        }
    }
}
