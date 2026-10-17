namespace Qyl.Telemetry.AutoInstrumentation.GeneratedCode;

/// <summary>
/// The consuming build's version, handed over by the module initializer of the generated
/// <c>QylConsumerBuild</c> in the application's own assembly. <c>AddQyl</c> stamps it as
/// <c>service.version</c> unless the application sets one; no reflection reads it.
/// </summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public static class QylConsumerBuildInfo
{
    private static string? _version;

    /// <summary>Registers the consuming build's version. The entry assembly's module initializer runs
    /// before <c>Main</c>, so the first registration wins.</summary>
    /// <param name="version">The build's informational version.</param>
    [JetBrains.Annotations.UsedImplicitly]
    public static void Register(string version)
    {
        if (!string.IsNullOrWhiteSpace(version))
            Interlocked.CompareExchange(ref _version, version, null);
    }

    /// <summary>The registered version, or <see langword="null"/> when no consuming build registered one.</summary>
    internal static string? Version => Volatile.Read(ref _version);
}
