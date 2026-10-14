namespace Qyl.Build;

sealed partial class QylBuild
{
    Target ApiBaseline => definition => definition
        .Description("Public API report: rewrites src/<project>/PublicAPI.txt to the Release assemblies' surface and notes the change against HEAD; never fails")
        .DependsOn(Build)
        .Executes(() =>
        {
            var change = ApiBaselineReport.Run();
            ReportSummary(_ => new() { ["api"] = change });
        });
}
