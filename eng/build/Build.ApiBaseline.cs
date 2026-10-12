namespace Qyl.Build;

sealed partial class QylBuild
{
    Target ApiBaseline => definition => definition
        .Description("Public API baselines of the six shipped projects and the XML-doc enforcement of the runtime and the generator")
        .After(Build)
        .Executes(() => ApiBaselineGate.Verify());
}
