using System.Text.Json;
using System.Text.RegularExpressions;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Parsimony;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins what a Proposal's Parsimony section presents from the settled contract: the Findings with their stored
/// descriptions, the short read, parser work only as a percent change, wall time only in seconds or minutes, and an
/// evidence view that was not kept as a plain answer.
/// </summary>
public sealed class ProposalParsimonyPresentationTests
{
    private const string Project = "/tmp/parsimony-presentation.fwdata";
    private const string Id = "proposal/aaaaaaaaaaaaaaaaaaaaaa";
    private const string Digest = "sha256:old";
    private const string Changed = "sha256:changed";

    [Fact]
    public async Task TheNewAndChangedFindingsAreListedWithTheirStoredDescriptions()
    {
        var (model, _) = await WatchedAsync();

        var grammar = model.Groups[0];
        Assert.True(grammar.HasFindingsRead);
        Assert.Equal(
        [
            "Evidence Changed|Repeated ad hoc restrictions: Prohibition ka- + -ni now has different evidence",
            "New|Repeated ad hoc restrictions: Prohibition ma- + -tu repeats a slot",
            "New|Repeated ad hoc restrictions: 2 of 4 prohibitions",
        ], grammar.Findings.Select(finding => finding.Kind + "|" + finding.Line));
        Assert.Equal("3 Findings to Look at", grammar.FindingsHeader);
        Assert.False(model.Groups[1].HasFindingsRead);
    }

    [Fact]
    public async Task TheReadCountsMeasuresAndNeverFindingsOrAScore()
    {
        var (model, _) = await WatchedAsync();

        Assert.Equal("1 measure found new or changed Findings. Look at it first.\n" +
            "1 measure no longer finds Findings the Baseline had.\n" +
            "1 measure gave no result.\nThe Text group was not measured.", model.ReadText);
    }

    [Fact]
    public async Task TheReadSaysNothingIsNewOnlyWhenEveryGrammarMeasureComputed()
    {
        var (model, _) = await WatchedAsync(Quiet());

        Assert.Equal("No measure found a new Finding for this Proposal.\nThe Text group was not measured.", model.ReadText);
        Assert.Equal("No new or changed Findings in this group.", model.Groups[0].FindingsNote);
        Assert.Empty(model.Groups[0].Findings);
    }

    [Fact]
    public async Task AMeasureThatDidNotComputeIsNeverReadAsNothingNew()
    {
        var quiet = ProposalParsimonyStatusFixtures.Measured() with
        {
            Changes = [new ProposalParsimonyMeasureChange(ProposalParsimonyStatusFixtures.Computed, 0, 0, 8, 0, "r1", "r2")],
        };

        var (model, _) = await WatchedAsync(ProposalParsimonyStatusFixtures.Ready(Id).With(quiet));

        Assert.Equal("The measures that gave a result found no new Finding.\n1 measure gave no result.\n" +
            "The Text group was not measured.", model.ReadText);
        Assert.DoesNotContain("No measure found", model.ReadText);
    }

    [Theory]
    [InlineData(ProposalParsimonyGroupState.Failed)]
    [InlineData(ProposalParsimonyGroupState.Abstained)]
    public async Task AGrammarGroupThatGaveNoResultIsNeverReadAsNothingNew(ProposalParsimonyGroupState state)
    {
        var grammar = new ProposalParsimonyGroup(SIL.Motif.Contract.Parsimony.ParsimonyMeasureGroup.Grammar, state, null, null, [], []);

        var (model, fake) = await WatchedAsync(ProposalParsimonyStatusFixtures.Ready(Id).With(grammar));

        Assert.Equal("The Grammar measures gave no result.\nThe Text group was not measured.", model.ReadText);
        Assert.Empty(fake.ParsimonyViewRequests);
    }

