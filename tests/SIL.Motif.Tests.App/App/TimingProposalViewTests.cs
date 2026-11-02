using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins the Timing page in Proposal mode: one read of the open Proposal's timing per Proposal, Trial, scope and word
/// set, only while the page is shown; a pair only over the same words; and never an older key's numbers.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class TimingProposalViewTests
{
    internal const string ProjectPath = @"C:\projects\sample.fwdata";

    internal sealed record Word(string Name, long Steps, bool Stopped = false);

    private static readonly Word[] TodayCalm = [new("a", 1_000), new("b", 2_000), new("c", 3_000), new("d", 4_000)];
    internal static readonly Word[] ProposalCalm = [new("a", 900), new("b", 1_800), new("c", 2_700), new("d", 3_600)];
    private static readonly Word[] TodayStops = [new("a", 1_000), new("b", 2_000), new("c", 3_000), new("d", 4_000, true)];
    private static readonly Word[] ProposalStops =
        [new("a", 900), new("b", 1_800), new("c", 2_700, true), new("d", 3_600, true)];

    // Answers as the command does: the word set is applied to this side's own words.
    internal static TimingResponse Respond(TimingRequest request, IReadOnlyList<Word> side, params (string Kind, int Of)[] mix)
    {
        IEnumerable<Word> chosen = side;
        if (request.ExplicitWords is { Count: > 0 } explicitWords)
            chosen = side.Where(word => explicitWords.Contains(word.Name));
        else if (request.WordSet == "parser-work-limit") chosen = side.Where(word => word.Stopped);
        else if (request.WordSet == "slowest")
            chosen = side.OrderByDescending(word => word.Steps).ThenBy(word => word.Name, StringComparer.Ordinal).Take(request.Top);
        var words = chosen.ToArray();
        var total = words.Sum(word => word.Steps);
        var sorted = words.Select(word => (double)word.Steps).Order().ToArray();
        double? median = sorted.Length == 0 ? null : sorted.Length % 2 == 1 ? sorted[sorted.Length / 2]
            : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
        var parts = mix.Length == 0 ? [("morph_rule", 1)] : mix;
        var denominator = parts.Sum(part => part.Of);
        var given = 0L;
        var kinds = new List<TimingWorkAggregate>();
        for (var index = 0; index < parts.Length; index++)
        {
            var steps = index == parts.Length - 1 ? total - given : total * parts[index].Of / denominator;
            given += steps;
            kinds.Add(new TimingWorkAggregate(parts[index].Kind, parts[index].Kind, parts[index].Kind, steps, false));
        }
        return new TimingResponse("assessment", request.WordSet, request.By, words.Length, null, null, [],
            request.By == "kind" ? [.. parts.Select(part => new TimingAggregateRow(part.Kind, part.Kind, 0, 1, words.Length))] : [], [])
        {
            Words = [.. words.Select(word => new TimingWordRow(word.Name, (int)(word.Steps / 10),
                word.Stopped ? TimingCompletion.WorkLimit : "Finished") { ParserWork = word.Steps })],
            Work = new WordWorkSummary(words.Length, total, median, null),
            ParserWorkAggregates = request.By == "kind" ? kinds : [],
        };
    }

    internal sealed class Side
    {
        public Word[] Today = TodayCalm;
        public Func<string, Word[]> Proposals = _ => ProposalCalm;
        public Func<TimingRequest, Task<CommandOutcome<TimingResponse>>>? Hold;
    }

    internal static TrialRecord TrialFor(string proposalId, bool comparable) =>
        OverviewProposalViewTests.Trial() with
        {
            ProposalId = proposalId,
            JobId = "job/" + proposalId,
            Work = comparable ? new TrialWork(4, 10_000, 9_000, -10, [], 0, 0) : new TrialWork(4, null, null, null, [], 2, 0),
        };

    internal static async Task<(FakeCommandClient Fake, WorkspaceContext Context, TimingPageModel Page)> OpenedAsync(
        Side? side = null, bool comparable = true, bool todayParsed = true, bool onTimingPage = true, string proposal = "proposal/one",
        bool todayRefused = false)
    {
        side ??= new Side();
        var fake = new FakeCommandClient();
        fake.OnTiming((request, _) =>
        {
            if (side.Hold is { } hold) return hold(request);
            var response = request.ProposalId is null
                ? Respond(request, side.Today, ("morph_rule", 3), ("affix", 2))
                : Respond(request, side.Proposals(request.ProposalId), ("morph_rule", 1), ("synth_rule", 1));
            return Task.FromResult(request.ProposalId is null && (!todayParsed || todayRefused)
                ? CommandOutcome<TimingResponse>.Refused(new Refusal("none", FailureReason.Refused, "none"))
                : CommandOutcome<TimingResponse>.Success(response));
        });
        fake.OnReadTrial((request, _) => Task.FromResult(
            CommandOutcome<TrialRecord?>.Success(TrialFor(request.ProposalId, comparable))));
        if (!todayParsed)
        {
            var now = DateTimeOffset.UtcNow;
            fake.ReadCurrentEvidenceCompletesWith(new CurrentEvidenceSnapshot("one", now, null, EvidenceFreshness.Current,
                new BaselineRecord("project-1", OverviewProposalViewTests.Token, "root", ProjectPath, now, now),
                null, null, null, null));
        }
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new TimingPageModel(context);
        await context.OpenProjectAsync(ProjectPath);
        if (!todayParsed)
            context.Baseline = new WorkspaceBaseline(true, string.Empty, string.Empty, string.Empty, string.Empty, null);
        if (todayParsed)
        {
            context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.Now, WasRerun: false));
            await context.EvidencePublication;
        }
        if (onTimingPage) context.OpenPage(WorkspacePage.Timing);
        await context.OpenProposalAsync(proposal, CancellationToken.None);
        return (fake, context, page);
    }

    internal static AssessCommandResponse Assessment() => new(
        new BaselineCaptureResponse(
            new BaselineToken("project-1", "sha256:" + new string('a', 64), "1", "2026-09-05T11:02:00Z",
                "sha256:" + new string('b', 64)),
            ProjectPath, DateTimeOffset.UtcNow, false, false),
        new SelectionProjection([], []), [], "summary")
    {
        InvocationId = "invocation/one",
        Measurements = [new ProducedAssessmentReference("assessment-parse", "ParseTime", "invocation/one")],
    };

    internal static string Pair(string today, string proposal) =>
        string.Format(MotifAppStrings.OverviewPairFormat, today, proposal);

    internal static int ProposalReads(FakeCommandClient fake) =>
        fake.TimingRequests.Count(request => request.ProposalId is not null);

    [Fact]
    public async Task TodayToProposalPairsTheHeadlineAndTheKindsOverTheSameWords()
    {
        var (fake, context, page) = await OpenedAsync();

        context.ShowProposalView(MarkupView.TodayToProposal);

        Assert.Equal(Pair("10,000 parser work", "9,000 parser work"), page.HeadlineTotal);
        Assert.Equal(Pair("2,500 parser work", "2,250 parser work"), page.HeadlineMedian);
        Assert.Equal(Pair("0", "0"), page.HeadlineStopped);
        Assert.Equal(MotifAppStrings.TimingProposalPairedNote, page.ProposalNote);
        Assert.Equal(1, ProposalReads(fake));
        Assert.DoesNotContain(fake.TimingRequests, request => request.ProposalId is not null && request.By != "kind");
    }

    [Fact]
    public async Task AKindOnlyOneSideHasReadsAsNoneOnTheOtherNeverAsZero()
    {
        var (_, context, page) = await OpenedAsync();
        context.ShowProposalView(MarkupView.TodayToProposal);

        var affix = page.KindShares.Single(row => row.Kind == "affix");
        var synthesis = page.KindShares.Single(row => row.Kind == "synth_rule");
        var morph = page.KindShares.Single(row => row.Kind == "morph_rule");

        Assert.EndsWith(MotifAppStrings.ProposalPageNone, affix.ShownWorkText);
        Assert.StartsWith(MotifAppStrings.TimingNoneToday, synthesis.ShownWorkText);
        Assert.True(synthesis.IsProposalOnly);
        Assert.Equal(Pair("6,000 parser work", "4,500 parser work"), morph.ShownWorkText);
    }

    [Fact]
    public async Task AFlipOrARepublishWithTheSameKeyReadsNothingAndTheTodayViewIsTodaysAlone()
    {
        var (fake, context, page) = await OpenedAsync();
        var reads = ProposalReads(fake);

        foreach (var view in new[] { MarkupView.TodayToProposal, MarkupView.Proposal, MarkupView.Today })
            context.ShowProposalView(view);
        context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.Now, WasRerun: false));
        await context.EvidencePublication;

        Assert.Equal(reads, ProposalReads(fake));
        Assert.Equal("10,000 parser work", page.HeadlineTotal);
        Assert.DoesNotContain(page.KindShares, row => row.ProposalShareText is not null);
        Assert.Equal(string.Format(MotifAppStrings.ProposalPageTodayViewNoteFormat, MotifAppStrings.TimingNoun),
            page.ProposalNote);
    }

    [Fact]
    public async Task TheProposalIsReadOnlyWhileTheTimingPageIsShown()
    {
        var (fake, context, page) = await OpenedAsync(onTimingPage: false);
        await Task.Delay(50);
        Assert.Equal(0, ProposalReads(fake));

        context.OpenPage(WorkspacePage.Timing);
        await Task.Delay(100);
        context.ShowProposalView(MarkupView.TodayToProposal);

        Assert.Equal(1, ProposalReads(fake));
        Assert.Equal(Pair("10,000 parser work", "9,000 parser work"), page.HeadlineTotal);
    }

    [Fact]
    public async Task TheProposalViewShowsTheProposalsNumbersAloneAndTheNoteNamesWhatStaysToday()
    {
        var (_, context, page) = await OpenedAsync();

        context.ShowProposalView(MarkupView.Proposal);

        Assert.Equal("9,000 parser work", page.HeadlineTotal);
        Assert.Equal("2,250 parser work", page.HeadlineMedian);
        Assert.Equal("0", page.HeadlineStopped);
        Assert.Equal(["morph_rule", "synth_rule"],
            page.KindShares.Where(row => !row.IsNotAttributed).Select(row => row.Kind));
        Assert.Equal(MotifAppStrings.TimingProposalViewNote, page.ProposalNote);
    }

    [Fact]
    public async Task WorkThatCannotBeComparedKeepsTodaysTotalsAndPairsOnlyTheStoppedWordsOverTheSameWords()
    {
        var (_, context, page) = await OpenedAsync(new Side { Today = TodayStops, Proposals = _ => ProposalStops },
            comparable: false);

        context.ShowProposalView(MarkupView.TodayToProposal);

        Assert.Equal("10,000 parser work", page.HeadlineTotal);
        Assert.Equal("2,500 parser work", page.HeadlineMedian);
        Assert.Equal(Pair("1", "2"), page.HeadlineStopped);
        Assert.DoesNotContain(page.KindShares, row => row.ProposalShareText is not null);
        Assert.Equal(MotifAppStrings.TimingProposalStoppedOnlyNote, page.ProposalNote);
    }

    [Fact]
    public async Task TheProposalViewHonoursTheSameRuleAndShowsNoWorkTheTriesCannotCompare()
    {
        var (_, context, page) = await OpenedAsync(new Side { Today = TodayStops, Proposals = _ => ProposalStops },
            comparable: false);

        context.ShowProposalView(MarkupView.Proposal);

        Assert.Equal(MotifAppStrings.ProposalPageNone, page.HeadlineTotal);
        Assert.Equal(MotifAppStrings.ProposalPageNone, page.HeadlineMedian);
        Assert.Equal("2", page.HeadlineStopped);
        Assert.Empty(page.KindShares);
        Assert.Equal(MotifAppStrings.TimingProposalViewNotComparableNote, page.ProposalNote);
    }

    [Fact]
    public async Task AProposalWithOtherWordsIsNotPairedAndTheNoteDoesNotClaimTheSameWords()
    {
        var other = new Word[] { new("b", 1_800), new("c", 2_700), new("d", 3_600), new("e", 100) };
        var (_, context, page) = await OpenedAsync(new Side { Proposals = _ => other });

        context.ShowProposalView(MarkupView.TodayToProposal);

        Assert.Equal("10,000 parser work", page.HeadlineTotal);
        Assert.Equal("0", page.HeadlineStopped);
        Assert.DoesNotContain(page.KindShares, row => row.ProposalShareText is not null);
        Assert.Equal(MotifAppStrings.TimingProposalDifferentWordsNote, page.ProposalNote);
        Assert.DoesNotContain("same words", page.ProposalNote);
    }

    [Fact]
    public async Task ADifferentNumberOfWordsIsNotPaired()
    {
        Word[] more = [.. ProposalCalm, new Word("e", 100)];
        var (_, context, page) = await OpenedAsync(new Side { Proposals = _ => more });

        context.ShowProposalView(MarkupView.TodayToProposal);

        Assert.Equal("10,000 parser work", page.HeadlineTotal);
        Assert.Equal(MotifAppStrings.TimingProposalDifferentWordsNote, page.ProposalNote);
    }

    [Fact]
    public async Task TheSlowestWordsArePairedOnlyWhenBothSidesPickTheSameWords()
    {
        var (_, context, page) = await OpenedAsync();
        context.ShowProposalView(MarkupView.TodayToProposal);
        page.SlowestCount = 2;
        await page.SelectWordSetCommand.ExecuteAsync("slowest");
        Assert.Equal(Pair("7,000 parser work", "6,300 parser work"), page.HeadlineTotal);

        var (_, otherContext, otherPage) = await OpenedAsync(new Side
        {
            Proposals = _ => [new("a", 900), new("b", 5_000), new("c", 2_700), new("d", 3_600)],
        });
        otherContext.ShowProposalView(MarkupView.TodayToProposal);
        otherPage.SlowestCount = 2;
        await otherPage.SelectWordSetCommand.ExecuteAsync("slowest");
        Assert.Equal("7,000 parser work", otherPage.HeadlineTotal);
        Assert.Equal(MotifAppStrings.TimingProposalDifferentWordsNote, otherPage.ProposalNote);
    }

    [Fact]
    public async Task ChangingTheWordSetShowsReadingAndNeverTheOldNumberUntilTheNewReadLands()
    {
        var side = new Side();
        var (_, context, page) = await OpenedAsync(side);
        context.ShowProposalView(MarkupView.Proposal);
        Assert.Equal("9,000 parser work", page.HeadlineTotal);
        var todayGate = new TaskCompletionSource<CommandOutcome<TimingResponse>>();
        var proposalGate = new TaskCompletionSource<CommandOutcome<TimingResponse>>();
        side.Hold = request => request.WordSet != "parser-work-limit"
            ? Task.FromResult(CommandOutcome<TimingResponse>.Success(request.ProposalId is null
                ? Respond(request, side.Today, ("morph_rule", 1)) : Respond(request, ProposalCalm, ("morph_rule", 1))))
            : request.ProposalId is null ? todayGate.Task : proposalGate.Task;

        var selecting = page.SelectWordSetCommand.ExecuteAsync("parser-work-limit");
        await Task.Delay(50);
        Assert.Equal(MotifAppStrings.OverviewProposalReading, page.HeadlineTotal);
        Assert.Empty(page.KindShares);

        todayGate.SetResult(CommandOutcome<TimingResponse>.Success(
            Respond(new TimingRequest(ProjectPath, null, "parser-work-limit"), [new Word("z", 700, true)], ("morph_rule", 1))));
        await Task.Delay(50);
        Assert.Equal(MotifAppStrings.OverviewProposalReading, page.HeadlineTotal);

        proposalGate.SetResult(CommandOutcome<TimingResponse>.Success(
            Respond(new TimingRequest(ProjectPath, null, "parser-work-limit"), [new Word("z", 500, true)], ("morph_rule", 1))));
        await selecting;
        Assert.Equal("500 parser work", page.HeadlineTotal);
    }

    [Fact]
    public async Task ARefusedTodayReadStillReadsTheProposalForTheWordsOnShow()
    {
        var (fake, context, page) = await OpenedAsync(todayRefused: true);

        context.ShowProposalView(MarkupView.Proposal);
        await Task.Delay(50);

        Assert.Equal("9,000 parser work", page.HeadlineTotal);
        Assert.Equal(1, ProposalReads(fake));
    }

    [Fact]
    public async Task OpeningAnotherProposalNeverShowsTheOldOnesNumbersWhileTheNewOneReads()
    {
        var side = new Side { Proposals = id => id == "proposal/two"
            ? [new("a", 100), new("b", 200), new("c", 300), new("d", 400)] : ProposalCalm };
        var twoGate = new TaskCompletionSource<CommandOutcome<TimingResponse>>();
        var (_, context, page) = await OpenedAsync(side);
        context.ShowProposalView(MarkupView.Proposal);
        Assert.Equal("9,000 parser work", page.HeadlineTotal);
        side.Hold = request => request.ProposalId == "proposal/two" ? twoGate.Task
            : Task.FromResult(CommandOutcome<TimingResponse>.Success(request.ProposalId is null
                ? Respond(request, side.Today, ("morph_rule", 1)) : Respond(request, ProposalCalm, ("morph_rule", 1))));

        var opening = context.OpenProposalAsync("proposal/two", CancellationToken.None);
        await Task.Delay(50);
        context.ShowProposalView(MarkupView.Proposal);
        Assert.Equal(MotifAppStrings.OverviewProposalReading, page.HeadlineTotal);
        Assert.Empty(page.KindShares);
        context.ShowProposalView(MarkupView.TodayToProposal);
        Assert.DoesNotContain("9,000", page.HeadlineTotal);

        twoGate.SetResult(CommandOutcome<TimingResponse>.Success(
            Respond(new TimingRequest(ProjectPath), side.Proposals("proposal/two"), ("morph_rule", 1))));
        await opening;
        context.ShowProposalView(MarkupView.Proposal);
        Assert.Equal("1,000 parser work", page.HeadlineTotal);
    }

    [Fact]
    public async Task ALateReadForTheOldProposalIsDroppedWhenTheNewOneHasLanded()
    {
        var side = new Side { Proposals = id => id == "proposal/two"
            ? [new("a", 100), new("b", 200), new("c", 300), new("d", 400)] : ProposalCalm };
        var oneGate = new TaskCompletionSource<CommandOutcome<TimingResponse>>();
        var (_, context, page) = await OpenedAsync(side, onTimingPage: false);
        side.Hold = request => request.ProposalId == "proposal/one" ? oneGate.Task
            : Task.FromResult(CommandOutcome<TimingResponse>.Success(request.ProposalId is null
                ? Respond(request, side.Today, ("morph_rule", 1)) : Respond(request, side.Proposals(request.ProposalId), ("morph_rule", 1))));
        context.OpenPage(WorkspacePage.Timing);
        await Task.Delay(50);

        await context.OpenProposalAsync("proposal/two", CancellationToken.None);
        context.ShowProposalView(MarkupView.Proposal);
        Assert.Equal("1,000 parser work", page.HeadlineTotal);
        oneGate.SetResult(CommandOutcome<TimingResponse>.Success(
            Respond(new TimingRequest(ProjectPath), ProposalCalm, ("morph_rule", 1))));
        await Task.Delay(50);

        Assert.Equal("1,000 parser work", page.HeadlineTotal);
    }

    [Fact]
    public async Task APageFocusedOnWordsWithStoredEvidenceStillReadsTheNewProposalForThoseWords()
    {
        var side = new Side { Proposals = id => id == "proposal/two"
            ? [new("a", 100), new("b", 200), new("c", 300), new("d", 400)] : ProposalCalm };
        var fake = new FakeCommandClient();
        fake.OnTiming((request, _) => Task.FromResult(CommandOutcome<TimingResponse>.Success(request.ProposalId is null
            ? Respond(request, side.Today, ("morph_rule", 1)) : Respond(request, side.Proposals(request.ProposalId), ("morph_rule", 1)))));
        fake.OnReadTrial((request, _) => Task.FromResult(
            CommandOutcome<TrialRecord?>.Success(TrialFor(request.ProposalId, true))));
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new TimingPageModel(context);
        await context.OpenProjectAsync(ProjectPath);
        context.OpenPage(WorkspacePage.Timing);
        await context.PublishCurrentEvidenceAsync(StoredSnapshot());
        context.Open(new OpenTimingRequest(["a", "b"], null));
        await Task.Delay(100);
        await context.OpenProposalAsync("proposal/one", CancellationToken.None);
        context.ShowProposalView(MarkupView.Proposal);
        Assert.Equal("2,700 parser work", page.HeadlineTotal);

        await context.OpenProposalAsync("proposal/two", CancellationToken.None);
        context.ShowProposalView(MarkupView.Proposal);

        Assert.True(context.Evidence.Assessment!.IsStored);
        Assert.Equal("300 parser work", page.HeadlineTotal);
    }

    internal static CurrentEvidenceSnapshot StoredSnapshot()
    {
        var saved = DateTimeOffset.UtcNow;
        return new CurrentEvidenceSnapshot("one", saved, saved, EvidenceFreshness.Current,
            new BaselineRecord("project-1", OverviewProposalViewTests.Token, "root", ProjectPath, saved, saved),
            null, null, null,
            new AssessmentRecord(
                AssessmentId: "assessment-parse", ProposalId: null, ProposalIntentDigest: null, Assessor: "pangloss",
                Kind: AssessmentKind.ParseTime.ToStoredKind(), ScopeJson: "{}", ScopeDigest: "sha256:scope",
                Subject: new AssessmentSubject.Words(Selection.Create("Default", ["a"]), "whitespace", "1"),
                BaselineToken: "{}", OutcomeDigest: null, SemanticDigest: null, GrammarSourceSha256: "sha256:grammar",
                ModelFingerprint: null, Pipeline: null, DiagnosticCount: null, SavedUtc: saved.ToString("O"),
                Details: new AssessmentRecordDetails.Words([new AssessedWord("a", "analysed", [], 10)
                    { ProjectStanding = ProjectStanding.Approved }], null, null, [])
                    with
                    {
                        Invocation = new BatchInvocationEvidence("invocation/assessment-parse", "source", "digest", "digest",
                            "words", "digest", "tsv", "digest", "stderr", "digest", 1000, ParserWorkLimit.Default, 1, false),
                    }));
    }

    [Fact]
    public async Task AProposalThatCannotBeReadNeverShowsTodaysNumberInItsView()
    {
        var side = new Side();
        var (_, context, page) = await OpenedAsync(side);
        side.Hold = request => Task.FromResult(request.ProposalId is null
            ? CommandOutcome<TimingResponse>.Success(Respond(request, side.Today, ("morph_rule", 1)))
            : CommandOutcome<TimingResponse>.Refused(new Refusal("read_failed", FailureReason.Refused, "no")));
        await context.OpenProposalAsync("proposal/two", CancellationToken.None);

        context.ShowProposalView(MarkupView.Proposal);

        Assert.Equal(MotifAppStrings.ProposalPageNone, page.HeadlineTotal);
        Assert.Empty(page.KindShares);
        context.ShowProposalView(MarkupView.TodayToProposal);
        Assert.Equal("10,000 parser work", page.HeadlineTotal);
        Assert.Contains(string.Format(MotifAppStrings.ProposalPageUnreadableFormat, MotifAppStrings.TimingNoun), page.ProposalNote);
    }

    [Fact]
    public async Task WithNoParseTodayTheProposalsNumbersStandInsteadOfTheParseCardInItsViews()
    {
        var (_, context, page) = await OpenedAsync(todayParsed: false);

        context.ShowProposalView(MarkupView.Proposal);
        await Task.Delay(50);
        Assert.True(page.ShowPageBody);
        Assert.False(page.ShowParsePromptCard);
        Assert.Equal("9,000 parser work", page.HeadlineTotal);
        Assert.True(page.ShowHeadlineCard);
        Assert.True(page.ShowKindCard);

        context.ShowProposalView(MarkupView.TodayToProposal);
        Assert.Equal(Pair(MotifAppStrings.TimingNoneToday, "9,000 parser work"), page.HeadlineTotal);
        Assert.All(page.KindShares.Where(row => !row.IsNotAttributed), row => Assert.True(row.IsProposalOnly));

        context.ShowProposalView(MarkupView.Today);
        Assert.False(page.ShowPageBody);
        Assert.True(page.ShowParsePromptCard);
    }

    [Fact]
    public async Task LeavingTheProposalDropsItsSideAndReadsNothing()
    {
        var (fake, context, page) = await OpenedAsync();
        context.ShowProposalView(MarkupView.TodayToProposal);
        var reads = ProposalReads(fake);

        await context.LeaveProposalAsync(CancellationToken.None);

        Assert.Equal("10,000 parser work", page.HeadlineTotal);
        Assert.Null(page.ProposalNote);
        Assert.DoesNotContain(page.KindShares, row => row.ProposalShareText is not null);
        Assert.Equal(reads, ProposalReads(fake));
    }

    [Fact]
    public async Task OnlyASmallSummaryOfTheProposalsReadIsKeptNotItsWordRows()
    {
        var side = new Side();
        var (fake, context, page) = await OpenedAsync(side, onTimingPage: false);
        WeakReference? read = null;
        side.Hold = request =>
        {
            if (request.ProposalId is null) return Task.FromResult(CommandOutcome<TimingResponse>.Success(Respond(request, side.Today, ("morph_rule", 1))));
            var big = Respond(request, ProposalCalm, ("morph_rule", 1)) with
            {
                Words = [.. Enumerable.Range(0, 5_000).Select(index => new TimingWordRow("w" + index, 1, "Finished") { ParserWork = 1 })],
            };
            read = new WeakReference(big.Words);
            return Task.FromResult(CommandOutcome<TimingResponse>.Success(big));
        };
        context.OpenPage(WorkspacePage.Timing);
        await Task.Delay(100);
        context.ShowProposalView(MarkupView.Proposal);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.NotNull(read);
        Assert.False(read!.IsAlive);
        GC.KeepAlive(page);
    }

    [Fact]
    public void TheWindowShowsTheNoteAndThePairedLegendAndHidesThemInTheTodayView()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            fake.OnTiming((request, _) => Task.FromResult(CommandOutcome<TimingResponse>.Success(
                request.ProposalId is null ? Respond(request, TodayCalm, ("morph_rule", 3), ("affix", 2))
                    : Respond(request, ProposalCalm, ("morph_rule", 1), ("synth_rule", 1)))));
            fake.ReadTrialCompletesWith(TrialFor("proposal/one", true));
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            try
            {
                workspace.Context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.Now, WasRerun: false));
                await workspace.Context.EvidencePublication;
                await workspace.Context.OpenProposalAsync("proposal/one", CancellationToken.None);
                workspace.Context.OpenPage(WorkspacePage.Timing);
                workspace.Context.ShowProposalView(MarkupView.TodayToProposal);
                PageScreenshots.Settle(window);
                await Task.Delay(100);
                PageScreenshots.Settle(window);

                var note = window.GetVisualDescendants().OfType<Border>().Single(border =>
                    border.Classes.Contains("proposalPageNote") && border.IsEffectivelyVisible);
                Assert.Equal(MotifAppStrings.TimingProposalPairedNote,
                    note.GetVisualDescendants().OfType<TextBlock>().First().Text);
                var texts = window.GetVisualDescendants().OfType<TimingPage>().Single().GetVisualDescendants()
                    .OfType<TextBlock>().Where(block => block.IsEffectivelyVisible).Select(block => block.Text).ToArray();
                Assert.Contains(Pair("10,000 parser work", "9,000 parser work"), texts);

                workspace.Context.ShowProposalView(MarkupView.Today);
                PageScreenshots.Settle(window);
                var today = window.GetVisualDescendants().OfType<TimingPage>().Single().GetVisualDescendants()
                    .OfType<TextBlock>().Where(block => block.IsEffectivelyVisible).Select(block => block.Text).ToArray();
                Assert.DoesNotContain(Pair("10,000 parser work", "9,000 parser work"), today);
                Assert.Contains("10,000 parser work", today);
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(3));
    }
}
