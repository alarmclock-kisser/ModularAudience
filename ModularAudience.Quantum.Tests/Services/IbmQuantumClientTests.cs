using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModularAudience.Quantum.Tests.Mocks;

namespace ModularAudience.Quantum.Tests.Services;

[TestClass]
public sealed class IbmQuantumClientTests
{
    [TestMethod]
    public async Task ExecuteAsync_SubmitsSamplerV2WithBearerCrnVersionAndShots()
    {
        using var fixture = new IbmClientFixture();
        var input = IbmClientFixture.Request();
        var result = await fixture.Client.ExecuteAsync(input, CancellationToken.None);
        Assert.AreEqual("job-1", result.JobId);
        Assert.AreEqual(512, result.Counts["1001"]);
        Assert.AreEqual(512, result.Counts["0110"]);
        Assert.IsTrue(result.ExecutionLatency >= TimeSpan.Zero);
        RecordedRequest[] requests = fixture.Handler.Requests;
        Assert.AreEqual(3, requests.Length);
        Assert.AreEqual("https://quantum.cloud.ibm.com/api/v1/jobs", requests[0].Uri.AbsoluteUri);
        Assert.AreEqual("?exclude_params=true", requests[1].Uri.Query);
        Assert.AreEqual("/api/v1/jobs/job-1/results", requests[2].Uri.AbsolutePath);
        Assert.IsTrue(requests.All(request => request.Authorization == "Bearer test-bearer" && request.ServiceCrn == "crn:test-instance" && request.ApiVersion == "2026-04-15"));
        using var body = JsonDocument.Parse(requests[0].Body);
        Assert.AreEqual("application/json", requests[0].ContentType);
        Assert.AreEqual("sampler", body.RootElement.GetProperty("program_id").GetString());
        Assert.AreEqual("ibm_test", body.RootElement.GetProperty("backend").GetString());
        JsonElement parameters = body.RootElement.GetProperty("params");
        Assert.AreEqual(input.CircuitQasm, parameters.GetProperty("pubs")[0][0].GetString());
        Assert.AreEqual(1024, parameters.GetProperty("shots").GetInt32());
        Assert.AreEqual(2, parameters.GetProperty("version").GetInt32());
        Assert.IsFalse(parameters.GetProperty("support_qiskit").GetBoolean());
    }

    [TestMethod]
    [DataRow(401)]
    [DataRow(403)]
    [DataRow(500)]
    [DataRow(429)]
    public async Task SubmissionFailures_AreNotRetried(int status)
    {
        using var fixture = new IbmClientFixture(_ => MockHttpMessageHandler.Json("{}", (HttpStatusCode)status));
        var error = await Assert.ThrowsExactlyAsync<HttpRequestException>(() => fixture.Client.ExecuteAsync(IbmClientFixture.Request(), CancellationToken.None));
        Assert.AreEqual((HttpStatusCode)status, error.StatusCode);
        StringAssert.StartsWith(error.Message, "IBM Quantum request failed with HTTP ");
        Assert.AreEqual(1, fixture.Handler.Requests.Length);
    }

    [TestMethod]
    [DataRow(429)]
    [DataRow(500)]
    [DataRow(502)]
    [DataRow(503)]
    [DataRow(504)]
    public async Task TransientReadFailures_AreRetriedWithoutResubmitting(int status)
    {
        int reads = 0;
        using var fixture = new IbmClientFixture(request =>
        {
            if (request.Method == HttpMethod.Get && Interlocked.Increment(ref reads) == 1)
            {
                var response = MockHttpMessageHandler.Json("{}", (HttpStatusCode)status);
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
                return response;
            }
            return IbmClientFixture.DefaultResponse(request);
        });
        await fixture.Client.ExecuteAsync(IbmClientFixture.Request(), CancellationToken.None);
        Assert.AreEqual(1, fixture.Handler.Requests.Count(request => request.Method == HttpMethod.Post));
        Assert.AreEqual(3, reads);
    }

