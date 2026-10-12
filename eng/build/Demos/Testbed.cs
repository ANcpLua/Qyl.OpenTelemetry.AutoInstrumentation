using System.Buffers.Binary;
using System.Data.Common;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;

namespace Qyl.Build;

/// <summary>The eight services of the Railway testbed. They sleep when idle.</summary>
enum TestbedService
{
    RabbitMq,
    Kafka,
    MongoDb,
    Redis,
    Postgres,
    MySql,
    Oracle,
    SqlServer,
}

/// <summary>
/// The testbed environment file: where the Railway testbed services are reached. Its path is
/// ~/.config/vitals/qyl-testbed.env unless QYL_TESTBED_ENV names another. The file is the binding
/// contract with eng/testbed; this class reads it and nothing else knows its keys.
/// </summary>
sealed class Testbed
{
    private readonly IReadOnlyDictionary<string, string> _values;

    private Testbed(string path, IReadOnlyDictionary<string, string> values)
    {
        Path = path;
        _values = values;
    }

    public string Path { get; }

    public static string DefaultPath =>
        Environment.GetEnvironmentVariable("QYL_TESTBED_ENV") is { Length: > 0 } configured
            ? configured
            : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "vitals", "qyl-testbed.env");

    public static Testbed Load()
    {
        var path = DefaultPath;
        Gate.Require(File.Exists(path), $"the testbed environment file {path} does not exist (QYL_TESTBED_ENV overrides the path); the container lanes need it");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            if (line.StartsWith("export ", StringComparison.Ordinal))
                line = line["export ".Length..].TrimStart();
            var separator = line.IndexOf('=');
            if (separator <= 0)
                continue;
            var value = line[(separator + 1)..].Trim();
            if (value.Length >= 2 && (value[0] == '"' && value[^1] == '"' || value[0] == '\'' && value[^1] == '\''))
                value = value[1..^1];
            values[line[..separator].Trim()] = value;
        }

        return new Testbed(path, values);
    }

    /// <summary>How long a sleeping service may take to answer its first connection.</summary>
    public TimeSpan WakeLimit =>
        TimeSpan.FromSeconds(_values.TryGetValue("QYL_TESTBED_WAKE_SECONDS_MAX", out var seconds) && int.TryParse(seconds, out var parsed) ? parsed : 300);

    public string Require(string key)
    {
        Gate.Require(_values.TryGetValue(key, out var value) && value.Length > 0, $"{Path} sets no {key}");
        return value!;
    }

    /// <summary>The host and port a service answers on.</summary>
    public (string Host, int Port) Endpoint(TestbedService service)
    {
        switch (service)
        {
            case TestbedService.RabbitMq: return UrlEndpoint(Require("QYL_TESTBED_RABBITMQ_URL"), 5672);
            case TestbedService.MongoDb: return UrlEndpoint(Require("QYL_TESTBED_MONGODB_URL"), 27017);
            case TestbedService.Redis: return UrlEndpoint(Require("QYL_TESTBED_REDIS_URL"), 6379);
            case TestbedService.Kafka: return HostPort(Require("QYL_TESTBED_KAFKA_BOOTSTRAP").Split(',')[0], 9092);
            case TestbedService.Postgres:
            {
                var builder = ConnectionString(Require("QYL_TESTBED_POSTGRES_URL"));
                return (Value(builder, "Host", "Server"), int.Parse(Value(builder, "Port", defaultValue: "5432"), System.Globalization.CultureInfo.InvariantCulture));
            }
            case TestbedService.MySql:
            {
                var builder = ConnectionString(Require("QYL_TESTBED_MYSQL_URL"));
                return (Value(builder, "Server", "Host"), int.Parse(Value(builder, "Port", defaultValue: "3306"), System.Globalization.CultureInfo.InvariantCulture));
            }
            case TestbedService.Oracle:
            {
                var builder = ConnectionString(Require("QYL_TESTBED_ORACLE_URL"));
                var source = Value(builder, "Data Source");
                return HostPort(source.Split('/')[0], 1521);
            }
            case TestbedService.SqlServer:
            {
                var builder = ConnectionString(Require("QYL_TESTBED_SQLSERVER_URL"));
                var server = Value(builder, "Server", "Data Source");
                var parts = server.Replace("tcp:", string.Empty, StringComparison.OrdinalIgnoreCase).Split(',');
                return (parts[0], parts.Length > 1 ? int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : 1433);
            }
            default: throw Gate.Fail($"unknown testbed service {service}");
        }
    }

    /// <summary>The environment a demo reads to reach a service, derived from the testbed contract.</summary>
    public IReadOnlyDictionary<string, string> DemoEnvironment(TestbedService service)
    {
        switch (service)
        {
            case TestbedService.RabbitMq:
                return new Dictionary<string, string> { ["QYL_RABBITMQ_URI"] = Require("QYL_TESTBED_RABBITMQ_URL") };
            case TestbedService.Kafka:
                return new Dictionary<string, string>
                {
                    ["QYL_KAFKA_BOOTSTRAP_SERVERS"] = Require("QYL_TESTBED_KAFKA_BOOTSTRAP"),
                    ["QYL_KAFKA_SASL_USERNAME"] = Require("QYL_TESTBED_KAFKA_USER"),
                    ["QYL_KAFKA_SASL_PASSWORD"] = Require("QYL_TESTBED_KAFKA_PASSWORD"),
                };
            case TestbedService.MongoDb:
                return new Dictionary<string, string> { ["QYL_MONGODB_CONNECTION_STRING"] = Require("QYL_TESTBED_MONGODB_URL") };
            case TestbedService.Redis:
            {
                // StackExchange.Redis takes host:port,password=..., not a redis:// URL.
                var url = new Uri(Require("QYL_TESTBED_REDIS_URL"));
                var userInfo = Uri.UnescapeDataString(url.UserInfo);
                var password = userInfo.Contains(':') ? userInfo[(userInfo.IndexOf(':') + 1)..] : userInfo;
                var configuration = $"{url.Host}:{(url.IsDefaultPort || url.Port < 0 ? 6379 : url.Port)}";
                if (password.Length > 0)
                    configuration += ",password=" + password;
                return new Dictionary<string, string> { ["QYL_REDIS_CONFIGURATION"] = configuration };
            }
            case TestbedService.Postgres:
                return new Dictionary<string, string> { ["QYL_POSTGRES_CONNECTION_STRING"] = Require("QYL_TESTBED_POSTGRES_URL") };
            case TestbedService.MySql:
                return new Dictionary<string, string> { ["QYL_MYSQL_CONNECTION_STRING"] = Require("QYL_TESTBED_MYSQL_URL") };
            case TestbedService.Oracle:
                return new Dictionary<string, string> { ["QYL_ORACLE_CONNECTION_STRING"] = Require("QYL_TESTBED_ORACLE_URL") };
            case TestbedService.SqlServer:
            {
                // The demo asserts db.namespace=tempdb and the server.address/server.port it dialed.
                var builder = ConnectionString(Require("QYL_TESTBED_SQLSERVER_URL"));
                if (!builder.ContainsKey("Initial Catalog") && !builder.ContainsKey("Database"))
                    builder["Initial Catalog"] = "tempdb";
                var (host, port) = Endpoint(TestbedService.SqlServer);
                return new Dictionary<string, string>
                {
                    ["QYL_SQLCLIENT_CONNECTION_STRING"] = builder.ConnectionString,
                    ["QYL_SQLCLIENT_EXPECTED_ADDRESS"] = host,
                    ["QYL_SQLCLIENT_EXPECTED_PORT"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                };
            }
            default: throw Gate.Fail($"unknown testbed service {service}");
        }
    }

    /// <summary>
    /// Waits until a sleeping service answers its protocol's first exchange, with a visible countdown,
    /// for at most the file's wake limit; then fails naming the service. Returns how long it took.
    /// </summary>
    public TimeSpan Wake(TestbedService service)
    {
        var (host, port) = Endpoint(service);
        var limit = WakeLimit;
        var stopwatch = Stopwatch.StartNew();
        var lastReport = TimeSpan.Zero;
        string? lastError = null;
        while (stopwatch.Elapsed < limit)
        {
            var remaining = limit - stopwatch.Elapsed;
            try
            {
                if (TestbedProbes.Answers(service, host, port, TimeSpan.FromSeconds(Math.Min(15, Math.Max(1, remaining.TotalSeconds)))))
                {
                    Log.Information("testbed {Service} at {Host}:{Port} answered after {Seconds:0.0}s", service, host, port, stopwatch.Elapsed.TotalSeconds);
                    return stopwatch.Elapsed;
                }

                lastError = "connected, but the service did not answer its protocol's first exchange";
            }
            catch (Exception exception) when (exception is SocketException or IOException or TimeoutException or OperationCanceledException)
            {
                lastError = exception.Message;
            }

            if (stopwatch.Elapsed - lastReport >= TimeSpan.FromSeconds(10) || lastReport == TimeSpan.Zero)
            {
                lastReport = stopwatch.Elapsed;
                Log.Information("waking testbed {Service} at {Host}:{Port}: {Left:0}s left ({Error})", service, host, port, Math.Max(0, (limit - stopwatch.Elapsed).TotalSeconds), lastError);
            }

            Thread.Sleep(TimeSpan.FromSeconds(2));
        }

        throw Gate.Fail($"testbed service {service} at {host}:{port} did not wake within {limit.TotalSeconds:0}s: {lastError}");
    }

    private static (string Host, int Port) UrlEndpoint(string url, int defaultPort)
    {
        var uri = new Uri(url);
        return (uri.Host, uri.Port > 0 ? uri.Port : defaultPort);
    }

    private static (string Host, int Port) HostPort(string value, int defaultPort)
    {
        var separator = value.LastIndexOf(':');
        return separator > 0
            ? (value[..separator], int.Parse(value[(separator + 1)..], System.Globalization.CultureInfo.InvariantCulture))
            : (value, defaultPort);
    }

    private static DbConnectionStringBuilder ConnectionString(string value) => new() { ConnectionString = value };

    private static string Value(DbConnectionStringBuilder builder, string key, string? alternative = null, string? defaultValue = null)
    {
        if (builder.TryGetValue(key, out var value) && value?.ToString() is { Length: > 0 } text)
            return text;
        if (alternative is not null && builder.TryGetValue(alternative, out value) && value?.ToString() is { Length: > 0 } other)
            return other;
        return defaultValue ?? throw Gate.Fail($"the testbed connection string has no {key}");
    }
}

