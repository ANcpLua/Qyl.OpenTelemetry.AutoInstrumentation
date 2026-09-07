using System.Diagnostics;
using OpenTelemetry;
using SessionAttributes = Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Session.SessionAttributes;

namespace Qyl;

/// <summary>
/// Copies the qyl session key down a trace, in-process. Applications stamp <c>session.id</c> on one
/// span (typically the request handler); qyl groups spans into sessions per span, so without
/// propagation the tagged span's children — including the GenAI spans that carry token usage —
/// would fall back to trace-keyed sessions.
/// </summary>
/// <remarks>
/// The copy happens on end, the last moment the tag can be observed: an application sets the tag
/// while handling the request, after child spans may have started but before they end. Only
/// in-process ancestors are visible; remote parents propagate nothing.
/// </remarks>
internal sealed class QylSessionSpanProcessor : BaseProcessor<Activity>
{
    public override void OnEnd(Activity data)
    {
        if (data.GetTagItem(SessionAttributes.Id) is not null)
            return;

        for (var ancestor = data.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor.GetTagItem(SessionAttributes.Id) is { } sessionId)
            {
                data.SetTag(SessionAttributes.Id, sessionId);
                return;
            }
        }
    }
}
