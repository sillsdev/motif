using System.Text.Json;
using Avalonia.Input;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Handoff;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Help;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins the Parsimony page's states, grouping and links against the stored Report alone. Opening or reopening the page
/// reads the latest stored Report and its Baseline; the fake client fails any other call, so a parser run or a write
/// would surface as a test failure.
/// </summary>
public sealed class ParsimonyPageModelTests
{
    private const string ProjectA = "/tmp/parsimony-page-a.fwdata";
    private const string ProjectB = "/tmp/parsimony-page-b.fwdata";
    private const string ReportId = "report/parsimony-page";
    private const string BundleId = "bundle/parsimony-page";

    private static readonly BaselineToken CurrentToken = Token("1");
    private static readonly BaselineToken EarlierToken = Token("2");

    private static readonly string RecordTypeId = "record-type/" + new string('A', 22);

    private static readonly string ParsimonyMeasure = "P-adhoc-duplicate";

    private static readonly string RestrictivenessMeasure =
        MeasureCatalog.All.First(measure => measure.Axis == ParsimonyAxis.Restrictiveness).Id;

    [Fact]
    public async Task PageIsOffWhenAdvancedAiModeIsOff_ItReadsNothingAndCannotBeOpened()
    {
        var fake = new FakeCommandClient();
        var (context, page) = Build(fake, advancedAi: false);

        await context.OpenProjectAsync(ProjectA);

        Assert.Equal(ParsimonyPageState.Off, page.State);
        Assert.Empty(fake.ReadLatestParsimonyRequests);
        Assert.Empty(fake.ShowParsimonyReportRequests);
        Assert.False(context.CanOpenPage(WorkspacePage.Parsimony));
        context.OpenPage(WorkspacePage.Parsimony);
        Assert.Equal(WorkspacePage.Overview, context.CurrentPage);
    }

    [Fact]
    public async Task PageIsListedAndOpenableWhenAdvancedAiModeIsOn()
    {
        var fake = new FakeCommandClient();
        ExpectNoReport(fake);
        var (context, _) = Build(fake, advancedAi: true);

        await context.OpenProjectAsync(ProjectA);
        context.OpenPage(WorkspacePage.Parsimony);

        Assert.True(context.CanOpenPage(WorkspacePage.Parsimony));
        Assert.Equal(WorkspacePage.Parsimony, context.CurrentPage);
    }

    [Fact]
    public async Task WithNoReportYetThePageIsEmptyAndNamesOneNextStep()
    {
        var fake = new FakeCommandClient();
        ExpectNoReport(fake);
        var (context, page) = Build(fake, advancedAi: true);

        await context.OpenProjectAsync(ProjectA);

        Assert.Equal(ParsimonyPageState.NoReport, page.State);
        Assert.Equal("No Parsimony Report yet.", page.Message);
        Assert.False(string.IsNullOrWhiteSpace(page.NextStep));
        Assert.Empty(fake.ShowParsimonyReportRequests);
    }

    [Fact]
    public async Task WhileTheStoredReportIsReadThePageIsLoading()
    {
        var fake = new FakeCommandClient();
        var release = new TaskCompletionSource<CommandOutcome<ParsimonyLatestReportResponse>>();
        fake.OnReadLatestParsimony((_, _) => release.Task);
        var (context, page) = Build(fake, advancedAi: true);

        var opening = context.OpenProjectAsync(ProjectA);
        await WaitUntil(() => page.State == ParsimonyPageState.Loading);

        release.SetResult(CommandOutcome<ParsimonyLatestReportResponse>.Refused(
            new Refusal("parsimony.no-report", FailureReason.NotFound, "No Parsimony Report yet.")));
        await opening;
        Assert.Equal(ParsimonyPageState.NoReport, page.State);
    }

    [Fact]
    public async Task ARefusedReadShowsTheCommandsOwnRefusalMessage()
    {
        var fake = new FakeCommandClient();
        fake.OnReadLatestParsimony((_, _) => Task.FromResult(CommandOutcome<ParsimonyLatestReportResponse>.Refused(
            new Refusal("parsimony.report-damaged", FailureReason.StoreInconsistent,
                "The stored Parsimony Report is damaged."))));
        var (context, page) = Build(fake, advancedAi: true);

        await context.OpenProjectAsync(ProjectA);

        Assert.Equal(ParsimonyPageState.Refused, page.State);
        Assert.Equal("The stored Parsimony Report is damaged.", page.Message);
        Assert.Empty(page.Groups);
    }

