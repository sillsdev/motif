using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.Views;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins the Warnings page in Proposal mode: it reads today's grammar check and the open Proposal's once each, pairs
/// the counts, strikes through the findings only today has, and keeps today's badge and summary whatever it shows.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class WarningsProposalViewTests
{
    private static GrammarWarning Finding(string code, GrammarDiagnosticLevel level) =>
        new(level, code, [], [], code) { Code = code };

    private static GrammarCheckResponse Today() => new(
    [
        Finding("a", GrammarDiagnosticLevel.Warning), Finding("b", GrammarDiagnosticLevel.Error),
        Finding("c", GrammarDiagnosticLevel.Information),
    ], true);

    private static GrammarCheckResponse Proposal() => new(
    [
        Finding("b", GrammarDiagnosticLevel.Error), Finding("d", GrammarDiagnosticLevel.Warning),
        Finding("e", GrammarDiagnosticLevel.Warning),
    ], true);

    private static async Task<(FakeCommandClient Fake, WorkspaceContext Context, WarningsPageModel Page)> OpenedAsync(
        GrammarCheckResponse? proposal, bool unreadable = false)
    {
        var fake = new FakeCommandClient();
        fake.StoredGrammarCheckHandler = (request, _) => Task.FromResult(request.ProposalId switch
        {
            null => CommandOutcome<StoredGrammarCheckResponse>.Success(new StoredGrammarCheckResponse(Today())),
            _ when unreadable => CommandOutcome<StoredGrammarCheckResponse>.Refused(
                new Refusal("read_failed", FailureReason.Refused, "no")),
            _ => CommandOutcome<StoredGrammarCheckResponse>.Success(new StoredGrammarCheckResponse(proposal)),
        });
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new WarningsPageModel(context);
        fake.ReadTrialCompletesWith(OverviewProposalViewTests.Trial());
        await context.OpenProjectAsync(Path.Combine(Path.GetTempPath(), "sample.fwdata"));
        context.OpenPage(WorkspacePage.Warnings);
        await context.OpenProposalAsync("proposal/one", CancellationToken.None);
        return (fake, context, page);
    }

    private static string[] Rows(WarningsPageModel page) => [.. page.Grammar.Warnings.Rows
        .Cast<GrammarWarningRowViewModel>().Select(row => row.GroupCode + ":" + row.ProposalTag)];

    [Fact]
    public async Task TodayToProposalPairsEachCountAndMarksTheFindingsOnlyOneSideHas()
    {
        var (fake, context, page) = await OpenedAsync(Proposal());

        context.ShowProposalView(MarkupView.TodayToProposal);
        var warnings = page.Grammar.Warnings;

        Assert.True(warnings.Pairs);
        Assert.Equal("3 → 3", warnings.AllCountText);
        Assert.Equal("1 → 1", warnings.ErrorCountText);
        Assert.Equal("1 → 2", warnings.WarningCountText);
        Assert.Equal("1 → 0", warnings.InformationCountText);
        Assert.True(warnings.HasReportedInformation);
        Assert.Equal(2, warnings.GoneCount);
        Assert.Equal(2, warnings.NewCount);
        Assert.Equal(["b:None", "d:New", "e:New", "a:Gone", "c:Gone"], Rows(page));
        Assert.Equal(1, fake.StoredGrammarCheckRequests.Count(request => request.ProposalId == "proposal/one"));
    }

    [Fact]
    public async Task TheGoneAndNewFiltersListOnlyTheirOwnFindings()
    {
        var (_, context, page) = await OpenedAsync(Proposal());
        context.ShowProposalView(MarkupView.TodayToProposal);

        page.Grammar.Warnings.SetBucketCommand.Execute(GrammarFindingBucket.GoneWithProposal);
        Assert.Equal(["a:Gone", "c:Gone"], Rows(page));
        page.Grammar.Warnings.SetBucketCommand.Execute(GrammarFindingBucket.NewWithProposal);
        Assert.Equal(["d:New", "e:New"], Rows(page));

        context.ShowProposalView(MarkupView.Proposal);
        Assert.Equal(GrammarFindingBucket.All, page.Grammar.Warnings.Bucket);
    }

    [Fact]
    public async Task AFlipRedrawsFromWhatIsHeldAndReadsNothing()
    {
        var (fake, context, page) = await OpenedAsync(Proposal());
        var reads = fake.StoredGrammarCheckRequests.Count;

        foreach (var view in new[] { MarkupView.TodayToProposal, MarkupView.Proposal, MarkupView.Today })
            context.ShowProposalView(view);

        Assert.Equal(reads, fake.StoredGrammarCheckRequests.Count);
    }

    [Fact]
    public async Task TheProposalViewListsTheProposalsOwnFindingsAndKeepsTheBadgeAndSummaryToday()
    {
        var (_, context, page) = await OpenedAsync(Proposal());

        context.ShowProposalView(MarkupView.Proposal);

        Assert.Equal(["b:None", "d:None", "e:None"], Rows(page));
        Assert.Equal("3", page.Grammar.Warnings.AllCountText);
        Assert.Equal(3, page.BadgeCount);
        Assert.Equal(MotifAppStrings.WarningsProposalViewNote, page.ProposalNote);
        Assert.False(page.ShowProposalNone);
    }

    [Fact]
    public async Task TheTodayViewIsTodaysFindingsAloneAndTheNoteSaysSo()
    {
        var (_, context, page) = await OpenedAsync(Proposal());

        context.ShowProposalView(MarkupView.Today);

        Assert.Equal(["a:None", "b:None", "c:None"], Rows(page));
        Assert.False(page.Grammar.Warnings.Pairs);
        Assert.Equal(string.Format(MotifAppStrings.ProposalPageTodayViewNoteFormat, MotifAppStrings.WarningsFindingsNoun),
            page.ProposalNote);
    }

    [Fact]
    public async Task AProposalWithoutACheckReadsNoneInTheProposalViewAndNeverShowsTodaysFindings()
    {
        var (_, context, page) = await OpenedAsync(null);

        context.ShowProposalView(MarkupView.Proposal);

        Assert.True(page.ShowProposalNone);
        Assert.Equal(MotifAppStrings.ProposalPageNone, page.ProposalNoneText);
        Assert.Empty(Rows(page));
        Assert.True(page.Grammar.Warnings.TodayHasAny);

        context.ShowProposalView(MarkupView.TodayToProposal);
        Assert.False(page.ShowProposalNone);
        Assert.False(page.Grammar.Warnings.Pairs);
        Assert.Equal(["a:None", "b:None", "c:None"], Rows(page));
        Assert.EndsWith(string.Format(MotifAppStrings.ProposalPageTodayOnlyFormat, MotifAppStrings.WarningsFindingsNoun),
            page.ProposalNote);
    }

    [Fact]
    public async Task AnUnreadableProposalCheckSaysSoInsteadOfPairing()
    {
        var (_, context, page) = await OpenedAsync(Proposal(), unreadable: true);

        context.ShowProposalView(MarkupView.TodayToProposal);

        Assert.False(page.Grammar.Warnings.Pairs);
        Assert.StartsWith(string.Format(MotifAppStrings.ProposalPageUnreadableFormat, MotifAppStrings.WarningsFindingsNoun),
            page.ProposalNote);
    }

    [Fact]
    public async Task LeavingTheProposalReturnsToTodaysFindingsAlone()
    {
        var (_, context, page) = await OpenedAsync(Proposal());
        context.ShowProposalView(MarkupView.TodayToProposal);

        await context.LeaveProposalAsync();

        Assert.False(page.Grammar.Warnings.Pairs);
        Assert.Equal(["a:None", "b:None", "c:None"], Rows(page));
        Assert.Null(page.ProposalNote);
    }

    [Fact]
    public void TheWindowShowsTheNotePairedChipsAndStruckThroughRowsAndHidesThemInTheTodayView()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.StoredGrammarCheckHandler = (request, _) => Task.FromResult(
                CommandOutcome<StoredGrammarCheckResponse>.Success(new StoredGrammarCheckResponse(
                    request.ProposalId is null ? Today() : Proposal())));
            fake.ReadTrialCompletesWith(OverviewProposalViewTests.Trial());
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            try
            {
                await workspace.Context.OpenProposalAsync("proposal/one", CancellationToken.None);
                workspace.Context.OpenPage(WorkspacePage.Warnings);
                workspace.Context.ShowProposalView(MarkupView.TodayToProposal);
                PageScreenshots.Settle(window);

                var chips = window.GetVisualDescendants().OfType<FilterChip>().Where(chip => chip.IsEffectivelyVisible).ToArray();
                Assert.Contains(chips, chip => chip.CountText == "3 → 3");
                Assert.Contains(chips, chip => chip.Label == MotifAppStrings.WarningsGoneWithProposal && chip.Count == 2);
                var gone = window.GetVisualDescendants().OfType<Border>().Where(border =>
                    border.Classes.Contains("goneWithProposal")).ToArray();
                Assert.Equal(2, gone.Length);
                var note = window.GetVisualDescendants().OfType<Border>().Single(border =>
                    border.Classes.Contains("proposalPageNote") && border.IsEffectivelyVisible);
                Assert.Equal(MotifAppStrings.WarningsProposalPairedNote,
                    note.GetVisualDescendants().OfType<TextBlock>().First().Text);

                workspace.Context.ShowProposalView(MarkupView.Today);
                PageScreenshots.Settle(window);
                Assert.DoesNotContain(window.GetVisualDescendants().OfType<Border>(),
                    border => border.Classes.Contains("goneWithProposal"));
                Assert.DoesNotContain(window.GetVisualDescendants().OfType<FilterChip>(),
                    chip => chip.IsEffectivelyVisible && chip.CountText?.Contains('→') == true);
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }
}
