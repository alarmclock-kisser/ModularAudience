using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ModularAudience.Forms.Updates;

/// <summary>
/// Joins published release tags to the workflow run that produced each build.
/// Workflow pages supply full commit messages in batches; release-tag lookups
/// provide a fallback for deleted runs or missing head_commit metadata.
/// </summary>
internal sealed class GitHubReleaseHistorySource
{
    private const int MaximumResponseBytes = 4 * 1024 * 1024;
    private const int MaximumPages = 100;
    private const int MaximumCommitFallbacks = 20;
    private const string ApiRoot = "https://api.github.com/repos/" + ApplicationRelease.Repository;
    private static readonly HttpClient DefaultClient = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(20)
    };
    private readonly HttpClient client;

    internal GitHubReleaseHistorySource(HttpClient? client = null) => this.client = client ?? DefaultClient;

    internal async Task<ReleaseCommitHistory> GetAsync(long installedRelease, long offeredRelease, CancellationToken token)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(installedRelease);
        if (offeredRelease <= installedRelease) throw new ArgumentOutOfRangeException(nameof(offeredRelease));
        token.ThrowIfCancellationRequested();
        long releaseRange = offeredRelease - installedRelease;
        int pageSize = (int)(releaseRange > 100L - ReleaseCommitHistory.MaximumHistoricalReleases
            ? 100L
            : releaseRange + ReleaseCommitHistory.MaximumHistoricalReleases);
        var releases = new Dictionary<long, ReleaseTag>
        {
            // The updater already verified that the offered release exists.
            [offeredRelease] = new(offeredRelease, "release-" + offeredRelease.ToString(CultureInfo.InvariantCulture), null)
        };
        var warnings = new List<string>();
        try
        {
            await ReadReleasesAsync(installedRelease, offeredRelease, pageSize, releases, warnings, token).ConfigureAwait(false);
        }
        catch (Exception error) when (IsMetadataError(error))
        {
            warnings.Add($"The release list could not be fully loaded; some intermediate builds may be missing, and some previous releases may be missing. {error.Message}");
        }

        var entries = new Dictionary<long, ReleaseCommitHistoryEntry>();
        Exception? workflowError = null;
        try
        {
            await ReadWorkflowCommitsAsync(installedRelease, pageSize, releases, entries, warnings, token).ConfigureAwait(false);
        }
        catch (Exception error) when (IsMetadataError(error))
        {
            workflowError = error;
        }

        bool fallbackAllowed = !IsRateLimit(workflowError);
        int fallbackCount = 0;
        foreach (ReleaseTag release in releases.Values
            .OrderBy(release => release.Number <= installedRelease ? 1 : 0)
            .ThenBy(release => release.Number <= installedRelease ? installedRelease - release.Number : release.Number))
        {
            token.ThrowIfCancellationRequested();
            if (entries.ContainsKey(release.Number)) continue;
            if (fallbackAllowed && fallbackCount < MaximumCommitFallbacks)
            {
                fallbackCount++;
                try
                {
                    using JsonDocument document = await ReadAsync("/commits/" + Uri.EscapeDataString(release.Tag) + "?per_page=1", token).ConfigureAwait(false);
                    JsonElement root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("commit", out JsonElement commit))
                        throw new InvalidDataException("GitHub did not return commit metadata for the release tag.");
                    ReleaseCommitHistoryEntry? entry = ParseCommit(release.Number, String(root, "sha"), commit, "committer", "date");
                    if (entry is null) throw new InvalidDataException("GitHub did not return a valid commit message for the release tag.");
                    entries[release.Number] = entry;
                    continue;
                }
                catch (Exception error) when (IsMetadataError(error))
                {
                    warnings.Add($"Build {release.Number}: commit details could not be loaded. {error.Message}");
                    if (IsRateLimit(error)) fallbackAllowed = false;
                }
            }

            entries[release.Number] = new(release.Number, null, null, null);
        }

        if (entries.Values.Any(entry => entry.Message is null))
            warnings.Add(workflowError is null
                ? "Some commit messages are unavailable. You can still update, skip, or dismiss this release."
                : $"GitHub build history is unavailable ({workflowError.Message}). You can still update, skip, or dismiss this release.");
        return new ReleaseCommitHistory(entries.Values.OrderBy(entry => entry.ReleaseNumber).ToList(), warnings);
    }

    private async Task ReadReleasesAsync(long installedRelease, long offeredRelease, int pageSize,
        Dictionary<long, ReleaseTag> releases, List<string> warnings, CancellationToken token)
    {
        int historicalReleasesAdded = 0;
        for (int page = 1; page <= MaximumPages; page++)
        {
            using JsonDocument document = await ReadAsync($"/releases?per_page={pageSize}&page={page}", token).ConfigureAwait(false);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Array) throw new InvalidDataException("GitHub returned an invalid release list.");
            foreach (JsonElement item in root.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || True(item, "draft") || True(item, "prerelease")) continue;
                string? tag = String(item, "tag_name");
                if (!ApplicationRelease.TryParseTag(tag, out long number)) continue;
                string? target = String(item, "target_commitish");
                var release = new ReleaseTag(number, tag!, IsCommitSha(target) ? target : null);
                if (number <= installedRelease)
                {
                    if (historicalReleasesAdded < ReleaseCommitHistory.MaximumHistoricalReleases &&
                        !releases.ContainsKey(number))
                    {
                        releases[number] = release;
                        historicalReleasesAdded++;
                    }
                    continue;
                }
                if (number <= offeredRelease) releases[number] = release;
            }
            if (historicalReleasesAdded >= ReleaseCommitHistory.MaximumHistoricalReleases ||
                root.GetArrayLength() < pageSize) return;
        }
        warnings.Add("The release-history pagination limit was reached; some older intermediate builds or previous releases may be missing.");
    }

    private async Task ReadWorkflowCommitsAsync(long installedRelease, int pageSize,
        Dictionary<long, ReleaseTag> releases, Dictionary<long, ReleaseCommitHistoryEntry> entries,
        List<string> warnings, CancellationToken token)
    {
        for (int page = 1; page <= MaximumPages; page++)
        {
            using JsonDocument document = await ReadAsync($"/actions/workflows/build.yml/runs?per_page={pageSize}&page={page}&exclude_pull_requests=true", token).ConfigureAwait(false);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("workflow_runs", out JsonElement runs) || runs.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("GitHub returned an invalid build history.");
            long oldestRelease = releases.Keys.Min();
            bool reachedOldestRelease = false;
            foreach (JsonElement run in runs.EnumerateArray())
            {
                if (run.ValueKind != JsonValueKind.Object || !run.TryGetProperty("run_number", out JsonElement version) ||
                    version.ValueKind != JsonValueKind.Number || !version.TryGetInt64(out long number)) continue;
                if (number <= oldestRelease) reachedOldestRelease = true;
                if (!releases.TryGetValue(number, out ReleaseTag? release) || entries.ContainsKey(number) ||
                    !run.TryGetProperty("head_commit", out JsonElement commit) || commit.ValueKind != JsonValueKind.Object) continue;
                string? sha = String(run, "head_sha");
                if (!string.Equals(sha, String(commit, "id"), StringComparison.OrdinalIgnoreCase) ||
                    (release.PinnedSha is not null && !string.Equals(sha, release.PinnedSha, StringComparison.OrdinalIgnoreCase))) continue;
                ReleaseCommitHistoryEntry? entry = ParseCommit(number, sha, commit, null, "timestamp");
                if (entry is not null) entries[number] = entry;
            }
            if (entries.Count == releases.Count || reachedOldestRelease || runs.GetArrayLength() < pageSize) return;
        }
        warnings.Add("The build-history pagination limit was reached; older commit messages may require release-tag lookups.");
    }

    private static ReleaseCommitHistoryEntry? ParseCommit(long number, string? sha, JsonElement commit, string? dateObject, string dateProperty)
    {
        if (commit.ValueKind != JsonValueKind.Object || !IsCommitSha(sha) ||
            !commit.TryGetProperty("message", out JsonElement message) || message.ValueKind != JsonValueKind.String) return null;
        JsonElement dateRoot = commit;
        if (dateObject is not null && (!commit.TryGetProperty(dateObject, out dateRoot) || dateRoot.ValueKind != JsonValueKind.Object))
            dateRoot = default;
        DateTimeOffset? date = DateTimeOffset.TryParse(String(dateRoot, dateProperty), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out DateTimeOffset value) ? value : null;
        return new(number, sha, message.GetString()!, date);
    }

    private async Task<JsonDocument> ReadAsync(string relativePath, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ApiRoot + relativePath);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("ModularAudience", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");
        using HttpResponseMessage response = await this.client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaximumResponseBytes)
            throw new InvalidDataException("The GitHub history response exceeds the allowed size.");
        await using Stream source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var bytes = new MemoryStream();
        byte[] buffer = new byte[81920];
        int count;
        while ((count = await source.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            if (bytes.Length + count > MaximumResponseBytes)
                throw new InvalidDataException("The GitHub history response exceeds the allowed size.");
            await bytes.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
        }
        return JsonDocument.Parse(bytes.ToArray());
    }

    private static string? String(JsonElement item, string property) => item.ValueKind == JsonValueKind.Object &&
        item.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool True(JsonElement item, string property) => item.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.True;

    private static bool IsCommitSha(string? value) => value is { Length: 40 or 64 } && value.All(Uri.IsHexDigit);

    private static bool IsMetadataError(Exception error) => error is HttpRequestException or JsonException or IOException or InvalidDataException;

    private static bool IsRateLimit(Exception? error) => error is HttpRequestException { StatusCode: HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests };

    private sealed record ReleaseTag(long Number, string Tag, string? PinnedSha);
}