    [Fact]
    public async Task AMeasuredTextGroupIsNotSaidToBeUnmeasured()
    {
        var status = Quiet() with
        {
            Groups = [Quiet().Groups[0], ProposalParsimonyStatusFixtures.TextGroup(ProposalParsimonyGroupState.Ready)],
        };

        var (model, _) = await WatchedAsync(status);

        Assert.Equal("No measure found a new Finding for this Proposal.", model.ReadText);
    }

    [Fact]
    public async Task NewFindingsThatAreAllAlreadyDecidedAreNotSaidToNeedALook()
    {
        var (model, _) = await WatchedAsync(OneNew(), Decided);

        Assert.Equal("Every new Finding is one you already decided.\nThe Text group was not measured.", model.ReadText);
        Assert.Equal("Every new Finding is one you already decided.", model.Groups[0].FindingsNote);
        Assert.Equal(["Repeated ad hoc restrictions: 1 Finding is set aside by a decision"], model.Groups[0].Judgments);
        Assert.DoesNotContain("Look at", model.ReadText);
    }

    [Fact]
    public async Task AnActiveViewThatDidNotComputeIsUnreadableAndNeverAnAllClear()
    {
        var (model, _) = await WatchedAsync(OneNew(), (request, served) => Success(request, ParsimonyMeasureStatus.NotAvailable, []));

        Assert.Equal("1 measure could not be checked, so it may have new Findings.\nThe Text group was not measured.",
            model.ReadText);
        Assert.Equal("The Findings could not be read. The counts above still stand.", model.Groups[0].FindingsNote);
    }

    [Fact]
    public async Task AnActiveViewReadStoppedEarlyIsSaidToBeIncompleteAndNeverAnAllClear()
    {
        var (model, fake) = await WatchedAsync(OneNew(), (request, _) =>
        {
            var page = Success(request, ParsimonyMeasureStatus.Computed, [Row("f-old", Digest, null)], total: 99999);
            return CommandOutcome<ParsimonyNamedViewResponse>.Success(page.Value! with { Truncated = true, NextCursor = "next" });
        });

        Assert.Equal("1 measure could not be checked, so it may have new Findings.\nThe Text group was not measured.",
            model.ReadText);
        Assert.Equal("Only part of the Findings could be read, so the list may be incomplete.", model.Groups[0].FindingsNote);
        Assert.Equal(21, fake.ParsimonyViewRequests.Count(request => request.Query.View == "parsimony-active-findings"));
    }

    [Fact]
    public async Task AnOutOfDateRunGivesNoReadAndNoFindings()
    {
        var (model, fake) = await WatchedAsync(ProposalParsimonyStatusFixtures.Stale(Id,
            ProposalParsimonyStaleReason.BaselineChanged));

        Assert.Equal(string.Empty, model.ReadText);
        Assert.All(model.Groups, group => Assert.False(group.HasFindingsRead));
        Assert.Empty(fake.ParsimonyViewRequests);
        Assert.Empty(fake.ShowParsimonyReportRequests);
    }

    [Fact]
    public async Task DecisionCountsAreShownOnlyForMeasuresThatHaveSome()
    {
        var (model, _) = await WatchedAsync();

        Assert.Equal(
        [
            "Repeated ad hoc restrictions: 1 Finding is set aside by a decision",
            "Repeated ad hoc restrictions: 2 Findings are back because their evidence changed",
            "Repeated ad hoc restrictions: 3 judgment records need a look",
        ], model.Groups[0].Judgments);
    }

    [Fact]
    public async Task OneDecisionRecordUsesTheSingularSentence()
    {
        var (_, fake) = Setup();
        var served = fake.ParsimonyViewHandler!;
        fake.ParsimonyViewHandler = async (request, token) =>
        {
            var outcome = await served(request, token);
            return request.Query.Filters.MeasureId != ProposalParsimonyStatusFixtures.Computed ? outcome
                : CommandOutcome<ParsimonyNamedViewResponse>.Success(outcome.Value! with
                {
                    ActiveCount = 3, SuppressedCount = 0, ResurfacedCount = 1, UnresolvedJudgmentCount = 0,
                });
        };
        var model = Model(fake);
        await model.Watch(Project, Id);

        Assert.Equal(["Repeated ad hoc restrictions: 1 Finding is back because its evidence changed"], model.Groups[0].Judgments);
    }

