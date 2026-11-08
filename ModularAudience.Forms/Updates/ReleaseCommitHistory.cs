using System.Globalization;
using System.Text;

namespace ModularAudience.Forms.Updates;

internal sealed record ReleaseCommitHistoryEntry(long ReleaseNumber, string? CommitSha, string? Message, DateTimeOffset? CommittedAt);

internal sealed record ReleaseCommitHistory(IReadOnlyList<ReleaseCommitHistoryEntry> Entries, IReadOnlyList<string> Warnings);

internal static class ReleaseCommitHistoryFormatter
{
    internal static string Format(ReleaseCommitHistory history, long installedRelease, long offeredRelease)
    {
        var text = new StringBuilder();
        foreach (ReleaseCommitHistoryEntry entry in history.Entries
            .Where(entry => entry.ReleaseNumber > installedRelease && entry.ReleaseNumber <= offeredRelease)
            .OrderBy(entry => entry.ReleaseNumber))
        {
            if (text.Length > 0) text.AppendLine().AppendLine();
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
        }

        if (text.Length == 0) text.Append("No commit messages are available for this release range.");
        foreach (string warning in history.Warnings.Distinct(StringComparer.Ordinal))
            text.AppendLine().AppendLine().Append("Note: ").Append(warning.Replace("\0", string.Empty, StringComparison.Ordinal));
        return text.ToString();
    }
}
