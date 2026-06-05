using Microsoft.Extensions.Logging;

ILogger logger = new SnapshotLogger();

logger.Log(
    LogLevel.Warning,
    new EventId(42, "snapshot-log"),
    "snapshot-state",
    exception: null,
    static (state, exception) => exception is null ? state : state + ":" + exception.GetType().Name);

return 0;

internal sealed class SnapshotLogger : ILogger
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
        => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
    }
}