    [Fact]
    public async Task AGroupListsAtMostAFewFindingsAndSaysWhichMeasuresTheRestBelongTo()
    {
        var grammar = ProposalParsimonyStatusFixtures.Measured() with
        {
            MeasureRuns =
            [
                new ParsimonyMeasureRun(ProposalParsimonyStatusFixtures.Computed, ParsimonyMeasureStatus.Computed, 1, 1, "x", null, null, null),
                new ParsimonyMeasureRun(ProposalParsimonyStatusFixtures.Second, ParsimonyMeasureStatus.Computed, 1, 1, "x", null, null, null),
            ],
            Changes =
            [
                new ProposalParsimonyMeasureChange(ProposalParsimonyStatusFixtures.Computed, 20, 0, 0, 0, "r1", "r2"),
                new ProposalParsimonyMeasureChange(ProposalParsimonyStatusFixtures.Second, 15, 0, 0, 0, "r3", "r4"),
            ],
        };
        var (_, fake) = Setup(ProposalParsimonyStatusFixtures.Ready(Id).With(grammar));
        fake.ParsimonyViewHandler = (request, _) => Task.FromResult(request.Query.View != "parsimony-active-findings"
            ? Serve(request)
            : Success(request, ParsimonyMeasureStatus.Computed,
                [.. Enumerable.Range(0, request.Query.Filters.MeasureId == ProposalParsimonyStatusFixtures.Computed ? 20 : 15)
                    .Select(index => Row(request.Query.Filters.MeasureId + "-" + index, Digest, "Prohibition " + index))]));
        var model = Model(fake);

        await model.Watch(Project, Id);

        var group = model.Groups[0];
        Assert.Equal(25, group.Findings.Count);
        Assert.Equal("35 Findings to Look at", group.FindingsHeader);
        Assert.Equal($"{ProposalParsimonyText.MeasureName(ProposalParsimonyStatusFixtures.Second)}: 10 more Findings are not listed here.",
            group.FindingsMoreText);
        Assert.True(group.HasFindingsMore);
    }

    [Fact]
    public async Task EvidenceIsReadOnlyWhenAskedForAndSaysNotKeptPlainly()
    {
        var (model, fake) = await WatchedAsync();
        var finding = model.Groups[0].Findings.Single(item => item.FindingId == "f-new");
        var viewReadsBefore = fake.ParsimonyViewRequests.Count;
        Assert.True(finding.CanShowEvidence);
        Assert.Equal(string.Empty, finding.EvidenceText);

        await finding.ShowEvidenceCommand.ExecuteAsync(null);

        Assert.Equal("Ad hoc prohibition: 3 rows\nAllomorph: not kept for this Proposal\nSlot: could not be read",
            finding.EvidenceText);
        Assert.False(finding.CanShowEvidence);
        var evidence = fake.ParsimonyViewRequests.Skip(viewReadsBefore).ToArray();
        Assert.Equal(["adhoc-context", "allomorph-context", "slot-context"], evidence.Select(r => r.Query.View));
        Assert.All(evidence, request => Assert.Equal("bundle/evidence", request.Query.BundleId));
        Assert.Equal("guid-1", evidence[0].Query.Filters.ObjectGuid);
    }

