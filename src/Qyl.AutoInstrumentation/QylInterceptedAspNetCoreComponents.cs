using System.Diagnostics.Metrics;

namespace Qyl.AutoInstrumentation;

public static class QylInterceptedAspNetCoreComponents
{
    private static readonly Meter Meter = new("Microsoft.AspNetCore.Components");
    private static readonly Counter<long> NavigationCounter = Meter.CreateCounter<long>("aspnetcore.components.navigation");

    public static void RecordNavigation()
    {
        if (!QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Metrics, QylAutoInstrumentationIds.AspNetCore))
            return;

        NavigationCounter.Add(1);
    }
}
