using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BKE.Notifications;
using Xunit;

namespace BKE.Notifications.Tests;

/// <summary>
/// Architecture proof only. No Agent HTTP, persistence, cloud transport, or UI lives here.
///
/// WHAT I NEED:
///   The consumer needs only INotificationClient.
///
/// WHAT I DO:
///   The notification capability exposes typed publish/feed/lifecycle/error/action semantics
///   while keeping provider implementation details outside the consumer.
///
/// WHAT I GIVE:
///   The consumer receives stable Notification* results and can render or react without
///   knowing whether the provider is in-memory, the Licensing Agent, or something else.
/// </summary>
public class NotificationArchitectureProofTests
{
    [Fact]
    public async Task What_I_need_consumer_depends_only_on_INotificationClient()
    {
        var provider = InMemoryNotificationClient.With(
            Item("a", "Provider A notification"));
        var consumer = new NotificationConsumer(provider);

        var titles = await consumer.GetVisibleTitlesAsync();

        Assert.Equal(new[] { "Provider A notification" }, titles);
        Assert.Equal(typeof(INotificationClient), consumer.DependencyType);
    }

    [Fact]
    public async Task Provider_can_be_replaced_without_changing_consumer_code()
    {
        var providerA = InMemoryNotificationClient.With(
            Item("a", "From provider A"));
        var providerB = InMemoryNotificationClient.With(
            Item("b", "From provider B"),
            Item("c", "Also from provider B"));

        var consumerUsingA = new NotificationConsumer(providerA);
        var consumerUsingB = new NotificationConsumer(providerB);

        Assert.Equal(
            new[] { "From provider A" },
            await consumerUsingA.GetVisibleTitlesAsync());

        Assert.Equal(
            new[] { "From provider B", "Also from provider B" },
            await consumerUsingB.GetVisibleTitlesAsync());
    }

    [Fact]
    public async Task What_I_do_contract_preserves_typed_notification_lifecycle()
    {
        var provider = InMemoryNotificationClient.With(
            Item("n1", "Lifecycle proof"));

        var unreadBefore = await provider.GetUnreadCountAsync();
        Assert.True(unreadBefore.Succeeded);
        Assert.Equal(1, unreadBefore.Count);

        var markRead = await provider.MarkReadAsync("n1");
        Assert.Equal(NotificationOperationStatus.Succeeded, markRead.Status);

        var afterRead = await provider.GetFeedAsync(new NotificationFeedQuery());
        Assert.Single(afterRead.Items);
        Assert.Equal(NotificationState.Read, afterRead.Items[0].State);

        var unreadAfter = await provider.GetUnreadCountAsync();
        Assert.True(unreadAfter.Succeeded);
        Assert.Equal(0, unreadAfter.Count);

        var dismiss = await provider.DismissAsync("n1");
        Assert.Equal(NotificationOperationStatus.Succeeded, dismiss.Status);

        var defaultFeed = await provider.GetFeedAsync(new NotificationFeedQuery());
        Assert.Empty(defaultFeed.Items);

        var includingDismissed = await provider.GetFeedAsync(
            new NotificationFeedQuery(includeDismissed: true));
        Assert.Single(includingDismissed.Items);
        Assert.Equal(NotificationState.Dismissed, includingDismissed.Items[0].State);
    }

