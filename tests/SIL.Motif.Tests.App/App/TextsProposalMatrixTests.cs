using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins the Texts matrix in Proposal mode: the Proposal's counts held under their key, a pair only over the same
/// words, a flip that reads nothing, a failed read that says so, and one read per key only while the page is shown.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class TextsProposalMatrixTests
{
    private const string ProjectPath = TimingProposalViewTests.ProjectPath;

    private static AssessmentWordResult Word(string form, bool parsed = false) =>
        new(form, parsed ? "unassessed" : "no-analysis", false, "Complete", 1, null);

    // Two words fail to parse today; the Proposal skips one, so NoParse is 2 to 1 and Skipped 2 to 3.
    private static readonly AssessmentWordResult[] TodayWords = [Word("a"), Word("b"), Word("c", true), Word("d", true)];
    private static readonly AssessmentWordResult[] ProposalWords = [Word("a"), Word("b", true), Word("c", true), Word("d", true)];

    private sealed class Reads
    {
        public int Summaries;
        public Func<IReadOnlyList<AssessmentWordResult>, MatrixProposalSummary>? Behave;
        public Func<string, AssessmentWordResult[]> Trials = _ => ProposalWords;
    }

    private static async Task<(WorkspaceContext Context, TextsPageModel Page, Reads Reads)> OpenedAsync(
        bool onTexts = true, string proposal = "proposal/one", AssessmentWordResult[]? today = null, Reads? reads = null,
        bool open = true)
    {
        reads ??= new Reads();
        var (fake, context) = WorkspaceContextTests.NewContextWithFake();
        var page = new TextsPageModel(context)
        {
            SummariseTrial = words =>
            {
                Interlocked.Increment(ref reads.Summaries);
                return (reads.Behave ?? MatrixProposalSummary.Of)(words);
            },
        };
        fake.OnReadTrial((request, _) => Task.FromResult(CommandOutcome<TrialRecord?>.Success(
            TimingProposalViewTests.TrialFor(request.ProposalId, true) with { WordResults = reads.Trials(request.ProposalId) })));
        await context.OpenProjectAsync(ProjectPath);
        var evidence = new WorkspaceEvidence(TimingProposalViewTests.Assessment() with { Words = today ?? TodayWords },
            DateTimeOffset.UtcNow, false);
        context.Assess.Restore(evidence);
        context.PublishEvidence(evidence);
        await context.EvidencePublication;
        context.OpenPage(onTexts ? WorkspacePage.Texts : WorkspacePage.Overview);
        if (open) await context.OpenProposalAsync(proposal, CancellationToken.None);
        await SettledAsync(page);
        return (context, page, reads);
    }

    private static async Task SettledAsync(TextsPageModel page)
    {
        for (var wait = 0; wait < 100 && page.Assess.Compare.ProposalWordsMatch is null &&
            page.ProposalNote is null; wait++) await Task.Delay(20);
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        for (var wait = 0; wait < 150 && !condition(); wait++) await Task.Delay(20);
        Assert.True(condition());
    }

    private static CompareCellViewModel Cell(TextsPageModel page, CompareColumnKind column) =>
        page.Assess.Compare.Cells.Single(cell => cell.Column == column && cell.Row == WordProjectStatus.NotPresent);

    private static string Pair(string today, string proposal) =>
        string.Format(MotifAppStrings.OverviewPairFormat, today, proposal);

    [Fact]
    public async Task ThePairShowsTodayToProposalInEveryCellRowAndColumnOverTheSameWordsAndMarksTheCellsThatMoved()
    {
        var (context, page, _) = await OpenedAsync();
        context.ShowProposalView(MarkupView.TodayToProposal);

        var noParse = Cell(page, CompareColumnKind.NoParse);
        Assert.Equal(Pair("2", "1"), noParse.CountNumberText);
        Assert.True(noParse.IsMoved);
        Assert.Equal(Pair("2", "3"), Cell(page, CompareColumnKind.Skipped).CountNumberText);
        Assert.Equal(Pair("0", "0"), Cell(page, CompareColumnKind.Match).CountNumberText);
        Assert.False(Cell(page, CompareColumnKind.Match).IsMoved);
        Assert.Equal(Pair("2", "1"), page.Assess.Compare.Columns.Single(column => column.Column == CompareColumnKind.NoParse).CountText);
        Assert.StartsWith(Pair("4", "4"), page.Assess.Compare.Rows.Single(row => row.Row == WordProjectStatus.NotPresent).CountText);
        Assert.Equal(MotifAppStrings.TextsProposalPairedNote, page.ProposalNote);
    }

    [Fact]
    public async Task ADifferentWordSetIsNeverPairedAndTheNoteSaysSo()
    {
        var reads = new Reads { Trials = _ => [.. ProposalWords, Word("e")] };
        var (context, page, _) = await OpenedAsync(reads: reads);
        context.ShowProposalView(MarkupView.TodayToProposal);

        Assert.Equal("2", Cell(page, CompareColumnKind.NoParse).CountNumberText);
        Assert.False(Cell(page, CompareColumnKind.NoParse).IsMoved);
        Assert.Equal(false, page.Assess.Compare.ProposalWordsMatch);
        Assert.Equal(MotifAppStrings.TextsProposalDifferentWordsNote, page.ProposalNote);
        context.ShowProposalView(MarkupView.Proposal);
        Assert.Equal("2", Cell(page, CompareColumnKind.NoParse).CountNumberText);
        Assert.Equal("3", Cell(page, CompareColumnKind.Skipped).CountNumberText);
    }

    [Fact]
    public async Task TheProposalViewShowsTheProposalsCountsAloneAndNoneOfTodaysWordRows()
    {
        var (context, page, _) = await OpenedAsync();
        Assert.True(page.Assess.Compare.ShowsTodayWords);

        context.ShowProposalView(MarkupView.Proposal);

        Assert.Equal("1", Cell(page, CompareColumnKind.NoParse).CountNumberText);
        Assert.Equal("1", page.Assess.Compare.Columns.Single(column => column.Column == CompareColumnKind.NoParse).CountText);
        Assert.False(page.Assess.Compare.ShowsTodayWords);
        Assert.False(page.Assess.Compare.ShowsFixFirst);
        Assert.Equal(MotifAppStrings.TextsProposalViewNote, page.ProposalNote);
        context.ShowProposalView(MarkupView.Today);
        Assert.Equal("2", Cell(page, CompareColumnKind.NoParse).CountNumberText);
        Assert.True(page.Assess.Compare.ShowsTodayWords);
    }

    [Fact]
    public async Task AProposalWithNoWordsReadsAsLackingNeverAsZeroAndTodaysRowsNeverSitUnderIt()
    {
        var (fake, context) = WorkspaceContextTests.NewContextWithFake();
        var page = new TextsPageModel(context);
        fake.OnReadTrial((request, _) => Task.FromResult(CommandOutcome<TrialRecord?>.Success(
            TimingProposalViewTests.TrialFor(request.ProposalId, true) with { AssessmentIds = new Dictionary<string, string>() })));
        await context.OpenProjectAsync(ProjectPath);
        var evidence = new WorkspaceEvidence(TimingProposalViewTests.Assessment() with { Words = TodayWords },
            DateTimeOffset.UtcNow, false);
        context.Assess.Restore(evidence);
        context.PublishEvidence(evidence);
        await context.EvidencePublication;
        context.OpenPage(WorkspacePage.Texts);
        await context.OpenProposalAsync("proposal/one", CancellationToken.None);

        context.ShowProposalView(MarkupView.Proposal);
        Assert.Equal(MissingValue.Marker, Cell(page, CompareColumnKind.NoParse).CountNumberText);
        Assert.Equal(MissingValue.Marker, page.Assess.Compare.Columns[0].CountText);
        Assert.False(page.Assess.Compare.ShowsTodayWords);
        context.ShowProposalView(MarkupView.TodayToProposal);
        Assert.Equal("2", Cell(page, CompareColumnKind.NoParse).CountNumberText);
        Assert.False(Cell(page, CompareColumnKind.NoParse).IsMoved);
    }

    [Fact]
    public async Task AFlipReadsNothingAndKeepsTheSelectionSortAndFilter()
    {
        var (context, page, reads) = await OpenedAsync();
        var compare = page.Assess.Compare;
        var chosen = Cell(page, CompareColumnKind.NoParse);
        chosen.IsSelected = true;
        compare.Sort = CompareSort.Alphabetical;
        compare.SearchText = "a";
        var summaries = reads.Summaries;

        foreach (var view in new[] { MarkupView.Proposal, MarkupView.Today, MarkupView.TodayToProposal })
            context.ShowProposalView(view);

        Assert.Equal(summaries, reads.Summaries);
        Assert.True(chosen.IsSelected);
        Assert.Same(chosen, compare.ChosenCell);
        Assert.Equal(CompareSort.Alphabetical, compare.Sort);
        Assert.Equal("a", compare.SearchText);
    }

    [Fact]
    public async Task TheProposalSideIsReadOncePerKeyAndOnlyWhileTheTextsPageIsShown()
    {
        var (context, page, reads) = await OpenedAsync(onTexts: false);
        Assert.Equal(0, reads.Summaries);

        context.OpenPage(WorkspacePage.Texts);
        await UntilAsync(() => page.Assess.Compare.ProposalWordsMatch is not null);
        Assert.Equal(1, reads.Summaries);

        context.OpenPage(WorkspacePage.Overview);
        context.OpenPage(WorkspacePage.Texts);
        context.ShowProposalView(MarkupView.Proposal);
        await context.OpenProposalAsync("proposal/one", CancellationToken.None);
        context.PublishEvidence(new WorkspaceEvidence(TimingProposalViewTests.Assessment() with { Words = TodayWords },
            DateTimeOffset.UtcNow, false));
        await context.EvidencePublication;
        Assert.Equal(1, reads.Summaries);

        await context.OpenProposalAsync("proposal/two", CancellationToken.None);
        await UntilAsync(() => reads.Summaries == 2);
        Assert.Equal(2, reads.Summaries);
    }

    [Fact]
    public async Task ALateLandingForAnOlderProposalIsDroppedAndNeverShownUnderTheNewOne()
    {
        var gate = new ManualResetEventSlim();
        var reads = new Reads { Trials = id => id == "proposal/two" ? [.. ProposalWords, Word("z")] : ProposalWords };
        var (context, page, _) = await OpenedAsync(onTexts: false, reads: reads);
        reads.Behave = words =>
        {
            if (words.Count == ProposalWords.Length) Assert.True(gate.Wait(TimeSpan.FromSeconds(20)));
            return MatrixProposalSummary.Of(words);
        };
        context.OpenPage(WorkspacePage.Texts);
        context.ShowProposalView(MarkupView.Proposal);
        await Task.Delay(100);
        Assert.Equal("…", Cell(page, CompareColumnKind.NoParse).CountNumberText);

        await context.OpenProposalAsync("proposal/two", CancellationToken.None);
        context.ShowProposalView(MarkupView.Proposal);
        await UntilAsync(() => Cell(page, CompareColumnKind.Skipped).CountNumberText == "3");
        gate.Set();
        await Task.Delay(200);

        Assert.Equal("2", Cell(page, CompareColumnKind.NoParse).CountNumberText);
        Assert.Equal("3", Cell(page, CompareColumnKind.Skipped).CountNumberText);
    }

    [Fact]
    public async Task AFailedReadSaysItCouldNotBeReadShowsNothingAsAProposalNumberAndIsReadAgainWhenShownOrPublished()
    {
        var failures = 2;
        var reads = new Reads();
        reads.Behave = words => failures-- > 0 ? throw new InvalidOperationException("broke") : MatrixProposalSummary.Of(words);
        var (context, page, _) = await OpenedAsync(reads: reads);
        context.ShowProposalView(MarkupView.Proposal);
        await UntilAsync(() => page.ProposalNote?.Contains("could not be read") == true);

        Assert.Equal(MissingValue.Marker, Cell(page, CompareColumnKind.NoParse).CountNumberText);
        context.ShowProposalView(MarkupView.TodayToProposal);
        Assert.Equal("2", Cell(page, CompareColumnKind.NoParse).CountNumberText);

        context.OpenPage(WorkspacePage.Overview);
        context.OpenPage(WorkspacePage.Texts);
        await UntilAsync(() => reads.Summaries == 2);
        await UntilAsync(() => page.ProposalNote?.Contains("could not be read") == true);

        context.PublishEvidence(new WorkspaceEvidence(TimingProposalViewTests.Assessment() with { Words = TodayWords },
            DateTimeOffset.UtcNow, false));
        await context.EvidencePublication;
        await UntilAsync(() => Cell(page, CompareColumnKind.NoParse).CountNumberText == Pair("2", "1"));
        Assert.Equal(3, reads.Summaries);
    }

    [Fact]
    public async Task LeavingTheProposalDrawsTodayAloneAgain()
    {
        var (context, page, _) = await OpenedAsync();
        context.ShowProposalView(MarkupView.Proposal);

        await context.LeaveProposalAsync(CancellationToken.None);

        Assert.Equal("2", Cell(page, CompareColumnKind.NoParse).CountNumberText);
        Assert.True(page.Assess.Compare.ShowsTodayWords);
        Assert.Null(page.ProposalNote);
        Assert.False(Cell(page, CompareColumnKind.NoParse).IsMoved);
    }

    [Fact]
    public async Task TheListsAndWhatChangedTabsDrawNothingInTheProposalViewAndSayTheyAreNotPairedYet()
    {
        var (context, page, _) = await OpenedAsync();
        page.Tab = TextsTab.Lists;
        context.ShowProposalView(MarkupView.Proposal);

        Assert.False(page.ShowListsContent);
        Assert.Equal(MotifAppStrings.TextsProposalTabNotPairedViewNote, page.ProposalNote);
        page.Tab = TextsTab.WhatChanged;
        Assert.False(page.ShowWhatChangedContent);
        context.ShowProposalView(MarkupView.TodayToProposal);
        Assert.True(page.ShowWhatChangedContent);
        page.Tab = TextsTab.AnalyzeTexts;
        Assert.Equal(MotifAppStrings.TextsProposalWordCardsNote, page.ProposalNote);
    }

    [Fact]
    public async Task TheCellsAccessibleNameCarriesTheProposalCountOnlyWhereItChangesWhatIsDrawn()
    {
        var (context, page, _) = await OpenedAsync();
        var cell = Cell(page, CompareColumnKind.NoParse);
        context.ShowProposalView(MarkupView.Today);
        var today = cell.AccessibleName;
        context.ShowProposalView(MarkupView.TodayToProposal);

        Assert.Equal(string.Format(MotifAppStrings.CompareCellProposalAccessibleNameFormat, today, Pair("2", "1")),
            cell.AccessibleName);
        context.ShowProposalView(MarkupView.Today);
        Assert.Equal(today, cell.AccessibleName);
    }

    [Fact]
    public async Task AHeldPairIsDrawnAsTodaysAloneWhenTodaysWordsChangeAndTheNoteFollowsAtOnce()
    {
        var (context, page, _) = await OpenedAsync();
        context.ShowProposalView(MarkupView.TodayToProposal);
        Assert.Equal(Pair("2", "1"), Cell(page, CompareColumnKind.NoParse).CountNumberText);
        var raised = new List<string?>();
        page.PropertyChanged += (_, args) => raised.Add(args.PropertyName);
        var different = new[] { Word("p"), Word("q"), Word("r", true) };

        context.Assess.Restore(new WorkspaceEvidence(TimingProposalViewTests.Assessment() with { Words = different },
            DateTimeOffset.UtcNow, false));

        Assert.Contains(nameof(TextsPageModel.ProposalNote), raised);
        Assert.Equal(false, page.Assess.Compare.ProposalWordsMatch);
        Assert.Equal("2", Cell(page, CompareColumnKind.NoParse).CountNumberText);
        Assert.Equal("1", Cell(page, CompareColumnKind.Skipped).CountNumberText);
        Assert.False(Cell(page, CompareColumnKind.NoParse).IsMoved);
        Assert.Equal(MotifAppStrings.TextsProposalDifferentWordsNote, page.ProposalNote);
    }

    [Fact]
    public async Task ALoadOfTheSameWordsKeepsTheDigestAndALoadOfOthersDoesNot()
    {
        var (_, page, _) = await OpenedAsync();
        var compare = page.Assess.Compare;
        var revision = compare.WordsRevision;
        Assert.Equal(true, compare.ProposalWordsMatch);

        compare.UpdateOccurrences(_ => 3);
        Assert.Equal(revision, compare.WordsRevision);
        Assert.Equal(true, compare.ProposalWordsMatch);

        compare.LoadSources([Word("other")]);
        Assert.True(compare.WordsRevision > revision);
        Assert.Equal(false, compare.ProposalWordsMatch);
    }

    [Fact]
    public async Task ACellIsFaintOnlyWhenTheNumberDrawnInThatViewIsZero()
    {
        var reads = new Reads { Trials = _ => [Word("a", true), Word("b", true), Word("c", true), Word("d", true)] };
        var (context, page, _) = await OpenedAsync(today: [Word("a"), Word("b"), Word("c"), Word("d")], reads: reads);
        var skipped = Cell(page, CompareColumnKind.Skipped);
        var noParse = Cell(page, CompareColumnKind.NoParse);

        context.ShowProposalView(MarkupView.Today);
        Assert.True(skipped.IsEmpty);
        Assert.False(noParse.IsEmpty);
        context.ShowProposalView(MarkupView.TodayToProposal);
        Assert.Equal(Pair("0", "4"), skipped.CountNumberText);
        Assert.False(skipped.IsEmpty);
        Assert.False(noParse.IsEmpty);
        context.ShowProposalView(MarkupView.Proposal);
        Assert.False(skipped.IsEmpty);
        Assert.True(noParse.IsEmpty);
    }

    [Fact]
    public async Task TheMatrixSaysItIsReadingInThePairViewAndTheCountsLandWithoutAnotherPublish()
    {
        var gate = new ManualResetEventSlim();
        var (context, page, reads) = await OpenedAsync(onTexts: false);
        reads.Behave = words =>
        {
            Assert.True(gate.Wait(TimeSpan.FromSeconds(20)));
            return MatrixProposalSummary.Of(words);
        };
        await context.OpenProposalAsync("proposal/two", CancellationToken.None);
        context.OpenPage(WorkspacePage.Texts);
        await Task.Delay(100);

        context.ShowProposalView(MarkupView.TodayToProposal);
        Assert.Equal(MotifAppStrings.TextsProposalReadingNote, page.ProposalNote);
        Assert.Equal("2", Cell(page, CompareColumnKind.NoParse).CountNumberText);

        gate.Set();
        await UntilAsync(() => page.ProposalNote == MotifAppStrings.TextsProposalPairedNote);
        Assert.Equal(Pair("2", "1"), Cell(page, CompareColumnKind.NoParse).CountNumberText);
    }

    [Fact]
    public async Task TheNotesSayWhatEachViewDrawsAndNothingWhenThereIsNoMatrix()
    {
        var (context, page, _) = await OpenedAsync();
        page.Tab = TextsTab.Lists;
        Assert.Equal(MotifAppStrings.TextsProposalTabNotPairedNote, page.ProposalNote);
        context.ShowProposalView(MarkupView.Proposal);
        Assert.Equal(MotifAppStrings.TextsProposalTabNotPairedViewNote, page.ProposalNote);
        Assert.DoesNotContain("shows today's", page.ProposalNote);
        page.Tab = TextsTab.Matrix;
        context.ShowProposalView(MarkupView.TodayToProposal);
        Assert.Contains("can still hold different words", page.ProposalNote);

        context.Assess.Restore(new WorkspaceEvidence(TimingProposalViewTests.Assessment() with { Words = [] },
            DateTimeOffset.UtcNow, false));

        Assert.False(page.Assess.Compare.HasWords);
        Assert.Null(page.ProposalNote);
        Assert.False(page.HasProposalNote);
    }

    [Fact]
    public async Task TheOwnCancellationOfTheOpenIsNotAFailedReadButACancelOfTheReadAloneIsSilent()
    {
        using var cancel = new CancellationTokenSource();
        var gate = new ManualResetEventSlim();
        var own = true;
        var reads = new Reads { Behave = words =>
        {
            Assert.True(gate.Wait(TimeSpan.FromSeconds(20)));
            throw new OperationCanceledException(own ? cancel.Token : new CancellationToken(true));
        } };
        var (context, page, _) = await OpenedAsync(reads: reads, open: false);
        var opening = context.OpenProposalAsync("proposal/one", cancel.Token);
        await Task.Delay(50);
        await cancel.CancelAsync();
        gate.Set();
        try { await opening; }
        catch (OperationCanceledException) { }
        context.ShowProposalView(MarkupView.TodayToProposal);

        Assert.DoesNotContain("could not be read", page.ProposalNote ?? string.Empty);
        own = false;
        await context.OpenProposalAsync("proposal/two", CancellationToken.None);
        await Task.Delay(100);
        Assert.DoesNotContain("could not be read", page.ProposalNote ?? string.Empty);
    }
}
