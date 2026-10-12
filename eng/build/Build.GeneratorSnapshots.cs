namespace Qyl.Build;

sealed partial class QylBuild
{
    Target GeneratorSnapshots => definition => definition
        .Description("Byte-exact generator snapshots, the QYL1001 shape-mismatch diagnostic and the live interceptor-manifest coverage of every implemented signal")
        .After(ApiBaseline)
        .Executes(() => GeneratorSnapshotGate.Verify());
}
