using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Cli;

[Collection(LcmCacheTestCollection.Name)]
public sealed class PendingChangesTests
{
    private readonly string _path;
    private readonly SeededProject _seed;

    public PendingChangesTests(PristineProjectFixture pristine)
    {
        _seed = pristine.Seed;
        using var scratch = pristine.NewScratch();
        _path = scratch.ProjectId.Path;
    }

    [Fact]
    public void RecapturingAnUnchangedProjectKeepsPendingChangeFitting()
    {
        var loader = new FwDataProjectLoader();
        Guid wordformId;
        using (var cache = loader.LoadCache(_path))
        {
            wordformId = Guid.Empty;
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                wordformId = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                    .Create(TsStringUtils.MakeString("same-save", cache.DefaultVernWs)).Guid);
            loader.Save(cache);
        }
        var managed = Path.Combine(Path.GetDirectoryName(_path)!, "same-save-managed");
        var first = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(_path), managed);
        Assert.True(first.Succeeded, first.Refusal?.Message);
        var pending = PendingChanges.Load(new PendingChangesRequest(_path, "1.0"));
        var added = PendingChanges.Put(new PutPendingChangeRequest(_path, "1.0", pending.Value!.Revision,
            new ChangeIntent(CanonicalId.Mint().Value, "incorrect-spelling",
                CanonicalId.FromGuid(wordformId).Value, "same-save")));
        Assert.True(added.Succeeded, added.Refusal?.Message);

        File.SetLastWriteTimeUtc(_path, File.GetLastWriteTimeUtc(_path).AddMinutes(1));
        var second = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(_path), managed);
        var reloaded = PendingChanges.Load(new PendingChangesRequest(_path, "1.0"));

        Assert.True(second.Succeeded, second.Refusal?.Message);
        Assert.True(first.Value!.Token.HasSameSemanticIdentity(second.Value!.Token));
        Assert.NotEqual(first.Value.Token.BundleDigest, second.Value.Token.BundleDigest);
        using var database = ProjectMotifDatabase.Open(_path);
        var project = new ProjectLocator(
            Path.GetFullPath(_path), Path.GetFileNameWithoutExtension(_path));
        var current = new BaselineRepository(database)
            .GetCurrent(SIL.Motif.Worker.Projects.ProjectWorkspaceKey.Compute(project));
        Assert.Equal(second.Value.Token, current!.Token);
        Assert.True(Assert.Single(reloaded.Value!.FitSummary).StillFits);
    }

    [Fact]
    public void CheckingAgainRefreshesTheFingerprintWhenAnUnrelatedWordChanged()
    {
        var loader = new FwDataProjectLoader();
        Guid wordformId = Guid.Empty;
        using (var cache = loader.LoadCache(_path))
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                wordformId = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                    .Create(TsStringUtils.MakeString("still-here", cache.DefaultVernWs)).Guid);
            loader.Save(cache);
        }
        var managed = Path.Combine(Path.GetDirectoryName(_path)!, "check-again-managed");
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(_path), managed).Succeeded);
        var pending = PendingChanges.Load(new PendingChangesRequest(_path, "1.0"));
        var added = PendingChanges.Put(new PutPendingChangeRequest(_path, "1.0", pending.Value!.Revision,
            new ChangeIntent(CanonicalId.Mint().Value, "incorrect-spelling",
                CanonicalId.FromGuid(wordformId).Value, "still-here")));
        Assert.True(added.Succeeded, added.Refusal?.Message);
        using (var cache = loader.LoadCache(_path))
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                    .Create(TsStringUtils.MakeString("unrelated", cache.DefaultVernWs)));
            loader.Save(cache);
        }
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(_path), managed).Succeeded);
        var drifted = PendingChanges.Load(new PendingChangesRequest(_path, "1.0"));
        Assert.False(Assert.Single(drifted.Value!.FitSummary).StillFits);

        var checkedAgain = PendingChanges.Recheck(new RecheckPendingChangesRequest(_path, "1.0",
            drifted.Value.Revision));

        Assert.True(checkedAgain.Succeeded, checkedAgain.Refusal?.Message);
        Assert.True(Assert.Single(checkedAgain.Value!.FitSummary).StillFits);
        Assert.NotEqual(drifted.Value.Revision, checkedAgain.Value.Revision);
    }

    [Fact]
    public void CheckingAgainDoesNotRenewADeletedWordform()
    {
        var loader = new FwDataProjectLoader();
        Guid wordformId = Guid.Empty;
        using (var cache = loader.LoadCache(_path))
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                wordformId = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                    .Create(TsStringUtils.MakeString("gone", cache.DefaultVernWs)).Guid);
            loader.Save(cache);
        }
        var managed = Path.Combine(Path.GetDirectoryName(_path)!, "deleted-managed");
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(_path), managed).Succeeded);
        var pending = PendingChanges.Load(new PendingChangesRequest(_path, "1.0"));
        var added = PendingChanges.Put(new PutPendingChangeRequest(_path, "1.0", pending.Value!.Revision,
            new ChangeIntent(CanonicalId.Mint().Value, "incorrect-spelling",
                CanonicalId.FromGuid(wordformId).Value, "gone")));
        Assert.True(added.Succeeded, added.Refusal?.Message);
        using (var cache = loader.LoadCache(_path))
        {
            var wordform = cache.ServiceLocator.GetInstance<IWfiWordformRepository>().GetObject(wordformId);
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () => wordform.Delete());
            loader.Save(cache);
        }
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(_path), managed).Succeeded);
        var drifted = PendingChanges.Load(new PendingChangesRequest(_path, "1.0"));

        var checkedAgain = PendingChanges.Recheck(new RecheckPendingChangesRequest(_path, "1.0",
            drifted.Value!.Revision));

        Assert.True(checkedAgain.Succeeded, checkedAgain.Refusal?.Message);
        Assert.False(Assert.Single(checkedAgain.Value!.FitSummary).StillFits);
        Assert.Equal(drifted.Value.Revision, checkedAgain.Value.Revision);
    }

    [Fact]
    public void CheckingAgainDoesNotRenewAChangedSpellingStatus()
    {
        var loader = new FwDataProjectLoader();
        Guid wordformId = Guid.Empty;
        using (var cache = loader.LoadCache(_path))
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                wordformId = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                    .Create(TsStringUtils.MakeString("spelling-decision", cache.DefaultVernWs)).Guid);
            loader.Save(cache);
        }
        var managed = Path.Combine(Path.GetDirectoryName(_path)!, "spelling-decision-managed");
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(_path), managed).Succeeded);
        var pending = PendingChanges.Load(new PendingChangesRequest(_path, "1.0"));
        var added = PendingChanges.Put(new PutPendingChangeRequest(_path, "1.0", pending.Value!.Revision,
            new ChangeIntent(CanonicalId.Mint().Value, "incorrect-spelling",
                CanonicalId.FromGuid(wordformId).Value, "spelling-decision")));
        Assert.True(added.Succeeded, added.Refusal?.Message);
        using (var cache = loader.LoadCache(_path))
        {
            var wordform = cache.ServiceLocator.GetInstance<IWfiWordformRepository>().GetObject(wordformId);
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () => wordform.SpellingStatus = 1);
            loader.Save(cache);
        }
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(_path), managed).Succeeded);
        var drifted = PendingChanges.Load(new PendingChangesRequest(_path, "1.0"));
        Assert.False(Assert.Single(drifted.Value!.FitSummary).StillFits);
        Assert.Contains("spelling status", string.Join(" ", drifted.Value.FitSummary[0].Reasons),
            StringComparison.OrdinalIgnoreCase);

        var checkedAgain = PendingChanges.Recheck(new RecheckPendingChangesRequest(_path, "1.0",
            drifted.Value.Revision));

        Assert.True(checkedAgain.Succeeded, checkedAgain.Refusal?.Message);
        Assert.False(Assert.Single(checkedAgain.Value!.FitSummary).StillFits);
        Assert.Equal(drifted.Value.Revision, checkedAgain.Value.Revision);
    }

    [Fact]
    public void PreflightRefusesAChangedSpellingStatus()
    {
        var loader = new FwDataProjectLoader();
        Guid wordformId = Guid.Empty;
        using (var cache = loader.LoadCache(_path))
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                wordformId = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                    .Create(TsStringUtils.MakeString("preflight-spelling", cache.DefaultVernWs)).Guid);
            loader.Save(cache);
        }
        var managed = Path.Combine(Path.GetDirectoryName(_path)!, "preflight-spelling-managed");
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(_path), managed).Succeeded);
        var pending = PendingChanges.Load(new PendingChangesRequest(_path, "1.0"));
        var added = PendingChanges.Put(new PutPendingChangeRequest(_path, "1.0", pending.Value!.Revision,
            new ChangeIntent(CanonicalId.Mint().Value, "incorrect-spelling",
                CanonicalId.FromGuid(wordformId).Value, "preflight-spelling")));
        Assert.True(added.Succeeded, added.Refusal?.Message);
        var finalized = ProposalCommands.Finalize(new FinalizeRequest(_path, "1.0", PendingChanges.DraftName));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        using (var cache = loader.LoadCache(_path))
        {
            var wordform = cache.ServiceLocator.GetInstance<IWfiWordformRepository>().GetObject(wordformId);
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () => wordform.SpellingStatus = 1);
            loader.Save(cache);
        }
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(_path), managed).Succeeded);

        var preflight = ProposalCommands.Preflight(new PreflightRequest(_path, "1.0",
            finalized.Value!.ProposalId));

        Assert.True(preflight.Succeeded, preflight.Refusal?.Message);
        var fit = Assert.Single(preflight.Value!.Changes);
        Assert.False(fit.StillFits);
        Assert.Contains("spelling status", fit.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    [InlineData("candidate")]
    [InlineData("add-candidate")]
    public void CheckingAgainDoesNotRenewAChangedAnalysisDecision(string kind)
    {
        var loader = new FwDataProjectLoader();
        Guid wordformId = Guid.Empty;
        Guid analysisId = Guid.Empty;
        string? morphId = null;
        string? msaId = null;
        using (var cache = loader.LoadCache(_path))
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var wordform = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                    .Create(TsStringUtils.MakeString("analysis-decision", cache.DefaultVernWs));
                wordformId = wordform.Guid;
                var entry = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(_seed.FirstEntryId);
                morphId = entry.LexemeFormOA!.Guid.ToString("D");
                msaId = entry.MorphoSyntaxAnalysesOC.First().Guid.ToString("D");
                if (kind == "add-candidate") return;
                var analysis = cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
                wordform.AnalysesOC.Add(analysis);
                var bundle = cache.ServiceLocator.GetInstance<IWfiMorphBundleFactory>().Create();
                analysis.MorphBundlesOS.Add(bundle);
                bundle.MorphRA = entry.LexemeFormOA;
                bundle.MsaRA = entry.MorphoSyntaxAnalysesOC.First();
                analysisId = analysis.Guid;
                if (kind == "candidate")
                    cache.LangProject.DefaultUserAgent.SetEvaluation(analysis, Opinions.approves);
            });
            loader.Save(cache);
        }
        var managed = Path.Combine(Path.GetDirectoryName(_path)!, kind + "-decision-managed");
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(_path), managed);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        string? assessmentId = null;
        if (kind == "add-candidate")
        {
            assessmentId = CanonicalId.Mint().Value;
            var reading = new ParseAnalysis([new ParseMorph(morphId, msaId, null, null)]);
            using var database = ProjectMotifDatabase.Open(_path);
            new AssessmentRepository(database).Record(new NewAssessmentRecord(
                assessmentId, null, null, "test", "ParseTime", "{}", "sha256:scope",
                "whitespace-and-punctuation", "1", JsonSerializer.Serialize(captured.Value!.Token),
                Selection.Create("parser-change", ["analysis-decision"]), "sha256:outcome", "sha256:semantic",
                "sha256:grammar", "model", "pipeline", 0,
                [new AssessedWord("analysis-decision", "parsed", [], 0)
                {
                    Morphology = new ParseWordEvidence(ParseMorphEvidence.Schema, 0,
                        "analysis-decision", 0, false, false, false, [reading], []),
                }]));
        }
        var pending = PendingChanges.Load(new PendingChangesRequest(_path, "1.0"));
        var added = PendingChanges.Put(new PutPendingChangeRequest(_path, "1.0", pending.Value!.Revision,
            new ChangeIntent(CanonicalId.Mint().Value, kind, CanonicalId.FromGuid(wordformId).Value,
                "analysis-decision", AssessmentId: assessmentId, Reading: kind == "add-candidate"
                    ? new ParseAnalysis([new ParseMorph(morphId, msaId, null, null)]) : null,
                StoredAnalysisId: kind == "add-candidate" ? null : CanonicalId.FromGuid(analysisId).Value)));
        Assert.True(added.Succeeded, added.Refusal?.Message);
        using (var cache = loader.LoadCache(_path))
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                if (kind == "add-candidate")
                {
                    var wordform = cache.ServiceLocator.GetInstance<IWfiWordformRepository>().GetObject(wordformId);
                    var entry = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(_seed.FirstEntryId);
                    var analysis = cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
                    wordform.AnalysesOC.Add(analysis);
                    var bundle = cache.ServiceLocator.GetInstance<IWfiMorphBundleFactory>().Create();
                    analysis.MorphBundlesOS.Add(bundle);
                    bundle.MorphRA = entry.LexemeFormOA;
                    bundle.MsaRA = entry.MorphoSyntaxAnalysesOC.First();
                }
                else
                {
                    var analysis = cache.ServiceLocator.GetInstance<IWfiAnalysisRepository>().GetObject(analysisId);
                    cache.LangProject.DefaultUserAgent.SetEvaluation(analysis,
                        kind == "reject" ? Opinions.approves : Opinions.disapproves);
                }
            });
            loader.Save(cache);
        }
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(_path), managed).Succeeded);
        var drifted = PendingChanges.Load(new PendingChangesRequest(_path, "1.0"));
        Assert.False(Assert.Single(drifted.Value!.FitSummary).StillFits);

        var checkedAgain = PendingChanges.Recheck(new RecheckPendingChangesRequest(_path, "1.0",
            drifted.Value.Revision));

        Assert.True(checkedAgain.Succeeded, checkedAgain.Refusal?.Message);
        Assert.False(Assert.Single(checkedAgain.Value!.FitSummary).StillFits);
        Assert.Equal(drifted.Value.Revision, checkedAgain.Value.Revision);
    }

    [Fact]
    public void DefaultTrialQueuesOnlyTheTouchedWord()
    {
        var loader = new FwDataProjectLoader();
        Guid changedId = Guid.Empty;
        using (var cache = loader.LoadCache(_path))
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var factory = cache.ServiceLocator.GetInstance<IWfiWordformFactory>();
                changedId = factory.Create(TsStringUtils.MakeString("trial-changed", cache.DefaultVernWs)).Guid;
                factory.Create(TsStringUtils.MakeString("trial-untouched", cache.DefaultVernWs));
            });
            loader.Save(cache);
        }
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(_path),
            Path.Combine(Path.GetDirectoryName(_path)!, "trial-managed")).Succeeded);
        var pending = PendingChanges.Load(new PendingChangesRequest(_path, "1.0"));
        var changed = PendingChanges.Put(new PutPendingChangeRequest(_path, "1.0", pending.Value!.Revision,
            new ChangeIntent(CanonicalId.Mint().Value, "incorrect-spelling",
                CanonicalId.FromGuid(changedId).Value, "trial-changed")));
        Assert.True(changed.Succeeded, changed.Refusal?.Message);

        var queued = JobCommands.EnqueueTrial(new EnqueueTrialRequest(_path, "1.0", changed.Value!.DraftId!));

        Assert.True(queued.Succeeded, queued.Refusal?.Message);
        using var database = ProjectMotifDatabase.Open(_path);
        var input = TrialJobInput.Parse(new JobRepository(database).Get(queued.Value!.JobId)!.InputJson);
        Assert.Equal(["trial-changed"], input.Words);
    }

    [Fact]
    public void ApplyingTheReviewedPendingChangeRecordsAReceiptAndClearsTheList()
    {
        var loader = new FwDataProjectLoader();
        Guid wordformId = Guid.Empty;
        using (var cache = loader.LoadCache(_path))
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                wordformId = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                    .Create(TsStringUtils.MakeString("review-word", cache.DefaultVernWs)).Guid);
            loader.Save(cache);
        }
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(_path),
            Path.Combine(Path.GetDirectoryName(_path)!, "review-managed"));
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        var initial = PendingChanges.Load(new PendingChangesRequest(_path, "1.0"));
        var added = PendingChanges.Put(new PutPendingChangeRequest(_path, "1.0", initial.Value!.Revision,
            new ChangeIntent(CanonicalId.Mint().Value, "incorrect-spelling",
                CanonicalId.FromGuid(wordformId).Value, "review-word")));
        Assert.True(added.Succeeded, added.Refusal?.Message);
        Assert.True(ProposalCommands.Label(new LabelRequest(_path, "1.0", PendingChanges.DraftName,
            "Correct word spelling")).Succeeded);
        Assert.True(ProposalCommands.Comment(new CommentRequest(_path, "1.0", PendingChanges.DraftName,
            "The project's spelling status needs correction.")).Succeeded);
        var finalized = ProposalCommands.Finalize(new FinalizeRequest(_path, "1.0", PendingChanges.DraftName));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var proposalId = finalized.Value!.ProposalId;
        Assert.All(ProposalCommands.Preflight(new PreflightRequest(_path, "1.0", proposalId)).Value!.Changes,
            fit => Assert.True(fit.StillFits, fit.Reason));
        var dryRun = DryRunJobRunner.Run(_path, "1.0", proposalId);
        Assert.True(dryRun.Succeeded, dryRun.Refusal?.Message);
        using (var database = ProjectMotifDatabase.Open(_path))
        {
            new AssessmentRepository(database).Record(new NewAssessmentRecord(
                CanonicalId.Mint("assessment/").Value, CanonicalId.Parse(proposalId),
                finalized.Value.IntentDigest, "pangloss", "Correctness",
                """{"perWordLimitMs":1000,"perWordStepLimit":{"steps":200000,"isUnbounded":false}}""",
                "sha256:scope",
                "none", "1", JsonSerializer.Serialize(captured.Value!.Token),
                SIL.Motif.Host.Corpus.Selection.Create("review", ["review-word"]),
                "sha256:outcome", "sha256:semantic", "sha256:grammar", "model", "pipeline", 0,
                [CorrectnessFixture.Word("review-word", true)]));
        }

        var applied = ProposalCommands.Apply(new ApplyRequest(_path, "1.0", proposalId, "reviewer"));

        Assert.True(applied.Succeeded, applied.Refusal?.Message);
        Assert.Empty(PendingChanges.Load(new PendingChangesRequest(_path, "1.0")).Value!.Changes);
        using var stored = ProjectMotifDatabase.Open(_path);
        using var connection = stored.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Receipts WHERE ProposalId = $proposal;";
        command.Parameters.AddWithValue("$proposal", proposalId);
        Assert.Equal(1L, (long)command.ExecuteScalar()!);
    }

    [Fact]
    public void LaterChoiceForSameStoredReadingReplacesEarlierChoiceAndBumpsRevision()
    {
        var loader = new FwDataProjectLoader();
        Guid wordformId = Guid.Empty;
        Guid analysisId = Guid.Empty;
        using (var cache = loader.LoadCache(_path))
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var wordform = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                    .Create(TsStringUtils.MakeString("chosen-word", cache.DefaultVernWs));
                var analysis = cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
                wordform.AnalysesOC.Add(analysis);
                var bundle = cache.ServiceLocator.GetInstance<IWfiMorphBundleFactory>().Create();
                analysis.MorphBundlesOS.Add(bundle);
                var entry = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(_seed.FirstEntryId);
                bundle.MorphRA = entry.LexemeFormOA;
                bundle.MsaRA = entry.MorphoSyntaxAnalysesOC.First();
                wordformId = wordform.Guid;
                analysisId = analysis.Guid;
            });
            loader.Save(cache);
        }
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(_path),
            Path.Combine(Path.GetDirectoryName(_path)!, "choice-managed")).Succeeded);
        var initial = PendingChanges.Load(new PendingChangesRequest(_path, "1.0"));
        var firstId = CanonicalId.Mint().Value;
        var secondId = CanonicalId.Mint().Value;
        var wordId = CanonicalId.FromGuid(wordformId).Value;
        var storedId = CanonicalId.FromGuid(analysisId).Value;
        var first = PendingChanges.Put(new PutPendingChangeRequest(_path, "1.0", initial.Value!.Revision,
            new ChangeIntent(firstId, "approve", wordId, "chosen-word", StoredAnalysisId: storedId)));
        Assert.True(first.Succeeded, first.Refusal?.Message);

        var second = PendingChanges.Put(new PutPendingChangeRequest(_path, "1.0", first.Value!.Revision,
            new ChangeIntent(secondId, "reject", wordId, "chosen-word", StoredAnalysisId: storedId)));

        Assert.True(second.Succeeded, second.Refusal?.Message);
        Assert.NotEqual(first.Value.Revision, second.Value!.Revision);
        Assert.Equal(secondId, Assert.Single(second.Value.Changes).ChangeId);
        using var database = ProjectMotifDatabase.Open(_path);
        var draft = JsonNode.Parse(new ProposalRepository(database).GetDraft(PendingChanges.DraftName).ProposalJson!)!;
        Assert.Single(draft["operations"]!.AsArray());
        Assert.Equal("defaultUserDisapproves", draft["operations"]![0]!["after"]!["member"]!.GetValue<string>());

        var returned = PendingChanges.Put(new PutPendingChangeRequest(_path, "1.0", second.Value.Revision,
            new ChangeIntent(CanonicalId.Mint().Value, "candidate", wordId, "chosen-word",
                StoredAnalysisId: storedId)));
        Assert.True(returned.Succeeded, returned.Refusal?.Message);
        Assert.NotEqual(second.Value.Revision, returned.Value!.Revision);
        Assert.Empty(returned.Value.Changes);
    }

    [Fact]
    public void PreflightFlagsStoredAnalysisWhoseHumanOpinionChanged()
    {
        var loader = new FwDataProjectLoader();
        Guid wordformId = Guid.Empty;
        Guid analysisId = Guid.Empty;
        using (var cache = loader.LoadCache(_path))
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var wordform = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                    .Create(TsStringUtils.MakeString("opinion-word", cache.DefaultVernWs));
                var analysis = cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
                wordform.AnalysesOC.Add(analysis);
                var bundle = cache.ServiceLocator.GetInstance<IWfiMorphBundleFactory>().Create();
                analysis.MorphBundlesOS.Add(bundle);
                var entry = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(_seed.FirstEntryId);
                bundle.MorphRA = entry.LexemeFormOA;
                bundle.MsaRA = entry.MorphoSyntaxAnalysesOC.First();
                cache.LangProject.DefaultUserAgent.SetEvaluation(analysis, Opinions.approves);
                wordformId = wordform.Guid;
                analysisId = analysis.Guid;
            });
            loader.Save(cache);
        }
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(_path),
            Path.Combine(Path.GetDirectoryName(_path)!, "opinion-managed")).Succeeded);
        var pending = PendingChanges.Load(new PendingChangesRequest(_path, "1.0"));
        var added = PendingChanges.Put(new PutPendingChangeRequest(_path, "1.0", pending.Value!.Revision,
            new ChangeIntent(CanonicalId.Mint().Value, "candidate",
                CanonicalId.FromGuid(wordformId).Value, "opinion-word",
                StoredAnalysisId: CanonicalId.FromGuid(analysisId).Value)));
        Assert.True(added.Succeeded, added.Refusal?.Message);
        using (var cache = loader.LoadCache(_path))
        {
            var analysis = cache.ServiceLocator.GetInstance<IWfiAnalysisRepository>().GetObject(analysisId);
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                cache.LangProject.DefaultUserAgent.SetEvaluation(analysis, Opinions.disapproves));
            loader.Save(cache);
        }

        var refreshed = PendingChanges.Load(new PendingChangesRequest(_path, "1.0"));

        Assert.True(refreshed.Succeeded, refreshed.Refusal?.Message);
        Assert.All(refreshed.Value!.FitSummary, fit =>
        {
            Assert.False(fit.StillFits);
            Assert.Contains(fit.Reasons, reason => reason.Contains("opinion", StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public void ApplyRefusesAssessmentThatDidNotMeasureChangedWord()
    {
        var loader = new FwDataProjectLoader();
        Guid wordformId = Guid.Empty;
        using (var cache = loader.LoadCache(_path))
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                wordformId = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                    .Create(TsStringUtils.MakeString("changed-word", cache.DefaultVernWs)).Guid);
            loader.Save(cache);
        }
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(_path),
            Path.Combine(Path.GetDirectoryName(_path)!, "coverage-managed"));
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        var pending = PendingChanges.Load(new PendingChangesRequest(_path, "1.0"));
        var added = PendingChanges.Put(new PutPendingChangeRequest(_path, "1.0", pending.Value!.Revision,
            new ChangeIntent(CanonicalId.Mint().Value, "incorrect-spelling",
                CanonicalId.FromGuid(wordformId).Value, "changed-word")));
        Assert.True(added.Succeeded, added.Refusal?.Message);
        Assert.True(ProposalCommands.Label(new LabelRequest(_path, "1.0", PendingChanges.DraftName,
            "Correct spelling")).Succeeded);
        Assert.True(ProposalCommands.Comment(new CommentRequest(_path, "1.0", PendingChanges.DraftName,
            "Mark the chosen word's spelling as incorrect.")).Succeeded);
        var finalized = ProposalCommands.Finalize(new FinalizeRequest(_path, "1.0", PendingChanges.DraftName));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var proposalId = finalized.Value!.ProposalId;
        Assert.True(DryRunJobRunner.Run(_path, "1.0", proposalId).Succeeded);
        using (var database = ProjectMotifDatabase.Open(_path))
        {
            new AssessmentRepository(database).Record(new NewAssessmentRecord(
                CanonicalId.Mint("assessment/").Value, CanonicalId.Parse(proposalId),
                finalized.Value.IntentDigest, "pangloss", "Correctness",
                """{"perWordLimitMs":1000,"perWordStepLimit":{"steps":200000,"isUnbounded":false}}""",
                "sha256:scope", "none", "1", JsonSerializer.Serialize(captured.Value!.Token),
                SIL.Motif.Host.Corpus.Selection.Create("other", ["other-word"]),
                "sha256:outcome", "sha256:semantic", "sha256:grammar", "model", "pipeline", 0,
                [CorrectnessFixture.Word("other-word", true)]));
        }

        var applied = ProposalCommands.Apply(new ApplyRequest(_path, "1.0", proposalId, "reviewer"));

        Assert.Equal("apply.not-ready", applied.Refusal?.Code);
        Assert.Contains("changed-word", applied.Refusal!.Message, StringComparison.Ordinal);
        using var stored = ProjectMotifDatabase.Open(_path);
        Assert.Equal("proposed", new ProposalRepository(stored).Get(CanonicalId.Parse(proposalId)).Status);
    }

    [Fact]
    public void ASecondChangeForTheSameSlotIsRefusedWithoutLosingTheFirst()
    {
        var loader = new FwDataProjectLoader();
        Guid wordformId = Guid.Empty;
        using (var cache = loader.LoadCache(_path))
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                wordformId = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                    .Create(TsStringUtils.MakeString("pending-word", cache.DefaultVernWs)).Guid);
            loader.Save(cache);
        }
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(_path),
            Path.Combine(Path.GetDirectoryName(_path)!, "pending-managed")).Succeeded);

        var initial = PendingChanges.Load(new PendingChangesRequest(_path, "1.0"));
        Assert.True(initial.Succeeded, initial.Refusal?.Message);
        var wordId = CanonicalId.FromGuid(wordformId).Value;
        var firstId = CanonicalId.Mint().Value;
        var secondId = CanonicalId.Mint().Value;
        var first = PendingChanges.Put(new PutPendingChangeRequest(_path, "1.0",
            initial.Value!.Revision, new ChangeIntent(firstId, "incorrect-spelling", wordId, "pending-word")));
        Assert.True(first.Succeeded, first.Refusal?.Message);
        var second = PendingChanges.Put(new PutPendingChangeRequest(_path, "1.0",
            first.Value!.Revision, new ChangeIntent(secondId, "incorrect-spelling", wordId, "pending-word")));
        Assert.Equal("change.slot-occupied", second.Refusal?.Code);
        Assert.Equal(secondId, second.Refusal?.Facts["changeId"]);
        Assert.Equal(firstId, second.Refusal?.Facts["existingChangeId"]);
        Assert.All(first.Value.FitSummary, fit => Assert.True(fit.StillFits,
            string.Join(" ", fit.Reasons)));
        Assert.Single(PendingChanges.Load(new PendingChangesRequest(_path, "1.0")).Value!.Changes);

        var stale = PendingChanges.Remove(new RemovePendingChangeRequest(_path, "1.0",
            initial.Value.Revision, firstId));
        Assert.Equal("change.revision-conflict", stale.Refusal?.Code);
        Assert.Equal(firstId, stale.Refusal?.Facts["changeId"]);
        Assert.Single(PendingChanges.Load(new PendingChangesRequest(_path, "1.0")).Value!.Changes);

        var json = JsonSerializer.Serialize(first.Value);
        Assert.Equal(json, JsonSerializer.Serialize(JsonSerializer.Deserialize<PendingChangesSnapshot>(json)));

        using (var cache = loader.LoadCache(_path))
        {
            var wordform = cache.ServiceLocator.GetInstance<IWfiWordformRepository>().GetObject(wordformId);
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () => wordform.Delete());
            loader.Save(cache);
        }
        var afterDeletion = PendingChanges.Load(new PendingChangesRequest(_path, "1.0"));
        Assert.True(afterDeletion.Succeeded, afterDeletion.Refusal?.Message);
        Assert.All(afterDeletion.Value!.FitSummary, fit =>
        {
            Assert.False(fit.StillFits);
            Assert.Contains(fit.Reasons, reason => reason.Contains("deleted", StringComparison.Ordinal));
        });

        using (var database = ProjectMotifDatabase.Open(_path))
        {
            var repository = new ProposalRepository(database);
            var stored = JsonNode.Parse(repository.GetDraft(PendingChanges.DraftName).ProposalJson!)!;
            stored["composerProvenance"]!.AsArray().Clear();
            stored["operations"]![0]!["extensions"]!["changeFit"] = null;
            repository.SaveDraft(PendingChanges.DraftName, stored.ToJsonString());
        }
        var unmapped = PendingChanges.Load(new PendingChangesRequest(_path, "1.0"));
        Assert.True(unmapped.Succeeded, unmapped.Refusal?.Message);
        Assert.Single(unmapped.Value!.Changes);
        Assert.All(unmapped.Value.FitSummary, fit =>
            Assert.Contains(fit.Reasons, reason => reason.Contains("mapping or fingerprint", StringComparison.Ordinal)));

        using (var database = ProjectMotifDatabase.Open(_path))
        {
            var repository = new ProposalRepository(database);
            var stored = JsonNode.Parse(repository.GetDraft(PendingChanges.DraftName).ProposalJson!)!;
            stored["composerProvenance"]!.AsArray().Add(new JsonObject
            {
                ["changeId"] = firstId, ["wordformId"] = wordId,
                ["word"] = "pending-word", ["kind"] = "incorrect-spelling",
                ["operationIds"] = new JsonArray(stored["operations"]![0]!["operationId"]!.GetValue<string>()),
            });
            stored["operations"]![0]!["extensions"]!.AsObject().Remove("changeId");
            repository.SaveDraft(PendingChanges.DraftName, stored.ToJsonString());
        }
        var orphaned = PendingChanges.Load(new PendingChangesRequest(_path, "1.0"));
        Assert.Single(orphaned.Value!.Changes);
        var removedOrphan = PendingChanges.Remove(new RemovePendingChangeRequest(_path, "1.0",
            orphaned.Value.Revision, firstId));
        Assert.True(removedOrphan.Succeeded, removedOrphan.Refusal?.Message);
        Assert.Empty(removedOrphan.Value!.Changes);
    }

    [Fact]
    public void AWordWithoutUniqueWordformIdentityIsRefusedWithFacts()
    {
        var loader = new FwDataProjectLoader();
        using (var cache = loader.LoadCache(_path))
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var factory = cache.ServiceLocator.GetInstance<IWfiWordformFactory>();
                factory.Create(TsStringUtils.MakeString("same-form", cache.DefaultVernWs));
                factory.Create(TsStringUtils.MakeString("same-form", cache.DefaultVernWs));
            });
            loader.Save(cache);
        }
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(_path),
            Path.Combine(Path.GetDirectoryName(_path)!, "ambiguous-managed")).Succeeded);
        var initial = PendingChanges.Load(new PendingChangesRequest(_path, "1.0"));
        var changeId = CanonicalId.Mint().Value;

        var outcome = PendingChanges.Put(new PutPendingChangeRequest(_path, "1.0",
            initial.Value!.Revision, new ChangeIntent(changeId, "incorrect-spelling", "", "same-form")));

        Assert.Equal("change.wordform-ambiguous", outcome.Refusal?.Code);
        Assert.Equal(changeId, outcome.Refusal?.Facts["changeId"]);
        Assert.Equal("same-form", outcome.Refusal?.Facts["word"]);
    }

    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    [InlineData("candidate")]
    public void OpinionRequiresAnExplicitAnalysisIdentity(string kind)
    {
        var initial = PendingChanges.Load(new PendingChangesRequest(_path, "1.0"));
        var outcome = PendingChanges.Put(new PutPendingChangeRequest(_path, "1.0",
            initial.Value!.Revision, new ChangeIntent(CanonicalId.Mint().Value, kind, "", "word",
                Reading: new ParseAnalysis([new ParseMorph(null, null, null, "word")]))));

        Assert.Equal("change.analysis-identity-required", outcome.Refusal?.Code);
    }

    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    [InlineData("add-candidate")]
    public void AParserReadingWithoutAssessmentIsRefused(string kind)
    {
        var loader = new FwDataProjectLoader();
        Guid wordformId = Guid.Empty;
        using (var cache = loader.LoadCache(_path))
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                wordformId = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                    .Create(TsStringUtils.MakeString("pending-reading", cache.DefaultVernWs)).Guid);
            loader.Save(cache);
        }
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(_path),
            Path.Combine(Path.GetDirectoryName(_path)!, "reading-managed")).Succeeded);
        var initial = PendingChanges.Load(new PendingChangesRequest(_path, "1.0"));
        var firstId = CanonicalId.Mint().Value;
        var wordId = CanonicalId.FromGuid(wordformId).Value;
        var reading = new ParseAnalysis([new ParseMorph(null, null, null, "pending-reading")]);
        var result = PendingChanges.Put(new PutPendingChangeRequest(_path, "1.0", initial.Value!.Revision,
            new ChangeIntent(firstId, kind, wordId, "pending-reading", Reading: reading,
                ReadingIndex: 0)));

        Assert.Equal("change.assessment-required", result.Refusal?.Code);
        Assert.Empty(PendingChanges.Load(new PendingChangesRequest(_path, "1.0")).Value!.Changes);
    }

    [Fact]
    public void WordformLookupAcceptsCanonicallyEquivalentUnicode()
    {
        var loader = new FwDataProjectLoader();
        using (var cache = loader.LoadCache(_path))
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                    .Create(TsStringUtils.MakeString("a\u0301", cache.DefaultVernWs)));
            loader.Save(cache);
        }
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(_path),
            Path.Combine(Path.GetDirectoryName(_path)!, "unicode-managed")).Succeeded);
        var current = PendingChanges.Load(new PendingChangesRequest(_path, "1.0"));

        var put = PendingChanges.Put(new PutPendingChangeRequest(_path, "1.0", current.Value!.Revision,
            new ChangeIntent(CanonicalId.Mint().Value, "incorrect-spelling", "", "\u00e1")));

        Assert.True(put.Succeeded, put.Refusal?.Message);
        Assert.True(Assert.Single(put.Value!.FitSummary).StillFits);
    }
}
