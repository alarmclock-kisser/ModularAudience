using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ModularAudience.Ytdlp;

internal sealed record ReleaseAsset(string Name, string Url, string? Digest, long Size);

internal static partial class GitHubReleaseSource
{
    private const long MaximumAssetBytes = 768L * 1024 * 1024;
    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromMinutes(15)
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ModularAudience.Ytdlp", "1.0"));
        return client;
    }

    internal static async Task<IReadOnlyList<ReleaseAsset>> GetAssetsAsync(string repository, CancellationToken token)
    {
        var bytes = await ReadLimitedAsync($"https://api.github.com/repos/{repository}/releases/latest", 4 * 1024 * 1024, token);
        using var document = JsonDocument.Parse(bytes);
        var assets = new List<ReleaseAsset>();
        foreach (var asset in document.RootElement.GetProperty("assets").EnumerateArray())
        {
            if (asset.ValueKind != JsonValueKind.Object ||
                !asset.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(name.GetString()) ||
                !asset.TryGetProperty("browser_download_url", out var url) || url.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(url.GetString()) ||
                !asset.TryGetProperty("size", out var size) || size.ValueKind != JsonValueKind.Number ||
                !size.TryGetInt64(out var length) || length <= 0)
                throw new InvalidDataException("The GitHub release contains invalid asset metadata.");
            assets.Add(new ReleaseAsset(name.GetString()!, url.GetString()!,
                asset.TryGetProperty("digest", out var digest) && digest.ValueKind == JsonValueKind.String
                    ? digest.GetString() : null,
                length));
        }
        return assets;
    }

    internal static async Task<string> GetChecksumAsync(ReleaseAsset asset, IReadOnlyList<ReleaseAsset> assets,
        CancellationToken token)
    {
        if (asset.Digest is { } digest && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            && HashPattern().IsMatch(digest[7..]))
            return digest[7..].ToLowerInvariant();

        var checksum = assets.FirstOrDefault(a => a.Name == asset.Name + ".sha256")
            ?? assets.FirstOrDefault(a => a.Name == asset.Name + ".sha256sum")
            ?? assets.FirstOrDefault(a => a.Name == "SHA2-256SUMS")
            ?? assets.FirstOrDefault(a => a.Name.Equals("checksums.sha256", StringComparison.OrdinalIgnoreCase));
        if (checksum is null)
            throw new InvalidDataException($"No SHA256 digest or checksum asset is available for {asset.Name}.");
        var text = Encoding.UTF8.GetString(await ReadLimitedAsync(checksum.Url, 4 * 1024 * 1024, token))
            .Trim().TrimStart('\uFEFF');
        var dedicated = checksum.Name == asset.Name + ".sha256" || checksum.Name == asset.Name + ".sha256sum";
        if (dedicated && HashPattern().IsMatch(text))
            return text.ToLowerInvariant();
        // Deno's Windows checksum sidecar is currently PowerShell Get-FileHash output.
        if (dedicated && PowerShellHashPattern().Match(text) is { Success: true } match)
            return match.Groups[1].Value.ToLowerInvariant();
        foreach (var line in text.Split('\n'))
        {
            var parts = line.Trim().Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && HashPattern().IsMatch(parts[0]) &&
                parts[1].TrimStart('*').Trim() == asset.Name)
                return parts[0].ToLowerInvariant();
        }
        throw new InvalidDataException($"No matching SHA256 checksum was found for {asset.Name}.");
    }

    internal static async Task DownloadAsync(ReleaseAsset asset, string destination, string expectedHash,
        CancellationToken token)
    {
        if (asset.Size <= 0 || asset.Size > MaximumAssetBytes)
            throw new InvalidDataException("The binary asset size exceeds the allowed limit.");
        using var response = await SendAsync(asset.Url, token);
        await using var source = await response.Content.ReadAsStreamAsync(token);
        await using (var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            81920, FileOptions.Asynchronous))
        {
            await CopyLimitedAsync(source, target, asset.Size, token);
            await target.FlushAsync(token);
        }
        if (new FileInfo(destination).Length != asset.Size ||
            !string.Equals(await HashAsync(destination, token), expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"SHA256 or size verification failed for {asset.Name}.");
    }

    internal static async Task<string> HashAsync(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token)).ToLowerInvariant();
    }

    private static async Task<byte[]> ReadLimitedAsync(string url, long limit, CancellationToken token)
    {
        using var response = await SendAsync(url, token);
        await using var source = await response.Content.ReadAsStreamAsync(token);
        using var target = new MemoryStream();
        await CopyLimitedAsync(source, target, limit, token);
        return target.ToArray();
    }

    private static async Task CopyLimitedAsync(Stream source, Stream target, long limit, CancellationToken token)
    {
        var buffer = new byte[81920];
        long total = 0;
        int count;
        while ((count = await source.ReadAsync(buffer.AsMemory(), token)) != 0)
        {
            total = checked(total + count);
            if (total > limit)
                throw new InvalidDataException("The download exceeded the allowed size.");
            await target.WriteAsync(buffer.AsMemory(0, count), token);
        }
    }

    private static async Task<HttpResponseMessage> SendAsync(string url, CancellationToken token)
    {
        var uri = new Uri(url, UriKind.Absolute);
        for (var redirect = 0; redirect < 6; redirect++)
        {
            ValidateEndpoint(uri);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or
                HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                var location = response.Headers.Location;
                response.Dispose();
                if (location is null)
                    throw new HttpRequestException("GitHub returned a redirect without a location.");
                uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                continue;
            }
            if (response.IsSuccessStatusCode)
                return response;
            var status = response.StatusCode;
            response.Dispose();
            throw new HttpRequestException($"GitHub returned HTTP {(int)status} ({status}).", null, status);
        }
        throw new HttpRequestException("GitHub returned too many redirects.");
    }

    private static void ValidateEndpoint(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443 || !string.IsNullOrEmpty(uri.UserInfo) ||
            uri.IdnHost is not ("api.github.com" or "github.com" or "release-assets.githubusercontent.com" or
                "objects.githubusercontent.com" or "github-releases.githubusercontent.com"))
            throw new InvalidDataException("The binary URL is not an approved HTTPS GitHub endpoint.");
    }

    [GeneratedRegex("^[A-Fa-f0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex HashPattern();

    [GeneratedRegex(@"^[ \t]*Algorithm[ \t]*:[ \t]*SHA256[ \t]*\r?\n[ \t]*Hash[ \t]*:[ \t]*([A-Fa-f0-9]{64})[ \t]*\r?$",
        RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex PowerShellHashPattern();
}
