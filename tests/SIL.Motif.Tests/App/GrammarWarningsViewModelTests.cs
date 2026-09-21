using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>Pins the grammar-warning table's per-column search over rows already in memory.</summary>
public sealed class GrammarWarningsViewModelTests
{
    private static readonly GrammarWarning EntryWarning = new(
        "warning", "Entry",
        [new("lex entry", "text"), new("kuona", "object", "e", "Entry", "silfw://localhost/link?x")],
        [new("msa", "text"), new("0c686afa-8d21-4e3b-bc0e-41812150cf4c", "missing"),
         new("does not resolve within this entry", "text")],
        "warning: lex entry \"e\": msa \"0c686afa-8d21-4e3b-bc0e-41812150cf4c\" does not resolve within this entry");

    private static readonly GrammarWarning PhonemeWarning = new(
        "capability", "Phoneme",
        [new("phoneme", "text"), new("ng", "object", "p", "Phoneme")],
        [new("is not modelled", "text")],
        "capability: phoneme \"p\": is not modelled");

    [Fact]
    public void LoadingShowsEveryRowAndCountsThem()
    {
        var table = new GrammarWarningsViewModel();

        table.Load([EntryWarning, PhonemeWarning]);

        Assert.True(table.HasAny);
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal("2 finding(s)", table.CountSummary);
    }

    [Fact]
    public void EachColumnFiltersOnItsOwnText()
    {
        var table = new GrammarWarningsViewModel();
        table.Load([EntryWarning, PhonemeWarning]);

        table.KindFilter = "phon";
        Assert.Equal("ng", Assert.IsType<GrammarWarningRowViewModel>(Assert.Single(table.Rows)).SubjectParts[1].Text);
        Assert.Equal("1 of 2 finding(s) match the filters", table.CountSummary);

        table.KindFilter = string.Empty;
        table.WhereFilter = "kuona";
        Assert.Equal("Entry", Assert.IsType<GrammarWarningRowViewModel>(Assert.Single(table.Rows)).Kind);

        table.SeverityFilter = "capability";
        Assert.Empty(table.Rows);
    }

    [Fact]
    public void TheProblemFilterAlsoMatchesTheOriginalLine_SoAPastedIdentifierFindsItsRow()
    {
        var table = new GrammarWarningsViewModel();
        table.Load([EntryWarning, PhonemeWarning]);

        table.ProblemFilter = "0c686afa";

        Assert.Equal("warning", Assert.IsType<GrammarWarningRowViewModel>(Assert.Single(table.Rows)).Severity);
    }

    [Fact]
    public void LoadingNothingClearsTheTable()
    {
        var table = new GrammarWarningsViewModel();
        table.Load([EntryWarning]);

        table.Load(null);

        Assert.False(table.HasAny);
        Assert.Empty(table.Rows);
    }
}