/// <summary>
/// One protocol exchange per service that only the awake server can answer: a TCP connect alone
/// proves nothing behind a proxy that accepts for a sleeping service.
/// </summary>
static class TestbedProbes
{
    public static bool Answers(TestbedService service, string host, int port, TimeSpan timeout)
    {
        using var client = new TcpClient();
        using var cancellation = new CancellationTokenSource(timeout);
        client.ConnectAsync(host, port, cancellation.Token).AsTask().GetAwaiter().GetResult();
        client.ReceiveTimeout = (int)timeout.TotalMilliseconds;
        client.SendTimeout = (int)timeout.TotalMilliseconds;
        using var stream = client.GetStream();
        var request = Request(service, host, port);
        if (request.Length > 0)
            stream.Write(request);

        var buffer = new byte[512];
        var read = ReadAtLeast(stream, buffer, MinimumAnswer(service), cancellation.Token);
        return Accepts(service, buffer.AsSpan(0, read));
    }

    private static int ReadAtLeast(NetworkStream stream, byte[] buffer, int minimum, CancellationToken token)
    {
        var total = 0;
        while (total < minimum)
        {
            var read = stream.ReadAsync(buffer.AsMemory(total), token).AsTask().GetAwaiter().GetResult();
            if (read == 0)
                break;
            total += read;
        }

        return total;
    }

