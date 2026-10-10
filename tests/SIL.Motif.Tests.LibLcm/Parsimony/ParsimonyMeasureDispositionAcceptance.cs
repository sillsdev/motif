using System.Security.Cryptography;
using System.Text;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Parsimony;
using SIL.Motif.LiveHost.HumanJudgments;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Model.DryRun;
using SIL.Motif.Projection.HumanJudgments;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.DryRun;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using SIL.Motif.Worker;
using Xunit;

namespace SIL.Motif.Tests.Parsimony;

internal static class ParsimonyMeasureDispositionAcceptance
{
    private const string ProductVersion = "0.1.0";
    private static readonly string[] NotebookEffects =
    [
        SnapshotFields.RnResearchNbkRecords,
        SnapshotFields.RnGenericRecType,
        SnapshotFields.RnGenericRecTitle,
        SnapshotFields.RnGenericRecDescription,
        SnapshotFields.StTextParagraphs,
        SnapshotFields.StTxtParaContents,
        SnapshotFields.RnGenericRecMotifHumanJudgment,
    ];

    public static async Task AssertEveryNoEditRouteAsync(string projectPath, string workerRoot,
        string measureId, string keepReason, string question, string deferReason,
        IReadOnlyList<string>? assessmentWords = null, string? findingIdentity = null,
        string? findingIdPrefix = null)
    {
        Directory.CreateDirectory(workerRoot);
        var initialized = ProjectInitializationCommand.Initialize(
            new ProjectInitializationRequest(projectPath, ProjectInitializationCommand.ConfirmationPhrase), workerRoot);
        Assert.True(initialized.Succeeded, initialized.Refusal?.Message);
        EnsureNotebookRecordType(projectPath);
        var initialProject = await File.ReadAllBytesAsync(projectPath);
        try
        {
            foreach (var route in new[]
            {
                new Route("keep", keepReason, null),
                new Route("ask", "The question is pending; no grammar change is proposed.", question),
                new Route("defer", deferReason, null),
            })
            {
                await File.WriteAllBytesAsync(projectPath, initialProject);
                var report = await RefreshReportAsync(projectPath, workerRoot, measureId, assessmentWords);
                Assert.Equal([measureId], report.MeasureRuns.Select(item => item.MeasureId));
                var findings = report.Findings.Where(item => item.MeasureId == measureId).ToArray();
                Assert.True(findings.Length > 0, report.MeasureRuns.Single().Detail);
                var finding = FindFinding(findings, findingIdentity, findingIdPrefix);
                Assert.Equal(measureId, finding.MeasureId);
                Assert.Equal("active", Assert.Single(report.DispositionProjection!.Findings,
                    item => item.Finding.FindingId == finding.FindingId).State);
                await AssertRouteAsync(projectPath, workerRoot, report, finding, measureId, route,
                    assessmentWords);
            }
        }
        finally
        {
            await File.WriteAllBytesAsync(projectPath, initialProject);
        }
    }

