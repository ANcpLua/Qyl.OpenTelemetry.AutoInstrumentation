using Microsoft.Extensions.Logging;

// A SECOND interceptable call site in a SECOND file. This is what makes the snapshot pin the
// determinism fix: with two ILogger.Log sites across two files, the emission order and the _N
// interceptor-name indices are decided by the OrderBy(Location.Data) sort, not by Roslyn's
// cross-tree visitation order. Drop that sort and this snapshot's byte-compare flips red.
internal static class Probe
{
    internal static void Emit(ILogger logger) =>
        logger.Log(
            LogLevel.Information,
            new EventId(7, "probe-log"),
            "probe-state",
            exception: null,
            static (state, exception) => exception is null ? state : state + ":" + exception.GetType().Name);
}
