using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Assess;
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
    public void NewInvocationDeletesAnExpiredNonemptyDirectory()
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

        _store.DirectoryFor("next");

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
        using var lease = StatsCacheLock.Create(directory);
        Assert.True(lease.TryAcquire());

        _store.DirectoryFor("next");

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
        Directory.SetLastWriteTimeUtc(empty, DateTime.UtcNow.AddDays(-31));

        _store.DirectoryFor("next");

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
        File.SetLastWriteTimeUtc(cache, DateTime.UtcNow.AddDays(-31));
        var invocation = new BatchInvocationEvidence("cited", Path.Combine(directory, "source.fwdata"),
            "sha256:source", "sha256:executable", "words.txt", "sha256:words", "rows.tsv", "sha256:rows",
            "stderr.txt", "sha256:stderr", 1000, 200000, 1, true);
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
        Assert.True(StorePruneCommand.RunHeld(_database, _project, lease, 1).Succeeded);
        _store.DirectoryFor("sweep-one");
        Assert.True(File.Exists(cache));
        Assert.True(File.Exists(Path.Combine(directory, "source.fwdata")));
        Assert.True(File.Exists(Path.Combine(directory, "retry-stats.sqlite")));

        using (var connection = _database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "DELETE FROM AssessmentPins WHERE AssessmentId = 'cited';";
            command.ExecuteNonQuery();
        }
        Assert.True(StorePruneCommand.RunHeld(_database, _project, lease, 1).Succeeded);
        _store.DirectoryFor("sweep-two");
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void UnknownOwnershipPreservesAnExpiredDirectory()
    {
        var directory = Path.Combine(_root, "assessment-runs", "unknown");
        Directory.CreateDirectory(directory);
        var cache = Path.Combine(directory, "stats.sqlite");
        File.WriteAllText(cache, "cache");
        File.SetLastWriteTimeUtc(cache, DateTime.UtcNow.AddDays(-31));
        _store.DirectoryFor("sweep-unknown");
        Assert.True(File.Exists(cache));
    }

    [Fact]
    public void ExpiredOwnerMarkerWithoutAnInvocationDirectoryIsRemoved()
    {
        var directory = _store.DirectoryFor("abandoned");
        var owner = directory + ".owner";
        File.SetLastWriteTimeUtc(owner, DateTime.UtcNow.AddDays(-31));
        _store.DirectoryFor("next");
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
        _store.DirectoryFor("next");
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

    public void Dispose()
    {
        _database.Dispose();
        try { Directory.Delete(_root, recursive: true); }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
    }
}