    private static async Task AssertRouteAsync(string projectPath, string workerRoot,
        ParsimonyReportResponse report, ParsimonyFinding finding, string measureId, Route route,
        IReadOnlyList<string>? assessmentWords)
    {
        var draft = $"measure-disposition-{measureId.Replace('-', '_')}-{route.Disposition}";
        var created = ProposalCommands.New(new NewDraftRequest(projectPath, ProductVersion,
            draft, "Record this measure's no-edit route against its actual finding."));
        Assert.True(created.Succeeded, created.Refusal?.Message);

        var recordType = Assert.Single(ParsimonyCommands.ListNotebookRecordTypes(
            new ListNotebookRecordTypesRequest(projectPath, ProductVersion))
            .Value!.RecordTypes);
        var currentBaseline = CurrentBaselineQuery.Query(new CurrentBaselineRequest(projectPath));
        Assert.True(currentBaseline.Succeeded, currentBaseline.Refusal?.Message);
        Assert.Equal(report.Inputs.BaselineToken, currentBaseline.Value!.Token);
        var composed = ParsimonyCommands.RecordDisposition(new RecordParsimonyDispositionFromFindingRequest(
            projectPath, ProductVersion, report.ReportId, finding.FindingId,
            route.Disposition, recordType.Id, draft, route.Reason, route.Question));
        Assert.True(composed.Succeeded, composed.Refusal?.Message);
        Assert.Equal(7, composed.Value!.Operations.Count);
        Assert.True(ProposalCommands.Comment(new CommentRequest(projectPath, ProductVersion,
            draft, "Keep the exact measure finding and its evidence in the Notebook decision.")).Succeeded);
        var finalized = ProposalCommands.Finalize(new FinalizeRequest(projectPath, ProductVersion,
            draft));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var loaded = ProjectStoreCommand.Run<Proposal>(projectPath, ProductVersion, (database, _) =>
            CommandOutcome<Proposal>.Success(ProposalJsonParser.Parse(new ProposalRepository(database)
                .Get(CanonicalId.Parse(finalized.Value!.ProposalId)).ProposalJson!)));
        Assert.True(loaded.Succeeded, loaded.Refusal?.Message);
        var proposal = loaded.Value!;

        using (var live = new FwDataProjectLoader().LoadScratchCache(projectPath))
        {
            Assert.DoesNotContain(HumanJudgmentReader.Read(live).Judgments,
                item => item.Judgment.Body is DispositionJudgment body && body.MeasureId == measureId);
            var dryRun = ScratchDryRun.Of(live, proposal);
            Assert.Equal(NotebookEffects.Order(StringComparer.Ordinal),
                dryRun.ExpectedEffects.Select(effect => effect.Field).Order(StringComparer.Ordinal));
            Assert.Equal(7, dryRun.ExpectedEffects.Count);
            var receipt = ProposalApplier.Apply(live, proposal, dryRun.Anchor, "reviewer");
            Assert.False(receipt.AlreadyApplied);
            Assert.Equal(NotebookEffects.Order(StringComparer.Ordinal),
                receipt.ActualEffects.Select(effect => effect.Field).Order(StringComparer.Ordinal));
            new FwDataProjectLoader().Save(live);
        }

        HumanJudgmentLineageProjection judgments;
        using (var reopened = new FwDataProjectLoader().LoadScratchCache(projectPath))
        {
            var snapshot = HumanJudgmentReader.Read(reopened);
            var saved = Assert.Single(snapshot.Judgments,
                item => item.Judgment.Body is DispositionJudgment body && body.MeasureId == measureId).Judgment;
            Assert.Equal(route.Reason, saved.Reason);
            Assert.Equal(report.ReportId, saved.Source!.ReportId);
            var body = Assert.IsType<DispositionJudgment>(saved.Body);
            Assert.Equal(measureId, body.MeasureId);
            Assert.Equal(ParseDisposition(route.Disposition), body.Disposition);
            Assert.Equal(StripDigest(finding.EvidenceDigest), StripDigest(body.EvidenceDigest));
            Assert.Equal(MeasureCatalog.Find(measureId)!.QueryId, body.EvidenceContract);
            Assert.Equal(route.Question, body.Question);
            judgments = JudgmentLineageResolver.Resolve(snapshot);
        }

        var refreshedReport = await RefreshReportAsync(projectPath, workerRoot, measureId, assessmentWords);
        var refreshedFinding = Assert.Single(refreshedReport.Findings, item => item.MeasureId == measureId &&
            item.FindingId == finding.FindingId);
        Assert.Equal(StripDigest(finding.EvidenceDigest), StripDigest(refreshedFinding.EvidenceDigest));
        var refreshedRow = Assert.Single(refreshedReport.DispositionProjection!.Findings,
            item => item.Finding.FindingId == finding.FindingId);
        Assert.Equal(route.Disposition == "ask" ? "active" : "suppressed", refreshedRow.State);

        if (route.Disposition is "keep" or "defer")
        {
            var changedEvidence = refreshedFinding with { EvidenceDigest = ChangedDigest(refreshedFinding.EvidenceDigest) };
            var resurfaced = ParsimonyDispositionQuery.Project(refreshedReport.Inputs.BundleId,
                refreshedReport.ReportId, refreshedReport.Inputs.BaselineToken.BundleDigest,
                [changedEvidence], judgments);
            var row = Assert.Single(resurfaced.Findings);
            Assert.Equal("resurfaced", row.State);
            Assert.Equal(route.Reason, row.Reason);
            Assert.Equal(1, resurfaced.ResurfacedCount);
        }

        await Task.CompletedTask;
    }

