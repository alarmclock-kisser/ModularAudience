using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Forms.Updates;

namespace ModularAudience.Audio.Tests;

[TestClass]
public sealed class GitHubReleaseHistoryTests
{
    [TestMethod]
    public async Task SingleUpdate_UsesFullTriggeringCommitMessageAndApprovedApiRequests()
    {
        using var api = new FakeApi(request => IsReleases(request)
            ? Json(new[] { Tag(128), Tag(127) })
            : Json(new { workflow_runs = new[] { Run(128, "Add co-op\n\nSynchronize tracks in all directions."), Run(127, "Older") } }));

        ReleaseCommitHistory history = await api.Source.GetAsync(127, 128, CancellationToken.None);

        ReleaseCommitHistoryEntry entry = history.Entries.Single(entry => entry.ReleaseNumber == 128);
        Assert.AreEqual(128L, entry.ReleaseNumber);
        Assert.AreEqual("Add co-op\n\nSynchronize tracks in all directions.", entry.Message);
        Assert.AreEqual(Sha(128), entry.CommitSha);
        Assert.IsNotNull(entry.CommittedAt);
        Assert.AreEqual("Older", history.Entries.Single(entry => entry.ReleaseNumber == 127).Message);
        Assert.HasCount(0, history.Warnings);
        Assert.HasCount(2, api.Requests);
        Assert.IsTrue(api.Requests.All(request => request.Uri.Scheme == "https" && request.Uri.Host == "api.github.com"));
        Assert.IsTrue(api.Requests.All(request => request.ApiVersion == "2026-03-10" && request.Accept.Contains("application/vnd.github+json")));
    }

    [TestMethod]
    public async Task MultipleUpdates_IncludeOnlyPublishedBuildsInExclusiveInclusiveRangeAndSortNumerically()
    {
        using var api = new FakeApi(request => IsReleases(request)
            ? Json(new object[] { Tag(130), Tag(128), Tag(127), Tag(126), Tag(131), Tag(129, draft: true), Tag(125), new { tag_name = "v2.0" } })
            : Json(new { workflow_runs = new object[] { Run(131, "Too new"), Run(130, "Newest"), Run(129, "Unpublished"), Run(128, "Middle"), Run(127, "Oldest"), Run(126, "Installed"), Run(125, "Previous") } }));

        ReleaseCommitHistory history = await api.Source.GetAsync(126, 130, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { 125L, 126L, 127L, 128L, 130L }, history.Entries.Select(entry => entry.ReleaseNumber).ToArray());
        CollectionAssert.AreEqual(new[] { "Previous", "Installed", "Oldest", "Middle", "Newest" }, history.Entries.Select(entry => entry.Message).ToArray());
    }

