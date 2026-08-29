# BKE.Notifications

## Purpose

`BKE.Notifications` is the product-neutral software notification capability contract for BKE applications.

It defines typed notification publishing, feed retrieval, lifecycle operations, unread counts, logical actions, and typed failures while leaving transport, persistence, authentication, and presentation to provider/application implementations.

Current package source targets **.NET 10**.

## Capability identity

```text
Capability ID:      bke.notifications
Contract version:   1
```

Exposed by:

```csharp
NotificationCapability.Id
NotificationCapability.ContractVersion
```

## Contract model

```text
PRODUCER / PRODUCT
       ↓
INotificationClient
       ↓
BKE.Notifications contract
       ↓
NOTIFICATION PROVIDER
       ↓
STORE / TRANSPORT / PRODUCT UI
```

The contract describes notification behavior without requiring a specific database, broker, push service, desktop toast system, or UI framework.

## WHAT I NEED

### To publish a notification

Use `NotificationPublishRequest` with:

- `Source`
- `Title`
- `Body`
- optional `Category`
- optional `Severity`
- optional logical `Actions`
- optional `ExpiresAt`

Required text values must be non-empty.

### To read the feed

Use `NotificationFeedQuery` with:

- `Limit` — 1 to 200, default 50
- `IncludeDismissed` — default `false`

### To change notification state

Supply the notification ID to:

- `MarkReadAsync(...)`
- `DismissAsync(...)`

### Provider requirement

The consumer needs an implementation of:

```csharp
INotificationClient
```

The SDK does not choose persistence or transport.

## WHAT I DO

The contract defines these operations:

```csharp
Task<NotificationPublishResult> PublishAsync(...);
Task<NotificationFeedResult> GetFeedAsync(...);
Task<NotificationOperationResult> MarkReadAsync(...);
Task<NotificationOperationResult> DismissAsync(...);
Task<NotificationUnreadCountResult> GetUnreadCountAsync(...);
```

A conforming provider maps its implementation into these stable typed results.

## WHAT I GIVE

### Publish result

`NotificationPublishResult` contains:

- `Status`
- optional `NotificationId`
- optional `Error`

`NotificationPublishStatus`:

- `Accepted`
- `Rejected`
- `Failed`

### Feed result

`NotificationFeedResult` contains:

- `Items`
- optional `Error`
- `Succeeded`

Each `NotificationItem` contains:

- `Id`
- `Source`
- `Title`
- `Body`
- `Category`
- `Severity`
- `CreatedAt`
- optional `ExpiresAt`
- `State`
- logical `Actions`

### Lifecycle operation result

`NotificationOperationResult` reports:

- `Succeeded`
- `NotFound`
- `Rejected`
- `Failed`

plus an optional typed error.

### Unread count result

`NotificationUnreadCountResult` contains:

- `Count`
- optional `Error`
- `Succeeded`

Unread count can never be negative.

## Notification states

```csharp
NotificationState.Unread
NotificationState.Read
NotificationState.Dismissed
```

## Categories

```csharp
NotificationCategory.General
NotificationCategory.Product
NotificationCategory.Licensing
NotificationCategory.Update
NotificationCategory.System
```

## Severities

```csharp
NotificationSeverity.Information
NotificationSeverity.Success
NotificationSeverity.Warning
NotificationSeverity.Error
```

Severity communicates meaning to the consumer/provider. The SDK does not prescribe colors, icons, sounds, or platform presentation.

## Logical actions

A notification may contain `NotificationAction` values with:

- `Id`
- `Label`

Actions are logical identifiers only.

The provider/product decides what an action ID means and how it is presented. The SDK does not embed executable commands or arbitrary URLs as privileged behavior.

## Typed failures

`NotificationError` contains:

- `Code`
- `Message`
- `Retryable`

`NotificationErrorCode`:

- `InvalidRequest`
- `ProviderUnavailable`
- `Rejected`
- `Conflict`
- `ProtocolFailure`
- `Unknown`

This allows products to distinguish invalid input, temporary provider outages, explicit rejection, state conflicts, protocol problems, and unknown failures.

## Capabilities

Current contract supports:

```text
PUBLISH
  input  → NotificationPublishRequest
  output → NotificationPublishResult

READ FEED
  input  → NotificationFeedQuery
  output → NotificationFeedResult

MARK READ
  input  → notification ID
  output → NotificationOperationResult

DISMISS
  input  → notification ID
  output → NotificationOperationResult

UNREAD COUNT
  input  → none
  output → NotificationUnreadCountResult
```

## What this SDK does NOT do

`BKE.Notifications` intentionally does not own:

- Windows toast / Action Center integration
- Android/iOS push services
- WebSockets or SSE infrastructure
- persistence/database implementation
- Redis/message brokers
- notification server hosting
- product notification UI
- producer authentication implementation

Those are provider/application concerns behind or above the contract.

## Minimal publish usage

```csharp
using BKE.Notifications;

public sealed class ProductNotifier
{
    private readonly INotificationClient notifications;

    public ProductNotifier(INotificationClient notifications)
    {
        this.notifications = notifications;
    }

    public Task<NotificationPublishResult> PublishUpdateAvailableAsync(
        CancellationToken cancellationToken = default)
    {
        var request = new NotificationPublishRequest(
            source: "bke-render-dock",
            title: "Update available",
            body: "A new Render Dock version is available.",
            category: NotificationCategory.Update,
            severity: NotificationSeverity.Information);

        return notifications.PublishAsync(request, cancellationToken);
    }
}
```

## Provider responsibility

A provider implementation may own:

- durable storage
- ordering and retention
- producer authentication/authorization
- transport
- synchronization
- delivery infrastructure
- mapping logical actions into safe application behavior

Those choices must remain behind the portable SDK contract.

## Consumer responsibility

The consuming product owns:

- when to publish notifications
- how to render its notification center
- how to interpret logical action IDs
- how to react to typed provider errors
- product-specific filtering and presentation behavior

The product should depend on `INotificationClient`, not on a specific database, broker, or notification service implementation.
