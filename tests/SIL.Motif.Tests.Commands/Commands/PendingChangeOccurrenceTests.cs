using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Store;
using SIL.Motif.Model.DryRun;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheTestCollection.Name)]
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

        ChangeWordform(project.Path, project.OtherWordformId, "changed-second-word");
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
        Assert.Equal("changed-second-word", Assert.Single(uncertain.Uncertainty!.AfterTokens,
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
        runner.Start(project.Path);
        var dryRun = JobCommands.WaitForDryRun(new WaitForDryRunRequest(
            project.Path, ProductVersion, finalized.Value.ProposalId, queued.Value!.JobId,
            TimeSpan.FromMinutes(2)));
        Assert.True(dryRun.Succeeded, dryRun.Refusal?.Message);
        var applied = ProposalCommands.Apply(new ApplyRequest(
            project.Path, ProductVersion, finalized.Value.ProposalId, "test-user", Force: true));
        Assert.True(applied.Succeeded, applied.Refusal?.Message);
    }

    [Fact]
    public void AWordChangedInAnotherParagraphLeavesTheAnchoredChangeFitting()
    {
        var project = CreateProject();
        var pending = Collect(project);

        ChangeWordform(project.Path, project.Text.UnanalysedWordformId, "changed-other-paragraph");
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
    public void AChangedTargetAnalysisTakesPrecedenceOverSentenceUncertainty()
    {
        var project = CreateProject();
        var pending = Collect(project);
        ChangeWordform(project.Path, project.OtherWordformId, "changed-second-word");
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
        ChangeWordform(project.Path, project.OtherWordformId, "changed-second-word");
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
            paragraph.ParseIsCurrent = true;
        });
        new FwDataProjectLoader().Save(cache);
        Directory.CreateDirectory(_root);
        var scenario = new Scenario(cache.ProjectId.Path, _root, text, otherWordformId);
        Capture(scenario);
        return scenario;
    }

    private PendingChangesSnapshot Collect(Scenario project)
    {
        var initial = PendingChanges.Load(new PendingChangesRequest(project.Path, ProductVersion));
        Assert.True(initial.Succeeded, initial.Refusal?.Message);
        var anchor = new OccurrenceAnchor(project.Text.TextId, project.Text.FirstParagraphId,
            project.Text.FirstSegmentId, 0);
        var intent = new ChangeIntent("occurrence-change", AnalysisChangeKinds.Reject,
            CanonicalId.FromGuid(project.Text.AnalysedWordformId).Value, SeededProject.AnalysedWordForm,
            StoredAnalysisId: CanonicalId.FromGuid(project.Text.ApprovedAnalysisId).Value,
            OriginPage: "Texts", Occurrence: anchor);
        var put = PendingChanges.Put(new PutPendingChangeRequest(
            project.Path, ProductVersion, initial.Value!.Revision, intent));
        Assert.True(put.Succeeded, put.Refusal?.Message);
        Assert.Equal("fits", Assert.Single(put.Value!.FitSummary).Status);
        return put.Value;
    }

    private static void ChangeWordform(string path, Guid wordformId, string form)
    {
        using var cache = new FwDataProjectLoader().LoadScratchCache(path);
        var wordform = cache.ServiceLocator.GetInstance<IWfiWordformRepository>().GetObject(wordformId);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            wordform.Form.set_String(cache.DefaultVernWs, form));
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
        var operations = draft.Operations.Select(operation => new OperationEnvelope(
            CanonicalId.Parse(operation.OperationId), operation.Kind,
            operation.EntityId is null ? null : CanonicalId.Parse(operation.EntityId),
            operation.Target is null ? null : CanonicalId.Parse(operation.Target),
            JsonSerializer.SerializeToElement(operation.After), dependsOn: operation.DependsOn
                .Select(id => new OperationDependency(CanonicalId.Parse(id))).ToArray(),
            extensions: operation.Extensions)).ToArray();
        var proposal = new Proposal(draft.ContractVersions, CanonicalId.Parse(draft.ProposalId),
            draft.Requires.Select(CanonicalId.Parse).ToArray(), operations);
        return IntentDigest.Compute(proposal);
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

    private sealed record Scenario(string Path, string Root, SeededText Text, Guid OtherWordformId);
}
