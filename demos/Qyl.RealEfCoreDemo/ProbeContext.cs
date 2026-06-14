using System.Data.Common;
using Microsoft.EntityFrameworkCore;

#if USE_COMPILED_MODEL
using Qyl.RealEfCoreDemo.CompiledModels;
#endif

namespace Qyl.RealEfCoreDemo;

public sealed partial class ProbeContext(DbConnection connection) : DbContext
{
    public DbSet<ProbeItem> Items => Set<ProbeItem>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
#if USE_COMPILED_MODEL
        optionsBuilder.UseModel(ProbeContextModel.Instance);
#endif
        optionsBuilder.UseSqlite(connection);
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