    [Fact]
    public async Task AViewUnavailableForAnotherReasonIsNotSaidToBeUnkept()
    {
        var (_, fake) = Setup();
        var served = fake.ParsimonyViewHandler!;
        fake.ParsimonyViewHandler = async (request, token) => request.Query.View == "allomorph-context"
            ? Success(request, ParsimonyMeasureStatus.NotAvailable, [], "Required facts sections are unavailable: x.")
            : await served(request, token);
        var model = Model(fake);
        await model.Watch(Project, Id);
        var finding = model.Groups[0].Findings.Single(item => item.FindingId == "f-new");

        await finding.ShowEvidenceCommand.ExecuteAsync(null);

        Assert.Contains("Allomorph: not available for this Proposal", finding.EvidenceText);
        Assert.DoesNotContain("not kept", finding.EvidenceText);
    }

    [Fact]
    public async Task UnreadableActiveFindingsKeepTheCountsAndSayTheFindingsCouldNotBeRead()
    {
        var (_, fake) = Setup();
        fake.ParsimonyViewHandler = (_, _) => Task.FromResult(CommandOutcome<ParsimonyNamedViewResponse>.Refused(
            new Refusal("parsimony.view-failed", FailureReason.Refused, "Failed.")));
        var model = Model(fake);

        await model.Watch(Project, Id);

        Assert.Equal("Ready", model.StateLabel);
        Assert.Equal(3, model.Groups[0].Measures.Count);
        Assert.Equal("The Findings could not be read. The counts above still stand.", model.Groups[0].FindingsNote);
        Assert.Empty(model.Groups[0].Findings);
    }

    [Fact]
    public async Task AnUnreadableBaselineReportLeavesNoGuessAtWhichFindingsAreNew()
    {
        var (_, fake) = Setup();
        fake.OnReadParsimonyReport((_, _) => Task.FromResult(CommandOutcome<ParsimonyReportResponse>.Refused(
            new Refusal("parsimony.report-missing", FailureReason.Refused, "Missing."))));
        var model = Model(fake);

        await model.Watch(Project, Id);

        Assert.Empty(model.Groups[0].Findings);
        Assert.Equal("Which Findings are new could not be worked out, so none are listed.", model.Groups[0].FindingsNote);
    }

    [Fact]
    public async Task AReadThatThrowsReadsAsUnreadableNeverAsACrash()
    {
        var (_, fake) = Setup();
        fake.ParsimonyViewHandler = (_, _) => throw new InvalidOperationException("The store failed.");
        var model = Model(fake);

        await model.Watch(Project, Id);

        Assert.Equal("The Findings could not be read. The counts above still stand.", model.Groups[0].FindingsNote);
    }

    [Fact]
    public async Task TheSameCandidateReadAgainKeepsItsFindingsAndReadsNothingMore()
    {
        var (model, fake) = await WatchedAsync();
        var views = fake.ParsimonyViewRequests.Count;
        var reports = fake.ShowParsimonyReportRequests.Count;

        await model.Watch(Project, Id);

        Assert.Equal(3, model.Groups[0].Findings.Count);
        Assert.Equal(views, fake.ParsimonyViewRequests.Count);
        Assert.Equal(reports, fake.ShowParsimonyReportRequests.Count);
    }

    [Fact]
    public async Task OnlyWhatTheBaselineChangedIsReadFromItsReport()
    {
        var (_, fake) = await WatchedAsync();

        Assert.Equal(["r1"], fake.ShowParsimonyReportRequests.Select(request => request.ReportId));
        Assert.All(fake.ParsimonyViewRequests.Where(request => request.Query.View == "parsimony-active-findings"),
            request =>
            {
                Assert.Equal("b2", request.Query.BundleId);
                Assert.Equal(request.Query.Filters.MeasureId == ProposalParsimonyStatusFixtures.Computed ? "r2" : "r4",
                    request.Query.Filters.ReportId);
            });
    }

