using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;

namespace ModularAudience.Forms.Hotkeys
{
    public sealed class HotkeySettingsManager
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly string _fallbackSettingsPath;
        private string _settingsPath;
        private readonly object _lock = new();
        private List<HotkeyBinding> _bindings = [];
        private HotkeyCombo _pausingSyncerHotkey = new() { Key = Keys.ShiftKey, Modifiers = HotkeyModifiers.None };
        private HotkeyCombo _nudgingSyncerHotkey = new() { Key = Keys.CapsLock, Modifiers = HotkeyModifiers.None };

        public HotkeySettingsManager()
        {
            this._fallbackSettingsPath = Path.Combine(
                AppContext.BaseDirectory,
                "hotkeys.json");
            this._settingsPath = ResolveSettingsPath(this._fallbackSettingsPath);
            this._bindings = this.LoadCore();
        }

        public string SettingsPath => this._settingsPath;

        public event Action? Changed;

        public HotkeyCombo PausingSyncerHotkey
        {
            get
            {
                lock (this._lock)
                {
                    return this._pausingSyncerHotkey;
                }
            }
            set
            {
                lock (this._lock)
                {
                    this._pausingSyncerHotkey = value ?? new HotkeyCombo { Key = Keys.ShiftKey, Modifiers = HotkeyModifiers.None };
                    this.SaveCore();
                }
                this.Changed?.Invoke();
            }
        }

        public HotkeyCombo NudgingSyncerHotkey
        {
            get
            {
                lock (this._lock)
                {
                    return this._nudgingSyncerHotkey;
                }
            }
            set
            {
                lock (this._lock)
                {
                    this._nudgingSyncerHotkey = value ?? new HotkeyCombo { Key = Keys.CapsLock, Modifiers = HotkeyModifiers.None };
                    this.SaveCore();
                }
                this.Changed?.Invoke();
            }
        }

        public IReadOnlyList<HotkeyBinding> Snapshot()
        {
            lock (this._lock)
            {
                return this._bindings.ToList();
            }
        }

        public HotkeyBinding? Find(string id)
        {
            lock (this._lock)
            {
                return this._bindings.FirstOrDefault(b => b.Id == id);
            }
        }

        public HotkeyBinding? FindBinding(Keys key, HotkeyModifiers modifiers, string? hardwareId)
        {
            return this.FindBinding(key, modifiers, hardwareId, out _);
        }

        public HotkeyBinding? FindBinding(
            Keys key,
            HotkeyModifiers modifiers,
            string? hardwareId,
            out HotkeyModifiers additionalModifiers)
        {
            string scope = HotkeyKeyboardIds.Normalize(hardwareId);
            additionalModifiers = HotkeyModifiers.None;

            lock (this._lock)
            {
                HotkeyBinding? exactUniversal = null;
                HotkeyBinding? subsetUniversal = null;
                HotkeyBinding? subsetScoped = null;
                int subsetUniversalSpecificity = -1;
                int subsetScopedSpecificity = -1;

                foreach (HotkeyBinding binding in this._bindings)
                {
                    if ((binding.Key & Keys.KeyCode) != (key & Keys.KeyCode))
                    {
                        continue;
                    }

                    bool scoped = scope.Length > 0
                        && HotkeyKeyboardIds.Same(scope, binding.KeyboardHardwareId);
                    bool universal = binding.IsUniversal;

                    if (binding.Modifiers == modifiers)
                    {
                        if (scoped)
                        {
                            return binding;
                        }

                        if (universal)
                        {
                            exactUniversal ??= binding;
                        }

                        continue;
                    }

                    if ((binding.Modifiers & ~modifiers) != 0)
                    {
                        continue;
                    }

                    int specificity = CountModifiers(binding.Modifiers);
                    if (scoped && specificity > subsetScopedSpecificity)
                    {
                        subsetScoped = binding;
                        subsetScopedSpecificity = specificity;
                    }
                    else if (universal && specificity > subsetUniversalSpecificity)
                    {
                        subsetUniversal = binding;
                        subsetUniversalSpecificity = specificity;
                    }
                }

                if (exactUniversal != null)
                {
                    return exactUniversal;
                }

                HotkeyBinding? match = subsetScoped ?? subsetUniversal;
                if (match != null)
                {
                    additionalModifiers = modifiers & ~match.Modifiers;
                }

                return match;
            }
        }

        private static int CountModifiers(HotkeyModifiers modifiers)
        {
            int count = 0;
            if ((modifiers & HotkeyModifiers.Control) != 0)
            {
                count++;
            }
            if ((modifiers & HotkeyModifiers.Shift) != 0)
            {
                count++;
            }
            if ((modifiers & HotkeyModifiers.Alt) != 0)
            {
                count++;
            }
            if ((modifiers & HotkeyModifiers.Win) != 0)
            {
                count++;
            }

            return count;
        }

