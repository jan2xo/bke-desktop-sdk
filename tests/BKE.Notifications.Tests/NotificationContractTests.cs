using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BKE.Notifications;
using Xunit;

namespace BKE.Notifications.Tests;

public class NotificationContractTests
{
    [Fact]
    public void Capability_identity_is_stable()
    {
        Assert.Equal("bke.notifications", NotificationCapability.Id);
        Assert.Equal(1, NotificationCapability.ContractVersion);
    }

    [Fact]
    public void Publish_request_contains_message_metadata_but_not_provider_generated_identity()
    {
        var request = new NotificationPublishRequest(
            "Air Stack",
            "Update",
            "Ready",
            NotificationCategory.Update,
            NotificationSeverity.Information,
            new[] { new NotificationAction("open-update", "Open") });

        Assert.Equal("Air Stack", request.Source);
        Assert.Single(request.Actions);

        var properties = typeof(NotificationPublishRequest)
            .GetProperties()
            .Select(property => property.Name)
            .OrderBy(name => name)
            .ToArray();

        Assert.Equal(
            new[] { "Actions", "Body", "Category", "ExpiresAt", "Severity", "Source", "Title" },
            properties);
    }

    [Fact]
    public void Action_is_logical_only_and_cannot_carry_execution_authority()
    {
        var properties = typeof(NotificationAction)
            .GetProperties()
            .Select(property => property.Name)
            .OrderBy(name => name)
            .ToArray();

        Assert.Equal(new[] { "Id", "Label" }, properties);
    }

    [Fact]
    public void Publish_result_is_separate_from_notification_lifecycle_state()
    {
        var publish = NotificationPublishResult.Accepted("n1");
        var item = new NotificationItem(
            "n1",
            "Air Stack",
            "Update",
            "Ready",
            DateTimeOffset.UtcNow,
            NotificationState.Unread,
            NotificationCategory.Update);

        Assert.Equal(NotificationPublishStatus.Accepted, publish.Status);
        Assert.Equal("n1", publish.NotificationId);
        Assert.Null(publish.Error);
        Assert.Equal(NotificationState.Unread, item.State);
    }

    [Fact]
    public void Rejected_publish_requires_a_typed_error_and_no_notification_id()
    {
        var error = new NotificationError(
            NotificationErrorCode.Rejected,
            "The provider rejected the notification.");

        var result = NotificationPublishResult.Rejected(error);

        Assert.Equal(NotificationPublishStatus.Rejected, result.Status);
        Assert.Null(result.NotificationId);
        Assert.Same(error, result.Error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public void Feed_query_rejects_unbounded_limits(int limit)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new NotificationFeedQuery(limit));
    }

    [Fact]
    public void Unread_count_cannot_be_negative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NotificationUnreadCountResult.Success(-1));
    }

    [Fact]
    public async Task Contract_exposes_publish_feed_and_lifecycle_operations_as_async_ports()
    {
        INotificationClient client = new StubClient();

        var publish = await client.PublishAsync(new NotificationPublishRequest("p", "t", "b"));
        var feed = await client.GetFeedAsync(new NotificationFeedQuery());
        var markRead = await client.MarkReadAsync("n1");
        var dismiss = await client.DismissAsync("n1");
        var unread = await client.GetUnreadCountAsync();

        Assert.Equal(NotificationPublishStatus.Accepted, publish.Status);
        Assert.True(feed.Succeeded);
        Assert.Equal(NotificationOperationStatus.Succeeded, markRead.Status);
        Assert.Equal(NotificationOperationStatus.Succeeded, dismiss.Status);
        Assert.True(unread.Succeeded);
        Assert.Equal(1, unread.Count);
    }

    private sealed class StubClient : INotificationClient
    {
        public Task<NotificationPublishResult> PublishAsync(
            NotificationPublishRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(NotificationPublishResult.Accepted("n1"));

        public Task<NotificationFeedResult> GetFeedAsync(
            NotificationFeedQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(NotificationFeedResult.Success(new[]
            {
                new NotificationItem("n1", "p", "t", "b", DateTimeOffset.UtcNow)
            }));

        public Task<NotificationOperationResult> MarkReadAsync(
            string notificationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(NotificationOperationResult.Succeeded());

        public Task<NotificationOperationResult> DismissAsync(
            string notificationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(NotificationOperationResult.Succeeded());

        public Task<NotificationUnreadCountResult> GetUnreadCountAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(NotificationUnreadCountResult.Success(1));
    }
}