    [Fact]
    public async Task WallTimeIsShownOnlyAsWholeSecondsOrMinutes()
    {
        Assert.Equal("Measured in about 28 seconds.", (await WatchedAsync(Timed(28_400))).Model.WallTime);
        Assert.Equal("Measured in about 1 second.", (await WatchedAsync(Timed(300))).Model.WallTime);
        Assert.Equal("Measured in about 3 minutes.", (await WatchedAsync(Timed(150_000))).Model.WallTime);
        Assert.Equal(string.Empty, (await WatchedAsync()).Model.WallTime);
        var running = ProposalParsimonyStatusFixtures.Running(Id);
        Assert.Equal(string.Empty, (await WatchedAsync(running with
        {
            Job = running.Job! with { StageTimings = [new ParsimonyStageTiming("total", null, 5_000)] },
        })).Model.WallTime);
    }

    [Fact]
    public async Task WallTimeIgnoresPerStageTimings()
    {
        var status = ProposalParsimonyStatusFixtures.Ready(Id);
        status = status with
        {
            Job = status.Job! with
            {
                StageTimings =
                [
                    new ParsimonyStageTiming("facts", "candidate", 90_000),
                    new ParsimonyStageTiming("total", null, 40_000),
                ],
            },
        };

        Assert.Equal("Measured in about 40 seconds.", (await WatchedAsync(status)).Model.WallTime);
    }

    [Theory]
    [InlineData(12.4, "About 13% more parser work than the Baseline")]
    [InlineData(5.2, "About 6% more parser work than the Baseline")]
    [InlineData(-30.2, "About 31% less parser work than the Baseline")]
    [InlineData(0.2, "About 1% more parser work than the Baseline")]
    [InlineData(0, "About the same parser work as the Baseline")]
    public void ParserWorkIsAPercentChangeAgainstTheBaselineAndNoLevel(double percent, string expected)
    {
        var model = Model(new FakeCommandClient());

        model.SetParserWork(new TrialWork(40, 1_234_567, 1_387_654, percent, [], 0, 0));

        Assert.Equal(expected, model.ParserWork);
        Assert.True(model.HasParserWork);
    }

    [Fact]
    public void ParserWorkWithoutAComparisonSaysSoAndShowsNoNumber()
    {
        var model = Model(new FakeCommandClient());

        model.SetParserWork(new TrialWork(40, null, null, null, [], 0, 0));

        Assert.Equal("Parser work cannot be compared for this Trial", model.ParserWork);
    }

    [Fact]
    public void ParserWorkIsWorkedOutFromTheTotalsWhenTheTrialRecordedNoPercent()
    {
        var model = Model(new FakeCommandClient());

        model.SetParserWork(new TrialWork(40, 1_000, 1_250, null, [], 0, 0));

        Assert.Equal("About 25% more parser work than the Baseline", model.ParserWork);
    }

    [Fact]
    public void AProposalWithNoTrialShowsNoParserWork()
    {
        var model = Model(new FakeCommandClient());
        model.SetParserWork(new TrialWork(40, 1_000, 1_250, 25, [], 0, 0));

        model.SetParserWork(null);

        Assert.False(model.HasParserWork);
        Assert.Equal(string.Empty, model.ParserWork);
    }

    [Fact]
    public async Task ParserWorkIsTheSameWholePercentAsTheTrialWorkNotice()
    {
        var model = Model(new FakeCommandClient());

        foreach (var percent in new[] { 5.2, 12.4, 40.01 })
        {
            var work = new TrialWork(40, 1_000, 1_000 + (long)(percent * 10), percent, [], 0, 0);
            model.SetParserWork(work);

            var whole = Regex.Match(TrialWorkNotice.For(work)!, @"\d+").Value;
            Assert.StartsWith($"About {whole}% more", model.ParserWork, StringComparison.Ordinal);
        }
        await Task.CompletedTask;
    }

