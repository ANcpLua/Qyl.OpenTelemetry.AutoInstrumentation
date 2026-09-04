using System.Net.Http;

// A call site that names a declared integration receiver and method but does not fit the declared
// shape. QylHttpClient derives from System.Net.Http.HttpClient, so the receiver matches, and the
// method name GetAsync matches the HttpClient intercept declaration -- but the signature returns
// Task<string> from an int, which the HttpClient shape does not describe.
//
// The generator must emit NO interceptor for it and report exactly one QYL1001 instead. This is the
// failure mode a library major introduces when it changes an intercepted signature: silence here
// would mean instrumentation disappearing with no signal, and emitting an interceptor anyway would
// break the consumer's build.
internal static class ShapeMismatchProbe
{
    internal static Task<string> UnmatchedAsync(QylUnmatchedClient client)
        => client.GetAsync(1);
}

internal sealed class QylUnmatchedClient : HttpClient
{
    internal Task<string> GetAsync(int id)
        => Task.FromResult(id.ToString(System.Globalization.CultureInfo.InvariantCulture));
}
