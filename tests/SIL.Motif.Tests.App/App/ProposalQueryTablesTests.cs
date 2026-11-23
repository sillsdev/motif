using SIL.Motif.App;
using SIL.Motif.App.GuideQueries;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Operations;
using Xunit;
using static SIL.Motif.Tests.App.GuideQueryTestSupport;

namespace SIL.Motif.Tests.App;

/// <summary>The query tables on a Proposal's review: the Rationale's blocks, each run at the Baseline and at the Proposal.</summary>
public sealed class ProposalQueryTablesTests
{
    internal const string Rationale =
        "Why this change.\n\n```motif-query\nid: alternates\ntitle: Alternate forms\nrun: motif query forms --where kind=alternate\n```\n\n" +
        "```motif-show\nquery: alternates\nshow: table\n```\n";

    internal static ProposalDetailProjection WithRationale(string markdown, string status = "proposed") => new("one", status,
        "Change the gloss", new RationaleView(markdown, null, "2026-11-23T00:00:00Z", false, null, false), "intent/one",
        [new ProposalOperationView("op/one", LexicalSenseOperationKinds.SetGloss, ProposalEntryContextTests.SenseTwo, null, [],
            """{"ws":"en","text":"buy"}""")]);

    internal static FakeCommandClient Fake(FormsQueryAnswer answer, string markdown = Rationale) => new()
    {
        ProposalDetail = WithRationale(markdown),
        GuideQueryHandler = (_, _) => Task.FromResult(Answer(answer())),
    };

    internal delegate SIL.Motif.Commands.Queries.FormsQueryResponse FormsQueryAnswer();

    [Fact]
    public async Task ARationaleQueryRunsAtTheBaselineAndTheProposalAndShowsBothSides()
    {
        var fake = Fake(BothAnswer);

        await using var opened = await ProposalEntryContextTests.Open(fake);
        await opened.Document.Queries.Running;

        var request = Assert.Single(fake.GuideQueryRequests);
        Assert.Equal("motif query forms --where kind=alternate --as-of one --sides both", request.Line);
        var block = Assert.Single(opened.Document.Queries.Blocks);
        var display = Assert.Single(block.Displays);
        Assert.True(display.ShowsProposal);
        Assert.Contains("ka-on-an", display.Rows[0].ProposalAnalysis);
        Assert.Contains("ka-onan", display.Rows[0].BaselineAnalysis);
    }

    [Fact]
    public async Task AProposalWithNoTrialShowsItsSideAsPendingAndStillShowsTheBaseline()
    {
        await using var opened = await ProposalEntryContextTests.Open(Fake(PendingAnswer));
        await opened.Document.Queries.Running;

        var display = Assert.Single(Assert.Single(opened.Document.Queries.Blocks).Displays);
        Assert.True(display.HasProposalPending);
        Assert.Contains("pending", display.ProposalPending);
        Assert.NotEmpty(display.Rows);
    }

    [Fact]
    public async Task SortAndFilterWorkOnEitherSideOfAProposalTable()
    {
        await using var opened = await ProposalEntryContextTests.Open(Fake(BothAnswer));
        await opened.Document.Queries.Running;
        var display = Assert.Single(Assert.Single(opened.Document.Queries.Blocks).Displays);

        display.SortCommand.Execute(GuideQueryColumn.ProposalWord);
        Assert.Equal(["baha", "kaonan", "zuri"], display.Rows.Select(row => row.Id));
        display.FilterText = "ba-ha";
        Assert.Equal(["baha"], display.Rows.Select(row => row.Id));
    }

    [Fact]
    public async Task EditingAndRunningAProposalQueryWorksButSavingSaysTheProposalCannotHoldItYet()
    {
        var fake = Fake(BothAnswer);
        await using var opened = await ProposalEntryContextTests.Open(fake);
        await opened.Document.Queries.Running;
        var block = Assert.Single(opened.Document.Queries.Blocks);

        block.EditCommand.Execute(null);
        block.DraftText = block.DraftText.Replace("alternate", "lexeme", StringComparison.Ordinal);
        await block.RunCommand.ExecuteAsync(null);
        Assert.Contains("kind=lexeme", fake.GuideQueryRequests[^1].Line);
        await block.SaveCommand.ExecuteAsync(null);

        Assert.Contains(MotifAppStrings.ProposalQuerySaveUnsupported, block.Notice);
        Assert.Empty(fake.SetRationaleRequests);
    }

    [Fact]
    public async Task ARationaleWithNoQueryBlockShowsNoTablesAndRunsNothing()
    {
        var fake = Fake(BothAnswer, "Only prose.");

        await using var opened = await ProposalEntryContextTests.Open(fake);

        Assert.False(opened.Document.Queries.IsShown);
        Assert.Empty(fake.GuideQueryRequests);
    }

    [Fact]
    public async Task AReadWithTheSameRationaleAndStateDoesNotRunTheTablesAgain()
    {
        var fake = Fake(BothAnswer);
        await using var opened = await ProposalEntryContextTests.Open(fake);
        await opened.Document.Queries.Running;

        await opened.Document.RefreshAsync();
        await opened.Document.Queries.Running;

        Assert.Single(fake.GuideQueryRequests);
    }

    [Theory]
    [InlineData(RefusalCodes.QueryAsOfUnavailable)]
    [InlineData(RefusalCodes.QueryInvalid)]
    [InlineData(RefusalCodes.QueryNoTrialRows)]
    [InlineData(RefusalCodes.GuideQueryRunInvalid)]
    [InlineData(RefusalCodes.GuideQueryRunNotReadOnly)]
    public async Task ARefusedRunSaysWhyInTheWindowsWordsNotTheCommands(string code)
    {
        var fake = Fake(BothAnswer);
        fake.GuideQueryHandler = (_, _) => Task.FromResult(CommandOutcome<SIL.Motif.Commands.Queries.FormsQueryResponse>.Refused(
            new Refusal(code, FailureReason.Refused, "The command's own sentence.")));

        await using var opened = await ProposalEntryContextTests.Open(fake);
        await opened.Document.Queries.Running;

        var notice = Assert.Single(opened.Document.Queries.Blocks).Notice;
        Assert.NotEmpty(notice);
        Assert.DoesNotContain("The command's own sentence.", notice);
        Assert.Contains(WindowRefusal.From(new Refusal(code, FailureReason.Refused, "x")).Sentence, notice);
    }
}