    [TestMethod]
    public async Task ReadRetryBudget_IsBoundedAndAuthorizationErrorsAreNotRetried()
    {
        foreach (HttpStatusCode status in new[] { HttpStatusCode.InternalServerError, HttpStatusCode.Unauthorized })
        {
            using var fixture = new IbmClientFixture(request => request.Method == HttpMethod.Post
                ? IbmClientFixture.DefaultResponse(request) : MockHttpMessageHandler.Json("{}", status));
            await Assert.ThrowsExactlyAsync<HttpRequestException>(() => fixture.Client.ExecuteAsync(IbmClientFixture.Request(), CancellationToken.None));
            Assert.AreEqual(status == HttpStatusCode.InternalServerError ? 4 : 2, fixture.Handler.Requests.Length);
        }
    }

    [TestMethod]
    public async Task TransportReadFailuresAndPastRetryAfterDates_AreHandled()
    {
        int reads = 0;
        using var fixture = new IbmClientFixture(request =>
        {
            if (request.Method == HttpMethod.Get)
            {
                int attempt = Interlocked.Increment(ref reads);
                if (attempt == 1) throw new HttpRequestException("Connection closed.");
                if (attempt == 2)
                {
                    var response = MockHttpMessageHandler.Json("{}", HttpStatusCode.ServiceUnavailable);
                    response.Headers.RetryAfter = new RetryConditionHeaderValue(DateTimeOffset.UnixEpoch);
                    return response;
                }
            }
            return IbmClientFixture.DefaultResponse(request);
        });
        await fixture.Client.ExecuteAsync(IbmClientFixture.Request(), CancellationToken.None);
        Assert.AreEqual(4, reads);
    }

