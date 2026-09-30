using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;
using ModularAudience.Forms.ControlsConfig;

namespace ModularAudience.Forms.ControlsConfig
{
    public sealed class ControlsSettingsManager
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly string _settingsPath;
        private ControlScheme _currentScheme;
        private readonly object _lock = new();

        public ControlsSettingsManager()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string dir = Path.Combine(appData, "ModularAudience", "BreakbeatPatternEditor");
            Directory.CreateDirectory(dir);
            _settingsPath = Path.Combine(dir, "controls-config.json");
        }

        public ControlScheme CurrentScheme
        {
            get
            {
                lock (_lock)
                {
                    if (_currentScheme == null)
                        _currentScheme = LoadOrCreateDefault();
                    return _currentScheme;
                }
            }
        }

        public event Action<ControlScheme>? SchemeChanged;

        private ControlScheme LoadOrCreateDefault()
        {
            if (File.Exists(_settingsPath))
            {
                try
                {
                    string json = File.ReadAllText(_settingsPath);
                    var scheme = JsonSerializer.Deserialize<ControlScheme>(json, JsonOptions);
                    if (scheme != null)
                    {
                        scheme.RebuildIndex();
                        MergeWithDefaults(scheme);
                        return scheme;
                    }
                }
                catch
                {
                    // Ignore corrupt settings, fall back to defaults
                }
            }
            return DefaultControlScheme.Create();
        }

        private void MergeWithDefaults(ControlScheme loaded)
        {
            var defaults = DefaultControlScheme.Create();
            var loadedIds = new HashSet<string>(loaded.Bindings.ConvertAll(b => b.ActionId));

            foreach (var def in defaults.Bindings)
            {
                if (!loadedIds.Contains(def.ActionId))
                {
                    loaded.Bindings.Add(def.Clone());
                }
                else
                {
                    var existing = loaded.BindingsById[def.ActionId];
                    existing.IsHardcoded = def.IsHardcoded;
                    existing.AllowRemapping = def.AllowRemapping;
                    existing.ValidScopes = def.ValidScopes;
                }
            }

            loaded.Bindings.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
            loaded.RebuildIndex();
        }

        public void Save()
        {
            lock (_lock)
            {
                if (_currentScheme == null) return;
                _currentScheme.Bindings.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
                string json = JsonSerializer.Serialize(_currentScheme, JsonOptions);
                File.WriteAllText(_settingsPath, json);
            }
        }

        public bool TryUpdateBinding(string actionId, ControlBinding newBinding, out List<ControlBinding> conflicts)
        {
            lock (_lock)
            {
                if (!_currentScheme.BindingsById.TryGetValue(actionId, out var existing))
                {
                    conflicts = new List<ControlBinding>();
                    return false;
                }

                if (existing.IsHardcoded && !existing.AllowRemapping)
                {
                    conflicts = new List<ControlBinding>();
                    return false;
                }

                var testScheme = _currentScheme.Clone();
                var testBinding = testScheme.BindingsById[actionId];
                CopyBindingData(newBinding, testBinding);
                testScheme.RebuildIndex();

                conflicts = testScheme.GetConflicts(testBinding);

                if (conflicts.Count > 0)
                {
                    return false;
                }

                CopyBindingData(newBinding, existing);
                _currentScheme.RebuildIndex();
                Save();
                SchemeChanged?.Invoke(_currentScheme);
                return true;
            }
        }

        public void ForceUpdateBinding(string actionId, ControlBinding newBinding, List<ControlBinding> clearedConflicts)
        {
            lock (_lock)
            {
                if (!_currentScheme.BindingsById.TryGetValue(actionId, out var existing)) return;

                foreach (var conflict in clearedConflicts)
                {
                    ClearBinding(conflict.ActionId);
                }

                CopyBindingData(newBinding, existing);
                _currentScheme.RebuildIndex();
                Save();
                SchemeChanged?.Invoke(_currentScheme);
            }
        }

        private void ClearBinding(string actionId)
        {
            if (_currentScheme.BindingsById.TryGetValue(actionId, out var binding))
            {
                binding.PrimaryInput = InputType.None;
                binding.PrimaryKey = Keys.None;
                binding.PrimaryModifiers = ModifierKey.None;
                binding.PrimaryMouseAction = MouseAction.Click;
                binding.SecondaryInput = InputType.None;
                binding.SecondaryKey = Keys.None;
                binding.SecondaryModifiers = ModifierKey.None;
                binding.SecondaryMouseAction = MouseAction.Click;
            }
        }

        private static void CopyBindingData(ControlBinding from, ControlBinding to)
        {
            to.PrimaryInput = from.PrimaryInput;
            to.PrimaryKey = from.PrimaryKey;
            to.PrimaryModifiers = from.PrimaryModifiers;
            to.PrimaryMouseAction = from.PrimaryMouseAction;
            to.SecondaryInput = from.SecondaryInput;
            to.SecondaryKey = from.SecondaryKey;
            to.SecondaryModifiers = from.SecondaryModifiers;
            to.SecondaryMouseAction = from.SecondaryMouseAction;
        }

        public void ResetToDefaults()
        {
            lock (_lock)
            {
                _currentScheme = DefaultControlScheme.Create();
                Save();
                SchemeChanged?.Invoke(_currentScheme);
            }
        }

        public ControlScheme Clone()
        {
            lock (_lock)
            {
                var clone = new ControlScheme { Name = _currentScheme.Name };
                foreach (var b in _currentScheme.Bindings)
                    clone.Bindings.Add(b.Clone());
                clone.RebuildIndex();
                return clone;
            }
        }
    }

    public static class ControlSchemeExtensions
    {
        public static ControlScheme Clone(this ControlScheme scheme)
        {
            var clone = new ControlScheme { Name = scheme.Name };
            foreach (var b in scheme.Bindings)
                clone.Bindings.Add(b.Clone());
            clone.RebuildIndex();
            return clone;
        }
    }
}