    [Fact]
    public async Task StoppingCancelsAnEvidenceReadUnderWayAndClearsTheSection()
    {
        var (model, fake) = await WatchedAsync(Timed(28_400));
        var gate = GateEvidence(fake, out var tokens);
        var finding = model.Groups[0].Findings.Single(item => item.FindingId == "f-new");
        var reading = finding.ShowEvidenceCommand.ExecuteAsync(null);
        Assert.Equal("Reading the evidence…", finding.EvidenceText);

        model.Stop();
        gate.SetResult();
        await reading;

        Assert.All(tokens, token => Assert.True(token.IsCancellationRequested));
        Assert.Equal(string.Empty, finding.EvidenceText);
        Assert.Equal(string.Empty, model.ReadText + model.WallTime + model.ParserWork);
        Assert.Empty(model.Groups);
    }

    [Fact]
    public async Task SwitchingProposalsCancelsEvidenceReadsAndALateResultNeverReachesTheOldFinding()
    {
        var (model, fake) = await WatchedAsync();
        var gate = GateEvidence(fake, out var tokens);
        var finding = model.Groups[0].Findings.Single(item => item.FindingId == "f-new");
        var reading = finding.ShowEvidenceCommand.ExecuteAsync(null);

        await model.Watch(Project, "proposal/bbbbbbbbbbbbbbbbbbbbbb");
        gate.SetResult();
        await reading;

        Assert.All(tokens, token => Assert.True(token.IsCancellationRequested));
        Assert.Equal(string.Empty, finding.EvidenceText);
        Assert.True(finding.CanShowEvidence);
    }

    [Fact]
    public async Task AReadThatReturnsAfterItsFindingWasCancelledLeavesNoStuckReadingText()
    {
        var gate = new TaskCompletionSource();
        var finding = new ProposalParsimonyFindingViewModel("f", "New", "Line", async _ =>
        {
            await gate.Task;
            return ["late line"];
        });
        var reading = finding.ShowEvidenceCommand.ExecuteAsync(null);

        finding.Cancel();
        gate.SetResult();
        await reading;

        Assert.Equal(string.Empty, finding.EvidenceText);
        Assert.True(finding.CanShowEvidence);
    }

    [Fact]
    public void TheShowEvidenceButtonNamesTheFindingItShows()
    {
        var finding = new ProposalParsimonyFindingViewModel("f", "New", "Slots: ma- repeats", _ => Task.FromResult<IReadOnlyList<string>>([]));

        Assert.Equal("Show the evidence for Slots: ma- repeats", finding.ShowEvidenceAccessibleName);
    }

    // Holds every evidence read until the returned source is set, and records the tokens those reads were given.
    private static TaskCompletionSource GateEvidence(FakeCommandClient fake, out List<CancellationToken> tokens)
    {
        var gate = new TaskCompletionSource();
        var seen = tokens = [];
        var served = fake.ParsimonyViewHandler!;
        fake.ParsimonyViewHandler = async (request, token) =>
        {
            if (request.Query.View == "parsimony-active-findings") return await served(request, token);
            seen.Add(token);
            await gate.Task;
            return await served(request, token);
        };
        return gate;
    }

    // A Ready status whose Grammar measures all computed and found nothing new.
    private static ProposalParsimonyStatus Quiet() => ProposalParsimonyStatusFixtures.Ready(Id).With(Computed(
        new ProposalParsimonyMeasureChange(ProposalParsimonyStatusFixtures.Computed, 0, 0, 8, 0, "r1", "r2"),
        new ProposalParsimonyMeasureChange(ProposalParsimonyStatusFixtures.Second, 0, 0, 8, 0, "r3", "r4")));

    // A Ready status whose first measure reports one new Finding.
    private static ProposalParsimonyStatus OneNew() => ProposalParsimonyStatusFixtures.Ready(Id).With(Computed(
        new ProposalParsimonyMeasureChange(ProposalParsimonyStatusFixtures.Computed, 1, 0, 8, 0, "r1", "r2"),
        new ProposalParsimonyMeasureChange(ProposalParsimonyStatusFixtures.Second, 0, 0, 8, 0, "r3", "r4")));

