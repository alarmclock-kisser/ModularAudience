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

        private readonly string _settingsPath;
        private readonly object _lock = new();
        private List<HotkeyBinding> _bindings = [];

        public HotkeySettingsManager()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string dir = Path.Combine(appData, "ModularAudience", "Hotkeys");
            Directory.CreateDirectory(dir);
            this._settingsPath = Path.Combine(dir, "hotkeys.json");
            this._bindings = this.LoadCore();
        }

        public string SettingsPath => this._settingsPath;

        public event Action? Changed;

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

        public int CopyProfile(
            string sourceHardwareId,
            string sourceDisplayName,
            string destinationHardwareId,
            string destinationDisplayName)
        {
            string source = HotkeyKeyboardIds.Normalize(sourceHardwareId);
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
                    RelativeY = source.Target.RelativeY
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
            try
            {
                HotkeySettingsFile file = new() { Bindings = this._bindings };
                string json = JsonSerializer.Serialize(file, JsonOptions);

                string tempPath = this._settingsPath + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Copy(tempPath, this._settingsPath, overwrite: true);
                File.Delete(tempPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Hotkeys: could not save settings: {ex.Message}");
            }
        }
    }
}
