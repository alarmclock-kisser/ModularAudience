using System.Globalization;
using System.Reflection;
using ModularAudience.Ytdlp;

namespace ModularAudience.Forms.Updates;

internal sealed record ApplicationRelease(long Number, ReleaseAsset Asset, IReadOnlyList<ReleaseAsset> Assets)
{
    internal const string Repository = "alarmclock-kisser/ModularAudience";
    internal const string AssetName = "ModularAudience.Forms.exe";
    private const string TagPrefix = "release-";
    private const string MetadataKey = "GitHubReleaseNumber";

    internal static long? RunningReleaseNumber()
    {
        var entries = typeof(ApplicationRelease).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(attribute => attribute.Key == MetadataKey).ToArray();
        if (entries.Length == 0) return null;
        if (entries.Length != 1 || !TryParseNumber(entries[0].Value, out long number))
            throw new InvalidDataException("The executable contains an invalid GitHub release number.");
        return number;
    }

    internal static ApplicationRelease? NewerThan(GitHubRelease release, long runningRelease, long? skipReleaseNumber = null)
    {
        if (runningRelease <= 0) throw new ArgumentOutOfRangeException(nameof(runningRelease));
        if (skipReleaseNumber is <= 0) throw new ArgumentOutOfRangeException(nameof(skipReleaseNumber));
        if (!TryParseTag(release.TagName, out long number))
            throw new InvalidDataException($"The latest GitHub release has an invalid tag: {release.TagName}");
        if (number <= runningRelease || (skipReleaseNumber.HasValue && number <= skipReleaseNumber.Value)) return null;

        var matches = release.Assets.Where(asset => asset.Name == AssetName).ToArray();
        if (matches.Length != 1 || matches[0].Size <= 0)
            throw new InvalidDataException($"The GitHub release must contain one valid {AssetName} asset.");
        string expectedUrl = $"https://github.com/{Repository}/releases/download/{release.TagName}/{AssetName}";
        if (!string.Equals(matches[0].Url, expectedUrl, StringComparison.Ordinal))
            throw new InvalidDataException("The release executable has an unexpected download URL.");
        return new ApplicationRelease(number, matches[0], release.Assets);
    }

    internal static bool TryParseTag(string? tag, out long number)
    {
        number = 0;
        return tag is not null && tag.StartsWith(TagPrefix, StringComparison.Ordinal) &&
            TryParseNumber(tag[TagPrefix.Length..], out number);
    }

    private static bool TryParseNumber(string? value, out long number) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number) &&
        number > 0 && value == number.ToString(CultureInfo.InvariantCulture);
}
