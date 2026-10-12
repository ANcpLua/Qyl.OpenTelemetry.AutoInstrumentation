namespace Qyl.Build;

sealed partial class QylBuild
{
    [Parameter("WebApiAotDemo, OtlpEvidence: rewrite the verified file instead of comparing")]
    readonly bool UpdateVerified;

    Target WebApiAotDemo => definition => definition
        .Description("NativeAOT web API consumer under its vendor-warning policy, compared with the verified report")
        .After(AotPublishGate)
        .Executes(() => WebApiAotDemoGate.Run(UpdateVerified));
}
