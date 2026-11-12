using System.Globalization;
using System.Text;

namespace ModularAudience.Forms.Updates;

internal sealed record ReleaseCommitHistoryEntry(long ReleaseNumber, string? CommitSha, string? Message, DateTimeOffset? CommittedAt);

internal sealed record ReleaseCommitHistory(IReadOnlyList<ReleaseCommitHistoryEntry> Entries, IReadOnlyList<string> Warnings)
{
    internal const int MaximumHistoricalReleases = 24;
}

internal static class ReleaseCommitHistoryFormatter
{
    internal static string Format(ReleaseCommitHistory history, long installedRelease, long offeredRelease) =>
        Format(history, installedRelease, offeredRelease, out _);

    internal static string Format(ReleaseCommitHistory history, long installedRelease, long offeredRelease,
        out int latestReleaseStart)
    {
        ReleaseCommitHistoryEntry[] previousEntries = history.Entries
            .Where(entry => entry.ReleaseNumber <= installedRelease)
            .OrderByDescending(entry => entry.ReleaseNumber)
            .Take(ReleaseCommitHistory.MaximumHistoricalReleases)
            .OrderBy(entry => entry.ReleaseNumber)
            .ToArray();
        ReleaseCommitHistoryEntry[] currentEntries = history.Entries
            .Where(entry => entry.ReleaseNumber > installedRelease && entry.ReleaseNumber <= offeredRelease)
            .OrderBy(entry => entry.ReleaseNumber)
            .ToArray();
        var text = new StringBuilder();

        if (previousEntries.Length > 0)
        {
            text.Append("Previous releases (up to ")
                .Append(ReleaseCommitHistory.MaximumHistoricalReleases.ToString(CultureInfo.InvariantCulture))
                .AppendLine(", oldest first)")
                .AppendLine();
            AppendEntries(text, previousEntries);
            text.AppendLine().AppendLine();
        }

        text.Append("Changes since build ")
            .Append(installedRelease.ToString(CultureInfo.InvariantCulture))
            .AppendLine()
            .AppendLine();
        if (currentEntries.Length == 0)
        {
            latestReleaseStart = text.Length;
            text.Append("No commit messages are available for this release range.");
        }
        else
            latestReleaseStart = AppendEntries(text, currentEntries, currentEntries[^1].ReleaseNumber);

        foreach (string warning in history.Warnings.Distinct(StringComparer.Ordinal))
            text.AppendLine().AppendLine().Append("Note: ").Append(warning.Replace("\0", string.Empty, StringComparison.Ordinal));
        return text.ToString();
    }

    private static int AppendEntries(StringBuilder text, IEnumerable<ReleaseCommitHistoryEntry> entries, long? releaseToFind = null)
    {
        bool hasEntry = false;
        int releaseStart = -1;
        foreach (ReleaseCommitHistoryEntry entry in entries)
        {
            if (hasEntry) text.AppendLine().AppendLine();
            if (entry.ReleaseNumber == releaseToFind) releaseStart = text.Length;
            text.Append("Build ").Append(entry.ReleaseNumber.ToString(CultureInfo.InvariantCulture));
            if (entry.CommittedAt is { } date)
                text.Append(" | ").Append(date.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(entry.CommitSha))
                text.Append(" | ").Append(entry.CommitSha[..Math.Min(12, entry.CommitSha.Length)]);
            text.AppendLine();
            text.Append(entry.Message is null
                ? "Commit message unavailable for this release."
                : string.IsNullOrWhiteSpace(entry.Message)
                    ? "(Empty commit message)"
                    : entry.Message.Replace("\0", string.Empty, StringComparison.Ordinal).ReplaceLineEndings(Environment.NewLine).Trim());
            hasEntry = true;
        }
        return releaseStart;
    }
}