    private static async Task<ParsimonyReportResponse> RefreshReportAsync(string projectPath, string workerRoot,
        string measureId, IReadOnlyList<string>? assessmentWords)
    {
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(projectPath), workerRoot);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);

        IReadOnlyList<string> assessmentIds = [];
        if (assessmentWords is { Count: > 0 })
        {
            var parserPath = PanGlossExecutable.TryLocate();
            Assert.False(string.IsNullOrWhiteSpace(parserPath));
            var stepLimit = new StepCap(StepCap.DefaultSteps);
            var assessment = AssessCommand.Assess(new AssessRequest(projectPath,
                    new SelectionRequest(false, [], assessmentWords, false, null,
                        PerWordStepLimit: stepLimit), PerWordLimitMs: 1000, PerWordStepLimit: stepLimit),
                workerRoot, parserPath, onProgress: null, cancellationToken: CancellationToken.None);
            Assert.True(assessment.Succeeded, assessment.Refusal?.Message);
            assessmentIds = assessment.Value!.Measurements
                .Where(item => item.Kind == AssessmentKind.ParseTime.ToStoredKind())
                .Select(item => item.AssessmentId).ToArray();
            Assert.Single(assessmentIds);
        }

        var queued = ParsimonyCommands.Enqueue(new EnqueueParsimonyReportRequest(projectPath,
            ProductVersion, measureId, ParsimonyEvidenceScopeKind.ProjectApproved, assessmentIds));
        Assert.True(queued.Succeeded, queued.Refusal?.Message);
        var parser = PanGlossExecutable.TryLocate();
        await using (var runner = new InProcessRunnerLauncher(new JobRunnerLaunchOptions(workerRoot, parser)))
        {
            runner.Start(projectPath);
            await runner.WhenIdleAsync().WaitAsync(TimeSpan.FromMinutes(5));
        }

        var result = ParsimonyCommands.Wait(new WaitForParsimonyReportRequest(projectPath,
            ProductVersion, queued.Value!.JobId, TimeSpan.FromMinutes(1)));
        Assert.True(result.Succeeded, result.Refusal?.Message);
        return result.Value!;
    }

    private static ParsimonyDispositionKind ParseDisposition(string value) => value switch
    {
        "keep" => ParsimonyDispositionKind.Keep,
        "ask" => ParsimonyDispositionKind.Ask,
        "defer" => ParsimonyDispositionKind.Defer,
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static void EnsureNotebookRecordType(string projectPath)
    {
        var loader = new FwDataProjectLoader();
        using var cache = loader.LoadCache(projectPath);
        var notebook = cache.LangProject.ResearchNotebookOA;
        if (notebook.RecTypesOA?.PossibilitiesOS.Count > 0) return;

        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var recordTypes = notebook.RecTypesOA ?? cache.ServiceLocator
                .GetInstance<ICmPossibilityListFactory>().Create();
            notebook.RecTypesOA = recordTypes;
            var recordType = cache.ServiceLocator.GetInstance<ICmPossibilityFactory>().Create();
            recordTypes.PossibilitiesOS.Add(recordType);
            recordType.Name.set_String(cache.DefaultAnalWs,
                TsStringUtils.MakeString("Motif human judgment", cache.DefaultAnalWs));
        });
        loader.Save(cache);
    }

    private static string StripDigest(string value) => value.StartsWith("sha256:", StringComparison.Ordinal)
        ? value[7..]
        : value;

    private static ParsimonyFinding FindFinding(IReadOnlyList<ParsimonyFinding> findings,
        string? findingIdentity, string? findingIdPrefix)
    {
        if (findingIdentity is not null)
            return findings.First(item => MatchesFindingIdentity(item.AttachesTo.Identity, findingIdentity));
        return findingIdPrefix is null
            ? findings[0]
            : Assert.Single(findings, item => item.FindingId.StartsWith(findingIdPrefix,
                StringComparison.Ordinal));
    }

    private static bool MatchesFindingIdentity(string actual, string expected)
    {
        if (string.Equals(actual, expected, StringComparison.Ordinal)) return true;
        return CanonicalId.TryParse(expected, out var portable) &&
               Guid.TryParse(actual, out var guid) && portable.ToGuid() == guid;
    }

    private static string ChangedDigest(string value)
    {
        var changed = SHA256.HashData(Encoding.UTF8.GetBytes(StripDigest(value) + ":relevant-evidence-changed"));
        return Convert.ToHexStringLower(changed);
    }

    private sealed record Route(string Disposition, string Reason, string? Question);
}

public sealed class ParsimonyFactsFactAttribute : Xunit.FactAttribute
{
    public ParsimonyFactsFactAttribute()
    {
        if (PanGlossExecutable.TryLocate() is null)
            Skip = $"PanGloss was not found; Parsimony grammar facts are unavailable. Set {PanGlossExecutable.PathVariable}.";
    }
}