    [TestMethod]
    public async Task Pagination_DoesNotLoseIntermediateReleasesWhenNewerBuildsAppear()
    {
        using var api = new FakeApi(request =>
        {
            bool second = request.RequestUri!.Query.Contains("&page=2", StringComparison.Ordinal);
            long first = second ? 104 : 131;
            if (IsReleases(request))
                return Json(Enumerable.Range(0, 27).Select(offset => Tag(first - offset)).ToArray());
            return Json(new { workflow_runs = Enumerable.Range(0, 27)
                .Select(offset => Run(first - offset, "Build " + (first - offset).ToString(CultureInfo.InvariantCulture))).ToArray() });
        });

        ReleaseCommitHistory history = await api.Source.GetAsync(126, 129, CancellationToken.None);

        CollectionAssert.AreEqual(Enumerable.Range(103, 27).Select(number => (long)number).ToArray(),
            history.Entries.Select(entry => entry.ReleaseNumber).ToArray());
        Assert.HasCount(4, api.Requests);
        Assert.IsTrue(api.Requests.All(request => request.Uri.Query.Contains("per_page=27", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task MissingWorkflowHistory_FallsBackToImmutableReleaseTagNotCurrentBranch()
    {
        using var api = new FakeApi(request =>
        {
            if (IsReleases(request)) return Json(new[] { Tag(128), Tag(127) });
            if (IsRuns(request)) return new HttpResponseMessage(HttpStatusCode.NotFound);
            return Json(new { sha = Sha(128), commit = new { message = "Message from release tag", committer = new { date = "2026-11-08T20:37:40Z" } } });
        });

        ReleaseCommitHistory history = await api.Source.GetAsync(127, 128, CancellationToken.None);

        Assert.AreEqual("Message from release tag", history.Entries.Single(entry => entry.ReleaseNumber == 128).Message);
        Assert.HasCount(0, history.Warnings);
        Assert.IsTrue(api.Requests.Any(request => request.Uri.AbsolutePath.EndsWith("/commits/release-128", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task PinnedReleaseShaMismatch_RejectsWrongWorkflowMessageAndResolvesTag()
    {
        using var api = new FakeApi(request =>
        {
            if (IsReleases(request)) return Json(new[] { Tag(128, pinnedSha: Sha(999)), Tag(127) });
            if (IsRuns(request)) return Json(new { workflow_runs = new[] { Run(128, "Wrong commit") } });
            return Json(new { sha = Sha(999), commit = new { message = "Correct tagged commit" } });
        });

        ReleaseCommitHistory history = await api.Source.GetAsync(127, 128, CancellationToken.None);

        ReleaseCommitHistoryEntry offered = history.Entries.Single(entry => entry.ReleaseNumber == 128);
        Assert.AreEqual("Correct tagged commit", offered.Message);
        Assert.AreEqual(Sha(999), offered.CommitSha);
    }

    [TestMethod]
    public async Task NullHeadCommit_UsesTagFallback()
    {
        using var api = new FakeApi(request =>
        {
            if (IsReleases(request)) return Json(new[] { Tag(128), Tag(127) });
            if (IsRuns(request)) return Json(new { workflow_runs = new[] { new { run_number = 128, head_commit = (object?)null } } });
            return Json(new { sha = Sha(128), commit = new { message = "Recovered message" } });
        });

        ReleaseCommitHistory history = await api.Source.GetAsync(127, 128, CancellationToken.None);

        Assert.AreEqual("Recovered message", history.Entries.Single(entry => entry.ReleaseNumber == 128).Message);
    }

    [TestMethod]
    public async Task RateLimit_ReturnsExplicitPlaceholdersAndStopsAdditionalCommitRequests()
    {
        using var api = new FakeApi(request => IsReleases(request)
            ? Json(new[] { Tag(130), Tag(129), Tag(128), Tag(127) })
            : new HttpResponseMessage(HttpStatusCode.Forbidden));

        ReleaseCommitHistory history = await api.Source.GetAsync(127, 130, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { 127L, 128L, 129L, 130L }, history.Entries.Select(entry => entry.ReleaseNumber).ToArray());
        Assert.IsTrue(history.Entries.All(entry => entry.Message is null));
        Assert.IsTrue(history.Warnings.Count > 0);
        Assert.HasCount(2, api.Requests);
    }

    [TestMethod]
    public async Task ReleaseListFailure_StillShowsOfferedBuildAndExplainsIncompleteHistory()
    {
        using var api = new FakeApi(request => IsReleases(request)
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : Json(new { workflow_runs = new[] { Run(130, "Latest available message") } }));

        ReleaseCommitHistory history = await api.Source.GetAsync(127, 130, CancellationToken.None);

        Assert.AreEqual(130L, history.Entries.Single().ReleaseNumber);
        Assert.AreEqual("Latest available message", history.Entries.Single().Message);
        Assert.IsTrue(history.Warnings.Any(warning => warning.Contains("intermediate builds may be missing", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task OversizedMetadata_IsBoundedAndDoesNotBlockWorkflowFallback()
    {
        using var api = new FakeApi(request => IsReleases(request)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[4 * 1024 * 1024 + 1]) }
            : Json(new { workflow_runs = new[] { Run(128, "Available message") } }));

        ReleaseCommitHistory history = await api.Source.GetAsync(127, 128, CancellationToken.None);

        Assert.AreEqual("Available message", history.Entries.Single().Message);
        Assert.IsTrue(history.Warnings.Any(warning => warning.Contains("allowed size", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task Cancellation_StopsBeforeSendingRequests()
    {
        using var api = new FakeApi(_ => throw new AssertFailedException("Cancelled history must not send requests."));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await api.Source.GetAsync(127, 128, cancellation.Token));

        Assert.HasCount(0, api.Requests);
    }

    [TestMethod]
    public void Formatting_ExcludesInstalledAndFutureBuildsAndPreservesMessageBodies()
    {
        var history = new ReleaseCommitHistory(
        [
            new(130, Sha(130), "Newest\n\nDetails äöü", null),
            new(127, Sha(127), "Already installed", null),
            new(131, Sha(131), "Not offered yet", null),
            new(128, Sha(128), "Oldest\r\nBody", null),
            new(129, Sha(129), null, null)
        ], []);

        string text = ReleaseCommitHistoryFormatter.Format(history, 127, 130);

        StringAssert.Contains(text, "Already installed");
        Assert.IsFalse(text.Contains("Not offered yet", StringComparison.Ordinal));
        Assert.IsTrue(text.IndexOf("Build 127", StringComparison.Ordinal) < text.IndexOf("Build 128", StringComparison.Ordinal));
        Assert.IsTrue(text.IndexOf("Build 128", StringComparison.Ordinal) < text.IndexOf("Build 129", StringComparison.Ordinal));
        Assert.IsTrue(text.IndexOf("Build 129", StringComparison.Ordinal) < text.IndexOf("Build 130", StringComparison.Ordinal));
        StringAssert.Contains(text, "Newest" + Environment.NewLine + Environment.NewLine + "Details äöü");
        StringAssert.Contains(text, "Commit message unavailable");
    }

    [TestMethod]
    public void Formatting_LimitsPreviousReleasesAndStartsAtCurrentChanges()
    {
        var history = new ReleaseCommitHistory(
            Enumerable.Range(1, 102)
                .Select(number => new ReleaseCommitHistoryEntry(number, null, "Message " + number.ToString(CultureInfo.InvariantCulture), null))
                .ToArray(),
            []);

        string text = ReleaseCommitHistoryFormatter.Format(history, 100, 102, out int currentChangesStart);

        Assert.IsFalse(text.Contains("Build 76", StringComparison.Ordinal));
        StringAssert.Contains(text, "Build 77");
        StringAssert.Contains(text, "Build 100");
        StringAssert.Contains(text, "Build 101");
        StringAssert.Contains(text, "Build 102");
        Assert.AreEqual(text.IndexOf("Build 101", StringComparison.Ordinal), currentChangesStart);
    }

    [TestMethod]
    public void Formatting_RemovesEmbeddedNullsAndDisplaysEmptyMessagesAndWarnings()
    {
        var history = new ReleaseCommitHistory([new(128, null, "Before\0After", null), new(129, null, string.Empty, null)], ["Partial history"]);

        string text = ReleaseCommitHistoryFormatter.Format(history, 127, 129);

        StringAssert.Contains(text, "BeforeAfter");
        StringAssert.Contains(text, "(Empty commit message)");
        StringAssert.Contains(text, "Note: Partial history");
    }

    private static object Tag(long number, bool draft = false, string? pinnedSha = null) => new
    {
        tag_name = "release-" + number.ToString(CultureInfo.InvariantCulture), draft, prerelease = false,
        target_commitish = pinnedSha ?? "master"
    };

    private static object Run(long number, string message) => new
    {
        run_number = number, head_sha = Sha(number),
        head_commit = new { id = Sha(number), message, timestamp = "2026-11-08T20:37:40Z" }
    };

    private static string Sha(long number) => number.ToString("x40", CultureInfo.InvariantCulture);

    private static bool IsReleases(HttpRequestMessage request) => request.RequestUri!.AbsolutePath.EndsWith("/releases", StringComparison.Ordinal);
    private static bool IsRuns(HttpRequestMessage request) => request.RequestUri!.AbsolutePath.EndsWith("/runs", StringComparison.Ordinal);

    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
    };

    private sealed record RequestInfo(Uri Uri, string ApiVersion, string Accept);

    private sealed class FakeApi : IDisposable
    {
        private readonly HttpClient client;
        internal List<RequestInfo> Requests { get; } = [];
        internal GitHubReleaseHistorySource Source { get; }

        internal FakeApi(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            this.client = new HttpClient(new Handler(request =>
            {
                this.Requests.Add(new(request.RequestUri!, request.Headers.GetValues("X-GitHub-Api-Version").Single(), request.Headers.Accept.ToString()));
                return respond(request);
            }));
            this.Source = new GitHubReleaseHistorySource(this.client);
        }

        public void Dispose() => this.client.Dispose();
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(respond(request));
        }
    }
}
