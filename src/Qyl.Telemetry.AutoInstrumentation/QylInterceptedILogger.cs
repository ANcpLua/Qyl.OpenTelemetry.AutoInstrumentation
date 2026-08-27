using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Qyl.Telemetry.AutoInstrumentation.Internal;

namespace Qyl.Telemetry.AutoInstrumentation.GeneratedCode;

/// <summary>Microsoft.Extensions.Logging log-as-span activities.</summary>
/// <remarks>This runtime surface is NativeAOT-compatible and is consumed by source-generated interceptors without runtime IL rewriting, profiler attach, or reflection discovery.</remarks>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
[QylIntegration(QylAutoInstrumentationIds.ILogger, QylInstrumentationDomains.LogILogger, Signal = QylAutoInstrumentationSignal.Logs)]
[QylIntercept("Microsoft.Extensions.Logging.ILogger", "Log", Shape = QylShapes.LoggerLog, Body = QylInterceptorBody.Log, Start = nameof(Log))]
[QylIntercept(
    "Microsoft.Extensions.Logging.ILogger",
    "Log", "LogTrace", "LogDebug", "LogInformation", "LogWarning", "LogError", "LogCritical",
    Shape = QylShapes.LoggerExtension,
    Body = QylInterceptorBody.LogExtension,
    Start = nameof(LogExtension))]
public static class QylInterceptedILogger
{
    private const string ActivityName = "ILogger log";

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
            QylInterceptedActivity.RecordException(activity, caughtException);
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
            QylInterceptedActivity.RecordException(activity, caughtException);
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
            ActivityName,
            ActivityKind.Internal,
            QylInstrumentationDomains.LogILogger);
        if (activity is null)
            return null;

        QylActivityTags.SetLogSeverity(activity, severity);

        if (exception is not null)
            QylInterceptedActivity.RecordException(activity, exception);

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
}
