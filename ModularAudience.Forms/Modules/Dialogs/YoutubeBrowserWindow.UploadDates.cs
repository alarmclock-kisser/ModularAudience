using ModularAudience.Ytdlp;

namespace ModularAudience.Forms.Modules.Dialogs;

public partial class YoutubeBrowserWindow
{
    private CancellationTokenSource? dateCancellation;
    private readonly HashSet<Task> dateTasks = [];
    private int dateGeneration;
    private readonly Dictionary<string, DateOnly> resultDateCache = new(StringComparer.Ordinal);
    private Func<YtdlpVideo, CancellationToken, Task<DateOnly?>>? uploadDateProvider;

    private void StartUploadDates(IReadOnlyList<YtdlpVideo> videos)
    {
        dateCancellation?.Cancel();
        dateCancellation?.Dispose();
        dateCancellation = new CancellationTokenSource();
        int generation = ++dateGeneration;
        foreach (ListViewItem row in resultsList.Items)
            if (((YtdlpVideo)row.Tag!).UploadDate is null) row.SubItems[6].Text = "Loading...";
        var lookupClient = new YtdlpClient(binaryManager)
        {
            NetworkOptions = client.NetworkOptions,
            MetadataSession = client.MetadataSession
        };
        Func<YtdlpVideo, CancellationToken, Task<DateOnly?>> provider =
            uploadDateProvider ?? new Func<YtdlpVideo, CancellationToken, Task<DateOnly?>>(lookupClient.GetUploadDateAsync);
        Task task = EnrichUploadDatesAsync(videos, provider, generation, dateCancellation.Token);
        dateTasks.Add(task);
        _ = ObserveDateTaskAsync(task);
    }

    private async Task ObserveDateTaskAsync(Task task)
    {
        try { await task; }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!closing && !resourcesReleased) AppendLog("Warning: Upload-date lookup failed. " + error.Message);
        }
        finally { dateTasks.Remove(task); }
    }

    private async Task EnrichUploadDatesAsync(IReadOnlyList<YtdlpVideo> videos,
        Func<YtdlpVideo, CancellationToken, Task<DateOnly?>> provider, int generation, CancellationToken token)
    {
        using var slots = new SemaphoreSlim(4);
        await Task.WhenAll(videos.Where(video => video.UploadDate is null).GroupBy(video => video.Id)
            .Select(group => LoadUploadDateAsync(group.First(), provider, slots, generation, token)));
        if (generation == dateGeneration && !closing && !resourcesReleased) ApplySort();
    }

    private async Task LoadUploadDateAsync(YtdlpVideo video,
        Func<YtdlpVideo, CancellationToken, Task<DateOnly?>> provider, SemaphoreSlim slots,
        int generation, CancellationToken token)
    {
        await slots.WaitAsync(token);
        DateOnly? date = null;
        string? failure = null;
        try
        {
            date = resultDateCache.TryGetValue(video.Id, out var cached) ? cached : await provider(video, token);
            if (!token.IsCancellationRequested && generation == dateGeneration && date is { } value)
            {
                if (resultDateCache.Count >= 2000) resultDateCache.Clear();
                resultDateCache[video.Id] = value;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        catch (Exception error) { failure = error.Message; }
        finally { slots.Release(); }
        if (token.IsCancellationRequested || generation != dateGeneration || closing || resourcesReleased) return;
        foreach (ListViewItem row in resultsList.Items)
        {
            if (row.Tag is not YtdlpVideo current || current.Id != video.Id || current.UploadDate is not null) continue;
            row.Tag = current with { UploadDate = date };
            row.SubItems[6].Text = date is null ? "Unavailable" : FormatUploadDate(date);
            row.ToolTipText = failure is null ? current.Title : "Upload date: " + failure;
        }
        if (failure is not null) AppendLog($"Warning: Upload date for {video.Id}: {failure}");
    }
}
