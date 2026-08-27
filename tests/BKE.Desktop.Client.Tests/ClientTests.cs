using System.Net;
using System.Text;
using BKE.Desktop.Client;

namespace BKE.Desktop.Client.Tests;

public sealed class ClientTests
{
    [Fact]
    public async Task Authorize_Maps_Authorized_Response()
    {
        using var client = ClientReturning("{\"authorized\":true,\"reason\":\"authorized\"}");
        var result = await client.AuthorizeAsync("bke-test", "1.0.0", "installation-1");
        Assert.Equal(AuthorizationStatus.Authorized, result.Status);
    }

    [Fact]
    public async Task Authorize_Maps_Denied_Response()
    {
        using var client = ClientReturning("{\"authorized\":false,\"reason\":\"not_entitled\"}");
        var result = await client.AuthorizeAsync("bke-test", "1.0.0", "installation-1");
        Assert.Equal(AuthorizationStatus.Denied, result.Status);
    }

    [Fact]
    public async Task Authorize_Maps_Activation_Required()
    {
        using var client = ClientReturning("{\"authorized\":false,\"reason\":\"activation_required\"}");
        var result = await client.AuthorizeAsync("bke-test", "1.0.0", "installation-1");
        Assert.Equal(AuthorizationStatus.ActivationRequired, result.Status);
    }

    [Fact]
    public async Task Authorize_Maps_Agent_Unavailable()
    {
        using var client = ClientWithHandler((_, _) => Task.FromResult(Response(HttpStatusCode.ServiceUnavailable, "")));
        var result = await client.AuthorizeAsync("p", "1", "i");
        Assert.Equal(AuthorizationStatus.AgentUnavailable, result.Status);
    }

    [Fact]
    public async Task Authorize_Rejects_Malformed_Response()
    {
        using var client = ClientReturning("{\"authorized\":true}");
        var result = await client.AuthorizeAsync("p", "1", "i");
        Assert.Equal(AuthorizationStatus.InvalidResponse, result.Status);
    }

    [Fact]
    public async Task Authorize_Rejects_Redirect_Response()
    {
        using var client = ClientWithHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect)));
        var result = await client.AuthorizeAsync("p", "1", "i");
        Assert.Equal(AuthorizationStatus.InvalidResponse, result.Status);
    }

    [Fact]
    public async Task Caller_Cancellation_Is_Propagated()
    {
        using var client = ClientWithHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Response(HttpStatusCode.OK, "{}");
        });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.AuthorizeAsync("p", "1", "i", cancellation.Token));
    }

    [Fact]
    public async Task License_Center_Requires_Correlation()
    {
        using var client = ClientReturning("{\"outcome\":\"authorization_refreshed\",\"reason\":\"ok\",\"correlation_id\":\"wrong\"}");
        var result = await client.OpenLicenseCenterAsync("p", "1", "i");
        Assert.Equal(LicenseCenterStatus.InvalidResponse, result.Status);
    }

    [Fact]
    public async Task Authorize_Sends_Canonical_Path_And_Json()
    {
        HttpRequestMessage? seen = null;
        using var client = ClientWithHandler((request, _) =>
        {
            seen = request;
            return Task.FromResult(Response(HttpStatusCode.OK, "{\"authorized\":false,\"reason\":\"denied\"}"));
        });
        await client.AuthorizeAsync("bke-test", "1.0.0", "installation-1");
        Assert.Equal("/v1/authorize", seen!.RequestUri!.AbsolutePath);
        Assert.Equal(HttpMethod.Post, seen.Method);
        Assert.Contains("\"product_id\":\"bke-test\"", await seen.Content!.ReadAsStringAsync());
        Assert.Contains("\"installation_id\":\"installation-1\"", await seen.Content!.ReadAsStringAsync());
    }

    private static BkeDesktopClient ClientReturning(string body) => ClientWithHandler((_, _) => Task.FromResult(Response(HttpStatusCode.OK, body)));

    private static BkeDesktopClient ClientWithHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) =>
        BkeDesktopClient.Create(new HttpClient(new StubHandler(handler)));

    private static HttpResponseMessage Response(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handler(request, cancellationToken);
    }
}
