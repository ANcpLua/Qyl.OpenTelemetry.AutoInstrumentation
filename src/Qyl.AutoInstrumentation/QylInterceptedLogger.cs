using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Qyl.AutoInstrumentation;

public static class QylInterceptedLogger
{

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

        var activity = StartActivity(logger, logLevel, eventId, exception);
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

    public static void LogExtension(
        ILogger logger,
        LogLevel logLevel,
        EventId eventId,
        Exception? exception,
        string? message,
        object?[] args)
    {
        var activity = logger is null ? null : StartActivity(logger, logLevel, eventId, exception);
        try
        {
            LoggerExtensions.Log(logger!, logLevel, eventId, exception, message, args);
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

    private static Activity? StartActivity(ILogger logger, LogLevel logLevel, EventId eventId, Exception? exception)
    {
        if (!QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Logs, QylAutoInstrumentationIds.ILogger))
            return null;

        var severity = NormalizeSeverity(logLevel);
        if (severity is null || !logger.IsEnabled(logLevel))
            return null;

        var activity = QylActivitySource.Source.StartActivity("ILogger log", ActivityKind.Internal);
        if (activity is null)
            return null;

        activity.SetTag(QylSemanticAttributes.QylInstrumentationDomain, QylInstrumentationDomains.LogILogger);
        activity.SetTag(QylSemanticAttributes.LogSeverity, severity);

        if (exception is not null)
            RecordException(activity, exception);

        return activity;
    }

    private static string? NormalizeSeverity(LogLevel logLevel)
        => logLevel switch
        {
            LogLevel.Trace => QylSemanticAttributes.LogSeverityTrace,
            LogLevel.Debug => QylSemanticAttributes.LogSeverityDebug,
            LogLevel.Information => QylSemanticAttributes.LogSeverityInformation,
            LogLevel.Warning => QylSemanticAttributes.LogSeverityWarning,
            LogLevel.Error => QylSemanticAttributes.LogSeverityError,
            LogLevel.Critical => QylSemanticAttributes.LogSeverityCritical,
            LogLevel.None => null,
            _ => QylSemanticAttributes.LogSeverityOther,
        };

    private static void RecordException(Activity? activity, Exception exception)
    {
        activity?.SetTag(QylSemanticAttributes.ErrorType, exception.GetType().Name);
        activity?.SetStatus(ActivityStatusCode.Error);
    }
}
