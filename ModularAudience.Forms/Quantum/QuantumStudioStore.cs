using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModularAudience.Quantum.Models;
using ModularAudience.Quantum.Serialization;

namespace ModularAudience.Forms.Quantum;

internal enum QuantumAuthenticationMode { ApiKey, IamBearerToken }

internal sealed record QuantumConnectionSettings
{
    internal static QuantumConnectionSettings Defaults => new();
    public string ProfileName { get; init; } = "Default";
    public QuantumAuthenticationMode Authentication { get; init; }
    [JsonIgnore] public string Secret { get; init; } = "";
    public string ServiceCrn { get; init; } = "";
    public string Backend { get; init; } = "";
    public string BaseAddress { get; init; } = "https://quantum.cloud.ibm.com/api/v1/";
    public string ApiVersion { get; init; } = "2026-04-15";
    public string PythonExecutable { get; init; } = "python";
    public int RequestTimeoutSeconds { get; init; } = 30;
    public int JobTimeoutSeconds { get; init; } = 300;
    public int PollIntervalMs { get; init; } = 1000;
    public int ReadRetries { get; init; } = 2;

    public override string ToString() => $"Quantum connection profile '{this.ProfileName}' ({this.Authentication}); credential redacted.";

    internal IbmQuantumOptions ToOptions() => new()
    {
        ApiKey = this.Authentication == QuantumAuthenticationMode.ApiKey ? this.Secret : "",
        AccessToken = this.Authentication == QuantumAuthenticationMode.IamBearerToken ? this.Secret : "",
        ServiceCrn = this.ServiceCrn, Backend = this.Backend, BaseAddress = new Uri(this.BaseAddress), ApiVersion = this.ApiVersion,
        RequestTimeout = TimeSpan.FromSeconds(this.RequestTimeoutSeconds), JobTimeout = TimeSpan.FromSeconds(this.JobTimeoutSeconds),
        PollInterval = TimeSpan.FromMilliseconds(this.PollIntervalMs), MaxReadRetries = this.ReadRetries
    };
}

internal sealed record QuantumConnectionDocument(int Version, QuantumConnectionSettings Settings, string ProtectedSecret);

internal sealed record QuantumStudioWorkspace(QuantumExperimentDocument Experiment, int Shots = 1024, bool FreshLocalSeed = false,
    bool AutoSave = true, bool AutoRender = true, bool AutoPlay = false, bool LoopPlayback = true,
    bool UseIbm = false, bool UsePreparedIsa = false, string PreparedIsa = "", int OptimizationLevel = 1,
    bool AutoQubits = false, string CircuitPreset = "Bell break", string ConnectionProfile = "Default",
    string? LoadedExperimentName = null, int SelectedTab = 0, int WindowWidth = 1360, int WindowHeight = 900);

internal sealed record StoredQuantumExperiment(int Version, string Name, DateTimeOffset SavedAtUtc, QuantumStudioWorkspace Workspace);

internal interface IQuantumSecretProtector
{
    string Protect(string secret);
    string Unprotect(string ciphertext);
}