    private static ProposalParsimonyGroup Computed(params ProposalParsimonyMeasureChange[] changes) =>
        ProposalParsimonyStatusFixtures.Measured() with
        {
            MeasureRuns =
            [
                new ParsimonyMeasureRun(ProposalParsimonyStatusFixtures.Computed, ParsimonyMeasureStatus.Computed, 10, 2, "x", null, null, null),
                new ParsimonyMeasureRun(ProposalParsimonyStatusFixtures.Second, ParsimonyMeasureStatus.Computed, 8, 0, "x", null, null, null),
            ],
            Changes = changes,
        };

    // An Active view whose one New Finding is set aside by a decision, so it has no rows and a suppressed count.
    private static CommandOutcome<ParsimonyNamedViewResponse> Decided(ReadParsimonyViewRequest request,
        CommandOutcome<ParsimonyNamedViewResponse> served) => request.Query.Filters.MeasureId != ProposalParsimonyStatusFixtures.Computed
        ? served : Success(request, ParsimonyMeasureStatus.Computed, [], total: 0, counts: (0, 1, 0, 0));

    // The stored finding descriptions and counts are the harness's words; this window adds only its own framing.
    private static (ProposalParsimonyViewModel Model, FakeCommandClient Fake) Setup(ProposalParsimonyStatus? status = null)
    {
        var fake = new FakeCommandClient();
        fake.ProposalParsimonyStatusIs(status ?? ProposalParsimonyStatusFixtures.Ready(Id));
        ConfigureStoredReports(fake);
        return (Model(fake), fake);
    }

    /// <summary>Serves the stored Reports and views behind <see cref="ProposalParsimonyStatusFixtures.Ready"/>.</summary>
    internal static void ConfigureStoredReports(FakeCommandClient fake)
    {
        fake.OnReadParsimonyReport((request, _) => Task.FromResult(CommandOutcome<ParsimonyReportResponse>.Success(
            request.ReportId == "r1" ? Report("r1", Finding("f-old", Digest), Finding("f-changed", Digest))
                : Report(request.ReportId))));
        fake.ParsimonyViewHandler = (request, _) => Task.FromResult(Serve(request));
    }

    private static async Task<(ProposalParsimonyViewModel Model, FakeCommandClient Fake)> WatchedAsync(
        ProposalParsimonyStatus? status = null)
    {
        var (model, fake) = Setup(status);
        await model.Watch(Project, Id);
        return (model, fake);
    }

    // Replaces what the Active Findings view answers; every other view is served as usual.
    private static async Task<(ProposalParsimonyViewModel Model, FakeCommandClient Fake)> WatchedAsync(
        ProposalParsimonyStatus status,
        Func<ReadParsimonyViewRequest, CommandOutcome<ParsimonyNamedViewResponse>, CommandOutcome<ParsimonyNamedViewResponse>> active)
    {
        var (model, fake) = Setup(status);
        var served = fake.ParsimonyViewHandler!;
        fake.ParsimonyViewHandler = async (request, token) =>
        {
            var outcome = await served(request, token);
            return request.Query.View == "parsimony-active-findings" ? active(request, outcome) : outcome;
        };
        await model.Watch(Project, Id);
        return (model, fake);
    }

    private static ProposalParsimonyViewModel Model(FakeCommandClient fake) => new(fake.ReadProposalParsimonyStatusAsync,
        (_, _) => Task.FromCanceled(new CancellationToken(canceled: true)), fake.ReadParsimonyReportAsync,
        fake.ReadParsimonyViewAsync);

    private static ProposalParsimonyStatus Timed(long totalMilliseconds)
    {
        var status = ProposalParsimonyStatusFixtures.Ready(Id);
        return status with
        {
            Job = status.Job! with { StageTimings = [new ParsimonyStageTiming("total", null, totalMilliseconds)] },
        };
    }

