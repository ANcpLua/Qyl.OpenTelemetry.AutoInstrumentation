// netstandard2.0 has no IsExternalInit; records/init require it. The production generator gets this
// from ANcpLua.Roslyn.Utilities.Sources — the isolated experiment supplies it directly.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
