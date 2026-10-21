using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ModularAudience.Forms.Modules;

namespace ModularAudience.Forms.Hotkeys
{
    public sealed class HotkeyManager
    {
        private enum CaptureStage
        {
            Idle,
            AwaitingHotkey,
            AwaitingAction
        }

        private enum SyncerHotkeyCaptureMode
        {
            None,
            PausingSyncer,
            NudgingSyncer
        }

        private sealed class PendingMouseAction
        {
            public Control Control { get; init; } = null!;
            public Form Owner { get; init; } = null!;
            public HotkeyMouseGesture Gesture { get; init; }
            public HotkeyModifiers Modifiers { get; init; }
            public Point ScreenPoint { get; init; }
            public DateTime CapturedUtc { get; init; }
        }

        private readonly HotkeySettingsManager _settings;
        private readonly KeyboardDeviceTracker _deviceTracker;
        private readonly KeyboardMessageFilter _keyboardFilter;
        private readonly MouseMessageFilter _mouseFilter;
        private readonly LowLevelMouseProc _mouseHookProc;
        private readonly System.Windows.Forms.Timer _holdTimer;
        private readonly System.Windows.Forms.Timer _mouseCaptureTimer;
        private readonly HashSet<Keys> _pressedKeys = [];
        private readonly Dictionary<Keys, HotkeyBinding> _activeWheelBindings = [];
        private readonly Dictionary<Keys, string> _keyHardwareIds = [];

        private CaptureStage _stage;
        private Keys _capturedKey = Keys.None;
        private HotkeyModifiers _capturedModifiers = HotkeyModifiers.None;
        private string _pendingKeyboardHardwareId = string.Empty;
        private string _pendingKeyboardDisplayName = string.Empty;
        private HotkeyBinding? _reRegisterBinding;
        private SyncerHotkeyCaptureMode _syncerCaptureMode = SyncerHotkeyCaptureMode.None;
        private Keys _pendingSyncerModifierKey = Keys.None;
        private HotkeyModifiers _pendingSyncerModifiers = HotkeyModifiers.None;
        private Keys _pendingHotkeyModifierKey = Keys.None;

        private PendingMouseAction? _pendingMouseAction;

        private HotkeyBinding? _holdBinding;
        private Keys _holdKey;
        private HotkeyModifiers _holdAdditionalModifiers;
        private DateTime _holdStartedUtc;
        private bool _holdActionExecuted;

        private IntPtr _mouseHookHandle = IntPtr.Zero;

        public static HotkeyManager? Instance { get; private set; }

        public event Action? Changed;

        public event Action? KeyboardsChanged;

        public string StatusText { get; private set; } = "No hotkey is being registered.";

        public HotkeyCombo PausingSyncerHotkey => this._settings.PausingSyncerHotkey;

        public HotkeyCombo NudgingSyncerHotkey => this._settings.NudgingSyncerHotkey;

        public bool IsCapturing => this._stage != CaptureStage.Idle;

        public bool IsAwaitingAction => this._stage == CaptureStage.AwaitingAction;

        public bool IsSyncerCaptureActive => this._syncerCaptureMode != SyncerHotkeyCaptureMode.None;

        public string SettingsPath => this._settings.SettingsPath;

        private HotkeyManager()
        {
            this._settings = new HotkeySettingsManager();
            this._deviceTracker = new KeyboardDeviceTracker();
            this._deviceTracker.KeyboardsChanged += this.DeviceTracker_KeyboardsChanged;
            this._keyboardFilter = new KeyboardMessageFilter(this);
            this._mouseFilter = new MouseMessageFilter(this);
            this._mouseHookProc = this.MouseHookCallback;
            this.InstallMouseHook();

            this._holdTimer = new System.Windows.Forms.Timer { Interval = 25 };
            this._holdTimer.Tick += this.HoldTimer_Tick;

            this._mouseCaptureTimer = new System.Windows.Forms.Timer
            {
                Interval = SystemInformation.DoubleClickTime + 75
            };
            this._mouseCaptureTimer.Tick += this.MouseCaptureTimer_Tick;
        }

        public static HotkeyManager Initialize()
        {
            if (Instance != null)
            {
                return Instance;
            }

            HotkeyManager manager = new();
            Instance = manager;
            Application.AddMessageFilter(manager._keyboardFilter);
            Application.AddMessageFilter(manager._mouseFilter);
            return manager;
        }

        public static void Shutdown()
        {
            HotkeyManager? manager = Instance;
            if (manager == null)
            {
                return;
            }

            Instance = null;
            try { manager._holdTimer.Stop(); manager._holdTimer.Dispose(); } catch { }
            try { manager._mouseCaptureTimer.Stop(); manager._mouseCaptureTimer.Dispose(); } catch { }
            try { Application.RemoveMessageFilter(manager._keyboardFilter); } catch { }
            try { Application.RemoveMessageFilter(manager._mouseFilter); } catch { }
            try { manager.RemoveMouseHook(); } catch { }
            try { manager._deviceTracker.KeyboardsChanged -= manager.DeviceTracker_KeyboardsChanged; } catch { }
            try { manager._deviceTracker.Dispose(); } catch { }
        }

        public IReadOnlyList<HotkeyBinding> GetBindings()
        {
            return this._settings.Snapshot();
        }

        public IReadOnlyList<HotkeyBinding> GetBindingsForScope(string? hardwareId)
        {
            return this._settings.GetBindingsForScope(hardwareId);
        }

