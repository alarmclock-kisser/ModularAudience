using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace ModularAudience.Ytdlp;

public sealed partial class YtdlpClient
{
    private async IAsyncEnumerable<YtdlpVideo> ReadMetadataStreamAsync(string target, int? maxResults,
        [EnumeratorCancellation] CancellationToken token, YtdlpResultType resultType, int? fetchCount = null)
    {
        var progress = CreateProgress(null);
        await EnsureAvailableAsync(progress, token);
        var arguments = CommonArguments();
        MetadataSession?.AddArguments(arguments);
        arguments.AddRange(["--dump-json", "--flat-playlist", "--lazy-playlist", "--skip-download", "--yes-playlist"]);
        if ((fetchCount ?? maxResults) is { } limit)
            arguments.AddRange(["--playlist-end", limit.ToString(CultureInfo.InvariantCulture)]);
        arguments.AddRange(["--", target]);
        string? cookies = MetadataSession?.CookieContents;
        string job = CreateJobDirectory();
        using var source = CancellationTokenSource.CreateLinkedTokenSource(token);
        var output = Channel.CreateBounded<YtdlpVideo>(new BoundedChannelOptions(32)
        { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
        Task producer = ProduceAsync();
        try
        {
            await foreach (var entry in output.Reader.ReadAllAsync(token).ConfigureAwait(false)) yield return entry;
        }
        finally
        {
            // Breaking enumeration, cancelling or closing the window must also stop the
            // producer/process, including when its bounded output channel is full.
            source.Cancel();
            await producer.ConfigureAwait(false);
            YtdlpBinaryManager.DeleteStaging(job);
        }

        async Task ProduceAsync()
        {
            var seen = new HashSet<(bool Channel, bool Playlist, string Id)>();
            int count = 0;
            try
            {
                await YtdlpProcess.RunStreamingAsync(manager.YtdlpPath, arguments, job, progress, async (line, cancellation) =>
                {
                    foreach (var entry in YtdlpParser.ParseVideos(line, YtdlpParser.MaximumResults, includeChannels: true, includePlaylists: true))
                    {
                        cancellation.ThrowIfCancellationRequested();
                        if (maxResults is { } maximum && count >= maximum) break;
                        if (!YtdlpParser.MatchesResultType(entry, resultType) || !seen.Add((entry.IsChannel, entry.IsPlaylist, entry.Id))) continue;
                        await output.Writer.WriteAsync(entry, cancellation).ConfigureAwait(false);
                        count++;
                    }
                }, source.Token, cookies).ConfigureAwait(false);
                progress.Report($"Loaded {count} YouTube entries.");
                output.Writer.TryComplete();
            }
            catch (Exception error) { output.Writer.TryComplete(error); }
        }
    }

    private static async Task<IReadOnlyList<YtdlpVideo>> CollectMetadataAsync(IAsyncEnumerable<YtdlpVideo> stream, CancellationToken token)
    {
        var results = new List<YtdlpVideo>();
        await foreach (var entry in stream.WithCancellation(token).ConfigureAwait(false)) results.Add(entry);
        return results.AsReadOnly();
    }
}
