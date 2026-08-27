using System.Net;
using BKE.Desktop.Client;
namespace BKE.Desktop.Client.Tests;
public sealed class ClientTests
{
 [Fact] public async Task Authorize_Uses_Canonical_Request_And_Maps_Decision()
 {
  HttpRequestMessage? seen=null;
  using var client=BkeDesktopClient.Create(new HttpClient(new StubHandler(r=>{seen=r;return Json(HttpStatusCode.OK,"{\"authorized\":false,\"reason\":\"activation_required\"}");})));
  var result=await client.AuthorizeAsync("bke-test","1.0.0","installation-1");
  Assert.Equal(AuthorizationStatus.ActivationRequired,result.Status);
  Assert.Equal("/v1/authorize",seen!.RequestUri!.AbsolutePath);
  Assert.Contains("\"product_id\":\"bke-test\"",await seen.Content!.ReadAsStringAsync());
 }
 [Fact] public async Task Malformed_Response_Is_Not_Authorization()
 {
  using var client=BkeDesktopClient.Create(new HttpClient(new StubHandler(_=>Json(HttpStatusCode.OK,"{\"authorized\":true}"))));
  Assert.Equal(AuthorizationStatus.InvalidResponse,(await client.AuthorizeAsync("p","1","i")).Status);
 }
 [Fact] public async Task License_Center_Requires_Correlation()
 {
  using var client=BkeDesktopClient.Create(new HttpClient(new StubHandler(_=>Json(HttpStatusCode.OK,"{\"outcome\":\"authorization_refreshed\",\"reason\":\"ok\",\"correlation_id\":\"wrong\"}"))));
  Assert.Equal(LicenseCenterStatus.InvalidResponse,(await client.OpenLicenseCenterAsync("p","1","i")).Status);
 }
 private static HttpResponseMessage Json(HttpStatusCode s,string b)=>new(s){Content=new StringContent(b,System.Text.Encoding.UTF8,"application/json")};
 private sealed class StubHandler(Func<HttpRequestMessage,HttpResponseMessage> h):HttpMessageHandler
 { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken c)=>Task.FromResult(h(r)); }
}