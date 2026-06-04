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

    public static HttpResponseMessage Send(HttpClient client, HttpRequestMessage request)
    {
        ThrowIfInvalidCallTarget(client, request);
        var observation = StartHttpClientObservation(request);
        if (!observation.IsEnabled)
            return client.Send(request);

        try
        {
            var response = client.Send(request);
            RecordResponse(observation, response);
            return response;
        }
        catch (Exception exception)
        {
            RecordException(observation, exception);
            throw;
        }
        finally
        {
            observation.Dispose();
        }
    }

    public static HttpResponseMessage Send(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ThrowIfInvalidCallTarget(client, request);
        var observation = StartHttpClientObservation(request);
        if (!observation.IsEnabled)
            return client.Send(request, cancellationToken);

        try
        {
            var response = client.Send(request, cancellationToken);
            RecordResponse(observation, response);
            return response;
        }
        catch (Exception exception)
        {
            RecordException(observation, exception);
            throw;
        }
        finally
        {
            observation.Dispose();
        }
    }

    public static HttpResponseMessage Send(HttpClient client, HttpRequestMessage request, HttpCompletionOption completionOption)
    {
        ThrowIfInvalidCallTarget(client, request);
        var observation = StartHttpClientObservation(request);
        if (!observation.IsEnabled)
            return client.Send(request, completionOption);

        try
        {
            var response = client.Send(request, completionOption);
            RecordResponse(observation, response);
            return response;
        }
        catch (Exception exception)
        {
            RecordException(observation, exception);
            throw;
        }
        finally
        {
            observation.Dispose();
        }
    }

    public static HttpResponseMessage Send(HttpClient client, HttpRequestMessage request, HttpCompletionOption completionOption, CancellationToken cancellationToken)
    {
        ThrowIfInvalidCallTarget(client, request);
        var observation = StartHttpClientObservation(request);
        if (!observation.IsEnabled)
            return client.Send(request, completionOption, cancellationToken);

        try
        {
            var response = client.Send(request, completionOption, cancellationToken);
            RecordResponse(observation, response);
            return response;
        }
        catch (Exception exception)
        {
            RecordException(observation, exception);
            throw;
        }
        finally
        {
            observation.Dispose();
        }
    }

    public static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpRequestMessage request)
    {
        ThrowIfInvalidCallTarget(client, request);
        var observation = StartHttpClientObservation(request);
        try { return ObserveResponseAsync(client.SendAsync(request), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    public static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ThrowIfInvalidCallTarget(client, request);
        var observation = StartHttpClientObservation(request);
        try { return ObserveResponseAsync(client.SendAsync(request, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    public static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpRequestMessage request, HttpCompletionOption completionOption)
    {
        ThrowIfInvalidCallTarget(client, request);
        var observation = StartHttpClientObservation(request);
        try { return ObserveResponseAsync(client.SendAsync(request, completionOption), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    public static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpRequestMessage request, HttpCompletionOption completionOption, CancellationToken cancellationToken)
    {
        ThrowIfInvalidCallTarget(client, request);
        var observation = StartHttpClientObservation(request);
        try { return ObserveResponseAsync(client.SendAsync(request, completionOption, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    public static Task<HttpResponseMessage> GetAsync(HttpClient client, string? requestUri)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(QylSemanticAttributes.HttpRequestMethodGet, requestUri);
        try { return ObserveResponseAsync(client.GetAsync(requestUri), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    public static Task<HttpResponseMessage> GetAsync(HttpClient client, Uri? requestUri)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(QylSemanticAttributes.HttpRequestMethodGet, requestUri);
        try { return ObserveResponseAsync(client.GetAsync(requestUri), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    public static Task<HttpResponseMessage> GetAsync(HttpClient client, string? requestUri, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(QylSemanticAttributes.HttpRequestMethodGet, requestUri);
        try { return ObserveResponseAsync(client.GetAsync(requestUri, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    public static Task<HttpResponseMessage> GetAsync(HttpClient client, Uri? requestUri, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(QylSemanticAttributes.HttpRequestMethodGet, requestUri);
        try { return ObserveResponseAsync(client.GetAsync(requestUri, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    public static Task<HttpResponseMessage> GetAsync(HttpClient client, string? requestUri, HttpCompletionOption completionOption)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(QylSemanticAttributes.HttpRequestMethodGet, requestUri);
        try { return ObserveResponseAsync(client.GetAsync(requestUri, completionOption), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    public static Task<HttpResponseMessage> GetAsync(HttpClient client, Uri? requestUri, HttpCompletionOption completionOption)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(QylSemanticAttributes.HttpRequestMethodGet, requestUri);
        try { return ObserveResponseAsync(client.GetAsync(requestUri, completionOption), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    public static Task<HttpResponseMessage> GetAsync(HttpClient client, string? requestUri, HttpCompletionOption completionOption, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(QylSemanticAttributes.HttpRequestMethodGet, requestUri);
        try { return ObserveResponseAsync(client.GetAsync(requestUri, completionOption, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    public static Task<HttpResponseMessage> GetAsync(HttpClient client, Uri? requestUri, HttpCompletionOption completionOption, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(QylSemanticAttributes.HttpRequestMethodGet, requestUri);
        try { return ObserveResponseAsync(client.GetAsync(requestUri, completionOption, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    public static Task<HttpResponseMessage> PostAsync(HttpClient client, string? requestUri, HttpContent? content)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(QylSemanticAttributes.HttpRequestMethodPost, requestUri);
        try { return ObserveResponseAsync(client.PostAsync(requestUri, content), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    public static Task<HttpResponseMessage> PostAsync(HttpClient client, Uri? requestUri, HttpContent? content)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(QylSemanticAttributes.HttpRequestMethodPost, requestUri);
        try { return ObserveResponseAsync(client.PostAsync(requestUri, content), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    public static Task<HttpResponseMessage> PostAsync(HttpClient client, string? requestUri, HttpContent? content, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(QylSemanticAttributes.HttpRequestMethodPost, requestUri);
        try { return ObserveResponseAsync(client.PostAsync(requestUri, content, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    public static Task<HttpResponseMessage> PostAsync(HttpClient client, Uri? requestUri, HttpContent? content, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(QylSemanticAttributes.HttpRequestMethodPost, requestUri);
        try { return ObserveResponseAsync(client.PostAsync(requestUri, content, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    public static Task<HttpResponseMessage> PutAsync(HttpClient client, string? requestUri, HttpContent? content)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(QylSemanticAttributes.HttpRequestMethodPut, requestUri);
        try { return ObserveResponseAsync(client.PutAsync(requestUri, content), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    public static Task<HttpResponseMessage> PutAsync(HttpClient client, Uri? requestUri, HttpContent? content)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(QylSemanticAttributes.HttpRequestMethodPut, requestUri);
        try { return ObserveResponseAsync(client.PutAsync(requestUri, content), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    public static Task<HttpResponseMessage> PutAsync(HttpClient client, string? requestUri, HttpContent? content, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(QylSemanticAttributes.HttpRequestMethodPut, requestUri);
        try { return ObserveResponseAsync(client.PutAsync(requestUri, content, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    public static Task<HttpResponseMessage> PutAsync(HttpClient client, Uri? requestUri, HttpContent? content, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(QylSemanticAttributes.HttpRequestMethodPut, requestUri);
        try { return ObserveResponseAsync(client.PutAsync(requestUri, content, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    public static Task<HttpResponseMessage> PatchAsync(HttpClient client, string? requestUri, HttpContent? content)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(QylSemanticAttributes.HttpRequestMethodPatch, requestUri);
        try { return ObserveResponseAsync(client.PatchAsync(requestUri, content), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    public static Task<HttpResponseMessage> PatchAsync(HttpClient client, Uri? requestUri, HttpContent? content)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(QylSemanticAttributes.HttpRequestMethodPatch, requestUri);
        try { return ObserveResponseAsync(client.PatchAsync(requestUri, content), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    public static Task<HttpResponseMessage> PatchAsync(HttpClient client, string? requestUri, HttpContent? content, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(QylSemanticAttributes.HttpRequestMethodPatch, requestUri);
        try { return ObserveResponseAsync(client.PatchAsync(requestUri, content, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    public static Task<HttpResponseMessage> PatchAsync(HttpClient client, Uri? requestUri, HttpContent? content, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(QylSemanticAttributes.HttpRequestMethodPatch, requestUri);
        try { return ObserveResponseAsync(client.PatchAsync(requestUri, content, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    public static Task<HttpResponseMessage> DeleteAsync(HttpClient client, string? requestUri)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(QylSemanticAttributes.HttpRequestMethodDelete, requestUri);
        try { return ObserveResponseAsync(client.DeleteAsync(requestUri), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    public static Task<HttpResponseMessage> DeleteAsync(HttpClient client, Uri? requestUri)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(QylSemanticAttributes.HttpRequestMethodDelete, requestUri);
        try { return ObserveResponseAsync(client.DeleteAsync(requestUri), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    public static Task<HttpResponseMessage> DeleteAsync(HttpClient client, string? requestUri, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(QylSemanticAttributes.HttpRequestMethodDelete, requestUri);
        try { return ObserveResponseAsync(client.DeleteAsync(requestUri, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    public static Task<HttpResponseMessage> DeleteAsync(HttpClient client, Uri? requestUri, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(QylSemanticAttributes.HttpRequestMethodDelete, requestUri);
        try { return ObserveResponseAsync(client.DeleteAsync(requestUri, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    public static Task<string> GetStringAsync(HttpClient client, string? requestUri)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(QylSemanticAttributes.HttpRequestMethodGet, requestUri);
        try { return ObserveValueAsync(client.GetStringAsync(requestUri), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    public static Task<string> GetStringAsync(HttpClient client, Uri? requestUri)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(QylSemanticAttributes.HttpRequestMethodGet, requestUri);
        try { return ObserveValueAsync(client.GetStringAsync(requestUri), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    public static Task<string> GetStringAsync(HttpClient client, string? requestUri, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(QylSemanticAttributes.HttpRequestMethodGet, requestUri);
        try { return ObserveValueAsync(client.GetStringAsync(requestUri, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    public static Task<string> GetStringAsync(HttpClient client, Uri? requestUri, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(QylSemanticAttributes.HttpRequestMethodGet, requestUri);
        try { return ObserveValueAsync(client.GetStringAsync(requestUri, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    public static Task<byte[]> GetByteArrayAsync(HttpClient client, string? requestUri)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(QylSemanticAttributes.HttpRequestMethodGet, requestUri);
        try { return ObserveValueAsync(client.GetByteArrayAsync(requestUri), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    public static Task<byte[]> GetByteArrayAsync(HttpClient client, Uri? requestUri)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(QylSemanticAttributes.HttpRequestMethodGet, requestUri);
        try { return ObserveValueAsync(client.GetByteArrayAsync(requestUri), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    public static Task<byte[]> GetByteArrayAsync(HttpClient client, string? requestUri, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(QylSemanticAttributes.HttpRequestMethodGet, requestUri);
        try { return ObserveValueAsync(client.GetByteArrayAsync(requestUri, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    public static Task<byte[]> GetByteArrayAsync(HttpClient client, Uri? requestUri, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(QylSemanticAttributes.HttpRequestMethodGet, requestUri);
        try { return ObserveValueAsync(client.GetByteArrayAsync(requestUri, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    public static Task<Stream> GetStreamAsync(HttpClient client, string? requestUri)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(QylSemanticAttributes.HttpRequestMethodGet, requestUri);
        try { return ObserveValueAsync(client.GetStreamAsync(requestUri), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    public static Task<Stream> GetStreamAsync(HttpClient client, Uri? requestUri)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(QylSemanticAttributes.HttpRequestMethodGet, requestUri);
        try { return ObserveValueAsync(client.GetStreamAsync(requestUri), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    public static Task<Stream> GetStreamAsync(HttpClient client, string? requestUri, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(QylSemanticAttributes.HttpRequestMethodGet, requestUri);
        try { return ObserveValueAsync(client.GetStreamAsync(requestUri, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    public static Task<Stream> GetStreamAsync(HttpClient client, Uri? requestUri, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(QylSemanticAttributes.HttpRequestMethodGet, requestUri);
        try { return ObserveValueAsync(client.GetStreamAsync(requestUri, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    private static Task<HttpResponseMessage> ObserveResponseAsync(Task<HttpResponseMessage> originalTask, HttpClientObservation observation)
        => !observation.IsEnabled ? originalTask : ObserveResponseSlowAsync(originalTask, observation);

    private static async Task<HttpResponseMessage> ObserveResponseSlowAsync(Task<HttpResponseMessage> originalTask, HttpClientObservation observation)
    {
        try
        {
            var response = await originalTask.ConfigureAwait(false);
            RecordResponse(observation, response);
            return response;
        }
        catch (Exception exception)
        {
            RecordException(observation, exception);
            throw;
        }
        finally
        {
            observation.Dispose();
        }
    }

    private static Task<T> ObserveValueAsync<T>(Task<T> originalTask, HttpClientObservation observation)
        => !observation.IsEnabled ? originalTask : ObserveValueSlowAsync(originalTask, observation);

    private static async Task<T> ObserveValueSlowAsync<T>(Task<T> originalTask, HttpClientObservation observation)
    {
        try
        {
            var result = await originalTask.ConfigureAwait(false);
            RecordSuccess(observation);
            return result;
        }
        catch (Exception exception)
        {
            RecordException(observation, exception);
            throw;
        }
        finally
        {
            observation.Dispose();
        }
    }

    private static HttpClientObservation StartHttpClientObservation(HttpRequestMessage request)
    {
        var observation = StartHttpClientObservation(request.Method.Method, request.RequestUri, null);
        if (observation.Activity is not null)
            SetConfiguredHeaders(observation.Activity, QylSemanticAttributes.HttpRequestHeaderPrefix, QylAutoInstrumentationOptions.Current.HttpClientCapturedRequestHeaders, request.Headers, request.Content?.Headers);

        return observation;
    }

    private static HttpClientObservation StartHttpClientObservation(string method, string? requestUri)
    {
        Uri? uri = null;
        if (!string.IsNullOrWhiteSpace(requestUri))
            Uri.TryCreate(requestUri, UriKind.RelativeOrAbsolute, out uri);

        return StartHttpClientObservation(method, uri, requestUri);
    }

    private static HttpClientObservation StartHttpClientObservation(string method, Uri? requestUri)
        => StartHttpClientObservation(method, requestUri, null);

    private static HttpClientObservation StartHttpClientObservation(string method, Uri? requestUri, string? rawRequestUri)
    {
        var options = QylAutoInstrumentationOptions.Current;
        var traceEnabled = options.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Traces, QylAutoInstrumentationIds.HttpClient);
        var metricsEnabled = options.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Metrics, QylAutoInstrumentationIds.HttpClient);
        if (!traceEnabled && !metricsEnabled)
            return default;

        method = QylHttpMethod.Normalize(method);
        var startTimeUtc = TimeProvider.System.GetUtcNow().UtcDateTime;
        Activity? activity = null;

        if (traceEnabled)
        {
            activity = QylActivitySource.Source.StartActivity($"HTTP {method}", ActivityKind.Client);
            if (activity is not null)
            {
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

                    if (options.CaptureSensitiveValues || options.HttpClientUrlQueryRedactionDisabled)
                    {
                        var urlFull = rawRequestUri ?? requestUri.ToString();
                        activity.SetTag(
                            QylSemanticAttributes.UrlFull,
                            options.HttpClientUrlQueryRedactionDisabled ? urlFull : RedactQuery(urlFull));
                    }
                }
            }
        }

        return new HttpClientObservation(activity, startTimeUtc, method, metricsEnabled);
    }

    private static void RecordResponse(HttpClientObservation observation, HttpResponseMessage response)
    {
        var statusCode = (int)response.StatusCode;
        var activity = observation.Activity;
        if (activity is not null)
        {
            activity.SetTag(QylSemanticAttributes.HttpResponseStatusCode, statusCode);
            SetConfiguredHeaders(activity, QylSemanticAttributes.HttpResponseHeaderPrefix, QylAutoInstrumentationOptions.Current.HttpClientCapturedResponseHeaders, response.Headers, response.Content?.Headers);

            if (statusCode >= 400)
            {
                activity.SetTag(QylSemanticAttributes.ErrorType, statusCode.ToString(CultureInfo.InvariantCulture));
                activity.SetStatus(ActivityStatusCode.Error);
            }
        }

        RecordDuration(observation, statusCode);
    }

    private static void RecordSuccess(HttpClientObservation observation)
    {
        RecordDuration(observation, null);
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

    private static void RecordException(HttpClientObservation observation, Exception exception)
    {
        var activity = observation.Activity;
        activity?.SetTag(QylSemanticAttributes.ErrorType, exception.GetType().Name);
        activity?.SetStatus(ActivityStatusCode.Error);
        RecordDuration(observation, null);
    }

    private static void RecordDuration(HttpClientObservation observation, int? statusCode)
    {
        if (!observation.RecordMetrics)
            return;

        QylHttpClientMetrics.RecordRequestDuration(
            observation.StartTimeUtc,
            observation.Method,
            statusCode);
    }

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

    private readonly record struct HttpClientObservation(
        Activity? Activity,
        DateTime StartTimeUtc,
        string? Method,
        bool RecordMetrics)
    {
        public bool IsEnabled => Activity is not null || RecordMetrics;

        public void Dispose()
            => Activity?.Dispose();
    }
}
