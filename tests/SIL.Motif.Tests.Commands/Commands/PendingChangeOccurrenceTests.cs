using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host;
using SIL.Motif.Host.Analysis;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Store;
using SIL.Motif.Model.DryRun;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group1)]
public sealed class PendingChangeOccurrenceTests(PristineProjectFixture pristine) : IDisposable
{
    private const string ProductVersion = "1.0";
    private readonly string _root = Path.Combine(Path.GetTempPath(),
        "SIL.Motif.PendingChangeOccurrenceTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task AChangedWordInTheSameSegmentBecomesUncertainAndCanBeReconfirmedAndApplied()
    {
        var project = CreateProject();
        var pending = Collect(project);
        var intentDigest = PendingIntentDigest(project.Path);
        var originalFingerprint = ReadFingerprint(project.Path);

        EditSentenceWord(project.Path, project.Text.FirstParagraphId, project.Text.FirstSegmentId,
            1, "changedword", "motifanalysed changedword.");
        Capture(project);
        var checkedChanges = PendingChanges.Recheck(new RecheckPendingChangesRequest(
            project.Path, ProductVersion, pending.Revision));

        Assert.True(checkedChanges.Succeeded, checkedChanges.Refusal?.Message);
        var uncertain = Assert.Single(checkedChanges.Value!.FitSummary);
        Assert.Equal("uncertain", uncertain.Status);
        Assert.False(uncertain.StillFits);
        var checkedFingerprint = ReadFingerprint(project.Path);
        Assert.Equal(originalFingerprint.Occurrence!.Anchor, checkedFingerprint.Occurrence!.Anchor);
        Assert.Equal(originalFingerprint.Occurrence.WordformId, checkedFingerprint.Occurrence.WordformId);
        Assert.Equal(originalFingerprint.Occurrence.AnalysisId, checkedFingerprint.Occurrence.AnalysisId);
        Assert.Equal(originalFingerprint.Occurrence.Token, checkedFingerprint.Occurrence.Token);
        Assert.Equal(originalFingerprint.Occurrence.ParseIsCurrent, checkedFingerprint.Occurrence.ParseIsCurrent);
        Assert.Equal(originalFingerprint.Occurrence.WordDigest, checkedFingerprint.Occurrence.WordDigest);
        Assert.Equal(originalFingerprint.Occurrence.Tokens, checkedFingerprint.Occurrence.Tokens);
        Assert.NotEqual(originalFingerprint.BaselineToken, checkedFingerprint.BaselineToken);
        Assert.Equal("changedword", Assert.Single(uncertain.Uncertainty!.AfterTokens,
            token => token.Index == 1).Form);
        Assert.Equal("motifanalysed", Assert.Single(uncertain.Uncertainty.BeforeTokens,
            token => token.Index == 0).Form);
        var refusedApply = PendingChangesWorkflow.Apply(new ApplyPendingRequest(
            project.Path, checkedChanges.Value.DraftId, checkedChanges.Value.Revision, "test-user"));
        Assert.Equal("apply.change-uncertain", refusedApply.Refusal?.Code);
        Assert.Contains(uncertain.ChangeId, refusedApply.Refusal!.Message, StringComparison.Ordinal);
        Assert.Contains("check again", refusedApply.Refusal.Message, StringComparison.OrdinalIgnoreCase);

        var reconfirmed = PendingChanges.Reconfirm(new ReconfirmPendingChangeRequest(
            project.Path, ProductVersion, checkedChanges.Value.Revision, uncertain.ChangeId));

        Assert.True(reconfirmed.Succeeded, reconfirmed.Refusal?.Message);
        Assert.Equal("fits", Assert.Single(reconfirmed.Value!.FitSummary).Status);
        Assert.Equal(intentDigest, PendingIntentDigest(project.Path));
        var finalized = ProposalCommands.Finalize(new FinalizeRequest(
            project.Path, ProductVersion, PendingChanges.DraftName, reconfirmed.Value.Revision));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var runner = IsolatedRunner.Process(project.Root);
        var queued = JobCommands.EnqueueDryRun(new EnqueueDryRunRequest(
            project.Path, ProductVersion, finalized.Value!.ProposalId));
        Assert.True(queued.Succeeded, queued.Refusal?.Message);

        string frozenProposalJson;
        string operationId;
        using (var database = ProjectMotifDatabase.Open(project.Path))
        {
            var job = new JobRepository(database).Get(queued.Value!.JobId)!;
            var frozen = DryRunJobInput.Parse(job.InputJson);
            frozenProposalJson = frozen.Proposal.ProposalJson;
            using var proposal = JsonDocument.Parse(frozenProposalJson);
            operationId = proposal.RootElement.GetProperty("operations")[0].GetProperty("operationId")
                .GetString()!;
        }

        var reopened = ProposalCommands.Reopen(new ReopenRequest(project.Path, ProductVersion,
            "edited-after-enqueue", finalized.Value.ProposalId));
        Assert.True(reopened.Succeeded, reopened.Refusal?.Message);
        var removed = ProposalCommands.RemoveOperations(new RemoveOperationsRequest(project.Path,
            ProductVersion, "edited-after-enqueue", [operationId], Force: true));
        Assert.True(removed.Succeeded, removed.Refusal?.Message);
        Assert.Equal(0, removed.Value!.OperationCount);
        using (var database = ProjectMotifDatabase.Open(project.Path))
        {
            var queuedAfterEdit = new JobRepository(database).Get(queued.Value.JobId)!;
            Assert.Equal(frozenProposalJson, DryRunJobInput.Parse(queuedAfterEdit.InputJson).Proposal.ProposalJson);
        }
        var discarded = ProposalCommands.DiscardDraft(new DiscardDraftRequest(project.Path, ProductVersion,
            "edited-after-enqueue"));
        Assert.True(discarded.Succeeded, discarded.Refusal?.Message);

        runner.Start(project.Path);
        var dryRun = JobCommands.WaitForDryRun(new WaitForDryRunRequest(
            project.Path, ProductVersion, finalized.Value.ProposalId, queued.Value!.JobId,
            TimeSpan.FromMinutes(2)));
        Assert.True(dryRun.Succeeded, dryRun.Refusal?.Message);

        DryRunJobCompletion dryRunCompletion;
        using (var database = ProjectMotifDatabase.Open(project.Path))
            dryRunCompletion = DryRunJobCompletion.Parse(
                new JobRepository(database).Get(queued.Value.JobId)!.ResultJson!);
        var missingBaselinePath = dryRunCompletion.SourceBaseline.FwDataPath;
        var movedBaselinePath = missingBaselinePath + ".temporarily-missing";
        File.Move(missingBaselinePath, movedBaselinePath);
        CommandOutcome<JobEnqueuedResponse> candidate;
        try
        {
            candidate = ParsimonyCommands.EnqueueCandidate(new EnqueueParsimonyCandidateRequest(
                project.Path, ProductVersion, queued.Value.JobId, "P-adhoc-duplicate",
                ParsimonyEvidenceScopeKind.ProjectApproved));
        }
        finally
        {
            File.Move(movedBaselinePath, missingBaselinePath);
        }
        Assert.False(candidate.Succeeded);
        Assert.Equal("parsimony.dry-run-evidence-missing", candidate.Refusal?.Code);
        Assert.Contains("Rerun its Dry Run", candidate.Refusal!.Message, StringComparison.Ordinal);

        var applied = ProposalCommands.Apply(new ApplyRequest(
            project.Path, ProductVersion, finalized.Value.ProposalId, "test-user", Force: true));
        Assert.True(applied.Succeeded, applied.Refusal?.Message);
    }

    [Fact]
    public void CandidateEnqueueRefusesOlderDryRunEvidenceWithRerunInstruction()
    {
        var project = CreateProject();
        using var database = ProjectMotifDatabase.Open(project.Path);
        var projectKey = ProjectWorkspaceKey.Compute(
            new ProjectLocator(project.Path, Path.GetFileNameWithoutExtension(project.Path)));
        var now = JobTimestamp.FormatUtc(DateTimeOffset.UtcNow);
        var jobs = new JobRepository(database);
        var oldDryRun = jobs.Create(CanonicalId.Mint("job/").Value, projectKey, JobCommands.DryRunKind,
            "{}", now);
        var claims = new JobClaims(database);
        var claimed = claims.Claim(projectKey, "legacy-dry-run-test", now, TimeSpan.FromMinutes(5));
        Assert.NotNull(claimed);
        jobs.PublishDryRun(oldDryRun.JobId, "{}", claimed.Version);
        Assert.True(claims.Finish(oldDryRun.JobId, claimed.ClaimToken!, JobStatus.CompletedDryRunOnly,
            JobFailureCategory.None, "{}"));

        var refused = ParsimonyCommands.EnqueueCandidate(new EnqueueParsimonyCandidateRequest(
            project.Path, ProductVersion, oldDryRun.JobId, "P-adhoc-duplicate",
            ParsimonyEvidenceScopeKind.ProjectApproved));

        Assert.False(refused.Succeeded);
        Assert.Equal("parsimony.dry-run-evidence-missing", refused.Refusal?.Code);
        Assert.Contains("Rerun its Dry Run", refused.Refusal!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AWordChangedInAnotherParagraphLeavesTheAnchoredChangeFitting()
    {
        var project = CreateProject();
        var pending = Collect(project);

        EditSentenceWord(project.Path, project.Text.SecondParagraphId, project.Text.SecondSegmentId,
            0, "changed-other-paragraph", "changed-other-paragraph");
        Capture(project);
        var checkedChanges = PendingChanges.Recheck(new RecheckPendingChangesRequest(
            project.Path, ProductVersion, pending.Revision));

        Assert.True(checkedChanges.Succeeded, checkedChanges.Refusal?.Message);
        Assert.Equal("fits", Assert.Single(checkedChanges.Value!.FitSummary).Status);
    }

    [Fact]
    public void CollectionStoresTheOccurrenceEvidenceAndRejectsAnAnchorOnAnotherWordform()
    {
        var project = CreateProject();
        var pending = Collect(project);
        var evidence = ReadFingerprint(project.Path).Occurrence!;
        Assert.Equal(project.Text.TextId, evidence.Anchor.TextId);
        Assert.Equal(project.Text.FirstParagraphId, evidence.Anchor.ParagraphId);
        Assert.Equal(project.Text.FirstSegmentId, evidence.Anchor.SegmentId);
        Assert.Equal(0, evidence.Anchor.Index);
        Assert.Equal(CanonicalId.FromGuid(project.Text.AnalysedWordformId).Value, evidence.WordformId);
        Assert.Equal(CanonicalId.FromGuid(project.Text.ApprovedAnalysisId).Value, evidence.AnalysisId);
        Assert.True(evidence.ParseIsCurrent);
        Assert.StartsWith("sha256:", evidence.WordDigest, StringComparison.Ordinal);
        Assert.Equal("second-word", Assert.Single(evidence.Tokens, token => token.Index == 1).Form);

        var mismatch = PendingChanges.Put(new PutPendingChangeRequest(project.Path, ProductVersion,
            pending.Revision, new ChangeIntent("wrong-occurrence", AnalysisChangeKinds.Reject,
                CanonicalId.FromGuid(project.Text.AnalysedWordformId).Value, SeededProject.AnalysedWordForm,
                StoredAnalysisId: CanonicalId.FromGuid(project.Text.ApprovedAnalysisId).Value,
                Occurrence: new OccurrenceAnchor(project.Text.TextId, project.Text.FirstParagraphId,
                    project.Text.FirstSegmentId, 1))));

        Assert.Equal("change.occurrence-wordform-mismatch", mismatch.Refusal?.Code);
    }

    [Fact]
    public void DeletingTheAnchoredSegmentMakesTheChangeUncertain()
    {
        var project = CreateProject();
        var pending = Collect(project);
        using (var cache = new FwDataProjectLoader().LoadScratchCache(project.Path))
        {
            var paragraph = cache.ServiceLocator.GetInstance<IStTxtParaRepository>()
                .GetObject(project.Text.FirstParagraphId);
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () => paragraph.SegmentsOS[0].Delete());
            new FwDataProjectLoader().Save(cache);
        }
        Touch(project.Path);
        Capture(project);

        var checkedChanges = PendingChanges.Recheck(new RecheckPendingChangesRequest(
            project.Path, ProductVersion, pending.Revision));

        Assert.True(checkedChanges.Succeeded, checkedChanges.Refusal?.Message);
        var fit = Assert.Single(checkedChanges.Value!.FitSummary);
        Assert.Equal("uncertain", fit.Status);
        Assert.Contains("Segment", fit.Uncertainty!.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AStaleParagraphParseMakesTheChangeUncertain()
    {
        var project = CreateProject();
        var pending = Collect(project);
        using (var cache = new FwDataProjectLoader().LoadScratchCache(project.Path))
        {
            var paragraph = cache.ServiceLocator.GetInstance<IStTxtParaRepository>()
                .GetObject(project.Text.FirstParagraphId);
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () => paragraph.ParseIsCurrent = false);
            new FwDataProjectLoader().Save(cache);
        }
        Touch(project.Path);
        Capture(project);

        var checkedChanges = PendingChanges.Recheck(new RecheckPendingChangesRequest(
            project.Path, ProductVersion, pending.Revision));

        Assert.True(checkedChanges.Succeeded, checkedChanges.Refusal?.Message);
        var fit = Assert.Single(checkedChanges.Value!.FitSummary);
        Assert.Equal("uncertain", fit.Status);
        Assert.Contains("parse", fit.Uncertainty!.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PutRefusesAnOccurrenceWhenItsParagraphParseIsStale()
    {
        var project = CreateProject();
        SetParseCurrent(project.Path, project.Text.FirstParagraphId, false);
        Capture(project);
        var initial = PendingChanges.Load(new PendingChangesRequest(project.Path, ProductVersion));
        Assert.True(initial.Succeeded, initial.Refusal?.Message);
        var intent = new ChangeIntent("stale-parse-change", AnalysisChangeKinds.Reject,
            CanonicalId.FromGuid(project.Text.AnalysedWordformId).Value, SeededProject.AnalysedWordForm,
            StoredAnalysisId: CanonicalId.FromGuid(project.Text.ApprovedAnalysisId).Value,
            Occurrence: new OccurrenceAnchor(project.Text.TextId, project.Text.FirstParagraphId,
                project.Text.FirstSegmentId, 0));

        var put = PendingChanges.Put(new PutPendingChangeRequest(
            project.Path, ProductVersion, initial.Value!.Revision, intent));

        Assert.Equal("change.occurrence-unavailable", put.Refusal?.Code);
    }

    [Fact]
    public void ReconfirmRefusesWhenTheNewOccurrenceEvidenceIsStillUncertain()
    {
        var project = CreateProject();
        var pending = Collect(project);
        SetParseCurrent(project.Path, project.Text.FirstParagraphId, false);
        Capture(project);
        var checkedChanges = PendingChanges.Recheck(new RecheckPendingChangesRequest(
            project.Path, ProductVersion, pending.Revision));
        Assert.True(checkedChanges.Succeeded, checkedChanges.Refusal?.Message);
        Assert.Equal("uncertain", Assert.Single(checkedChanges.Value!.FitSummary).Status);

        var reconfirmed = PendingChanges.Reconfirm(new ReconfirmPendingChangeRequest(
            project.Path, ProductVersion, checkedChanges.Value.Revision,
            Assert.Single(checkedChanges.Value.Changes).ChangeId));

        Assert.Equal("change.occurrence-unavailable", reconfirmed.Refusal?.Code);
    }

    [Fact]
    public void ReconfirmRefusesAChangeThatDoesNotNeedAnotherCheckWithItsOwnCode()
    {
        var project = CreateProject();
        var pending = Collect(project);
        var change = Assert.Single(pending.Changes);

        var reconfirmed = PendingChanges.Reconfirm(new ReconfirmPendingChangeRequest(
            project.Path, ProductVersion, pending.Revision, change.ChangeId));

        Assert.Equal("change.reconfirm-unneeded", reconfirmed.Refusal?.Code);
    }

    [Fact]
    public void SplittingTheAnchoredSegmentMakesTheChangeUncertain()
    {
        var project = CreateProject();
        var pending = Collect(project);
        SplitFirstSegment(project.Path, project.Text.FirstParagraphId);
        Capture(project);

        var checkedChanges = PendingChanges.Recheck(new RecheckPendingChangesRequest(
            project.Path, ProductVersion, pending.Revision));

        Assert.True(checkedChanges.Succeeded, checkedChanges.Refusal?.Message);
        var fit = Assert.Single(checkedChanges.Value!.FitSummary);
        Assert.Equal("uncertain", fit.Status);
        Assert.Contains("Segment", fit.Uncertainty!.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void RepeatedWordformsWithAnInsertedMatchRemainUncertain()
    {
        var project = CreateRepeatedWordProject();
        var pending = Collect(project, occurrenceIndex: 1);
        InsertRepeatedWordBeforeAnchor(project.Path, project.Text.FirstParagraphId,
            project.Text.FirstSegmentId);
        Capture(project);

        var checkedChanges = PendingChanges.Recheck(new RecheckPendingChangesRequest(
            project.Path, ProductVersion, pending.Revision));

        Assert.True(checkedChanges.Succeeded, checkedChanges.Refusal?.Message);
        Assert.Equal("uncertain", Assert.Single(checkedChanges.Value!.FitSummary).Status);
    }

    [Fact]
    public void InsertingPunctuationBeforeRepeatedWordsKeepsTheWordOrdinal()
    {
        var project = CreateRepeatedWordProject();
        var pending = Collect(project, occurrenceIndex: 1);
        Assert.Equal(1, ReadOccurrenceWordPosition(project.Path));
        InsertPunctuationBeforeWords(project.Path, project.Text.FirstParagraphId, project.Text.FirstSegmentId);
        Capture(project);

        var checkedChanges = PendingChanges.Recheck(new RecheckPendingChangesRequest(
            project.Path, ProductVersion, pending.Revision));

        Assert.True(checkedChanges.Succeeded, checkedChanges.Refusal?.Message);
        Assert.Equal("fits", Assert.Single(checkedChanges.Value!.FitSummary).Status);
    }

    [Fact]
    public void MissingChangeMappingReplacesOtherReasonsAsBeforeOccurrenceEvidence()
    {
        var project = CreateProject();
        var pending = Collect(project, includeOccurrence: false);
        ChangeTargetAnalysis(project.Path, project.Text.ApprovedAnalysisId, pristine.Seed.SecondEntryId);
        Capture(project);
        RemoveComposerProvenance(project.Path);

        var loaded = PendingChanges.Load(new PendingChangesRequest(project.Path, ProductVersion));

        Assert.True(loaded.Succeeded, loaded.Refusal?.Message);
        Assert.Equal(["Change mapping or fingerprint is missing."], Assert.Single(loaded.Value!.FitSummary).Reasons);
    }

    [Fact]
    public void AChangedTargetAnalysisTakesPrecedenceOverSentenceUncertainty()
    {
        var project = CreateProject();
        var pending = Collect(project);
        EditSentenceWord(project.Path, project.Text.FirstParagraphId, project.Text.FirstSegmentId,
            1, "changedword", "motifanalysed changedword.");
        ChangeTargetAnalysis(project.Path, project.Text.ApprovedAnalysisId, pristine.Seed.SecondEntryId);
        Capture(project);

        var checkedChanges = PendingChanges.Recheck(new RecheckPendingChangesRequest(
            project.Path, ProductVersion, pending.Revision));

        Assert.True(checkedChanges.Succeeded, checkedChanges.Refusal?.Message);
        var fit = Assert.Single(checkedChanges.Value!.FitSummary);
        Assert.Equal("no-longer-fits", fit.Status);
        Assert.Contains("reading", string.Join(" ", fit.Reasons), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnUncertainChangeCannotBeAppliedWithForce()
    {
        var project = CreateProject();
        var pending = Collect(project);
        EditSentenceWord(project.Path, project.Text.FirstParagraphId, project.Text.FirstSegmentId,
            1, "changedword", "motifanalysed changedword.");
        Capture(project);
        var checkedChanges = PendingChanges.Recheck(new RecheckPendingChangesRequest(
            project.Path, ProductVersion, pending.Revision));
        Assert.True(checkedChanges.Succeeded, checkedChanges.Refusal?.Message);
        Assert.Equal("uncertain", Assert.Single(checkedChanges.Value!.FitSummary).Status);
        var finalized = ProposalCommands.Finalize(new FinalizeRequest(
            project.Path, ProductVersion, PendingChanges.DraftName, checkedChanges.Value.Revision));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        using (var database = ProjectMotifDatabase.Open(project.Path))
        {
            var anchor = new BoundDryRunAnchor(
                "sha256:" + new string('a', 64), "sha256:" + new string('b', 64),
                "sha256:" + new string('c', 64), "1.0", "11.0.0", "1", "20260925T000000Z");
            new ProposalRepository(database).SetAnchor(CanonicalId.Parse(finalized.Value!.ProposalId),
                JsonSerializer.Serialize(anchor));
        }

        var applied = ProposalCommands.Apply(new ApplyRequest(
            project.Path, ProductVersion, finalized.Value!.ProposalId, "test-user", Force: true));

        Assert.Equal("apply.change-uncertain", applied.Refusal?.Code);
        Assert.Contains(Assert.Single(checkedChanges.Value.FitSummary).ChangeId,
            applied.Refusal!.Message, StringComparison.Ordinal);
        Assert.Contains("check again", applied.Refusal.Message, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private Scenario CreateProject()
    {
        using var cache = pristine.NewScratch();
        var text = SeededProject.SeedText(cache, pristine.Seed);
        Guid otherWordformId = Guid.Empty;
        var paragraph = cache.ServiceLocator.GetInstance<IStTxtParaRepository>()
            .GetObject(text.FirstParagraphId);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var otherWordform = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString("second-word", cache.DefaultVernWs));
            otherWordformId = otherWordform.Guid;
            paragraph.SegmentsOS[0].AnalysesRS.Insert(1, otherWordform);
            paragraph.Contents = TsStringUtils.MakeString(
                $"{SeededProject.AnalysedWordForm} second-word{SeededProject.PunctuationForm}",
                cache.DefaultVernWs);
            paragraph.ParseIsCurrent = true;
        });
        new FwDataProjectLoader().Save(cache);
        Directory.CreateDirectory(_root);
        var scenario = new Scenario(cache.ProjectId.Path, _root, text, otherWordformId);
        Capture(scenario);
        return scenario;
    }

    private PendingChangesSnapshot Collect(Scenario project, int occurrenceIndex = 0,
        bool includeOccurrence = true)
    {
        var initial = PendingChanges.Load(new PendingChangesRequest(project.Path, ProductVersion));
        Assert.True(initial.Succeeded, initial.Refusal?.Message);
        var anchor = new OccurrenceAnchor(project.Text.TextId, project.Text.FirstParagraphId,
            project.Text.FirstSegmentId, occurrenceIndex);
        var intent = new ChangeIntent("occurrence-change", AnalysisChangeKinds.Reject,
            CanonicalId.FromGuid(occurrenceIndex == 0 ? project.Text.AnalysedWordformId : project.OtherWordformId)
                .Value,
            occurrenceIndex == 0 ? SeededProject.AnalysedWordForm : "second-word",
            StoredAnalysisId: occurrenceIndex == 0
                ? CanonicalId.FromGuid(project.Text.ApprovedAnalysisId).Value
                : CanonicalId.FromGuid(project.OtherAnalysisId).Value,
            OriginPage: "Texts", Occurrence: includeOccurrence ? anchor : null);
        var put = PendingChanges.Put(new PutPendingChangeRequest(
            project.Path, ProductVersion, initial.Value!.Revision, intent));
        Assert.True(put.Succeeded, put.Refusal?.Message);
        Assert.Equal("fits", Assert.Single(put.Value!.FitSummary).Status);
        return put.Value;
    }

    private static void EditSentenceWord(string path, Guid paragraphId, Guid segmentId,
        int analysisIndex, string replacementForm, string contents)
    {
        using var cache = new FwDataProjectLoader().LoadScratchCache(path);
        var paragraph = cache.ServiceLocator.GetInstance<IStTxtParaRepository>().GetObject(paragraphId);
        var segment = cache.ServiceLocator.GetInstance<ISegmentRepository>().GetObject(segmentId);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var replacement = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString(replacementForm, cache.DefaultVernWs));
            segment.AnalysesRS.RemoveAt(analysisIndex);
            segment.AnalysesRS.Insert(analysisIndex, replacement);
            paragraph.Contents = TsStringUtils.MakeString(contents, cache.DefaultVernWs);
            paragraph.ParseIsCurrent = true;
        });
        new FwDataProjectLoader().Save(cache);
        Touch(path);
    }

    private void SetParseCurrent(string path, Guid paragraphId, bool current)
    {
        using var cache = new FwDataProjectLoader().LoadScratchCache(path);
        var paragraph = cache.ServiceLocator.GetInstance<IStTxtParaRepository>().GetObject(paragraphId);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () => paragraph.ParseIsCurrent = current);
        new FwDataProjectLoader().Save(cache);
        Touch(path);
    }

    private void SplitFirstSegment(string path, Guid paragraphId)
    {
        using var cache = new FwDataProjectLoader().LoadScratchCache(path);
        var paragraph = cache.ServiceLocator.GetInstance<IStTxtParaRepository>().GetObject(paragraphId);
        var original = paragraph.SegmentsOS[0];
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var first = cache.ServiceLocator.GetInstance<ISegmentFactory>().Create();
            var second = cache.ServiceLocator.GetInstance<ISegmentFactory>().Create();
            paragraph.SegmentsOS.Add(first);
            paragraph.SegmentsOS.Add(second);
            first.AnalysesRS.Add(original.AnalysesRS[0]);
            second.AnalysesRS.Add(original.AnalysesRS[1]);
            second.AnalysesRS.Add(original.AnalysesRS[2]);
            original.Delete();
            paragraph.Contents = TsStringUtils.MakeString("analysed-word. second-word.", cache.DefaultVernWs);
            paragraph.ParseIsCurrent = true;
        });
        new FwDataProjectLoader().Save(cache);
        Touch(path);
    }

    private Scenario CreateRepeatedWordProject()
    {
        using var cache = pristine.NewScratch();
        var text = SeededProject.SeedText(cache, pristine.Seed);
        var paragraph = cache.ServiceLocator.GetInstance<IStTxtParaRepository>()
            .GetObject(text.FirstParagraphId);
        Guid otherWordformId = Guid.Empty;
        Guid otherAnalysisId = Guid.Empty;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var otherWordform = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString("second-word", cache.DefaultVernWs));
            otherWordformId = otherWordform.Guid;
            var sourceAnalysis = cache.ServiceLocator.GetInstance<IWfiAnalysisRepository>()
                .GetObject(text.ApprovedAnalysisId);
            var otherAnalysis = cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
            otherWordform.AnalysesOC.Add(otherAnalysis);
            otherAnalysis.CategoryRA = sourceAnalysis.CategoryRA;
            foreach (var sourceBundle in sourceAnalysis.MorphBundlesOS)
            {
                var bundle = cache.ServiceLocator.GetInstance<IWfiMorphBundleFactory>().Create();
                otherAnalysis.MorphBundlesOS.Add(bundle);
                bundle.MorphRA = sourceBundle.MorphRA;
                bundle.MsaRA = sourceBundle.MsaRA;
                if (sourceBundle.SenseRA is { } sense) bundle.SenseRA = sense;
            }
            otherAnalysisId = otherAnalysis.Guid;
            while (paragraph.SegmentsOS[0].AnalysesRS.Count > 0)
                paragraph.SegmentsOS[0].AnalysesRS.RemoveAt(0);
            paragraph.SegmentsOS[0].AnalysesRS.Add(otherAnalysis);
            paragraph.SegmentsOS[0].AnalysesRS.Add(otherAnalysis);
            paragraph.Contents = TsStringUtils.MakeString("second-word second-word", cache.DefaultVernWs);
            paragraph.ParseIsCurrent = true;
        });
        new FwDataProjectLoader().Save(cache);
        Directory.CreateDirectory(_root);
        var scenario = new Scenario(cache.ProjectId.Path, _root, text, otherWordformId, otherAnalysisId);
        Capture(scenario);
        return scenario;
    }

    private static void InsertRepeatedWordBeforeAnchor(string path, Guid paragraphId, Guid segmentId)
    {
        using var cache = new FwDataProjectLoader().LoadScratchCache(path);
        var paragraph = cache.ServiceLocator.GetInstance<IStTxtParaRepository>().GetObject(paragraphId);
        var segment = cache.ServiceLocator.GetInstance<ISegmentRepository>().GetObject(segmentId);
        var same = segment.AnalysesRS[0];
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            segment.AnalysesRS.Insert(0, same);
            paragraph.Contents = TsStringUtils.MakeString("second-word second-word second-word",
                cache.DefaultVernWs);
            paragraph.ParseIsCurrent = true;
        });
        new FwDataProjectLoader().Save(cache);
        Touch(path);
    }

    private static void InsertPunctuationBeforeWords(string path, Guid paragraphId, Guid segmentId)
    {
        using var cache = new FwDataProjectLoader().LoadScratchCache(path);
        var paragraph = cache.ServiceLocator.GetInstance<IStTxtParaRepository>().GetObject(paragraphId);
        var segment = cache.ServiceLocator.GetInstance<ISegmentRepository>().GetObject(segmentId);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var punctuation = cache.ServiceLocator.GetInstance<IPunctuationFormFactory>().Create();
            punctuation.Form = TsStringUtils.MakeString(",", cache.DefaultVernWs);
            segment.AnalysesRS.Insert(0, punctuation);
            paragraph.Contents = TsStringUtils.MakeString(", second-word second-word", cache.DefaultVernWs);
            paragraph.ParseIsCurrent = true;
        });
        new FwDataProjectLoader().Save(cache);
        Touch(path);
    }

    private static void ChangeTargetAnalysis(string path, Guid analysisId, Guid entryId)
    {
        using var cache = new FwDataProjectLoader().LoadScratchCache(path);
        var analysis = cache.ServiceLocator.GetInstance<IWfiAnalysisRepository>().GetObject(analysisId);
        var entry = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(entryId);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            analysis.MorphBundlesOS[0].MorphRA = entry.LexemeFormOA);
        new FwDataProjectLoader().Save(cache);
        Touch(path);
    }

    private static void Touch(string path) => File.SetLastWriteTimeUtc(path,
        File.GetLastWriteTimeUtc(path).AddMinutes(1));

    private static void Capture(Scenario project)
    {
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(project.Path), project.Root);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
    }

    private static string PendingIntentDigest(string path)
    {
        using var database = ProjectMotifDatabase.Open(path);
        var draftRecord = new ProposalRepository(database).GetDraft(PendingChanges.DraftName);
        var draft = JsonSerializer.Deserialize<DraftDocument>(draftRecord.ProposalJson!,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var proposal = ProposalJsonParser.Parse(ProposalCommands.BuildProposalJson(draft));
        return IntentDigest.Compute(proposal);
    }

    private static void RemoveComposerProvenance(string path)
    {
        using var database = ProjectMotifDatabase.Open(path);
        var repository = new ProposalRepository(database);
        var current = repository.GetDraft(PendingChanges.DraftName);
        var draft = JsonSerializer.Deserialize<DraftDocument>(current.ProposalJson!,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        draft.ComposerProvenance.Clear();
        var replacement = JsonSerializer.Serialize(draft, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.True(repository.TrySaveDraft(PendingChanges.DraftName, current.ProposalJson!, replacement));
    }

    private static int ReadOccurrenceWordPosition(string path)
    {
        using var database = ProjectMotifDatabase.Open(path);
        var draftRecord = new ProposalRepository(database).GetDraft(PendingChanges.DraftName);
        using var document = JsonDocument.Parse(draftRecord.ProposalJson!);
        return document.RootElement.GetProperty("operations")[0].GetProperty("extensions")
            .GetProperty("changeFit").GetProperty("occurrence").GetProperty("wordPosition").GetInt32();
    }

    private static ChangeFitFingerprint ReadFingerprint(string path)
    {
        using var database = ProjectMotifDatabase.Open(path);
        var draftRecord = new ProposalRepository(database).GetDraft(PendingChanges.DraftName);
        var draft = JsonSerializer.Deserialize<DraftDocument>(draftRecord.ProposalJson!,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var fit = draft.Operations[0].Extensions!.Value.GetProperty("changeFit");
        return JsonSerializer.Deserialize<ChangeFitFingerprint>(fit.GetRawText(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    private sealed record Scenario(string Path, string Root, SeededText Text, Guid OtherWordformId,
        Guid OtherAnalysisId = default);
}
