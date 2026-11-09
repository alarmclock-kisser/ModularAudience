using System.Net;
using System.Text;

namespace ModularAudience.Quantum.Tests.Mocks;

internal sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Authorization,
    string? ServiceCrn, string? ApiVersion, string? ContentType, string Body);

internal sealed class MockHttpMessageHandler(Func<RecordedRequest, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    private readonly List<RecordedRequest> requests = [];
    private readonly Lock requestLock = new();
    internal RecordedRequest[] Requests { get { lock (this.requestLock) return this.requests.ToArray(); } }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var snapshot = new RecordedRequest(request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(),
            request.Headers.TryGetValues("Service-CRN", out var crn) ? crn.Single() : null,
            request.Headers.TryGetValues("IBM-API-Version", out var version) ? version.Single() : null,
            request.Content?.Headers.ContentType?.MediaType,
            request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
        lock (this.requestLock) this.requests.Add(snapshot);
        return await respond(snapshot, cancellationToken);
    }

    internal static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };
}
