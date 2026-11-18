using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Options;
using SIL.Harmony.Changes;
using SIL.Harmony.Config;
using SIL.Harmony.Db;
using SIL.Harmony.Entities;
using SIL.Harmony.Core;

namespace SIL.Motif.RecordStore;

internal sealed class RecordDatabase(DbContextOptions<RecordDatabase> options, IOptions<HarmonyConfig> config,
    RecordContent content) : DbContext(options), ICrdtDbContext
{
    private readonly RecordCodec codec = new(content);

    protected override void ConfigureConventions(ModelConfigurationBuilder builder) =>
        builder.Properties<Guid>().HaveConversion<NetworkGuidConverter>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.UseCrdt(config.Value);
        builder.Entity<StoreState>().ToTable("RecordStoreState").HasKey(x => x.Id);
        builder.Entity<StoreState>().Property(x => x.Id).ValueGeneratedNever();
        builder.Entity<ContentRow>().ToTable("RecordContent").HasKey(x => x.Id);
        builder.Entity<ContentRow>().Property(x => x.Id).ValueGeneratedNever();
        builder.Entity<ObjectSnapshot>().Property(x => x.Entity).HasColumnType("blob")
            .HasConversion(x => codec.Snapshot(x), x => codec.Snapshot(x));
        builder.Entity<ChangeEntity<IChange>>().Property(x => x.Change).HasColumnType("blob")
            .HasConversion(x => codec.Change(x), x => codec.Change(x));
    }

    private void PrepareContent()
    {
        foreach (var entry in ChangeTracker.Entries<ObjectSnapshot>().Where(e => e.State is EntityState.Added or EntityState.Modified))
            codec.Snapshot(entry.Entity.Entity);
        foreach (var entry in ChangeTracker.Entries<ChangeEntity<IChange>>().Where(e => e.State is EntityState.Added or EntityState.Modified))
            codec.Change(entry.Entity.Change);
        foreach (var row in content.Pending)
            if (Entry(row).State == EntityState.Detached) Add(row);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        PrepareContent();
        var count = base.SaveChanges(acceptAllChangesOnSuccess);
        content.Saved();
        return count;
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        PrepareContent();
        var count = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        content.Saved();
        return count;
    }
}

internal sealed class NetworkGuidConverter() : ValueConverter<Guid, byte[]>(
    value => Write(value), value => Read(value))
{
    private static Guid Read(byte[] value) => new(value.AsSpan(), bigEndian: true);
    internal static byte[] Write(Guid value)
    {
        var bytes = new byte[16];
        value.TryWriteBytes(bytes, bigEndian: true, out _);
        return bytes;
    }
}
