using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class NamedSelectionWordListTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-selection-word-list", Guid.NewGuid().ToString("N"));
    private readonly MotifDatabase _database;
    private readonly NamedSelectionRepository _selections;

    public NamedSelectionWordListTests()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "project.fwdata");
        File.WriteAllText(path, "synthetic project marker");
        _database = MotifDatabase.OpenOwned(Path.Combine(_root, "project.motif.db"),
            new ProjectLocator(path, "project"), MotifSchema.CurrentSchema, new Version(1, 0));
        _selections = new NamedSelectionRepository(_database);
    }

    public void Dispose()
    {
        _database.Dispose();
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static readonly SelectionParsingLimits Limits =
        new(ParserWorkLimit.Default, SelectionTimeLimitMode.Estimated, null);

    private static SelectionWordList List(params (string Word, int Count)[] counts) => new("freq.tsv",
        "sha256:" + new string('c', 64), counts.ToDictionary(entry => entry.Word, entry => entry.Count, StringComparer.Ordinal));

    [Fact]
    public void ASelectionKeepsTheListItsWordsCameFromWithSourceHashAndCounts()
    {
        _selections.SetDefault("Default", [], ["kaon", "baga", "plain"], Limits, null,
            List(("kaon", 12), ("baga", 3)));

        var saved = _selections.GetDefault()!;

        Assert.Equal("freq.tsv", saved.WordList!.Source);
        Assert.Equal("sha256:" + new string('c', 64), saved.WordList.Sha256);
        Assert.Equal(12, saved.WordList.CountOf("kaon"));
        Assert.Null(saved.WordList.CountOf("plain"));
    }

    [Fact]
    public void ACountForAWordTheSelectionDoesNotHoldIsNotKept()
    {
        _selections.SetDefault("Default", [], ["kaon"], Limits, null, List(("kaon", 1), ("stray", 9)));

        Assert.Equal(["kaon"], _selections.GetDefault()!.WordList!.Counts.Keys);
    }

    [Fact]
    public void ASaveWithTheSameWordsAndNoListKeepsTheStoredListAndItsSource()
    {
        var first = _selections.SetDefault("Default", [], ["kaon"], Limits, null, List(("kaon", 4)));

        _selections.SetDefault("Default", [], ["kaon"], Limits with { AssessBudgetSeconds = 600 }, first.Revision.ToString());

        var list = _selections.GetDefault()!.WordList!;
        Assert.Equal(4, list.CountOf("kaon"));
        Assert.Equal(first.WordList!.Source, list.Source);
        Assert.Equal(first.WordList.Sha256, list.Sha256);
    }

    [Fact]
    public void ASaveWithTheSameTextsAndWordsAndNoListKeepsTheList()
    {
        var text = Guid.NewGuid();
        var first = _selections.SetDefault("Default", [text], ["kaon"], Limits, null, List(("kaon", 4)));

        _selections.SetDefault("Default", [text], ["kaon"], Limits with { AssessBudgetSeconds = 600 }, first.Revision.ToString());

        Assert.Equal(4, _selections.GetDefault()!.WordList!.CountOf("kaon"));
    }

    [Fact]
    public void ASelectionThatGainsATextDropsTheList()
    {
        var first = _selections.SetDefault("Default", [], ["kaon"], Limits, null, List(("kaon", 4)));

        _selections.SetDefault("Default", [Guid.NewGuid()], ["kaon"], Limits, first.Revision.ToString());

        Assert.Null(_selections.GetDefault()!.WordList);
    }

    [Fact]
    public void ASelectionThatDropsATextDropsTheList()
    {
        var text = Guid.NewGuid();
        var first = _selections.SetDefault("Default", [text], ["kaon"], Limits, null, List(("kaon", 4)));

        _selections.SetDefault("Default", [], ["kaon"], Limits, first.Revision.ToString());

        Assert.Null(_selections.GetDefault()!.WordList);
    }

    [Fact]
    public void ASaveWithOtherWordsAndNoListDropsTheList()
    {
        var first = _selections.SetDefault("Default", [], ["kaon"], Limits, null, List(("kaon", 1)));

        _selections.SetDefault("Default", [], ["kaon", "inom"], Limits, first.Revision.ToString());

        Assert.Null(_selections.GetDefault()!.WordList);
    }

    [Fact]
    public void ChangingOnlyTheLimitsKeepsTheList()
    {
        var first = _selections.SetDefault("Default", [], ["kaon"], Limits, null, List(("kaon", 1)));

        _selections.SetLimits("Default", first.Revision.ToString(), Limits with { AssessBudgetSeconds = 600 });

        Assert.Equal(1, _selections.GetDefault()!.WordList!.CountOf("kaon"));
    }

    [Fact]
    public void ASelectionSavedWithTypedWordsHasNoList()
    {
        _selections.SetDefault("Default", [], ["kaon"], Limits, null);

        Assert.Null(_selections.GetDefault()!.WordList);
    }
}
