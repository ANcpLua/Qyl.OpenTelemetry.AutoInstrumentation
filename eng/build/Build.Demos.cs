namespace Qyl.Build;

sealed partial class QylBuild
{
    [Parameter("Demos: the lanes to run, by name (default: all thirty) — e.g. --lane quartz http-client")]
    readonly string[] Lane = [];

    Target Demos => definition => definition
        .Description("The thirty demo lanes: build/publish each demo, export OTLP to a local qyl collector, assert its report and the arrived telemetry")
        .After(AotPublishGate)
        .Executes(() =>
        {
            var lanes = Lane.Length == 0 ? DemoLanes.All : Lane.Select(DemoLanes.ByName).ToArray();
            var testbed = lanes.Any(static lane => lane.Services.Count > 0) ? Testbed.Load() : null;
            var collector = QylCollector.Shared;
            foreach (var lane in lanes)
            {
                Log.Information("== {Lane} demo lane ({Demo}) ==", lane.Name, lane.Demo);
                foreach (var service in lane.Services)
                    testbed!.Wake(service);
                lane.Run(new LaneContext(lane, collector.OtlpHttp, new CollectorTelemetryCheck(collector.Client, lane.ServiceName), testbed));
                Log.Information("real-{Lane}-demo-ok", lane.Name);
            }

            Log.Information("demos-ok lanes={Lanes}", string.Join(",", lanes.Select(static lane => lane.Name)));
        });
}
