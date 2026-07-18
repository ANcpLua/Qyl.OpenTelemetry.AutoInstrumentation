#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORK="${TMPDIR:-/tmp}/qyl-smoke"
FEED="$WORK/feed"
PACKAGES="$WORK/packages"
NUGET_ORG="https://api.nuget.org/v3/index.json"
MODE="local"
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT/Directory.Build.props" | head -n 1)"
if [[ "${1:-}" == "--published" ]]; then
  if [[ $# -ne 2 || -z "${2:-}" ]]; then
    echo "usage: $0 [--published VERSION]" >&2
    exit 2
  fi
  MODE="published"
  VERSION="$2"
elif [[ $# -ne 0 ]]; then
  echo "usage: $0 [--published VERSION]" >&2
  exit 2
fi
PACKAGE_SOURCES="$FEED;$NUGET_ORG"
if [[ "$MODE" == "published" ]]; then
  PACKAGE_SOURCES="$NUGET_ORG"
fi
VERIFIED="$ROOT/tools/Qyl.OpenTelemetry.AutoInstrumentation.SmokeTest/verified/stdout.txt"
GENERATOR_DLL="$ROOT/artifacts/bin/Qyl.OpenTelemetry.AutoInstrumentation.SourceGenerators/release/Qyl.OpenTelemetry.AutoInstrumentation.SourceGenerators.dll"

case "$(uname -s)-$(uname -m)" in
  Darwin-arm64|Darwin-aarch64) RID="osx-arm64" ;;
  Darwin-*) RID="osx-x64" ;;
  Linux-arm64|Linux-aarch64) RID="linux-arm64" ;;
  Linux-*) RID="linux-x64" ;;
  *) echo "unsupported smoke platform: $(uname -s) $(uname -m)" >&2; exit 2 ;;
esac

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

if [[ -z "$VERSION" ]]; then
  echo "Directory.Build.props does not contain a package <Version>" >&2
  exit 2
fi

rm -rf "$WORK"
mkdir -p "$FEED" "$PACKAGES"

if [[ "$MODE" == "local" ]]; then
  dotnet build "$ROOT/src/Qyl.OpenTelemetry.AutoInstrumentation.SourceGenerators/Qyl.OpenTelemetry.AutoInstrumentation.SourceGenerators.csproj" -c Release -v quiet
  dotnet pack "$ROOT/src/Qyl.OpenTelemetry.AutoInstrumentation/Qyl.OpenTelemetry.AutoInstrumentation.csproj" -c Release -o "$FEED" -v quiet
  dotnet pack "$ROOT/src/Qyl.OpenTelemetry.AutoInstrumentation.DiagnosticListeners/Qyl.OpenTelemetry.AutoInstrumentation.DiagnosticListeners.csproj" -c Release -o "$FEED" -v quiet
  dotnet pack "$ROOT/src/Qyl.OpenTelemetry.AutoInstrumentation.Hosting/Qyl.OpenTelemetry.AutoInstrumentation.Hosting.csproj" -c Release -o "$FEED" -v quiet
fi

