using System.Net;
using System.Net.Http;

using var client = new HttpClient(new SnapshotHandler());

using (await client.GetAsync("http://qyl.invalid/program"))
{
}

await Probe.EmitAsync(client);

return 0;

internal sealed class SnapshotHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
}