    private static CommandOutcome<ParsimonyNamedViewResponse> Serve(ReadParsimonyViewRequest request)
    {
        var query = request.Query;
        if (query.View == "adhoc-context") return Success(request, ParsimonyMeasureStatus.Computed, [], total: 3);
        if (query.View == "allomorph-context")
            return Success(request, ParsimonyMeasureStatus.NotAvailable, [], ParsimonyViewsQuery.NotKeptDetail);
        if (query.View == "slot-context")
            return CommandOutcome<ParsimonyNamedViewResponse>.Refused(
                new Refusal("parsimony.view-failed", FailureReason.Refused, "Failed."));
        if (query.Filters.MeasureId != ProposalParsimonyStatusFixtures.Computed)
            return Success(request, ParsimonyMeasureStatus.Computed, [], total: 0, counts: (0, 0, 0, 0));
        ParsimonyViewRow[] rows =
        [
            Row("f-old", Digest, null),
            Row("f-changed", Changed, "Prohibition ka- + -ni now has different evidence"),
            Row("f-new", Digest, "Prohibition ma- + -tu repeats a slot"),
            Row("f-plain", Digest, null),
        ];
        return Success(request, ParsimonyMeasureStatus.Computed, rows, total: 4, counts: (4, 1, 2, 3));
    }

    private static CommandOutcome<ParsimonyNamedViewResponse> Success(ReadParsimonyViewRequest request,
        ParsimonyMeasureStatus status, IReadOnlyList<ParsimonyViewRow> rows, string? detail = null, long? total = null,
        (int Active, int Suppressed, int Resurfaced, int Unresolved)? counts = null) =>
        CommandOutcome<ParsimonyNamedViewResponse>.Success(new ParsimonyNamedViewResponse(request.Query.BundleId,
            request.Query.View, 1, status, [], total, rows.Count, null, false, rows, "sha256:items", detail)
        {
            ActiveCount = counts?.Active, SuppressedCount = counts?.Suppressed, ResurfacedCount = counts?.Resurfaced,
            UnresolvedJudgmentCount = counts?.Unresolved, JudgmentProjectionDigest = counts is null ? null : "sha256:j",
        });

    private static ParsimonyFindingDispositionViewRow Row(string id, string digest, string? description) =>
        new("r2", "baseline", "active", Finding(id, digest, description), null, null, null, null, null, null);

    private static ParsimonyReportResponse Report(string reportId, params ParsimonyFinding[] findings) =>
        ParsimonySampleReports.Listed(Token()) with { ReportId = reportId, Findings = findings };

    private static ParsimonyFinding Finding(string id, string digest, string? description = null) => new(
        id, ProposalParsimonyStatusFixtures.Computed, ParsimonyAxis.Parsimony, ParsimonyTier.Static,
        new ParsimonyFindingAttachment(ParsimonyAttachmentKind.AuthoredObject, "attachment-" + id,
            ParsimonyAuthoredObjectKind.AdhocProhibition),
        null, new ParsimonyMeasureNumber(2, 4, "prohibitions"),
        new ParsimonyMeasureThreshold(ParsimonyThresholdOperator.GreaterThan, 0, "v1"), digest,
        [
            new ParsimonyEvidenceReference("bundle/evidence", "adhoc-context", JsonDocument.Parse("{\"objectGuid\":\"guid-1\"}").RootElement),
            new ParsimonyEvidenceReference("bundle/evidence", "allomorph-context", JsonDocument.Parse("{}").RootElement),
            new ParsimonyEvidenceReference("bundle/evidence", "slot-context", JsonDocument.Parse("{}").RootElement),
        ],
        "guide:parsimony/recipes/p-adhoc-duplicate", [], ParsimonyVerification.NotRun)
    { Description = description };

    private static BaselineToken Token() => new("project", "sha256:" + new string('1', 64), "projection",
        "2026-10-23T00:00:00Z", "sha256:" + new string('9', 64));
}
