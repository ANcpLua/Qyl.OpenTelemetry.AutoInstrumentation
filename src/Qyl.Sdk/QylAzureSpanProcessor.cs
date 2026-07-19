using System.Diagnostics;
using OpenTelemetry;
using Qyl.OpenTelemetry.AutoInstrumentation;

namespace Qyl;

internal sealed class QylAzureSpanProcessor : BaseProcessor<Activity>
{
    public override void OnEnd(Activity data)
    {
        if (!data.Source.Name.StartsWith("Azure.", StringComparison.Ordinal))
            return;

        data.SetTag(QylSemanticAttributes.QylInstrumentationDomain, QylInstrumentationDomains.AzureSdk);
        data.SetTag(QylSemanticAttributes.UrlFull, null);
        data.SetTag(QylSemanticAttributes.UrlPath, null);

        if (data.Status is ActivityStatusCode.Error)
        {
            var exceptionType = data.GetTagItem(QylSemanticAttributes.ErrorType) as string
                ?? FindExceptionType(data);
            if (exceptionType is not null)
                data.SetTag(QylSemanticAttributes.ErrorType, GetSimpleTypeName(exceptionType));
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
