using Qyl.Telemetry.AutoInstrumentation;

var options = QylAutoInstrumentationOptions.Current;

Console.WriteLine("global=" + options.GlobalEnabled);
Console.WriteLine("traces=" + options.TracesEnabled);
Console.WriteLine("metrics=" + options.MetricsEnabled);
Console.WriteLine("logs=" + options.LogsEnabled);
Console.WriteLine("trace.http=" + options.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Traces, QylAutoInstrumentationIds.HttpClient));
Console.WriteLine("trace.sql=" + options.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Traces, QylAutoInstrumentationIds.SqlClient));
Console.WriteLine("metric.http=" + options.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Metrics, QylAutoInstrumentationIds.HttpClient));
Console.WriteLine("metric.sql=" + options.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Metrics, QylAutoInstrumentationIds.SqlClient));
Console.WriteLine("log.fallback=" + options.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Logs, "UNDECLARED"));
Console.WriteLine("meters=" + string.Join("|", QylMetricMeters.GetEnabledMeterNames()));
Console.WriteLine("ef.text=" + options.EntityFrameworkCoreSetDbStatementForText);
Console.WriteLine("sql.text=" + options.SqlClientSetDbStatementForText);
Console.WriteLine("aspnetcore.req=" + string.Join("|", options.AspNetCoreCapturedRequestHeaders));
Console.WriteLine("aspnetcore.res=" + string.Join("|", options.AspNetCoreCapturedResponseHeaders));
Console.WriteLine("aspnetcore.query.unredacted=" + options.AspNetCoreUrlQueryRedactionDisabled);
Console.WriteLine("http.query.unredacted=" + options.HttpClientUrlQueryRedactionDisabled);
