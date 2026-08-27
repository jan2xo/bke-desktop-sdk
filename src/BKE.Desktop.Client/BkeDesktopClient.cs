using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;


namespace BKE.Desktop.Client;

public sealed class BkeDesktopClient : IDisposable
{
    public static readonly Uri DefaultAgentBaseAddress = new("http://127.0.0.1:43873/");
    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;

    public static BkeDesktopClient Create(HttpClient? httpClient = null)
    {
        if (httpClient is not null)
            return new BkeDesktopClient(httpClient, owns: false);

        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false
        };

        return new BkeDesktopClient(
            new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
            owns: true);
    }

    public BkeDesktopClient(HttpClient httpClient)
        : this(httpClient ?? throw new ArgumentNullException(nameof(httpClient)), owns: false)
    {
    }

    private BkeDesktopClient(HttpClient client, bool owns)
    {
        httpClient = client;
        ownsHttpClient = owns;
    }

    public async Task<AuthorizationResult> AuthorizeAsync(
        string productId,
        string version,
        string installationId,
        CancellationToken cancellationToken = default)
    {
        if (!Valid(productId, version, installationId))
            return new(AuthorizationStatus.InvalidRequest, "Product identity is missing or invalid.");

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));

            using var response = await httpClient.PostAsJsonAsync(
                new Uri(DefaultAgentBaseAddress, "v1/authorize"),
                new AuthorizationRequest(productId, version, installationId),
                timeout.Token);

            if (!response.IsSuccessStatusCode)
                return new(MapAuthorizationHttpFailure(response.StatusCode),
                    $"The Licensing Agent rejected the authorization request with HTTP {(int)response.StatusCode}.");

            var decision = await response.Content.ReadFromJsonAsync<AuthorizationResponse>(
                cancellationToken: timeout.Token);

            if (decision?.Authorized is null || string.IsNullOrWhiteSpace(decision.Reason))
                return new(AuthorizationStatus.InvalidResponse,
                    "The Licensing Agent returned an invalid authorization response.");

            if (decision.Authorized.Value)
                return new(AuthorizationStatus.Authorized, decision.Reason);

            var status = decision.Reason.Equals("activation_required", StringComparison.OrdinalIgnoreCase)
                ? AuthorizationStatus.ActivationRequired
                : decision.Reason.Equals("unsupported", StringComparison.OrdinalIgnoreCase)
                    || decision.Reason.Equals("unsupported_product", StringComparison.OrdinalIgnoreCase)
                    || decision.Reason.Equals("unsupported_version", StringComparison.OrdinalIgnoreCase)
                    ? AuthorizationStatus.Unsupported
                    : AuthorizationStatus.Denied;

            return new(status, decision.Reason);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(AuthorizationStatus.Timeout, "The Licensing Agent did not respond in time.");
        }
        catch (HttpRequestException)
        {
            return new(AuthorizationStatus.AgentUnavailable, "The Licensing Agent is unavailable.");
        }
        catch (JsonException)
        {
            return new(AuthorizationStatus.InvalidResponse, "The Licensing Agent returned malformed data.");
        }
    }

    public async Task<LicenseCenterResult> OpenLicenseCenterAsync(
        string productId,
        string version,
        string installationId,
        CancellationToken cancellationToken = default)
    {
        if (!Valid(productId, version, installationId))
            return new(LicenseCenterStatus.InvalidRequest, "Product identity is missing or invalid.");

        var correlationId = Guid.NewGuid().ToString("N");

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(15));

            using var response = await httpClient.PostAsJsonAsync(
                new Uri(DefaultAgentBaseAddress, "v1/license-center/open"),
                new LicenseCenterRequest(productId, version, installationId, correlationId),
                timeout.Token);

            if (!response.IsSuccessStatusCode)
                return new(MapLicenseCenterHttpFailure(response.StatusCode),
                    $"The Licensing Agent rejected the License Center request with HTTP {(int)response.StatusCode}.");

            var result = await response.Content.ReadFromJsonAsync<LicenseCenterResponse>(
                cancellationToken: timeout.Token);

            if (result?.CorrelationId != correlationId || string.IsNullOrWhiteSpace(result.Outcome))
                return new(LicenseCenterStatus.InvalidResponse,
                    "The Licensing Agent returned malformed License Center data.");

            return result.Outcome switch
            {
                "authorization_refreshed" => new(LicenseCenterStatus.AuthorizationRefreshed, result.Reason ?? string.Empty),
                "completed" => new(LicenseCenterStatus.Completed, result.Reason ?? string.Empty),
                "cancelled" => new(LicenseCenterStatus.Cancelled, result.Reason ?? string.Empty),
                "agent_unavailable" => new(LicenseCenterStatus.AgentUnavailable, result.Reason ?? string.Empty),
                "invalid_product_context" => new(LicenseCenterStatus.InvalidProductContext, result.Reason ?? string.Empty),
                "incompatible_product_version" => new(LicenseCenterStatus.IncompatibleProductVersion, result.Reason ?? string.Empty),
                "activation_failed" => new(LicenseCenterStatus.ActivationFailed, result.Reason ?? string.Empty),
                "failed" => new(LicenseCenterStatus.Failed, result.Reason ?? string.Empty),
                _ => new(LicenseCenterStatus.Unsupported, result.Reason ?? $"Unsupported outcome: {result.Outcome}")
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(LicenseCenterStatus.Timeout, "License Center did not complete in time.");
        }
        catch (HttpRequestException)
        {
            return new(LicenseCenterStatus.AgentUnavailable, "The Licensing Agent is unavailable.");
        }
        catch (JsonException)
        {
            return new(LicenseCenterStatus.InvalidResponse, "The Licensing Agent returned malformed data.");
        }
    }

    private static AuthorizationStatus MapAuthorizationHttpFailure(HttpStatusCode statusCode) =>
        (int)statusCode >= 500
            ? AuthorizationStatus.AgentUnavailable
            : AuthorizationStatus.ProtocolRejected;

    private static LicenseCenterStatus MapLicenseCenterHttpFailure(HttpStatusCode statusCode) =>
        (int)statusCode >= 500
            ? LicenseCenterStatus.AgentUnavailable
            : LicenseCenterStatus.ProtocolRejected;

    private static bool Valid(params string[] values) =>
        values.All(value => !string.IsNullOrWhiteSpace(value));

    public void Dispose()
    {
        if (ownsHttpClient)
            httpClient.Dispose();
    }

    private sealed record AuthorizationRequest(
        [property: JsonPropertyName("product_id")] string ProductId,
        [property: JsonPropertyName("version")] string Version,
        [property: JsonPropertyName("installation_id")] string InstallationId);

    private sealed record AuthorizationResponse(
        [property: JsonPropertyName("authorized")] bool? Authorized,
        [property: JsonPropertyName("reason")] string? Reason);

    private sealed record LicenseCenterRequest(
        [property: JsonPropertyName("product_id")] string ProductId,
        [property: JsonPropertyName("version")] string Version,
        [property: JsonPropertyName("installation_id")] string InstallationId,
        [property: JsonPropertyName("correlation_id")] string CorrelationId);

    private sealed record LicenseCenterResponse(
        [property: JsonPropertyName("outcome")] string? Outcome,
        [property: JsonPropertyName("reason")] string? Reason,
        [property: JsonPropertyName("correlation_id")] string? CorrelationId);
}

public enum AuthorizationStatus
{
    Authorized,
    Denied,
    ActivationRequired,
    AgentUnavailable,
    Timeout,
    ProtocolRejected,
    Unsupported,
    InvalidRequest,
    InvalidResponse
}

public sealed record AuthorizationResult(AuthorizationStatus Status, string Reason);

public enum LicenseCenterStatus
{
    Completed,
    AuthorizationRefreshed,
    Cancelled,
    AgentUnavailable,
    Timeout,
    ProtocolRejected,
    InvalidProductContext,
    IncompatibleProductVersion,
    ActivationFailed,
    Unsupported,
    InvalidRequest,
    InvalidResponse,
    Failed
}

public sealed record LicenseCenterResult(LicenseCenterStatus Status, string Reason);
