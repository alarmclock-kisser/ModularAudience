using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;

namespace ModularAudience.Forms.ControlsConfig
{
    /// <summary>
    /// Owns the persisted control scheme. The file lives in
    /// %appdata%\ModularAudience\BreakbeatPatternEditor\controls-config.json
    /// and is the single source of truth for the Breakbeat Pattern Editor bindings.
    /// </summary>
    public sealed class ControlsSettingsManager
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly string _settingsPath;
        private readonly object _lock = new();
        private ControlScheme _currentScheme;

        public ControlsSettingsManager()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string dir = Path.Combine(appData, "ModularAudience", "BreakbeatPatternEditor");
            Directory.CreateDirectory(dir);
            _settingsPath = Path.Combine(dir, "controls-config.json");
            _currentScheme = LoadOrCreateDefault();
        }

        /// <summary>Absolute path of the persisted settings file (shown in the dialog).</summary>
        public string SettingsPath => _settingsPath;

        public ControlScheme CurrentScheme
        {
            get
            {
                lock (_lock)
                {
                    return _currentScheme;
                }
            }
        }

        public event Action<ControlScheme>? SchemeChanged;

        private ControlScheme LoadOrCreateDefault()
        {
            if (!File.Exists(_settingsPath))
            {
                return DefaultControlScheme.Create();
            }

            try
            {
                string json = File.ReadAllText(_settingsPath);
                var scheme = JsonSerializer.Deserialize<ControlScheme>(json, JsonOptions);
                if (scheme?.Bindings is { Count: > 0 })
                {
                    scheme.RebuildIndex();
                    MergeWithDefaults(scheme);
                    return scheme;
                }
            }
            catch (Exception ex)
            {
                // Keep the broken file around for inspection, then fall back to defaults.
                TryBackupCorruptFile();
                System.Diagnostics.Debug.WriteLine($"ControlsConfig: could not read settings ({ex.Message}); using defaults.");
            }

            return DefaultControlScheme.Create();
        }

        private void TryBackupCorruptFile()
        {
            try
            {
                File.Copy(_settingsPath, _settingsPath + ".corrupt", overwrite: true);
            }
            catch { }
        }

        /// <summary>
        /// Keeps user customisations but refreshes metadata (names, scopes, hardcoded flags,
        /// sort order) and appends actions that were added since the file was written.
        /// </summary>
        private static void MergeWithDefaults(ControlScheme loaded)
        {
            var defaults = DefaultControlScheme.Create();
            var known = new HashSet<string>(loaded.Bindings.Select(b => b.ActionId), StringComparer.Ordinal);

            foreach (var def in defaults.Bindings)
            {
                if (!known.Add(def.ActionId))
                {
                    continue;
                }

                loaded.Bindings.Add(def.Clone());
            }

            foreach (var def in defaults.Bindings)
            {
                var existing = loaded.Bindings.FirstOrDefault(b => b.ActionId == def.ActionId);
                if (existing == null)
                {
                    continue;
                }

                // Presentation/metadata always comes from the code, never from the file.
                existing.DisplayName = def.DisplayName;
                existing.Description = def.Description;
                existing.Category = def.Category;
                existing.ValidScopes = def.ValidScopes;
                existing.IsHardcoded = def.IsHardcoded;
                existing.AllowRemapping = def.AllowRemapping;
                existing.Implemented = def.Implemented;
                existing.SortOrder = def.SortOrder;
            }

            loaded.Bindings.Sort(CompareBindings);
            loaded.RebuildIndex();
        }

        private static int CompareBindings(ControlBinding a, ControlBinding b)
        {
            int byCategory = a.Category.CompareTo(b.Category);
            if (byCategory != 0)
            {
                return byCategory;
            }

            int byOrder = a.SortOrder.CompareTo(b.SortOrder);
            return byOrder != 0 ? byOrder : string.CompareOrdinal(a.ActionId, b.ActionId);
        }

        /// <summary>
        /// Applies a complete scheme atomically.
        /// Duplicate gestures are resolved deterministically: the action the user just edited
        /// keeps the gesture and the previous owner is unbound. Without a preference the action
        /// further down the display order wins. Every action that lost its binding is reported.
        /// </summary>
        public IReadOnlyList<ControlBinding> Apply(ControlScheme scheme, IReadOnlyCollection<string>? preferredActionIds = null)
        {
            List<ControlBinding> unbound = new();
            HashSet<string> preferred = preferredActionIds != null
                ? new HashSet<string>(preferredActionIds, StringComparer.Ordinal)
                : [];

            lock (_lock)
            {
                var defaults = DefaultControlScheme.Create();

                foreach (var def in defaults.Bindings)
                {
                    if (!scheme.Bindings.Any(b => b.ActionId == def.ActionId))
                    {
                        scheme.Bindings.Add(def.Clone());
                    }
                }

                // Read-only actions always fall back to their built-in gesture.
                foreach (var def in defaults.Bindings)
                {
                    if (!def.IsHardcoded || def.AllowRemapping)
                    {
                        continue;
                    }

                    var target = scheme.Bindings.First(b => b.ActionId == def.ActionId);
                    target.ClearBindings();
                    target.PrimaryInput = def.PrimaryInput;
                    target.PrimaryKey = def.PrimaryKey;
                    target.PrimaryModifiers = def.PrimaryModifiers;
                    target.PrimaryMouseAction = def.PrimaryMouseAction;
                    target.SecondaryInput = def.SecondaryInput;
                    target.SecondaryKey = def.SecondaryKey;
                    target.SecondaryModifiers = def.SecondaryModifiers;
                    target.SecondaryMouseAction = def.SecondaryMouseAction;
                }

                scheme.Bindings.Sort(CompareBindings);
                scheme.RebuildIndex();

                // Two passes: detect every clash against a clean scheme, then unbind the loser.
                for (int i = 0; i < scheme.Bindings.Count; i++)
                {
                    var current = scheme.Bindings[i];
                    for (int j = 0; j < i; j++)
                    {
                        var earlier = scheme.Bindings[j];
                        if (!current.ConflictsWith(earlier))
                        {
                            continue;
                        }

                        // Decide which of the two keeps the gesture: the one the user just
                        // touched, otherwise the later one in display order (== current).
                        bool currentWins = !preferred.Contains(earlier.ActionId)
                                           || (preferred.Contains(current.ActionId) && preferred.Count == 1);
                        if (preferred.Count > 1)
                        {
                            currentWins = preferred.Contains(current.ActionId);
                        }

                        ControlBinding winner = currentWins ? current : earlier;
                        ControlBinding loser = currentWins ? earlier : current;

                        UnbindSlot(loser, winner);
                        if (!unbound.Contains(loser))
                        {
                            unbound.Add(loser);
                        }
                    }
                }

                _currentScheme = scheme;
                _currentScheme.RebuildIndex();
                SaveCore();
            }

            SchemeChanged?.Invoke(CurrentScheme);
            return unbound;
        }

        /// <summary>Strips whichever slot of <paramref name="loser"/> clashes with <paramref name="winner"/>.</summary>
        private static void UnbindSlot(ControlBinding loser, ControlBinding winner)
        {
            if (GesturesMatch(winner.PrimaryInput, winner.PrimaryKey, winner.PrimaryModifiers, winner.PrimaryMouseAction,
                              loser.PrimaryInput, loser.PrimaryKey, loser.PrimaryModifiers, loser.PrimaryMouseAction))
            {
                loser.PrimaryInput = InputType.None;
                loser.PrimaryKey = Keys.None;
                loser.PrimaryModifiers = ModifierKey.None;
                loser.PrimaryMouseAction = MouseAction.Click;
            }
            else
            {
                loser.SecondaryInput = InputType.None;
                loser.SecondaryKey = Keys.None;
                loser.SecondaryModifiers = ModifierKey.None;
                loser.SecondaryMouseAction = MouseAction.Click;
            }
        }

        private static bool GesturesMatch(InputType aInput, Keys aKey, ModifierKey aMods, MouseAction aMouse,
                                          InputType bInput, Keys bKey, ModifierKey bMods, MouseAction bMouse)
        {
            if (aInput == InputType.None || bInput == InputType.None || aInput != bInput || aMods != bMods)
            {
                return false;
            }

            return aInput switch
            {
                InputType.Key or InputType.KeyCombo => aKey == bKey,
                InputType.MouseWheel => aMouse == bMouse,
                _ => aMouse == bMouse
            };
        }

        public void ResetToDefaults()
        {
            Apply(DefaultControlScheme.Create());
        }

        public ControlScheme CreateWorkingCopy()
        {
            lock (_lock)
            {
                return _currentScheme.Clone();
            }
        }

        private void SaveCore()
        {
            try
            {
                _currentScheme.Bindings.Sort(CompareBindings);
                _currentScheme.RebuildIndex();

                string json = JsonSerializer.Serialize(_currentScheme, JsonOptions);

                // Write to a temp file first so a crash can never leave a half-written config.
                string tempPath = _settingsPath + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Copy(tempPath, _settingsPath, overwrite: true);
                File.Delete(tempPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ControlsConfig: could not save settings: {ex.Message}");
            }
        }
    }
}
