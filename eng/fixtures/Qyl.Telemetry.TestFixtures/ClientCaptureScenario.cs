namespace Qyl.Telemetry.TestFixtures;

public enum ClientCaptureControl
{
    Enabled,
    InstrumentationDisabled,
    GlobalDisabled,
    Unsampled,
    Suppressed
}

/// <summary>Explicit expected outcomes passed from a native test row to its child process.</summary>
public sealed record ClientCaptureScenario(
    string Profile,
    ClientCaptureControl Control,
    bool RequestCaptured,
    bool ResponseCaptured)
{
    public bool EmitsSpans => Control is ClientCaptureControl.Enabled;

    public string[] ToArguments() =>
        [Profile, Control.ToString(), RequestCaptured.ToString(), ResponseCaptured.ToString()];

    public static ClientCaptureScenario Parse(string[] arguments)
    {
        if (arguments is not [var profile, var control, var request, var response])
            throw new ArgumentException("Expected profile, control, request capture and response capture arguments.", nameof(arguments));
        if (!Enum.TryParse<ClientCaptureControl>(control, out var parsedControl) || !Enum.IsDefined(parsedControl) ||
            !bool.TryParse(request, out var captureRequest) || !bool.TryParse(response, out var captureResponse))
            throw new ArgumentException("Invalid capture control or expected capture flags.", nameof(arguments));
        return new ClientCaptureScenario(profile, parsedControl, captureRequest, captureResponse);
    }
}
