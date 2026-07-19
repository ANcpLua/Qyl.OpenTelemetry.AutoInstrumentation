namespace Qyl.OpenTelemetry.AutoInstrumentation;

internal static class QylAspNetCoreOwnership
{
    private static int _middlewareRegistered;

    internal static bool MiddlewareRegistered
        => Volatile.Read(ref _middlewareRegistered) is not 0;

    internal static void RegisterMiddleware()
        => Interlocked.Exchange(ref _middlewareRegistered, 1);
}
