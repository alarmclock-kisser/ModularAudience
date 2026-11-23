using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Maui.Storage;

namespace ModularAudience.VocalSynth.Android.Services;

internal sealed record AndroidReleaseUpdate(
    int BuildNumber,
    Uri DownloadUri,
    long FileSize,
    string? Sha256);

internal sealed record AndroidUpdateProgress(
    long DownloadedBytes,
    long TotalBytes,
    TimeSpan Elapsed,
    double BytesPerSecond,
    TimeSpan? Remaining);

internal sealed class AndroidReleaseUpdateService
{
    private const string ReleasesEndpoint =
        "https://api.github.com/repos/alarmclock-kisser/ModularAudience/releases?per_page=100";
    private const string SignedApkName = "ModularAudience.VocalSynth-Signed.apk";
    private const string CarriedApkPrefix = "ModularAudience.VocalSynth-Signed-from-";
    private static readonly HttpClient HttpClient = CreateHttpClient();

    internal async Task<AndroidReleaseUpdate?> FindLatestSignedUpdateAsync(
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await HttpClient.GetAsync(
            ReleasesEndpoint,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken);

        foreach (JsonElement release in document.RootElement.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out JsonElement draft) && draft.GetBoolean())
            {
                continue;
            }

            if (release.TryGetProperty("prerelease", out JsonElement prerelease) && prerelease.GetBoolean())
            {
                continue;
            }

            string tag = release.TryGetProperty("tag_name", out JsonElement tagElement)
                ? tagElement.GetString() ?? string.Empty
                : string.Empty;
            if (!TryParseBuildNumber(tag, out int releaseBuild))
            {
                continue;
            }

            if (!release.TryGetProperty("assets", out JsonElement assets))
            {
                continue;
            }

