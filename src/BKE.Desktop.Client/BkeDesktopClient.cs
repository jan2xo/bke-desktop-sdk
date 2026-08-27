using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BKE.Desktop.Client;

public sealed class BkeDesktopClient : IDisposable
{
    public static readonly Uri DefaultAgentBaseAddress = new("http://127.0.0.1:43873/");
    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;

    public static BkeDesktopClient Create(HttpClient? httpClient = null)
    {
        if (httpClient is not null) return new BkeDesktopClient(httpClient, false);
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        return new BkeDesktopClient(new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan }, true);
    }

    public BkeDesktopClient(HttpClient httpClient) => this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    private BkeDesktopClient(HttpClient client, bool owns) { httpClient = client; ownsHttpClient = owns; }

    public async Task<AuthorizationResult> AuthorizeAsync(string productId, string version, string installationId, CancellationToken cancellationToken = default)
    {
        if (!Valid(productId, version, installationId)) return new(AuthorizationStatus.InvalidRequest, "Product identity is missing or invalid.");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            using var response = await httpClient.PostAsJsonAsync(new Uri(DefaultAgentBaseAddress, "v1/authorize"), new AuthorizationRequest(productId, version, installationId), timeout.Token);
            if (!response.IsSuccessStatusCode) return MapHttpFailure(response.StatusCode);
            var decision = await response.Content.ReadFromJsonAsync<AuthorizationResponse>(cancellationToken: timeout.Token);
            if (decision?.Authorized is null || string.IsNullOrWhiteSpace(decision.Reason)) return new(AuthorizationStatus.InvalidResponse, "The Licensing Agent returned an invalid authorization response.");
            if (decision.Authorized.Value) return new(AuthorizationStatus.Authorized, decision.Reason);
            var status = decision.Reason.Equals("activation_required", StringComparison.OrdinalIgnoreCase) ? AuthorizationStatus.ActivationRequired : decision.Reason.Equals("unsupported", StringComparison.OrdinalIgnoreCase) || decision.Reason.Equals("unsupported_product", StringComparison.OrdinalIgnoreCase) || decision.Reason.Equals("unsupported_version", StringComparison.OrdinalIgnoreCase) ? AuthorizationStatus.Unsupported : AuthorizationStatus.Denied;
            return new(status, decision.Reason);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return new(AuthorizationStatus.AgentUnavailable, "The Licensing Agent did not respond in time."); }
        catch (HttpRequestException) { return new(AuthorizationStatus.AgentUnavailable, "The Licensing Agent is unavailable."); }
        catch (JsonException) { return new(AuthorizationStatus.InvalidResponse, "The Licensing Agent returned malformed data."); }
    }

    public async Task<LicenseCenterResult> OpenLicenseCenterAsync(string productId, string version, string installationId, CancellationToken cancellationToken = default)
    {
        if (!Valid(productId, version, installationId)) return new(LicenseCenterStatus.InvalidRequest, "Product identity is missing or invalid.");
        var correlationId = Guid.NewGuid().ToString("N");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(15));
            using var response = await httpClient.PostAsJsonAsync(new Uri(DefaultAgentBaseAddress, "v1/license-center/open"), new LicenseCenterRequest(productId, version, installationId, correlationId), timeout.Token);
            if (!response.IsSuccessStatusCode) return MapLicenseCenterHttpFailure(response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<LicenseCenterResponse>(cancellationToken: timeout.Token);
            if (result?.CorrelationId != correlationId || string.IsNullOrWhiteSpace(result.Outcome)) return new(LicenseCenterStatus.InvalidResponse, "The Licensing Agent returned malformed License Center data.");
            return result.Outcome switch { "authorization_refreshed" => new(LicenseCenterStatus.AuthorizationRefreshed, result.Reason ?? string.Empty), "cancelled" => new(LicenseCenterStatus.Cancelled, result.Reason ?? string.Empty), "agent_unavailable" => new(LicenseCenterStatus.AgentUnavailable, result.Reason ?? string.Empty), _ => new(LicenseCenterStatus.Failed, result.Reason ?? string.Empty) };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return new(LicenseCenterStatus.AgentUnavailable, "License Center did not complete in time."); }
        catch (HttpRequestException) { return new(LicenseCenterStatus.AgentUnavailable, "The Licensing Agent is unavailable."); }
        catch (JsonException) { return new(LicenseCenterStatus.InvalidResponse, "The Licensing Agent returned malformed data."); }
    }

    private static AuthorizationResult MapHttpFailure(HttpStatusCode statusCode) => IsUnavailable(statusCode) ? new(AuthorizationStatus.AgentUnavailable, "The Licensing Agent is unavailable.") : new(AuthorizationStatus.InvalidResponse, "The Licensing Agent returned an unexpected HTTP response.");
    private static LicenseCenterResult MapLicenseCenterHttpFailure(HttpStatusCode statusCode) => IsUnavailable(statusCode) ? new(LicenseCenterStatus.AgentUnavailable, "The Licensing Agent is unavailable.") : new(LicenseCenterStatus.InvalidResponse, "The Licensing Agent returned an unexpected HTTP response.");
    private static bool IsUnavailable(HttpStatusCode statusCode) => statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;
    private static bool Valid(params string[] values) => values.All(v => !string.IsNullOrWhiteSpace(v));
    public void Dispose() { if (ownsHttpClient) httpClient.Dispose(); }

    private sealed record AuthorizationRequest([property: JsonPropertyName("product_id")] string ProductId, [property: JsonPropertyName("version")] string Version, [property: JsonPropertyName("installation_id")] string InstallationId);
    private sealed record AuthorizationResponse([property: JsonPropertyName("authorized")] bool? Authorized, [property: JsonPropertyName("reason")] string? Reason);
    private sealed record LicenseCenterRequest([property: JsonPropertyName("product_id")] string ProductId, [property: JsonPropertyName("version")] string Version, [property: JsonPropertyName("installation_id")] string InstallationId, [property: JsonPropertyName("correlation_id")] string CorrelationId);
    private sealed record LicenseCenterResponse([property: JsonPropertyName("outcome")] string? Outcome, [property: JsonPropertyName("reason")] string? Reason, [property: JsonPropertyName("correlation_id")] string? CorrelationId);
}

public enum AuthorizationStatus { Authorized, Denied, ActivationRequired, AgentUnavailable, Unsupported, InvalidRequest, InvalidResponse }
public sealed record AuthorizationResult(AuthorizationStatus Status, string Reason);
public enum LicenseCenterStatus { AuthorizationRefreshed, Cancelled, AgentUnavailable, InvalidRequest, InvalidResponse, Failed }
public sealed record LicenseCenterResult(LicenseCenterStatus Status, string Reason);
