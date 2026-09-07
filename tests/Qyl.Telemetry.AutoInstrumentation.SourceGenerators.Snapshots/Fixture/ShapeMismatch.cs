// A call site that names a declared integration receiver and method but does not fit the declared
// shape. QylUnmatchedCommand derives from System.Data.Common.DbCommand, so the receiver matches, and
// the method name ExecuteScalar matches the DbCommand intercept declaration -- but the signature
// takes an int and returns string, which the DbCommand shape does not describe.
//
// The generator must emit NO interceptor for it and report exactly one QYL1001 instead. This is the
// failure mode a library major introduces when it changes an intercepted signature: silence here
// would mean instrumentation disappearing with no signal, and emitting an interceptor anyway would
// break the consumer's build.
internal static class ShapeMismatchProbe
{
    internal static string Unmatched(QylUnmatchedCommand command)
        => command.ExecuteScalar(1);
}

internal sealed class QylUnmatchedCommand : SnapshotCommand
{
    internal string ExecuteScalar(int id)
        => id.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
