using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

var builder = WebApplication.CreateSlimBuilder(args);
var app = builder.Build();
app.UseMiddleware<PassThroughMiddleware>();
app.MapGet("/", () => "ok");
app.Run();

static void Probe(System.Data.Common.DbCommand command)
    => _ = command.ExecuteScalar();

internal sealed class PassThroughMiddleware(RequestDelegate next)
{
    // `next(context)` is a RequestDelegate (delegate) invocation, not an ordinary member call,
    // so the interceptors feature cannot intercept it (CS9207). The generator must skip it.
    public System.Threading.Tasks.Task InvokeAsync(HttpContext context) => next(context);
}
