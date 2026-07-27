using System.Diagnostics;
using OpenTelemetry;

namespace Qyl;

/// <summary>
/// Propagates the qyl session key across a trace. Applications stamp <c>session.id</c> on one
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
    private const string SessionIdTag = "session.id";

    public override void OnEnd(Activity data)
    {
        if (data.GetTagItem(SessionIdTag) is not null)
            return;

        for (var ancestor = data.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor.GetTagItem(SessionIdTag) is { } sessionId)
            {
                data.SetTag(SessionIdTag, sessionId);
                return;
            }
        }
    }
}