write_program() {
  local dir="$1"
  cat > "$dir/Program.cs" <<'EOF'
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Qyl.OpenTelemetry.AutoInstrumentation;

var captured = new List<Activity>();
using var listener = new ActivityListener
{
    ShouldListenTo = static source => source.Name == "Qyl.OpenTelemetry.AutoInstrumentation",
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity => captured.Add(activity),
};

ActivitySource.AddActivityListener(listener);

await using var server = LoopbackHttpServer.Start();
using var http = new HttpClient();
var response = await http.GetAsync(server.Uri + "probe?secret=redacted");
await server.RequestCompleted;
Console.WriteLine("http.status=" + ((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture));

var concreteLogger = new CapturingLogger();
ILogger logger = concreteLogger;
logger.Log(
    LogLevel.Warning,
    new EventId(5, "smoke"),
    "smoke-log",
    exception: null,
    static (state, exception) => exception is null ? state : state + ":" + exception.GetType().Name);

Console.WriteLine("logger.calls=" + concreteLogger.Calls.ToString(System.Globalization.CultureInfo.InvariantCulture));
Console.WriteLine("logger.last=" + concreteLogger.Last);

foreach (var activity in captured.OrderBy(static activity => activity.DisplayName, StringComparer.Ordinal))
{
    var tags = activity.TagObjects.ToDictionary(
        static tag => tag.Key,
        static tag => Convert.ToString(tag.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
        StringComparer.Ordinal);

    tags.TryGetValue("qyl.instrumentation.domain", out var domain);
    tags.TryGetValue("http.request.method", out var method);
    tags.TryGetValue("http.response.status_code", out var statusCode);
    tags.TryGetValue("log.severity", out var severity);

    Console.WriteLine("activity=" + activity.DisplayName + "|" + activity.Kind + "|" + domain + "|" + method + "|" + statusCode + "|" + severity);
}

Console.WriteLine("activity.count=" + captured.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));

return captured.Count == 2 ? 0 : 3;

internal sealed class LoopbackHttpServer : IAsyncDisposable
{
    private readonly TcpListener _listener;

    private LoopbackHttpServer(TcpListener listener)
    {
        _listener = listener;
        Uri = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/", UriKind.Absolute);
        RequestCompleted = ServeOnceAsync(listener);
    }

    public Uri Uri { get; }

    public Task RequestCompleted { get; }

    public static LoopbackHttpServer Start()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(1);
        return new LoopbackHttpServer(listener);
    }

    public ValueTask DisposeAsync()
    {
        _listener.Stop();
        return ValueTask.CompletedTask;
    }

    private static async Task ServeOnceAsync(TcpListener listener)
    {
        using var client = await listener.AcceptTcpClientAsync();
        await using var stream = client.GetStream();
        var buffer = new byte[4096];
        var received = new List<byte>();
        while (true)
        {
            var count = await stream.ReadAsync(buffer);
            if (count == 0)
                throw new InvalidOperationException("Loopback client closed before sending HTTP headers.");
            received.AddRange(buffer.AsSpan(0, count).ToArray());
            if (received.Count >= 4 && received.ToArray().AsSpan().IndexOf("\r\n\r\n"u8) >= 0)
                break;
        }

        var response = Encoding.ASCII.GetBytes("HTTP/1.1 204 No Content\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(response);
    }
}

internal sealed class CapturingLogger : ILogger
{
    public int Calls { get; private set; }

    public string Last { get; private set; } = string.Empty;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Calls++;
        Last = logLevel + ":" + eventId.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + formatter(state, exception);
    }
}
EOF
}

write_package_consumer() {
  local dir="$1"
  mkdir -p "$dir"
  cat > "$dir/Consumer.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RestoreSources>$PACKAGE_SOURCES</RestoreSources>
    <RestorePackagesPath>$PACKAGES/pkg</RestorePackagesPath>
    <RestoreNoCache>true</RestoreNoCache>
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
    <CompilerGeneratedFilesOutputPath>Generated</CompilerGeneratedFilesOutputPath>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Qyl.OpenTelemetry.AutoInstrumentation" Version="$VERSION" />
    <PackageReference Include="Qyl.OpenTelemetry.AutoInstrumentation.Hosting" Version="$VERSION" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.8" />
    <Compile Remove="Generated/**/*.cs" />
  </ItemGroup>
</Project>
EOF
  write_program "$dir"
}

