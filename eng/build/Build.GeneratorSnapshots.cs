namespace Qyl.Build;

sealed partial class QylBuild
{
    Target GeneratorSnapshots => definition => definition
        .Description("Byte-exact generator snapshots and live interceptor-manifest coverage of every implemented signal")
        .After(ApiBaseline)
        .Executes(GeneratorSnapshotGate.Verify);
}