            foreach (JsonElement asset in assets.EnumerateArray())
            {
                string name = asset.TryGetProperty("name", out JsonElement nameElement)
                    ? nameElement.GetString() ?? string.Empty
                    : string.Empty;
                int buildNumber = GetAssetBuildNumber(name, releaseBuild);
                if (buildNumber <= 0 ||
                    !asset.TryGetProperty("browser_download_url", out JsonElement urlElement) ||
                    !Uri.TryCreate(urlElement.GetString(), UriKind.Absolute, out Uri? downloadUri))
                {
                    continue;
                }

                long fileSize = asset.TryGetProperty("size", out JsonElement sizeElement)
                    ? sizeElement.GetInt64()
                    : 0;
                string? digest = asset.TryGetProperty("digest", out JsonElement digestElement)
                    ? digestElement.GetString()
                    : null;
                return new AndroidReleaseUpdate(buildNumber, downloadUri, fileSize, digest);
            }
        }

        return null;
    }

    internal async Task<string> DownloadAsync(
        AndroidReleaseUpdate update,
        IProgress<AndroidUpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        string directory = Path.Combine(FileSystem.CacheDirectory, "updates");
        Directory.CreateDirectory(directory);
        string destination = Path.Combine(
            directory,
            $"ModularAudience-VocalSynth-{update.BuildNumber}-{Guid.NewGuid():N}.apk");

        try
        {
            using var probeRequest = new HttpRequestMessage(HttpMethod.Get, update.DownloadUri);
            probeRequest.Headers.Range = new RangeHeaderValue(0, 0);
            using HttpResponseMessage probe = await HttpClient.SendAsync(
                probeRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            probe.EnsureSuccessStatusCode();

            long totalBytes = probe.Content.Headers.ContentRange?.Length
                ?? probe.Content.Headers.ContentLength
                ?? update.FileSize;
            long downloadedBytes = 0;
            var progressLock = new object();
            Stopwatch timer = Stopwatch.StartNew();
            void ReportRead(int count)
            {
                lock (progressLock)
                {
                    downloadedBytes += count;
                    double bytesPerSecond = downloadedBytes / Math.Max(timer.Elapsed.TotalSeconds, 0.001);
                    TimeSpan? remaining = totalBytes > downloadedBytes && bytesPerSecond > 0
                        ? TimeSpan.FromSeconds((totalBytes - downloadedBytes) / bytesPerSecond)
                        : null;
                    progress?.Report(new AndroidUpdateProgress(
                        downloadedBytes,
                        totalBytes,
                        timer.Elapsed,
                        bytesPerSecond,
                        remaining));
                }
            }

            bool supportsRanges = probe.StatusCode == System.Net.HttpStatusCode.PartialContent
                && probe.Content.Headers.ContentRange is
                {
                    From: 0,
                    To: 0,
                    Length: > 1,
                };
            if (probe.StatusCode == System.Net.HttpStatusCode.PartialContent && !supportsRanges)
            {
                throw new InvalidDataException("The update server returned an invalid byte range.");
            }

            totalBytes = supportsRanges
                ? probe.Content.Headers.ContentRange!.Length!.Value
                : probe.Content.Headers.ContentLength ?? update.FileSize;
            if (supportsRanges)
            {
                await using Stream probeStream = await probe.Content.ReadAsStreamAsync(cancellationToken);
                byte[] probeByte = new byte[1];
                if (await probeStream.ReadAsync(probeByte, cancellationToken) != 1 ||
                    await probeStream.ReadAsync(probeByte, cancellationToken) != 0)
                {
                    throw new InvalidDataException("The update server returned an invalid byte-range probe.");
                }
            }

            await using (var output = new FileStream(
                destination,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                256 * 1024,
                FileOptions.Asynchronous | (supportsRanges ? FileOptions.RandomAccess : FileOptions.SequentialScan)))
            {
                if (supportsRanges)
                {
                    output.SetLength(totalBytes);
                    await DownloadRangesAsync(
                        update.DownloadUri,
                        output,
                        totalBytes,
                        ReportRead,
                        cancellationToken);
                }
                else
                {
                    await using Stream input = await probe.Content.ReadAsStreamAsync(cancellationToken);
                    await CopyToFileAsync(input, output, ReportRead, cancellationToken);
                }

                await output.FlushAsync(cancellationToken);
                long actualBytes = downloadedBytes;
                if (totalBytes > 0 && actualBytes != totalBytes)
                {
                    throw new IOException($"The update download was incomplete ({actualBytes} of {totalBytes} bytes).");
                }
            }

            await VerifyDigestAsync(destination, update.Sha256, cancellationToken);
            return destination;
        }
        catch
        {
            if (File.Exists(destination))
            {
                File.Delete(destination);
            }

            throw;
        }
    }

    private static async Task DownloadRangesAsync(
        Uri downloadUri,
        FileStream output,
        long totalBytes,
        Action<int> reportRead,
        CancellationToken cancellationToken)
    {
        const int maxConcurrentRanges = 4;
        int rangeCount = (int)Math.Min(maxConcurrentRanges, totalBytes);
        long baseRangeLength = totalBytes / rangeCount;
        long extraBytes = totalBytes % rangeCount;
        var downloads = new Task[rangeCount];
        long rangeStart = 0;
        for (int index = 0; index < rangeCount; index++)
        {
            long rangeLength = baseRangeLength + (index < extraBytes ? 1 : 0);
            long rangeEnd = rangeStart + rangeLength - 1;
            downloads[index] = DownloadRangeAsync(
                downloadUri,
                output,
                rangeStart,
                rangeEnd,
                totalBytes,
                reportRead,
                cancellationToken);
            rangeStart = rangeEnd + 1;
        }

        await Task.WhenAll(downloads);
    }

    private static async Task DownloadRangeAsync(
        Uri downloadUri,
        FileStream output,
        long rangeStart,
        long rangeEnd,
        long totalBytes,
        Action<int> reportRead,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, downloadUri);
        request.Headers.Range = new RangeHeaderValue(rangeStart, rangeEnd);
        using HttpResponseMessage response = await HttpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        long expectedLength = rangeEnd - rangeStart + 1;
        if (response.StatusCode != System.Net.HttpStatusCode.PartialContent ||
            response.Content.Headers.ContentRange is not
            {
                From: long from,
                To: long to,
                Length: long length,
            } ||
            from != rangeStart ||
            to != rangeEnd ||
            length != totalBytes ||
            response.Content.Headers.ContentLength is long contentLength && contentLength != expectedLength)
        {
            throw new InvalidDataException("The update server returned an invalid byte range.");
        }

        await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken);
        byte[] buffer = new byte[256 * 1024];
        long offset = rangeStart;
        long remaining = expectedLength;
        while (remaining > 0)
        {
            int count = await input.ReadAsync(
                buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)),
                cancellationToken);
            if (count == 0)
            {
                throw new IOException("The update download ended before the requested byte range was complete.");
            }

            await RandomAccess.WriteAsync(output.SafeFileHandle, buffer.AsMemory(0, count), offset, cancellationToken);
            offset += count;
            remaining -= count;
            reportRead(count);
        }

        if (await input.ReadAsync(buffer.AsMemory(0, 1), cancellationToken) != 0)
        {
            throw new InvalidDataException("The update server sent more data than requested.");
        }
    }

    private static async Task CopyToFileAsync(
        Stream input,
        FileStream output,
        Action<int> reportRead,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[256 * 1024];
        while (true)
        {
            int count = await input.ReadAsync(buffer, cancellationToken);
            if (count == 0)
            {
                return;
            }

            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
            reportRead(count);
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("ModularAudience-Android", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private static int GetAssetBuildNumber(string assetName, int releaseBuild)
    {
        if (string.Equals(assetName, SignedApkName, StringComparison.Ordinal))
        {
            return releaseBuild;
        }

        if (!assetName.StartsWith(CarriedApkPrefix, StringComparison.Ordinal) ||
            !assetName.EndsWith(".apk", StringComparison.Ordinal))
        {
            return 0;
        }

        string sourceBuild = assetName[CarriedApkPrefix.Length..^".apk".Length];
        return int.TryParse(sourceBuild, out int parsedBuild) ? parsedBuild : 0;
    }

    private static bool TryParseBuildNumber(string tag, out int buildNumber)
    {
        const string prefix = "release-";
        buildNumber = 0;
        return tag.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(tag[prefix.Length..], out buildNumber)
            && buildNumber > 0;
    }

    private static async Task VerifyDigestAsync(
        string filePath,
        string? digest,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(digest))
        {
            return;
        }

        const string prefix = "sha256:";
        if (!digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The update release uses an unsupported digest format.");
        }

        await using FileStream stream = File.OpenRead(filePath);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
        string actual = Convert.ToHexString(hash);
        string expected = digest[prefix.Length..];
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            Trace.TraceError($"Downloaded APK digest mismatch: expected {expected}, received {actual}.");
            throw new InvalidDataException("The downloaded APK failed its SHA-256 integrity check.");
        }
    }
}
