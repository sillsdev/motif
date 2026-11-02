using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Store;

public sealed class ParsimonySelectionRepositoryTests : IDisposable
{
    private static readonly SelectionParsingLimits Limits =
        new(ParserWorkLimit.Default, SelectionTimeLimitMode.Estimated, null);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-parsimony-selection-" + Guid.NewGuid().ToString("N"));
    private readonly MotifDatabase _database;

    public ParsimonySelectionRepositoryTests()
    {
        Directory.CreateDirectory(_root);
        _database = MotifDatabase.OpenOwned(Path.Combine(_root, "p.motif.db"),
            new ProjectLocator(Path.Combine(_root, "p.fwdata"), "parsimony-selection"), MotifSchema.CurrentSchema,
            new Version(1, 0));
    }

    [Fact]
    public void TheParsimonySelectionAndTheDefaultSelectionAreSeparatePointers()
    {
        var repository = new NamedSelectionRepository(_database);
        Assert.Null(repository.GetParsimony());

        var saved = repository.SetParsimony("evidence", [], ["kitabu"], Limits, null);

        Assert.Equal("evidence", repository.GetParsimony()!.Name);
        Assert.Equal(saved.Revision, repository.GetParsimony()!.Revision);
        Assert.Null(repository.GetDefault());
        repository.SetDefault("timing", [], ["mtoto"], Limits, null);
        Assert.Equal("evidence", repository.GetParsimony()!.Name);
        Assert.Equal("timing", repository.GetDefault()!.Name);
    }

    [Fact]
    public void ASelectionThatAPointerNamesCannotBeDeleted()
    {
        var repository = new NamedSelectionRepository(_database);
        repository.SetParsimony("evidence", [], ["kitabu"], Limits, null);

        using var connection = _database.OpenConnection();
        using var delete = connection.CreateCommand();
        delete.CommandText = "DELETE FROM NamedSelections WHERE SelectionName = 'evidence';";
        Assert.Throws<SqliteException>(() => delete.ExecuteNonQuery());
    }

    [Fact]
    public void ClearingForgetsThePointerAndKeepsTheSelection()
    {
        var repository = new NamedSelectionRepository(_database);
        repository.SetParsimony("evidence", [], ["kitabu"], Limits, null);

        repository.ClearParsimony();

        Assert.Null(repository.GetParsimony());
        Assert.NotNull(repository.Get("evidence"));
        Assert.True(repository.PointParsimonyAt("evidence"));
        Assert.False(repository.PointParsimonyAt("absent"));
    }

    public void Dispose()
    {
        _database.Dispose();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