        public IReadOnlyList<HotkeyBinding> GetBindingsForScope(string? hardwareId)
        {
            string scope = HotkeyKeyboardIds.Normalize(hardwareId);
            lock (this._lock)
            {
                return this._bindings
                    .Where(binding => HotkeyKeyboardIds.Same(scope, binding.KeyboardHardwareId))
                    .ToList();
            }
        }

        public IReadOnlyList<HotkeyKeyboardProfile> GetProfiles()
        {
            lock (this._lock)
            {
                return this._bindings
                    .Where(binding => !binding.IsUniversal)
                    .GroupBy(binding => HotkeyKeyboardIds.Normalize(binding.KeyboardHardwareId))
                    .Select(group => new HotkeyKeyboardProfile
                    {
                        HardwareId = group.Key,
                        DisplayName = group
                            .Select(binding => binding.KeyboardDisplayName)
                            .LastOrDefault(name => !string.IsNullOrWhiteSpace(name))
                            ?? HotkeyKeyboardIds.GetShortDisplay(group.Key)
                    })
                    .ToList();
            }
        }

        public void AddOrReplace(HotkeyBinding binding)
        {
            lock (this._lock)
            {
                string scope = HotkeyKeyboardIds.Normalize(binding.KeyboardHardwareId);
                binding.KeyboardHardwareId = scope;

                this._bindings.RemoveAll(candidate =>
                    candidate.Id != binding.Id
                    && (candidate.Key & Keys.KeyCode) == (binding.Key & Keys.KeyCode)
                    && candidate.Modifiers == binding.Modifiers
                    && HotkeyKeyboardIds.Same(scope, candidate.KeyboardHardwareId));

                int index = this._bindings.FindIndex(b => b.Id == binding.Id);
                if (index >= 0)
                {
                    this._bindings[index] = binding;
                }
                else
                {
                    this._bindings.Add(binding);
                }

                this.SaveCore();
            }

            this.Changed?.Invoke();
        }

        public void UpdateHotkey(string id, Keys key, HotkeyModifiers modifiers)
        {
            bool updated = false;
            lock (this._lock)
            {
                HotkeyBinding? binding = this._bindings.FirstOrDefault(b => b.Id == id);
                if (binding == null)
                {
                    return;
                }

                string scope = HotkeyKeyboardIds.Normalize(binding.KeyboardHardwareId);
                this._bindings.RemoveAll(candidate =>
                    candidate.Id != id
                    && (candidate.Key & Keys.KeyCode) == (key & Keys.KeyCode)
                    && candidate.Modifiers == modifiers
                    && HotkeyKeyboardIds.Same(scope, candidate.KeyboardHardwareId));

                binding.Key = key;
                binding.Modifiers = modifiers;
                this.SaveCore();
                updated = true;
            }

            if (updated)
            {
                this.Changed?.Invoke();
            }
        }

        public void UpdateHoldThreshold(string id, int holdThresholdMs)
        {
            bool updated = false;
            lock (this._lock)
            {
                HotkeyBinding? binding = this._bindings.FirstOrDefault(b => b.Id == id);
                if (binding == null)
                {
                    return;
                }

                binding.HoldThresholdMs = Math.Clamp(
                    holdThresholdMs,
                    HotkeyDefaults.MinHoldThresholdMs,
                    HotkeyDefaults.MaxHoldThresholdMs);
                this.SaveCore();
                updated = true;
            }

            if (updated)
            {
                this.Changed?.Invoke();
            }
        }

        public void UpdateInvertWhileHeld(string id, bool invertWhileHeld)
        {
            bool updated = false;
            lock (this._lock)
            {
                HotkeyBinding? binding = this._bindings.FirstOrDefault(b => b.Id == id);
                if (binding == null)
                {
                    return;
                }

                binding.InvertWhileHeld = invertWhileHeld;
                this.SaveCore();
                updated = true;
            }

            if (updated)
            {
                this.Changed?.Invoke();
            }
        }

        public void UpdateScrollTickDistance(string id, int scrollTickDistance)
        {
            bool updated = false;
            lock (this._lock)
            {
                HotkeyBinding? binding = this._bindings.FirstOrDefault(b => b.Id == id);
                if (binding == null)
                {
                    return;
                }

                binding.Target.ScrollTickDistance = Math.Clamp(
                    scrollTickDistance,
                    HotkeyDefaults.MinScrollTickDistance,
                    HotkeyDefaults.MaxScrollTickDistance);
                this.SaveCore();
                updated = true;
            }

            if (updated)
            {
                this.Changed?.Invoke();
            }
        }

