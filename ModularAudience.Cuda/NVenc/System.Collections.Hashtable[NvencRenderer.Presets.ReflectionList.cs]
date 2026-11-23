#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

namespace ModularAudience.Cuda
{
    public static partial class NvencVideoRenderer
    {
        public sealed record PresetEntry(string Name, NvencOptions Options)
        {
            public string DisplayName { get; } = ExtractDisplayName(Name, true);

            public override string ToString() => this.DisplayName;

            private static string ExtractDisplayName(string name, bool toUpper = false)
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    return string.Empty;
                }

                var parts = name.Split('.', StringSplitOptions.RemoveEmptyEntries);
                string displayName = parts.Length > 0 ? parts[^1] : name;
                return toUpper ? displayName.ToUpperInvariant() : displayName;
            }
        }

        public static BindingList<PresetEntry> GetAllPresetEntries(bool showCudaCodecs = true)
        {
            var entries = new List<PresetEntry>();

            void AddFromType(Type t)
            {
                const BindingFlags flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;

                foreach (var p in t.GetProperties(flags))
                {
                    if (p.PropertyType != typeof(NvencOptions) || p.GetMethod is null)
                    {
                        continue;
                    }

                    if (p.GetValue(null) is NvencOptions val)
                    {
                        entries.Add(new PresetEntry($"{t.Name}.{p.Name}", val));
                    }
                }

                foreach (var f in t.GetFields(flags))
                {
                    if (f.FieldType != typeof(NvencOptions))
                    {
                        continue;
                    }

                    if (f.GetValue(null) is NvencOptions val)
                    {
                        entries.Add(new PresetEntry($"{t.Name}.{f.Name}", val));
                    }
                }
            }

            var root = typeof(NvencVideoRenderer);
            AddFromType(root);

            foreach (var nt in root.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            {
                AddFromType(nt);
            }

            entries = entries
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!showCudaCodecs)
            {
                entries = entries
                    .Where(e => !e.Options.VideoCodec.Contains("nv", StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            return new BindingList<PresetEntry>(entries);
        }

        public static BindingList<NvencOptions> GetAllPresetOptions()
        {
            var list = GetAllPresetEntries().Select(e => e.Options).ToList();
            return new BindingList<NvencOptions>(list);
        }
    }
}
