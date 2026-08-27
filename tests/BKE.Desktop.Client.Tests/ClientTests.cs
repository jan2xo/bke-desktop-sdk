using System.Net;
using System.Net.Http.Headers;
using System.Text;
using BKE.Desktop.Client;

namespace BKE.Desktop.Client.Tests;

public sealed class ClientTests
{
    [Fact]
    public async Task Authorize_Sends_Canonical_Request_And_Content_Type()
    {
        HttpRequestMessage? request = null;
        using var client = CreateClient(r =>
        {
            request = r;
            return Json(HttpStatusCode.OK, """{"authorized":true,"reason":"authorized"}""");
        });

        var result = await client.AuthorizeAsync("bke-test", "1.0.0", "installation-1");

        Assert.Equal(AuthorizationStatus.Authorized, result.Status);
        Assert.Equal("POST", request!.Method.Method);
        Assert.Equal("/v1/authorize", request.RequestUri!.AbsolutePath);
        Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
        Assert.Equal("""{"product_id":"bke-test","version":"1.0.0","installation_id":"installation-1"}""",
            await request.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("""{"authorized":true,"reason":"ok"}""", AuthorizationStatus.Authorized)]
    [InlineData("""{"authorized":false,"reason":"denied"}""", AuthorizationStatus.Denied)]
    [InlineData("""{"authorized":false,"reason":"activation_required"}""", AuthorizationStatus.ActivationRequired)]
    [InlineData("""{"authorized":false,"reason":"unsupported_version"}""", AuthorizationStatus.Unsupported)]
    public async Task Authorize_Maps_Canonical_Decisions(string body, AuthorizationStatus expected)
    {
        using var client = CreateClient(_ => Json(HttpStatusCode.OK, body));

        var result = await client.AuthorizeAsync("p", "1", "i");

        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public async Task Missing_Decision_And_Malformed_Json_Fail_Closed()
    {
        using var missing = CreateClient(_ => Json(HttpStatusCode.OK, """{"authorized":true}"""));
        using var malformed = CreateClient(_ => Json(HttpStatusCode.OK, "{not-json"));

        Assert.Equal(AuthorizationStatus.InvalidResponse,
            (await missing.AuthorizeAsync("p", "1", "i")).Status);
        Assert.Equal(AuthorizationStatus.InvalidResponse,
            (await malformed.AuthorizeAsync("p", "1", "i")).Status);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.UnsupportedMediaType)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Non_success_authorization_response_never_authorizes(HttpStatusCode status)
    {
        using var client = CreateClient(_ => Json(status, "{}"));

        var result = await client.AuthorizeAsync("p", "1", "i");

        Assert.NotEqual(AuthorizationStatus.Authorized, result.Status);
        Assert.Equal(AuthorizationStatus.AgentUnavailable, result.Status);
    }

    [Fact]
    public async Task Transport_failure_is_agent_unavailable()
    {
        using var client = CreateClient(_ => throw new HttpRequestException("offline"));

        Assert.Equal(AuthorizationStatus.AgentUnavailable,
            (await client.AuthorizeAsync("p", "1", "i")).Status);
    }

    [Fact]
    public async Task Caller_cancellation_is_preserved()
    {
        using var client = CreateClient(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Json(HttpStatusCode.OK, """{"authorized":true,"reason":"ok"}""");
        });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.AuthorizeAsync("p", "1", "i", cancellation.Token));
    }

    [Fact]
    public async Task License_center_sends_and_validates_correlation()
    {
        HttpRequestMessage? request = null;
        using var client = CreateClient(async r =>
        {
            request = r;
            var body = await r.Content!.ReadAsStringAsync();
            var correlation = System.Text.Json.JsonDocument.Parse(body).RootElement.GetProperty("correlation_id").GetString();
            return Json(HttpStatusCode.OK, $"{{"outcome":"authorization_refreshed","reason":"ok","correlation_id":"{correlation}"}}");
        });

        var result = await client.OpenLicenseCenterAsync("p", "1", "i");

        Assert.Equal(LicenseCenterStatus.AuthorizationRefreshed, result.Status);
        Assert.Equal("/v1/license-center/open", request!.RequestUri!.AbsolutePath);
        Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task License_center_rejects_wrong_correlation()
    {
        using var client = CreateClient(_ => Json(HttpStatusCode.OK,
            """{"outcome":"authorization_refreshed","reason":"ok","correlation_id":"wrong"}"""));

        Assert.Equal(LicenseCenterStatus.InvalidResponse,
            (await client.OpenLicenseCenterAsync("p", "1", "i")).Status);
    }

    [Theory]
    [InlineData("completed", LicenseCenterStatus.Failed)]
    [InlineData("cancelled", LicenseCenterStatus.Cancelled)]
    [InlineData("agent_unavailable", LicenseCenterStatus.AgentUnavailable)]
    [InlineData("authorization_refreshed", LicenseCenterStatus.AuthorizationRefreshed)]
    public async Task License_center_maps_terminal_outcomes(string outcome, LicenseCenterStatus expected)
    {
        using var client = CreateClient(async r =>
        {
            var body = await r.Content!.ReadAsStringAsync();
            var correlation = System.Text.Json.JsonDocument.Parse(body).RootElement.GetProperty("correlation_id").GetString();
            return Json(HttpStatusCode.OK, $"{{"outcome":"{outcome}","reason":"reason","correlation_id":"{correlation}"}}");
        });

        Assert.Equal(expected, (await client.OpenLicenseCenterAsync("p", "1", "i")).Status);
    }

    [Fact]
    public async Task Redirect_response_is_not_followed()
    {
        var calls = 0;
        using var client = CreateClient(_ =>
        {
            calls++;
            return new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Headers = { Location = new Uri("https://example.invalid/") }
            };
        });

        var result = await client.AuthorizeAsync("p", "1", "i");

        Assert.Equal(1, calls);
        Assert.NotEqual(AuthorizationStatus.Authorized, result.Status);
    }

    [Fact]
    public async Task Invalid_product_identity_fails_closed()
    {
        using var client = CreateClient(_ => throw new InvalidOperationException("must not send"));

        var result = await client.AuthorizeAsync("", "1", "i");

        Assert.Equal(AuthorizationStatus.InvalidResponse, result.Status);
    }

    private static BkeDesktopClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> handler) =>
        BkeDesktopClient.Create(new HttpClient(new StubHandler(handler)));

    private static BkeDesktopClient CreateClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) =>
        BkeDesktopClient.Create(new HttpClient(new StubHandler(handler)));

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) :
            this((request, _) => Task.FromResult(handler(request))) { }

        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) =>
            this.handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            handler(request, cancellationToken);
    }
}