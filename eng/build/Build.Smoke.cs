namespace Qyl.Build;

sealed partial class QylBuild
{
    [Parameter("Smoke, OtlpEvidence, VerifyRelease: verify this published version from nuget.org instead of packing the checkout")]
    readonly string? PublishedVersion;

    Target Smoke => definition => definition
        .Description("Package smoke test: package and ProjectReference consumers, managed and NativeAOT, verified stdout, warning-free AOT publish, SqlClient package assets")
        .After(AotPublishGate)
        .Executes(() => SmokeGate.Run(PublishedVersion));
}
