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

    public static Task<HttpResponseMessage> GetAsync(HttpClient client, string? requestUri)
    {
        ThrowIfNullClient(client);

        var activity = StartHttpClientActivity("GET", requestUri);
        try
        {
            return ObserveAsync(client.GetAsync(requestUri), activity);
        }
        catch (Exception exception)
        {
            RecordException(activity, exception);
            activity?.Dispose();
            throw;
        }
    }

    public static Task<HttpResponseMessage> GetAsync(HttpClient client, Uri? requestUri)
    {
        ThrowIfNullClient(client);

        var activity = StartHttpClientActivity("GET", requestUri);
        try
        {
            return ObserveAsync(client.GetAsync(requestUri), activity);
        }
        catch (Exception exception)
        {
            RecordException(activity, exception);
            activity?.Dispose();
            throw;
        }
    }

    public static Task<HttpResponseMessage> GetAsync(
        HttpClient client,
        string? requestUri,
        CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);

        var activity = StartHttpClientActivity("GET", requestUri);
        try
        {
            return ObserveAsync(client.GetAsync(requestUri, cancellationToken), activity);
        }
        catch (Exception exception)
        {
            RecordException(activity, exception);
            activity?.Dispose();
            throw;
        }
    }

    public static Task<HttpResponseMessage> GetAsync(
        HttpClient client,
        Uri? requestUri,
        CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);

        var activity = StartHttpClientActivity("GET", requestUri);
        try
        {
            return ObserveAsync(client.GetAsync(requestUri, cancellationToken), activity);
        }
        catch (Exception exception)
        {
            RecordException(activity, exception);
            activity?.Dispose();
            throw;
        }
    }

    public static Task<HttpResponseMessage> GetAsync(
        HttpClient client,
        string? requestUri,
        HttpCompletionOption completionOption)
    {
        ThrowIfNullClient(client);

        var activity = StartHttpClientActivity("GET", requestUri);
        try
        {
            return ObserveAsync(client.GetAsync(requestUri, completionOption), activity);
        }
        catch (Exception exception)
        {
            RecordException(activity, exception);
            activity?.Dispose();
            throw;
        }
    }

    public static Task<HttpResponseMessage> GetAsync(
        HttpClient client,
        Uri? requestUri,
        HttpCompletionOption completionOption)
    {
        ThrowIfNullClient(client);

        var activity = StartHttpClientActivity("GET", requestUri);
        try
        {
            return ObserveAsync(client.GetAsync(requestUri, completionOption), activity);
        }
        catch (Exception exception)
        {
            RecordException(activity, exception);
            activity?.Dispose();
            throw;
        }
    }

    public static Task<HttpResponseMessage> GetAsync(
        HttpClient client,
        string? requestUri,
        HttpCompletionOption completionOption,
        CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);

        var activity = StartHttpClientActivity("GET", requestUri);
        try
        {
            return ObserveAsync(client.GetAsync(requestUri, completionOption, cancellationToken), activity);
        }
        catch (Exception exception)
        {
            RecordException(activity, exception);
            activity?.Dispose();
            throw;
        }
    }

    public static Task<HttpResponseMessage> GetAsync(
        HttpClient client,
        Uri? requestUri,
        HttpCompletionOption completionOption,
        CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);

        var activity = StartHttpClientActivity("GET", requestUri);
        try
        {
            return ObserveAsync(client.GetAsync(requestUri, completionOption, cancellationToken), activity);
        }
        catch (Exception exception)
        {
            RecordException(activity, exception);
            activity?.Dispose();
            throw;
        }
    }

    public static Task<HttpResponseMessage> DeleteAsync(HttpClient client, string? requestUri)
    {
        ThrowIfNullClient(client);

        var activity = StartHttpClientActivity("DELETE", requestUri);
        try
        {
            return ObserveAsync(client.DeleteAsync(requestUri), activity);
        }
        catch (Exception exception)
        {
            RecordException(activity, exception);
            activity?.Dispose();
            throw;
        }
    }

    public static Task<HttpResponseMessage> DeleteAsync(HttpClient client, Uri? requestUri)
    {
        ThrowIfNullClient(client);

        var activity = StartHttpClientActivity("DELETE", requestUri);
        try
        {
            return ObserveAsync(client.DeleteAsync(requestUri), activity);
        }
        catch (Exception exception)
        {
            RecordException(activity, exception);
            activity?.Dispose();
            throw;
        }
    }

    public static Task<HttpResponseMessage> DeleteAsync(
        HttpClient client,
        string? requestUri,
        CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);

        var activity = StartHttpClientActivity("DELETE", requestUri);
        try
        {
            return ObserveAsync(client.DeleteAsync(requestUri, cancellationToken), activity);
        }
        catch (Exception exception)
        {
            RecordException(activity, exception);
            activity?.Dispose();
            throw;
        }
    }

    public static Task<HttpResponseMessage> DeleteAsync(
        HttpClient client,
        Uri? requestUri,
        CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);

        var activity = StartHttpClientActivity("DELETE", requestUri);
        try
        {
            return ObserveAsync(client.DeleteAsync(requestUri, cancellationToken), activity);
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
        => StartHttpClientActivity(request.Method.Method, request.RequestUri, request.RequestUri?.ToString());

    private static Activity? StartHttpClientActivity(string method, string? requestUri)
    {
        Uri? uri = null;
        if (!string.IsNullOrWhiteSpace(requestUri))
            Uri.TryCreate(requestUri, UriKind.RelativeOrAbsolute, out uri);

        return StartHttpClientActivity(method, uri, requestUri);
    }

    private static Activity? StartHttpClientActivity(string method, Uri? requestUri)
        => StartHttpClientActivity(method, requestUri, requestUri?.ToString());

    private static Activity? StartHttpClientActivity(string method, Uri? requestUri, string? rawRequestUri)
    {
        method = NormalizeMethod(method);
        var activity = QylActivitySource.Source.StartActivity($"HTTP {method}", ActivityKind.Client);
        if (activity is null)
            return null;

        activity.SetTag(DomainAttribute, HttpClientDomain);
        activity.SetTag(HttpRequestMethodAttribute, method);

        if (requestUri is not null)
        {
            if (requestUri.IsAbsoluteUri)
            {
                activity.SetTag(ServerAddressAttribute, requestUri.Host);
                if (!requestUri.IsDefaultPort)
                    activity.SetTag(ServerPortAttribute, requestUri.Port);
            }

            if (CaptureSensitiveValues)
                activity.SetTag(UrlFullAttribute, rawRequestUri ?? requestUri.ToString());
        }

        return activity;
    }

    private static void ThrowIfInvalidCallTarget(HttpClient client, HttpRequestMessage request)
    {
        ThrowIfNullClient(client);

        ArgumentNullException.ThrowIfNull(request);
    }

    private static void ThrowIfNullClient(HttpClient client)
    {
        if (client is null)
            throw new NullReferenceException();
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
