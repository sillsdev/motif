using SIL.Motif.Contract.Requests;
using SIL.Motif.Host.Assess;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Config;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.LcmUtils;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.LCModel.Core.Text;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>
/// The trap ADR 0042's Trial amendment names, driven through the real <c>apply</c> verb on a real project:
/// applying promotes the Proposal's candidate Assessment to current, and a sweep of the Proposal's own
/// working Assessments must not take the promoted one with it. Also covers regression gating (ADR 0042
/// decision 5), including the default refusal and an explicit forced Apply.
/// </summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group3)]
public sealed class ApplyPromotionGatingAndSweepTests
{
    private const string ProductVersion = "1.0";
    private const string GrammarSha = "sha256:" + "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd";

    private readonly SeededProject _seed;
    private readonly string _fwDataPath;

    public ApplyPromotionGatingAndSweepTests(PristineProjectFixture pristine)
    {
        _seed = pristine.Seed;
        using var scratch = pristine.NewScratch();
        _fwDataPath = scratch.ProjectId.Path;
    }

    [Fact]
    public void ApplyWithPurgeOnDefault_PromotesTheCandidate_AndTheProposalsOtherAssessmentsAreGone()
    {
        var proposalId = FinalizeAndTrial("trap-purge-on", "purge-on test gloss");
        var intentDigest = GetRecord(proposalId).IntentDigest!;
        var candidateId = RecordAssessment(proposalId, intentDigest, "Correctness", ("alpha", true));
        var scratchId = RecordAssessment(proposalId, intentDigest, "ParseTime", ("alpha", true));

        var result = ProposalCommands.Apply(new ApplyRequest(_fwDataPath, ProductVersion, proposalId));
        Assert.True(result.Succeeded);

        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        var assessments = new AssessmentRepository(database);
        // Asserted after the sweep already ran: the promoted Assessment survives by identity, not by luck.
        Assert.Equal(candidateId, assessments.GetCurrent()!.AssessmentId);
        Assert.Equal(candidateId, assessments.Get(candidateId).AssessmentId);
        Assert.Throws<KeyNotFoundException>(() => assessments.Get(scratchId));
        var remaining = assessments.ListByProposal(CanonicalId.Parse(proposalId));
        Assert.Equal(new[] { candidateId }, remaining.Select(record => record.AssessmentId));
    }

    [Fact]
    public void AnAutonomousApplyWithAMessageAndNoForceSucceeds_AndTheLogShowsModeAndMessage()
    {
        var proposalId = FinalizeAndTrial("autonomous-ready", "autonomous ready gloss");
        RecordAssessment(proposalId, GetRecord(proposalId).IntentDigest!, "Correctness", ("alpha", true));

        var result = ProposalCommands.Apply(new ApplyRequest(_fwDataPath, ProductVersion, proposalId,
            Attribution: new ApplyAttribution(ApplyModes.Autonomous, "Glossed from the corpus", WriteNote: true)));

        Assert.True(result.Succeeded, result.Refusal?.Message);
        Assert.Equal(("autonomous", "Glossed from the corpus"),
            (result.Value!.AppliedLogEntry.Mode, result.Value.AppliedLogEntry.Message));
        var entry = Assert.Single(ProposalCommands.Log(new LogRequest(_fwDataPath)).Value!.Entries);
        Assert.Equal(("autonomous", "Glossed from the corpus"), (entry.Mode, entry.Description));
    }

    [Fact]
    public void ApplyWithPurgeOff_LeavesEveryAssessmentReadable()
    {
        WriteConfiguration(gateOnRegression: false, purgeOnApply: false);
        var proposalId = FinalizeAndTrial("trap-purge-off", "purge-off test gloss");
        var intentDigest = GetRecord(proposalId).IntentDigest!;
        var candidateId = RecordAssessment(proposalId, intentDigest, "Correctness", ("alpha", true));
        var scratchId = RecordAssessment(proposalId, intentDigest, "ParseTime", ("alpha", true));

        var result = ProposalCommands.Apply(new ApplyRequest(_fwDataPath, ProductVersion, proposalId));
        Assert.True(result.Succeeded);

        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        var assessments = new AssessmentRepository(database);
        Assert.Equal(candidateId, assessments.GetCurrent()!.AssessmentId);
        Assert.Equal(candidateId, assessments.Get(candidateId).AssessmentId);
        Assert.Equal(scratchId, assessments.Get(scratchId).AssessmentId);
        Assert.Equal(2, assessments.ListByProposal(CanonicalId.Parse(proposalId)).Count);
    }

