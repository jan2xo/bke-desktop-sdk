using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BKE.Updater;
using Xunit;

namespace BKE.Updater.Tests;

public class UpdaterContractTests
{
    [Fact]
    public void Capability_identity_is_stable()
    {
        Assert.Equal("bke.updates.check", UpdateCapability.Id);
        Assert.Equal(1, UpdateCapability.ContractVersion);
    }

    [Fact]
    public void Request_carries_only_product_and_version_contract_inputs()
    {
        var request = new UpdateCheckRequest("bke-test-product", "1.0.0", "2.0.0");

        Assert.Equal("bke-test-product", request.ProductId);
        Assert.Equal("1.0.0", request.CurrentVersion);
        Assert.Equal("2.0.0", request.RequestedVersion);

        var properties = typeof(UpdateCheckRequest)
            .GetProperties()
            .Select(property => property.Name)
            .OrderBy(name => name)
            .ToArray();

        Assert.Equal(new[] { "CurrentVersion", "ProductId", "RequestedVersion" }, properties);
    }

    [Theory]
    [InlineData("", "1.0.0")]
    [InlineData("   ", "1.0.0")]
    [InlineData("bke-product", "")]
    [InlineData("bke-product", "   ")]
    public void Request_rejects_missing_required_values(string productId, string currentVersion)
    {
        Assert.Throws<ArgumentException>(() => new UpdateCheckRequest(productId, currentVersion));
    }

    [Fact]
    public void Up_to_date_result_cannot_claim_an_available_version_or_error()
    {
        var result = UpdateCheckResult.UpToDate();

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
        Assert.Null(result.AvailableVersion);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Update_available_requires_a_version_and_carries_no_error()
    {
        Assert.Throws<ArgumentException>(() => UpdateCheckResult.UpdateAvailable(" "));

        var result = UpdateCheckResult.UpdateAvailable("2.0.0");

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Equal("2.0.0", result.AvailableVersion);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Failed_result_requires_and_preserves_a_typed_error()
    {
        var error = new UpdateError(
            UpdateErrorCode.VerificationFailure,
            "The provider response could not be verified.",
            retryable: false);

        var result = UpdateCheckResult.Failed(error);

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.Null(result.AvailableVersion);
        Assert.Same(error, result.Error);
        Assert.Equal(UpdateErrorCode.VerificationFailure, result.Error!.Code);
    }

    [Fact]
    public async Task Contract_is_async_friendly()
    {
        IUpdateClient client = new StubClient();

        var result = await client.CheckAsync(new UpdateCheckRequest("p", "1.0.0"));

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
    }

    private sealed class StubClient : IUpdateClient
    {
        public Task<UpdateCheckResult> CheckAsync(
            UpdateCheckRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(UpdateCheckResult.UpToDate());
    }
}
