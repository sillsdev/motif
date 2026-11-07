using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Store;

/// <summary>The per-user data folder loses what no live store references and keeps what one does.</summary>
public sealed class UserDataSweepTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-user-data-" + Guid.NewGuid().ToString("N"));
    private readonly MachineDatabase _machine;
    private readonly KnownProjectRegistry _registry;

    public UserDataSweepTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "data"));
        _machine = MachineDatabase.Open(Path.Combine(_root, "data"));
        _registry = new KnownProjectRegistry(_machine);
    }

    private string Data => Path.Combine(_root, "data");

    [Theory]
    [InlineData(".fwdata")]
    [InlineData(".json")]
    public void ALeasedSourceSurvivesBudgetSweepUntilItsLeaseIsReleased(string extension)
    {
        var directory = Path.Combine(Data, "assessment-sources");
        Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, new string('b', 64) + extension);
        File.WriteAllText(source, "leased evidence");
        File.SetLastWriteTimeUtc(source, Now.UtcDateTime.AddDays(-30));

        using (var lease = StatsCacheLock.AcquireForFile(source))
        {
            Sweep(new UserDataSweepOptions { BudgetBytes = 1 });
            Assert.True(File.Exists(source));
        }

        Sweep(new UserDataSweepOptions { BudgetBytes = 1 });
        Assert.False(File.Exists(source));
    }

    [Fact]
    public void AReferencedSharedSnapshotSurvivesAndAnOrphanIsCollected()
    {
        var keptDigest = new string('a', 64);
        var goneDigest = new string('b', 64);
        AddLiveProject("snapshot", "run-snapshot-0001", baselineDigest: keptDigest);
        var directory = Path.Combine(Data, "assessment-sources");
        Directory.CreateDirectory(directory);
        var kept = Path.Combine(directory, keptDigest + ".json");
        var gone = Path.Combine(directory, goneDigest + ".json");
        File.WriteAllText(kept, "retained snapshot");
        File.WriteAllText(gone, "unreferenced snapshot");
        File.SetLastWriteTimeUtc(kept, Now.UtcDateTime.AddDays(-30));
        File.SetLastWriteTimeUtc(gone, Now.UtcDateTime.AddDays(-30));

        var result = Sweep(new UserDataSweepOptions { BudgetBytes = 1 });

        Assert.True(File.Exists(kept));
        Assert.False(File.Exists(gone));
        Assert.Equal(1, result.SourcesRemoved);
    }

    [Fact]
    public async Task SharedSourcesCanBeReadConcurrentlyWhileSweepWaits()
    {
        var directory = Path.Combine(Data, "assessment-sources");
        Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, new string('c', 64) + ".fwdata");
        File.WriteAllText(source, "shared evidence");
        File.SetLastWriteTimeUtc(source, Now.UtcDateTime.AddDays(-30));
        using var first = StatsCacheLock.AcquireForFile(source);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        using var second = await Task.Run(() => StatsCacheLock.AcquireForFile(source, timeout.Token));
        Sweep(new UserDataSweepOptions { BudgetBytes = 1 });

        Assert.True(File.Exists(source));
    }

    [Fact]
    public void AnOrphanOlderThanTheMinimumAgeGoesAndAReferencedRunStays()
    {
        var store = AddLiveProject("live", "run-referenced-0001");
        var kept = Run("run-referenced-0001", 30);
        var orphan = Run("run-orphan-aaaa-0002", 30);
        var fresh = Run("run-fresh-bbbb-0003", 1);

        var result = Sweep();

        Assert.True(Directory.Exists(kept));
        Assert.False(Directory.Exists(orphan));
        Assert.True(Directory.Exists(fresh));
        Assert.Equal(1, result.RunsRemoved);
        Assert.True(result.BytesRemoved > 0);
        Assert.True(File.Exists(store));
    }

    [Fact]
    public void ARunWhoseOwnerStoreIsGoneGoesEvenWhenItsMarkerIsTheOnlyHint()
    {
        AddLiveProject("live", "run-referenced-0001");
        Run("run-referenced-0001", 30);
        var owned = Run("run-owned-gone-0004", 30);
        File.WriteAllText(owned + ".owner", Path.Combine(_root, "deleted", "x.motif.db"));

        var result = Sweep();

        Assert.False(Directory.Exists(owned));
        Assert.False(File.Exists(owned + ".owner"));
        Assert.Equal(1, result.RunsRemoved);
    }

    [Fact]
    public void AnUnreferencedSourceAndBaselineGoOnlyUnderTheBudgetAndNeverAReferencedOne()
    {
        var digest = new string('a', 64);
        var orphanDigest = new string('b', 64);
        AddLiveProject("live", "run-referenced-0001", baselineDigest: digest);
        Run("run-referenced-0001", 30);
        Directory.CreateDirectory(Path.Combine(Data, "baselines", digest));
        File.WriteAllText(Path.Combine(Data, "baselines", digest, "x.fwdata"), "kept");
        Directory.CreateDirectory(Path.Combine(Data, "baselines", orphanDigest));
        File.WriteAllText(Path.Combine(Data, "baselines", orphanDigest, "x.fwdata"), "gone");
        Directory.CreateDirectory(Path.Combine(Data, "assessment-sources"));
        var source = Path.Combine(Data, "assessment-sources", orphanDigest + ".fwdata");
        File.WriteAllText(source, "gone");
        Age(Path.Combine(Data, "baselines", digest), 30);
        Age(Path.Combine(Data, "baselines", orphanDigest), 30);
        File.SetLastWriteTimeUtc(source, Now.UtcDateTime.AddDays(-30));

        var idle = Sweep();
        Assert.Equal(0, idle.BaselinesRemoved + idle.SourcesRemoved);

        var result = Sweep(new UserDataSweepOptions { BudgetBytes = 10 });

        Assert.True(Directory.Exists(Path.Combine(Data, "baselines", digest)));
        Assert.False(Directory.Exists(Path.Combine(Data, "baselines", orphanDigest)));
        Assert.False(File.Exists(source));
        Assert.Equal(1, result.BaselinesRemoved);
        Assert.Equal(1, result.SourcesRemoved);
    }

    [Fact]
    public void AProjectLastUsedLongAgoOnAnUnmountedDriveLosesNothingUntilItsFolderHasBeenMissingForTheGrace()
    {
        _registry.Record("dormant", Path.Combine(_root, "unmounted", "p.fwdata"), Now.AddDays(-45));
        var baseline = Path.Combine(Data, "baselines", new string('f', 64));
        Directory.CreateDirectory(baseline);
        File.WriteAllText(Path.Combine(baseline, "x.fwdata"), "kept");
        Age(baseline, 90);
        var run = Run("run-dormant-0016", 90);
        var tight = new UserDataSweepOptions { BudgetBytes = 1 };

        var first = Sweep(tight);
        var later = SweepAt(Now.AddDays(29), tight);

        Assert.Equal(0, first.ProjectsForgotten + first.BaselinesRemoved + first.RunsRemoved);
        Assert.Equal(0, later.ProjectsForgotten + later.BaselinesRemoved + later.RunsRemoved);
        Assert.NotNull(_registry.Find("dormant"));
        Assert.True(Directory.Exists(baseline));
        Assert.True(Directory.Exists(run));

        var forgotten = SweepAt(Now.AddDays(31), tight);

        Assert.Equal(1, forgotten.ProjectsForgotten);
        Assert.Null(_registry.Find("dormant"));
        Assert.False(Directory.Exists(run));
        Assert.False(Directory.Exists(baseline));
    }

    [Fact]
    public void WhenTheDriveReturnsTheMissingSinceClockStartsOver()
    {
        var folder = Path.Combine(_root, "flaky");
        var fwdata = Path.Combine(folder, "p.fwdata");
        _registry.Record("flaky", fwdata, Now.AddDays(-45));

        Sweep();
        Directory.CreateDirectory(folder);
        Sweep();
        Directory.Delete(folder);
        var result = SweepAt(Now.AddDays(40));

        Assert.Equal(0, result.ProjectsForgotten);
        Assert.NotNull(_registry.Find("flaky"));
    }

    [Fact]
    public void AProjectOnAnAbsentDriveKeepsEveryUnownedEntryUntilItsFolderHasBeenGoneForTheGrace()
    {
        _registry.Record("absent", Path.Combine(_root, "unmounted", "p.fwdata"), Now.AddDays(-2));
        var digest = new string('c', 64);
        var baseline = Path.Combine(Data, "baselines", digest);
        Directory.CreateDirectory(baseline);
        File.WriteAllText(Path.Combine(baseline, "x.fwdata"), "kept");
        Age(baseline, 90);
        var run = Run("run-absent-owner-0010", 90);
        File.WriteAllText(run + ".owner", Path.Combine(_root, "unmounted", "p.motif.db"));
        var young = Run("run-absent-owner-0011", 5);
        File.WriteAllText(young + ".owner", Path.Combine(_root, "unmounted", "p.motif.db"));

        var result = Sweep();

        Assert.True(Directory.Exists(baseline));
        Assert.Equal(1, result.UnreadableStores);
        Assert.Equal(0, result.BaselinesRemoved);
        Assert.True(Directory.Exists(run));
        Assert.True(Directory.Exists(young));
    }

    [Fact]
    public void AStoreOutsideKnownProjectsIsReadThroughItsRunMarkerAndKeepsWhatItNames()
    {
        var digest = new string('d', 64);
        var store = AddLiveProject("stranger", "run-stranger-0012", baselineDigest: digest);
        _registry.Forget(_registry.List().Single(project => project.FullFwDataPath.Contains("stranger")).WorkspaceKey);
        var run = Run("run-stranger-0012", 30);
        File.WriteAllText(run + ".owner", store);
        var baseline = Path.Combine(Data, "baselines", digest);
        Directory.CreateDirectory(baseline);
        File.WriteAllText(Path.Combine(baseline, "x.fwdata"), "kept");
        Age(baseline, 30);

        var result = Sweep();

        Assert.True(Directory.Exists(run));
        Assert.True(Directory.Exists(baseline));
        Assert.Equal(0, result.RunsRemoved + result.BaselinesRemoved);
    }

    [Fact]
    public void AGarbledOwnerMarkerKeepsItsRunAndDoesNotStopTheSweep()
    {
        var run = Run("run-garbled-0013", 90);
        File.WriteAllText(run + ".owner", "bad\0path");
        var orphan = Run("run-orphan-0014", 90);

        var result = Sweep();

        Assert.True(Directory.Exists(run));
        Assert.False(Directory.Exists(orphan));
        Assert.Equal(1, result.RunsRemoved);
    }

    [Theory]
    [InlineData("Proposals", "DraftJson")]
    [InlineData("Proposals", "AnchorJson")]
    [InlineData("ProposalRevisions", "ProposalJson")]
    [InlineData("Receipts", "ReceiptJson")]
    [InlineData("AppliedIndex", "RecordJson")]
    public void ABaselineNamedOnlyByProposalMaterialIsKept(string table, string column)
    {
        var digest = new string('e', 64);
        var store = AddLiveProject("proposal-" + table.ToLowerInvariant(), "run-live-0015");
        using (var connection = new SqliteConnection("Data Source=" + store + ";Pooling=False"))
        {
            connection.Open();
            InsertRow(connection, table, column, "{\"baseline\":\"" + digest + "\"}");
        }
        var baseline = Path.Combine(Data, "baselines", digest);
        Directory.CreateDirectory(baseline);
        File.WriteAllText(Path.Combine(baseline, "x.fwdata"), "kept");
        Age(baseline, 90);

        Sweep();

        Assert.True(Directory.Exists(baseline));
    }

    [Fact]
    public void CompactionIsStampedOnlyOnceAnOpenStoreWasVisited()
    {
        var store = AddLiveProject("compact", "run-compact-0017");
        var stamp = Path.Combine(Data, "store-compaction-stamp");

        SIL.Motif.Worker.WorkerRuntime.CompactOpenStoresWhenDue(Data, _registry, _ => null, CancellationToken.None);
        Assert.False(File.Exists(stamp));

        using var database = MotifDatabase.OpenOwned(store, new ProjectLocator(Path.Combine(_root, "compact", "compact.fwdata"), "compact"),
            MotifSchema.CurrentSchema, new Version(1, 0));
        SIL.Motif.Worker.WorkerRuntime.CompactOpenStoresWhenDue(Data, _registry, _ => database, CancellationToken.None);
        Assert.True(File.Exists(stamp));
    }

    private static void InsertRow(SqliteConnection connection, string table, string column, string value)
    {
        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys = OFF;";
            pragma.ExecuteNonQuery();
        }
        var columns = new List<string>();
        var values = new List<string>();
        using (var info = connection.CreateCommand())
        {
            info.CommandText = $"PRAGMA table_info({table});";
            using var reader = info.ExecuteReader();
            while (reader.Read())
            {
                var name = reader.GetString(1);
                if (name == column) { columns.Add(name); values.Add("$value"); continue; }
                if (reader.GetInt32(3) == 0 || !reader.IsDBNull(4)) continue;
                columns.Add(name);
                values.Add(reader.GetString(2).Contains("INT", StringComparison.OrdinalIgnoreCase) ? "0" : "'x" + columns.Count + "'");
            }
        }
        using var insert = connection.CreateCommand();
        insert.CommandText = $"INSERT INTO {table} ({string.Join(",", columns)}) VALUES ({string.Join(",", values)});";
        insert.Parameters.AddWithValue("$value", value);
        insert.ExecuteNonQuery();
    }

    [Fact]
    public void AnUnreadableLiveStoreKeepsEveryEntryThatHasNoMissingOwner()
    {
        var path = Path.Combine(_root, "old", "old.fwdata");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "fwdata");
        File.WriteAllText(Path.ChangeExtension(path, ".motif.db"), "not a motif store");
        _registry.Record("old", path, Now);
        var unknown = Run("run-unknown-dddd-0006", 30);

        var result = Sweep();

        Assert.True(Directory.Exists(unknown));
        Assert.Equal(1, result.UnreadableStores);
        Assert.Equal(0, result.RunsRemoved);
    }

    [Fact]
    public void WhenOverBudgetTheOldestUnreferencedEntriesGoFirstButNeverTheYoungerThanADay()
    {
        var oldest = Run("run-oldest-eeee-0007", 3, 4000);
        var middle = Run("run-middle-ffff-0008", 2, 4000);
        var youngest = Run("run-youngest-gggg-0009", 0, 4000);

        var result = Sweep(new UserDataSweepOptions { BudgetBytes = 5000 });

        Assert.False(Directory.Exists(oldest));
        Assert.False(Directory.Exists(middle));
        Assert.True(Directory.Exists(youngest));
        Assert.Equal(2, result.RunsRemoved);
        Assert.Equal(4000, result.BytesKept);
    }

    private UserDataSweepResult Sweep(UserDataSweepOptions? options = null) => SweepAt(Now, options);

    private UserDataSweepResult SweepAt(DateTimeOffset when, UserDataSweepOptions? options = null) =>
        new UserDataSweep(Data, _registry, new FixedClock(when), options).Sweep();

    private string Run(string name, int ageDays, int bytes = 100)
    {
        var directory = Path.Combine(Data, "assessment-runs", name);
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, "stats.sqlite"), new byte[bytes]);
        Age(directory, ageDays);
        return directory;
    }

    private static void Age(string directory, int days)
    {
        var when = Now.UtcDateTime.AddDays(-days).AddHours(-1);
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)) File.SetLastWriteTimeUtc(file, when);
        Directory.SetLastWriteTimeUtc(directory, when);
    }

    private string AddLiveProject(string name, string invocationId, string? baselineDigest = null)
    {
        var directory = Path.Combine(_root, name);
        Directory.CreateDirectory(directory);
        var fwdata = Path.Combine(directory, name + ".fwdata");
        File.WriteAllText(fwdata, "fwdata");
        var storePath = Path.ChangeExtension(fwdata, ".motif.db");
        using (var database = MotifDatabase.OpenOwned(storePath, new ProjectLocator(fwdata, name),
                   MotifSchema.CurrentSchema, new Version(1, 0)))
        using (var connection = database.OpenConnection())
        {
            new AssessmentRepository(database).Record(new NewAssessmentRecord(
                "assessment-1", null, null, "pangloss", "ParseTime", "{}", "scope",
                new AssessmentSubject.Words(Selection.Create("fixture", ["word"]), "none", "1"), "{}", "outcome",
                "semantic", "grammar", "model", "pipeline", 0,
                new AssessmentRecordMaterial.Words([new AssessedWord("word", "complete", [])], null, null, []),
                "2026-01-01T00:00:00Z"));
            if (baselineDigest is not null) Reference(connection, baselineDigest);
            Reference(connection, invocationId);
        }
        _registry.Record(name, fwdata, Now);
        return storePath;
    }

    // Names a run or Baseline the way a job's stored input names a path, so only the digest or id is what matches.
    private static void Reference(SqliteConnection connection, string token)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO Jobs (JobId, ProjectKey, Kind, Status, LineageId, InputJson, CreatedUtc, UpdatedUtc) " +
            "VALUES ($id, 'k', 'trial', 'succeeded', $id, $input, 'now', 'now');";
        command.Parameters.AddWithValue("$id", "job-" + token[..8] + Guid.NewGuid().ToString("N")[..6]);
        command.Parameters.AddWithValue("$input", "{\"path\":\"/data/x/" + token + "/stats.sqlite\"}");
        command.ExecuteNonQuery();
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    public void Dispose()
    {
        _machine.Dispose();
        try { Directory.Delete(_root, recursive: true); }
        catch (DirectoryNotFoundException) { }
    }
}
