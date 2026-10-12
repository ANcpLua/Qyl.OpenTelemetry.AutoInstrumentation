namespace Qyl.Build;

sealed partial class QylBuild
{
    Target TestbedProbe => definition => definition
        .Description("Wake and reach the eight Railway testbed services named by the testbed environment file, with wake timing")
        .Executes(() =>
        {
            var testbed = Testbed.Load();
            Log.Information("testbed file {Path}; wake limit {Seconds:0}s", testbed.Path, testbed.WakeLimit.TotalSeconds);
            var services = Enum.GetValues<TestbedService>();
            var results = services.AsParallel().WithDegreeOfParallelism(services.Length).Select(service =>
            {
                try
                {
                    var (host, port) = testbed.Endpoint(service);
                    return (Service: service, Ok: true, Detail: $"{host}:{port} answered after {testbed.Wake(service).TotalSeconds:0.0}s");
                }
                catch (GateFailure failure)
                {
                    return (Service: service, Ok: false, Detail: failure.Message);
                }
            }).ToArray();

            foreach (var (service, ok, detail) in results.OrderBy(static result => result.Service))
                Log.Information("  [{State}] {Service,-9} {Detail}", ok ? "UP" : "DOWN", service, detail);
            var down = results.Where(static result => !result.Ok).Select(static result => result.Service.ToString()).ToArray();
            Gate.Require(down.Length == 0, $"testbed services unreachable: {string.Join(", ", down)}");
            Log.Information("testbed-probe-ok services={Count}", results.Length);
        });
}
