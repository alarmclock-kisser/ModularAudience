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

        List<AndroidBuildEntry> entries = [];
        HashSet<string> seenCommits = [];
        foreach (JsonElement run in document.RootElement.EnumerateArray())
        {
            if (entries.Count >= MaxEntries)
                break;
            if (!run.TryGetProperty("jobs", out JsonElement jobs) ||
                !JobsIncludeAndroid(jobs))
            {
                continue;
            }

            string? sha = run.TryGetProperty("head_sha", out JsonElement shaElement)
                ? shaElement.GetString()
                : null;
            if (sha != null && !seenCommits.Add(sha))
                continue;

            int buildNumber = run.TryGetProperty("run_number", out JsonElement runNumberElement)
                ? runNumberElement.GetInt32()
                : 0;
            if (buildNumber <= 0)
                continue;

            string message = run.TryGetProperty("head_commit", out JsonElement headCommit) &&
                headCommit.TryGetProperty("message", out JsonElement messageElement)
                ? messageElement.GetString() ?? string.Empty
                : string.Empty;
            DateTime createdAt = DateTime.Now;
            if (run.TryGetProperty("created_at", out JsonElement createdAtElement) &&
                DateTime.TryParse(createdAtElement.GetString(), out DateTime parsed))
            {
                createdAt = parsed;
            }
            string? conclusion = run.TryGetProperty("conclusion", out JsonElement conclusionElement)
                ? conclusionElement.GetString()
                : null;
            entries.Add(new AndroidBuildEntry(buildNumber, message, sha, createdAt, conclusion));
        }

        return entries;
    }

    private static bool JobsIncludeAndroid(JsonElement jobs)
    {
        foreach (JsonElement job in jobs.EnumerateArray())
        {
            if (job.TryGetProperty("name", out JsonElement nameElement) &&
                string.Equals(nameElement.GetString(), "android", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
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
}