write_projectreference_consumer() {
  local dir="$1"
  mkdir -p "$dir"
  cat > "$dir/Consumer.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RestoreSources>$NUGET_ORG</RestoreSources>
    <RestorePackagesPath>$PACKAGES/projref</RestorePackagesPath>
    <RestoreNoCache>true</RestoreNoCache>
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
    <CompilerGeneratedFilesOutputPath>Generated</CompilerGeneratedFilesOutputPath>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="$ROOT/src/Qyl.OpenTelemetry.AutoInstrumentation/Qyl.OpenTelemetry.AutoInstrumentation.csproj" />
    <ProjectReference Include="$ROOT/src/Qyl.OpenTelemetry.AutoInstrumentation.SourceGenerators/Qyl.OpenTelemetry.AutoInstrumentation.SourceGenerators.csproj"
                      Condition="'\$(PublishAot)' != 'true'"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false"
                      GlobalPropertiesToRemove="PublishAot;PublishSingleFile;PublishTrimmed;RuntimeIdentifier;RuntimeIdentifiers;SelfContained" />
    <Analyzer Include="$GENERATOR_DLL" Condition="'\$(PublishAot)' == 'true'" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.8" />
    <Compile Remove="Generated/**/*.cs" />
  </ItemGroup>

  <Import Project="$ROOT/src/Qyl.OpenTelemetry.AutoInstrumentation/buildTransitive/Qyl.OpenTelemetry.AutoInstrumentation.targets" />
</Project>
EOF
  write_program "$dir"
}

assert_generated_interceptor() {
  local dir="$1"
  local count
  count="$(find "$dir/Generated" -name 'QylAutoInstrumentation.Interceptors.g.cs' | wc -l | tr -d '[:space:]')"
  if [[ "$count" != "1" ]]; then
    echo "expected one generated interceptor source in $dir, found $count" >&2
    find "$dir/Generated" -type f -print >&2 || true
    exit 4
  fi
}

assert_no_aot_warnings() {
  local log="$1"
  local consumer="$2"
  local matches

  matches="$(grep -Eo '\b(IL2[0-9]{3}|IL3[0-9]{3}|IL4[0-9]{3}|CA[0-9]{4})\b' "$log" | sort -u || true)"
  if [[ -n "$matches" ]]; then
    echo "AOT warning gate failed for $consumer; found analyzer warnings:" >&2
    echo "$matches" >&2
    echo "--- publish log ---" >&2
    cat "$log" >&2
    exit 5
  fi

  echo "aot-warning-gate-ok consumer=$consumer warnings=0"
}

run_managed_consumer() {
  local dir="$1"
  local managed_out="$2"

  dotnet restore "$dir/Consumer.csproj" --no-cache --force-evaluate -v quiet
  dotnet build "$dir/Consumer.csproj" -c Release --no-restore -v quiet
  assert_generated_interceptor "$dir"
  dotnet "$dir/bin/Release/net10.0/Consumer.dll" > "$managed_out"
  diff -u "$VERIFIED" "$managed_out"
}

publish_nativeaot_consumer() {
  local name="$1"
  local dir="$2"
  local publish_log="$3"

  dotnet restore "$dir/Consumer.csproj" \
    -r "$RID" \
    -p:PublishAot=true \
    --no-cache \
    --force-evaluate \
    -v quiet

  dotnet publish "$dir/Consumer.csproj" \
    -c Release \
    -r "$RID" \
    -p:PublishAot=true \
    -p:SelfContained=true \
    -p:InvariantGlobalization=true \
    -p:TreatWarningsAsErrors=true \
    --no-restore \
    -v quiet 2>&1 | tee "$publish_log"
  assert_no_aot_warnings "$publish_log" "$name"
}

run_nativeaot_consumer() {
  local dir="$1"
  local native_out="$2"

  "$dir/bin/Release/net10.0/$RID/publish/Consumer" > "$native_out"
  diff -u "$VERIFIED" "$native_out"
}

run_consumer() {
  local name="$1"
  local dir="$2"

  run_managed_consumer "$dir" "$WORK/$name.managed.stdout"
  publish_nativeaot_consumer "$name" "$dir" "$WORK/$name.nativeaot.publish.log"
  run_nativeaot_consumer "$dir" "$WORK/$name.nativeaot.stdout"
}

write_package_consumer "$WORK/pkg-consumer"
run_consumer "package-reference" "$WORK/pkg-consumer"
if [[ "$MODE" == "local" ]]; then
  write_projectreference_consumer "$WORK/projref-consumer"
  run_consumer "project-reference" "$WORK/projref-consumer"
fi

echo "smoketest-ok mode=$MODE version=$VERSION rid=$RID"
