namespace Qyl.Build;

sealed partial class QylBuild
{
    [Parameter("AotPublishGate: which classification to publish — clean, warned or all (default all)", Name = "AotSet")]
    readonly string _aotSet = "all";

    Target AotPublishGate => definition => definition
        .Description("NativeAOT publish matrix: clean demos warning-free, vendor-warned demos exactly their approved diagnostics, promotions fail")
        .After(Check)
        .Executes(() =>
        {
            Gate.Require(_aotSet is "clean" or "warned" or "all", $"--aot-set must be clean, warned or all, not {_aotSet}");
            var plan = new List<(string Demo, string Kind)>();
            if (_aotSet is "clean" or "all")
                plan.AddRange(AotPublish.CleanDemos.Select(static demo => (demo, "clean")));
            if (_aotSet is "warned" or "all")
                plan.AddRange(AotPublish.VendorWarnedDemos.Keys.Select(static demo => (demo, "warned")));

            Log.Information("AOT publish gate | rid={Rid} | {Count} demo(s) | set={Set}", Repo.RuntimeIdentifier, plan.Count, _aotSet);
            AotPublish.PrebuildGenerator();
            var failures = 0;
            var promotions = 0;
            foreach (var (demo, kind) in plan)
            {
                var (verdict, detail) = kind == "clean" ? AotPublish.CheckClean(demo) : AotPublish.CheckWarned(demo);
                var label = verdict switch
                {
                    AotVerdict.Ok => "PASS",
                    AotVerdict.Promotion => "PROMO",
                    _ => "FAIL",
                };
                Log.Information("  [{Label}] {Kind,-6} {Demo}  {Detail}", label, kind, demo, detail);
                if (verdict is AotVerdict.Promotion)
                    promotions++;
                // A promotion fails as well: a vendor boundary that no longer exists must be removed.
                if (verdict is not AotVerdict.Ok)
                    failures++;
            }

            Gate.Require(failures == 0, $"aot-publish-gate FAILED: {failures} failure(s), {promotions} promotion(s)");
            Log.Information("aot-publish-gate-ok ({Count} demos)", plan.Count);
        });
}
