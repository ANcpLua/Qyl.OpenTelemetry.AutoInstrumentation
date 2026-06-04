using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Qyl.AutoInstrumentation;

public static class QylInterceptedLogger
{
    private const string LoggerDomain = "log.ilogger";

    public static void Log<TState>(
        ILogger logger,
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (logger is null)
            throw new NullReferenceException();

        var activity = StartActivity(logLevel, eventId, exception);
        try
        {
            logger.Log(logLevel, eventId, state, exception, formatter);
        }
        catch (Exception caughtException)
        {
            RecordException(activity, caughtException);
            throw;
        }
        finally
        {
            activity?.Dispose();
        }
    }

    private static Activity? StartActivity(LogLevel logLevel, EventId eventId, Exception? exception)
    {
        if (!QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Logs, QylAutoInstrumentationIds.ILogger))
            return null;

        var activity = QylActivitySource.Source.StartActivity("ILogger " + logLevel, ActivityKind.Internal);
        if (activity is null)
            return null;

        activity.SetTag(QylSemanticAttributes.QylInstrumentationDomain, LoggerDomain);
        activity.SetTag(QylSemanticAttributes.LogSeverity, logLevel.ToString());

        if (!string.IsNullOrWhiteSpace(eventId.Name))
            activity.SetTag(QylSemanticAttributes.LogEventName, eventId.Name);

        if (exception is not null)
            RecordException(activity, exception);

        return activity;
    }

    private static void RecordException(Activity? activity, Exception exception)
    {
        activity?.SetTag(QylSemanticAttributes.ErrorType, exception.GetType().Name);
        activity?.SetStatus(ActivityStatusCode.Error);
    }
}