    [Fact]
    public async Task AnIncompleteReportSaysWhichInputsAreMissing()
    {
        var fake = new FakeCommandClient();
        var report = Report(
            [Finding("f1", ParsimonyMeasure, ParsimonyAxis.Parsimony, 2, 3)],
            measureRuns:
            [
                new ParsimonyMeasureRun(ParsimonyMeasure, ParsimonyMeasureStatus.Computed, 3, 1, "prohibitions",
                    new ParsimonyMeasureNumber(2, 3, "prohibitions"), 0.66, null),
                new ParsimonyMeasureRun(RestrictivenessMeasure, ParsimonyMeasureStatus.NotAvailable, null, null,
                    "forms", null, null, "The parser evidence table is missing."),
            ]);
        ExpectReport(fake, report);
        var (context, page) = Build(fake, advancedAi: true);

        await context.OpenProjectAsync(ProjectA);

        Assert.Equal(ParsimonyPageState.Listed, page.State);
        Assert.True(page.IsIncomplete);
        Assert.Contains(ShortTitle(RestrictivenessMeasure), page.MissingInputs);
        Assert.DoesNotContain(ShortTitle(ParsimonyMeasure), page.MissingInputs);
        Assert.Equal($"Not measured: {ShortTitle(RestrictivenessMeasure)}.", page.MissingInputsText);
        Assert.Single(page.Groups.SelectMany(group => group.Items));
    }

    [Fact]
    public async Task FindingsAreGroupedByMeasureAndEachAxisIsCountedSeparately()
    {
        var fake = new FakeCommandClient();
        ExpectReport(fake, Report(
            [
                Finding("p1", ParsimonyMeasure, ParsimonyAxis.Parsimony, 1, 3),
                Finding("p2", ParsimonyMeasure, ParsimonyAxis.Parsimony, 2, 3),
                Finding("r1", RestrictivenessMeasure, ParsimonyAxis.Restrictiveness, 1, 4),
            ]));
        var (context, page) = Build(fake, advancedAi: true);

        await context.OpenProjectAsync(ProjectA);

        Assert.Equal(2, page.Groups.Count);
        var parsimonyGroup = Assert.Single(page.Groups, group => group.Title == Title(ParsimonyMeasure));
        Assert.Equal(2, parsimonyGroup.Items.Count);
        Assert.Equal("Parsimony", parsimonyGroup.AxisLabel);
        var restrictivenessGroup = Assert.Single(page.Groups, group => group.Title == Title(RestrictivenessMeasure));
        Assert.Equal("Restrictiveness", restrictivenessGroup.AxisLabel);
        Assert.Equal("3 findings to review", page.AxisSummary);
        Assert.Equal("Parsimony: a simpler grammar. Restrictiveness: a grammar that accepts fewer wrong forms.", page.AxisLegend);
        var headers = page.Rows.Where(row => row.IsHeader).ToArray();
        Assert.Equal("2 findings", headers.Single(row => row.Title == Title(ParsimonyMeasure)).Count);
        Assert.Equal("Restrictiveness: a grammar that accepts fewer wrong forms",
            headers.Single(row => row.Title == Title(RestrictivenessMeasure)).Detail);
        Assert.Equal("1 finding", headers.Single(row => row.Title == Title(RestrictivenessMeasure)).Count);
        Assert.Equal("3", page.Badge);
    }

    [Fact]
    public async Task SuppressedFindingsLeaveActiveAndAppearInSuppressedWithTheirReason()
    {
        var fake = new FakeCommandClient();
        var kept = Finding("kept", ParsimonyMeasure, ParsimonyAxis.Parsimony, 1, 3);
        var deferred = Finding("deferred", ParsimonyMeasure, ParsimonyAxis.Parsimony, 1, 4);
        var open = Finding("open", ParsimonyMeasure, ParsimonyAxis.Parsimony, 1, 5);
        ExpectReport(fake, Report([kept, deferred, open]),
        [
            History(kept, "keep", "suppressed", "Checked against the paradigm."),
            History(deferred, "defer", "suppressed", null),
        ]);
        var (context, page) = Build(fake, advancedAi: true);

        await context.OpenProjectAsync(ProjectA);

        var active = Assert.Single(page.Groups.SelectMany(group => group.Items));
        Assert.Equal(Describe(open), active.Description);
        Assert.Equal(string.Empty, active.Disposition);
        Assert.Equal("Active (1)", page.ActiveTabLabel);
        Assert.Equal("Suppressed (2)", page.SuppressedTabLabel);
        var suppressed = page.SuppressedItems.ToDictionary(item => item.Decision + "/" + item.Reason);
        Assert.Equal("Suppressed", suppressed["Kept/Reason: Checked against the paradigm."].StateLabel);
        Assert.Equal("No reason given", suppressed["Deferred/No reason given"].Reason);
    }

