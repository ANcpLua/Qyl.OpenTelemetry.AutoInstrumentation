using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using Qyl.Telemetry.AutoInstrumentation.Internal;
using Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Qyl;
using HttpAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Http.HttpAttributes;

namespace Qyl.Telemetry.AutoInstrumentation.GeneratedCode;

/// <summary>
/// Runtime target for compile-time generated HttpClient interceptors. Each method calls the original
/// BCL API so qyl observes HttpClient behavior without reimplementing transport semantics.
/// </summary>
/// <remarks>
/// Declared with the forwarding body rather than the trace template: the helper mirrors the BCL's
/// null-receiver semantics before any span starts, resolves the request URI against the client's
/// base address, enriches the span from the typed response (status, protocol version, headers),
/// maps <see cref="HttpRequestException.StatusCode"/> to the response status instead of an
/// exception, and completes synchronously-finished tasks without an await — none of which the
/// argument-bound trace template expresses.
/// </remarks>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
[QylIntegration(QylAutoInstrumentationIds.HttpClient, QylAttributes.InstrumentationDomainValues.HttpClient, MetricIds = [QylAutoInstrumentationIds.HttpClient])]
[QylIntercept(
    "System.Net.Http.HttpClient",
    "Send", "SendAsync", "GetAsync", "DeleteAsync", "PostAsync", "PutAsync", "PatchAsync", "GetStringAsync", "GetByteArrayAsync", "GetStreamAsync",
    Shape = QylShapes.HttpClient,
    Body = QylInterceptorBody.Forward)]
public static class QylInterceptedHttpClient
{
    // Registered on first use — which is inside an intercepted HttpClient call, before that call reaches
    // the BCL that raises the HttpClient DiagnosticListener event — so the listener lane defers (no double).
    static QylInterceptedHttpClient()
        => QylSignalOwnership.Register(QylAutoInstrumentationIds.HttpClient, QylSignalOwnership.Interceptor);

    /// <inheritdoc cref="HttpClient.Send(HttpRequestMessage)"/>
    public static HttpResponseMessage Send(HttpClient client, HttpRequestMessage request)
        => SendCore(client, request, default, default, HttpClientSendOverload.Default);

