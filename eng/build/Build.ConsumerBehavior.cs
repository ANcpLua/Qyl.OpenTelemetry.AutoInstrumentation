namespace Qyl.Build;

sealed partial class QylBuild
{
    Target ConsumerBehavior => definition => definition
        .Description("Consumer behavior: ProjectReference and package consumers, environment options, AddQyl, the application scope and QYL0111, kill switches, middleware delegates, interceptors, parity and the NativeAOT consumer")
        .After(PackageLayout)
        .Executes(() =>
        {
            ProjectReferenceBehavior.Run();
            EnvironmentOptionsBehavior.Run();
            AddQylBehavior.Run();
            ApplicationScopeBehavior.Run();
            InstrumentationDisabledBehavior.Run();
            AspNetCoreMiddlewareDelegate.Run();
            SourceInterceptorConsumer.Run();
            ConsumerParity.Run();
            NativeAotConsumer.Run();
            Log.Information("consumer-behavior-gate-ok");
        });
}
