using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Qyl.OpenTelemetry.AutoInstrumentation.Internal;

namespace Qyl.OpenTelemetry.AutoInstrumentation.GeneratedCode;

/// <summary>Defines the qyl auto-instrumentation surface for qyl Intercepted Logger.</summary>
/// <remarks>This runtime surface is NativeAOT-compatible and is consumed by source-generated interceptors without runtime IL rewriting, profiler attach, or reflection discovery.</remarks>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public static class QylInterceptedLogger
{

    /// <summary>Runs the generic Microsoft.Extensions.Logging log helper used by source-generated qyl interceptors.</summary>
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

    /// <summary>Runs the Log Extension runtime helper used by source-generated qyl interceptors.</summary>
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

        var activity = QylActivityFactory.StartLogActivity(
            QylAutoInstrumentationIds.ILogger,
            QylActivityNames.LoggerLog,
            ActivityKind.Internal,
            QylInstrumentationDomains.LogILogger);
        if (activity is null)
            return null;

        QylActivityTags.SetLogSeverity(activity, severity);

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
        QylActivityStatus.RecordException(activity, exception);
    }
}
