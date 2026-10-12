using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

var captured = new List<Activity>();
using var listener = new ActivityListener
{
    ShouldListenTo = static source => source.Name == "System.Net.Http",
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity => captured.Add(activity),
};
ActivitySource.AddActivityListener(listener);

var tcp = new TcpListener(IPAddress.Loopback, 0);
tcp.Start(1);
var port = ((IPEndPoint)tcp.LocalEndpoint).Port;
var serve = Task.Run(async () =>
{
    using var client = await tcp.AcceptTcpClientAsync();
    await using var stream = client.GetStream();
    var buffer = new byte[8192];
    var received = new List<byte>();
    while (true)
    {
        var count = await stream.ReadAsync(buffer);
        if (count == 0) throw new InvalidOperationException("client closed early");
        received.AddRange(buffer.AsSpan(0, count).ToArray());
        if (received.Count >= 4 && received.ToArray().AsSpan().IndexOf("\r\n\r\n"u8) >= 0) break;
    }
    var response = Encoding.ASCII.GetBytes(
        "HTTP/1.1 204 No Content\r\nContent-Length: 0\r\nX-Server: srv1\r\nMcp-Param-Result: response-secret\r\nConnection: close\r\n\r\n");
    await stream.WriteAsync(response);
});

using var http = new HttpClient();
http.DefaultRequestHeaders.Add("X-Client", "abc");
http.DefaultRequestHeaders.Add("Mcp-Param-Region", "request-secret");
var response = await http.GetAsync($"http://127.0.0.1:{port}/probe?user=alice&token=hunter2");
await serve;
tcp.Stop();
Console.WriteLine("http.status=" + (int)response.StatusCode);

Console.WriteLine("activity.count=" + captured.Count);
foreach (var activity in captured)
{
    var tags = activity.TagObjects
        .Select(tag => (tag.Key, Value: tag.Value switch
        {
            string s => s,
            System.Collections.IEnumerable e => string.Join(",", e.Cast<object?>()),
            var other => Convert.ToString(other, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
        }))
        .OrderBy(tag => tag.Key, StringComparer.Ordinal)
        .ToList();

    foreach (var (key, value) in tags)
    {
        if (key == "url.full")
            Console.WriteLine("url.full=" + value.Replace(":" + port, ":PORT"));
        else if (key.StartsWith("http.request.header.", StringComparison.Ordinal)
                 || key.StartsWith("http.response.header.", StringComparison.Ordinal))
            Console.WriteLine("unexpected-captured-header:" + key + "=" + value);
    }
}
