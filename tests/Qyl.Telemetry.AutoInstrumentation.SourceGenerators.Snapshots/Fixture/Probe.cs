using System.Net.Http;

// A SECOND interceptable call site in a SECOND file. This is what makes the snapshot pin the
// determinism fix: with two HttpClient.GetAsync sites across two files, the emission order and the
// _N interceptor-name indices are decided by the OrderBy(Location.Data) sort, not by Roslyn's
// cross-tree visitation order. Drop that sort and this snapshot's byte-compare flips red.
internal static class Probe
{
    internal static async Task EmitAsync(HttpClient client)
    {
        using (await client.GetAsync("http://qyl.invalid/probe"))
        {
        }
    }
}
