using System.Text.Json.Serialization;

namespace ModularAudience.Quantum.Serialization;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(IbmJobSubmission))]
[JsonSerializable(typeof(IbmJobStatus))]
[JsonSerializable(typeof(IbmIamToken))]
[JsonSerializable(typeof(Dictionary<string, int>))]
internal partial class QuantumJsonContext : JsonSerializerContext;

internal sealed record IbmJobSubmission(string Backend, string ProgramId, IbmSamplerParameters Params);
internal sealed record IbmSamplerParameters(string[][] Pubs, int Shots, int Version = 2, bool SupportQiskit = false);
internal sealed record IbmJobStatus(string? Id, string? Status, IbmJobState? State);
internal sealed record IbmJobState(string? Status, string? Reason);
internal sealed record IbmIamToken(string? AccessToken, int ExpiresIn);
