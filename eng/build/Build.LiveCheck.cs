namespace Qyl.Build;

sealed partial class QylBuild
{
    [Parameter("LiveCheck: lanes to skip, named in the output as partial (azure corewcf elastictransport elasticsearch masstransit mongodb nservicebus quartz rabbitmq)")]
    readonly string[] LiveCheckSkip = [];

    Target LiveCheck => definition => definition
        .Description("The nine native-source demo lanes exported into weaver registry live-check at SEMCONV_REF (tools/live-check.env); zero violations")
        .After(Demos)
        .Executes(() => WeaverLiveCheck.Run(LiveCheckSkip));
}
