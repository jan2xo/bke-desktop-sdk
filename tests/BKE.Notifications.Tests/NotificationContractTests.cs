using BKE.Notifications;

namespace BKE.Notifications.Tests;

public class NotificationContractTests
{
    [Fact]
    public void Message_carries_source_category_severity_and_actions()
    {
        var message = new NotificationMessage("n1", "Air Stack", "Update", "Ready",
            NotificationCategory.Update, NotificationSeverity.Information,
            new[] { new NotificationAction("open", "Open") });
        Assert.Equal("Air Stack", message.Source);
        Assert.Single(message.Actions!);
    }

    [Fact]
    public async Task Contract_is_async_friendly()
    {
        INotificationClient client = new StubClient();
        var result = await client.PublishAsync(new NotificationMessage("n1", "p", "t", "b"));
        Assert.Equal(NotificationState.Delivered, result.State);
    }

    private sealed class StubClient : INotificationClient
    {
        public Task<NotificationResult> PublishAsync(NotificationMessage message, CancellationToken cancellationToken = default) =>
            Task.FromResult(new NotificationResult(NotificationState.Delivered));
    }
}