    [Fact]
    public void RegressionByDefault_Gates_SoAnUnconfiguredProjectIsStillProtected()
    {
        var proposalId = FinalizeAndTrial("regression-default", "regression default gloss");
        var intentDigest = GetRecord(proposalId).IntentDigest!;
        PromotePrevious(("alpha", true));
        RecordAssessment(proposalId, intentDigest, "Correctness", ("alpha", false));

        var result = ProposalCommands.Apply(new ApplyRequest(_fwDataPath, ProductVersion, proposalId));

        Assert.False(result.Succeeded);
        Assert.Equal("apply.regression", result.Refusal!.Code);
        Assert.Equal(FailureReason.Refused, result.Refusal.Reason);
        Assert.Contains("not ready to apply", result.Refusal.Message, StringComparison.Ordinal);
        Assert.Equal("proposed", GetRecord(proposalId).Status);
    }

    [Fact]
    public void FreshBaselineEvidenceGatesARegressionBeforeAnyAssessmentIsPromoted()
    {
        var proposalId = FinalizeAndTrial("baseline-regression", "baseline regression gloss");
        var intentDigest = GetRecord(proposalId).IntentDigest!;
        RecordAssessment(null, null, "Correctness", ("alpha", true));
        RecordAssessment(proposalId, intentDigest, "Correctness", ("alpha", false));
        Assert.Null(GetCurrentAssessmentId());

        var result = ProposalCommands.Apply(new ApplyRequest(_fwDataPath, ProductVersion, proposalId));

        Assert.False(result.Succeeded);
        Assert.Equal("apply.regression", result.Refusal!.Code);
        Assert.Contains("alpha", result.Refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RecapturingUnchangedStateKeepsMeasuredEvidenceReadyToApply()
    {
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(_fwDataPath));
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        var proposalId = FinalizeAndTrial("recaptured-evidence", "recaptured evidence gloss");
        RecordAssessment(proposalId, GetRecord(proposalId).IntentDigest!, "Correctness", ("alpha", true));
        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        var projectKey = ProjectWorkspaceKey.Compute(new ProjectLocator(_fwDataPath,
            Path.GetFileNameWithoutExtension(_fwDataPath)));
        var before = new BaselineRepository(database).GetCurrent(projectKey)!.Token;
        var recaptured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(_fwDataPath));
        Assert.True(recaptured.Succeeded, recaptured.Refusal?.Message);
        var after = new BaselineRepository(database).GetCurrent(projectKey)!.Token;
        Assert.Equal(before.SemanticIdentity, after.SemanticIdentity);

        var result = ProposalCommands.Apply(new ApplyRequest(_fwDataPath, ProductVersion, proposalId));

        Assert.True(result.Succeeded, result.Refusal?.Message);
    }

    [Fact]
    public void ApplyWithNoAssessmentAtAll_IsRefused_BecauseNothingHasMeasuredWhatItWouldDo()
    {
        var proposalId = FinalizeAndTrial("unmeasured", "unmeasured gloss");

        var refused = ProposalCommands.Apply(new ApplyRequest(_fwDataPath, ProductVersion, proposalId));

        Assert.False(refused.Succeeded);
        Assert.Equal("apply.not-ready", refused.Refusal!.Code);
        Assert.Contains("no Assessment covers its current content", refused.Refusal.Message, StringComparison.Ordinal);
        Assert.Equal("proposed", GetRecord(proposalId).Status);

        Assert.True(ProposalCommands.Apply(new ApplyRequest(
            _fwDataPath, ProductVersion, proposalId, Force: true)).Succeeded);
        Assert.Equal("applied", GetRecord(proposalId).Status);
    }

