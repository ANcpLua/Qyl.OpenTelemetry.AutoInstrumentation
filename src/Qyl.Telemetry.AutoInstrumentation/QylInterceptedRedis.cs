using System.Diagnostics;
using System.Globalization;
using Qyl.Telemetry.AutoInstrumentation.Internal;

namespace Qyl.Telemetry.AutoInstrumentation.GeneratedCode;

/// <summary>StackExchange.Redis command spans.</summary>
/// <remarks>This runtime surface is NativeAOT-compatible and is consumed by source-generated interceptors without runtime IL rewriting, profiler attach, or reflection discovery.</remarks>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
[QylIntegration(QylAutoInstrumentationIds.StackExchangeRedis, QylInstrumentationDomains.DbRedis)]
[QylIntercept("StackExchange.Redis.IDatabaseAsync", Shape = QylShapes.RedisCommand, Start = nameof(Command))]
public static class QylInterceptedRedis
{
    /// <summary>Starts the client span for the resolved wire command on the receiving database.</summary>
    public static Activity? Command([QylFromShape] string? operationName, [QylFromReceiver("Database")] int databaseIndex)
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
        if (databaseIndex >= 0)
            activity.SetTag(QylSemanticAttributes.DbNamespace, databaseIndex.ToString(CultureInfo.InvariantCulture));

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
}
