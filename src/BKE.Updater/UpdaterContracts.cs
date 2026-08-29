using System;
using System.Threading;
using System.Threading.Tasks;

namespace BKE.Updater;

public static class UpdateCapability
{
    public const string Id = "bke.updates.check";
    public const int ContractVersion = 1;
}

public sealed record UpdateCheckRequest
{
    public string ProductId { get; }
    public string CurrentVersion { get; }
    public string? RequestedVersion { get; }

    public UpdateCheckRequest(string productId, string currentVersion, string? requestedVersion = null)
    {
        ProductId = RequireValue(productId, nameof(productId));
        CurrentVersion = RequireValue(currentVersion, nameof(currentVersion));
        RequestedVersion = NormalizeOptionalValue(requestedVersion, nameof(requestedVersion));
    }

    private static string RequireValue(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty value is required.", parameterName);
        }

        return value;
    }

    private static string? NormalizeOptionalValue(string? value, string parameterName)
    {
        if (value is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("When provided, the value must be non-empty.", parameterName);
        }

        return value;
    }
}

public enum UpdateCheckStatus
{
    UpToDate,
    UpdateAvailable,
    Deferred,
    Failed
}

public enum UpdateErrorCode
{
    InvalidRequest,
    ProviderUnavailable,
    TransportFailure,
    ProtocolFailure,
    MalformedResponse,
    VerificationFailure,
    PolicyDenied,
    Unknown
}

public sealed record UpdateError
{
    public UpdateErrorCode Code { get; }
    public string Message { get; }
    public bool Retryable { get; }

    public UpdateError(UpdateErrorCode code, string message, bool retryable = false)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("An error message is required.", nameof(message));
        }

        Code = code;
        Message = message;
        Retryable = retryable;
    }
}

public sealed record UpdateCheckResult
{
    public UpdateCheckStatus Status { get; }
    public string? AvailableVersion { get; }
    public UpdateError? Error { get; }

    private UpdateCheckResult(UpdateCheckStatus status, string? availableVersion, UpdateError? error)
    {
        Status = status;
        AvailableVersion = availableVersion;
        Error = error;
    }

    public static UpdateCheckResult UpToDate() =>
        new(UpdateCheckStatus.UpToDate, null, null);

    public static UpdateCheckResult UpdateAvailable(string availableVersion)
    {
        if (string.IsNullOrWhiteSpace(availableVersion))
        {
            throw new ArgumentException("An available version is required.", nameof(availableVersion));
        }

        return new UpdateCheckResult(UpdateCheckStatus.UpdateAvailable, availableVersion, null);
    }

    public static UpdateCheckResult Deferred(string? availableVersion = null)
    {
        if (availableVersion is not null && string.IsNullOrWhiteSpace(availableVersion))
        {
            throw new ArgumentException("When provided, the available version must be non-empty.", nameof(availableVersion));
        }

        return new UpdateCheckResult(UpdateCheckStatus.Deferred, availableVersion, null);
    }

    public static UpdateCheckResult Failed(UpdateError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new UpdateCheckResult(UpdateCheckStatus.Failed, null, error);
    }
}

public interface IUpdateClient
{
    Task<UpdateCheckResult> CheckAsync(
        UpdateCheckRequest request,
        CancellationToken cancellationToken = default);
}
