using System.ComponentModel;

namespace Qyl.Telemetry.AutoInstrumentation.GeneratedCode;

/// <summary>
/// Declares an intercepted integration. The source generator discovers every class carrying this
/// attribute in the referenced runtime assembly and derives the interceptor kind, matcher, emitted
/// body, instrumentation id, domain, signal, and contract manifest from the declaration alone.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class QylIntegrationAttribute : Attribute
{
    /// <summary>Declares the integration's instrumentation id and domain.</summary>
    public QylIntegrationAttribute(string instrumentationId, string domain)
    {
        InstrumentationId = instrumentationId;
        Domain = domain;
    }

    /// <summary>The upstream instrumentation id (the <c>OTEL_DOTNET_AUTO_*_{ID}_INSTRUMENTATION_ENABLED</c> token).</summary>
    public string InstrumentationId { get; }

    /// <summary>The <c>qyl.instrumentation.domain</c> value stamped on every span.</summary>
    public string Domain { get; }

    /// <summary>The signal the integration's spans belong to.</summary>
    public QylAutoInstrumentationSignal Signal { get; set; } = QylAutoInstrumentationSignal.Traces;

    /// <summary>Metric instrumentation ids the integration also owns.</summary>
    public string[] MetricIds { get; set; } = [];
}

/// <summary>
/// Declares one intercepted call shape on an integration: the receiver type, the method names, the
/// named shape predicate that validates the overload, and the helper members the emitted body calls.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class QylInterceptAttribute : Attribute
{
    /// <summary>Declares the receiver type (metadata name, arity as a backtick suffix; empty when the shape decides) and the intercepted method names (empty when the shape decides).</summary>
    public QylInterceptAttribute(string receiverType, params string[] methods)
    {
        ReceiverType = receiverType;
        Methods = methods;
    }

    /// <summary>The receiver type the intercepted method is declared on, or that the extension-method receiver implements.</summary>
    public string ReceiverType { get; }

    /// <summary>The intercepted method names.</summary>
    public string[] Methods { get; }

    /// <summary>The named shape predicate (<see cref="QylShapes"/>) that validates the overload and computes the shape-bound value.</summary>
    public string Shape { get; set; } = string.Empty;

    /// <summary>The helper method that starts the activity.</summary>
    public string Start { get; set; } = string.Empty;

    /// <summary>The body template the generator emits.</summary>
    public QylInterceptorBody Body { get; set; } = QylInterceptorBody.Trace;

    /// <summary>Whether the runtime observes the returned task instead of the interceptor awaiting it.</summary>
    public bool ObserveAsync { get; set; }

    /// <summary>Whether <see cref="ObserveAsync"/> applies only to asynchronous overloads with by-reference parameters.</summary>
    public bool ObserveByRefOnly { get; set; }

    /// <summary>The helper method that enriches the started activity from the call's arguments.</summary>
    public string Enrich { get; set; } = string.Empty;

    /// <summary>The helper method that records the operation's duration metric; the helper also exposes <c>GetTimestamp()</c>.</summary>
    public string Metric { get; set; } = string.Empty;
}

/// <summary>The closed set of body templates the generator emits.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public enum QylInterceptorBody
{
    /// <summary>Start an activity, invoke, record the exception, dispose.</summary>
    Trace,
    /// <summary>Forward the call to a same-named helper overload.</summary>
    Forward,
    /// <summary>Trace a <c>DbCommand</c> execution with the duration metric observed alongside the task.</summary>
    DbCommand,
}

/// <summary>The named shape predicates the generator implements; a declaration selects one by name.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class QylShapes
{
    /// <summary>The <c>HttpClient</c> convenience and <c>Send</c> overloads.</summary>
    public const string HttpClient = "HttpClient";
    /// <summary>The <c>DbCommand</c> execute overloads, with the provider fan-out by receiver namespace.</summary>
    public const string DbCommand = "DbCommand";
    /// <summary>Operation methods on a <c>ClientBase&lt;TChannel&gt;</c>, named by the contract attributes.</summary>
    public const string WcfClient = "WcfClient";
    /// <summary>The <c>IProducer&lt;TKey, TValue&gt;</c> produce overloads.</summary>
    public const string KafkaProduce = "KafkaProduce";
    /// <summary>The <c>IConsumer&lt;TKey, TValue&gt;</c> consume overloads.</summary>
    public const string KafkaConsume = "KafkaConsume";
    /// <summary>A <c>Task</c>-returning NServiceBus publish or send.</summary>
    public const string NServiceBusOperation = "NServiceBusOperation";
    /// <summary>The <c>IJob.Execute(IJobExecutionContext, CancellationToken)</c> operation.</summary>
    public const string QuartzJob = "QuartzJob";
    /// <summary>An <c>IDatabaseAsync</c> command whose wire command the command table resolves.</summary>
    public const string RedisCommand = "RedisCommand";
    /// <summary>The <c>IDocumentExecuter.ExecuteAsync</c> operation.</summary>
    public const string GraphQlExecute = "GraphQlExecute";
    /// <summary>An <c>IMongoCollection&lt;T&gt;</c> operation.</summary>
    public const string MongoDbCollection = "MongoDbCollection";
}

/// <summary>Binds a helper parameter to an intercepted argument by position, optionally filtered by the argument's type and converted by a format whose <c>{0}</c> is the argument.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = true, Inherited = false)]
public sealed class QylFromArgumentAttribute : Attribute
{
    /// <summary>Binds to the intercepted argument at <paramref name="index"/>.</summary>
    public QylFromArgumentAttribute(int index)
    {
        Index = index;
    }

    /// <summary>The intercepted argument's position.</summary>
    public int Index { get; }

    /// <summary>The argument type this binding applies to; empty applies to any type.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>The conversion applied at the call site, with <c>{0}</c> standing for the argument.</summary>
    public string Convert { get; set; } = "{0}";
}

/// <summary>Binds a helper parameter to the receiver, or to a member path on it.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class QylFromReceiverAttribute : Attribute
{
    /// <summary>Binds to the receiver itself, or to <paramref name="path"/> read from it.</summary>
    public QylFromReceiverAttribute(string path = "")
    {
        Path = path;
    }

    /// <summary>The member path read from the receiver; empty binds the receiver itself.</summary>
    public string Path { get; }
}

/// <summary>Binds a helper parameter to the intercepted method's name.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class QylFromMethodNameAttribute : Attribute;

/// <summary>Binds a helper parameter to the instrumentation id the intercepted call reports under.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class QylFromInstrumentationIdAttribute : Attribute;

/// <summary>Binds a helper parameter to the value the declaration's shape predicate computed for the call.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class QylFromShapeAttribute : Attribute;