        public IReadOnlyList<HotkeyKeyboardProfile> GetKeyboardProfiles()
        {
            Dictionary<string, HotkeyKeyboardProfile> profiles =
                new(StringComparer.OrdinalIgnoreCase);

            foreach (HotkeyKeyboardProfile profile in this._settings.GetProfiles())
            {
                profiles[profile.HardwareId] = profile;
            }

            foreach (HotkeyKeyboardProfile connected in this._deviceTracker.GetConnectedKeyboards())
            {
                if (profiles.TryGetValue(connected.HardwareId, out HotkeyKeyboardProfile? existing))
                {
                    existing.DisplayName = connected.DisplayName;
                    existing.IsConnected = true;
                }
                else
                {
                    profiles[connected.HardwareId] = connected;
                }
            }

            return profiles.Values
                .OrderByDescending(profile => profile.IsConnected)
                .ThenBy(profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(profile => profile.HardwareId, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public int CopyKeyboardProfile(
            HotkeyKeyboardProfile source,
            HotkeyKeyboardProfile destination)
        {
            if (source == null || destination == null
                || string.IsNullOrWhiteSpace(source.HardwareId)
                || string.IsNullOrWhiteSpace(destination.HardwareId))
            {
                return 0;
            }

            int copied = this._settings.CopyProfile(
                source.HardwareId,
                source.DisplayName,
                destination.HardwareId,
                destination.DisplayName);

            if (copied > 0)
            {
                this.SetStatus(
                    $"Copied {copied} hotkey(s) from \"{source.DisplayName}\" to \"{destination.DisplayName}\".");
            }

            return copied;
        }

        public int RemoveKeyboardProfile(HotkeyKeyboardProfile profile)
        {
            if (profile == null || string.IsNullOrWhiteSpace(profile.HardwareId))
            {
                return 0;
            }

            int removed = this._settings.RemoveProfile(profile.HardwareId);
            if (removed > 0)
            {
                this.SetStatus(
                    $"Removed {removed} hotkey(s) for \"{profile.DisplayName}\".");
            }

            return removed;
        }

        public void BeginRegisterNew(string? hardwareId = null, string? displayName = null)
        {
            this.CancelPendingMouseAction();
            this._deviceTracker.ClearPendingKeyEvents();
            this._reRegisterBinding = null;
            this._capturedKey = Keys.None;
            this._capturedModifiers = HotkeyModifiers.None;
            this._pendingHotkeyModifierKey = Keys.None;
            this._pendingKeyboardHardwareId = HotkeyKeyboardIds.Normalize(hardwareId);
            this._pendingKeyboardDisplayName = displayName ?? string.Empty;

            // Check if this is a syncer hotkey capture
            if (string.Equals(displayName, "Pausing Syncer", StringComparison.OrdinalIgnoreCase))
            {
                this._syncerCaptureMode = SyncerHotkeyCaptureMode.PausingSyncer;
            }
            else if (string.Equals(displayName, "Nudging Syncer", StringComparison.OrdinalIgnoreCase))
            {
                this._syncerCaptureMode = SyncerHotkeyCaptureMode.NudgingSyncer;
            }
            else
            {
                this._syncerCaptureMode = SyncerHotkeyCaptureMode.None;
            }

            this._stage = CaptureStage.AwaitingHotkey;

            string scope = this._pendingKeyboardHardwareId.Length == 0
                ? "all keyboards"
                : string.IsNullOrWhiteSpace(displayName)
                    ? HotkeyKeyboardIds.GetShortDisplay(this._pendingKeyboardHardwareId)
                    : displayName;
            this.SetStatus($"Press the new hotkey for {scope} (a single modifier like Ctrl is allowed, Esc to cancel)...");
        }

        public void BeginReRegister(HotkeyBinding binding)
        {
            if (binding == null)
            {
                return;
            }

            this.CancelPendingMouseAction();
            this._deviceTracker.ClearPendingKeyEvents();
            this._reRegisterBinding = binding;
            this._capturedKey = Keys.None;
            this._capturedModifiers = HotkeyModifiers.None;
            this._pendingHotkeyModifierKey = Keys.None;
            this._pendingKeyboardHardwareId = string.Empty;
            this._pendingKeyboardDisplayName = string.Empty;
            this._stage = CaptureStage.AwaitingHotkey;
            this.SetStatus($"Press a new hotkey for \"{binding.ActionName}\" (a single modifier is allowed, Esc to cancel)...");
        }

        public void CancelCapture()
        {
            bool wasCapturing = this._stage != CaptureStage.Idle;
            this._stage = CaptureStage.Idle;
            this._reRegisterBinding = null;
            this._syncerCaptureMode = SyncerHotkeyCaptureMode.None;
            this._pendingSyncerModifierKey = Keys.None;
            this._pendingSyncerModifiers = HotkeyModifiers.None;
            this._pendingHotkeyModifierKey = Keys.None;
            this._pendingKeyboardHardwareId = string.Empty;
            this._pendingKeyboardDisplayName = string.Empty;
            this._deviceTracker.ClearPendingKeyEvents();
            this.CancelPendingMouseAction();

            if (wasCapturing)
            {
                this.SetStatus("Hotkey registration canceled.");
            }
        }

        public void RemoveBinding(string id)
        {
            HotkeyBinding? binding = this._settings.Find(id);
            if (binding == null)
            {
                return;
            }

            this._settings.Remove(id);
            this.SetStatus($"Removed hotkey for \"{binding.ActionName}\".");
        }

        public void UpdateHoldThreshold(string id, int holdThresholdMs)
        {
            HotkeyBinding? binding = this._settings.Find(id);
            if (binding == null || binding.HoldThresholdMs == holdThresholdMs)
            {
                return;
            }

            this._settings.UpdateHoldThreshold(id, holdThresholdMs);
            this.SetStatus(
                $"Hold threshold for \"{binding.ActionName}\" set to " +
                $"{binding.HoldThresholdMs} ms.");
        }

        public void UpdateInvertWhileHeld(string id, bool invertWhileHeld)
        {
            HotkeyBinding? binding = this._settings.Find(id);
            if (binding == null || binding.InvertWhileHeld == invertWhileHeld)
            {
                return;
            }

            this._settings.UpdateInvertWhileHeld(id, invertWhileHeld);
            string state = invertWhileHeld ? "enabled" : "disabled";
            this.SetStatus($"Hold inversion for \"{binding.ActionName}\" {state}.");
        }

        public void UpdateScrollTickDistance(string id, int scrollTickDistance)
        {
            HotkeyBinding? binding = this._settings.Find(id);
            if (binding == null || binding.Target.ScrollTickDistance == scrollTickDistance)
            {
                return;
            }

            this._settings.UpdateScrollTickDistance(id, scrollTickDistance);
            this.SetStatus(
                $"Mouse-wheel tick distance for \"{binding.ActionName}\" set to " +
                $"{binding.Target.ScrollTickDistance} units.");
        }

        public void UpdateLoopRateUnitsPerWheelTick(string id, int unitsPerWheelTick)
        {
            HotkeyBinding? binding = this._settings.Find(id);
            if (binding == null || binding.Target.LoopRateUnitsPerWheelTick == unitsPerWheelTick)
            {
                return;
            }

            this._settings.UpdateLoopRateUnitsPerWheelTick(id, unitsPerWheelTick);
            this.SetStatus(
                $"Varispeed step for \"{binding.ActionName}\" set to " +
                $"{unitsPerWheelTick} units per wheel tick.");
        }

        public void UpdatePausingSyncerHotkey(Keys key, HotkeyModifiers modifiers)        {
            this._settings.PausingSyncerHotkey = new HotkeyCombo { Key = key, Modifiers = modifiers };
            this.SetStatus($"Pausing Syncer hotkey set to {HotkeyFormatter.FormatCombo(key, modifiers)}.");
        }

        public void UpdateNudgingSyncerHotkey(Keys key, HotkeyModifiers modifiers)
        {
            this._settings.NudgingSyncerHotkey = new HotkeyCombo { Key = key, Modifiers = modifiers };
            this.SetStatus($"Nudging Syncer hotkey set to {HotkeyFormatter.FormatCombo(key, modifiers)}.");
        }

        /// <summary>
        /// Captures a key press for one of the syncer hotkeys. Modifier keys are not committed
        /// immediately, but on their key release, so that combinations consisting of several
        /// modifiers (e.g. "Ctrl + Shift") can be captured independently of the press order.
        /// </summary>
        private bool HandleSyncerHotkeyKeyDown(Keys key)
        {
            if (IsModifierKey(key))
            {
                this._pendingSyncerModifierKey = HotkeyFormatter.NormalizeKey(key);
                this._pendingSyncerModifiers = HotkeyActionResolver.GetCurrentModifiers()
                    & ~HotkeyFormatter.GetInherentModifier(key);
                return false;
            }

            this._pendingSyncerModifierKey = Keys.None;
            this.CompleteSyncerHotkeyCapture(
                key,
                HotkeyActionResolver.GetCurrentModifiers() & ~HotkeyFormatter.GetInherentModifier(key));
            return true;
        }

        private bool HandleSyncerHotkeyKeyUp(Keys key)
        {
            if (this._pendingSyncerModifierKey == Keys.None)
            {
                return false;
            }

            if (HotkeyFormatter.NormalizeKey(key) != this._pendingSyncerModifierKey)
            {
                return false;
            }

            Keys capturedKey = this._pendingSyncerModifierKey;
            HotkeyModifiers capturedModifiers = this._pendingSyncerModifiers;
            this._pendingSyncerModifierKey = Keys.None;
            this.CompleteSyncerHotkeyCapture(capturedKey, capturedModifiers);
            return true;
        }

        private void CompleteSyncerHotkeyCapture(Keys key, HotkeyModifiers modifiers)
        {
            SyncerHotkeyCaptureMode mode = this._syncerCaptureMode;

            // Reset the capture state before persisting, because persisting raises the
            // "Changed" event and the config window resets its buttons when the capture ended.
            this._stage = CaptureStage.Idle;
            this._syncerCaptureMode = SyncerHotkeyCaptureMode.None;
            this._pendingSyncerModifierKey = Keys.None;
            this._pendingSyncerModifiers = HotkeyModifiers.None;
            this._pendingHotkeyModifierKey = Keys.None;
            this._pendingKeyboardHardwareId = string.Empty;
            this._pendingKeyboardDisplayName = string.Empty;

            switch (mode)
            {
                case SyncerHotkeyCaptureMode.PausingSyncer:
                    this.UpdatePausingSyncerHotkey(key, modifiers);
                    break;
                case SyncerHotkeyCaptureMode.NudgingSyncer:
                    this.UpdateNudgingSyncerHotkey(key, modifiers);
                    break;
            }
        }

        private bool HandleKeyDown(Keys key)
        {
            key = HotkeyFormatter.NormalizeKey(key);
            if (key == Keys.None)
            {
                return false;
            }

            if (this._stage == CaptureStage.AwaitingHotkey)
            {
                if (key == Keys.Escape)
                {
                    this.CancelCapture();
                    return true;
                }

                if (this._syncerCaptureMode != SyncerHotkeyCaptureMode.None)
                {
                    return this.HandleSyncerHotkeyKeyDown(key);
                }

                if (IsModifierKey(key))
                {
                    // Remember the modifier but wait for its release, so that a single
                    // modifier (e.g. Ctrl) can be registered on its own. As soon as another
                    // key is pressed, the combination is captured instead.
                    this._pendingHotkeyModifierKey = HotkeyFormatter.NormalizeKey(key);
                    return false;
                }

                this._deviceTracker.TakeKeyboardId(key);
                this._pendingHotkeyModifierKey = Keys.None;
                HotkeyModifiers modifiers = HotkeyActionResolver.GetCurrentModifiers();
                this._capturedKey = key;
                this._capturedModifiers = modifiers;

                if (this._reRegisterBinding != null)
                {
                    HotkeyBinding reRegister = this._reRegisterBinding;
                    this._settings.UpdateHotkey(reRegister.Id, key, modifiers);
                    this._stage = CaptureStage.Idle;
                    this._reRegisterBinding = null;
                    this.SetStatus($"Updated hotkey for \"{reRegister.ActionName}\" to {HotkeyFormatter.FormatCombo(key, modifiers)}.");
                    return true;
                }

                this._stage = CaptureStage.AwaitingAction;
                this.SetStatus(
                    $"Captured {HotkeyFormatter.FormatCombo(key, modifiers)}. " +
                    "Now click the action control, numeric up/down arrow, or context menu item you want to bind.");
                return true;
            }

            if (this._stage == CaptureStage.AwaitingAction)
            {
                if (key == Keys.Escape)
                {
                    this.CancelCapture();
                    return true;
                }

                return false;
            }

            this.PrunePressedKeys();

            string? hardwareId = this._deviceTracker.TakeKeyboardId(key);
            if (!string.IsNullOrEmpty(hardwareId))
            {
                // Remember which keyboard produced the key, so that the wheel hotkeys
                // can respect the keyboard scope of their binding.
                this._keyHardwareIds[key] = hardwareId;
            }

            HotkeyModifiers currentModifiers = HotkeyActionResolver.GetCurrentModifiers()
                & ~HotkeyFormatter.GetInherentModifier(key);
            HotkeyBinding? binding = this._settings.FindBinding(
                key,
                currentModifiers,
                hardwareId,
                out HotkeyModifiers additionalModifiers);

            if (binding == null)
            {
                return false;
            }

            if (this._pressedKeys.Contains(key))
            {
                return true;
            }

            this._pressedKeys.Add(key);
            if (IsWheelActionKind(binding.Target.ActionKind))
            {
                // Scrollbar and varispeed bindings only react to the mouse wheel
                // while the key is held.
                this._activeWheelBindings[key] = binding;
                return true;
            }

            this.StartHold(binding, key, additionalModifiers);
            return true;
        }

        private bool HandleKeyUp(Keys key)
        {
            key = HotkeyFormatter.NormalizeKey(key);
            this._pressedKeys.Remove(key);
            this._activeWheelBindings.Remove(key);

            if (this._stage == CaptureStage.AwaitingHotkey
                && this._syncerCaptureMode != SyncerHotkeyCaptureMode.None)
            {
                return this.HandleSyncerHotkeyKeyUp(key);
            }

            if (this._stage == CaptureStage.AwaitingHotkey
                && this._pendingHotkeyModifierKey != Keys.None)
            {
                return this.HandleLoneModifierKeyUp(key);
            }

            return false;
        }

        private bool HandleLoneModifierKeyUp(Keys key)
        {
            if (HotkeyFormatter.NormalizeKey(key) != this._pendingHotkeyModifierKey)
            {
                return false;
            }

            Keys capturedKey = this._pendingHotkeyModifierKey;
            this._pendingHotkeyModifierKey = Keys.None;
            this.CompleteLoneModifierHotkeyCapture(capturedKey);
            return true;
        }

        private void CompleteLoneModifierHotkeyCapture(Keys key)
        {
            HotkeyModifiers modifiers = HotkeyModifiers.None;

            if (this._reRegisterBinding != null)
            {
                HotkeyBinding reRegister = this._reRegisterBinding;
                this._settings.UpdateHotkey(reRegister.Id, key, modifiers);
                this._stage = CaptureStage.Idle;
                this._reRegisterBinding = null;
                this.SetStatus(
                    $"Updated hotkey for \"{reRegister.ActionName}\" to " +
                    $"{HotkeyFormatter.FormatCombo(key, modifiers)}.");
                return;
            }

            this._capturedKey = key;
            this._capturedModifiers = modifiers;
            this._stage = CaptureStage.AwaitingAction;
            this.SetStatus(
                $"Captured {HotkeyFormatter.FormatCombo(key, modifiers)}. " +
                "Now click the action control, numeric up/down arrow, or context menu item you want to bind.");
        }

        private void StartHold(
            HotkeyBinding binding,
            Keys key,
            HotkeyModifiers additionalModifiers)
        {
            this.ReleaseActiveHold(executeRelease: true);

            this._holdBinding = binding;
            this._holdKey = key & Keys.KeyCode;
            this._holdAdditionalModifiers = additionalModifiers;
            this._holdStartedUtc = DateTime.UtcNow;

            if (IsMomentaryHold(binding))
            {
                // Invert unchecked: a short tap toggles the action exactly once, a long
                // hold turns the action on while it is held and off again on release.
                // The action is not executed here, so a tap can never turn it on and
                // immediately off again.
                this._holdActionExecuted = false;
            }
            else
            {
                this._holdActionExecuted = ExecuteBinding(binding, additionalModifiers);
            }

            this._holdTimer.Start();
        }

        private void HoldTimer_Tick(object? sender, EventArgs e)
        {
            this.PrunePressedKeys();

            HotkeyBinding? binding = this._holdBinding;
            if (binding == null)
            {
                this._holdTimer.Stop();
                return;
            }

            if (HotkeyActionResolver.IsKeyPhysicallyDown(this._holdKey))
            {
                this.EngageMomentaryHold(binding);
                return;
            }

            this._holdBinding = null;
            this._holdTimer.Stop();

            double heldMs = (DateTime.UtcNow - this._holdStartedUtc).TotalMilliseconds;
            this.ExecuteHoldRelease(binding, heldMs);

            this._holdActionExecuted = false;
            this._holdAdditionalModifiers = HotkeyModifiers.None;
        }

        private void ReleaseActiveHold(bool executeRelease)
        {
            HotkeyBinding? binding = this._holdBinding;
            if (binding == null)
            {
                return;
            }

            this._holdBinding = null;
            this._holdTimer.Stop();

            double heldMs = (DateTime.UtcNow - this._holdStartedUtc).TotalMilliseconds;
            if (executeRelease)
            {
                this.ExecuteHoldRelease(binding, heldMs);
            }

            this._holdActionExecuted = false;
            this._holdAdditionalModifiers = HotkeyModifiers.None;
        }

        private static bool IsMomentaryHold(HotkeyBinding binding)
        {
            return binding.ToggleOnRelease && !binding.InvertWhileHeld;
        }

        private void EngageMomentaryHold(HotkeyBinding binding)
        {
            if (!IsMomentaryHold(binding) || this._holdActionExecuted)
            {
                return;
            }

            double heldMs = (DateTime.UtcNow - this._holdStartedUtc).TotalMilliseconds;
            if (heldMs < binding.HoldThresholdMs)
            {
                return;
            }

            // The hold threshold was reached: the action stays on until release.
            this._holdActionExecuted = true;
            if (HotkeyActionResolver.GetToggleState(binding.Target) != true)
            {
                ExecuteBinding(binding, this._holdAdditionalModifiers);
            }
        }

        private void ExecuteHoldRelease(HotkeyBinding binding, double heldMs)
        {
            if (IsMomentaryHold(binding))
            {
                // If the release arrives before the hold timer noticed the threshold,
                // make sure the action was turned on before switching it off again.
                if (!this._holdActionExecuted
                    && heldMs >= binding.HoldThresholdMs
                    && HotkeyActionResolver.GetToggleState(binding.Target) != true)
                {
                    ExecuteBinding(binding, this._holdAdditionalModifiers);
                }

                // A tap toggles once; a long hold is switched off again on release.
                ExecuteBinding(binding, this._holdAdditionalModifiers);
                return;
            }

            if (this._holdActionExecuted && ShouldExecuteToggleRelease(binding, heldMs))
            {
                ExecuteBinding(binding, this._holdAdditionalModifiers);
            }
        }

        private static bool ShouldExecuteToggleRelease(HotkeyBinding binding, double heldMs)
        {
            // Inverted mode: only a hold past the debounce delay toggles the action again.
            return binding.ToggleOnRelease
                && binding.InvertWhileHeld
                && heldMs >= binding.HoldThresholdMs;
        }

        private static bool ExecuteBinding(
            HotkeyBinding binding,
            HotkeyModifiers additionalModifiers)
        {
            IHotkeyAction action = HotkeyActionCatalog.Create(binding.Target);
            if (action is IModifierAwareHotkeyAction modifierAware)
            {
                return modifierAware.Execute(additionalModifiers);
            }

            return action.Execute();
        }

        private void PrunePressedKeys()
        {
            if (this._pressedKeys.Count == 0)
            {
                if (this._activeWheelBindings.Count > 0)
                {
                    this._activeWheelBindings.Clear();
                }
                return;
            }

            this._pressedKeys.RemoveWhere(key => !HotkeyActionResolver.IsKeyPhysicallyDown(key));
            if (this._activeWheelBindings.Count > 0)
            {
                foreach (Keys key in this._activeWheelBindings.Keys
                    .Where(key => !this._pressedKeys.Contains(key))
                    .ToArray())
                {
                    this._activeWheelBindings.Remove(key);
                }
            }
        }

        private static bool IsWheelActionKind(string actionKind)
        {
            return string.Equals(
                    actionKind,
                    HotkeyActionKinds.ScrollBar,
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    actionKind,
                    HotkeyActionKinds.LoopTrackRate,
                    StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Runs the wheel hotkeys whose key is currently held down. The key state is read
        /// directly from the operating system, so the gesture also works when the window
        /// that received the key press was not the focused window.
        /// </summary>
        private bool TryHandleWheel(int delta, bool horizontalWheel)
        {
            if (delta == 0)
            {
                return false;
            }

            HotkeyModifiers currentModifiers = HotkeyActionResolver.GetCurrentModifiers();

            foreach (HotkeyBinding binding in this._settings.Snapshot())
            {
                if (!IsWheelActionKind(binding.Target.ActionKind))
                {
                    continue;
                }

                Keys bindingKey = binding.Key & Keys.KeyCode;
                if (bindingKey == Keys.None
                    || !HotkeyActionResolver.IsKeyPhysicallyDown(bindingKey))
                {
                    continue;
                }

                if (!binding.IsUniversal
                    && this._keyHardwareIds.TryGetValue(bindingKey, out string? pressedDevice)
                    && !HotkeyKeyboardIds.Same(pressedDevice, binding.KeyboardHardwareId))
                {
                    // The key was pressed on a different keyboard than the one the
                    // hotkey is bound to.
                    continue;
                }

                if ((binding.Modifiers & ~currentModifiers) != 0)
                {
                    continue;
                }

                int effectiveDelta = binding.InvertWhileHeld ? -delta : delta;
                bool handled = string.Equals(
                    binding.Target.ActionKind,
                    HotkeyActionKinds.ScrollBar,
                    StringComparison.OrdinalIgnoreCase)
                    ? HotkeyActionResolver.ExecuteScrollBar(
                        binding.Target,
                        effectiveDelta,
                        horizontalWheel)
                    : HotkeyActionResolver.ExecuteLoopRate(
                        binding.Target,
                        effectiveDelta,
                        horizontalWheel);

                if (handled)
                {
                    return true;
                }
            }

            return false;
        }

        private bool HandleWheelMessage(bool horizontalWheel, IntPtr wParam)
        {
            int delta = unchecked((short)((long)wParam >> 16));
            if (delta == 0)
            {
                return false;
            }

            return this.TryHandleWheel(delta, horizontalWheel);
        }

        private bool HandleMouseMessage(ref Message message)
        {
            const int WM_MOUSEWHEEL = 0x020A;
            const int WM_MOUSEHWHEEL = 0x020E;

            if (message.Msg == WM_MOUSEWHEEL || message.Msg == WM_MOUSEHWHEEL)
            {
                return this.HandleWheelMessage(
                    message.Msg == WM_MOUSEHWHEEL,
                    message.WParam);
            }

            if (this._stage != CaptureStage.AwaitingAction)
            {
                return false;
            }

            const int WM_LBUTTONDOWN = 0x0201;
            const int WM_LBUTTONDBLCLK = 0x0203;
            const int WM_RBUTTONDOWN = 0x0204;
            const int WM_MBUTTONDOWN = 0x0207;

            bool isLeftDown = message.Msg == WM_LBUTTONDOWN;
            bool isDoubleClick = message.Msg == WM_LBUTTONDBLCLK;
            bool isRightDown = message.Msg == WM_RBUTTONDOWN;
            bool isMiddleDown = message.Msg == WM_MBUTTONDOWN;

            if (!isLeftDown && !isDoubleClick && !isRightDown && !isMiddleDown)
            {
                return false;
            }

            Point screenPoint = GetMessageScreenPoint();
            ToolStripItem? menuItem = HotkeyActionResolver.GetContextMenuItemAt(screenPoint);

            if (this._pendingMouseAction?.Gesture == HotkeyMouseGesture.RightClick)
            {
                if (menuItem != null)
                {
                    if ((isLeftDown || isDoubleClick)
                        && HotkeyActionResolver.IsSelectableContextMenuItem(menuItem))
                    {
                        this.CompleteContextMenuItemCapture(this._pendingMouseAction, menuItem);
                    }

                    return false;
                }

                if (!isRightDown)
                {
                    this.FinalizePendingRightClick();
                    return false;
                }

                this.FinalizePendingRightClick();
            }

            if (menuItem != null)
            {
                return false;
            }

            if (isLeftDown
                && HotkeyActionResolver.TryGetNumericUpDownChange(
                    screenPoint,
                    out UpDownBase? upDown,
                    out bool increment)
                && upDown != null)
            {
                Form? numericOwner = upDown.FindForm();
                if (numericOwner != null && numericOwner is not HotkeyConfigWindow)
                {
                    HotkeyModifiers numericModifiers = HotkeyActionResolver.GetCurrentModifiers();
                    this.CompleteNumericUpDownCapture(
                        upDown,
                        numericOwner,
                        increment,
                        numericModifiers);
                    return false;
                }
            }

            if (isLeftDown
                && HotkeyActionResolver.TryGetScrollBar(screenPoint, out ScrollBar? scrollBar)
                && scrollBar != null)
            {
                Form? scrollOwner = scrollBar.FindForm();
                if (scrollOwner != null && scrollOwner is not HotkeyConfigWindow)
                {
                    this.CompleteScrollBarCapture(scrollBar, scrollOwner);
                    return false;
                }
            }

            Control? control = GetControlAt(screenPoint);
            if (control == null)
            {
                return false;
            }
            Form? owner = control.FindForm();
            if (owner == null || owner is HotkeyConfigWindow)
            {
                return false;
            }

            HotkeyModifiers actionModifiers = HotkeyActionResolver.GetCurrentModifiers();

            if (isDoubleClick)
            {
                this.CancelPendingMouseAction();
                this.CompleteActionCapture(
                    control,
                    owner,
                    HotkeyMouseGesture.LeftDoubleClick,
                    actionModifiers,
                    screenPoint);
                return false;
            }

            if (isLeftDown)
            {
                if (this._pendingMouseAction != null)
                {
                    bool sameControl = ReferenceEquals(this._pendingMouseAction.Control, control);
                    bool withinDoubleClick = (DateTime.UtcNow - this._pendingMouseAction.CapturedUtc).TotalMilliseconds
                        <= SystemInformation.DoubleClickTime;

                    if (sameControl && withinDoubleClick)
                    {
                        this.CancelPendingMouseAction();
                        this.CompleteActionCapture(
                            control,
                            owner,
                            HotkeyMouseGesture.LeftDoubleClick,
                            actionModifiers,
                            screenPoint);
                        return false;
                    }

                    this.FinalizePendingMouseAction();
                }

                this._pendingMouseAction = new PendingMouseAction
                {
                    Control = control,
                    Owner = owner,
                    Gesture = HotkeyMouseGesture.LeftClick,
                    Modifiers = actionModifiers,
                    ScreenPoint = screenPoint,
                    CapturedUtc = DateTime.UtcNow
                };
                this._mouseCaptureTimer.Start();
                return false;
            }

            this.CancelPendingMouseAction();

            if (isRightDown)
            {
                this._pendingMouseAction = new PendingMouseAction
                {
                    Control = control,
                    Owner = owner,
                    Gesture = HotkeyMouseGesture.RightClick,
                    Modifiers = actionModifiers,
                    ScreenPoint = screenPoint,
                    CapturedUtc = DateTime.UtcNow
                };
                this.SetStatus(
                    "Captured right-click. Select a context menu item, or click elsewhere " +
                    "to bind opening the context menu.");
                return false;
            }

            this.CompleteActionCapture(
                control,
                owner,
                HotkeyMouseGesture.MiddleClick,
                actionModifiers,
                screenPoint);
            return false;
        }

        private void MouseCaptureTimer_Tick(object? sender, EventArgs e)
        {
            PendingMouseAction? pending = this._pendingMouseAction;
            if (pending != null)
            {
                Size dragSize = SystemInformation.DragSize;
                Point current = Cursor.Position;
                if (Math.Abs(current.X - pending.ScreenPoint.X) > dragSize.Width / 2
                    || Math.Abs(current.Y - pending.ScreenPoint.Y) > dragSize.Height / 2)
                {
                    // The user dragged instead of clicking. Let the drag gesture capture
                    // itself (for example the Loop Control varispeed drag).
                    this.CancelPendingMouseAction();
                    return;
                }
            }

            this.FinalizePendingMouseAction();
        }

        private void FinalizePendingMouseAction()
        {
            PendingMouseAction? pending = this._pendingMouseAction;
            if (pending == null)
            {
                this._mouseCaptureTimer.Stop();
                return;
            }

            this._pendingMouseAction = null;
            this._mouseCaptureTimer.Stop();

            this.CompleteActionCapture(
                pending.Control,
                pending.Owner,
                pending.Gesture,
                pending.Modifiers,
                pending.ScreenPoint);
        }

        private void FinalizePendingRightClick()
        {
            PendingMouseAction? pending = this._pendingMouseAction;
            if (pending == null || pending.Gesture != HotkeyMouseGesture.RightClick)
            {
                return;
            }

            this._pendingMouseAction = null;
            this.CompleteActionCapture(
                pending.Control,
                pending.Owner,
                pending.Gesture,
                pending.Modifiers,
                pending.ScreenPoint);
        }

        private void CancelPendingMouseAction()
        {
            this._pendingMouseAction = null;
            try { this._mouseCaptureTimer.Stop(); } catch (ObjectDisposedException) { }
        }

        private void CompleteContextMenuItemCapture(
            PendingMouseAction pending,
            ToolStripItem item)
        {
            if (this._stage != CaptureStage.AwaitingAction)
            {
                return;
            }

            Keys hotkey = this._capturedKey;
            HotkeyModifiers hotkeyModifiers = this._capturedModifiers;
            string keyboardHardwareId = this._pendingKeyboardHardwareId;
            string keyboardDisplayName = this._pendingKeyboardDisplayName;
            this._stage = CaptureStage.Idle;
            this._capturedKey = Keys.None;
            this._capturedModifiers = HotkeyModifiers.None;
            this._pendingKeyboardHardwareId = string.Empty;
            this._pendingKeyboardDisplayName = string.Empty;
            this.CancelPendingMouseAction();

            try
            {
                HotkeyActionTarget target = HotkeyActionResolver.BuildContextMenuItemTarget(
                    pending.Control,
                    pending.Owner,
                    item,
                    pending.Modifiers,
                    pending.ScreenPoint);

                string actionName = this.MakeUniqueActionName(
                    HotkeyActionResolver.BuildContextMenuItemActionName(pending.Owner, item));

                HotkeyBinding binding = new()
                {
                    Id = Guid.NewGuid().ToString("N"),
                    ActionName = actionName,
                    Key = hotkey,
                    Modifiers = hotkeyModifiers,
                    KeyboardHardwareId = keyboardHardwareId,
                    KeyboardDisplayName = keyboardDisplayName,
                    Target = target,
                    ToggleOnRelease = false
                };

                this._settings.AddOrReplace(binding);
                this.SetStatus(
                    $"Registered {binding.GetHotkeyDisplay()} -> {binding.ActionName} " +
                    $"(context menu item) for {binding.GetKeyboardDisplay()}.");
            }
            catch (Exception ex)
            {
                this.SetStatus("Could not register this context menu item.");
                System.Diagnostics.Debug.WriteLine($"Hotkeys: context menu capture failed: {ex.Message}");
            }
        }

        private void CompleteNumericUpDownCapture(
            UpDownBase upDown,
            Form owner,
            bool increment,
            HotkeyModifiers actionModifiers)
        {
            if (this._stage != CaptureStage.AwaitingAction)
            {
                return;
            }

            Keys hotkey = this._capturedKey;
            HotkeyModifiers hotkeyModifiers = this._capturedModifiers;
            string keyboardHardwareId = this._pendingKeyboardHardwareId;
            string keyboardDisplayName = this._pendingKeyboardDisplayName;
            this._stage = CaptureStage.Idle;
            this._capturedKey = Keys.None;
            this._capturedModifiers = HotkeyModifiers.None;
            this._pendingKeyboardHardwareId = string.Empty;
            this._pendingKeyboardDisplayName = string.Empty;
            this.CancelPendingMouseAction();

            try
            {
                HotkeyActionTarget target = HotkeyActionResolver.BuildNumericUpDownTarget(
                    upDown,
                    owner,
                    increment,
                    actionModifiers);

                string actionName = this.MakeUniqueActionName(
                    HotkeyActionResolver.BuildNumericUpDownActionName(owner, upDown, increment));

                HotkeyBinding binding = new()
                {
                    Id = Guid.NewGuid().ToString("N"),
                    ActionName = actionName,
                    Key = hotkey,
                    Modifiers = hotkeyModifiers,
                    KeyboardHardwareId = keyboardHardwareId,
                    KeyboardDisplayName = keyboardDisplayName,
                    Target = target,
                    ToggleOnRelease = false
                };

                this._settings.AddOrReplace(binding);
                this.SetStatus(
                    $"Registered {binding.GetHotkeyDisplay()} -> {binding.ActionName} " +
                    $"({binding.GetGestureDisplay()}) for {binding.GetKeyboardDisplay()}.");
            }
            catch (Exception ex)
            {
                this.SetStatus("Could not register this numeric up/down action.");
                System.Diagnostics.Debug.WriteLine($"Hotkeys: numeric up/down capture failed: {ex.Message}");
            }
        }

        public bool TryCaptureLoopRateAction(LoopControl loopControl, int rowIndex, bool shiftAll)
        {
            if (this._stage != CaptureStage.AwaitingAction)
            {
                return false;
            }

            if (rowIndex < 0)
            {
                return false;
            }

            Keys hotkey = this._capturedKey;
            HotkeyModifiers hotkeyModifiers = this._capturedModifiers;
            string keyboardHardwareId = this._pendingKeyboardHardwareId;
            string keyboardDisplayName = this._pendingKeyboardDisplayName;
            this._stage = CaptureStage.Idle;
            this._capturedKey = Keys.None;
            this._capturedModifiers = HotkeyModifiers.None;
            this._pendingKeyboardHardwareId = string.Empty;
            this._pendingKeyboardDisplayName = string.Empty;
            this.CancelPendingMouseAction();

            try
            {
                HotkeyActionTarget target = HotkeyActionResolver.BuildLoopRateTarget(
                    loopControl,
                    rowIndex,
                    shiftAll);

                string actionName = this.MakeUniqueActionName(
                    HotkeyActionResolver.BuildLoopRateActionName(
                        loopControl,
                        rowIndex,
                        shiftAll));

                HotkeyBinding binding = new()
                {
                    Id = Guid.NewGuid().ToString("N"),
                    ActionName = actionName,
                    Key = hotkey,
                    Modifiers = hotkeyModifiers,
                    KeyboardHardwareId = keyboardHardwareId,
                    KeyboardDisplayName = keyboardDisplayName,
                    Target = target,
                    ToggleOnRelease = false,
                    InvertWhileHeld = false
                };

                this._settings.AddOrReplace(binding);
                this.SetStatus(
                    $"Registered {binding.GetHotkeyDisplay()} -> {binding.ActionName}. " +
                    "Hold it and turn the mouse wheel to change the varispeed.");
                return true;
            }
            catch (Exception ex)
            {
                this.SetStatus("Could not register this varispeed action.");
                System.Diagnostics.Debug.WriteLine($"Hotkeys: varispeed capture failed: {ex.Message}");
                return false;
            }
        }

        private void CompleteScrollBarCapture(ScrollBar scrollBar, Form owner)
        {
            if (this._stage != CaptureStage.AwaitingAction)
            {
                return;
            }

            Keys hotkey = this._capturedKey;
            HotkeyModifiers hotkeyModifiers = this._capturedModifiers;
            string keyboardHardwareId = this._pendingKeyboardHardwareId;
            string keyboardDisplayName = this._pendingKeyboardDisplayName;
            this._stage = CaptureStage.Idle;
            this._capturedKey = Keys.None;
            this._capturedModifiers = HotkeyModifiers.None;
            this._pendingKeyboardHardwareId = string.Empty;
            this._pendingKeyboardDisplayName = string.Empty;
            this.CancelPendingMouseAction();

            try
            {
                HotkeyActionTarget target = HotkeyActionResolver.BuildScrollBarTarget(
                    scrollBar,
                    owner);

                string actionName = this.MakeUniqueActionName(
                    HotkeyActionResolver.BuildScrollBarActionName(owner, scrollBar));

                HotkeyBinding binding = new()
                {
                    Id = Guid.NewGuid().ToString("N"),
                    ActionName = actionName,
                    Key = hotkey,
                    Modifiers = hotkeyModifiers,
                    KeyboardHardwareId = keyboardHardwareId,
                    KeyboardDisplayName = keyboardDisplayName,
                    Target = target,
                    ToggleOnRelease = false,
                    InvertWhileHeld = false
                };

                this._settings.AddOrReplace(binding);
                this.SetStatus(
                    $"Registered {binding.GetHotkeyDisplay()} -> {binding.ActionName}. " +
                    $"Hold it and turn the mouse wheel to scroll ({target.ScrollTickDistance} units per notch).");
            }
            catch (Exception ex)
            {
                this.SetStatus("Could not register this scrollbar action.");
                System.Diagnostics.Debug.WriteLine($"Hotkeys: scrollbar capture failed: {ex.Message}");
            }
        }

        private void CompleteActionCapture(
            Control control,
            Form owner,
            HotkeyMouseGesture gesture,
            HotkeyModifiers actionModifiers,
            Point screenPoint)
        {
            if (this._stage != CaptureStage.AwaitingAction)
            {
                return;
            }

            Keys hotkey = this._capturedKey;
            HotkeyModifiers hotkeyModifiers = this._capturedModifiers;
            string keyboardHardwareId = this._pendingKeyboardHardwareId;
            string keyboardDisplayName = this._pendingKeyboardDisplayName;
            this._stage = CaptureStage.Idle;
            this._capturedKey = Keys.None;
            this._capturedModifiers = HotkeyModifiers.None;
            this._pendingKeyboardHardwareId = string.Empty;
            this._pendingKeyboardDisplayName = string.Empty;
            this.CancelPendingMouseAction();

            try
            {
                HotkeyActionTarget target = HotkeyActionResolver.BuildTarget(
                    control,
                    owner,
                    gesture,
                    actionModifiers,
                    screenPoint);

                string actionName = this.MakeUniqueActionName(
                    HotkeyActionResolver.BuildActionName(owner, control, gesture, actionModifiers));

                HotkeyBinding binding = new()
                {
                    Id = Guid.NewGuid().ToString("N"),
                    ActionName = actionName,
                    Key = hotkey,
                    Modifiers = hotkeyModifiers,
                    KeyboardHardwareId = keyboardHardwareId,
                    KeyboardDisplayName = keyboardDisplayName,
                    Target = target,
                    ToggleOnRelease = HotkeyActionResolver.IsToggleAction(control)
                };

                this._settings.AddOrReplace(binding);
                this.SetStatus(
                    $"Registered {binding.GetHotkeyDisplay()} -> {binding.ActionName} " +
                    $"({binding.GetGestureDisplay()}) for {binding.GetKeyboardDisplay()}.");
            }
            catch (Exception ex)
            {
                this.SetStatus("Could not register this action.");
                System.Diagnostics.Debug.WriteLine($"Hotkeys: capture failed: {ex.Message}");
            }
        }

        private string MakeUniqueActionName(string actionName)
        {
            HashSet<string> existing = this._settings.Snapshot()
                .Select(binding => binding.ActionName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (!existing.Contains(actionName))
            {
                return actionName;
            }

            for (int suffix = 2; ; suffix++)
            {
                string candidate = $"{actionName} ({suffix})";
                if (!existing.Contains(candidate))
                {
                    return candidate;
                }
            }
        }

        private void DeviceTracker_KeyboardsChanged()
        {
            this.KeyboardsChanged?.Invoke();
        }

        private void SetStatus(string status)
        {
            this.StatusText = status;
            this.Changed?.Invoke();
        }

        private static bool IsModifierKey(Keys key)
        {
            return key is Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey
                or Keys.ControlKey or Keys.LControlKey or Keys.RControlKey
                or Keys.Menu or Keys.LMenu or Keys.RMenu
                or Keys.LWin or Keys.RWin;
        }

        private static Point GetMessageScreenPoint()
        {
            uint packed = GetMessagePos();
            int x = unchecked((short)(packed & 0xFFFF));
            int y = unchecked((short)((packed >> 16) & 0xFFFF));
            return new Point(x, y);
        }

        private static Control? GetControlAt(Point screenPoint)
        {
            POINT point = new() { X = screenPoint.X, Y = screenPoint.Y };
            IntPtr handle = WindowFromPoint(point);
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            Control? control = Control.FromChildHandle(handle);
            while (control != null
                && control is not Form
                && string.IsNullOrEmpty(control.Name)
                && control.Tag == null
                && control is not Button
                && control is not CheckBox
                && control is not RadioButton)
            {
                control = control.Parent;
            }

            return control;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        private const int WhMouseLowLevel = 14;
        private const int WmMouseWheel = 0x020A;
        private const int WmMouseHWheel = 0x020E;

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT point);

        [DllImport("user32.dll")]
        private static extern uint GetMessagePos();

        private sealed class KeyboardMessageFilter : IMessageFilter
        {
            private readonly HotkeyManager _manager;

            public KeyboardMessageFilter(HotkeyManager manager)
            {
                this._manager = manager;
            }

            public bool PreFilterMessage(ref Message message)
            {
                const int WM_KEYDOWN = 0x0100;
                const int WM_KEYUP = 0x0101;
                const int WM_SYSKEYDOWN = 0x0104;
                const int WM_SYSKEYUP = 0x0105;

                Keys key = (Keys)((int)message.WParam & 0xFFFF);

                if (message.Msg == WM_KEYDOWN || message.Msg == WM_SYSKEYDOWN)
                {
                    return this._manager.HandleKeyDown(key);
                }

                if (message.Msg == WM_KEYUP || message.Msg == WM_SYSKEYUP)
                {
                    this._manager.HandleKeyUp(key);
                }

                return false;
            }
        }

        private sealed class MouseMessageFilter : IMessageFilter
        {
            private readonly HotkeyManager _manager;

            public MouseMessageFilter(HotkeyManager manager)
            {
                this._manager = manager;
            }

            public bool PreFilterMessage(ref Message message)
            {
                return this._manager.HandleMouseMessage(ref message);
            }
        }

        private void InstallMouseHook()
        {
            try
            {
                this._mouseHookHandle = SetWindowsHookEx(
                    WhMouseLowLevel,
                    this._mouseHookProc,
                    GetModuleHandle(null),
                    0);
            }
            catch (Exception ex)
            {
                this._mouseHookHandle = IntPtr.Zero;
                System.Diagnostics.Debug.WriteLine(
                    $"Hotkeys: mouse hook could not be installed: {ex.Message}");
            }
        }

        private void RemoveMouseHook()
        {
            if (this._mouseHookHandle == IntPtr.Zero)
            {
                return;
            }

            try
            {
                UnhookWindowsHookEx(this._mouseHookHandle);
            }
            catch
            {
            }

            this._mouseHookHandle = IntPtr.Zero;
        }

        /// <summary>
        /// Low level mouse hook. It catches the mouse wheel no matter which window is
        /// focused, so that the wheel hotkeys also work while another program is active.
        /// </summary>
        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int message = wParam.ToInt32();
                if (message == WmMouseWheel || message == WmMouseHWheel)
                {
                    try
                    {
                        MSLLHOOKSTRUCT hook = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                        int delta = unchecked((short)(hook.mouseData >> 16));
                        if (delta != 0 && this.TryHandleWheel(delta, message == WmMouseHWheel))
                        {
                            // The wheel was used by a hotkey, so it must not scroll
                            // the window below the mouse pointer.
                            return 1;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"Hotkeys: wheel hotkey failed: {ex.Message}");
                    }
                }
            }

            return CallNextHookEx(this._mouseHookHandle, nCode, wParam, lParam);
        }

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(
            int idHook,
            LowLevelMouseProc lpfn,
            IntPtr hMod,
            uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(
            IntPtr hhk,
            int nCode,
            IntPtr wParam,
            IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);
    }
}
