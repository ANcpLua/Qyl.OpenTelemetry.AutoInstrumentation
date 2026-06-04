using System.Diagnostics;
using System.Globalization;
using System.Net.Http;

namespace Qyl.AutoInstrumentation;

/// <summary>
/// Runtime target for compile-time generated HttpClient interceptors. These methods deliberately
/// call the original BCL API before returning a wrapper task so synchronous exceptions from
/// <see cref="HttpClient.SendAsync(HttpRequestMessage)"/> stay synchronous.
/// </summary>
public static class QylInterceptedHttpClient
{
    private const string DomainAttribute = "qyl.instrumentation.domain";
    private const string HttpClientDomain = "http.client";
    private const string HttpRequestMethodAttribute = "http.request.method";
    private const string HttpResponseStatusCodeAttribute = "http.response.status_code";
    private const string ServerAddressAttribute = "server.address";
    private const string ServerPortAttribute = "server.port";
    private const string UrlFullAttribute = "url.full";
    private const string ErrorTypeAttribute = "error.type";
    private const string UnknownHttpMethod = "_OTHER";

    private static readonly bool CaptureSensitiveValues = ReadBoolean("QYL_AUTOINSTRUMENTATION_CAPTURE_SENSITIVE_VALUES");

    public static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpRequestMessage request)
    {
        ThrowIfInvalidCallTarget(client, request);

        var activity = StartHttpClientActivity(request);
        try
        {
            return ObserveAsync(client.SendAsync(request), activity);
        }
        catch (Exception exception)
        {
            RecordException(activity, exception);
            activity?.Dispose();
            throw;
        }
    }

    public static Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ThrowIfInvalidCallTarget(client, request);

        var activity = StartHttpClientActivity(request);
        try
        {
            return ObserveAsync(client.SendAsync(request, cancellationToken), activity);
        }
        catch (Exception exception)
        {
            RecordException(activity, exception);
            activity?.Dispose();
            throw;
        }
    }

    public static Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpRequestMessage request,
        HttpCompletionOption completionOption)
    {
        ThrowIfInvalidCallTarget(client, request);

        var activity = StartHttpClientActivity(request);
        try
        {
            return ObserveAsync(client.SendAsync(request, completionOption), activity);
        }
        catch (Exception exception)
        {
            RecordException(activity, exception);
            activity?.Dispose();
            throw;
        }
    }

    public static Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpRequestMessage request,
        HttpCompletionOption completionOption,
        CancellationToken cancellationToken)
    {
        ThrowIfInvalidCallTarget(client, request);

        var activity = StartHttpClientActivity(request);
        try
        {
            return ObserveAsync(client.SendAsync(request, completionOption, cancellationToken), activity);
        }
        catch (Exception exception)
        {
            RecordException(activity, exception);
            activity?.Dispose();
            throw;
        }
    }

    private static async Task<HttpResponseMessage> ObserveAsync(
        Task<HttpResponseMessage> originalTask,
        Activity? activity)
    {
        try
        {
            var response = await originalTask.ConfigureAwait(false);
            activity?.SetTag(HttpResponseStatusCodeAttribute, (int)response.StatusCode);

            if ((int)response.StatusCode >= 400)
            {
                activity?.SetTag(ErrorTypeAttribute, ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture));
                activity?.SetStatus(ActivityStatusCode.Error);
            }

            return response;
        }
        catch (Exception exception)
        {
            RecordException(activity, exception);
            throw;
        }
        finally
        {
            activity?.Dispose();
        }
    }

    private static Activity? StartHttpClientActivity(HttpRequestMessage request)
    {
        var method = NormalizeMethod(request.Method.Method);
        var activity = QylActivitySource.Source.StartActivity($"HTTP {method}", ActivityKind.Client);
        if (activity is null)
            return null;

        activity.SetTag(DomainAttribute, HttpClientDomain);
        activity.SetTag(HttpRequestMethodAttribute, method);

        var uri = request.RequestUri;
        if (uri is not null)
        {
            activity.SetTag(ServerAddressAttribute, uri.Host);
            if (!uri.IsDefaultPort)
                activity.SetTag(ServerPortAttribute, uri.Port);

            if (CaptureSensitiveValues)
                activity.SetTag(UrlFullAttribute, uri.ToString());
        }

        return activity;
    }

    private static void ThrowIfInvalidCallTarget(HttpClient client, HttpRequestMessage request)
    {
        if (client is null)
            throw new NullReferenceException();

        ArgumentNullException.ThrowIfNull(request);
    }

    private static void RecordException(Activity? activity, Exception exception)
    {
        activity?.SetTag(ErrorTypeAttribute, exception.GetType().Name);
        activity?.SetStatus(ActivityStatusCode.Error);
    }

    private static string NormalizeMethod(string? method)
        => method switch
        {
            "CONNECT" or "DELETE" or "GET" or "HEAD" or "OPTIONS" or "PATCH" or "POST" or "PUT" or "TRACE" => method,
            _ => UnknownHttpMethod,
        };

    private static bool ReadBoolean(string name)
        => Environment.GetEnvironmentVariable(name)?.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "on" => true,
            _ => false,
        };
}
