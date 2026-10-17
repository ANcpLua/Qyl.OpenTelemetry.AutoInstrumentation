using Qyl.Telemetry.AutoInstrumentation;
using Qyl.Telemetry.TestFixtures;

namespace Qyl.Telemetry.Integration.Tests;

[TestClass]
public sealed class ClientMetadataCaptureTests(TestContext testContext)
{
    private const string RequestHeaders = " X-Demo-Md, X-Request-Id, X_Request_Id, X-REQUEST-ID, X.Request.Id, X-Multi, TRACE-BIN ";
    private const string ResponseHeaders = "X-Demo-Res-Md,X-Demo-Trailer-Md,X-Overlap,trace-bin";
    private const string EmptyHeaders = " , \t, ";
    private const string HttpHeader = "X-Demo-Md";
    private const string BinaryHeader = "TRACE-BIN";
    private static NativeCaptureFixture? _fixture;

    [ClassInitialize]
    public static async Task PublishAsync(TestContext context) =>
        _fixture = await NativeCaptureFixture.PublishAsync("ClientMetadataCapture", context);

    [TestMethod]
    [DataRow(ClientCaptureProfiles.Configured, RequestHeaders, ResponseHeaders, ClientCaptureControl.Enabled, true, true, null)]
    [DataRow(ClientCaptureProfiles.Request, RequestHeaders, null, ClientCaptureControl.Enabled, true, false, null)]
    [DataRow(ClientCaptureProfiles.Response, null, ResponseHeaders, ClientCaptureControl.Enabled, false, true, null)]
    [DataRow(ClientCaptureProfiles.None, null, null, ClientCaptureControl.Enabled, false, false, null)]
    [DataRow(ClientCaptureProfiles.Empty, EmptyHeaders, EmptyHeaders, ClientCaptureControl.Enabled, false, false, null)]
    [DataRow(ClientCaptureProfiles.BinaryOnly, BinaryHeader, BinaryHeader, ClientCaptureControl.Enabled, false, false, null)]
    [DataRow(ClientCaptureProfiles.HttpOnly, null, null, ClientCaptureControl.Enabled, false, false, HttpHeader)]
    [DataRow(ClientCaptureProfiles.Disabled, RequestHeaders, ResponseHeaders, ClientCaptureControl.InstrumentationDisabled, false, false, null)]
    [DataRow(ClientCaptureProfiles.GlobalDisabled, RequestHeaders, ResponseHeaders, ClientCaptureControl.GlobalDisabled, false, false, null)]
    [DataRow(ClientCaptureProfiles.Unsampled, RequestHeaders, ResponseHeaders, ClientCaptureControl.Unsampled, false, false, null)]
    [DataRow(ClientCaptureProfiles.Suppressed, RequestHeaders, ResponseHeaders, ClientCaptureControl.Suppressed, false, false, null)]
    public async Task Published_native_grpc_capture_preserves_metadata_completion_and_controls(
        string profile, string? requestHeaders, string? responseHeaders,
        ClientCaptureControl control, bool requestCaptured, bool responseCaptured, string? httpRequestHeaders)
    {
        var environment = NativeCaptureFixture.CreateEnvironment(control, QylAutoInstrumentationIds.GrpcNetClient);
        if (requestHeaders is not null)
            environment[QylAutoInstrumentationOptions.GrpcRequestMetadataVariable] = requestHeaders;
        if (responseHeaders is not null)
            environment[QylAutoInstrumentationOptions.GrpcResponseMetadataVariable] = responseHeaders;
        if (httpRequestHeaders is not null)
            environment[QylAutoInstrumentationOptions.HttpClientRequestHeadersVariable] = httpRequestHeaders;
        Assert.IsNotNull(_fixture);
        await _fixture.RunAsync(new ClientCaptureScenario(profile, control, requestCaptured, responseCaptured), environment, testContext);
    }
}
