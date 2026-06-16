using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

#if USE_COMPILED_MODEL
using Qyl.RealEfCoreDemo.CompiledModels;
#endif

namespace Qyl.RealEfCoreDemo;

public sealed partial class ProbeContext : DbContext
{
    private readonly DbConnection _connection;

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "EF Core DbContext ctors are RequiresUnreferencedCode; not trim-safe by design. Demo only.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "EF Core DbContext ctors are RequiresDynamicCode; not AOT-safe by design. Demo only.")]
    public ProbeContext(DbConnection connection) => _connection = connection;

    public DbSet<ProbeItem> Items => Set<ProbeItem>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
#if USE_COMPILED_MODEL
        optionsBuilder.UseModel(ProbeContextModel.Instance);
#endif
        optionsBuilder.UseSqlite(_connection);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProbeItem>(static entity =>
        {
            entity.ToTable("Items");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Name).IsRequired();
        });
    }
}

public sealed class ProbeItem
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}