internal sealed class WindowsQuantumSecretProtector : IQuantumSecretProtector
{
    private static readonly byte[] Purpose = Encoding.UTF8.GetBytes("ModularAudience.QuantumBeatEngine.credentials.v1");
    public string Protect(string secret)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(secret);
        try { return "dpapi:v1:" + Convert.ToBase64String(ProtectedData.Protect(bytes, Purpose, DataProtectionScope.CurrentUser)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public string Unprotect(string ciphertext)
    {
        if (!ciphertext.StartsWith("dpapi:v1:", StringComparison.Ordinal)) throw new CryptographicException("Unsupported quantum credential protection format.");
        byte[] bytes = ProtectedData.Unprotect(Convert.FromBase64String(ciphertext[9..]), Purpose, DataProtectionScope.CurrentUser);
        try { return Encoding.UTF8.GetString(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}

/// <summary>Atomic, versioned AppData JSON. Experiments never contain plaintext or encrypted credentials.</summary>
internal sealed class QuantumStudioStore
{
    internal static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ModularAudience", "QuantumBeatEngine");
    private readonly string directory;
    private readonly IQuantumSecretProtector protector;
    internal QuantumStudioStore(string? directory = null, IQuantumSecretProtector? protector = null)
    {
        this.directory = directory ?? DefaultDirectory;
        this.protector = protector ?? new WindowsQuantumSecretProtector();
    }
    internal string RootDirectory => this.directory;

    internal QuantumStudioWorkspace? LoadWorkspace()
    {
        string path = Path.Combine(this.directory, "workspace.json");
        return File.Exists(path) ? ValidateWorkspace(JsonSerializer.Deserialize(Read(path), QuantumStudioJsonContext.Default.QuantumStudioWorkspace)) : null;
    }

    internal void SaveWorkspace(QuantumStudioWorkspace workspace)
    {
        ValidateWorkspace(workspace);
        AtomicWrite(Path.Combine(this.directory, "workspace.json"), JsonSerializer.Serialize(workspace, QuantumStudioJsonContext.Default.QuantumStudioWorkspace));
    }

    internal void SaveRecoveryWorkspace(QuantumStudioWorkspace workspace, Guid sessionId)
    {
        ValidateWorkspace(workspace);
        AtomicWrite(Path.Combine(this.directory, "Recovery", "workspace-" + sessionId.ToString("N") + ".json"),
            JsonSerializer.Serialize(workspace, QuantumStudioJsonContext.Default.QuantumStudioWorkspace));
    }

    internal string[] ExperimentNames() => this.Names("Experiments", "experiment-");
    internal string[] ConnectionNames() => this.Names("Connections", "connection-");

    internal void SaveExperiment(string name, QuantumStudioWorkspace workspace)
    {
        ValidateWorkspace(workspace);
        _ = QuantumExperimentSerializer.Serialize(workspace.Experiment); // Named presets must be executable, unlike a work-in-progress workspace.
        var stored = new StoredQuantumExperiment(1, ValidateName(name), DateTimeOffset.UtcNow, workspace with { LoadedExperimentName = name });
        AtomicWrite(this.PathFor("Experiments", "experiment-", name), JsonSerializer.Serialize(stored, QuantumStudioJsonContext.Default.StoredQuantumExperiment));
    }

    internal QuantumStudioWorkspace LoadExperiment(string name)
    {
        var stored = JsonSerializer.Deserialize(Read(this.PathFor("Experiments", "experiment-", name)), QuantumStudioJsonContext.Default.StoredQuantumExperiment);
        if (stored is null || stored.Version != 1 || !string.Equals(stored.Name, name, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The saved quantum experiment has an unsupported format.");
        var workspace = ValidateWorkspace(stored.Workspace);
        _ = QuantumExperimentSerializer.Serialize(workspace.Experiment);
        return workspace with { LoadedExperimentName = name };
    }

    internal void RenameExperiment(string oldName, string newName)
    {
        string oldPath = this.PathFor("Experiments", "experiment-", oldName), newPath = this.PathFor("Experiments", "experiment-", newName);
        if (File.Exists(newPath)) throw new IOException("An experiment with that name already exists.");
        var workspace = this.LoadExperiment(oldName);
        this.SaveExperiment(newName, workspace with { LoadedExperimentName = newName });
        File.Delete(oldPath);
    }

    internal void DeleteExperiment(string name) => File.Delete(this.PathFor("Experiments", "experiment-", name));

    internal void SaveConnection(QuantumConnectionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ValidateName(settings.ProfileName);
        if (settings.Secret.Length > 65_536) throw new ArgumentException("The quantum credential is too long.");
        var document = new QuantumConnectionDocument(1, settings with { Secret = "" }, this.protector.Protect(settings.Secret));
        AtomicWrite(this.PathFor("Connections", "connection-", settings.ProfileName), JsonSerializer.Serialize(document, QuantumStudioJsonContext.Default.QuantumConnectionDocument));
    }

    internal QuantumConnectionSettings LoadConnection(string name)
    {
        var stored = JsonSerializer.Deserialize(Read(this.PathFor("Connections", "connection-", name)), QuantumStudioJsonContext.Default.QuantumConnectionDocument);
        if (stored is null || stored.Version != 1 || stored.Settings is null || !Enum.IsDefined(stored.Settings.Authentication) ||
            !string.Equals(stored.Settings.ProfileName, name, StringComparison.OrdinalIgnoreCase) || stored.ProtectedSecret is null || stored.ProtectedSecret.Length > 131_072)
            throw new InvalidDataException("The connection profile has an unsupported format.");
        return stored.Settings with { Secret = this.protector.Unprotect(stored.ProtectedSecret) };
    }

    internal void DeleteConnection(string name) => File.Delete(this.PathFor("Connections", "connection-", name));

    private string[] Names(string folder, string prefix)
    {
        string path = Path.Combine(this.directory, folder);
        return Directory.Exists(path) ? Directory.GetFiles(path, prefix + "*.json").Select(file => Path.GetFileNameWithoutExtension(file)[prefix.Length..])
            .Order(StringComparer.OrdinalIgnoreCase).ToArray() : [];
    }

    private string PathFor(string folder, string prefix, string name) => Path.Combine(this.directory, folder, prefix + ValidateName(name) + ".json");
    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name != name.Trim() || name.Length > 80 || name is "." or ".." ||
            name.EndsWith('.') || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Use a name of 1 to 80 characters without file-name special characters or trailing spaces/dots.");
        return name;
    }

    private static QuantumStudioWorkspace ValidateWorkspace(QuantumStudioWorkspace? workspace)
    {
        if (workspace is null || workspace.Experiment is null || workspace.Experiment.Version != 1 ||
            workspace.Experiment.Circuit is null || workspace.Experiment.Circuit.Gates is null || workspace.Experiment.Options is null ||
            workspace.Experiment.Tracks is null || workspace.Experiment.Randomization is null ||
            workspace.Experiment.Circuit.QubitCount is < 4 or > 16 || workspace.Experiment.Circuit.Gates.Count > 512 ||
            workspace.Experiment.Tracks.Count > 32 || workspace.Shots is < 1 or > 1_000_000 ||
            workspace.PreparedIsa is null || workspace.PreparedIsa.Length > 524_288 || workspace.OptimizationLevel is < 0 or > 3 ||
            workspace.Experiment.Circuit.Gates.Any(gate => gate is null || !Enum.IsDefined(gate.Kind) || gate.Qubit is < 0 or > 15 ||
                gate.Target is < 0 or > 15 || !double.IsFinite(gate.AngleRadians)))
            throw new InvalidDataException("The quantum workspace has an unsupported version or invalid settings.");
        return workspace;
    }

    private static string Read(string path)
    {
        if (new FileInfo(path).Length > 1_048_576) throw new InvalidDataException("Quantum settings files must not exceed 1 MiB.");
        return File.ReadAllText(path);
    }

    private static void AtomicWrite(string path, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, json, new UTF8Encoding(false)); File.Move(temporary, path, overwrite: true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