        public void UpdateLoopRateUnitsPerWheelTick(string id, int unitsPerWheelTick)
        {
            bool updated = false;
            lock (this._lock)
            {
                HotkeyBinding? binding = this._bindings.FirstOrDefault(b => b.Id == id);
                if (binding == null)
                {
                    return;
                }

                binding.Target.LoopRateUnitsPerWheelTick = Math.Clamp(
                    unitsPerWheelTick,
                    HotkeyDefaults.MinLoopRateUnitsPerWheelTick,
                    HotkeyDefaults.MaxLoopRateUnitsPerWheelTick);
                this.SaveCore();
                updated = true;
            }

            if (updated)
            {
                this.Changed?.Invoke();
            }
        }

        public int CopyProfile(
            string sourceHardwareId,
            string sourceDisplayName,
            string destinationHardwareId,
            string destinationDisplayName)
        {            string source = HotkeyKeyboardIds.Normalize(sourceHardwareId);
            string destination = HotkeyKeyboardIds.Normalize(destinationHardwareId);
            if (source.Length == 0 || destination.Length == 0 || source == destination)
            {
                return 0;
            }

            int copied;
            lock (this._lock)
            {
                List<HotkeyBinding> sourceBindings = this._bindings
                    .Where(binding => HotkeyKeyboardIds.Same(source, binding.KeyboardHardwareId))
                    .ToList();

                if (sourceBindings.Count == 0)
                {
                    return 0;
                }

                this._bindings.RemoveAll(binding =>
                    HotkeyKeyboardIds.Same(destination, binding.KeyboardHardwareId));

                foreach (HotkeyBinding sourceBinding in sourceBindings)
                {
                    HotkeyBinding copy = CloneBinding(sourceBinding);
                    copy.Id = Guid.NewGuid().ToString("N");
                    copy.KeyboardHardwareId = destination;
                    copy.KeyboardDisplayName = string.IsNullOrWhiteSpace(destinationDisplayName)
                        ? sourceDisplayName
                        : destinationDisplayName;
                    this._bindings.Add(copy);
                }

                this.SaveCore();
                copied = sourceBindings.Count;
            }

            this.Changed?.Invoke();
            return copied;
        }

        public int RemoveProfile(string hardwareId)
        {
            string scope = HotkeyKeyboardIds.Normalize(hardwareId);
            if (scope.Length == 0)
            {
                return 0;
            }

            int removed;
            lock (this._lock)
            {
                removed = this._bindings.RemoveAll(binding =>
                    HotkeyKeyboardIds.Same(scope, binding.KeyboardHardwareId));
                if (removed > 0)
                {
                    this.SaveCore();
                }
            }

            if (removed > 0)
            {
                this.Changed?.Invoke();
            }

            return removed;
        }

        private static HotkeyBinding CloneBinding(HotkeyBinding source)
        {
            return new HotkeyBinding
            {
                Id = source.Id,
                ActionName = source.ActionName,
                Key = source.Key,
                Modifiers = source.Modifiers,
                KeyboardHardwareId = source.KeyboardHardwareId,
                KeyboardDisplayName = source.KeyboardDisplayName,
                HoldThresholdMs = source.HoldThresholdMs,
                ToggleOnRelease = source.ToggleOnRelease,
                InvertWhileHeld = source.InvertWhileHeld,
                Target = new HotkeyActionTarget
                {
                    ActionKind = source.Target.ActionKind,
                    OwnerType = source.Target.OwnerType,
                    OwnerTitle = source.Target.OwnerTitle,
                    TrackViewId = source.Target.TrackViewId,
                    UseLastFocusedTrackView = source.Target.UseLastFocusedTrackView,
                    ControlPath = source.Target.ControlPath
                        .Select(segment => new HotkeyControlPathSegment
                        {
                            TypeName = segment.TypeName,
                            Name = segment.Name,
                            Tag = segment.Tag,
                            Text = segment.Text,
                            Index = segment.Index
                        })
                        .ToList(),
                    ContextMenuName = source.Target.ContextMenuName,
                    MenuItemPath = source.Target.MenuItemPath
                        .Select(segment => new HotkeyMenuItemPathSegment
                        {
                            TypeName = segment.TypeName,
                            Name = segment.Name,
                            Text = segment.Text,
                            Index = segment.Index
                        })
                        .ToList(),
                    NumericUpDownIncrement = source.Target.NumericUpDownIncrement,
                    Gesture = source.Target.Gesture,
                    ActionModifiers = source.Target.ActionModifiers,
                    RelativeX = source.Target.RelativeX,
                    RelativeY = source.Target.RelativeY,
                    ScrollTickDistance = source.Target.ScrollTickDistance,
                    LoopControlTrackIndex = source.Target.LoopControlTrackIndex,
                    LoopRateUnitsPerWheelTick = source.Target.LoopRateUnitsPerWheelTick,
                    LoopControlAudioId = source.Target.LoopControlAudioId,
                    LoopRateShiftAll = source.Target.LoopRateShiftAll
                }
            };
        }

