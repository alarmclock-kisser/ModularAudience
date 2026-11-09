namespace ModularAudience.Quantum.Models;

public sealed class IbmQuantumOptions
{
    public Uri BaseAddress { get; set; } = new("https://quantum.cloud.ibm.com/api/v1/");
    public Uri IamTokenEndpoint { get; set; } = new("https://iam.cloud.ibm.com/identity/token");
    public string ApiKey { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string ServiceCrn { get; set; } = string.Empty;
    public string Backend { get; set; } = string.Empty;
    public string ApiVersion { get; set; } = "2026-04-15";
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan JobTimeout { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(500);
    public int MaxReadRetries { get; set; } = 2;

    internal void Validate()
    {
        ValidateEndpoint(this.BaseAddress, nameof(this.BaseAddress));
        ValidateEndpoint(this.IamTokenEndpoint, nameof(this.IamTokenEndpoint));
        if (!this.BaseAddress.AbsolutePath.EndsWith('/'))
            throw new ArgumentException("The IBM Quantum base address must end with '/'.", nameof(this.BaseAddress));
        if (string.IsNullOrWhiteSpace(this.ApiKey) && string.IsNullOrWhiteSpace(this.AccessToken))
            throw new ArgumentException("Configure an IBM Cloud API key or an IAM bearer token.");
        ArgumentException.ThrowIfNullOrWhiteSpace(this.Backend);
        ArgumentException.ThrowIfNullOrWhiteSpace(this.ServiceCrn);
        if (!DateOnly.TryParseExact(this.ApiVersion, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out _))
            throw new ArgumentException("The IBM API version must use yyyy-MM-dd.", nameof(this.ApiVersion));
        ValidateDuration(this.RequestTimeout, nameof(this.RequestTimeout), allowZero: false);
        ValidateDuration(this.JobTimeout, nameof(this.JobTimeout), allowZero: false);
        ValidateDuration(this.PollInterval, nameof(this.PollInterval), allowZero: false);
        ValidateDuration(this.RetryDelay, nameof(this.RetryDelay), allowZero: true);
        if (this.MaxReadRetries is < 0 or > 10)
            throw new ArgumentOutOfRangeException(nameof(this.MaxReadRetries), "Read retries must be between 0 and 10.");
    }

    private static void ValidateEndpoint(Uri? endpoint, string name)
    {
        if (endpoint is null || !endpoint.IsAbsoluteUri || endpoint.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(endpoint.UserInfo) || !string.IsNullOrEmpty(endpoint.Query) || !string.IsNullOrEmpty(endpoint.Fragment))
            throw new ArgumentException("Quantum endpoints must be absolute HTTPS addresses without credentials, queries or fragments.", name);
    }

    private static void ValidateDuration(TimeSpan value, string name, bool allowZero)
    {
        if (value < TimeSpan.Zero || (!allowZero && value == TimeSpan.Zero) || value.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(name, "Timeouts and intervals must be finite, positive durations (retry delay may be zero).");
    }
}
