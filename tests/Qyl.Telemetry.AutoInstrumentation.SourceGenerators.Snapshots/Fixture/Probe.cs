// A SECOND interceptable call site in a SECOND file. This is what makes the snapshot pin the
// determinism fix: with two DbCommand.ExecuteScalar sites across two files, the emission order and
// the _N interceptor-name indices are decided by the OrderBy(Location.Data) sort, not by Roslyn's
// cross-tree visitation order. Drop that sort and this snapshot's byte-compare flips red.
internal static class Probe
{
    internal static void Emit(SnapshotCommand command)
    {
        _ = command.ExecuteScalar();
    }
}
