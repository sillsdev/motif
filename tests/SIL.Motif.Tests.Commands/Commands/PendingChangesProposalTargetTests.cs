using SIL.Motif.Host.Assess;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host.Analysis;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group1)]
public sealed class PendingChangesProposalTargetTests
{
    private readonly PristineProjectFixture _pristine;
    private readonly string _path;

    public PendingChangesProposalTargetTests(PristineProjectFixture pristine)
    {
        _pristine = pristine;
        using var scratch = pristine.NewScratch();
        _path = scratch.ProjectId.Path;
    }

    private (Guid First, Guid Second) CaptureTwoWords()
    {
        Guid first = default, second = default;
        var loader = new FwDataProjectLoader();
        using (var cache = loader.LoadCache(_path))
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var factory = cache.ServiceLocator.GetInstance<IWfiWordformFactory>();
                first = factory.Create(TsStringUtils.MakeString("alpha-word", cache.DefaultVernWs)).Guid;
                second = factory.Create(TsStringUtils.MakeString("beta-word", cache.DefaultVernWs)).Guid;
            });
            loader.Save(cache);
        }
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(_path),
            Path.Combine(Path.GetDirectoryName(_path)!, "target-managed"));
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        return (first, second);
    }

    private string NewProposal(string name) =>
        ProposalCommands.New(new NewDraftRequest(_path, "1.0", name, "A Proposal")).Value!.ProposalId;

    private static ChangeIntent Spelling(Guid wordform, string word) => new(CanonicalId.Mint().Value,
        "incorrect-spelling", CanonicalId.FromGuid(wordform).Value, word);

    [Fact]
    public void PutIntoAProposalLeavesThePendingDraftUnchanged()
    {
        var (first, _) = CaptureTwoWords();
        var proposalId = NewProposal("proposal-a");
        var before = PendingChanges.Load(new PendingChangesRequest(_path, "1.0", proposalId));

        var put = PendingChanges.Put(new PutPendingChangeRequest(_path, "1.0", before.Value!.Revision,
            Spelling(first, "alpha-word"), proposalId));

        Assert.True(put.Succeeded, put.Refusal?.Message);
        Assert.Single(put.Value!.Changes);
        Assert.Empty(PendingChanges.Load(new PendingChangesRequest(_path, "1.0")).Value!.Changes);
        Assert.Single(PendingChanges.Load(new PendingChangesRequest(_path, "1.0", proposalId)).Value!.Changes);
    }

    [Fact]
    public void PutIntoAFinalizedProposalReopensIt()
    {
        var (first, second) = CaptureTwoWords();
        var proposalId = NewProposal("proposal-b");
        var loaded = PendingChanges.Load(new PendingChangesRequest(_path, "1.0", proposalId));
        Assert.True(PendingChanges.Put(new PutPendingChangeRequest(_path, "1.0", loaded.Value!.Revision,
            Spelling(first, "alpha-word"), proposalId)).Succeeded);
        Assert.True(ProposalCommands.Comment(new CommentRequest(_path, "1.0", "proposal-b", "Why")).Succeeded);
        var finalize = ProposalCommands.Finalize(new FinalizeRequest(_path, "1.0", "proposal-b"));
        Assert.True(finalize.Succeeded, finalize.Refusal?.Message);
        var finalized = PendingChanges.Load(new PendingChangesRequest(_path, "1.0", proposalId));
        Assert.Single(finalized.Value!.Changes);

        var put = PendingChanges.Put(new PutPendingChangeRequest(_path, "1.0", finalized.Value.Revision,
            Spelling(second, "beta-word"), proposalId));

        Assert.True(put.Succeeded, put.Refusal?.Message);
        Assert.Equal(2, put.Value!.Changes.Count);
        using var database = ProjectMotifDatabase.Open(_path);
        Assert.True(new ProposalRepository(database).DraftNameExists("review/" + proposalId));
    }

    [Fact]
    public void APutNamingAnUnknownProposalIsRefused()
    {
        CaptureTwoWords();
        var put = PendingChanges.Put(new PutPendingChangeRequest(_path, "1.0", "none",
            Spelling(Guid.NewGuid(), "x"), CanonicalId.Mint().Value));

        Assert.Equal("proposal.not-found", put.Refusal?.Code);
    }

    [Fact]
    public void AnApprovedReadingThatNamesACreatedAllomorphDependsOnItsCreation()
    {
        var (first, _) = CaptureTwoWords();
        var createdForm = CanonicalId.Mint();
        var creator = CanonicalId.Mint();
        using var cache = new FwDataProjectLoader().LoadCache(_path);
        var reading = new ParseAnalysis([new ParseMorph(createdForm.ToGuid().ToString("D"), null, null, null)]);

        var operations = AnalysisChangeComposer.Build(cache, new AnalysisChangeIntent(
            AnalysisChangeKinds.AddCandidate, CanonicalId.FromGuid(first), reading,
            CreatedInProposal: new Dictionary<CanonicalId, CanonicalId> { [createdForm] = creator }));

        var created = Assert.Single(operations);
        Assert.Equal(WfiAnalysisOperationKinds.CreateAnalysis, created.Kind);
        Assert.Equal(creator, Assert.Single(created.DependsOn).OperationId);
    }

    [Fact]
    public void AReadingNamingAnUnknownAllomorphStillFailsWithoutACreatingOperation()
    {
        var (first, _) = CaptureTwoWords();
        using var cache = new FwDataProjectLoader().LoadCache(_path);
        var reading = new ParseAnalysis([new ParseMorph(Guid.NewGuid().ToString("D"), null, null, null)]);

        var failure = Record.Exception(() => AnalysisChangeComposer.Build(cache, new AnalysisChangeIntent(
            AnalysisChangeKinds.AddCandidate, CanonicalId.FromGuid(first), reading)));

        Assert.NotNull(failure);
    }

    private PutPendingChangeRequest TrialWrite(Guid wordform, TrialEvidenceExpectation trial,
        SIL.Motif.Contract.Baselines.BaselineToken token, string? intoProposal)
    {
        var revision = PendingChanges.Load(new PendingChangesRequest(_path, "1.0", intoProposal)).Value!.Revision;
        return new PutPendingChangeRequest(_path, "1.0", revision, Spelling(wordform, "alpha-word"), intoProposal)
        {
            ExpectedContext = new ExpectedContext(token) { Trial = trial },
        };
    }

    [Fact]
    public void AWriteCarryingTrialEvidenceIsAcceptedAgainstTheSourceBaselineAndTheTrialRoot()
    {
        var (first, _) = CaptureTwoWords();
        var (token, trial) = SeedTrialRoot();

        var put = PendingChanges.Put(TrialWrite(first, trial, token, trial.ProposalId));

        Assert.True(put.Succeeded, put.Refusal?.Message);
    }

    [Fact]
    public void ATrialOfOneProposalCannotWriteIntoAnother()
    {
        var (first, _) = CaptureTwoWords();
        var (token, trial) = SeedTrialRoot();
        var other = NewProposal("proposal-other");

        var put = PendingChanges.Put(TrialWrite(first, trial, token, other));

        Assert.Equal("texts.evidence-changed", put.Refusal?.Code);
        Assert.Empty(PendingChanges.Load(new PendingChangesRequest(_path, "1.0", other)).Value!.Changes);
    }

    [Fact]
    public void ATrialContextCannotWriteIntoThePendingDraft()
    {
        var (first, _) = CaptureTwoWords();
        var (token, trial) = SeedTrialRoot();

        var put = PendingChanges.Put(TrialWrite(first, trial, token, null));

        Assert.Equal("texts.evidence-changed", put.Refusal?.Code);
        Assert.Empty(PendingChanges.Load(new PendingChangesRequest(_path, "1.0")).Value!.Changes);
    }

    [Fact]
    public void AStaleWriteIntoAFinalizedProposalLeavesItFinalized()
    {
        var (first, second) = CaptureTwoWords();
        var proposalId = NewProposal("proposal-c");
        var loaded = PendingChanges.Load(new PendingChangesRequest(_path, "1.0", proposalId));
        Assert.True(PendingChanges.Put(new PutPendingChangeRequest(_path, "1.0", loaded.Value!.Revision,
            Spelling(first, "alpha-word"), proposalId)).Succeeded);
        Assert.True(ProposalCommands.Comment(new CommentRequest(_path, "1.0", "proposal-c", "Why")).Succeeded);
        Assert.True(ProposalCommands.Finalize(new FinalizeRequest(_path, "1.0", "proposal-c")).Succeeded);
        var finalized = PendingChanges.Load(new PendingChangesRequest(_path, "1.0", proposalId));
        Assert.True(ProposalCommands.New(new NewDraftRequest(_path, "1.0", "review/" + proposalId, "Taken")).Succeeded);

        var put = PendingChanges.Put(new PutPendingChangeRequest(_path, "1.0", finalized.Value!.Revision,
            Spelling(second, "beta-word"), proposalId));

        Assert.Equal("change.revision-conflict", put.Refusal?.Code);
        using var database = ProjectMotifDatabase.Open(_path);
        Assert.Null(new ProposalRepository(database).Get(CanonicalId.Parse(proposalId)).DraftName);
    }

    [Fact]
    public void LoadNamingAnUnknownProposalIsRefused()
    {
        CaptureTwoWords();

        var loaded = PendingChanges.Load(new PendingChangesRequest(_path, "1.0", CanonicalId.Mint().Value));

        Assert.Equal("proposal.not-found", loaded.Refusal?.Code);
    }

    [Theory]
    [InlineData("applied", "proposal.already-applied")]
    [InlineData("rejected", "proposal.not-editable")]
    [InlineData("superseded", "proposal.not-editable")]
    public void AFinalizedProposalThatIsNotWantedCannotBeLoadedOrEdited(string status, string code)
    {
        var (first, _) = CaptureTwoWords();
        var proposalId = NewProposal("proposal-d");
        var loaded = PendingChanges.Load(new PendingChangesRequest(_path, "1.0", proposalId));
        Assert.True(PendingChanges.Put(new PutPendingChangeRequest(_path, "1.0", loaded.Value!.Revision,
            Spelling(first, "alpha-word"), proposalId)).Succeeded);
        Assert.True(ProposalCommands.Comment(new CommentRequest(_path, "1.0", "proposal-d", "Why")).Succeeded);
        Assert.True(ProposalCommands.Finalize(new FinalizeRequest(_path, "1.0", "proposal-d")).Succeeded);
        using (var database = ProjectMotifDatabase.Open(_path))
            new ProposalRepository(database).SetStatus(CanonicalId.Parse(proposalId), status, null);

        var read = PendingChanges.Load(new PendingChangesRequest(_path, "1.0", proposalId));

        Assert.Equal(code, read.Refusal?.Code);
    }

    [Fact]
    public void TheTrialFitIsComputedOnceAndAgainWhenTheDraftOrTheCopyChanges()
    {
        var calls = 0;
        IReadOnlyList<ChangeFit> Compute() { calls++; return []; }

        TrialFitMemo.GetOrCompute("p", "r1", "job", true, Compute);
        TrialFitMemo.GetOrCompute("p", "r1", "job", true, Compute);
        Assert.Equal(1, calls);
        TrialFitMemo.GetOrCompute("p", "r2", "job", true, Compute);
        TrialFitMemo.GetOrCompute("p", "r1", "job", false, Compute);
        Assert.Equal(3, calls);
    }

    [Fact]
    public void AWriteNamingATrialRootAsCurrentEvidenceIsRefused()
    {
        var (first, _) = CaptureTwoWords();
        var (token, trial) = SeedTrialRoot();
        var asCurrent = new SelectionEvidenceExpectation("explicit", "", [], [], "sha256:x",
            trial.RootAssessmentId, [], [], []);
        var put = PendingChanges.Put(new PutPendingChangeRequest(_path, "1.0",
            PendingChanges.Load(new PendingChangesRequest(_path, "1.0")).Value!.Revision,
            Spelling(first, "alpha-word"))
        {
            ExpectedContext = new ExpectedContext(token) { SelectionEvidence = asCurrent },
        });

        Assert.Equal("texts.evidence-changed", put.Refusal?.Code);
    }

    [Fact]
    public void AWriteNamingAMissingTrialRootIsRefused()
    {
        var (first, _) = CaptureTwoWords();
        var (token, trial) = SeedTrialRoot();
        var put = PendingChanges.Put(TrialWrite(first, trial with { RootAssessmentId = "missing" }, token,
            trial.ProposalId));

        Assert.Equal("texts.evidence-changed", put.Refusal?.Code);
    }

    private (SIL.Motif.Contract.Baselines.BaselineToken Token, TrialEvidenceExpectation Trial) SeedTrialRoot()
    {
        var proposalId = CanonicalId.Parse(NewProposal("trial-owner"));
        var assessmentId = CanonicalId.Mint().Value;
        using var database = ProjectMotifDatabase.Open(_path);
        var project = new SIL.Motif.Contract.Projects.ProjectLocator(Path.GetFullPath(_path),
            Path.GetFileNameWithoutExtension(_path));
        var token = new BaselineRepository(database).GetCurrent(
            SIL.Motif.Worker.Projects.ProjectWorkspaceKey.Compute(project))!.Token;
        var selection = Selection.Create("trial", ["alpha-word"]);
        new AssessmentRepository(database).Record(new NewAssessmentRecord(
            AssessmentId: assessmentId,
            ProposalId: proposalId,
            ProposalIntentDigest: "sha256:digest",
            Assessor: "test",
            Kind: "ParseTime",
            ScopeJson: "{}",
            ScopeDigest: "sha256:scope",
            Subject: new AssessmentSubject.Words(selection, "whitespace-and-punctuation", "1"),
            BaselineToken: JsonSerializer.Serialize(token, MotifJson.CreateOptions()),
            OutcomeDigest: "sha256:outcome",
            SemanticDigest: "sha256:semantic",
            GrammarSourceSha256: "sha256:grammar",
            ModelFingerprint: "model",
            Pipeline: "pipeline",
            DiagnosticCount: 0,
            Material: new AssessmentRecordMaterial.Words([], null, null, [])
        ));
        return (token, new TrialEvidenceExpectation(proposalId.Value, "sha256:digest", assessmentId));
    }
}
