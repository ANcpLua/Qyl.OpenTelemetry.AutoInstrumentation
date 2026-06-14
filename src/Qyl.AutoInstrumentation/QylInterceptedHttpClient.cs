using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using Qyl.AutoInstrumentation.Internal;

namespace Qyl.AutoInstrumentation;

/// <summary>
/// Runtime target for compile-time generated HttpClient interceptors. Each method calls the original
/// BCL API so qyl observes HttpClient behavior without reimplementing transport semantics.
/// </summary>
public static class QylInterceptedHttpClient
{

    /// <summary>Runs the Send runtime helper used by source-generated qyl interceptors.</summary>
    public static HttpResponseMessage Send(HttpClient client, HttpRequestMessage request)
        => SendCore(client, request, default, default, HttpClientSendOverload.Default);

    /// <summary>Runs the Send runtime helper used by source-generated qyl interceptors.</summary>
    public static HttpResponseMessage Send(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
        => SendCore(client, request, default, cancellationToken, HttpClientSendOverload.CancellationToken);

    /// <summary>Runs the Send runtime helper used by source-generated qyl interceptors.</summary>
    public static HttpResponseMessage Send(HttpClient client, HttpRequestMessage request, HttpCompletionOption completionOption)
        => SendCore(client, request, completionOption, default, HttpClientSendOverload.CompletionOption);

    /// <summary>Runs the Send runtime helper used by source-generated qyl interceptors.</summary>
    public static HttpResponseMessage Send(HttpClient client, HttpRequestMessage request, HttpCompletionOption completionOption, CancellationToken cancellationToken)
        => SendCore(client, request, completionOption, cancellationToken, HttpClientSendOverload.CompletionOptionCancellationToken);

    private static HttpResponseMessage SendCore(
        HttpClient client,
        HttpRequestMessage request,
        HttpCompletionOption completionOption,
        CancellationToken cancellationToken,
        HttpClientSendOverload overload)
    {
        ThrowIfInvalidCallTarget(client, request);
        var observation = StartHttpClientObservation(client, request);
        if (!observation.IsEnabled)
            return SendOriginal(client, request, completionOption, cancellationToken, overload);

        try
        {
            var response = SendOriginal(client, request, completionOption, cancellationToken, overload);
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

    private static HttpResponseMessage SendOriginal(
        HttpClient client,
        HttpRequestMessage request,
        HttpCompletionOption completionOption,
        CancellationToken cancellationToken,
        HttpClientSendOverload overload)
        => overload switch
        {
            HttpClientSendOverload.Default => client.Send(request),
            HttpClientSendOverload.CancellationToken => client.Send(request, cancellationToken),
            HttpClientSendOverload.CompletionOption => client.Send(request, completionOption),
            HttpClientSendOverload.CompletionOptionCancellationToken => client.Send(request, completionOption, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(overload), overload, null),
        };

    /// <summary>Runs the Send Async runtime helper used by source-generated qyl interceptors.</summary>
    public static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpRequestMessage request)
    {
        ThrowIfInvalidCallTarget(client, request);
        var observation = StartHttpClientObservation(client, request);
        try { return ObserveResponseAsync(client.SendAsync(request), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <summary>Runs the Send Async runtime helper used by source-generated qyl interceptors.</summary>
    public static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ThrowIfInvalidCallTarget(client, request);
        var observation = StartHttpClientObservation(client, request);
        try { return ObserveResponseAsync(client.SendAsync(request, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <summary>Runs the Send Async runtime helper used by source-generated qyl interceptors.</summary>
    public static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpRequestMessage request, HttpCompletionOption completionOption)
    {
        ThrowIfInvalidCallTarget(client, request);
        var observation = StartHttpClientObservation(client, request);
        try { return ObserveResponseAsync(client.SendAsync(request, completionOption), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <summary>Runs the Send Async runtime helper used by source-generated qyl interceptors.</summary>
    public static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpRequestMessage request, HttpCompletionOption completionOption, CancellationToken cancellationToken)
    {
        ThrowIfInvalidCallTarget(client, request);
        var observation = StartHttpClientObservation(client, request);
        try { return ObserveResponseAsync(client.SendAsync(request, completionOption, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <summary>Runs the Get Async runtime helper used by source-generated qyl interceptors.</summary>
    public static Task<HttpResponseMessage> GetAsync(HttpClient client, string? requestUri)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, QylSemanticAttributes.HttpRequestMethodGet, requestUri);
        try { return ObserveResponseAsync(client.GetAsync(requestUri), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <summary>Runs the Get Async runtime helper used by source-generated qyl interceptors.</summary>
    public static Task<HttpResponseMessage> GetAsync(HttpClient client, Uri? requestUri)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, QylSemanticAttributes.HttpRequestMethodGet, requestUri);
        try { return ObserveResponseAsync(client.GetAsync(requestUri), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <summary>Runs the Get Async runtime helper used by source-generated qyl interceptors.</summary>
    public static Task<HttpResponseMessage> GetAsync(HttpClient client, string? requestUri, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, QylSemanticAttributes.HttpRequestMethodGet, requestUri);
        try { return ObserveResponseAsync(client.GetAsync(requestUri, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <summary>Runs the Get Async runtime helper used by source-generated qyl interceptors.</summary>
    public static Task<HttpResponseMessage> GetAsync(HttpClient client, Uri? requestUri, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, QylSemanticAttributes.HttpRequestMethodGet, requestUri);
        try { return ObserveResponseAsync(client.GetAsync(requestUri, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <summary>Runs the Get Async runtime helper used by source-generated qyl interceptors.</summary>
    public static Task<HttpResponseMessage> GetAsync(HttpClient client, string? requestUri, HttpCompletionOption completionOption)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, QylSemanticAttributes.HttpRequestMethodGet, requestUri);
        try { return ObserveResponseAsync(client.GetAsync(requestUri, completionOption), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <summary>Runs the Get Async runtime helper used by source-generated qyl interceptors.</summary>
    public static Task<HttpResponseMessage> GetAsync(HttpClient client, Uri? requestUri, HttpCompletionOption completionOption)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, QylSemanticAttributes.HttpRequestMethodGet, requestUri);
        try { return ObserveResponseAsync(client.GetAsync(requestUri, completionOption), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <summary>Runs the Get Async runtime helper used by source-generated qyl interceptors.</summary>
    public static Task<HttpResponseMessage> GetAsync(HttpClient client, string? requestUri, HttpCompletionOption completionOption, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, QylSemanticAttributes.HttpRequestMethodGet, requestUri);
        try { return ObserveResponseAsync(client.GetAsync(requestUri, completionOption, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <summary>Runs the Get Async runtime helper used by source-generated qyl interceptors.</summary>
    public static Task<HttpResponseMessage> GetAsync(HttpClient client, Uri? requestUri, HttpCompletionOption completionOption, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, QylSemanticAttributes.HttpRequestMethodGet, requestUri);
        try { return ObserveResponseAsync(client.GetAsync(requestUri, completionOption, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <summary>Runs the Post Async runtime helper used by source-generated qyl interceptors.</summary>
    public static Task<HttpResponseMessage> PostAsync(HttpClient client, string? requestUri, HttpContent? content)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, QylSemanticAttributes.HttpRequestMethodPost, requestUri, content);
        try { return ObserveResponseAsync(client.PostAsync(requestUri, content), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <summary>Runs the Post Async runtime helper used by source-generated qyl interceptors.</summary>
    public static Task<HttpResponseMessage> PostAsync(HttpClient client, Uri? requestUri, HttpContent? content)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, QylSemanticAttributes.HttpRequestMethodPost, requestUri, content);
        try { return ObserveResponseAsync(client.PostAsync(requestUri, content), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <summary>Runs the Post Async runtime helper used by source-generated qyl interceptors.</summary>
    public static Task<HttpResponseMessage> PostAsync(HttpClient client, string? requestUri, HttpContent? content, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, QylSemanticAttributes.HttpRequestMethodPost, requestUri, content);
        try { return ObserveResponseAsync(client.PostAsync(requestUri, content, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <summary>Runs the Post Async runtime helper used by source-generated qyl interceptors.</summary>
    public static Task<HttpResponseMessage> PostAsync(HttpClient client, Uri? requestUri, HttpContent? content, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, QylSemanticAttributes.HttpRequestMethodPost, requestUri, content);
        try { return ObserveResponseAsync(client.PostAsync(requestUri, content, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <summary>Runs the Put Async runtime helper used by source-generated qyl interceptors.</summary>
    public static Task<HttpResponseMessage> PutAsync(HttpClient client, string? requestUri, HttpContent? content)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, QylSemanticAttributes.HttpRequestMethodPut, requestUri, content);
        try { return ObserveResponseAsync(client.PutAsync(requestUri, content), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <summary>Runs the Put Async runtime helper used by source-generated qyl interceptors.</summary>
    public static Task<HttpResponseMessage> PutAsync(HttpClient client, Uri? requestUri, HttpContent? content)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, QylSemanticAttributes.HttpRequestMethodPut, requestUri, content);
        try { return ObserveResponseAsync(client.PutAsync(requestUri, content), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <summary>Runs the Put Async runtime helper used by source-generated qyl interceptors.</summary>
    public static Task<HttpResponseMessage> PutAsync(HttpClient client, string? requestUri, HttpContent? content, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, QylSemanticAttributes.HttpRequestMethodPut, requestUri, content);
        try { return ObserveResponseAsync(client.PutAsync(requestUri, content, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <summary>Runs the Put Async runtime helper used by source-generated qyl interceptors.</summary>
    public static Task<HttpResponseMessage> PutAsync(HttpClient client, Uri? requestUri, HttpContent? content, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, QylSemanticAttributes.HttpRequestMethodPut, requestUri, content);
        try { return ObserveResponseAsync(client.PutAsync(requestUri, content, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <summary>Runs the Patch Async runtime helper used by source-generated qyl interceptors.</summary>
    public static Task<HttpResponseMessage> PatchAsync(HttpClient client, string? requestUri, HttpContent? content)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, QylSemanticAttributes.HttpRequestMethodPatch, requestUri, content);
        try { return ObserveResponseAsync(client.PatchAsync(requestUri, content), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <summary>Runs the Patch Async runtime helper used by source-generated qyl interceptors.</summary>
    public static Task<HttpResponseMessage> PatchAsync(HttpClient client, Uri? requestUri, HttpContent? content)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, QylSemanticAttributes.HttpRequestMethodPatch, requestUri, content);
        try { return ObserveResponseAsync(client.PatchAsync(requestUri, content), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <summary>Runs the Patch Async runtime helper used by source-generated qyl interceptors.</summary>
    public static Task<HttpResponseMessage> PatchAsync(HttpClient client, string? requestUri, HttpContent? content, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, QylSemanticAttributes.HttpRequestMethodPatch, requestUri, content);
        try { return ObserveResponseAsync(client.PatchAsync(requestUri, content, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <summary>Runs the Patch Async runtime helper used by source-generated qyl interceptors.</summary>
    public static Task<HttpResponseMessage> PatchAsync(HttpClient client, Uri? requestUri, HttpContent? content, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, QylSemanticAttributes.HttpRequestMethodPatch, requestUri, content);
        try { return ObserveResponseAsync(client.PatchAsync(requestUri, content, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <summary>Runs the Delete Async runtime helper used by source-generated qyl interceptors.</summary>
    public static Task<HttpResponseMessage> DeleteAsync(HttpClient client, string? requestUri)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, QylSemanticAttributes.HttpRequestMethodDelete, requestUri);
        try { return ObserveResponseAsync(client.DeleteAsync(requestUri), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <summary>Runs the Delete Async runtime helper used by source-generated qyl interceptors.</summary>
    public static Task<HttpResponseMessage> DeleteAsync(HttpClient client, Uri? requestUri)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, QylSemanticAttributes.HttpRequestMethodDelete, requestUri);
        try { return ObserveResponseAsync(client.DeleteAsync(requestUri), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <summary>Runs the Delete Async runtime helper used by source-generated qyl interceptors.</summary>
    public static Task<HttpResponseMessage> DeleteAsync(HttpClient client, string? requestUri, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, QylSemanticAttributes.HttpRequestMethodDelete, requestUri);
        try { return ObserveResponseAsync(client.DeleteAsync(requestUri, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <summary>Runs the Delete Async runtime helper used by source-generated qyl interceptors.</summary>
    public static Task<HttpResponseMessage> DeleteAsync(HttpClient client, Uri? requestUri, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, QylSemanticAttributes.HttpRequestMethodDelete, requestUri);
        try { return ObserveResponseAsync(client.DeleteAsync(requestUri, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <summary>Runs the Get String Async runtime helper used by source-generated qyl interceptors.</summary>
    public static Task<string> GetStringAsync(HttpClient client, string? requestUri)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, QylSemanticAttributes.HttpRequestMethodGet, requestUri);
        try { return ObserveValueAsync(client.GetStringAsync(requestUri), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <summary>Runs the Get String Async runtime helper used by source-generated qyl interceptors.</summary>
    public static Task<string> GetStringAsync(HttpClient client, Uri? requestUri)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, QylSemanticAttributes.HttpRequestMethodGet, requestUri);
        try { return ObserveValueAsync(client.GetStringAsync(requestUri), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <summary>Runs the Get String Async runtime helper used by source-generated qyl interceptors.</summary>
    public static Task<string> GetStringAsync(HttpClient client, string? requestUri, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, QylSemanticAttributes.HttpRequestMethodGet, requestUri);
        try { return ObserveValueAsync(client.GetStringAsync(requestUri, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <summary>Runs the Get String Async runtime helper used by source-generated qyl interceptors.</summary>
    public static Task<string> GetStringAsync(HttpClient client, Uri? requestUri, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, QylSemanticAttributes.HttpRequestMethodGet, requestUri);
        try { return ObserveValueAsync(client.GetStringAsync(requestUri, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <summary>Runs the Get Byte Array Async runtime helper used by source-generated qyl interceptors.</summary>
    public static Task<byte[]> GetByteArrayAsync(HttpClient client, string? requestUri)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, QylSemanticAttributes.HttpRequestMethodGet, requestUri);
        try { return ObserveValueAsync(client.GetByteArrayAsync(requestUri), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <summary>Runs the Get Byte Array Async runtime helper used by source-generated qyl interceptors.</summary>
    public static Task<byte[]> GetByteArrayAsync(HttpClient client, Uri? requestUri)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, QylSemanticAttributes.HttpRequestMethodGet, requestUri);
        try { return ObserveValueAsync(client.GetByteArrayAsync(requestUri), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <summary>Runs the Get Byte Array Async runtime helper used by source-generated qyl interceptors.</summary>
    public static Task<byte[]> GetByteArrayAsync(HttpClient client, string? requestUri, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, QylSemanticAttributes.HttpRequestMethodGet, requestUri);
        try { return ObserveValueAsync(client.GetByteArrayAsync(requestUri, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <summary>Runs the Get Byte Array Async runtime helper used by source-generated qyl interceptors.</summary>
    public static Task<byte[]> GetByteArrayAsync(HttpClient client, Uri? requestUri, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, QylSemanticAttributes.HttpRequestMethodGet, requestUri);
        try { return ObserveValueAsync(client.GetByteArrayAsync(requestUri, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <summary>Runs the Get Stream Async runtime helper used by source-generated qyl interceptors.</summary>
    public static Task<Stream> GetStreamAsync(HttpClient client, string? requestUri)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, QylSemanticAttributes.HttpRequestMethodGet, requestUri);
        try { return ObserveValueAsync(client.GetStreamAsync(requestUri), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <summary>Runs the Get Stream Async runtime helper used by source-generated qyl interceptors.</summary>
    public static Task<Stream> GetStreamAsync(HttpClient client, Uri? requestUri)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, QylSemanticAttributes.HttpRequestMethodGet, requestUri);
        try { return ObserveValueAsync(client.GetStreamAsync(requestUri), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <summary>Runs the Get Stream Async runtime helper used by source-generated qyl interceptors.</summary>
    public static Task<Stream> GetStreamAsync(HttpClient client, string? requestUri, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, QylSemanticAttributes.HttpRequestMethodGet, requestUri);
        try { return ObserveValueAsync(client.GetStreamAsync(requestUri, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <summary>Runs the Get Stream Async runtime helper used by source-generated qyl interceptors.</summary>
    public static Task<Stream> GetStreamAsync(HttpClient client, Uri? requestUri, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, QylSemanticAttributes.HttpRequestMethodGet, requestUri);
        try { return ObserveValueAsync(client.GetStreamAsync(requestUri, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    private static Task<HttpResponseMessage> ObserveResponseAsync(Task<HttpResponseMessage> originalTask, HttpClientObservation observation)
    {
        if (!observation.IsEnabled)
            return originalTask;

        if (!originalTask.IsCompletedSuccessfully)
            return ObserveResponseSlowAsync(originalTask, observation);

        RecordResponse(observation, originalTask.Result);
        observation.Dispose();
        return originalTask;
    }

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
    {
        if (!observation.IsEnabled)
            return originalTask;

        if (!originalTask.IsCompletedSuccessfully)
            return ObserveValueSlowAsync(originalTask, observation);

        RecordSuccess(observation);
        observation.Dispose();
        return originalTask;
    }

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

    private static HttpClientObservation StartHttpClientObservation(HttpClient client, HttpRequestMessage request)
    {
        if (!TryGetHttpClientObservationOptions(out var options, out var traceEnabled, out var metricsEnabled))
            return default;

        var observation = StartHttpClientObservation(
            client,
            options,
            traceEnabled,
            metricsEnabled,
            request.Method.Method,
            request.RequestUri,
            rawRequestUri: null);
        QylCaptureHelpers.SetHttpHeaders(
            observation.Activity,
            options.HttpClientCapturedRequestHeaderMap,
            request.Headers,
            client.DefaultRequestHeaders,
            request.Content?.Headers);

        return observation;
    }

    private static HttpClientObservation StartHttpClientObservation(
        HttpClient client,
        string method,
        string? requestUri,
        HttpContent? content = null)
    {
        if (!TryGetHttpClientObservationOptions(out var options, out var traceEnabled, out var metricsEnabled))
            return default;

        Uri? uri = null;
        if (traceEnabled && !string.IsNullOrWhiteSpace(requestUri))
            Uri.TryCreate(requestUri, UriKind.RelativeOrAbsolute, out uri);

        var observation = StartHttpClientObservation(client, options, traceEnabled, metricsEnabled, method, uri, requestUri);
        SetConvenienceRequestHeaders(observation.Activity, options, client, content);
        return observation;
    }

    private static HttpClientObservation StartHttpClientObservation(
        HttpClient client,
        string method,
        Uri? requestUri,
        HttpContent? content = null)
        => StartHttpClientObservation(client, method, requestUri, null, content);

    private static HttpClientObservation StartHttpClientObservation(
        HttpClient client,
        string method,
        Uri? requestUri,
        string? rawRequestUri,
        HttpContent? content = null)
    {
        if (!TryGetHttpClientObservationOptions(out var options, out var traceEnabled, out var metricsEnabled))
            return default;

        var observation = StartHttpClientObservation(client, options, traceEnabled, metricsEnabled, method, requestUri, rawRequestUri);
        SetConvenienceRequestHeaders(observation.Activity, options, client, content);
        return observation;
    }

    private static bool TryGetHttpClientObservationOptions(
        out QylAutoInstrumentationOptions options,
        out bool traceEnabled,
        out bool metricsEnabled)
    {
        options = QylAutoInstrumentationOptions.Current;
        traceEnabled = QylActivitySource.IsRecordingEnabled &&
                       options.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Traces, QylAutoInstrumentationIds.HttpClient);
        metricsEnabled = QylHttpClientMetrics.IsRecordingEnabledFor(options);
        return traceEnabled || metricsEnabled;
    }

    private static HttpClientObservation StartHttpClientObservation(
        HttpClient client,
        QylAutoInstrumentationOptions options,
        bool traceEnabled,
        bool metricsEnabled,
        string method,
        Uri? requestUri,
        string? rawRequestUri)
    {
        method = QylHttpMethod.Normalize(method);
        var startTimeUtc = QylDurationMetrics.GetHttpClientStartTimeUtc(metricsEnabled);
        Activity? activity = null;

        if (traceEnabled)
        {
            requestUri = ResolveRequestUri(client, requestUri);
            activity = QylHttpActivityPolicy.StartClientActivity(
                QylInstrumentationDomains.HttpClient,
                method,
                requestUri,
                rawRequestUri);
        }

        return new HttpClientObservation(activity, startTimeUtc, method, metricsEnabled);
    }

    private static Uri? ResolveRequestUri(HttpClient client, Uri? requestUri)
    {
        if (requestUri is null)
            return client.BaseAddress;

        if (requestUri.IsAbsoluteUri || client.BaseAddress is null)
            return requestUri;

        return new Uri(client.BaseAddress, requestUri);
    }

    private static void SetConvenienceRequestHeaders(
        Activity? activity,
        QylAutoInstrumentationOptions options,
        HttpClient client,
        HttpContent? content)
        => QylCaptureHelpers.SetHttpHeaders(
            activity,
            options.HttpClientCapturedRequestHeaderMap,
            client.DefaultRequestHeaders,
            content?.Headers);

    private static void RecordResponse(HttpClientObservation observation, HttpResponseMessage response)
    {
        var statusCode = (int)response.StatusCode;
        var activity = observation.Activity;
        if (activity is not null)
        {
            QylHttpActivityPolicy.SetResponseStatus(activity, statusCode, 400);
            QylCaptureHelpers.SetHttpHeaders(
                activity,
                QylAutoInstrumentationOptions.Current.HttpClientCapturedResponseHeaderMap,
                response.Headers,
                response.Content?.Headers);

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
        if (exception is HttpRequestException { StatusCode: { } statusCode })
        {
            RecordResponseStatusException(observation, (int)statusCode);
            return;
        }

        var activity = observation.Activity;
        QylActivityStatus.RecordException(activity, exception);
        RecordDuration(observation, null);
    }

    private static void RecordResponseStatusException(HttpClientObservation observation, int statusCode)
    {
        var activity = observation.Activity;
        if (activity is not null)
        {
            QylHttpActivityPolicy.SetResponseStatus(activity, statusCode, 400);
        }

        RecordDuration(observation, statusCode);
    }

    private static void RecordDuration(HttpClientObservation observation, int? statusCode)
    {
        if (!observation.RecordMetrics)
            return;

        QylDurationMetrics.RecordHttpClientDurationUnchecked(
            observation.StartTimeUtc,
            observation.Method,
            statusCode);
    }

    private readonly record struct HttpClientObservation(
        Activity? Activity,
        DateTime StartTimeUtc,
        string? Method,
        bool RecordMetrics) : IDisposable
    {
        /// <summary>Well-known Is Enabled value used by qyl auto-instrumentation.</summary>
        public bool IsEnabled => Activity is not null || RecordMetrics;

        /// <summary>Runs the Dispose runtime helper used by source-generated qyl interceptors.</summary>
        public void Dispose()
            => Activity?.Dispose();
    }

    private enum HttpClientSendOverload
    {
        Default,
        CancellationToken,
        CompletionOption,
        CompletionOptionCancellationToken,
    }
}
