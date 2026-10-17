namespace Qyl.Build;

sealed partial class QylBuild
{
    [Parameter("LiveCheck: lanes to skip, named in the output as partial (azure corewcf elastictransport elasticsearch masstransit mongodb nservicebus quartz rabbitmq)", Name = "LiveCheckSkip")]
    readonly string[] _liveCheckSkip = [];

    Target LiveCheck => definition => definition
        .Description("The nine native-source demo lanes exported into weaver registry live-check at v<Qyl.Telemetry.SemanticConventions pin>; zero violations")
        .After(Demos)
        .Executes(() => WeaverLiveCheck.Run(_liveCheckSkip));
}