    private static int MinimumAnswer(TestbedService service) => service switch
    {
        TestbedService.Postgres => 1,
        TestbedService.MySql => 5,
        TestbedService.Redis => 1,
        TestbedService.RabbitMq => 8,
        TestbedService.Kafka => 8,
        TestbedService.MongoDb => 16,
        TestbedService.SqlServer => 8,
        TestbedService.Oracle => 8,
        _ => 1,
    };

    private static bool Accepts(TestbedService service, ReadOnlySpan<byte> answer) => service switch
    {
        // SSLRequest is answered with a single 'S' or 'N'.
        TestbedService.Postgres => answer.Length >= 1 && answer[0] is (byte)'S' or (byte)'N',
        // The server speaks first: a handshake (protocol 10) or an error packet (0xFF).
        TestbedService.MySql => answer.Length >= 5 && answer[4] is 0x0A or 0xFF,
        // PING is answered +PONG, or -NOAUTH / -ERR when a password is required.
        TestbedService.Redis => answer.Length >= 1 && answer[0] is (byte)'+' or (byte)'-',
        // The protocol header is answered with a Connection.Start method frame (type 1), or with the
        // server's own protocol header when the versions differ.
        TestbedService.RabbitMq => answer.Length >= 8 && (answer[0] == 1 || answer[..4].SequenceEqual("AMQP"u8)),
        // ApiVersions echoes the correlation id after the length prefix.
        TestbedService.Kafka => answer.Length >= 8 && BinaryPrimitives.ReadInt32BigEndian(answer[4..8]) == KafkaCorrelationId,
        // OP_MSG reply header: opCode 2013 at offset 12.
        TestbedService.MongoDb => answer.Length >= 16 && BinaryPrimitives.ReadInt32LittleEndian(answer[12..16]) == 2013,
        // PRELOGIN is answered with a tabular-result packet (type 4).
        TestbedService.SqlServer => answer.Length >= 8 && answer[0] == 0x04,
        // A TNS CONNECT is answered ACCEPT, REFUSE, REDIRECT or RESEND.
        TestbedService.Oracle => answer.Length >= 8 && answer[4] is 2 or 4 or 5 or 11,
        _ => false,
    };

    private const int KafkaCorrelationId = 0x51796C;

