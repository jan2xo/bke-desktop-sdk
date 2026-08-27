using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BKE.Desktop.Client;
using Xunit;

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
    [InlineData(HttpStatusCode.BadRequest, AuthorizationStatus.ProtocolRejected)]
    [InlineData(HttpStatusCode.Forbidden, AuthorizationStatus.ProtocolRejected)]
    [InlineData(HttpStatusCode.NotFound, AuthorizationStatus.ProtocolRejected)]
    [InlineData(HttpStatusCode.UnsupportedMediaType, AuthorizationStatus.ProtocolRejected)]
    [InlineData(HttpStatusCode.ServiceUnavailable, AuthorizationStatus.AgentUnavailable)]
    public async Task Non_success_authorization_response_never_authorizes(HttpStatusCode status, AuthorizationStatus expected)
    {
        using var client = CreateClient(_ => Json(status, "{}"));

        var result = await client.AuthorizeAsync("p", "1", "i");

        Assert.NotEqual(AuthorizationStatus.Authorized, result.Status);
        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public async Task Transport_failure_is_agent_unavailable()
    {
        using var client = CreateClient(_ => throw new HttpRequestException("offline"));

        Assert.Equal(AuthorizationStatus.AgentUnavailable,
            (await client.AuthorizeAsync("p", "1", "i")).Status);
    }

    [Fact]
    public async Task Authorization_internal_timeout_maps_to_timeout()
    {
        using var client = CreateClient(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Json(HttpStatusCode.OK, """{"authorized":true,"reason":"ok"}""");
        });

        var result = await client.AuthorizeAsync("p", "1", "i");

        Assert.Equal(AuthorizationStatus.Timeout, result.Status);
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
        using var client = CreateClient(async (r, _) =>
        {
            request = r;
            var body = await r.Content!.ReadAsStringAsync();
            var correlation = System.Text.Json.JsonDocument.Parse(body).RootElement.GetProperty("correlation_id").GetString();
            return Json(HttpStatusCode.OK, System.Text.Json.JsonSerializer.Serialize(new { outcome = "authorization_refreshed", reason = "ok", correlation_id = correlation }));
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
    [InlineData("completed", LicenseCenterStatus.Completed)]
    [InlineData("cancelled", LicenseCenterStatus.Cancelled)]
    [InlineData("agent_unavailable", LicenseCenterStatus.AgentUnavailable)]
    [InlineData("authorization_refreshed", LicenseCenterStatus.AuthorizationRefreshed)]
    [InlineData("invalid_product_context", LicenseCenterStatus.InvalidProductContext)]
    [InlineData("incompatible_product_version", LicenseCenterStatus.IncompatibleProductVersion)]
    [InlineData("activation_failed", LicenseCenterStatus.ActivationFailed)]
    [InlineData("failed", LicenseCenterStatus.Failed)]
    public async Task License_center_maps_terminal_outcomes(string outcome, LicenseCenterStatus expected)
    {
        using var client = CreateClient(async (r, _) =>
        {
            var body = await r.Content!.ReadAsStringAsync();
            var correlation = System.Text.Json.JsonDocument.Parse(body).RootElement.GetProperty("correlation_id").GetString();
            return Json(HttpStatusCode.OK, System.Text.Json.JsonSerializer.Serialize(new { outcome, reason = "reason", correlation_id = correlation }));
        });

        Assert.Equal(expected, (await client.OpenLicenseCenterAsync("p", "1", "i")).Status);
    }

    [Fact]
    public void Sdk_owned_transport_disables_redirects_and_proxies()
    {
        using var handler = BkeDesktopClient.CreateDefaultHandler();

        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseProxy);
    }

    [Fact]
    public async Task Sdk_owned_transport_does_not_follow_redirects()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 43873);
        listener.Start();

        var redirectTargetWasContacted = false;
        var server = Task.Run(async () =>
        {
            using var first = await listener.AcceptTcpClientAsync();
            await ReadRequestHeadersAsync(first.GetStream());
            await WriteResponseAsync(first.GetStream(),
                "HTTP/1.1 302 Found\r\nLocation: http://127.0.0.1:43873/redirect-target\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");

            using var probe = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            try
            {
                using var second = await listener.AcceptTcpClientAsync(probe.Token);
                redirectTargetWasContacted = true;
            }
            catch (OperationCanceledException)
            {
                // Expected: SDK-owned HttpClientHandler must not follow the redirect.
            }
        });

        using var client = BkeDesktopClient.Create();
        var result = await client.AuthorizeAsync("p", "1", "i");
        await server;

        Assert.Equal(AuthorizationStatus.ProtocolRejected, result.Status);
        Assert.False(redirectTargetWasContacted);
    }

    [Fact]
    public async Task Invalid_product_identity_fails_closed()
    {
        using var client = CreateClient(_ => throw new InvalidOperationException("must not send"));

        var result = await client.AuthorizeAsync("", "1", "i");

        Assert.Equal(AuthorizationStatus.InvalidRequest, result.Status);
    }

    private static async Task ReadRequestHeadersAsync(NetworkStream stream)
    {
        var buffer = new byte[1024];
        var received = new StringBuilder();

        while (!received.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
        {
            var count = await stream.ReadAsync(buffer);
            if (count == 0)
                break;

            received.Append(Encoding.ASCII.GetString(buffer, 0, count));
        }
    }

    private static async Task WriteResponseAsync(NetworkStream stream, string response)
    {
        var bytes = Encoding.ASCII.GetBytes(response);
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();
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
