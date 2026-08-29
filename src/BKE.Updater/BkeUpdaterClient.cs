using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace BKE.Updater;

/// <summary>
/// Default BKE updater client backed by the machine-local BKE Licensing Agent.
/// The Agent remains the trusted provider and authority boundary; this client only
/// translates the fixed loopback protocol into the public BKE.Updater contract.
/// </summary>
public sealed class BkeUpdaterClient : IUpdateClient, IDisposable
{
    public static readonly Uri DefaultAgentBaseAddress = new("http://127.0.0.1:43873/");

    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;

    public static BkeUpdaterClient Create()
    {
        var handler = CreateDefaultHandler();
        return new BkeUpdaterClient(
            new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
            owns: true);
    }

    internal static BkeUpdaterClient Create(HttpClient httpClient) =>
        new(httpClient ?? throw new ArgumentNullException(nameof(httpClient)), owns: false);

    internal static HttpClientHandler CreateDefaultHandler() =>
        new()
        {
            AllowAutoRedirect = false,
            UseProxy = false
        };

    private BkeUpdaterClient(HttpClient client, bool owns)
    {
        httpClient = client;
        ownsHttpClient = owns;
    }

    public async Task<UpdateCheckResult> CheckAsync(
        UpdateCheckRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));

            using var response = await httpClient.PostAsJsonAsync(
                new Uri(DefaultAgentBaseAddress, "v1/updates/check"),
                new AgentUpdateCheckRequest(
                    request.ProductId,
                    request.CurrentVersion,
                    request.RequestedVersion),
                timeout.Token).ConfigureAwait(false);

            AgentUpdateCheckResponse? document;
            try
            {
                document = await response.Content.ReadFromJsonAsync<AgentUpdateCheckResponse>(
                    cancellationToken: timeout.Token).ConfigureAwait(false);
            }
            catch (JsonException)
            {
                return Failure(
                    UpdateErrorCode.MalformedResponse,
                    "The local update provider returned malformed JSON.");
            }

            if (document is null)
            {
                return Failure(
                    UpdateErrorCode.MalformedResponse,
                    "The local update provider returned an empty response.");
            }

            if (!string.Equals(document.CapabilityId, UpdateCapability.Id, StringComparison.Ordinal) ||
                document.ContractVersion != UpdateCapability.ContractVersion)
            {
                return Failure(
                    UpdateErrorCode.ProtocolFailure,
                    "The local update provider returned an incompatible capability contract.");
            }

            var result = MapResponse(document);

            if (!response.IsSuccessStatusCode && result.Status != UpdateCheckStatus.Failed)
            {
                return Failure(
                    UpdateErrorCode.ProtocolFailure,
                    $"The local update provider returned HTTP {(int)response.StatusCode} with a non-failure result.");
            }

            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure(
                UpdateErrorCode.ProviderUnavailable,
                "The local update provider did not respond in time.",
                retryable: true);
        }
        catch (HttpRequestException)
        {
            return Failure(
                UpdateErrorCode.ProviderUnavailable,
                "The local update provider is unavailable.",
                retryable: true);
        }
    }

    private static UpdateCheckResult MapResponse(AgentUpdateCheckResponse document)
    {
        switch (document.Status)
        {
            case "UpToDate":
                if (document.AvailableVersion is not null || document.Error is not null)
                    return MalformedInvariant("UpToDate");
                return UpdateCheckResult.UpToDate();

            case "UpdateAvailable":
                if (string.IsNullOrWhiteSpace(document.AvailableVersion) || document.Error is not null)
                    return MalformedInvariant("UpdateAvailable");
                return UpdateCheckResult.UpdateAvailable(document.AvailableVersion);

            case "Deferred":
                if (document.Error is not null ||
                    (document.AvailableVersion is not null && string.IsNullOrWhiteSpace(document.AvailableVersion)))
                {
                    return MalformedInvariant("Deferred");
                }
                return UpdateCheckResult.Deferred(document.AvailableVersion);

            case "Failed":
                if (document.AvailableVersion is not null || document.Error is null)
                    return MalformedInvariant("Failed");

                if (!Enum.TryParse<UpdateErrorCode>(document.Error.Code, ignoreCase: false, out var code))
                {
                    return Failure(
                        UpdateErrorCode.ProtocolFailure,
                        "The local update provider returned an unsupported error code.");
                }

                if (string.IsNullOrWhiteSpace(document.Error.Message))
                    return MalformedInvariant("Failed");

                return UpdateCheckResult.Failed(new UpdateError(
                    code,
                    document.Error.Message,
                    document.Error.Retryable));

            default:
                return Failure(
                    UpdateErrorCode.ProtocolFailure,
                    "The local update provider returned an unsupported update status.");
        }
    }

    private static UpdateCheckResult MalformedInvariant(string status) =>
        Failure(
            UpdateErrorCode.MalformedResponse,
            $"The local update provider returned an invalid {status} result.");

    private static UpdateCheckResult Failure(
        UpdateErrorCode code,
        string message,
        bool retryable = false) =>
        UpdateCheckResult.Failed(new UpdateError(code, message, retryable));

    public void Dispose()
    {
        if (ownsHttpClient)
            httpClient.Dispose();
    }

    private sealed record AgentUpdateCheckRequest(
        [property: JsonPropertyName("product_id")] string ProductId,
        [property: JsonPropertyName("current_version")] string CurrentVersion,
        [property: JsonPropertyName("requested_version")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RequestedVersion);

    private sealed record AgentUpdateCheckResponse(
        [property: JsonPropertyName("capability_id")] string? CapabilityId,
        [property: JsonPropertyName("contract_version")] int ContractVersion,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("available_version")] string? AvailableVersion,
        [property: JsonPropertyName("error")] AgentUpdateError? Error);

    private sealed record AgentUpdateError(
        [property: JsonPropertyName("code")] string? Code,
        [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("retryable")] bool Retryable);
}
