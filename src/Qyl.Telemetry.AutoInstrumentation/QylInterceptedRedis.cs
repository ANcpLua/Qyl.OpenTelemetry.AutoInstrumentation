using System.Diagnostics;
using Qyl.Telemetry.AutoInstrumentation.Internal;

namespace Qyl.Telemetry.AutoInstrumentation.GeneratedCode;

/// <summary>Defines the qyl auto-instrumentation surface for qyl Intercepted Redis.</summary>
/// <remarks>This runtime surface is NativeAOT-compatible and is consumed by source-generated interceptors without runtime IL rewriting, profiler attach, or reflection discovery.</remarks>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public static class QylInterceptedRedis
{

    /// <summary>Runs the Start Command Activity runtime helper used by source-generated qyl interceptors.</summary>
    public static Activity? StartCommandActivity(string? operationName)
    {
        var activity = QylActivityFactory.StartTraceActivity(
            QylAutoInstrumentationIds.StackExchangeRedis,
            QylSpanNames.Db(operationName, QylSemanticAttributes.DbSystemRedis),
            ActivityKind.Client,
            QylInstrumentationDomains.DbRedis);
        if (activity is null)
            return null;

        activity.SetTag(QylSemanticAttributes.DbSystemName, QylSemanticAttributes.DbSystemRedis);
        if (operationName is not null)
            QylActivityTags.SetDbOperation(activity, operationName, operationName);

        return activity;
    }

    /// <summary>
    /// Normalizes the command text a caller passes to <c>IDatabaseAsync.ExecuteAsync</c> into the
    /// command name StackExchange.Redis puts on the wire, which upper-cases the caller's token.
    /// Returns <see langword="null"/> for text that is not a single command token, so an
    /// unbounded value is left off the span instead of becoming a span dimension.
    /// </summary>
    public static string? NormalizeCommandText(string? command)
    {
        if (command is null)
            return null;

        var trimmed = command.Trim();
        if (trimmed.Length is 0)
            return null;

        foreach (var character in trimmed)
        {
            if (char.IsWhiteSpace(character))
                return null;
        }

        return trimmed.ToUpperInvariant();
    }

    /// <summary>Runs the Record Exception runtime helper used by source-generated qyl interceptors.</summary>
    public static void RecordException(Activity? activity, Exception exception)
    {
        QylActivityStatus.RecordException(activity, exception);
    }
}
