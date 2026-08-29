namespace BKE.Updater;

public sealed record UpdateCheckRequest(
    string ProductId,
    string CurrentVersion,
    string? RequestedVersion = null,
    UpdateRequestIntent Intent = UpdateRequestIntent.CheckForUpdates);

public enum UpdateRequestIntent { CheckForUpdates, RefreshMetadata, DownloadApprovedUpdate }

public enum UpdateState { Unknown, UpToDate, UpdateAvailable, Deferred, Failed }

public enum UpdateErrorCode { None, InvalidRequest, Unavailable, TransportFailure, VerificationFailure, PolicyDenied, Unknown }

public sealed record UpdateError(UpdateErrorCode Code, string Message, bool Retryable = false);

public sealed record UpdateCheckResult(
    UpdateState State,
    bool UpdateAvailable,
    string? AvailableVersion = null,
    UpdateError? Error = null);

public interface IUpdateClient
{
    Task<UpdateCheckResult> CheckAsync(UpdateCheckRequest request, CancellationToken cancellationToken = default);
}