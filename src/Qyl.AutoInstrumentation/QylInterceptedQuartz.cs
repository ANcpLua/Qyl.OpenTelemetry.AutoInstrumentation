using System.Diagnostics;

namespace Qyl.AutoInstrumentation;

public static class QylInterceptedQuartz
{

    public static Activity? StartActivity()
    {
        if (!QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Traces, QylAutoInstrumentationIds.Quartz))
            return null;

        var activity = QylActivitySource.Source.StartActivity("Quartz execute", ActivityKind.Internal);
        if (activity is null)
            return null;

        activity.SetTag(QylSemanticAttributes.QylInstrumentationDomain, QylInstrumentationDomains.JobQuartz);
        return activity;
    }

    public static void RecordSuccess(Activity? activity)
    {
    }

    public static Task ObserveAsync(Task? task, Activity? activity)
    {
        if (activity is null || task is null)
        {
            activity?.Dispose();
            return task!;
        }

        return ObserveSlowAsync(task, activity);
    }

    private static async Task ObserveSlowAsync(Task task, Activity activity)
    {
        try
        {
            await task.ConfigureAwait(false);
            RecordSuccess(activity);
        }
        catch (Exception exception)
        {
            RecordException(activity, exception);
            throw;
        }
        finally
        {
            activity.Dispose();
        }
    }

    public static void RecordException(Activity? activity, Exception exception)
    {
        activity?.SetTag(QylSemanticAttributes.ErrorType, exception.GetType().Name);
        activity?.SetStatus(ActivityStatusCode.Error);
    }
}
