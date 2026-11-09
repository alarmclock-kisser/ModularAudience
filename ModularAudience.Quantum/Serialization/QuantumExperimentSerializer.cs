using System.Text.Json;
using System.Text.Json.Serialization;
using ModularAudience.Quantum.Models;
using ModularAudience.Quantum.Services;

namespace ModularAudience.Quantum.Serialization;

public static class QuantumExperimentSerializer
{
    public static string Serialize(QuantumExperimentDocument document)
    {
        Validate(document);
        return JsonSerializer.Serialize(document, QuantumExperimentJsonContext.Default.QuantumExperimentDocument);
    }

    public static QuantumExperimentDocument Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        if (json.Length > 1_048_576) throw new ArgumentException("Quantum project files must not exceed 1 MiB.");
        var document = JsonSerializer.Deserialize(json, QuantumExperimentJsonContext.Default.QuantumExperimentDocument)
            ?? throw new JsonException("The quantum project is empty.");
        Validate(document);
        return document;
    }

    private static void Validate(QuantumExperimentDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Version != 1 || document.Circuit is null || document.Options is null || document.Randomization is null)
            throw new ArgumentException("This quantum project version or document structure is not supported.");
        _ = QuantumCircuitComposer.Build(document.Circuit);
        document.Options.Validate(document.Circuit.QubitCount, document.Tracks);
        _ = QuantumCircuitComposer.Randomize(document.Circuit.QubitCount, document.Randomization);
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(QuantumExperimentDocument))]
internal partial class QuantumExperimentJsonContext : JsonSerializerContext;
