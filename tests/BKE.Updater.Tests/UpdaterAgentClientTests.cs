using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BKE.Updater;
using Xunit;

namespace BKE.Updater.Tests;

public sealed class UpdaterAgentClientTests
{
    [Fact]
    public async Task Check_posts_only_contract_inputs_to_fixed_agent_route()
    {
        string? requestPath = null;
        string? requestBody = null;
        var handler = new StubHandler(async request =>
        {
            requestPath = request.RequestUri?.ToString();
            requestBody = await request.Content!.ReadAsStringAsync();
            return Json(HttpStatusCode.OK, UpToDateJson());
        });

        using var http = new HttpClient(handler);
        using var client = BkeUpdaterClient.Create(http);

        var result = await client.CheckAsync(new UpdateCheckRequest(
            "bke-render-dock",
            "1.0.1",
            "1.0.2"));

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
        Assert.Equal("http://127.0.0.1:43873/v1/updates/check", requestPath);

        using var document = JsonDocument.Parse(requestBody!);
        var root = document.RootElement;
        Assert.Equal("bke-render-dock", root.GetProperty("product_id").GetString());
        Assert.Equal("1.0.1", root.GetProperty("current_version").GetString());
        Assert.Equal("1.0.2", root.GetProperty("requested_version").GetString());
        Assert.Equal(3, root.EnumerateObject().Count());
    }

    [Fact]
    public async Task Up_to_date_maps_to_contract_result()
    {
        var result = await CheckAsync(HttpStatusCode.OK, UpToDateJson());

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
        Assert.Null(result.AvailableVersion);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task Update_available_maps_to_contract_result()
    {
        var result = await CheckAsync(HttpStatusCode.OK, ResponseJson(
            status: "UpdateAvailable",
            availableVersion: "1.0.2"));

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Equal("1.0.2", result.AvailableVersion);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task Deferred_maps_to_contract_result()
    {
        var result = await CheckAsync(HttpStatusCode.OK, ResponseJson(
            status: "Deferred",
            availableVersion: "1.0.2"));

        Assert.Equal(UpdateCheckStatus.Deferred, result.Status);
        Assert.Equal("1.0.2", result.AvailableVersion);
        Assert.Null(result.Error);
    }

    [Theory]
    [InlineData("InvalidRequest", UpdateErrorCode.InvalidRequest, false)]
    [InlineData("ProviderUnavailable", UpdateErrorCode.ProviderUnavailable, true)]
    [InlineData("TransportFailure", UpdateErrorCode.TransportFailure, true)]
    [InlineData("ProtocolFailure", UpdateErrorCode.ProtocolFailure, false)]
    [InlineData("MalformedResponse", UpdateErrorCode.MalformedResponse, false)]
    [InlineData("VerificationFailure", UpdateErrorCode.VerificationFailure, false)]
    [InlineData("PolicyDenied", UpdateErrorCode.PolicyDenied, false)]
    [InlineData("Unknown", UpdateErrorCode.Unknown, false)]
    public async Task Canonical_provider_failure_maps_to_typed_sdk_error(
        string providerCode,
        UpdateErrorCode expectedCode,
        bool retryable)
    {
        var result = await CheckAsync(HttpStatusCode.OK, FailureJson(providerCode, retryable));

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.NotNull(result.Error);
        Assert.Equal(expectedCode, result.Error!.Code);
        Assert.Equal(retryable, result.Error.Retryable);
    }

    [Fact]
    public async Task Canonical_failure_is_preserved_even_on_http_error()
    {
        var result = await CheckAsync(
            HttpStatusCode.ServiceUnavailable,
            FailureJson("ProviderUnavailable", retryable: true));

        Assert.Equal(UpdateErrorCode.ProviderUnavailable, result.Error!.Code);
        Assert.True(result.Error.Retryable);
    }

    [Fact]
    public async Task Malformed_json_is_not_collapsed_into_unknown()
    {
        var result = await CheckAsync(HttpStatusCode.OK, "{not-json");

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.Equal(UpdateErrorCode.MalformedResponse, result.Error!.Code);
    }

    [Theory]
    [InlineData("other.capability", 1)]
    [InlineData("bke.updates.check", 99)]
    public async Task Capability_identity_or_version_mismatch_is_protocol_failure(
        string capabilityId,
        int contractVersion)
    {
        var result = await CheckAsync(
            HttpStatusCode.OK,
            ResponseJson("UpToDate", capabilityId: capabilityId, contractVersion: contractVersion));

        Assert.Equal(UpdateErrorCode.ProtocolFailure, result.Error!.Code);
    }

    [Fact]
    public async Task Contradictory_update_available_response_is_malformed()
    {
        var result = await CheckAsync(
            HttpStatusCode.OK,
            ResponseJson("UpdateAvailable", availableVersion: null));

        Assert.Equal(UpdateErrorCode.MalformedResponse, result.Error!.Code);
    }

    [Fact]
    public async Task Local_agent_transport_failure_is_provider_unavailable()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("offline"));
        using var http = new HttpClient(handler);
        using var client = BkeUpdaterClient.Create(http);

        var result = await client.CheckAsync(new UpdateCheckRequest("p", "1.0.0"));

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.Equal(UpdateErrorCode.ProviderUnavailable, result.Error!.Code);
        Assert.True(result.Error.Retryable);
    }

    [Fact]
    public void Default_transport_disables_proxy_and_redirects()
    {
        using var handler = BkeUpdaterClient.CreateDefaultHandler();

        Assert.False(handler.UseProxy);
        Assert.False(handler.AllowAutoRedirect);
    }

    private static async Task<UpdateCheckResult> CheckAsync(HttpStatusCode statusCode, string body)
    {
        var handler = new StubHandler(_ => Task.FromResult(Json(statusCode, body)));
        using var http = new HttpClient(handler);
        using var client = BkeUpdaterClient.Create(http);
        return await client.CheckAsync(new UpdateCheckRequest("bke-render-dock", "1.0.1"));
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string body) =>
        new(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private static string UpToDateJson() => ResponseJson("UpToDate");

    private static string FailureJson(string code, bool retryable) =>
        $$"""
        {
          "capability_id":"bke.updates.check",
          "contract_version":1,
          "status":"Failed",
          "available_version":null,
          "error":{"code":"{{code}}","message":"provider failure","retryable":{{retryable.ToString().ToLowerInvariant()}}}
        }
        """;

    private static string ResponseJson(
        string status,
        string? availableVersion = null,
        string capabilityId = "bke.updates.check",
        int contractVersion = 1)
    {
        var available = availableVersion is null
            ? "null"
            : JsonSerializer.Serialize(availableVersion);

        return $$"""
        {
          "capability_id":"{{capabilityId}}",
          "contract_version":{{contractVersion}},
          "status":"{{status}}",
          "available_version":{{available}},
          "error":null
        }
        """;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> responder;

        internal StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder)
        {
            this.responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => responder(request);
    }
}
