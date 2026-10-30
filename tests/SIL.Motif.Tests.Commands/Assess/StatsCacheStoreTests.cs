using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Assess;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Assess;

public sealed class StatsCacheStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SIL.Motif.Tests.CommandScratch", Guid.NewGuid().ToString("N"));
    private readonly StatsCacheStore _store;
    private readonly MotifDatabase _database;
    private readonly ProjectLocator _project;

    public StatsCacheStoreTests()
    {
        Directory.CreateDirectory(_root);
        var projectRoot = Path.Combine(_root, "project");
        Directory.CreateDirectory(projectRoot);
        _project = new ProjectLocator(Path.Combine(projectRoot, "project.fwdata"), "project");
        _database = MotifDatabase.OpenOwned(Path.Combine(projectRoot, "project.motif.db"), _project,
            MotifSchema.CurrentSchema, new Version(1, 0));
        _store = new StatsCacheStore(WorkspaceOwnership.Bootstrap(_root), _database.FullPath);
    }

    [Fact]
    public void InvocationIdentityDeterminesTheDirectoryWithoutCreatingIt()
    {
        var first = _store.DirectoryFor("invocation-one");
        Assert.Equal(first, _store.DirectoryFor("invocation-one"));
        Assert.NotEqual(first, _store.DirectoryFor("invocation-two"));
        Assert.Equal(Path.Combine(_root, "assessment-runs", "invocation-one"), first);
        Assert.True(Directory.Exists(Path.GetDirectoryName(first)));
        Assert.False(Directory.Exists(first));
    }

    [Fact]
    public void ExistingUnownedDirectoryCannotBeClaimedByAReservation()
    {
        var directory = Path.Combine(_root, "assessment-runs", "unowned");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "source.fwdata"), "keep");
        Assert.Throws<InvalidOperationException>(() => _store.DirectoryFor("unowned"));
        Assert.False(File.Exists(directory + ".owner"));
        Prune();
        Assert.True(File.Exists(Path.Combine(directory, "source.fwdata")));
    }

    [Fact]
    public void PruneDeletesAnExpiredNonemptyDirectory()
    {
        var old = _store.DirectoryFor("old");
        var fresh = _store.DirectoryFor("fresh");
        Directory.CreateDirectory(old);
        Directory.CreateDirectory(fresh);
        File.WriteAllText(Path.Combine(old, "stats.sqlite"), "old");
        File.WriteAllText(Path.Combine(old, "source.fwdata"), "source");
        File.WriteAllText(Path.Combine(old, "retry-stats.sqlite"), "retry");
        Directory.CreateDirectory(Path.Combine(old, "nested"));
        File.WriteAllText(Path.Combine(old, "nested", "other"), "other");
        File.WriteAllText(Path.Combine(fresh, "stats.sqlite"), "fresh");
        File.SetLastWriteTimeUtc(Path.Combine(old, "stats.sqlite"), DateTime.UtcNow.AddDays(-31));
        Directory.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-31));

        Prune();

        Assert.False(Directory.Exists(old));
        Assert.True(File.Exists(Path.Combine(fresh, "stats.sqlite")));
    }

    [Fact]
    public void ExpiredCacheWithAnActiveReaderOrWriterIsPreserved()
    {
        var directory = _store.DirectoryFor("busy");
        Directory.CreateDirectory(directory);
        var cache = Path.Combine(directory, "stats.sqlite");
        File.WriteAllText(cache, "busy");
        File.SetLastWriteTimeUtc(cache, DateTime.UtcNow.AddDays(-31));
        Directory.SetLastWriteTimeUtc(directory, DateTime.UtcNow.AddDays(-31));
        using var lease = StatsCacheLock.Create(directory);
        Assert.True(lease.TryAcquire());

        Prune();

        Assert.True(File.Exists(cache));
    }

    [Fact]
    public void ExpiredCachesAndPreviouslyEmptyDirectoriesAreRemovedWithoutRemovingOtherArtifacts()
    {
        var cacheOnly = _store.DirectoryFor("cache-only");
        var empty = _store.DirectoryFor("empty");
        Directory.CreateDirectory(cacheOnly);
        Directory.CreateDirectory(empty);
        File.WriteAllText(Path.Combine(cacheOnly, "stats.sqlite"), "cache");
        File.SetLastWriteTimeUtc(Path.Combine(cacheOnly, "stats.sqlite"), DateTime.UtcNow.AddDays(-31));
        Directory.SetLastWriteTimeUtc(cacheOnly, DateTime.UtcNow.AddDays(-31));
        Directory.SetLastWriteTimeUtc(empty, DateTime.UtcNow.AddDays(-31));

        Prune();

        Assert.False(Directory.Exists(cacheOnly));
        Assert.False(Directory.Exists(empty));
    }

    [Fact]
    public void RetainedAssessmentProtectsItsEntireInvocationUntilItIsPruned()
    {
        var directory = _store.DirectoryFor("cited");
        Directory.CreateDirectory(directory);
        var cache = Path.Combine(directory, "stats.sqlite");
        File.WriteAllText(cache, "cache");
        File.WriteAllText(Path.Combine(directory, "source.fwdata"), "source");
        File.WriteAllText(Path.Combine(directory, "retry-stats.sqlite"), "retry");
        var retryDirectory = _store.DirectoryFor("retry");
        Directory.CreateDirectory(retryDirectory);
        File.WriteAllText(Path.Combine(retryDirectory, "source.fwdata"), "source");
        File.WriteAllText(Path.Combine(retryDirectory, "retry-stats.sqlite"), "retry");
        var invocation = new BatchInvocationEvidence("cited", Path.Combine(directory, "source.fwdata"),
            "sha256:source", "sha256:executable", "words.txt", "sha256:words", "rows.tsv", "sha256:rows",
            "stderr.txt", "sha256:stderr", 1000, 200000, 1, true)
        {
            StoppedWordRetry = new StoppedWordRetry(1000, 1, 1, 1, 0, "retry", "a", "b", "c", "d", "word"),
        };
        var repository = new AssessmentRepository(_database);
        NewAssessmentRecord Record(string id, string saved, BatchInvocationEvidence? run) => new(
            id, null, null, "pangloss", "ParseTime", "{}", "scope",
            new AssessmentSubject.Words(Selection.Create("fixture", ["word"]), "none", "1"),
            "{}", "outcome", "semantic", "sha256:source", "model", "pipeline", 0,
            new AssessmentRecordMaterial.Words([new AssessedWord("word", "complete", [])], null, null, [], run), saved);
        repository.Record(Record("cited", "2026-01-01T00:00:00Z", invocation));
        repository.Record(Record("recent", "2026-01-02T00:00:00Z", null));
        using (var connection = _database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO AssessmentPins VALUES ('cited', 'test', '2026-01-01T00:00:00Z');";
            command.ExecuteNonQuery();
        }
        using var lease = ProjectParseLease.TryAcquire(_database)!;
        var pinned = StorePruneCommand.RunHeld(_database, _project, lease, 1, _root);
        Assert.True(pinned.Succeeded);
        Assert.Equal(0, pinned.Value!.InvocationDirectoriesRemoved);
        Assert.True(File.Exists(cache));
        Assert.True(File.Exists(Path.Combine(directory, "source.fwdata")));
        Assert.True(File.Exists(Path.Combine(directory, "retry-stats.sqlite")));
        Assert.True(File.Exists(Path.Combine(retryDirectory, "retry-stats.sqlite")));

        using (var connection = _database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "DELETE FROM AssessmentPins WHERE AssessmentId = 'cited';";
            command.ExecuteNonQuery();
        }
        var pruned = StorePruneCommand.RunHeld(_database, _project, lease, 1, _root);
        Assert.True(pruned.Succeeded);
        Assert.Equal(2, pruned.Value!.InvocationDirectoriesRemoved);
        Assert.False(Directory.Exists(directory));
        Assert.False(Directory.Exists(retryDirectory));
    }

    [Fact]
    public void PruningACacheOnlyAssessmentRemovesItsDirectory()
    {
        var directory = _store.DirectoryFor("cache-only-pruned");
        Directory.CreateDirectory(directory);
        var cache = Path.Combine(directory, "stats.sqlite");
        File.WriteAllText(cache, "cache");
        File.WriteAllText(Path.Combine(directory, "source.fwdata"), "source");
        var repository = new AssessmentRepository(_database);
        NewAssessmentRecord Record(string id, string saved, string? path) => new(
            id, null, null, "pangloss", "ParseTime", "{}", "scope",
            new AssessmentSubject.Words(Selection.Create("fixture", ["word"]), "none", "1"),
            "{}", "outcome", "semantic", "grammar", "model", "pipeline", 0,
            new AssessmentRecordMaterial.Words([new AssessedWord("word", "complete", [])], path, null, []), saved);
        repository.Record(Record("old", "2026-01-01T00:00:00Z", cache));
        repository.Record(Record("new", "2026-01-02T00:00:00Z", null));
        using var lease = ProjectParseLease.TryAcquire(_database)!;

        var result = StorePruneCommand.RunHeld(_database, _project, lease, 1, _root);

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Value!.InvocationDirectoriesRemoved);
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void PruneRetriesAnInvocationLockedDuringThePreviousSweep()
    {
        var directory = _store.DirectoryFor("locked-pruned");
        Directory.CreateDirectory(directory);
        var cache = Path.Combine(directory, "stats.sqlite");
        File.WriteAllText(cache, "cache");
        var repository = new AssessmentRepository(_database);
        NewAssessmentRecord Record(string id, string saved, string? path) => new(
            id, null, null, "pangloss", "ParseTime", "{}", "scope",
            new AssessmentSubject.Words(Selection.Create("fixture", ["word"]), "none", "1"),
            "{}", "outcome", "semantic", "grammar", "model", "pipeline", 0,
            new AssessmentRecordMaterial.Words([new AssessedWord("word", "complete", [])], path, null, []), saved);
        repository.Record(Record("old", "2026-01-01T00:00:00Z", cache));
        repository.Record(Record("new", "2026-01-02T00:00:00Z", null));
        using var lease = ProjectParseLease.TryAcquire(_database)!;
        using (var reader = StatsCacheLock.Create(directory))
        {
            Assert.True(reader.TryAcquire());
            var first = StorePruneCommand.RunHeld(_database, _project, lease, 1, _root);
            Assert.True(first.Succeeded);
            Assert.Equal(0, first.Value!.InvocationDirectoriesRemoved);
            Assert.True(Directory.Exists(directory));
        }

        var second = StorePruneCommand.RunHeld(_database, _project, lease, 1, _root);

        Assert.True(second.Succeeded);
        Assert.Equal(1, second.Value!.InvocationDirectoriesRemoved);
        Assert.False(Directory.Exists(directory));
        Assert.False(File.Exists(directory + ".prune"));
    }

    [Fact]
    public void PruneKeepsAndReportsUnownedDirectoriesIncludingOtherAndMissingStores()
    {
        var parent = Path.Combine(_root, "assessment-runs");
        var directories = new[] { "no-marker", "bad-marker", "missing-store", "wrong-schema", "fresh" }
            .Select(name => Path.Combine(parent, name)).ToArray();
        foreach (var directory in directories)
        {
            Directory.CreateDirectory(Path.Combine(directory, "nested"));
            File.WriteAllText(Path.Combine(directory, "source.fwdata"), "source");
            File.WriteAllText(Path.Combine(directory, "nested", "retry"), "retry");
            Directory.SetLastWriteTimeUtc(directory, DateTime.UtcNow.AddDays(-31));
        }
        File.WriteAllText(directories[1] + ".owner", "invalid marker");
        File.WriteAllText(directories[2] + ".owner", Path.Combine(_root, "missing.motif.db"));
        var otherStore = Path.Combine(_root, "other", "wrong-schema.motif.db");
        Directory.CreateDirectory(Path.GetDirectoryName(otherStore)!);
        File.WriteAllText(otherStore, "not a current store");
        File.WriteAllText(directories[3] + ".owner", otherStore);
        Directory.SetLastWriteTimeUtc(directories[4], DateTime.UtcNow);
        using var lease = ProjectParseLease.TryAcquire(_database)!;

        var result = StorePruneCommand.RunHeld(_database, _project, lease, 20, _root);

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.Value!.InvocationDirectoriesRemoved);
        Assert.Equal(5, result.Value.UnownedDirectoriesKept);
        Assert.Equal(55, result.Value.UnownedBytesKept);
        Assert.True(result.Value.UnownedSizeComplete);
        Assert.Equal(parent, result.Value.InvocationParentPath);
        Assert.All(directories, directory => Assert.True(File.Exists(Path.Combine(directory, "source.fwdata"))));
    }

    [Fact]
    public void PruneKeepsAndReportsAnUnreadableOwnerMarker()
    {
        if (OperatingSystem.IsWindows()) return;
        var directory = _store.DirectoryFor("unreadable-owner");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "source.fwdata"), "source");
        var owner = directory + ".owner";
        File.SetUnixFileMode(owner, UnixFileMode.None);
        try
        {
            using var lease = ProjectParseLease.TryAcquire(_database)!;
            var result = StorePruneCommand.RunHeld(_database, _project, lease, 20, _root);
            Assert.True(result.Succeeded);
            Assert.Equal(1, result.Value!.UnownedDirectoriesKept);
            Assert.Equal(6, result.Value.UnownedBytesKept);
            Assert.True(Directory.Exists(directory));
        }
        finally { File.SetUnixFileMode(owner, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
    }

    [Fact]
    public void SharedInvocationAndTrialRootKeepTheirFilesUntilTheLastReferenceIsRemoved()
    {
        var shared = _store.DirectoryFor("shared");
        var trial = _store.DirectoryFor("trial-run");
        foreach (var directory in new[] { shared, trial })
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "source.fwdata"), "source");
        }
        var repository = new AssessmentRepository(_database);
        NewAssessmentRecord Record(string id, string saved, string? directory)
        {
            BatchInvocationEvidence? invocation = directory is null ? null : new(
                Path.GetFileName(directory), Path.Combine(directory, "source.fwdata"),
                "sha256:source", "sha256:executable", "words.txt", "sha256:words", "rows.tsv", "sha256:rows",
                "stderr.txt", "sha256:stderr", 1000, 200000, 1, true);
            return new(id, null, null, "pangloss", "ParseTime", "{}", "scope",
                new AssessmentSubject.Words(Selection.Create("fixture", ["word"]), "none", "1"),
                "{}", "outcome", "semantic", "grammar", "model", "pipeline", 0,
                new AssessmentRecordMaterial.Words([new AssessedWord("word", "complete", [])], null, null, [], invocation), saved);
        }
        repository.Record(Record("trial", "2026-01-01T00:00:00Z", trial));
        repository.Record(Record("shared-old", "2026-01-02T00:00:00Z", shared));
        repository.Record(Record("shared-new", "2026-01-03T00:00:00Z", shared));
        new JobRepository(_database).Create("job", "project", "trial", "{\"baselineAssessmentIds\":[\"trial\"]}",
            "2026-01-01T00:00:00Z");
        using var lease = ProjectParseLease.TryAcquire(_database)!;
        var result = StorePruneCommand.RunHeld(_database, _project, lease, 1, _root);
        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Value!.AssessmentsRemoved);
        Assert.Equal(0, result.Value.InvocationDirectoriesRemoved);
        Assert.True(File.Exists(Path.Combine(shared, "source.fwdata")));
        Assert.True(File.Exists(Path.Combine(trial, "source.fwdata")));
        repository.Record(Record("newest", "2026-01-04T00:00:00Z", null));
        result = StorePruneCommand.RunHeld(_database, _project, lease, 1, _root);
        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Value!.InvocationDirectoriesRemoved);
        Assert.False(Directory.Exists(shared));
        Assert.True(Directory.Exists(trial));
    }

    [Fact]
    public void RetainedInvocationProtectsFilesInAnotherInvocationDirectory()
    {
        var directory = _store.DirectoryFor("shared-source");
        Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, "source.fwdata");
        File.WriteAllText(source, "source");
        var repository = new AssessmentRepository(_database);
        NewAssessmentRecord Record(string id, string run, string saved)
        {
            var invocation = new BatchInvocationEvidence(run, source, "sha256:source", "sha256:executable",
                "words.txt", "sha256:words", "rows.tsv", "sha256:rows", "stderr.txt", "sha256:stderr",
                1000, 200000, 1, true);
            return new(id, null, null, "pangloss", "ParseTime", "{}", "scope",
                new AssessmentSubject.Words(Selection.Create("fixture", ["word"]), "none", "1"),
                "{}", "outcome", "semantic", "grammar", "model", "pipeline", 0,
                new AssessmentRecordMaterial.Words([new AssessedWord("word", "complete", [])], null, null, [], invocation), saved);
        }
        repository.Record(Record("old", "shared-source", "2026-01-01T00:00:00Z"));
        repository.Record(Record("new", "other-run", "2026-01-02T00:00:00Z"));
        using var lease = ProjectParseLease.TryAcquire(_database)!;
        var result = StorePruneCommand.RunHeld(_database, _project, lease, 1, _root);
        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Value!.AssessmentsRemoved);
        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task ReplayWaitsForTheInvocationLockForNestedSourceFiles()
    {
        var directory = _store.DirectoryFor("reader");
        var nested = Path.Combine(directory, "nested");
        Directory.CreateDirectory(nested);
        var source = Path.Combine(nested, "source.fwdata");
        var cache = Path.Combine(directory, "stats.sqlite");
        File.WriteAllText(source, "source");
        File.WriteAllText(cache, "cache");
        var evidence = new BatchInvocationEvidence("reader", source, BatchInvocationEvidence.DigestFile(source),
            "sha256:executable", "words.txt", "sha256:words", "rows.tsv", "sha256:rows", "stderr.txt",
            "sha256:stderr", 1000, 200000, 1, true);
        var cacheDigest = BatchInvocationEvidence.DigestFile(cache);
        using var held = StatsCacheLock.Create(directory);
        Assert.True(held.TryAcquire());
        using var started = new ManualResetEventSlim();
        var replayTask = Task.Run(() =>
        {
            started.Set();
            return StatsEvidenceReplay.Create(evidence, cache, cacheDigest);
        });
        try
        {
            Assert.True(started.Wait(TimeSpan.FromSeconds(3)));
            Assert.NotSame(replayTask, await Task.WhenAny(replayTask, Task.Delay(250)));
        }
        finally { held.Release(); }
        using var replay = await replayTask;
        Assert.Equal("source", File.ReadAllText(replay.GrammarPath));
    }

    [Fact]
    public void UnknownDirectoryReferencedByAnotherKnownProjectIsPreserved()
    {
        var directory = Path.Combine(_root, "assessment-runs", "other-project-run");
        Directory.CreateDirectory(directory);
        var retry = Path.Combine(directory, "retry-stats.sqlite");
        File.WriteAllText(retry, "retry");
        Directory.SetLastWriteTimeUtc(directory, DateTime.UtcNow.AddDays(-8));
        var otherPath = Path.Combine(_root, "other", "other.fwdata");
        Directory.CreateDirectory(Path.GetDirectoryName(otherPath)!);
        var otherProject = new ProjectLocator(otherPath, "other");
        using var otherDatabase = MotifDatabase.OpenOwned(Path.ChangeExtension(otherPath, ".motif.db"), otherProject,
            MotifSchema.CurrentSchema, new Version(1, 0));
        new AssessmentRepository(otherDatabase).Record(new NewAssessmentRecord(
            "other-assessment", null, null, "pangloss", "ParseTime", "{}", "scope",
            new AssessmentSubject.Words(Selection.Create("fixture", ["word"]), "none", "1"),
            "{}", "outcome", "semantic", "grammar", "model", "pipeline", 0,
            new AssessmentRecordMaterial.Words([new AssessedWord("word", "complete", [])], retry, "hash", []),
            "2026-01-01T00:00:00Z"));
        File.WriteAllText(directory + ".owner", otherDatabase.FullPath);
        Assert.Null(KnownProjectRecorder.TryRecord(_root, otherProject));
        using var lease = ProjectParseLease.TryAcquire(_database)!;

        var result = StorePruneCommand.RunHeld(_database, _project, lease, 20, _root);

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Value!.UnownedDirectoriesKept);
        Assert.Equal(5, result.Value.UnownedBytesKept);
        Assert.True(File.Exists(retry));
    }

    [Fact]
    public void ExpiredOwnerMarkerWithoutAnInvocationDirectoryIsRemoved()
    {
        var directory = _store.DirectoryFor("abandoned");
        var owner = directory + ".owner";
        File.SetLastWriteTimeUtc(owner, DateTime.UtcNow.AddDays(-31));
        Prune();
        Assert.False(File.Exists(owner));
    }

    [RequiresSymbolicLinkFact]
    public void AnExpiredInvocationContainingALinkIsPreserved()
    {
        var directory = _store.DirectoryFor("linked");
        Directory.CreateDirectory(directory);
        var cache = Path.Combine(directory, "stats.sqlite");
        File.WriteAllText(cache, "cache");
        var outside = Path.Combine(_root, "outside-link-target");
        File.WriteAllText(outside, "keep");
        File.CreateSymbolicLink(Path.Combine(directory, "linked-file"), outside);
        File.SetLastWriteTimeUtc(cache, DateTime.UtcNow.AddDays(-31));
        Directory.SetLastWriteTimeUtc(directory, DateTime.UtcNow.AddDays(-31));
        Prune();
        Assert.True(Directory.Exists(directory));
        Assert.Equal("keep", File.ReadAllText(outside));
    }

    [Theory]
    [InlineData("")]
    [InlineData("..")]
    [InlineData("../escape")]
    [InlineData("..\\escape")]
    [InlineData("drive:stream")]
    [InlineData("trailing.")]
    [InlineData("trailing ")]
    [InlineData("wild*card")]
    [InlineData("CON")]
    public void UnsafeInvocationIdsAreRefusedBeforeCreatingAnyRunParent(string invocationId)
    {
        Assert.Throws<ArgumentException>(() => _store.DirectoryFor(invocationId));
        Assert.False(Directory.Exists(Path.Combine(_root, "assessment-runs")));
    }

    [RequiresSymbolicLinkFact]
    public void AReparsePointAtTheRunParentIsRefused()
    {
        var outside = Path.Combine(_root, "outside-target");
        Directory.CreateDirectory(outside);
        var link = Path.Combine(_root, "assessment-runs");
        Directory.CreateSymbolicLink(link, outside);
        try
        {
            Assert.Throws<InvalidOperationException>(() => _store.DirectoryFor("invocation-one"));
            Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
        }
        finally { Directory.Delete(link); }
    }

    [RequiresSymbolicLinkFact]
    public void AReparsePointAtTheRunDirectoryIsRefused()
    {
        var outside = Path.Combine(_root, "outside-target");
        Directory.CreateDirectory(outside);
        var path = _store.DirectoryFor("invocation-one");
        Directory.CreateSymbolicLink(path, outside);
        try
        {
            Assert.Throws<InvalidOperationException>(() => _store.DirectoryFor("invocation-one"));
        }
        finally { Directory.Delete(path); }
    }

    private void Prune()
    {
        using var lease = ProjectParseLease.TryAcquire(_database)!;
        Assert.True(StorePruneCommand.RunHeld(_database, _project, lease, 20, _root).Succeeded);
    }

    public void Dispose()
    {
        _database.Dispose();
        try { Directory.Delete(_root, recursive: true); }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
    }
}
