using System.Diagnostics;
using OpenTelemetry;
using Qyl.Telemetry.AutoInstrumentation;
using RpcAttributes = Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Rpc.RpcAttributes;

namespace Qyl;

internal sealed class QylCoreWcfSpanProcessor : BaseProcessor<Activity>
{
    private const string LegacyRpcSystem = "rpc.system";

    public override void OnEnd(Activity data)
    {
        if (!StringComparer.Ordinal.Equals(data.Source.Name, QylTelemetrySources.CoreWcf))
            return;

        if (data.GetTagItem(LegacyRpcSystem) is { } system)
            data.SetTag(RpcAttributes.SystemName, system);

        data.SetTag(LegacyRpcSystem, null);
    }
}
