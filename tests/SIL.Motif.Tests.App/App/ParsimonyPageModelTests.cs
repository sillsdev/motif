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
        Assert.Equal("Parsimony: 2 · Restrictiveness: 1", page.AxisSummary);
        Assert.Equal("3", page.Badge);
    }

    [Fact]
    public async Task KeptAndDeferredFindingsStayVisibleAsSuchAndUndecidedOnesAreNot()
    {
        var fake = new FakeCommandClient();
        var kept = Finding("kept", ParsimonyMeasure, ParsimonyAxis.Parsimony, 1, 3);
        var deferred = Finding("deferred", ParsimonyMeasure, ParsimonyAxis.Parsimony, 1, 4);
        var open = Finding("open", ParsimonyMeasure, ParsimonyAxis.Parsimony, 1, 5);
        ExpectReport(fake, Report([kept, deferred, open],
            dispositions:
            [
                Disposition(kept, "keep", "active"),
                Disposition(deferred, "defer", "suppressed"),
            ]));
        var (context, page) = Build(fake, advancedAi: true);

        await context.OpenProjectAsync(ProjectA);

        var rows = page.Groups.SelectMany(group => group.Items).ToDictionary(row => row.Description);
        Assert.Equal("Kept", rows[Describe(kept)].Disposition);
        Assert.Equal("Deferred", rows[Describe(deferred)].Disposition);
        Assert.Equal(string.Empty, rows[Describe(open)].Disposition);
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

    private static void ExpectReport(FakeCommandClient fake, ParsimonyReportResponse report)
    {
        fake.OnReadLatestParsimony((_, _) => Task.FromResult(CommandOutcome<ParsimonyLatestReportResponse>.Success(
            new ParsimonyLatestReportResponse(report.ReportId, report.Inputs.BundleId))));
        fake.OnReadParsimonyReport((_, _) => Task.FromResult(CommandOutcome<ParsimonyReportResponse>.Success(report)));
    }

    private static ParsimonyReportResponse Report(
        IReadOnlyList<ParsimonyFinding> findings,
        IReadOnlyList<ParsimonyMeasureRun>? measureRuns = null,
        IReadOnlyList<ParsimonyFindingDispositionViewRow>? dispositions = null,
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
            DispositionProjection = dispositions is null ? null : new ParsimonyDispositionProjection(BundleId, ReportId,
                null, "project", "sha256:" + new string('6', 64), 0, 0, 0, 0, dispositions, [], []),
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

    private static ParsimonyFindingDispositionViewRow Disposition(ParsimonyFinding finding, string disposition,
        string state) => new(ReportId, BundleId, state, finding, disposition, "Reviewed.", null, "judgment-" + finding.FindingId,
            "revision-" + finding.FindingId, null);

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
