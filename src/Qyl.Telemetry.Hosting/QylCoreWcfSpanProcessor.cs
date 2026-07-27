using System.Diagnostics;
using OpenTelemetry;
using Qyl.Telemetry.AutoInstrumentation;

namespace Qyl;

internal sealed class QylCoreWcfSpanProcessor : BaseProcessor<Activity>
{
    private const string LegacyRpcSystem = "rpc.system";

    public override void OnEnd(Activity data)
    {
        if (!StringComparer.Ordinal.Equals(data.Source.Name, QylTelemetrySources.CoreWcf))
            return;

        if (data.GetTagItem(LegacyRpcSystem) is { } system)
            data.SetTag(QylSemanticAttributes.RpcSystem, system);

        data.SetTag(LegacyRpcSystem, null);
    }
}