        public void Remove(string id)
        {
            bool removed;
            lock (this._lock)
            {
                removed = this._bindings.RemoveAll(b => b.Id == id) > 0;
                if (removed)
                {
                    this.SaveCore();
                }
            }

            if (removed)
            {
                this.Changed?.Invoke();
            }
        }

        private List<HotkeyBinding> LoadCore()
        {
            if (!File.Exists(this._settingsPath))
            {
                return [];
            }

            try
            {
                string json = File.ReadAllText(this._settingsPath);
                HotkeySettingsFile? file = JsonSerializer.Deserialize<HotkeySettingsFile>(json, JsonOptions);
                if (file?.Bindings == null)
                {
                    return [];
                }

                if (file.PausingSyncerHotkey != null)
                {
                    this._pausingSyncerHotkey = file.PausingSyncerHotkey;
                }
                if (file.NudgingSyncerHotkey != null)
                {
                    this._nudgingSyncerHotkey = file.NudgingSyncerHotkey;
                }

                foreach (HotkeyBinding binding in file.Bindings)
                {
                    if (string.IsNullOrWhiteSpace(binding.Id))
                    {
                        binding.Id = Guid.NewGuid().ToString("N");
                    }

                    if (string.IsNullOrWhiteSpace(binding.ActionName))
                    {
                        binding.ActionName = "Unnamed action";
                    }

                    binding.KeyboardHardwareId = HotkeyKeyboardIds.Normalize(binding.KeyboardHardwareId);
                    binding.KeyboardDisplayName ??= string.Empty;
                    binding.HoldThresholdMs = Math.Clamp(
                        binding.HoldThresholdMs,
                        HotkeyDefaults.MinHoldThresholdMs,
                        HotkeyDefaults.MaxHoldThresholdMs);

                    binding.Target ??= new HotkeyActionTarget();
                    if (string.IsNullOrWhiteSpace(binding.Target.ActionKind))
                    {
                        binding.Target.ActionKind = HotkeyActionKinds.ControlGesture;
                    }

                    binding.Target.ControlPath ??= [];
                    binding.Target.MenuItemPath ??= [];
                }

                return file.Bindings;
            }
            catch (Exception ex)
            {
                try
                {
                    File.Copy(this._settingsPath, this._settingsPath + ".corrupt", overwrite: true);
                }
                catch
                {
                }

                System.Diagnostics.Debug.WriteLine($"Hotkeys: could not read settings ({ex.Message}); starting empty.");
                return [];
            }
        }

        private void SaveCore()
        {
            HotkeySettingsFile file = new()
            {
                Bindings = this._bindings,
                PausingSyncerHotkey = this._pausingSyncerHotkey,
                NudgingSyncerHotkey = this._nudgingSyncerHotkey
            };
            string json;
            try
            {
                json = JsonSerializer.Serialize(file, JsonOptions);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Hotkeys: could not serialize settings: {ex.Message}");
                return;
            }

            if (TryWriteSettings(this._settingsPath, json))
            {
                return;
            }

            if (!string.Equals(this._settingsPath, this._fallbackSettingsPath, StringComparison.OrdinalIgnoreCase)
                && TryWriteSettings(this._fallbackSettingsPath, json))
            {
                this._settingsPath = this._fallbackSettingsPath;
                return;
            }

            System.Diagnostics.Debug.WriteLine(
                $"Hotkeys: could not save settings to '{this._settingsPath}'.");
        }

        private static bool TryWriteSettings(string path, string json)
        {
            try
            {
                string? directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string tempPath = path + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, path, overwrite: true);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Hotkeys: could not write settings to '{path}': {ex.Message}");
                return false;
            }
        }

        private static string ResolveSettingsPath(string fallbackPath)
        {
            string appData = string.Empty;
            try
            {
                appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            }
            catch
            {
            }

            if (!string.IsNullOrWhiteSpace(appData))
            {
                string directory = Path.Combine(appData, "ModularAudience", "Hotkeys");
                string path = Path.Combine(directory, "hotkeys.json");

                // A portable fallback file takes precedence while no roaming file exists.
                if (File.Exists(fallbackPath) && !File.Exists(path))
                {
                    return fallbackPath;
                }

                // Keep using an existing settings file even if the writability probe fails,
                // so the user does not silently lose their configuration.
                if (File.Exists(path) || CanWriteTo(directory))
                {
                    return path;
                }
            }

            return fallbackPath;
        }

        private static bool CanWriteTo(string directory)
        {
            try
            {
                Directory.CreateDirectory(directory);
                string probePath = Path.Combine(directory, ".write-probe");
                using (FileStream stream = new(probePath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    stream.WriteByte(0);
                }

                File.Delete(probePath);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