    [Fact]
    public void TimingOnlyAssessmentsKeepApplyGatedAndExplainRequiredCorrectness()
    {
        var proposalId = FinalizeAndTrial("timing-only", "timing only gloss");
        var intentDigest = GetRecord(proposalId).IntentDigest!;
        RecordAssessment(proposalId, intentDigest, "ParseTime", ("alpha", true));
        RecordAssessment(proposalId, intentDigest, "ObjectTiming", ("alpha", true));

        var result = ProposalCommands.Apply(new ApplyRequest(_fwDataPath, ProductVersion, proposalId));

        Assert.False(result.Succeeded);
        Assert.Equal("apply.not-ready", result.Refusal!.Code);
        Assert.Contains("Apply requires a Correctness Assessment", result.Refusal.Message, StringComparison.Ordinal);
        Assert.Contains("approved morphology comparisons", result.Refusal.Message, StringComparison.Ordinal);
        Assert.Contains("timing-only evidence cannot satisfy", result.Refusal.Message, StringComparison.Ordinal);
        Assert.Contains("no stems", result.Refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain($"trial {proposalId}", result.Refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("--force", result.Refusal.Message, StringComparison.Ordinal);
        Assert.Equal("proposed", GetRecord(proposalId).Status);
    }

    [Fact]
    public void ApplyIsRefusedWhenTheAssessmentMeasuredADifferentProjectStateThanTheCurrentOne()
    {
        var proposalId = FinalizeAndTrial("stale", "stale gloss");
        var intentDigest = GetRecord(proposalId).IntentDigest!;
        PromotePrevious(("alpha", true));
        RecordAssessment(proposalId, intentDigest, "Correctness", baselineToken: """{"snapshot":"moved"}""",
            words: ("alpha", true));

        var result = ProposalCommands.Apply(new ApplyRequest(_fwDataPath, ProductVersion, proposalId));

        Assert.False(result.Succeeded);
        Assert.Equal("apply.not-ready", result.Refusal!.Code);
        Assert.Contains("not been re-run since the project moved", result.Refusal.Message, StringComparison.Ordinal);
        Assert.Equal("proposed", GetRecord(proposalId).Status);
    }

    [Fact]
    public void RegressionGated_WithoutAnOverride_RefusesAndNamesTheRegression()
    {
        WriteConfiguration(gateOnRegression: true, purgeOnApply: true);
        var proposalId = FinalizeAndTrial("regression-gated-refuse", "regression gated refuse gloss");
        var intentDigest = GetRecord(proposalId).IntentDigest!;
        var previousId = PromotePrevious(("alpha", true));
        RecordAssessment(proposalId, intentDigest, "Correctness", ("alpha", false));

        var result = ProposalCommands.Apply(new ApplyRequest(_fwDataPath, ProductVersion, proposalId));

        Assert.False(result.Succeeded);
        Assert.Equal("apply.regression", result.Refusal!.Code);
        Assert.Contains("regression", result.Refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("alpha", result.Refusal.Message, StringComparison.Ordinal);
        // Nothing moved: the previous Assessment is still current, and the Proposal was never applied.
        Assert.Equal(previousId, GetCurrentAssessmentId());
        Assert.Equal("proposed", GetRecord(proposalId).Status);
    }

    [Fact]
    public void RegressionGated_WithForce_AppliesAnyway()
    {
        WriteConfiguration(gateOnRegression: true, purgeOnApply: true);
        var proposalId = FinalizeAndTrial("regression-gated-override", "regression gated override gloss");
        var intentDigest = GetRecord(proposalId).IntentDigest!;
        PromotePrevious(("alpha", true));
        var candidateId = RecordAssessment(proposalId, intentDigest, "Correctness", ("alpha", false));

        var result = ProposalCommands.Apply(new ApplyRequest(
            _fwDataPath, ProductVersion, proposalId, Force: true));

        Assert.True(result.Succeeded);
        Assert.Equal("applied", GetRecord(proposalId).Status);
        Assert.Equal(candidateId, GetCurrentAssessmentId());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReleasedPlainApplyProcessConsumesBoundDryRunAndPersistsItsReceipt(bool force)
    {
        var gloss = force ? "human readiness override" : "reviewed human apply";
        var id = FinalizeAndTrial("released-plain-apply", gloss);
        var digest = GetRecord(id).IntentDigest!;
        if (!force)
            RecordAssessment(id, digest, "Correctness", ("alpha", true));
        var root = Path.Combine(Path.GetDirectoryName(_fwDataPath)!, "released-plain-apply-worker");
        var arguments = new List<string> { "apply", id, "--project", _fwDataPath, "--json" };
        if (force)
        {
            var notReady = await CliInProcess.RunAsync(root, null, developerCommands: false, arguments.ToArray());
            Assert.Equal(2, notReady.ExitCode);
            Assert.Empty(notReady.Output);
            Assert.Equal("apply.not-ready", ProjectionJson.Deserialize<FailureEnvelope>(notReady.Error)!.Code);
            arguments.Add("--force");
        }

        var applied = await CliInProcess.RunAsync(root, null, developerCommands: false, arguments.ToArray());

        Assert.True(applied.ExitCode == 0, applied.Error + applied.Output);
        Assert.Empty(applied.Error);
        var response = ProjectionJson.Deserialize<ApplyPendingResult>(applied.Output)!;
        Assert.True(response.Ok);
        Assert.True(response.Applied);
        Assert.Equal(id, response.Receipt!.ProposalId);
        Assert.Equal(force, response.Receipt.Readiness!.Forced);
        Assert.Equal("applied", GetRecord(id).Status);
        using var cache = new FwDataProjectLoader().LoadScratchCache(_fwDataPath);
        Assert.Equal(gloss, cache.ServiceLocator.GetInstance<ILexSenseRepository>().GetObject(_seed.FirstSenseId)
            .Gloss.get_String(cache.WritingSystemFactory.GetWsFromStr(NewLangProjFixture.AnalysisTag)).Text);
        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Receipts WHERE ProposalId = $id;";
        command.Parameters.AddWithValue("$id", id);
        Assert.Equal(1L, command.ExecuteScalar());
    }

    [Fact]
    public async Task ReleasedPlainApplyForceCannotOverrideProjectDrift()
    {
        var id = FinalizeAndTrial("released-drift-apply", "proposal gloss");
        var loader = new FwDataProjectLoader();
        using (var cache = loader.LoadCache(_fwDataPath))
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                cache.ServiceLocator.GetInstance<ILexSenseRepository>().GetObject(_seed.FirstSenseId).Gloss
                    .set_String(cache.WritingSystemFactory.GetWsFromStr(NewLangProjFixture.AnalysisTag),
                        TsStringUtils.MakeString("FieldWorks edit after Dry Run", cache.DefaultAnalWs)));
            loader.Save(cache);
        }
        var bytes = File.ReadAllBytes(_fwDataPath);
        var root = Path.Combine(Path.GetDirectoryName(_fwDataPath)!, "released-drift-worker");

        var refused = await CliInProcess.RunAsync(root, null, developerCommands: false,
            "apply", id, "--project", _fwDataPath, "--force", "--json");

        Assert.NotEqual(0, refused.ExitCode);
        Assert.Empty(refused.Output);
        var failure = ProjectionJson.Deserialize<FailureEnvelope>(refused.Error)!;
        Assert.Equal("apply.precondition-failed", failure.Code);
        Assert.Equal(bytes, File.ReadAllBytes(_fwDataPath));
        Assert.Equal("proposed", GetRecord(id).Status);
    }

    // --- helpers ---

    [Fact]
    public void ApplyRefusesProjectDriftAsAPreconditionNotAnUntrustworthyCache()
    {
        var id = FinalizeAndTrial("precondition-drift-apply", "proposal gloss");
        var loader = new FwDataProjectLoader();
        using (var cache = loader.LoadCache(_fwDataPath))
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                cache.ServiceLocator.GetInstance<ILexSenseRepository>().GetObject(_seed.FirstSenseId).Gloss
                    .set_String(cache.WritingSystemFactory.GetWsFromStr(NewLangProjFixture.AnalysisTag),
                        TsStringUtils.MakeString("FieldWorks edit after Dry Run", cache.DefaultAnalWs)));
            loader.Save(cache);
        }

        var result = ProposalCommands.Apply(new ApplyRequest(_fwDataPath, ProductVersion, id, Force: true));

        Assert.False(result.Succeeded);
        Assert.Equal("apply.precondition-failed", result.Refusal!.Code);
        Assert.NotEqual(FailureReason.StoreInconsistent, result.Refusal.Reason);
        Assert.DoesNotContain("no longer trustworthy", result.Refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyRefusesATargetDeletedAfterTheDryRunWithItsPreconditionKind()
    {
        var id = FinalizeAndTrial("precondition-target-gone-apply", "proposal gloss");
        var loader = new FwDataProjectLoader();
        using (var cache = loader.LoadCache(_fwDataPath))
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                cache.ServiceLocator.GetInstance<ILexSenseRepository>().GetObject(_seed.FirstSenseId).Delete());
            loader.Save(cache);
        }

        var result = ProposalCommands.Apply(new ApplyRequest(_fwDataPath, ProductVersion, id, Force: true));

        Assert.False(result.Succeeded);
        Assert.Equal("apply.precondition-failed", result.Refusal!.Code);
        Assert.Equal("target-gone", result.Refusal.Facts["precondition"]);
        Assert.Equal(CanonicalId.FromGuid(_seed.FirstSenseId).Value, result.Refusal.Facts["target"]);
        Assert.True(result.Refusal.Facts.ContainsKey("operationId"));
        Assert.Equal(id, result.Refusal.Facts["proposalId"]);
    }

