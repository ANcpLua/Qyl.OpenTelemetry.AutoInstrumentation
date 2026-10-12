namespace Qyl.Build;

/// <summary>Grpc.Net.Client's native source: the raw CallInvoker shape, the ClientBase shape, and the
/// metadata capture opt-in, each managed and NativeAOT.</summary>
sealed class GrpcClientLane : DemoLane
{
    public override string Name => "grpc-client";

    public override string Demo => "Qyl.RealGrpcClientDemo";

    public override bool RunsNativeAot => true;

    public override void Run(LaneContext lane)
    {
        lane.BuildManaged();
        var executable = lane.PublishNativeAot();
        Verify(lane, lane.RunManaged("managed gRPC client demo"));
        Verify(lane, lane.RunNativeAot(executable, "NativeAOT gRPC client demo"));

        // ClientBase<T> and the raw CallInvoker share one native owner; metadata is opt-in in both.
        var client = lane.With(("QYL_GRPC_DEMO_MODE", "client"));
        Verify(lane, lane.RunManaged("managed gRPC client demo (client lane)", client));
        Verify(lane, lane.RunNativeAot(executable, "NativeAOT gRPC client demo (client lane)", client));

        var optIn = lane.With(
            ("QYL_GRPC_DEMO_MODE", "client"),
            ("OTEL_DOTNET_AUTO_TRACES_GRPCNETCLIENT_INSTRUMENTATION_CAPTURE_REQUEST_METADATA", "X-Demo-Md"),
            ("OTEL_DOTNET_AUTO_TRACES_GRPCNETCLIENT_INSTRUMENTATION_CAPTURE_RESPONSE_METADATA", "X-Demo-Res-Md"));
        Verify(lane, lane.RunManaged("managed gRPC client demo (metadata opt-in)", optIn));
        Verify(lane, lane.RunNativeAot(executable, "NativeAOT gRPC client demo (metadata opt-in)", optIn));
    }

    private static void Verify(LaneContext lane, DemoRun run)
    {
        run.RequireSucceeded().RequireTokens("expected-failure=Unavailable").RequireReportPassed();
        var activities = run.Array("Activities");
        var grpc = activities.EnumerateArray().Count(static activity => ReportedActivity.Tag(activity, "qyl.instrumentation.domain") == "rpc.grpc");
        Gate.Require(grpc == 2, () => $"{run.Label} expected exactly 2 gRPC client activities, got {grpc}:\n{activities}");
        lane.Telemetry.Spans(run, activities, "gRPC client");
    }
}
