using Qyl.Telemetry.AutoInstrumentation;
using Qyl.Telemetry.TestFixtures;

namespace Qyl.Telemetry.Integration.Tests;

[TestClass]
public sealed class ClientHeaderCaptureTests(TestContext testContext)
{
    private const string RequestHeaders = " X-Tenant-Id, X_Request_Id, X-Request-Id, X-REQUEST-ID, X.Request.Id, Content-Type, Content-Disposition, X-Custom-Multi ";
    private const string ResponseHeaders = "X-RateLimit-Remaining,Content-Type,X-Custom-Multi";
    private const string EmptyHeaders = " , \t, ";
    private static NativeCaptureFixture? _fixture;

    [ClassInitialize]
    public static async Task PublishAsync(TestContext context) =>
        _fixture = await NativeCaptureFixture.PublishAsync("ClientHeaderCapture", context);

    [TestMethod]
    [DataRow(ClientCaptureProfiles.Configured, RequestHeaders, ResponseHeaders, ClientCaptureControl.Enabled, true, true,
        ArgumentsDisplayName = ClientCaptureProfiles.Configured)]
    [DataRow(ClientCaptureProfiles.Request, RequestHeaders, null, ClientCaptureControl.Enabled, true, false,
        ArgumentsDisplayName = ClientCaptureProfiles.Request)]
    [DataRow(ClientCaptureProfiles.Response, null, ResponseHeaders, ClientCaptureControl.Enabled, false, true,
        ArgumentsDisplayName = ClientCaptureProfiles.Response)]
    [DataRow(ClientCaptureProfiles.None, null, null, ClientCaptureControl.Enabled, false, false,
        ArgumentsDisplayName = ClientCaptureProfiles.None)]
    [DataRow(ClientCaptureProfiles.Empty, EmptyHeaders, EmptyHeaders, ClientCaptureControl.Enabled, false, false,
        ArgumentsDisplayName = ClientCaptureProfiles.Empty)]
    [DataRow(ClientCaptureProfiles.Disabled, RequestHeaders, ResponseHeaders, ClientCaptureControl.InstrumentationDisabled, false, false,
        ArgumentsDisplayName = ClientCaptureProfiles.Disabled)]
    [DataRow(ClientCaptureProfiles.GlobalDisabled, RequestHeaders, ResponseHeaders, ClientCaptureControl.GlobalDisabled, false, false,
        ArgumentsDisplayName = ClientCaptureProfiles.GlobalDisabled)]
    [DataRow(ClientCaptureProfiles.Unsampled, RequestHeaders, ResponseHeaders, ClientCaptureControl.Unsampled, false, false,
        ArgumentsDisplayName = ClientCaptureProfiles.Unsampled)]
    [DataRow(ClientCaptureProfiles.Suppressed, RequestHeaders, ResponseHeaders, ClientCaptureControl.Suppressed, false, false,
        ArgumentsDisplayName = ClientCaptureProfiles.Suppressed)]
    public async Task Native_http_capture_preserves_headers_controls_and_parentage(
        string profile, string? requestHeaders, string? responseHeaders,
        ClientCaptureControl control, bool requestCaptured, bool responseCaptured)
    {
        var environment = NativeCaptureFixture.CreateEnvironment(control, QylAutoInstrumentationIds.HttpClient);
        if (requestHeaders is not null)
            environment[QylAutoInstrumentationOptions.HttpClientRequestHeadersVariable] = requestHeaders;
        if (responseHeaders is not null)
            environment[QylAutoInstrumentationOptions.HttpClientResponseHeadersVariable] = responseHeaders;
        Assert.IsNotNull(_fixture);
        await _fixture.RunAsync(new ClientCaptureScenario(profile, control, requestCaptured, responseCaptured), environment, testContext);
    }
}
