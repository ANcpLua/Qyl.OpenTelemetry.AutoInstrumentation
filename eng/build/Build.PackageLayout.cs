namespace Qyl.Build;

sealed partial class QylBuild
{
    Target PackageLayout => definition => definition
        .Description("The core package ships its generator, build assets and nothing of a profiler, startup hook or IL rewriter")
        .After(GeneratorSnapshots)
        .Executes(() => PackageLayoutGate.Verify());
}