    /// <inheritdoc cref="HttpClient.Send(HttpRequestMessage, CancellationToken)"/>
    public static HttpResponseMessage Send(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
        => SendCore(client, request, default, cancellationToken, HttpClientSendOverload.CancellationToken);

    /// <inheritdoc cref="HttpClient.Send(HttpRequestMessage, HttpCompletionOption)"/>
    public static HttpResponseMessage Send(HttpClient client, HttpRequestMessage request, HttpCompletionOption completionOption)
        => SendCore(client, request, completionOption, default, HttpClientSendOverload.CompletionOption);

    /// <inheritdoc cref="HttpClient.Send(HttpRequestMessage, HttpCompletionOption, CancellationToken)"/>
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

    /// <inheritdoc cref="HttpClient.SendAsync(HttpRequestMessage)"/>
    public static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpRequestMessage request)
    {
        ThrowIfInvalidCallTarget(client, request);
        var observation = StartHttpClientObservation(client, request);
        try { return ObserveResponseAsync(client.SendAsync(request), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <inheritdoc cref="HttpClient.SendAsync(HttpRequestMessage, CancellationToken)"/>
    public static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ThrowIfInvalidCallTarget(client, request);
        var observation = StartHttpClientObservation(client, request);
        try { return ObserveResponseAsync(client.SendAsync(request, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <inheritdoc cref="HttpClient.SendAsync(HttpRequestMessage, HttpCompletionOption)"/>
    public static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpRequestMessage request, HttpCompletionOption completionOption)
    {
        ThrowIfInvalidCallTarget(client, request);
        var observation = StartHttpClientObservation(client, request);
        try { return ObserveResponseAsync(client.SendAsync(request, completionOption), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <inheritdoc cref="HttpClient.SendAsync(HttpRequestMessage, HttpCompletionOption, CancellationToken)"/>
    public static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpRequestMessage request, HttpCompletionOption completionOption, CancellationToken cancellationToken)
    {
        ThrowIfInvalidCallTarget(client, request);
        var observation = StartHttpClientObservation(client, request);
        try { return ObserveResponseAsync(client.SendAsync(request, completionOption, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <inheritdoc cref="HttpClient.GetAsync(string)"/>
    public static Task<HttpResponseMessage> GetAsync(HttpClient client, string? requestUri)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, HttpAttributes.RequestMethodValues.Get, requestUri);
        try { return ObserveResponseAsync(client.GetAsync(requestUri), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <inheritdoc cref="HttpClient.GetAsync(Uri)"/>
    public static Task<HttpResponseMessage> GetAsync(HttpClient client, Uri? requestUri)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, HttpAttributes.RequestMethodValues.Get, requestUri);
        try { return ObserveResponseAsync(client.GetAsync(requestUri), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <inheritdoc cref="HttpClient.GetAsync(string, CancellationToken)"/>
    public static Task<HttpResponseMessage> GetAsync(HttpClient client, string? requestUri, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, HttpAttributes.RequestMethodValues.Get, requestUri);
        try { return ObserveResponseAsync(client.GetAsync(requestUri, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <inheritdoc cref="HttpClient.GetAsync(Uri, CancellationToken)"/>
    public static Task<HttpResponseMessage> GetAsync(HttpClient client, Uri? requestUri, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, HttpAttributes.RequestMethodValues.Get, requestUri);
        try { return ObserveResponseAsync(client.GetAsync(requestUri, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <inheritdoc cref="HttpClient.GetAsync(string, HttpCompletionOption)"/>
    public static Task<HttpResponseMessage> GetAsync(HttpClient client, string? requestUri, HttpCompletionOption completionOption)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, HttpAttributes.RequestMethodValues.Get, requestUri);
        try { return ObserveResponseAsync(client.GetAsync(requestUri, completionOption), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <inheritdoc cref="HttpClient.GetAsync(Uri, HttpCompletionOption)"/>
    public static Task<HttpResponseMessage> GetAsync(HttpClient client, Uri? requestUri, HttpCompletionOption completionOption)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, HttpAttributes.RequestMethodValues.Get, requestUri);
        try { return ObserveResponseAsync(client.GetAsync(requestUri, completionOption), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <inheritdoc cref="HttpClient.GetAsync(string, HttpCompletionOption, CancellationToken)"/>
    public static Task<HttpResponseMessage> GetAsync(HttpClient client, string? requestUri, HttpCompletionOption completionOption, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, HttpAttributes.RequestMethodValues.Get, requestUri);
        try { return ObserveResponseAsync(client.GetAsync(requestUri, completionOption, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <inheritdoc cref="HttpClient.GetAsync(Uri, HttpCompletionOption, CancellationToken)"/>
    public static Task<HttpResponseMessage> GetAsync(HttpClient client, Uri? requestUri, HttpCompletionOption completionOption, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, HttpAttributes.RequestMethodValues.Get, requestUri);
        try { return ObserveResponseAsync(client.GetAsync(requestUri, completionOption, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <inheritdoc cref="HttpClient.PostAsync(string, HttpContent)"/>
    public static Task<HttpResponseMessage> PostAsync(HttpClient client, string? requestUri, HttpContent? content)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, HttpAttributes.RequestMethodValues.Post, requestUri, content);
        try { return ObserveResponseAsync(client.PostAsync(requestUri, content), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <inheritdoc cref="HttpClient.PostAsync(Uri, HttpContent)"/>
    public static Task<HttpResponseMessage> PostAsync(HttpClient client, Uri? requestUri, HttpContent? content)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, HttpAttributes.RequestMethodValues.Post, requestUri, content);
        try { return ObserveResponseAsync(client.PostAsync(requestUri, content), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <inheritdoc cref="HttpClient.PostAsync(string, HttpContent, CancellationToken)"/>
    public static Task<HttpResponseMessage> PostAsync(HttpClient client, string? requestUri, HttpContent? content, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, HttpAttributes.RequestMethodValues.Post, requestUri, content);
        try { return ObserveResponseAsync(client.PostAsync(requestUri, content, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <inheritdoc cref="HttpClient.PostAsync(Uri, HttpContent, CancellationToken)"/>
    public static Task<HttpResponseMessage> PostAsync(HttpClient client, Uri? requestUri, HttpContent? content, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, HttpAttributes.RequestMethodValues.Post, requestUri, content);
        try { return ObserveResponseAsync(client.PostAsync(requestUri, content, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <inheritdoc cref="HttpClient.PutAsync(string, HttpContent)"/>
    public static Task<HttpResponseMessage> PutAsync(HttpClient client, string? requestUri, HttpContent? content)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, HttpAttributes.RequestMethodValues.Put, requestUri, content);
        try { return ObserveResponseAsync(client.PutAsync(requestUri, content), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <inheritdoc cref="HttpClient.PutAsync(Uri, HttpContent)"/>
    public static Task<HttpResponseMessage> PutAsync(HttpClient client, Uri? requestUri, HttpContent? content)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, HttpAttributes.RequestMethodValues.Put, requestUri, content);
        try { return ObserveResponseAsync(client.PutAsync(requestUri, content), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <inheritdoc cref="HttpClient.PutAsync(string, HttpContent, CancellationToken)"/>
    public static Task<HttpResponseMessage> PutAsync(HttpClient client, string? requestUri, HttpContent? content, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, HttpAttributes.RequestMethodValues.Put, requestUri, content);
        try { return ObserveResponseAsync(client.PutAsync(requestUri, content, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <inheritdoc cref="HttpClient.PutAsync(Uri, HttpContent, CancellationToken)"/>
    public static Task<HttpResponseMessage> PutAsync(HttpClient client, Uri? requestUri, HttpContent? content, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, HttpAttributes.RequestMethodValues.Put, requestUri, content);
        try { return ObserveResponseAsync(client.PutAsync(requestUri, content, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <inheritdoc cref="HttpClient.PatchAsync(string, HttpContent)"/>
    public static Task<HttpResponseMessage> PatchAsync(HttpClient client, string? requestUri, HttpContent? content)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, HttpAttributes.RequestMethodValues.Patch, requestUri, content);
        try { return ObserveResponseAsync(client.PatchAsync(requestUri, content), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <inheritdoc cref="HttpClient.PatchAsync(Uri, HttpContent)"/>
    public static Task<HttpResponseMessage> PatchAsync(HttpClient client, Uri? requestUri, HttpContent? content)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, HttpAttributes.RequestMethodValues.Patch, requestUri, content);
        try { return ObserveResponseAsync(client.PatchAsync(requestUri, content), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <inheritdoc cref="HttpClient.PatchAsync(string, HttpContent, CancellationToken)"/>
    public static Task<HttpResponseMessage> PatchAsync(HttpClient client, string? requestUri, HttpContent? content, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, HttpAttributes.RequestMethodValues.Patch, requestUri, content);
        try { return ObserveResponseAsync(client.PatchAsync(requestUri, content, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <inheritdoc cref="HttpClient.PatchAsync(Uri, HttpContent, CancellationToken)"/>
    public static Task<HttpResponseMessage> PatchAsync(HttpClient client, Uri? requestUri, HttpContent? content, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, HttpAttributes.RequestMethodValues.Patch, requestUri, content);
        try { return ObserveResponseAsync(client.PatchAsync(requestUri, content, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <inheritdoc cref="HttpClient.DeleteAsync(string)"/>
    public static Task<HttpResponseMessage> DeleteAsync(HttpClient client, string? requestUri)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, HttpAttributes.RequestMethodValues.Delete, requestUri);
        try { return ObserveResponseAsync(client.DeleteAsync(requestUri), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <inheritdoc cref="HttpClient.DeleteAsync(Uri)"/>
    public static Task<HttpResponseMessage> DeleteAsync(HttpClient client, Uri? requestUri)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, HttpAttributes.RequestMethodValues.Delete, requestUri);
        try { return ObserveResponseAsync(client.DeleteAsync(requestUri), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <inheritdoc cref="HttpClient.DeleteAsync(string, CancellationToken)"/>
    public static Task<HttpResponseMessage> DeleteAsync(HttpClient client, string? requestUri, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, HttpAttributes.RequestMethodValues.Delete, requestUri);
        try { return ObserveResponseAsync(client.DeleteAsync(requestUri, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <inheritdoc cref="HttpClient.DeleteAsync(Uri, CancellationToken)"/>
    public static Task<HttpResponseMessage> DeleteAsync(HttpClient client, Uri? requestUri, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, HttpAttributes.RequestMethodValues.Delete, requestUri);
        try { return ObserveResponseAsync(client.DeleteAsync(requestUri, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <inheritdoc cref="HttpClient.GetStringAsync(string)"/>
    public static Task<string> GetStringAsync(HttpClient client, string? requestUri)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, HttpAttributes.RequestMethodValues.Get, requestUri);
        try { return ObserveValueAsync(client.GetStringAsync(requestUri), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <inheritdoc cref="HttpClient.GetStringAsync(Uri)"/>
    public static Task<string> GetStringAsync(HttpClient client, Uri? requestUri)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, HttpAttributes.RequestMethodValues.Get, requestUri);
        try { return ObserveValueAsync(client.GetStringAsync(requestUri), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <inheritdoc cref="HttpClient.GetStringAsync(string, CancellationToken)"/>
    public static Task<string> GetStringAsync(HttpClient client, string? requestUri, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, HttpAttributes.RequestMethodValues.Get, requestUri);
        try { return ObserveValueAsync(client.GetStringAsync(requestUri, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <inheritdoc cref="HttpClient.GetStringAsync(Uri, CancellationToken)"/>
    public static Task<string> GetStringAsync(HttpClient client, Uri? requestUri, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, HttpAttributes.RequestMethodValues.Get, requestUri);
        try { return ObserveValueAsync(client.GetStringAsync(requestUri, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <inheritdoc cref="HttpClient.GetByteArrayAsync(string)"/>
    public static Task<byte[]> GetByteArrayAsync(HttpClient client, string? requestUri)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, HttpAttributes.RequestMethodValues.Get, requestUri);
        try { return ObserveValueAsync(client.GetByteArrayAsync(requestUri), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <inheritdoc cref="HttpClient.GetByteArrayAsync(Uri)"/>
    public static Task<byte[]> GetByteArrayAsync(HttpClient client, Uri? requestUri)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, HttpAttributes.RequestMethodValues.Get, requestUri);
        try { return ObserveValueAsync(client.GetByteArrayAsync(requestUri), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <inheritdoc cref="HttpClient.GetByteArrayAsync(string, CancellationToken)"/>
    public static Task<byte[]> GetByteArrayAsync(HttpClient client, string? requestUri, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, HttpAttributes.RequestMethodValues.Get, requestUri);
        try { return ObserveValueAsync(client.GetByteArrayAsync(requestUri, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <inheritdoc cref="HttpClient.GetByteArrayAsync(Uri, CancellationToken)"/>
    public static Task<byte[]> GetByteArrayAsync(HttpClient client, Uri? requestUri, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, HttpAttributes.RequestMethodValues.Get, requestUri);
        try { return ObserveValueAsync(client.GetByteArrayAsync(requestUri, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <inheritdoc cref="HttpClient.GetStreamAsync(string)"/>
    public static Task<Stream> GetStreamAsync(HttpClient client, string? requestUri)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, HttpAttributes.RequestMethodValues.Get, requestUri);
        try { return ObserveValueAsync(client.GetStreamAsync(requestUri), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <inheritdoc cref="HttpClient.GetStreamAsync(Uri)"/>
    public static Task<Stream> GetStreamAsync(HttpClient client, Uri? requestUri)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, HttpAttributes.RequestMethodValues.Get, requestUri);
        try { return ObserveValueAsync(client.GetStreamAsync(requestUri), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <inheritdoc cref="HttpClient.GetStreamAsync(string, CancellationToken)"/>
    public static Task<Stream> GetStreamAsync(HttpClient client, string? requestUri, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, HttpAttributes.RequestMethodValues.Get, requestUri);
        try { return ObserveValueAsync(client.GetStreamAsync(requestUri, cancellationToken), observation); }
        catch (Exception exception) { RecordException(observation, exception); observation.Dispose(); throw; }
    }

    /// <inheritdoc cref="HttpClient.GetStreamAsync(Uri, CancellationToken)"/>
    public static Task<Stream> GetStreamAsync(HttpClient client, Uri? requestUri, CancellationToken cancellationToken)
    {
        ThrowIfNullClient(client);
        var observation = StartHttpClientObservation(client, HttpAttributes.RequestMethodValues.Get, requestUri);
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

        observation.Dispose();
        return originalTask;
    }

    private static async Task<T> ObserveValueSlowAsync<T>(Task<T> originalTask, HttpClientObservation observation)
    {
        try
        {
            return await originalTask.ConfigureAwait(false);
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
        if (!TryGetHttpClientObservationOptions(out var options, out var traceEnabled))
            return default;

        var observation = StartHttpClientObservation(
            client,
            traceEnabled,
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
        if (!TryGetHttpClientObservationOptions(out var options, out var traceEnabled))
            return default;

        Uri? uri = null;
        if (traceEnabled && !string.IsNullOrWhiteSpace(requestUri))
            Uri.TryCreate(requestUri, UriKind.RelativeOrAbsolute, out uri);

        var observation = StartHttpClientObservation(client, traceEnabled, method, uri, requestUri);
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
        if (!TryGetHttpClientObservationOptions(out var options, out var traceEnabled))
            return default;

        var observation = StartHttpClientObservation(client, traceEnabled, method, requestUri, rawRequestUri);
        SetConvenienceRequestHeaders(observation.Activity, options, client, content);
        return observation;
    }

    // The forwarded call reaches the real HttpClient, whose native System.Net.Http meter owns
    // http.client.request.duration; the interceptor lane records no duplicate qyl instrument.
    private static bool TryGetHttpClientObservationOptions(
        out QylAutoInstrumentationOptions options,
        out bool traceEnabled)
    {
        options = QylAutoInstrumentationOptions.Current;
        traceEnabled = QylActivitySource.IsRecordingEnabled &&
                       options.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Traces, QylAutoInstrumentationIds.HttpClient);
        return traceEnabled;
    }

    private static HttpClientObservation StartHttpClientObservation(
        HttpClient client,
        bool traceEnabled,
        string method,
        Uri? requestUri,
        string? rawRequestUri)
    {
        method = QylHttpMethod.Normalize(method, out var methodOriginal);
        Activity? activity = null;

        if (traceEnabled)
        {
            requestUri = ResolveRequestUri(client, requestUri);
            activity = QylHttpActivityPolicy.StartClientActivity(
                QylAttributes.InstrumentationDomainValues.HttpClient,
                method,
                methodOriginal,
                requestUri,
                rawRequestUri);
        }

        return new HttpClientObservation(activity);
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
            QylHttpActivityPolicy.SetProtocolVersion(activity, response.Version);
            QylCaptureHelpers.SetHttpHeaders(
                activity,
                QylAutoInstrumentationOptions.Current.HttpClientCapturedResponseHeaderMap,
                response.Headers,
                response.Content?.Headers);

        }
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
    }

    private static void RecordResponseStatusException(HttpClientObservation observation, int statusCode)
    {
        var activity = observation.Activity;
        if (activity is not null)
        {
            QylHttpActivityPolicy.SetResponseStatus(activity, statusCode, 400);
        }
    }

    private readonly record struct HttpClientObservation(Activity? Activity) : IDisposable
    {
        public bool IsEnabled => Activity is not null;

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
