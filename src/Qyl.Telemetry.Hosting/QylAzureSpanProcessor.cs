using System.Diagnostics;
using OpenTelemetry;
using Qyl.Telemetry.AutoInstrumentation;
using Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Qyl;
using ErrorAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Error.ErrorAttributes;
using UrlAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Url.UrlAttributes;

namespace Qyl;

internal sealed class QylAzureSpanProcessor : BaseProcessor<Activity>
{
    public override void OnEnd(Activity data)
    {
        if (!data.Source.Name.StartsWith("Azure.", StringComparison.Ordinal))
            return;

        data.SetTag(QylAttributes.InstrumentationDomain, QylAttributes.InstrumentationDomainValues.AzureSdk);
        data.SetTag(UrlAttributes.Full, null);
        data.SetTag(UrlAttributes.Path, null);

        if (data.Status is ActivityStatusCode.Error)
        {
            var exceptionType = data.GetTagItem(ErrorAttributes.Type) as string
                ?? FindExceptionType(data);
            if (exceptionType is not null)
                data.SetTag(ErrorAttributes.Type, GetSimpleTypeName(exceptionType));
        }
    }

    private static string? FindExceptionType(Activity activity)
    {
        foreach (var activityEvent in activity.Events)
        {
            foreach (var tag in activityEvent.Tags)
            {
                if (StringComparer.Ordinal.Equals(tag.Key, "exception.type") && tag.Value is string exceptionType)
                    return exceptionType;
            }
        }

        return null;
    }

    private static string GetSimpleTypeName(string typeName)
    {
        var separator = typeName.LastIndexOf('.');
        return separator >= 0 ? typeName[(separator + 1)..] : typeName;
    }
}
