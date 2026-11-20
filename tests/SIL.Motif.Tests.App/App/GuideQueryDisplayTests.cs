using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Query;
using SIL.Motif.Contract.Gdr;
using Xunit;
using static SIL.Motif.Tests.App.GuideQueryTestSupport;

namespace SIL.Motif.Tests.App;

public sealed class GuideQueryDisplayTests
{
    private static GuideQueryDisplayViewModel Table(FormsQueryResponse json, bool both = true, int? limit = null)
    {
        var display = new GuideQueryDisplayViewModel(GuideShowKind.Table, limit, both, null);
        display.Show(Read(json));
        return display;
    }

    private static string[] Ids(GuideQueryDisplayViewModel display) => display.Rows.Select(row => row.Id).ToArray();

    [Fact]
    public void ABaselineTableShowsTheBaselineWordsAndAnalysesAndNoProposalColumns()
    {
        var display = Table(BaselineAnswer(), both: false);

        Assert.Equal(["kaonan", "baha", "alikula"], Ids(display));
        Assert.False(display.ShowsProposal);
        Assert.Equal("ka-onan (NOM-eat)", display.Rows[0].BaselineAnalysis);
    }

    [Fact]
    public void BothSidesShowWhatEachGrammarSaidAboutTheSameWord()
    {
        var display = Table(BothAnswer());

        var row = display.Rows[0];
        Assert.Contains("ka-onan", row.BaselineAnalysis);
        Assert.Contains("ka-on-an", row.ProposalAnalysis);
        Assert.True(display.ShowsProposal);
    }

    [Fact]
    public void APendingSideSaysSoInsteadOfShowingNothing()
    {
        var display = Table(PendingAnswer());

        Assert.True(display.HasProposalPending);
        Assert.Contains("pending", display.ProposalPending);
        Assert.False(display.HasBaselinePending);
        Assert.All(display.Rows, row => Assert.False(row.ProposalIsWord));
    }

    [Fact]
    public void SortingRunsOnEitherSide()
    {
        var display = Table(BothAnswer());

        display.SortCommand.Execute(GuideQueryColumn.ProposalWord);
        var ascending = Ids(display);
        display.SortCommand.Execute(GuideQueryColumn.ProposalWord);
        var descending = Ids(display);
        display.SortCommand.Execute(GuideQueryColumn.BaselineState);
        var byState = Ids(display);

        Assert.Equal(["baha", "kaonan", "zuri"], ascending);
        Assert.Equal(["kaonan", "baha", "zuri"], descending);
        Assert.Equal(3, byState.Length);
        Assert.NotEqual(ascending, byState);
    }

    [Fact]
    public void FilteringReadsEitherSideOrBoth()
    {
        var display = Table(BothAnswer());

        display.FilterText = "ka-on-an";
        Assert.Equal(["kaonan"], Ids(display));

        display.FilterScopeCommand.Execute(GuideQueryFilterScope.Baseline);
        Assert.Empty(Ids(display));

        display.FilterScopeCommand.Execute(GuideQueryFilterScope.Proposal);
        Assert.Equal(["kaonan"], Ids(display));

        display.FilterText = "";
        display.FilterScopeCommand.Execute(GuideQueryFilterScope.BothSides);
        Assert.Equal(3, display.Rows.Count);
    }

    [Fact]
    public void ALimitCapsTheRowsAndLongAnswersPage()
    {
        var rows = Enumerable.Range(0, 60).Select(index => Row("w" + index.ToString("00"), Word("w" + index, "w" + index, "parsed"), null)).ToArray();
        var json = Sides("baseline", "ready", "", "", rows);

        var limited = Table(json, both: false, limit: 5);
        Assert.Equal(5, limited.Rows.Count);

        var paged = Table(json, both: false);
        Assert.Equal(GuideQueryDisplayViewModel.PageSize, paged.Rows.Count);
        Assert.Equal(3, paged.PageCount);
        paged.NextPageCommand.Execute(null);
        Assert.Equal("w25", paged.Rows[0].Id);
        paged.NextPageCommand.Execute(null);
        Assert.Equal(10, paged.Rows.Count);
        Assert.False(paged.NextPageCommand.CanExecute(null));
        paged.PreviousPageCommand.Execute(null);
        Assert.Equal("w25", paged.Rows[0].Id);
    }

    [Fact]
    public void ClickingAWordOpensItsAnalysis()
    {
        var opened = new List<string>();
        var display = new GuideQueryDisplayViewModel(GuideShowKind.Table, null, true, opened.Add);
        display.Show(Read(BothAnswer()));

        display.Rows[0].OpenBaselineWordCommand.Execute(null);

        Assert.Equal(["kaonan"], opened);
    }

    [Fact]
    public void ACountsDisplayShowsFiguresNotRows()
    {
        var display = new GuideQueryDisplayViewModel(GuideShowKind.Counts, null, false, null);
        display.Show(Read(BaselineAnswer()));

        Assert.False(display.ShowsRows);
        Assert.NotEmpty(display.CountsText);
    }

    [Fact]
    public void AGroupedAnswerShowsKeyAndCountRows()
    {
        var json = Response(null, [new QueryGroup(["a"], 4, null, []), new QueryGroup(["b"], 9, null, [])]);

        var display = Table(json, both: false);

        Assert.True(display.IsGrouped);
        Assert.Equal(2, display.Rows.Count);
    }
}
