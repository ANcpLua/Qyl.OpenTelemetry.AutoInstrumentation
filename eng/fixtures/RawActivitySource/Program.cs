using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Qyl;

var builder = Host.CreateApplicationBuilder(args);
builder.AddQyl();

// An application that builds its own source: QYL0111 must fail this build. The analyzers reach it
// through the Qyl.Telemetry.Hosting package alone; nothing here references them.
using var source = new ActivitySource("Shop.Orders");
using (source.StartActivity("checkout"))
{
}

return 0;