    [TestMethod]
    public async Task TaskCanceledException_IsReportedAsTimeoutAndNeverResubmitted()
    {
        using var fixture = new IbmClientFixture((_, _) => Task.FromException<HttpResponseMessage>(new TaskCanceledException("Timeout.")));
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => fixture.Client.ExecuteAsync(IbmClientFixture.Request(), CancellationToken.None));
        Assert.AreEqual(1, fixture.Handler.Requests.Length);
    }

    [TestMethod]
    public async Task PerRequestTimeout_CancelsTheHttpOperation()
    {
        using var fixture = new IbmClientFixture(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return MockHttpMessageHandler.Json("{}");
        }, options => options.RequestTimeout = TimeSpan.FromMilliseconds(30));
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => fixture.Client.ExecuteAsync(IbmClientFixture.Request(), CancellationToken.None));
        Assert.AreEqual(1, fixture.Handler.Requests.Length);
    }

    [TestMethod]
    public async Task JobTimeout_StopsPendingPollingAndReportsRemoteJobId()
    {
        using var fixture = new IbmClientFixture(request => request.Method == HttpMethod.Post ? IbmClientFixture.DefaultResponse(request)
            : MockHttpMessageHandler.Json("""{"status":"Running"}"""), options => options.JobTimeout = TimeSpan.FromMilliseconds(100));
        var error = await Assert.ThrowsExactlyAsync<TimeoutException>(() => fixture.Client.ExecuteAsync(IbmClientFixture.Request(), CancellationToken.None));
        StringAssert.Contains(error.Message, "job-1");
    }

    [TestMethod]
    public async Task JobTimeoutDuringSubmission_IsNotMistakenForCallerCancellation()
    {
        using var fixture = new IbmClientFixture(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return MockHttpMessageHandler.Json("{}");
        }, options => options.JobTimeout = TimeSpan.FromMilliseconds(30));
        var error = await Assert.ThrowsExactlyAsync<TimeoutException>(() => fixture.Client.ExecuteAsync(IbmClientFixture.Request(), CancellationToken.None));
        StringAssert.Contains(error.Message, "before a job ID");
    }

    [TestMethod]
    public async Task CallerCancellation_IsPreservedBeforeAndDuringHttp()
    {
        using var cts = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var fixture = new IbmClientFixture(async (_, token) =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return MockHttpMessageHandler.Json("{}");
        });
        Task execution = fixture.Client.ExecuteAsync(IbmClientFixture.Request(), cts.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cts.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => execution);
        await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.Client.ExecuteAsync(IbmClientFixture.Request(), cts.Token));
        Assert.AreEqual(1, fixture.Handler.Requests.Length);
    }

    [TestMethod]
    [DataRow("Failed")]
    [DataRow("Error")]
    [DataRow("Cancelled")]
    [DataRow("Canceled")]
    public async Task TerminalFailures_StopWithoutFetchingResults(string status)
    {
        using var fixture = new IbmClientFixture(request => request.Method == HttpMethod.Post ? IbmClientFixture.DefaultResponse(request)
            : MockHttpMessageHandler.Json("{\"state\":{\"status\":\"" + status + "\",\"reason\":\"not exposed\"}}"));
        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Client.ExecuteAsync(IbmClientFixture.Request(), CancellationToken.None));
        StringAssert.Contains(error.Message, status);
        Assert.AreEqual(2, fixture.Handler.Requests.Length);
    }

    [TestMethod]
    public async Task PendingStatesAndDone_AreSupportedCaseInsensitively()
    {
        var states = new Queue<string>(["Creating", "Pending", "Validating", "queued", "Running", "done"]);
        using var fixture = new IbmClientFixture(request => request.Method == HttpMethod.Post || request.Uri.AbsolutePath.EndsWith("/results", StringComparison.Ordinal)
            ? IbmClientFixture.DefaultResponse(request) : MockHttpMessageHandler.Json("{\"status\":\"" + states.Dequeue() + "\"}"));
        await fixture.Client.ExecuteAsync(IbmClientFixture.Request(), CancellationToken.None);
        Assert.AreEqual(0, states.Count);
    }

    [TestMethod]
    [DataRow("null", true)]
    [DataRow("{}", true)]
    [DataRow("null", false)]
    [DataRow("{}", false)]
    [DataRow("{\"status\":\"Unknown\"}", false)]
    public async Task MalformedJobResponses_AreRejected(string json, bool submission)
    {
        using var fixture = new IbmClientFixture(request => (request.Method == HttpMethod.Post) == submission
            ? MockHttpMessageHandler.Json(json) : IbmClientFixture.DefaultResponse(request));
        await Assert.ThrowsExactlyAsync<JsonException>(() => fixture.Client.ExecuteAsync(IbmClientFixture.Request(), CancellationToken.None));
    }

    [TestMethod]
    public async Task ApiKey_IsExchangedForIamTokenAndCachedAcrossConcurrentJobs()
    {
        using var fixture = new IbmClientFixture(configure: options => { options.AccessToken = ""; options.ApiKey = "private+api/key"; });
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => fixture.Client.ExecuteAsync(IbmClientFixture.Request(), CancellationToken.None)));
        var iam = fixture.Handler.Requests.Single(request => request.Uri.Host == "iam.cloud.ibm.com");
        Assert.AreEqual("application/x-www-form-urlencoded", iam.ContentType);
        StringAssert.Contains(iam.Body, "grant_type=urn%3Aibm%3Aparams%3Aoauth%3Agrant-type%3Aapikey");
        StringAssert.Contains(iam.Body, "apikey=private%2Bapi%2Fkey");
        Assert.IsNull(iam.Authorization);
        Assert.IsTrue(fixture.Handler.Requests.Where(request => request != iam).All(request => request.Authorization == "Bearer iam-bearer"));
    }

    [TestMethod]
    public async Task ExpiredIamToken_IsRefreshed()
    {
        var clock = new AdjustableTimeProvider();
        using var fixture = new IbmClientFixture(configure: options => { options.AccessToken = ""; options.ApiKey = "key"; }, clock: clock);
        await fixture.Client.ExecuteAsync(IbmClientFixture.Request(), CancellationToken.None);
        clock.Advance(TimeSpan.FromHours(1));
        await fixture.Client.ExecuteAsync(IbmClientFixture.Request(), CancellationToken.None);
        Assert.AreEqual(2, fixture.Handler.Requests.Count(request => request.Uri.Host == "iam.cloud.ibm.com"));
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("{}")]
    [DataRow("{\"access_token\":\"\",\"expires_in\":3600}")]
    [DataRow("{\"access_token\":\"token\",\"expires_in\":0}")]
    public async Task InvalidIamTokens_AreRejectedBeforeSubmittingJobs(string response)
    {
        using var fixture = new IbmClientFixture(_ => MockHttpMessageHandler.Json(response), options => { options.AccessToken = ""; options.ApiKey = "key"; });
        await Assert.ThrowsExactlyAsync<JsonException>(() => fixture.Client.ExecuteAsync(IbmClientFixture.Request(), CancellationToken.None));
        Assert.AreEqual(1, fixture.Handler.Requests.Length);
    }
}