    private string FinalizeAndTrial(string draftName, string newGloss)
    {
        var senseGuid = _seed.FirstSenseId;
        var wsTag = NewLangProjFixture.AnalysisTag;
        var canonicalId = CanonicalId.FromGuid(senseGuid);

        Assert.True(ProposalCommands.New(new NewDraftRequest(_fwDataPath, ProductVersion, draftName, null)).Succeeded);
        Assert.True(ProposalCommands.AddSetGloss(new AddSetGlossRequest(
            _fwDataPath, ProductVersion, draftName, canonicalId.Value, wsTag, newGloss)).Succeeded);
        DraftRationale.Author(_fwDataPath, draftName, "Clarify the first sense gloss", "Exercise apply's promotion and sweep.");

        var finalizeResult = ProposalCommands.Finalize(new FinalizeRequest(_fwDataPath, ProductVersion, draftName));
        Assert.True(finalizeResult.Succeeded);
        var proposalId = finalizeResult.Value!.ProposalId;

        Assert.True(DryRunJobRunner.Run(_fwDataPath, ProductVersion, proposalId).Succeeded);
        return proposalId;
    }

    private ProposalRecord GetRecord(string proposalId)
    {
        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        return new ProposalRepository(database).Get(CanonicalId.Parse(proposalId));
    }

