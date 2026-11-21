using System.Net.Http.Headers;
using System.Text.Json;

namespace ModularAudience.VocalSynth.Android.Services;

internal sealed record AndroidBuildEntry(
    int BuildNumber,
    string CommitMessage,
    string? CommitSha,
    DateTime CreatedAt,
    string? Conclusion);

internal sealed class AndroidBuildHistoryService
{
    private const string WorkflowRunsEndpoint =
        "https://api.github.com/repos/alarmclock-kisser/ModularAudience/actions/workflows/build.yml/runs?per_page=100";
    private const int MaxEntries = 24;
    private static readonly HttpClient HttpClient = CreateHttpClient();

    internal async Task<IReadOnlyList<AndroidBuildEntry>> GetRecentApkBuildsAsync(
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await HttpClient.GetAsync(
            WorkflowRunsEndpoint,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken);

        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("workflow_runs", out JsonElement runs) ||
            runs.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("GitHub returned an invalid build history.");
        }

        List<AndroidBuildEntry> entries = [];
        HashSet<string> seenCommits = [];
        foreach (JsonElement run in runs.EnumerateArray())
        {
            if (entries.Count >= MaxEntries)
                break;
            if (run.ValueKind != JsonValueKind.Object)
                continue;

            string? sha = GetStringProperty(run, "head_sha");
            if (sha != null && !seenCommits.Add(sha))
                continue;

            if (!run.TryGetProperty("run_number", out JsonElement runNumberElement) ||
                runNumberElement.ValueKind != JsonValueKind.Number ||
                !runNumberElement.TryGetInt32(out int buildNumber) ||
                buildNumber <= 0)
            {
                continue;
            }

            string message = run.TryGetProperty("head_commit", out JsonElement headCommit) &&
                headCommit.ValueKind == JsonValueKind.Object
                ? GetStringProperty(headCommit, "message") ?? string.Empty
                : string.Empty;
            DateTime createdAt = DateTime.Now;
            if (DateTime.TryParse(GetStringProperty(run, "created_at"), out DateTime parsed))
            {
                createdAt = parsed;
            }
            string? conclusion = GetStringProperty(run, "conclusion");
            entries.Add(new AndroidBuildEntry(buildNumber, message, sha, createdAt, conclusion));
        }

        return entries;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("ModularAudience-Android", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private static string? GetStringProperty(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out JsonElement value) &&
            value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }
}
