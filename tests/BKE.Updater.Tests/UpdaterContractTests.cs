using BKE.Updater;

namespace BKE.Updater.Tests;

public class UpdaterContractTests
{
    [Fact]
    public void Request_carries_product_identity_and_current_version()
    {
        var request = new UpdateCheckRequest("bke-test-product", "1.0.0");
        Assert.Equal("bke-test-product", request.ProductId);
        Assert.Equal("1.0.0", request.CurrentVersion);
    }

    [Fact]
    public async Task Contract_is_async_friendly()
    {
        IUpdateClient client = new StubClient();
        var result = await client.CheckAsync(new UpdateCheckRequest("p", "1.0.0"));
        Assert.Equal(UpdateState.UpToDate, result.State);
    }

    private sealed class StubClient : IUpdateClient
    {
        public Task<UpdateCheckResult> CheckAsync(UpdateCheckRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new UpdateCheckResult(UpdateState.UpToDate, false));
    }
}