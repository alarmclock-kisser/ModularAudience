using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ModularAudience.Audio.Synthesis.Runtime;

/// <summary>
/// Persisted store for custom instrument synth descriptors under
/// %appdata%\ModularAudience\CustomInstruments\. Mirrors the InstrumentSynthStore pattern
/// (atomic writes, versioned schema, name validation).
/// </summary>
public sealed class CustomInstrumentStore
{
    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ModularAudience", "CustomInstruments");

    public static CustomInstrumentStore Shared { get; } = new();
    public string DirectoryPath { get; }

    private readonly object gate = new();
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public event Action? InstrumentsChanged;

    public CustomInstrumentStore(string? directory = null) => this.DirectoryPath = directory ?? DefaultDirectory;

    public string[] Names()
    {
        lock (this.gate)
        {
            return Directory.Exists(this.DirectoryPath)
                ? Directory.GetFiles(this.DirectoryPath, "*.json")
                    .Select(Path.GetFileNameWithoutExtension)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                : [];
        }
    }

    public CustomInstrumentDescriptor Load(string name)
    {
        ValidateName(name);
        string path = InstrumentPath(name);
        if (!File.Exists(path))
        {
            throw new ArgumentException("Custom instrument not found.");
        }

        lock (this.gate)
        {
            var descriptor = JsonSerializer.Deserialize<CustomInstrumentDescriptor>(File.ReadAllText(path), Json)
                ?? throw new InvalidDataException("Empty custom instrument file.");
            descriptor.UpdatedAt = DateTime.UtcNow;
            return descriptor;
        }
    }

    public void Save(CustomInstrumentDescriptor descriptor)
    {
        if (descriptor == null)
        {
            throw new ArgumentNullException(nameof(descriptor));
        }

        ValidateName(descriptor.Name);
        descriptor.Name = descriptor.Name.Trim();
        descriptor.UpdatedAt = DateTime.UtcNow;
        if (descriptor.CreatedAt == default)
        {
            descriptor.CreatedAt = DateTime.UtcNow;
        }

        string path = InstrumentPath(descriptor.Name);
        lock (this.gate)
        {
            WriteAtomic(path, descriptor);
        }
        this.InstrumentsChanged?.Invoke();
    }

    public void Delete(string name)
    {
        ValidateName(name);
        string path = InstrumentPath(name);
        lock (this.gate)
        {
            if (!File.Exists(path))
            {
                throw new ArgumentException("Custom instrument not found.");
            }
            File.Delete(path);
        }
        this.InstrumentsChanged?.Invoke();
    }

    public void Rename(string oldName, string newName)
    {
        ValidateName(newName);
        newName = newName.Trim();
        string oldPath = InstrumentPath(oldName);
        string newPath = InstrumentPath(newName);
        lock (this.gate)
        {
            if (!File.Exists(oldPath))
            {
                throw new ArgumentException("Custom instrument not found.");
            }
            if (File.Exists(newPath))
            {
                throw new ArgumentException("Another custom instrument already has this name.");
            }
            File.Move(oldPath, newPath);
        }
        this.InstrumentsChanged?.Invoke();
    }

    private static string InstrumentPath(string name) =>
        Path.Combine(DefaultDirectory, SanitizeFileName(name) + ".json");

    private static string SanitizeFileName(string name)
    {
        var invalid = new string(Path.GetInvalidFileNameChars());
        var regex = new Regex($"[{Regex.Escape(invalid)}]");
        return regex.Replace(name, "_");
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100 || name.Any(char.IsControl))
        {
            throw new ArgumentException("Enter a custom instrument name of 1–100 characters without control characters.");
        }
    }

    private static void WriteAtomic<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(value, Json));
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