    private static byte[] Request(TestbedService service, string host, int port) => service switch
    {
        TestbedService.Postgres => [0, 0, 0, 8, 0x04, 0xD2, 0x16, 0x2F],
        TestbedService.MySql => [],
        TestbedService.Redis => "PING\r\n"u8.ToArray(),
        TestbedService.RabbitMq => "AMQP\0\0\u0009\u0001"u8.ToArray(),
        TestbedService.Kafka => KafkaApiVersions(),
        TestbedService.MongoDb => MongoHello(),
        TestbedService.SqlServer => SqlServerPrelogin(),
        TestbedService.Oracle => OracleConnect(host, port),
        _ => [],
    };

    private static byte[] KafkaApiVersions()
    {
        var client = "qyl-testbed-probe"u8;
        var body = new byte[2 + 2 + 4 + 2 + client.Length];
        BinaryPrimitives.WriteInt16BigEndian(body.AsSpan(0), 18);
        BinaryPrimitives.WriteInt16BigEndian(body.AsSpan(2), 0);
        BinaryPrimitives.WriteInt32BigEndian(body.AsSpan(4), KafkaCorrelationId);
        BinaryPrimitives.WriteInt16BigEndian(body.AsSpan(8), (short)client.Length);
        client.CopyTo(body.AsSpan(10));
        var request = new byte[4 + body.Length];
        BinaryPrimitives.WriteInt32BigEndian(request, body.Length);
        body.CopyTo(request, 4);
        return request;
    }

    private static byte[] MongoHello()
    {
        // BSON { hello: 1, $db: "admin" }
        var document = new List<byte>();
        document.Add(0x10);
        document.AddRange("hello\0"u8.ToArray());
        document.AddRange(BitConverter.GetBytes(1));
        document.Add(0x02);
        document.AddRange("$db\0"u8.ToArray());
        document.AddRange(BitConverter.GetBytes(6));
        document.AddRange("admin\0"u8.ToArray());
        document.Add(0x00);
        var bson = new byte[4 + document.Count];
        BinaryPrimitives.WriteInt32LittleEndian(bson, bson.Length);
        document.CopyTo(bson, 4);

        var message = new byte[16 + 4 + 1 + bson.Length];
        BinaryPrimitives.WriteInt32LittleEndian(message.AsSpan(0), message.Length);
        BinaryPrimitives.WriteInt32LittleEndian(message.AsSpan(4), 1);
        BinaryPrimitives.WriteInt32LittleEndian(message.AsSpan(8), 0);
        BinaryPrimitives.WriteInt32LittleEndian(message.AsSpan(12), 2013);
        BinaryPrimitives.WriteInt32LittleEndian(message.AsSpan(16), 0);
        message[20] = 0;
        bson.CopyTo(message, 21);
        return message;
    }

    private static byte[] SqlServerPrelogin()
    {
        // Options: VERSION (offset 11, length 6), ENCRYPTION (offset 17, length 1), terminator.
        byte[] options = [0x00, 0x00, 0x0B, 0x00, 0x06, 0x01, 0x00, 0x11, 0x00, 0x01, 0xFF];
        byte[] data = [0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02];
        var length = 8 + options.Length + data.Length;
        var packet = new byte[length];
        packet[0] = 0x12;
        packet[1] = 0x01;
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2), (ushort)length);
        packet[6] = 0x01;
        options.CopyTo(packet, 8);
        data.CopyTo(packet, 8 + options.Length);
        return packet;
    }

    private static byte[] OracleConnect(string host, int port)
    {
        var descriptor = Encoding.ASCII.GetBytes($"(DESCRIPTION=(CONNECT_DATA=(SERVICE_NAME=FREEPDB1)(CID=(PROGRAM=qyl)(HOST=probe)(USER=qyl)))(ADDRESS=(PROTOCOL=TCP)(HOST={host})(PORT={port})))");
        // The version-318 layout: an Oracle 23 listener closes a pre-315 CONNECT without an answer.
        const int offset = 74;
        var packet = new byte[offset + descriptor.Length];
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(0), (ushort)packet.Length);
        packet[4] = 1;                                                    // CONNECT
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(8), 0x013E);  // version 318
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(10), 0x012C); // compatible 300
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(12), 0x0001); // service options: don't care
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(14), 0x2000); // SDU
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(16), 0xFFFF); // TDU
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(18), 0x4F98); // protocol characteristics
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(22), 0x0001); // value of 1 in hardware
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(24), (ushort)descriptor.Length);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(26), offset);
        packet[32] = 0x41;
        packet[33] = 0x41;
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(58), 0x2000);  // SDU, 32-bit
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(62), 0xFFFF);  // TDU, 32-bit
        descriptor.CopyTo(packet, offset);
        return packet;
    }
}