    [Fact]
    public async Task AFindingDecidedAndAppliedLeavesTheActiveListAfterARefresh()
    {
        var fake = new FakeCommandClient();
        var finding = Finding("f1", ParsimonyMeasure, ParsimonyAxis.Parsimony, 2, 3);
        var report = Report([finding]);
        ExpectReport(fake, report);
        var (context, page) = Build(fake, advancedAi: true);
        await context.OpenProjectAsync(ProjectA);
        Assert.Single(page.Groups.SelectMany(group => group.Items));

        // Apply wrote the decision: the live views now name it suppressed, and a Refresh reads them again.
        ExpectReport(fake, report, [History(finding, "keep", "suppressed", null)]);
        await page.ReloadAsync();

        Assert.Empty(page.Groups);
        Assert.Single(page.SuppressedItems);
        Assert.Equal("Active (0)", page.ActiveTabLabel);
    }

    [Fact]
    public async Task SelectingAFindingShowsItsEvidenceInTheDetailPaneWithoutInternalIdentities()
    {
        var fake = new FakeCommandClient();
        var finding = Finding("f1", ParsimonyMeasure, ParsimonyAxis.Parsimony, 2, 3);
        ExpectReport(fake, Report([finding]));
        var (context, page) = Build(fake, advancedAi: true);
        await context.OpenProjectAsync(ProjectA);
        var row = Assert.Single(page.Groups.SelectMany(group => group.Items));

        Assert.True(page.SelectFinding(row.Link));

        var evidence = Assert.IsType<ParsimonyEvidence>(page.Evidence);
        Assert.Equal(Title(ParsimonyMeasure), evidence.Title);
        Assert.Equal(finding.Limitations, evidence.Limitations);
        Assert.Contains(evidence.Details, detail => detail.Label == "Counted" && detail.Value == "2 of 3 prohibitions");
        Assert.Single(page.Groups.SelectMany(group => group.Items), item => item.IsSelected);
        var shown = string.Join(" ", evidence.Details.Select(detail => detail.Value).Append(evidence.Title));
        Assert.DoesNotContain(BundleId, shown, StringComparison.Ordinal);
        Assert.DoesNotContain(finding.EvidenceDigest, shown, StringComparison.Ordinal);
        Assert.DoesNotContain(ReportId, shown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnOldFindingLinkIsRefusedAfterTheProjectIsReplaced()
    {
        var fake = new FakeCommandClient();
        ExpectReport(fake, Report([Finding("f1", ParsimonyMeasure, ParsimonyAxis.Parsimony, 2, 3)]));
        var (context, page) = Build(fake, advancedAi: true);
        await context.OpenProjectAsync(ProjectA);
        var link = Assert.Single(page.Groups.SelectMany(group => group.Items)).Link;

        ExpectNoReport(fake);
        await context.OpenProjectAsync(ProjectB);

        Assert.False(page.SelectFinding(link));
        Assert.Null(page.Evidence);
        Assert.Contains("no longer open", page.SelectionMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReopeningReadsTheStoredReportAgainAndNothingElse()
    {
        var fake = new FakeCommandClient();
        ExpectReport(fake, Report([Finding("f1", ParsimonyMeasure, ParsimonyAxis.Parsimony, 2, 3)]));
        var (context, page) = Build(fake, advancedAi: true);

        await context.OpenProjectAsync(ProjectA);
        await context.OpenProjectAsync(ProjectA);

        Assert.Equal(2, fake.ReadLatestParsimonyRequests.Count);
        Assert.Equal(2, fake.ShowParsimonyReportRequests.Count);
        Assert.Empty(fake.RecordDispositionRequests);
        Assert.Empty(fake.RetractDispositionRequests);
        Assert.All(fake.ReadLatestParsimonyRequests, request => Assert.Equal(ProjectA, request.FwDataPath));
        Assert.Equal(ParsimonyPageState.Listed, page.State);
    }

    [Fact]
    public async Task AReportMeasuredFromAnEarlierBaselineIsSaidSo()
    {
        var fake = new FakeCommandClient();
        ExpectReport(fake, Report([Finding("f1", ParsimonyMeasure, ParsimonyAxis.Parsimony, 2, 3)],
            token: EarlierToken));
        var (context, page) = Build(fake, advancedAi: true);

        await context.OpenProjectAsync(ProjectA);

        Assert.True(page.IsHistorical);
    }

    [Fact]
    public async Task AReportMeasuredFromTheCurrentBaselineIsNotHistorical()
    {
        var fake = new FakeCommandClient();
        ExpectReport(fake, Report([Finding("f1", ParsimonyMeasure, ParsimonyAxis.Parsimony, 2, 3)]));
        var (context, page) = Build(fake, advancedAi: true);

        await context.OpenProjectAsync(ProjectA);

        Assert.False(page.IsHistorical);
    }

    [Fact]
    public async Task EachFindingRowNamesItsItemsOrSaysTheReportDoesNotNameThem()
    {
        var fake = new FakeCommandClient();
        ExpectReport(fake, Report(
            [
                Finding("named", ParsimonyMeasure, ParsimonyAxis.Parsimony, 2, 3, items: ["ka-", "-ni"]),
                Finding("unnamed", ParsimonyMeasure, ParsimonyAxis.Parsimony, 1, 4),
            ]));
        var (context, page) = Build(fake, advancedAi: true);

        await context.OpenProjectAsync(ProjectA);

        var rows = page.Rows.Where(row => !row.IsHeader).ToArray();
        Assert.Equal("ka-, -ni", rows[0].Items);
        Assert.Equal("The Report does not name the items for this finding.", rows[1].Items);
    }

    [Fact]
    public async Task TheDetailPaneSaysWhyTheFindingWasReportedAndDropsTheRepeatedAxis()
    {
        var fake = new FakeCommandClient();
        ExpectReport(fake, Report([Finding("f1", ParsimonyMeasure, ParsimonyAxis.Parsimony, 2, 3)]));
        var (context, page) = Build(fake, advancedAi: true);
        await context.OpenProjectAsync(ProjectA);

        Assert.True(page.SelectFinding(Assert.Single(page.Rows, row => !row.IsHeader).Link!));

        var details = page.Evidence!.Details.ToDictionary(detail => detail.Label, detail => detail.Value);
        Assert.DoesNotContain("Grammar side", details.Keys);
        Assert.DoesNotContain("Trigger", details.Keys);
        Assert.Equal($"Reported because 2 of 3 prohibitions match {ShortTitle(ParsimonyMeasure)}.",
            details["Why it was reported"]);
    }

    [Fact]
    public async Task TheDetailPaneShowsOnlyTheFindingsOwnStatedLimits()
    {
        var fake = new FakeCommandClient();
        ExpectReport(fake, Report(
            [
                Finding("bare", ParsimonyMeasure, ParsimonyAxis.Parsimony, 2, 3, limitations: []),
                Finding("stated", ParsimonyMeasure, ParsimonyAxis.Parsimony, 1, 4,
                    limitations: ["Counts only the prohibitions in this grammar."]),
            ]));
        var (context, page) = Build(fake, advancedAi: true);
        await context.OpenProjectAsync(ProjectA);
        var rows = page.Rows.Where(row => !row.IsHeader).ToArray();

        Assert.True(page.SelectFinding(rows[0].Link!));
        Assert.Empty(page.Evidence!.Limitations);
        Assert.True(page.SelectFinding(rows[1].Link!));
        Assert.Equal(["Counts only the prohibitions in this grammar."], page.Evidence!.Limitations);
    }

    [Fact]
    public async Task SeveralMissingChecksAreJoinedAsAPlainList()
    {
        var fake = new FakeCommandClient();
        var runs = new[] { ParsimonyMeasure, RestrictivenessMeasure }.Select(measure => new ParsimonyMeasureRun(measure,
            ParsimonyMeasureStatus.NotAvailable, null, null, "forms", null, null, "Missing.")).ToArray();
        ExpectReport(fake, Report([Finding("f1", ParsimonyMeasure, ParsimonyAxis.Parsimony, 2, 3)], measureRuns: runs));
        var (context, page) = Build(fake, advancedAi: true);

        await context.OpenProjectAsync(ProjectA);

        Assert.Equal($"Not measured: {ShortTitle(ParsimonyMeasure)} and {ShortTitle(RestrictivenessMeasure)}.",
            page.MissingInputsText);
    }

    [Fact]
    public async Task SelectingAFindingTellsTheViewItsDecisionActionsAreAvailable()
    {
        var fake = new FakeCommandClient();
        ExpectReport(fake, Report([Finding("f1", ParsimonyMeasure, ParsimonyAxis.Parsimony, 2, 3)]));
        var (context, page) = Build(fake, advancedAi: true);
        await context.OpenProjectAsync(ProjectA);
        var availableWhenSignalled = new List<bool>();
        page.KeepCommand.CanExecuteChanged += (_, _) => availableWhenSignalled.Add(page.KeepCommand.CanExecute(null));

        Assert.True(page.SelectFinding(Assert.Single(page.Rows, row => !row.IsHeader).Link!));

        // The view enables a button only on this signal, so it must come after the selection is in place.
        Assert.Contains(true, availableWhenSignalled);
        Assert.True(page.KeepCommand.CanExecute(null));
    }

    [Fact]
    public async Task KeepStagesAPendingChangeWithTheReasonAndTheSelectedRecordTypeAndWritesNothing()
    {
        var fake = new FakeCommandClient();
        var finding = Finding("f1", ParsimonyMeasure, ParsimonyAxis.Parsimony, 2, 3);
        ExpectReport(fake, Report([finding]));
        var (context, page) = Build(fake, advancedAi: true);
        await context.OpenProjectAsync(ProjectA);
        Assert.True(page.SelectFinding(Assert.Single(page.Rows, row => !row.IsHeader).Link!));
        page.ReasonText = "  Attested in the field notes.  ";

        await page.KeepCommand.ExecuteAsync(null);

        var request = Assert.Single(fake.RecordDispositionRequests);
        Assert.Equal("keep", request.Disposition);
        Assert.Equal("Attested in the field notes.", request.Reason);
        Assert.Null(request.Question);
        Assert.Equal(ReportId, request.ReportId);
        Assert.Equal(finding.FindingId, request.FindingId);
        Assert.Equal(PendingChanges.DraftName, request.DraftName);
        Assert.Equal(RecordTypeId, request.RecordTypeId);
        Assert.Empty(fake.RetractDispositionRequests);
        Assert.Equal("Staged in pending changes. Nothing is written to FieldWorks until Apply.", page.ActionMessage);
        Assert.Equal("Kept · staged, not yet applied", Assert.Single(page.Rows, row => !row.IsHeader).Disposition);
        Assert.Empty(page.SuppressedItems);
    }

    [Fact]
    public async Task AskNeedsOneQuestionAndDeferWithNoReasonSendsNone()
    {
        var fake = new FakeCommandClient();
        var finding = Finding("f1", ParsimonyMeasure, ParsimonyAxis.Parsimony, 2, 3);
        ExpectReport(fake, Report([finding]));
        var (context, page) = Build(fake, advancedAi: true);
        await context.OpenProjectAsync(ProjectA);
        Assert.True(page.SelectFinding(Assert.Single(page.Rows, row => !row.IsHeader).Link!));

        await page.AskCommand.ExecuteAsync(null);
        Assert.Empty(fake.RecordDispositionRequests);
        Assert.Equal("Write the question to ask first.", page.ActionMessage);

        page.QuestionText = "Is this allomorph conditioned by the stem class?";
        await page.AskCommand.ExecuteAsync(null);
        var ask = Assert.Single(fake.RecordDispositionRequests);
        Assert.Equal("ask", ask.Disposition);
        Assert.Equal("Is this allomorph conditioned by the stem class?", ask.Question);
        Assert.Null(ask.Reason);

        await page.DeferCommand.ExecuteAsync(null);
        var defer = fake.RecordDispositionRequests[1];
        Assert.Equal("defer", defer.Disposition);
        Assert.Null(defer.Reason);
        Assert.Null(defer.Question);
    }

    [Fact]
    public async Task ADecisionWithoutAChosenRecordTypeCannotBeStaged()
    {
        var fake = new FakeCommandClient();
        fake.RecordTypesHandler = (_, _) => Task.FromResult(CommandOutcome<NotebookRecordTypesResponse>.Success(
            new NotebookRecordTypesResponse(
            [
                new NotebookRecordType("record-type/" + new string('B', 22), "Review"),
                new NotebookRecordType("record-type/" + new string('C', 22), "Question"),
            ])));
        ExpectReport(fake, Report([Finding("f1", ParsimonyMeasure, ParsimonyAxis.Parsimony, 2, 3)]));
        var (context, page) = Build(fake, advancedAi: true);
        await context.OpenProjectAsync(ProjectA);
        Assert.True(page.SelectFinding(Assert.Single(page.Rows, row => !row.IsHeader).Link!));

        Assert.Null(page.SelectedRecordType);
        Assert.False(page.KeepCommand.CanExecute(null));
        page.SelectedRecordType = page.RecordTypes[1];
        Assert.True(page.KeepCommand.CanExecute(null));
    }

    [Fact]
    public async Task ANotebookWithNoRecordTypeSaysSoAndOffersNoDecision()
    {
        var fake = new FakeCommandClient();
        fake.RecordTypesHandler = (_, _) => Task.FromResult(CommandOutcome<NotebookRecordTypesResponse>.Success(
            new NotebookRecordTypesResponse([])));
        ExpectReport(fake, Report([Finding("f1", ParsimonyMeasure, ParsimonyAxis.Parsimony, 2, 3)]));
        var (context, page) = Build(fake, advancedAi: true);

        await context.OpenProjectAsync(ProjectA);

        Assert.False(page.HasRecordTypes);
        Assert.Contains("no record types", page.ActionMessage, StringComparison.Ordinal);
        Assert.True(page.SelectFinding(Assert.Single(page.Rows, row => !row.IsHeader).Link!));
        Assert.False(page.KeepCommand.CanExecute(null));
    }

    [Fact]
    public async Task ARefusedDecisionShowsTheCommandsMessageAndStagesNothing()
    {
        var fake = new FakeCommandClient();
        fake.RecordDispositionHandler = (_, _) => Task.FromResult(CommandOutcome<ComposedOperationsResponse>.Refused(
            new Refusal("parsimony.report-baseline-mismatch", FailureReason.Refused,
                "The Report was measured from a different Baseline.")));
        ExpectReport(fake, Report([Finding("f1", ParsimonyMeasure, ParsimonyAxis.Parsimony, 2, 3)]));
        var (context, page) = Build(fake, advancedAi: true);
        await context.OpenProjectAsync(ProjectA);
        Assert.True(page.SelectFinding(Assert.Single(page.Rows, row => !row.IsHeader).Link!));

        await page.DeferCommand.ExecuteAsync(null);

        Assert.Equal("The Report was measured from a different Baseline.", page.ActionMessage);
        Assert.Equal(string.Empty, Assert.Single(page.Rows, row => !row.IsHeader).Disposition);
    }

    [Fact]
    public async Task PendingDecisionsAreShownAsStagedAndSurviveAReopenWhileTheDraftExists()
    {
        var fake = new FakeCommandClient();
        var finding = Finding("f1", ParsimonyMeasure, ParsimonyAxis.Parsimony, 2, 3);
        ExpectReport(fake, Report([finding]));
        fake.PendingLoadHandler = (_, _) => Task.FromResult(CommandOutcome<PendingChangesSnapshot>.Success(
            new PendingChangesSnapshot("draft-1", "revision-1", [], [])));
        var (context, page) = Build(fake, advancedAi: true);
        await context.OpenProjectAsync(ProjectA);
        Assert.True(page.SelectFinding(Assert.Single(page.Rows, row => !row.IsHeader).Link!));
        await page.DeferCommand.ExecuteAsync(null);

        await page.ReloadAsync();

        Assert.Equal("Deferred · staged, not yet applied", Assert.Single(page.Rows, row => !row.IsHeader).Disposition);
        Assert.Empty(page.SuppressedItems);
    }

    [Fact]
    public async Task ReturnToActiveStagesTheWithdrawalWithTheExactHeadAndWritesNothing()
    {
        var fake = new FakeCommandClient();
        var kept = Finding("kept", ParsimonyMeasure, ParsimonyAxis.Parsimony, 1, 3);
        ExpectReport(fake, Report([kept]), [History(kept, "keep", "suppressed", "Checked.")]);
        var (context, page) = Build(fake, advancedAi: true);
        await context.OpenProjectAsync(ProjectA);
        Assert.True(page.SelectSuppressed("revision-kept"));
        Assert.True(page.ShowSuppressedActions);

        await page.ReturnToActiveCommand.ExecuteAsync(null);

        var request = Assert.Single(fake.RetractDispositionRequests);
        Assert.Equal(PendingChanges.DraftName, request.DraftName);
        using var intent = JsonDocument.Parse(request.IntentJson);
        Assert.Equal("record-kept", intent.RootElement.GetProperty("recordId").GetString());
        var head = Assert.Single(intent.RootElement.GetProperty("expectedHeads").EnumerateArray());
        Assert.Equal("revision-kept", head.GetProperty("revisionId").GetString());
        Assert.Equal("sha256:" + new string('8', 64), head.GetProperty("contentDigest").GetString());
        Assert.Empty(fake.RecordDispositionRequests);
        Assert.Equal("Return to Active is staged. Staged in pending changes. Nothing is written to FieldWorks until Apply.",
            page.ActionMessage);
        Assert.Equal("Return to Active is staged. Apply writes it.", Assert.Single(page.SuppressedItems).ReturnLabel);
    }

    [Fact]
    public async Task OnlyAnActiveSuppressionOffersReturnAndAStaleOneSaysWhy()
    {
        var fake = new FakeCommandClient();
        var stale = Finding("stale", ParsimonyMeasure, ParsimonyAxis.Parsimony, 1, 3);
        ExpectReport(fake, Report([]), [History(stale, "defer", "no-current-finding", null)]);
        var (context, page) = Build(fake, advancedAi: true);
        await context.OpenProjectAsync(ProjectA);

        Assert.True(page.SelectSuppressed("revision-stale"));

        Assert.False(page.ReturnToActiveCommand.CanExecute(null));
        Assert.Equal("Stale: no current finding matches it", Assert.Single(page.SuppressedItems).StateLabel);
    }

    [Fact]
    public async Task AChangedEvidenceFindingComesBackToActiveWithItsEarlierReason()
    {
        var fake = new FakeCommandClient();
        var changed = Finding("changed", ParsimonyMeasure, ParsimonyAxis.Parsimony, 2, 3);
        ExpectReport(fake, Report([changed]));
        ServeActive(fake, new ParsimonyFindingDispositionViewRow(ReportId, BundleId, "resurfaced", changed, "keep",
            "Checked against the paradigm.", null, "judgment-changed", "revision-changed", "sha256:" + new string('9', 64),
            "evidence-changed", null, null));
        var (context, page) = Build(fake, advancedAi: true);

        await context.OpenProjectAsync(ProjectA);

        var row = Assert.Single(page.Rows, item => !item.IsHeader);
        Assert.Equal("Changed since it was kept: back for review", row.Disposition);
        Assert.True(page.SelectFinding(row.Link!));
        Assert.Contains(page.Evidence!.Details, detail => detail.Label == "Earlier reason" &&
            detail.Value == "Checked against the paradigm.");
    }

    [Fact]
    public async Task AConflictingDecisionStaysVisibleAndActive()
    {
        var fake = new FakeCommandClient();
        var contested = Finding("contested", ParsimonyMeasure, ParsimonyAxis.Parsimony, 2, 3);
        ExpectReport(fake, Report([contested]));
        ServeActive(fake, new ParsimonyFindingDispositionViewRow(ReportId, BundleId, "active", contested, null, null, null,
            null, null, null, "unresolved", "Two judgments name this finding.", null));
        var (context, page) = Build(fake, advancedAi: true);

        await context.OpenProjectAsync(ProjectA);

        var row = Assert.Single(page.Rows, item => !item.IsHeader);
        Assert.Equal("Conflict: Two judgments name this finding.", row.Disposition);
        Assert.Equal("Active (1)", page.ActiveTabLabel);
        Assert.Empty(page.SuppressedItems);
    }

    [Fact]
    public async Task FixStagesNothingAndSaysWhetherTheCheckHasASupportedUpdate()
    {
        var fake = new FakeCommandClient();
        ExpectReport(fake, Report([Finding("f1", ParsimonyMeasure, ParsimonyAxis.Parsimony, 2, 3)]));
        var (context, page) = Build(fake, advancedAi: true);
        await context.OpenProjectAsync(ProjectA);

        Assert.True(page.SelectFinding(Assert.Single(page.Rows, row => !row.IsHeader).Link!));

        Assert.Empty(fake.RecordDispositionRequests);
        Assert.Empty(fake.RetractDispositionRequests);
        var recipe = ParsimonyRecipeCatalog.Load().Find(ParsimonyMeasure)!;
        Assert.StartsWith(recipe.Metadata.UpdateIntents.Count > 0
            ? "The check's supported update is in its Guide page"
            : "This check has no supported update in Motif yet", page.FixNote, StringComparison.Ordinal);
    }

    // Serves the given rows as the Active view and no Suppressed rows, for a test that needs an exact Active state.
    private static void ServeActive(FakeCommandClient fake, params ParsimonyViewRow[] active) =>
        fake.ParsimonyViewHandler = (request, _) => Task.FromResult(CommandOutcome<ParsimonyNamedViewResponse>.Success(
            new ParsimonyNamedViewResponse(BundleId, request.Query.View, 1, ParsimonyMeasureStatus.Computed, [], 0, 0,
                null, false, request.Query.View == "parsimony-active-findings" ? active : [], null, null)));

    private static (WorkspaceContext Context, ParsimonyPageModel Page) Build(FakeCommandClient fake, bool advancedAi)
    {
        var selection = new SelectionViewModel(fake);
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(CurrentToken, null, true));
        var context = new WorkspaceContext(selection, new AssessViewModel(fake, selection), new ChangesViewModel(fake),
            fake, new FolderPicker(), new DragSource(), new BaselineViewModel(fake))
        {
            AdvancedAiModeEnabled = advancedAi,
        };
        return (context, new ParsimonyPageModel(context));
    }

    private static void ExpectNoReport(FakeCommandClient fake) => fake.OnReadLatestParsimony((_, _) =>
        Task.FromResult(CommandOutcome<ParsimonyLatestReportResponse>.Refused(
            new Refusal("parsimony.no-report", FailureReason.NotFound, "No Parsimony Report yet."))));

    private static void ExpectReport(FakeCommandClient fake, ParsimonyReportResponse report,
        IReadOnlyList<ParsimonySuppressionHistoryViewRow>? suppressed = null)
    {
        suppressed ??= [];
        fake.RecordDispositionHandler ??= (request, _) => Task.FromResult(CommandOutcome<ComposedOperationsResponse>.Success(
            new ComposedOperationsResponse(request.DraftName, "RecordParsimonyDisposition", [], 1)));
        fake.RetractDispositionHandler ??= (request, _) => Task.FromResult(CommandOutcome<ComposedOperationsResponse>.Success(
            new ComposedOperationsResponse(request.DraftName, "RetractParsimonyDisposition", [], 1)));
        fake.OnReadLatestParsimony((_, _) => Task.FromResult(CommandOutcome<ParsimonyLatestReportResponse>.Success(
            new ParsimonyLatestReportResponse(report.ReportId, report.Inputs.BundleId))));
        fake.OnReadParsimonyReport((_, _) => Task.FromResult(CommandOutcome<ParsimonyReportResponse>.Success(report)));
        fake.ParsimonyViewHandler = (request, _) =>
        {
            var rows = request.Query.View == "parsimony-suppressed"
                ? suppressed.Cast<ParsimonyViewRow>().ToArray()
                : report.Findings
                    .Where(finding => !suppressed.Any(row => row.JudgmentId == "judgment-" + finding.FindingId))
                    .Select(finding => (ParsimonyViewRow)new ParsimonyFindingDispositionViewRow(ReportId, BundleId,
                        "active", finding, null, null, null, null, null, null))
                    .ToArray();
            return Task.FromResult(CommandOutcome<ParsimonyNamedViewResponse>.Success(new ParsimonyNamedViewResponse(
                BundleId, request.Query.View, 1, ParsimonyMeasureStatus.Computed, [], rows.Length, rows.Length, null,
                false, rows, null, null)));
        };
    }

    private static ParsimonyReportResponse Report(
        IReadOnlyList<ParsimonyFinding> findings,
        IReadOnlyList<ParsimonyMeasureRun>? measureRuns = null,
        BaselineToken? token = null)
    {
        var inputs = new ParsimonyReportInputs(BundleId, token ?? CurrentToken, "baseline", null,
            "sha256:" + new string('3', 64), new ParsimonyArtifactDigest(4, new string('4', 64)),
            new ParsimonyArtifactDigest(1, new string('5', 64)), null, null, []);
        return new ParsimonyReportResponse(ReportId, inputs, findings, "Stored recommendation.")
        {
            JoinQuality = new ParsimonyJoinQuality("default-selection", false, 0, 0, 0, 0, 0, 0, 0, null, null, null, null, null),
            Notes = [],
            MeasureRuns = measureRuns ?? [.. findings.Select(finding => finding.MeasureId).Distinct(StringComparer.Ordinal)
                .Select(measure => new ParsimonyMeasureRun(measure, ParsimonyMeasureStatus.Computed, 3, 1, "prohibitions",
                    new ParsimonyMeasureNumber(1, 3, "prohibitions"), 0.33, null))],
        };
    }

    private static ParsimonyFinding Finding(string id, string measureId, ParsimonyAxis axis, long numerator,
        long denominator, IReadOnlyList<string>? items = null, IReadOnlyList<string>? limitations = null) => (new ParsimonyFinding(
            id, measureId, axis, ParsimonyTier.Static,
            new ParsimonyFindingAttachment(ParsimonyAttachmentKind.AuthoredObject, "attachment-" + id,
                ParsimonyAuthoredObjectKind.AdhocProhibition),
            null, new ParsimonyMeasureNumber(numerator, denominator, "prohibitions"),
            new ParsimonyMeasureThreshold(ParsimonyThresholdOperator.GreaterThan, 0, "v1"),
            "sha256:" + new string('7', 64),
            [new ParsimonyEvidenceReference(BundleId, "adhoc-context", JsonDocument.Parse("{}").RootElement)],
            "guide:parsimony/recipes/" + measureId, limitations ?? ["The count is a suggestion, not a verdict."],
            ParsimonyVerification.NotRun) with { ItemNames = items ?? [] });

    private static ParsimonySuppressionHistoryViewRow History(ParsimonyFinding finding, string disposition, string state,
        string? reason) => new("judgment-" + finding.FindingId, "revision-" + finding.FindingId,
            "record-" + finding.FindingId, "sha256:" + new string('8', 64), finding.MeasureId, "subject-" + finding.FindingId,
            "the prohibition", ParsimonyRecipeCatalog.Load().Find(finding.MeasureId)?.Title ?? "Parsimony check",
            finding.EvidenceDigest, disposition, reason, state, ReportId, ReportId, "sha256:" + new string('6', 64),
            "project", null, null, null, null, null);

    private static string Describe(ParsimonyFinding finding) =>
        $"{finding.Number.Numerator} of {finding.Number.Denominator} {finding.Number.Unit}";

    private static string ShortTitle(string measureId) => ParsimonyRecipeCatalog.Load().Find(measureId)?.ShortTitle ?? "parsimony check";

    private static string Title(string measureId) => ParsimonyRecipeCatalog.Load().Find(measureId)?.Title ?? "Parsimony check";

    private static BaselineToken Token(string digit) => new("project-identity", "sha256:" + new string(digit[0], 64),
        "projection-v1", "2026-10-05T00:00:00Z", "sha256:" + new string('2', 64));

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 500 && !condition(); attempt++) await Task.Delay(5);
        Assert.True(condition(), "The condition was never reached.");
    }

    private sealed class FolderPicker : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }

    private sealed class DragSource : IFileDragSource
    {
        public Task<DragDropEffects> StartDragAsync(PointerPressedEventArgs trigger, IReadOnlyList<string> filePaths,
            DragDropEffects allowedEffects) => Task.FromResult(allowedEffects);
    }
}
