using System.Diagnostics;
using System.Globalization;
using System.Net.Http;

namespace Qyl.AutoInstrumentation;

/// <summary>
/// Runtime target for compile-time generated HttpClient interceptors. Each method calls the original
/// BCL API so qyl observes HttpClient behavior without reimplementing transport semantics.
/// </summary>
public static class QylInterceptedHttpClient
{
    private const string HttpClientDomain = "http.client";
    private const string UnknownHttpMethod = "_OTHER";

    public static HttpResponseMessage Send(HttpClient client, HttpRequestMessage request)
    {
        ThrowIfInvalidCallTarget(client, request);
        var activity = StartHttpClientActivity(request);
        if (activity is null)
            return client.Send(request);

        try
        {
            var response = client.Send(request);
            RecordResponse(activity, response);
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

    public static HttpResponseMessage Send(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ThrowIfInvalidCallTarget(client, request);
        var activity = StartHttpClientActivity(request);
        if (activity is null)
            return client.Send(request, cancellationToken);

        try
        {
            var response = client.Send(request, cancellationToken);
            RecordResponse(activity, response);
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

    public static HttpResponseMessage Send(HttpClient client, HttpRequestMessage request, HttpCompletionOption completionOption)
    {
        ThrowIfInvalidCallTarget(client, request);
        var activity = StartHttpClientActivity(request);
        if (activity is null)
            return client.Send(request, completionOption);

        try
        {
            var response = client.Send(request, completionOption);
            RecordResponse(activity, response);
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

    public static HttpResponseMessage Send(HttpClient client, HttpRequestMessage request, HttpCompletionOption completionOption, CancellationToken cancellationToken)
    {
        ThrowIfInvalidCallTarget(client, request);
        var activity = StartHttpClientActivity(request);
        if (activity is null)
            return client.Send(request, completionOption, cancellationToken);

        try
        {
            var response = client.Send(request, completionOption, cancellationToken);
            RecordResponse(activity, response);
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

    public static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpRequestMessage request)
    {
        ThrowIfInvalidCallTarget(client, request);
        var activity = StartHttpClientActivity(request);
        try { return ObserveResponseAsync(client.SendAsync(request), activity); }
        catch (Exception exception) { RecordException(activity, exception); activity?.Dispose(); throw; }
    }

    public static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ThrowIfInvalidCallTarget(client, request);
        var activity = StartHttpClientActivity(request);
        try { return ObserveResponseAsync(client.SendAsync(request, cancellationToken), activity); }
        catch (Exception exception) { RecordException(activity, exception); activity?.Dispose(); throw; }
    }

    public static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpRequestMessage request, HttpCompletionOption completionOption)
    {
        ThrowIfInvalidCallTarget(client, request);
        var activity = StartHttpClientActivity(request);
        try { return ObserveResponseAsync(client.SendAsync(request, completionOption), activity); }
        catch (Exception exception) { RecordException(activity, exception); activity?.Dispose(); throw; }
    }

    public static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpRequestMessage request, HttpCompletionOption completionOption, CancellationToken cancellationToken)
    {
        ThrowIfInvalidCallTarget(client, request);
        var activity = StartHttpClientActivity(request);
        try { return ObserveResponseAsync(client.SendAsync(request, completionOption, cancellationToken), activity); }
        catch (Exception exception) { RecordException(activity, exception); activity?.Dispose(); throw; }
    }

    public static Task<HttpResponseMessage> GetAsync(HttpClient client, string? requestUri)
    {
        ThrowIfNullClient(client);
        var activity = StartHttpClientActivity("GET", requestUri);
        try { return ObserveResponseAsync(client.GetAsync(requestUri), activity); }
        catch (Exception exception) { RecordException(activity, exception); activity?.Dispose(); throw; }
    }

    public static Task<HttpResponseMessage> GetAsync(HttpClient client, Uri? requestUri)
    {
        ThrowIfNullClient(client);
        var activity = StartHttpClientActivity("GET", requestUri);
        try { return ObserveResponseAsync(client.GetAsync(requestUri), activity); }
        catch (Exception exception) { RecordException(activity, exception); activity?.Dispose(); throw; }
    }

    public static Task<HttpResponseMessage> GetAsync(HttpClient client, string? requestUri, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var activity = StartHttpClientActivity("GET", requestUri);
        try { return ObserveResponseAsync(client.GetAsync(requestUri, cancellationToken), activity); }
        catch (Exception exception) { RecordException(activity, exception); activity?.Dispose(); throw; }
    }

    public static Task<HttpResponseMessage> GetAsync(HttpClient client, Uri? requestUri, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var activity = StartHttpClientActivity("GET", requestUri);
        try { return ObserveResponseAsync(client.GetAsync(requestUri, cancellationToken), activity); }
        catch (Exception exception) { RecordException(activity, exception); activity?.Dispose(); throw; }
    }

    public static Task<HttpResponseMessage> GetAsync(HttpClient client, string? requestUri, HttpCompletionOption completionOption)
    {
        ThrowIfNullClient(client);
        var activity = StartHttpClientActivity("GET", requestUri);
        try { return ObserveResponseAsync(client.GetAsync(requestUri, completionOption), activity); }
        catch (Exception exception) { RecordException(activity, exception); activity?.Dispose(); throw; }
    }

    public static Task<HttpResponseMessage> GetAsync(HttpClient client, Uri? requestUri, HttpCompletionOption completionOption)
    {
        ThrowIfNullClient(client);
        var activity = StartHttpClientActivity("GET", requestUri);
        try { return ObserveResponseAsync(client.GetAsync(requestUri, completionOption), activity); }
        catch (Exception exception) { RecordException(activity, exception); activity?.Dispose(); throw; }
    }

    public static Task<HttpResponseMessage> GetAsync(HttpClient client, string? requestUri, HttpCompletionOption completionOption, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var activity = StartHttpClientActivity("GET", requestUri);
        try { return ObserveResponseAsync(client.GetAsync(requestUri, completionOption, cancellationToken), activity); }
        catch (Exception exception) { RecordException(activity, exception); activity?.Dispose(); throw; }
    }

    public static Task<HttpResponseMessage> GetAsync(HttpClient client, Uri? requestUri, HttpCompletionOption completionOption, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var activity = StartHttpClientActivity("GET", requestUri);
        try { return ObserveResponseAsync(client.GetAsync(requestUri, completionOption, cancellationToken), activity); }
        catch (Exception exception) { RecordException(activity, exception); activity?.Dispose(); throw; }
    }

    public static Task<HttpResponseMessage> PostAsync(HttpClient client, string? requestUri, HttpContent? content)
    {
        ThrowIfNullClient(client);
        var activity = StartHttpClientActivity("POST", requestUri);
        try { return ObserveResponseAsync(client.PostAsync(requestUri, content), activity); }
        catch (Exception exception) { RecordException(activity, exception); activity?.Dispose(); throw; }
    }

    public static Task<HttpResponseMessage> PostAsync(HttpClient client, Uri? requestUri, HttpContent? content)
    {
        ThrowIfNullClient(client);
        var activity = StartHttpClientActivity("POST", requestUri);
        try { return ObserveResponseAsync(client.PostAsync(requestUri, content), activity); }
        catch (Exception exception) { RecordException(activity, exception); activity?.Dispose(); throw; }
    }

    public static Task<HttpResponseMessage> PostAsync(HttpClient client, string? requestUri, HttpContent? content, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var activity = StartHttpClientActivity("POST", requestUri);
        try { return ObserveResponseAsync(client.PostAsync(requestUri, content, cancellationToken), activity); }
        catch (Exception exception) { RecordException(activity, exception); activity?.Dispose(); throw; }
    }

    public static Task<HttpResponseMessage> PostAsync(HttpClient client, Uri? requestUri, HttpContent? content, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var activity = StartHttpClientActivity("POST", requestUri);
        try { return ObserveResponseAsync(client.PostAsync(requestUri, content, cancellationToken), activity); }
        catch (Exception exception) { RecordException(activity, exception); activity?.Dispose(); throw; }
    }

    public static Task<HttpResponseMessage> PutAsync(HttpClient client, string? requestUri, HttpContent? content)
    {
        ThrowIfNullClient(client);
        var activity = StartHttpClientActivity("PUT", requestUri);
        try { return ObserveResponseAsync(client.PutAsync(requestUri, content), activity); }
        catch (Exception exception) { RecordException(activity, exception); activity?.Dispose(); throw; }
    }

    public static Task<HttpResponseMessage> PutAsync(HttpClient client, Uri? requestUri, HttpContent? content)
    {
        ThrowIfNullClient(client);
        var activity = StartHttpClientActivity("PUT", requestUri);
        try { return ObserveResponseAsync(client.PutAsync(requestUri, content), activity); }
        catch (Exception exception) { RecordException(activity, exception); activity?.Dispose(); throw; }
    }

    public static Task<HttpResponseMessage> PutAsync(HttpClient client, string? requestUri, HttpContent? content, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var activity = StartHttpClientActivity("PUT", requestUri);
        try { return ObserveResponseAsync(client.PutAsync(requestUri, content, cancellationToken), activity); }
        catch (Exception exception) { RecordException(activity, exception); activity?.Dispose(); throw; }
    }

    public static Task<HttpResponseMessage> PutAsync(HttpClient client, Uri? requestUri, HttpContent? content, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var activity = StartHttpClientActivity("PUT", requestUri);
        try { return ObserveResponseAsync(client.PutAsync(requestUri, content, cancellationToken), activity); }
        catch (Exception exception) { RecordException(activity, exception); activity?.Dispose(); throw; }
    }

    public static Task<HttpResponseMessage> PatchAsync(HttpClient client, string? requestUri, HttpContent? content)
    {
        ThrowIfNullClient(client);
        var activity = StartHttpClientActivity("PATCH", requestUri);
        try { return ObserveResponseAsync(client.PatchAsync(requestUri, content), activity); }
        catch (Exception exception) { RecordException(activity, exception); activity?.Dispose(); throw; }
    }

    public static Task<HttpResponseMessage> PatchAsync(HttpClient client, Uri? requestUri, HttpContent? content)
    {
        ThrowIfNullClient(client);
        var activity = StartHttpClientActivity("PATCH", requestUri);
        try { return ObserveResponseAsync(client.PatchAsync(requestUri, content), activity); }
        catch (Exception exception) { RecordException(activity, exception); activity?.Dispose(); throw; }
    }

    public static Task<HttpResponseMessage> PatchAsync(HttpClient client, string? requestUri, HttpContent? content, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var activity = StartHttpClientActivity("PATCH", requestUri);
        try { return ObserveResponseAsync(client.PatchAsync(requestUri, content, cancellationToken), activity); }
        catch (Exception exception) { RecordException(activity, exception); activity?.Dispose(); throw; }
    }

    public static Task<HttpResponseMessage> PatchAsync(HttpClient client, Uri? requestUri, HttpContent? content, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var activity = StartHttpClientActivity("PATCH", requestUri);
        try { return ObserveResponseAsync(client.PatchAsync(requestUri, content, cancellationToken), activity); }
        catch (Exception exception) { RecordException(activity, exception); activity?.Dispose(); throw; }
    }

    public static Task<HttpResponseMessage> DeleteAsync(HttpClient client, string? requestUri)
    {
        ThrowIfNullClient(client);
        var activity = StartHttpClientActivity("DELETE", requestUri);
        try { return ObserveResponseAsync(client.DeleteAsync(requestUri), activity); }
        catch (Exception exception) { RecordException(activity, exception); activity?.Dispose(); throw; }
    }

    public static Task<HttpResponseMessage> DeleteAsync(HttpClient client, Uri? requestUri)
    {
        ThrowIfNullClient(client);
        var activity = StartHttpClientActivity("DELETE", requestUri);
        try { return ObserveResponseAsync(client.DeleteAsync(requestUri), activity); }
        catch (Exception exception) { RecordException(activity, exception); activity?.Dispose(); throw; }
    }

    public static Task<HttpResponseMessage> DeleteAsync(HttpClient client, string? requestUri, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var activity = StartHttpClientActivity("DELETE", requestUri);
        try { return ObserveResponseAsync(client.DeleteAsync(requestUri, cancellationToken), activity); }
        catch (Exception exception) { RecordException(activity, exception); activity?.Dispose(); throw; }
    }

    public static Task<HttpResponseMessage> DeleteAsync(HttpClient client, Uri? requestUri, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var activity = StartHttpClientActivity("DELETE", requestUri);
        try { return ObserveResponseAsync(client.DeleteAsync(requestUri, cancellationToken), activity); }
        catch (Exception exception) { RecordException(activity, exception); activity?.Dispose(); throw; }
    }

    public static Task<string> GetStringAsync(HttpClient client, string? requestUri)
    {
        ThrowIfNullClient(client);
        var activity = StartHttpClientActivity("GET", requestUri);
        try { return ObserveValueAsync(client.GetStringAsync(requestUri), activity); }
        catch (Exception exception) { RecordException(activity, exception); activity?.Dispose(); throw; }
    }

    public static Task<string> GetStringAsync(HttpClient client, Uri? requestUri)
    {
        ThrowIfNullClient(client);
        var activity = StartHttpClientActivity("GET", requestUri);
        try { return ObserveValueAsync(client.GetStringAsync(requestUri), activity); }
        catch (Exception exception) { RecordException(activity, exception); activity?.Dispose(); throw; }
    }

    public static Task<string> GetStringAsync(HttpClient client, string? requestUri, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var activity = StartHttpClientActivity("GET", requestUri);
        try { return ObserveValueAsync(client.GetStringAsync(requestUri, cancellationToken), activity); }
        catch (Exception exception) { RecordException(activity, exception); activity?.Dispose(); throw; }
    }

    public static Task<string> GetStringAsync(HttpClient client, Uri? requestUri, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var activity = StartHttpClientActivity("GET", requestUri);
        try { return ObserveValueAsync(client.GetStringAsync(requestUri, cancellationToken), activity); }
        catch (Exception exception) { RecordException(activity, exception); activity?.Dispose(); throw; }
    }

    public static Task<byte[]> GetByteArrayAsync(HttpClient client, string? requestUri)
    {
        ThrowIfNullClient(client);
        var activity = StartHttpClientActivity("GET", requestUri);
        try { return ObserveValueAsync(client.GetByteArrayAsync(requestUri), activity); }
        catch (Exception exception) { RecordException(activity, exception); activity?.Dispose(); throw; }
    }

    public static Task<byte[]> GetByteArrayAsync(HttpClient client, Uri? requestUri)
    {
        ThrowIfNullClient(client);
        var activity = StartHttpClientActivity("GET", requestUri);
        try { return ObserveValueAsync(client.GetByteArrayAsync(requestUri), activity); }
        catch (Exception exception) { RecordException(activity, exception); activity?.Dispose(); throw; }
    }

    public static Task<byte[]> GetByteArrayAsync(HttpClient client, string? requestUri, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var activity = StartHttpClientActivity("GET", requestUri);
        try { return ObserveValueAsync(client.GetByteArrayAsync(requestUri, cancellationToken), activity); }
        catch (Exception exception) { RecordException(activity, exception); activity?.Dispose(); throw; }
    }

    public static Task<byte[]> GetByteArrayAsync(HttpClient client, Uri? requestUri, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var activity = StartHttpClientActivity("GET", requestUri);
        try { return ObserveValueAsync(client.GetByteArrayAsync(requestUri, cancellationToken), activity); }
        catch (Exception exception) { RecordException(activity, exception); activity?.Dispose(); throw; }
    }

    public static Task<Stream> GetStreamAsync(HttpClient client, string? requestUri)
    {
        ThrowIfNullClient(client);
        var activity = StartHttpClientActivity("GET", requestUri);
        try { return ObserveValueAsync(client.GetStreamAsync(requestUri), activity); }
        catch (Exception exception) { RecordException(activity, exception); activity?.Dispose(); throw; }
    }

    public static Task<Stream> GetStreamAsync(HttpClient client, Uri? requestUri)
    {
        ThrowIfNullClient(client);
        var activity = StartHttpClientActivity("GET", requestUri);
        try { return ObserveValueAsync(client.GetStreamAsync(requestUri), activity); }
        catch (Exception exception) { RecordException(activity, exception); activity?.Dispose(); throw; }
    }

    public static Task<Stream> GetStreamAsync(HttpClient client, string? requestUri, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var activity = StartHttpClientActivity("GET", requestUri);
        try { return ObserveValueAsync(client.GetStreamAsync(requestUri, cancellationToken), activity); }
        catch (Exception exception) { RecordException(activity, exception); activity?.Dispose(); throw; }
    }

    public static Task<Stream> GetStreamAsync(HttpClient client, Uri? requestUri, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var activity = StartHttpClientActivity("GET", requestUri);
        try { return ObserveValueAsync(client.GetStreamAsync(requestUri, cancellationToken), activity); }
        catch (Exception exception) { RecordException(activity, exception); activity?.Dispose(); throw; }
    }

    private static Task<HttpResponseMessage> ObserveResponseAsync(Task<HttpResponseMessage> originalTask, Activity? activity)
        => activity is null ? originalTask : ObserveResponseSlowAsync(originalTask, activity);

    private static async Task<HttpResponseMessage> ObserveResponseSlowAsync(Task<HttpResponseMessage> originalTask, Activity activity)
    {
        try
        {
            var response = await originalTask.ConfigureAwait(false);
            RecordResponse(activity, response);
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

    private static Task<T> ObserveValueAsync<T>(Task<T> originalTask, Activity? activity)
        => activity is null ? originalTask : ObserveValueSlowAsync(originalTask, activity);

    private static async Task<T> ObserveValueSlowAsync<T>(Task<T> originalTask, Activity activity)
    {
        try
        {
            var result = await originalTask.ConfigureAwait(false);
            RecordSuccess(activity);
            return result;
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

    private static Activity? StartHttpClientActivity(HttpRequestMessage request)
    {
        var activity = StartHttpClientActivity(request.Method.Method, request.RequestUri, request.RequestUri?.ToString());
        if (activity is not null)
            SetConfiguredHeaders(activity, QylSemanticAttributes.HttpRequestHeaderPrefix, QylAutoInstrumentationOptions.Current.HttpClientCapturedRequestHeaders, request.Headers, request.Content?.Headers);

        return activity;
    }

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
        var options = QylAutoInstrumentationOptions.Current;
        if (!options.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Traces, QylAutoInstrumentationIds.HttpClient))
            return null;

        method = NormalizeMethod(method);
        var activity = QylActivitySource.Source.StartActivity($"HTTP {method}", ActivityKind.Client);
        if (activity is null)
            return null;

        activity.SetTag(QylSemanticAttributes.QylInstrumentationDomain, HttpClientDomain);
        activity.SetTag(QylSemanticAttributes.HttpRequestMethod, method);

        if (requestUri is not null)
        {
            if (requestUri.IsAbsoluteUri)
            {
                activity.SetTag(QylSemanticAttributes.ServerAddress, requestUri.Host);
                if (!requestUri.IsDefaultPort)
                    activity.SetTag(QylSemanticAttributes.ServerPort, requestUri.Port);
            }

            var urlFull = rawRequestUri ?? requestUri.ToString();
            activity.SetTag(
                QylSemanticAttributes.UrlFull,
                options.HttpClientUrlQueryRedactionDisabled ? urlFull : RedactQuery(urlFull));
        }

        return activity;
    }

    private static void RecordResponse(Activity activity, HttpResponseMessage response)
    {
        activity.SetTag(QylSemanticAttributes.HttpResponseStatusCode, (int)response.StatusCode);
        SetConfiguredHeaders(activity, QylSemanticAttributes.HttpResponseHeaderPrefix, QylAutoInstrumentationOptions.Current.HttpClientCapturedResponseHeaders, response.Headers, response.Content?.Headers);

        if ((int)response.StatusCode >= 400)
        {
            activity.SetTag(QylSemanticAttributes.ErrorType, ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture));
            activity.SetStatus(ActivityStatusCode.Error);
        }

        RecordDuration(activity);
    }

    private static void RecordSuccess(Activity activity)
    {
        RecordDuration(activity);
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
        activity?.SetTag(QylSemanticAttributes.ErrorType, exception.GetType().Name);
        activity?.SetStatus(ActivityStatusCode.Error);
        RecordDuration(activity);
    }

    private static void RecordDuration(Activity? activity)
    {
        if (activity is null ||
            !QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Metrics, QylAutoInstrumentationIds.HttpClient))
        {
            return;
        }

        QylHttpClientMetrics.RecordRequestDuration(activity.StartTimeUtc);
    }

    private static string NormalizeMethod(string? method)
        => method switch
        {
            "CONNECT" or "DELETE" or "GET" or "HEAD" or "OPTIONS" or "PATCH" or "POST" or "PUT" or "TRACE" => method,
            _ => UnknownHttpMethod,
        };

    private static string RedactQuery(string url)
    {
        var queryStart = url.IndexOf('?', StringComparison.Ordinal);
        if (queryStart < 0)
            return url;

        var fragmentStart = url.IndexOf('#', queryStart);
        return fragmentStart < 0
            ? url[..queryStart] + "?Redacted"
            : url[..queryStart] + "?Redacted" + url[fragmentStart..];
    }

    private static void SetConfiguredHeaders(Activity activity, string prefix, IReadOnlyList<string> configuredHeaders, params System.Net.Http.Headers.HttpHeaders?[] headerSources)
    {
        if (configuredHeaders.Count is 0)
            return;

        foreach (var headerName in configuredHeaders)
        {
            foreach (var source in headerSources)
            {
                if (source is null || !source.TryGetValues(headerName, out var values))
                    continue;

                activity.SetTag(prefix + NormalizeHeaderName(headerName), values.ToArray());
                break;
            }
        }
    }

    private static string NormalizeHeaderName(string headerName)
        => headerName.Replace('_', '-').ToLower(CultureInfo.InvariantCulture);
}
