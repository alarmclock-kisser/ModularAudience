using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModularAudience.Audio.Vocal;

namespace ModularAudience.Llama;

/// <summary>
/// Persisted configuration for the VocalSynth live LLM stream (local SSE proxy).
/// </summary>
public sealed class VocalLlmStreamSettings
{
    /// <summary>Protector used when persisting the API key. Replaceable in tests.</summary>
    [JsonIgnore]
    public static ILlmSecretProtector Protector { get; set; } = new WindowsLlmSecretProtector();

    /// <summary>Bearer token for the llama-server. Never persisted in plaintext.</summary>
    [JsonIgnore]
    public string ApiKey { get; set; } = "";

    /// <summary>DPAPI-protected form of <see cref="ApiKey"/> that is written to disk.</summary>
    public string ProtectedApiKey { get; set; } = "";

    /// <summary>The real llama-server address. The web UI is pointed at <see cref="ListenPort"/> instead.</summary>
    public string UpstreamUrl { get; set; } = "http://127.0.0.1:9931";

    /// <summary>Local port on which the SSE tap proxy listens.</summary>
    public int ListenPort { get; set; } = 8081;

    /// <summary>Whether <see cref="ModelFilter"/> is applied at all.</summary>
    public bool UseModelFilter { get; set; }

    /// <summary>Optional case-insensitive model name filter. Empty means every model.</summary>
    public string ModelFilter { get; set; } = "";

    public int MinWords { get; set; } = 3;
    public int MaxWords { get; set; } = 14;
    public bool BreakOnComma { get; set; } = true;
    public bool BreakOnColon { get; set; } = true;
    public bool BreakOnSentenceEnd { get; set; } = true;

    /// <summary>Maximum waiting time for a partial chunk before it is spoken anyway.</summary>
    public int FlushMilliseconds { get; set; } = 700;

    /// <summary>Minimum audio buffer level. Below this the reading speed is scaled down.</summary>
    public int MinBufferMilliseconds { get; set; } = 900;

    /// <summary>Slow down future chunks when the token stream cannot keep up with the set speed.</summary>
    public bool AdaptiveSpeed { get; set; } = true;

    /// <summary>Lowest speed multiplier used by adaptive speed (0.6 = 60% of the VocalSynth speed).</summary>
    public double MinSpeedFactor { get; set; } = 0.6;

    public bool Enabled { get; set; }

    public static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ModularAudience", "vocal-llm-stream-settings.json");

    public void Normalize()
    {
        UpstreamUrl = string.IsNullOrWhiteSpace(UpstreamUrl) ? "http://127.0.0.1:8080" : UpstreamUrl.Trim();
        if (!Uri.TryCreate(UpstreamUrl, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            UpstreamUrl = "http://127.0.0.1:9931";
        }

        ListenPort = Math.Clamp(ListenPort, 1, 65535);
        ModelFilter ??= "";
        MinWords = Math.Clamp(MinWords, 1, 100);
        MaxWords = Math.Clamp(MaxWords, MinWords, 500);
        FlushMilliseconds = Math.Clamp(FlushMilliseconds, 100, 10000);
        MinBufferMilliseconds = Math.Clamp(MinBufferMilliseconds, 100, 10000);
        if (!double.IsFinite(MinSpeedFactor))
        {
            MinSpeedFactor = 0.6;
        }

        MinSpeedFactor = Math.Clamp(MinSpeedFactor, 0.25, 1.0);
    }

    public VocalStreamChunkingSettings CreateChunkingSettings() => new()
    {
        MinWords = MinWords,
        MaxWords = MaxWords,
        BreakOnComma = BreakOnComma,
        BreakOnColon = BreakOnColon,
        BreakOnSentenceEnd = BreakOnSentenceEnd,
    };

    public VocalLlmStreamSettings Clone()
    {
        var copy = JsonSerializer.Deserialize<VocalLlmStreamSettings>(JsonSerializer.Serialize(this))!;
        copy.ApiKey = this.ApiKey;
        return copy;
    }

    public static VocalLlmStreamSettings Load(string? path = null)
    {
        path ??= SettingsPath;
        VocalLlmStreamSettings result = File.Exists(path)
            ? JsonSerializer.Deserialize<VocalLlmStreamSettings>(File.ReadAllText(path)) ?? new()
            : new();
        result.Normalize();
        try
        {
            result.ApiKey = LlamaHttp.NormalizeApiKey(Protector.Unprotect(result.ProtectedApiKey));
        }
        catch (CryptographicException)
        {
            // A key bound to another user/machine is unusable; keep the rest of the settings.
            result.ApiKey = "";
        }

        return result;
    }

    public void Save(string? path = null)
    {
        path ??= SettingsPath;
        Normalize();
        ProtectedApiKey = Protector.Protect(LlamaHttp.NormalizeApiKey(ApiKey));
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
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
