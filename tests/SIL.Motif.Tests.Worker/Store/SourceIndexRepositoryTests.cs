using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Sources;
using SIL.Motif.Host.Store;
using Xunit;

namespace SIL.Motif.Tests.Store;

public sealed class SourceIndexRepositoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-source-index-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void MissingPendingAndGenerationConflictRemainExplicit()
    {
        using var database = Open();
        var repository = new SourceIndexRepository(database);
        Assert.Null(repository.ReadState("source"));
        repository.MarkPending("source");
        Assert.Null(repository.ReadState("source"));
        Assert.True(Publish(repository, "one", ""));
        repository.MarkPending("source");
        Assert.Equal("pending", repository.ReadState("source")!.Status);
        var pending = repository.ReadState("source")!.Generation;
        Assert.NotEqual("one", pending);
        Assert.False(Publish(repository, "two", "one"));
        Assert.Equal(pending, repository.ReadState("source")!.Generation);
        Assert.True(Publish(repository, "two", pending));
        Assert.Equal("ready", repository.ReadState("source")!.Status);
    }

    [Fact]
    public void InvalidStageAndFailedCopyLeavePreviousGenerationIntact()
    {
        using var database = Open();
        var repository = new SourceIndexRepository(database);
        Publish(repository, "one");
        Assert.Throws<SqliteException>(() => repository.Publish(State("bad"), [Passage()], [], [Place(0)]));
        using var stage = SourceIndexStage.Create(State("two"), [new("chapter", "MAT 5", "hash", 1, 1)], [Wordform()], [Place(0)]);
        using (var corrupt = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = stage.Path, Pooling = false }.ToString()))
        {
            corrupt.Open();
            using var command = corrupt.CreateCommand();
            command.CommandText = "DROP TABLE SourceWordPlaces;";
            command.ExecuteNonQuery();
        }
        Assert.Throws<SqliteException>(() => repository.Publish(stage));
        Assert.Equal("one", repository.ReadState("source")!.Generation);
        Assert.Equal(3, repository.ReadPage("source", "chapter", -1, 201).Count);
    }

    [Fact]
    public void PageSeeksByCompositeKeyAndReplacesAllOldRows()
    {
        using var database = Open();
        var repository = new SourceIndexRepository(database);
        Publish(repository, "one");
        Assert.Equal(new[] { 1, 2 }, repository.ReadPage("source", "chapter", 0, 2).Select(row => row.Place.Ordinal));
        Assert.Equal("ws", repository.ReadPage("source", "chapter", -1, 1)[0].Wordform.WritingSystem);
        using (var connection = database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "EXPLAIN QUERY PLAN SELECT * FROM SourceWordPlaces WHERE SourceId='source' " +
                "AND PassageId='chapter' AND Ordinal>0 ORDER BY Ordinal LIMIT 2;";
            using var reader = command.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Contains("SEARCH", reader.GetString(3));
            Assert.Contains("Ordinal>?", reader.GetString(3));
            Assert.False(reader.Read());
        }
        repository.Publish(State("two"), [new("other", "other", "hash", 0, 0)], [], []);
        Assert.Null(repository.ReadPassage("source", "chapter"));
        Assert.Empty(repository.ReadPage("source", "chapter", -1, 2));
    }

    [Fact]
    public void EmptyPassagesRetainZeroCountsAndInvalidCountsAreRejected()
    {
        using var database = Open();
        var repository = new SourceIndexRepository(database);
        Assert.True(repository.Publish(State("empty"), [new("empty", "empty", "hash", 0, 0)], [], []));
        Assert.Equal(0, repository.ReadPassage("source", "empty")!.EligiblePlaces);
        Assert.Empty(repository.ReadPage("source", "empty", -1, 1));
        Assert.Throws<SqliteException>(() => repository.Publish(State("bad"), [new("empty", "empty", "hash", 1, 2)], [], []));
        Assert.Equal("empty", repository.ReadState("source")!.Generation);
    }

    [Fact]
    public void UpgradeDropsComputedSourceGenerations()
    {
        string path;
        using (var database = Open())
        {
            Publish(new SourceIndexRepository(database), "one");
            path = database.FullPath;
            using var oldVersion = database.OpenConnection();
            using var version = oldVersion.CreateCommand();
            version.CommandText = "PRAGMA user_version=72;";
            version.ExecuteNonQuery();
        }
        var output = Path.Combine(_root, "copy.motif");
        var report = MotifStoreUpgrade.Copy(path, output);
        Assert.Contains("SourceIndexState", report.Dropped);
        using var connection = new SqliteConnection("Data Source=" + output);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM SourceIndexState;";
        Assert.Equal(0L, command.ExecuteScalar());
    }

    [Fact]
    public void ReaderSnapshotRetainsCompleteOldGenerationAcrossPublication()
    {
        using var database = Open();
        var repository = new SourceIndexRepository(database);
        Publish(repository, "one");
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: true);
        Assert.Equal("one", SourceIndexRepository.ReadState(connection, transaction, "source")!.Generation);
        Assert.True(repository.Publish(State("two"), [new("replacement", "replacement", "hash", 0, 0)], [], [], "one"));
        Assert.Equal("one", SourceIndexRepository.ReadState(connection, transaction, "source")!.Generation);
        Assert.Equal(3, SourceIndexRepository.ReadPage(connection, transaction, "source", "chapter", -1, 201).Count);
        Assert.Equal("two", repository.ReadState("source")!.Generation);
        Assert.Null(repository.ReadPassage("source", "chapter"));
    }

    [Theory]
    [InlineData("source", "SOURCE")]
    [InlineData("ñandú", "ÑANDÚ")]
    public void SourceIdentityIsCaseInsensitiveAndPageSeekStillUsesItsIndex(string stored, string requested)
    {
        using var database = Open();
        var repository = new SourceIndexRepository(database);
        repository.Publish(State("one") with { SourceId = stored }, [Passage()], [Wordform()], Enumerable.Range(0, 3).Select(Place));
        Assert.Equal("one", repository.ReadState(requested)!.Generation);
        Assert.NotNull(repository.ReadPassage(requested, "chapter"));
        Assert.Equal(2, repository.ReadPage(requested, "chapter", 0, 2).Count);
        repository.MarkPending(requested);
        Assert.Equal("pending", repository.ReadState(stored)!.Status);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "EXPLAIN QUERY PLAN SELECT * FROM SourceWordPlaces WHERE SourceId='SOURCE' " +
            "AND PassageId='chapter' AND Ordinal>0 ORDER BY Ordinal LIMIT 2;";
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Contains("SEARCH", reader.GetString(3));
        Assert.Contains("Ordinal>?", reader.GetString(3));
        Assert.False(reader.Read());
    }

    [Fact]
    public void MismatchedPassageDenominatorIsRejectedBeforePublication()
    {
        using var database = Open();
        var repository = new SourceIndexRepository(database);
        Publish(repository, "one");
        Assert.Throws<InvalidDataException>(() => repository.Publish(State("bad"), [Passage()], [Wordform()], [Place(0)]));
        Assert.Equal("one", repository.ReadState("source")!.Generation);
    }

    private MotifDatabase Open()
    {
        Directory.CreateDirectory(_root);
        return MotifDatabase.OpenOwned(Path.Combine(_root, "test.motif"),
            new ProjectLocator(Path.Combine(_root, "test.fwdata"), "project"), MotifSchema.CurrentSchema, new Version(0, 1));
    }

    private static SourceIndexState State(string generation) => new("source", "paratext", generation, "digest", "{}", "ready", "1", "1");
    private static SourcePassage Passage() => new("chapter", "MAT 5", "hash", 3, 3);
    private static SourceWordform Wordform() => new("word", "ws", "word", "Word");
    private static SourceWordPlace Place(int ordinal) => new("chapter", ordinal, "anchor-" + ordinal, "1", "Word", "word");
    private static bool Publish(SourceIndexRepository repository, string generation, string? expected = null) =>
        repository.Publish(State(generation), [Passage()], [Wordform()], Enumerable.Range(0, 3).Select(Place), expected);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
