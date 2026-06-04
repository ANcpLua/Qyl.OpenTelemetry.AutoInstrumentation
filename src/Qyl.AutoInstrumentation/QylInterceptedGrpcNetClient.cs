using System.Diagnostics;

namespace Qyl.AutoInstrumentation;

public static class QylInterceptedGrpcNetClient
{
    private const string GrpcDomain = "rpc.grpc";

    public static Activity? StartActivity(string clientTypeName, string methodName)
    {
        ArgumentNullException.ThrowIfNull(clientTypeName);
        ArgumentNullException.ThrowIfNull(methodName);

        if (!QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Traces, QylAutoInstrumentationIds.GrpcNetClient))
            return null;

        var service = GetServiceName(clientTypeName);
        var activity = QylActivitySource.Source.StartActivity("gRPC " + service + "/" + methodName, ActivityKind.Client);
        if (activity is null)
            return null;

        activity.SetTag(QylSemanticAttributes.QylInstrumentationDomain, GrpcDomain);
        activity.SetTag(QylSemanticAttributes.RpcSystem, "grpc");
        activity.SetTag(QylSemanticAttributes.RpcService, service);
        activity.SetTag(QylSemanticAttributes.RpcMethod, methodName);
        return activity;
    }

    public static async Task<TResponse> ObserveUnaryResponseAsync<TResponse>(Task<TResponse> responseTask, Activity? activity)
    {
        if (activity is null)
            return await responseTask.ConfigureAwait(false);

        try
        {
            var response = await responseTask.ConfigureAwait(false);
            activity.SetTag(QylSemanticAttributes.RpcGrpcStatusCode, 0);
            return response;
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

    public static void Dispose(Activity? activity)
        => activity?.Dispose();

    private static string GetServiceName(string clientTypeName)
    {
        var lastDot = clientTypeName.LastIndexOf(".", StringComparison.Ordinal);
        var service = lastDot < 0 ? clientTypeName : clientTypeName[(lastDot + 1)..];
        return service.EndsWith("Client", StringComparison.Ordinal) ? service[..^6] : service;
    }
}
