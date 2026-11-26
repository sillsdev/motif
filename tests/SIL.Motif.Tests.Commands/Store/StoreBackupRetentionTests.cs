using SIL.Motif.Host.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>The cap on pre-upgrade backups: by count and by bytes, oldest first, never the newest or just-made one.</summary>
public sealed class StoreBackupRetentionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-backup-retention", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    private string Make(string stamp, int schema = 78, bool receipt = true, int bytes = 10, string reason = "pre-upgrade", string suffix = "")
    {
        var directory = Path.Combine(_root, $"{reason}-schema{schema}-{stamp}{suffix}");
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, "store.sqlite"), new byte[bytes]);
        if (receipt) File.WriteAllText(Path.Combine(directory, "motif-backup.json"), $"{{\"schemaVersion\":{schema}}}");
        return directory;
    }

    private static string Stamp(int n) => $"2026112{n}T000000000Z";

    private string[] Names() =>
        Directory.GetDirectories(_root).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray()!;

    [Fact]
    public void ACountOverTheCapEvictsTheOldestFirst()
    {
        var dirs = Enumerable.Range(1, 6).Select(n => Make(Stamp(n))).ToArray();
        var removed = StoreBackupRetention.Prune(_root, "pre-upgrade", dirs[5], 79, new StoreBackupRetentionPolicy(3, long.MaxValue));
        Assert.Equal(dirs.Take(3).Select(Path.GetFullPath), removed);
        Assert.Equal(dirs.Skip(3).Select(Path.GetFileName).Order(StringComparer.Ordinal), Names());
    }

    [Fact]
    public void TheDefaultKeepsTheLastThree()
    {
        var dirs = Enumerable.Range(1, 5).Select(n => Make(Stamp(n))).ToArray();
        StoreBackupRetention.Prune(_root, "pre-upgrade", dirs[4], 79);
        Assert.Equal(3, Names().Length);
        Assert.Equal(3, StoreBackupRetentionPolicy.Default.MaxCount);
    }

    [Fact]
    public void ABytesCapEvictsOldestFirstUntilItHolds()
    {
        var dirs = Enumerable.Range(1, 4).Select(n => Make(Stamp(n), bytes: 100)).ToArray();
        // Each folder holds 100 bytes of store and a receipt; the cap fits two.
        var perFolder = new DirectoryInfo(dirs[0]).EnumerateFiles().Sum(file => file.Length);
        var removed = StoreBackupRetention.Prune(_root, "pre-upgrade", dirs[3], 79, new StoreBackupRetentionPolicy(10, perFolder * 2 + 1));
        Assert.Equal(dirs.Take(2).Select(Path.GetFullPath), removed);
        Assert.Equal(2, Names().Length);
    }

    [Fact]
    public void TheJustMadeBackupIsKeptEvenWhenItIsTheOldestAndTheCapsAreTiny()
    {
        var made = Make(Stamp(1));
        Make(Stamp(2));
        Make(Stamp(3));
        StoreBackupRetention.Prune(_root, "pre-upgrade", made, 79, new StoreBackupRetentionPolicy(1, 1));
        // The just-made one and the newest verified one stay; the cap is exceeded rather than break either rule.
        Assert.Equal([Path.GetFileName(made), Path.GetFileName(Directory.GetDirectories(_root).Single(d => d.EndsWith(Stamp(3), StringComparison.Ordinal)))],
            Names().Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void TheNewestVerifiedBackupIsKeptEvenWhenNewerOnesLackAReceipt()
    {
        var verified = Make(Stamp(1));
        Make(Stamp(2), receipt: false);
        var made = Make(Stamp(3), receipt: false);
        StoreBackupRetention.Prune(_root, "pre-upgrade", made, 79, new StoreBackupRetentionPolicy(1, long.MaxValue));
        Assert.True(Directory.Exists(verified));
        Assert.True(Directory.Exists(made));
        Assert.Equal(2, Names().Length);
    }

    [Fact]
    public void ABackupOfANewerStoreIsNeverEvictedAndIsNotCounted()
    {
        var newer = Make(Stamp(1), schema: 90);
        var newerByReceipt = Make(Stamp(2), schema: 78);
        File.WriteAllText(Path.Combine(newerByReceipt, "motif-backup.json"), "{\"schemaVersion\":95}");
        var dirs = Enumerable.Range(3, 4).Select(n => Make(Stamp(n))).ToArray();
        StoreBackupRetention.Prune(_root, "pre-upgrade", dirs[^1], 79, new StoreBackupRetentionPolicy(2, 1));
        Assert.True(Directory.Exists(newer));
        Assert.True(Directory.Exists(newerByReceipt));
        // Of the four ordinary ones, the newest (just made and verified) is all the byte cap leaves.
        Assert.Equal(3, Names().Length);
    }

    [Fact]
    public void OtherKindsOfFolderAreNeverTouched()
    {
        var reset = Make(Stamp(1), reason: "pre-reset");
        var manual = Path.Combine(_root, "my-own-copy");
        Directory.CreateDirectory(manual);
        var restore = Make(Stamp(2), reason: "pre-restore");
        var dirs = Enumerable.Range(3, 4).Select(n => Make(Stamp(n))).ToArray();
        StoreBackupRetention.Prune(_root, "pre-upgrade", dirs[^1], 79, new StoreBackupRetentionPolicy(1, 1));
        Assert.True(Directory.Exists(reset));
        Assert.True(Directory.Exists(manual));
        Assert.True(Directory.Exists(restore));
        Assert.Equal(1, Names().Count(name => name!.StartsWith("pre-upgrade", StringComparison.Ordinal)));
    }

    [Fact]
    public void TheSameMillisecondSuffixOrdersAfterTheBaseName()
    {
        var first = Make(Stamp(1));
        var second = Make(Stamp(1), suffix: "-2");
        var third = Make(Stamp(1), suffix: "-3");
        StoreBackupRetention.Prune(_root, "pre-upgrade", third, 79, new StoreBackupRetentionPolicy(2, long.MaxValue));
        Assert.False(Directory.Exists(first));
        Assert.True(Directory.Exists(second));
        Assert.True(Directory.Exists(third));
    }

    [Theory]
    [InlineData("pre-upgrade-schema99999999999-20261101T000000000Z")]
    [InlineData("pre-upgrade-schema\u0661-20261101T000000000Z")]
    [InlineData("pre-upgrade-schema1-20261101T000000000Z-99999999999")]
    [InlineData("pre-upgrade-schema1-20261101T000000000Z\n")]
    public void ANameThatIsNotExactlyOursIsNeverTouchedAndNeverThrows(string name)
    {
        if (name.EndsWith('\n') && OperatingSystem.IsWindows()) return;
        var odd = Path.Combine(_root, name);
        Directory.CreateDirectory(odd);
        var dirs = Enumerable.Range(1, 4).Select(n => Make(Stamp(n))).ToArray();
        StoreBackupRetention.Prune(_root, "pre-upgrade", dirs[^1], 79, new StoreBackupRetentionPolicy(1, long.MaxValue));
        Assert.True(Directory.Exists(odd));
        Assert.Equal(1, Names().Count(n => n!.StartsWith("pre-upgrade-schema78", StringComparison.Ordinal)));
    }

    [Fact]
    public void SiblingBakFilesCountTowardTheSameCapOldestFirstAndNeverBeatTheNewestFolder()
    {
        var store = Path.Combine(_root, "project.motif.db");
        var bak1 = store + ".schema60.bak";
        var bak2 = store + ".schema70.bak2";
        File.WriteAllBytes(bak1, new byte[10]);
        File.WriteAllBytes(bak2, new byte[10]);
        File.WriteAllBytes(bak2 + "-wal", new byte[10]);
        File.SetLastWriteTimeUtc(bak1, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(bak2, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        var newerBak = store + ".schema500.bak";
        File.WriteAllBytes(newerBak, new byte[10]);
        var folders = new[] { Make("20261103T000000000Z"), Make("20261104T000000000Z") };
        var removed = StoreBackupRetention.Prune(_root, "pre-upgrade", folders[1], 79,
            new StoreBackupRetentionPolicy(3, long.MaxValue), store);
        Assert.Equal([Path.GetFullPath(bak1)], removed);
        Assert.False(File.Exists(bak1));
        Assert.True(File.Exists(bak2));
        Assert.True(File.Exists(newerBak));

        // A tighter cap evicts the other .bak next, and never a folder that is the newest verified one.
        StoreBackupRetention.Prune(_root, "pre-upgrade", folders[1], 79, new StoreBackupRetentionPolicy(1, long.MaxValue), store);
        Assert.False(File.Exists(bak2));
        Assert.False(File.Exists(bak2 + "-wal"));
        Assert.True(Directory.Exists(folders[1]));
        Assert.False(Directory.Exists(folders[0]));
    }

    [Fact]
    public void AbsentRootIsNothingToDo() =>
        Assert.Empty(StoreBackupRetention.Prune(Path.Combine(_root, "none"), "pre-upgrade", null, 79));

    [Fact]
    public void UnderTheCapsNothingIsRemoved()
    {
        Enumerable.Range(1, 3).ToList().ForEach(n => Make(Stamp(n)));
        Assert.Empty(StoreBackupRetention.Prune(_root, "pre-upgrade", null, 79));
        Assert.Equal(3, Names().Length);
    }
}

/// <summary>A real project-store upgrade prunes its older pre-upgrade backups after it succeeds.</summary>
public sealed class ProjectUpgradeBackupRetentionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-upgrade-retention", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    [Fact]
    public void AnUpgradeKeepsTheLastThreeBackupsAndTheOneItMade()
    {
        Directory.CreateDirectory(_root);
        var project = new SIL.Motif.Contract.Projects.ProjectLocator(Path.Combine(_root, "project.fwdata"), "fixture-project");
        var store = Path.Combine(_root, "project.motif.db");
        StoreSchemaFixtures.Create(store, project, 78);
        var backups = Path.Combine(_root, "project.motif-backups");
        var older = Enumerable.Range(1, 4).Select(n => Path.Combine(backups, $"pre-upgrade-schema70-2026010{n}T000000000Z")).ToArray();
        foreach (var directory in older)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "motif-backup.json"), "{\"schemaVersion\":70}");
        }
        var newer = Path.Combine(backups, "pre-upgrade-schema500-20250101T000000000Z");
        Directory.CreateDirectory(newer);

        using (MotifDatabase.OpenOwned(store, project, MotifSchema.CurrentSchema, new Version(1, 0))) { }

        var kept = Directory.GetDirectories(backups).Select(Path.GetFileName).ToHashSet();
        Assert.DoesNotContain(Path.GetFileName(older[0]), kept);
        Assert.DoesNotContain(Path.GetFileName(older[1]), kept);
        Assert.Contains(Path.GetFileName(older[2]), kept);
        Assert.Contains(Path.GetFileName(older[3]), kept);
        Assert.Contains(Path.GetFileName(newer), kept);
        Assert.Contains(kept, name => name!.StartsWith("pre-upgrade-schema78-", StringComparison.Ordinal));
        Assert.Equal(4, kept.Count);
        // The original is in the verified backup folder; no .bak copy is left beside the store.
        Assert.Empty(Directory.GetFiles(_root, "*.bak*"));
    }
}
