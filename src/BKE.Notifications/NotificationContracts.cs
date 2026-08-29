using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BKE.Notifications;

public enum NotificationSeverity { Information, Success, Warning, Error }

public enum NotificationCategory { General, Product, Licensing, Update, System }

public sealed record NotificationAction(string Id, string Label);

public sealed record NotificationMessage(
    string Id,
    string Source,
    string Title,
    string Body,
    NotificationCategory Category = NotificationCategory.General,
    NotificationSeverity Severity = NotificationSeverity.Information,
    IReadOnlyList<NotificationAction>? Actions = null);

public enum NotificationState { Unknown, Delivered, Read, Dismissed, Failed }

public sealed record NotificationResult(NotificationState State, NotificationError? Error = null);

public sealed record NotificationError(string Code, string Message, bool Retryable = false);

public interface INotificationClient
{
    Task<NotificationResult> PublishAsync(
        NotificationMessage message,
        CancellationToken cancellationToken = default);
}