    private string? GetCurrentAssessmentId()
    {
        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        return new AssessmentRepository(database).GetCurrent()?.AssessmentId;
    }

    /// <summary>Records and promotes an unrelated Assessment: the project's prior current one.</summary>
    private string PromotePrevious(params (string Word, bool Analysed)[] words)
    {
        var id = RecordAssessment(proposalId: null, intentDigest: null, "Correctness", words);
        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        new AssessmentRepository(database).PromoteToCurrent(id);
        return id;
    }

    private string RecordAssessment(
        string? proposalId, string? intentDigest, string kind, params (string Word, bool Analysed)[] words) =>
        RecordAssessment(proposalId, intentDigest, kind, null, words);

    private string RecordAssessment(
        string? proposalId, string? intentDigest, string kind, string? baselineToken,
        params (string Word, bool Analysed)[] words)
    {
        var selection = Selection.Create("test", words.Select(w => w.Word));
        var assessmentId = CanonicalId.Mint("assessment/").Value;
        var assessedWords = words
            .Select(w => CorrectnessFixture.Word(w.Word, w.Analysed))
            .ToArray();

        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        var project = new ProjectLocator(_fwDataPath, Path.GetFileNameWithoutExtension(_fwDataPath));
        baselineToken ??= JsonSerializer.Serialize(new BaselineRepository(database)
            .GetCurrent(ProjectWorkspaceKey.Compute(project))!.Token, MotifJson.CreateOptions());
        new AssessmentRepository(database).Record(new NewAssessmentRecord(
            AssessmentId: assessmentId,
            ProposalId: proposalId is null ? null : CanonicalId.Parse(proposalId),
            ProposalIntentDigest: intentDigest,
            Assessor: "pangloss",
            Kind: kind,
            ScopeJson: """{"perWordLimitMs":1000,"perWordStepLimit":{"steps":200000,"isUnbounded":false}}""",
            ScopeDigest: "sha256:" + new string('a', 64),
            Subject: new AssessmentSubject.Words(selection, "none", "1"),
            BaselineToken: baselineToken,
            OutcomeDigest: "sha256:" + new string('b', 64),
            SemanticDigest: "sha256:" + new string('c', 64),
            GrammarSourceSha256: GrammarSha,
            ModelFingerprint: "model",
            Pipeline: "pipeline",
            DiagnosticCount: 0,
            Material: new AssessmentRecordMaterial.Words(assessedWords, null, null, [])
        ));
        return assessmentId;
    }

    private void WriteConfiguration(bool gateOnRegression, bool purgeOnApply)
    {
        var path = ProjectConfigurationReader.PathFor(
            new ProjectLocator(_fwDataPath, Path.GetFileNameWithoutExtension(_fwDataPath)));
        File.WriteAllText(path, ProjectConfigurationFile.Render(
            new ProjectConfiguration(
                new[] { AssessmentScopeConfiguration.Default() }, gateOnRegression, purgeOnApply)));
    }

}
