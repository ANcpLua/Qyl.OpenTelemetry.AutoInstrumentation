using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Design;

namespace Qyl.RealEfCoreDemo;

public sealed class ProbeContextFactory : IDesignTimeDbContextFactory<ProbeContext>
{
    public ProbeContext CreateDbContext(string[] args)
        => new(new SqliteConnection("Data Source=probe.db"));
}
