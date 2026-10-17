namespace Qyl.Build.Infrastructure;

/// <summary>A gate assertion that did not hold. The message is the whole report: what was expected,
/// what was found, and the output that shows it.</summary>
sealed class GateFailure(string message) : Exception(message);

static class Gate
{
    public static GateFailure Fail(string message) => new(message);

    public static void Require(bool condition, string message)
    {
        if (!condition)
            throw new GateFailure(message);
    }

    public static void Require(bool condition, Func<string> message)
    {
        if (!condition)
            throw new GateFailure(message());
    }
}