    [Fact]
    public async Task What_I_give_provider_failure_is_exposed_as_typed_notification_error()
    {
        INotificationClient provider = new UnavailableNotificationClient();
        var consumer = new NotificationConsumer(provider);

        var result = await consumer.GetFeedAsync();

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Error);
        Assert.Equal(NotificationErrorCode.ProviderUnavailable, result.Error!.Code);
        Assert.True(result.Error.Retryable);
    }

    [Fact]
    public async Task Logical_action_crosses_boundary_as_intent_not_execution_authority()
    {
        var action = new NotificationAction("open-update", "Update");
        var provider = InMemoryNotificationClient.With(
            new NotificationItem(
                "update-1",
                "bke-platform",
                "Update available",
                "A new version is ready.",
                DateTimeOffset.UtcNow,
                actions: new[] { action },
                category: NotificationCategory.Update));
        var consumer = new NotificationConsumer(provider);

        var feed = await consumer.GetFeedAsync();

        var deliveredAction = Assert.Single(Assert.Single(feed.Items).Actions);
        Assert.Equal("open-update", deliveredAction.Id);
        Assert.Equal("Update", deliveredAction.Label);

        var properties = typeof(NotificationAction)
            .GetProperties()
            .Select(property => property.Name)
            .OrderBy(name => name)
            .ToArray();

        Assert.Equal(new[] { "Id", "Label" }, properties);
    }

    [Fact]
    public async Task Publish_is_provider_owned_but_result_remains_stable_to_consumer()
    {
        var provider = InMemoryNotificationClient.With();
        var request = new NotificationPublishRequest(
            source: "air-stack",
            title: "Automation finished",
            body: "The automation completed.",
            category: NotificationCategory.Product,
            severity: NotificationSeverity.Success);

        var result = await provider.PublishAsync(request);

        Assert.Equal(NotificationPublishStatus.Accepted, result.Status);
        Assert.NotNull(result.NotificationId);

        var feed = await provider.GetFeedAsync(new NotificationFeedQuery());
        var published = Assert.Single(feed.Items);
        Assert.Equal("air-stack", published.Source);
        Assert.Equal("Automation finished", published.Title);
        Assert.Equal(NotificationCategory.Product, published.Category);
        Assert.Equal(NotificationSeverity.Success, published.Severity);
    }

    private static NotificationItem Item(string id, string title) =>
        new(
            id,
            "architecture-proof",
            title,
            "body",
            DateTimeOffset.UtcNow);

    private sealed class NotificationConsumer
    {
        private readonly INotificationClient notifications;

        public NotificationConsumer(INotificationClient notifications)
        {
            this.notifications = notifications;
        }

        public Type DependencyType => typeof(INotificationClient);

        public Task<NotificationFeedResult> GetFeedAsync(
            CancellationToken cancellationToken = default) =>
            notifications.GetFeedAsync(new NotificationFeedQuery(), cancellationToken);

        public async Task<string[]> GetVisibleTitlesAsync(
            CancellationToken cancellationToken = default)
        {
            var feed = await GetFeedAsync(cancellationToken);
            return feed.Items.Select(item => item.Title).ToArray();
        }
    }

    private sealed class InMemoryNotificationClient : INotificationClient
    {
        private readonly Dictionary<string, NotificationItem> items;
        private int nextId;

        private InMemoryNotificationClient(IEnumerable<NotificationItem> initialItems)
        {
            items = initialItems.ToDictionary(item => item.Id, StringComparer.Ordinal);
            nextId = items.Count;
        }

        public static InMemoryNotificationClient With(params NotificationItem[] items) =>
            new(items);

        public Task<NotificationPublishResult> PublishAsync(
            NotificationPublishRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            var id = $"published-{Interlocked.Increment(ref nextId)}";
            items[id] = new NotificationItem(
                id,
                request.Source,
                request.Title,
                request.Body,
                DateTimeOffset.UtcNow,
                NotificationState.Unread,
                request.Category,
                request.Severity,
                request.Actions,
                request.ExpiresAt);

            return Task.FromResult(NotificationPublishResult.Accepted(id));
        }

        public Task<NotificationFeedResult> GetFeedAsync(
            NotificationFeedQuery query,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(query);

            var feed = items.Values
                .Where(item => query.IncludeDismissed || item.State != NotificationState.Dismissed)
                .OrderBy(item => item.CreatedAt)
                .Take(query.Limit)
                .ToArray();

            return Task.FromResult(NotificationFeedResult.Success(feed));
        }

        public Task<NotificationOperationResult> MarkReadAsync(
            string notificationId,
            CancellationToken cancellationToken = default)
        {
            if (!items.TryGetValue(notificationId, out var item))
                return Task.FromResult(NotificationOperationResult.NotFound());

            items[notificationId] = CopyWithState(item, NotificationState.Read);
            return Task.FromResult(NotificationOperationResult.Succeeded());
        }

        public Task<NotificationOperationResult> DismissAsync(
            string notificationId,
            CancellationToken cancellationToken = default)
        {
            if (!items.TryGetValue(notificationId, out var item))
                return Task.FromResult(NotificationOperationResult.NotFound());

            items[notificationId] = CopyWithState(item, NotificationState.Dismissed);
            return Task.FromResult(NotificationOperationResult.Succeeded());
        }

        public Task<NotificationUnreadCountResult> GetUnreadCountAsync(
            CancellationToken cancellationToken = default)
        {
            var count = items.Values.Count(item => item.State == NotificationState.Unread);
            return Task.FromResult(NotificationUnreadCountResult.Success(count));
        }

        private static NotificationItem CopyWithState(
            NotificationItem item,
            NotificationState state) =>
            new(
                item.Id,
                item.Source,
                item.Title,
                item.Body,
                item.CreatedAt,
                state,
                item.Category,
                item.Severity,
                item.Actions,
                item.ExpiresAt);
    }

    private sealed class UnavailableNotificationClient : INotificationClient
    {
        private static readonly NotificationError Error = new(
            NotificationErrorCode.ProviderUnavailable,
            "The architecture-proof provider is unavailable.",
            retryable: true);

        public Task<NotificationPublishResult> PublishAsync(
            NotificationPublishRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(NotificationPublishResult.Failed(Error));

        public Task<NotificationFeedResult> GetFeedAsync(
            NotificationFeedQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(NotificationFeedResult.Failed(Error));

        public Task<NotificationOperationResult> MarkReadAsync(
            string notificationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(NotificationOperationResult.Failed(Error));

        public Task<NotificationOperationResult> DismissAsync(
            string notificationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(NotificationOperationResult.Failed(Error));

        public Task<NotificationUnreadCountResult> GetUnreadCountAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(NotificationUnreadCountResult.Failed(Error));
    }
}
