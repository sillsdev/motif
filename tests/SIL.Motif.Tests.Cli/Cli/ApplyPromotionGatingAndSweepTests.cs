using SIL.Motif.Commands.Review;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Review;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Runtime.Review;
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
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.LcmUtils;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.LCModel.Core.Text;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>
/// The trap ADR 0020's Trial amendment names, driven through the real <c>apply</c> verb on a real project:
/// applying promotes the Proposal's candidate Assessment to current, and a sweep of the Proposal's own
/// working Assessments must not take the promoted one with it. Also covers regression gating (ADR 0020
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnAutonomousApplyWithAMessageAndNoForceSucceeds_AndTheLogShowsModeAndMessage(bool expectedLoss)
    {
        WriteGateConfiguration();
        var proposalId = FinalizeAndTrial("autonomous-ready", "autonomous ready gloss");
        RecordAssessment(proposalId, GetRecord(proposalId).IntentDigest!, "Correctness", ("alpha", true));
        RecordGate(proposalId, expectedLoss ? "covered-loss" : "green");

        var result = ProposalCommands.Apply(new ApplyRequest(_fwDataPath, ProductVersion, proposalId,
            Attribution: new ApplyAttribution(ApplyModes.Autonomous, "Glossed from the corpus", WriteNote: true)));

        Assert.True(result.Succeeded, result.Refusal?.Message);
        Assert.Equal(("autonomous", "Glossed from the corpus"),
            (result.Value!.AppliedLogEntry.Mode, result.Value.AppliedLogEntry.Message));
        var entry = Assert.Single(ProposalCommands.Log(new LogRequest(_fwDataPath)).Value!.Entries);
        Assert.Equal(("autonomous", "Glossed from the corpus"), (entry.Mode, entry.Description));
    }

    [Theory]
    [InlineData("missing-report")]
    [InlineData("undeclared-scope")]
    [InlineData("omitted-required")]
    [InlineData("changed-policy")]
    [InlineData("changed-gold-list")]
    [InlineData("legacy")]
    [InlineData("loss")]
    [InlineData("partial-expected")]
    [InlineData("unmeasured")]
    [InlineData("old-revision")]
    [InlineData("old-baseline")]
    [InlineData("failed-latest")]
    [InlineData("cancelled-latest")]
    [InlineData("parser-changed")]
    [InlineData("parser-missing")]
    [InlineData("pending-latest")]
    [InlineData("working-set")]
    public void AutonomousApplyRequiresTheNewestTrialsCurrentGreenGate(string scenario)
    {
        WriteGateConfiguration();
        var proposalId = FinalizeAndTrial("autonomous-gate", "gate protected gloss");
        RecordAssessment(proposalId, GetRecord(proposalId).IntentDigest!, "Correctness", ("alpha", true));
        if (scenario != "missing-report")
        {
            RecordGate(proposalId, "green");
            RecordGate(proposalId, scenario);
        }
        var result = ProposalCommands.Apply(new ApplyRequest(_fwDataPath, ProductVersion, proposalId,
            Attribution: new ApplyAttribution(ApplyModes.Autonomous, "Review confirmed")));

        Assert.False(result.Succeeded);
        Assert.Equal("apply.trial-gate", result.Refusal!.Code);
        Assert.Contains("Trial", result.Refusal.Message, StringComparison.Ordinal);
        Assert.Contains("--expect-loss", result.Refusal.Message, StringComparison.Ordinal);
        Assert.Equal("proposed", GetRecord(proposalId).Status);
        if (scenario == "working-set")
            Assert.Contains("working-set Trial", result.Refusal.Message, StringComparison.Ordinal);
        if (scenario is "failed-latest" or "cancelled-latest" or "pending-latest")
        {
            var latest = TrialReviewReads.ReadStored(_fwDataPath, new ReviewReadRequest("project", proposalId, Latest: true));
            Assert.Equal(result.Refusal.Facts["jobId"], Assert.IsType<TrialJobResult>(latest.Value).JobId);
        }
    }

    [Fact]
    public void AutonomousApplyRequiresADeclaredGateAndShowsExactToml()
    {
        var id = FinalizeAndTrial("undeclared", "proposal gloss");
        RecordAssessment(id, GetRecord(id).IntentDigest!, "Correctness", ("alpha", true));
        var refused = ProposalCommands.Apply(new ApplyRequest(_fwDataPath, ProductVersion, id,
            Attribution: new ApplyAttribution(ApplyModes.Autonomous, "Review confirmed")));
        Assert.False(refused.Succeeded);
        Assert.Equal("apply.gate-undeclared", refused.Refusal!.Code);
        Assert.Contains("[gate]\nword-sets = [\"gold\"]", refused.Refusal.Message);
        Assert.Contains("[[review-word-set]]", refused.Refusal.Message);
        Assert.Contains("gold.jsonl", refused.Refusal.Message);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AutonomousApplyRefusesExternalChangesOutsideTheProposalFootprint(bool writingSystem, bool recapture)
    {
        WriteGateConfiguration();
        var id = FinalizeAndTrial("external-state", "proposal gloss");
        RecordAssessment(id, GetRecord(id).IntentDigest!, "Correctness", ("alpha", true));
        RecordGate(id, "green");
        if (writingSystem)
        {
            var path = Directory.GetFiles(Path.Combine(Path.GetDirectoryName(_fwDataPath)!, "WritingSystemStore"), "*.ldml")[0];
            var document = System.Xml.Linq.XDocument.Load(path);
            var special = new System.Xml.Linq.XElement("special",
                new System.Xml.Linq.XElement(System.Xml.Linq.XName.Get("caseAlias", "urn://palaso.org/ldmlExtensions/v2"),
                    new System.Xml.Linq.XAttribute("value", "tr")));
            document.Root!.Add(special);
            document.Save(path);
        }
        else
        {
            var loader = new FwDataProjectLoader();
            using var cache = loader.LoadCache(_fwDataPath);
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                cache.LangProject.PartsOfSpeechOA.PossibilitiesOS[0].Name.set_String(cache.DefaultAnalWs,
                    TsStringUtils.MakeString("External category", cache.DefaultAnalWs)));
            loader.Save(cache);
        }
        if (recapture)
        {
            using var database = ProjectMotifDatabase.Open(_fwDataPath);
            var key = ProjectWorkspaceKey.Compute(new ProjectLocator(_fwDataPath, Path.GetFileNameWithoutExtension(_fwDataPath)));
            var before = new BaselineRepository(database).GetCurrent(key)!.Token;
            var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(_fwDataPath));
            Assert.True(captured.Succeeded, captured.Refusal?.Message);
            var after = new BaselineRepository(database).GetCurrent(key)!.Token;
            Assert.Equal(before.SemanticIdentity, after.SemanticIdentity);
            Assert.NotEqual(before.BundleDigest, after.BundleDigest);
            Assert.True(DryRunJobRunner.Run(_fwDataPath, ProductVersion, id).Succeeded);
        }
        var bytes = File.ReadAllBytes(_fwDataPath);
        var refused = ProposalCommands.Apply(new ApplyRequest(_fwDataPath, ProductVersion, id,
            Attribution: new ApplyAttribution(ApplyModes.Autonomous, "Review confirmed")));
        Assert.False(refused.Succeeded);
        Assert.Equal("apply.trial-gate", refused.Refusal!.Code);
        Assert.Equal(bytes, File.ReadAllBytes(_fwDataPath));
        Assert.Equal("proposed", GetRecord(id).Status);
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
    public void ANewerNotReadyAssessmentDoesNotHideAnOlderReadyOne_AndTheSweepKeepsTheOneRead()
    {
        WriteConfiguration(gateOnRegression: true, purgeOnApply: true);
        var proposalId = FinalizeAndTrial("older-ready", "older ready gloss");
        var intentDigest = GetRecord(proposalId).IntentDigest!;
        PromotePrevious(("alpha", true));
        var olderReadyId = RecordAssessment(proposalId, intentDigest, "Correctness", ("alpha", true));
        System.Threading.Thread.Sleep(30);
        var newerRegressingId = RecordAssessment(proposalId, intentDigest, "Correctness", ("alpha", false));

        var result = ProposalCommands.Apply(new ApplyRequest(_fwDataPath, ProductVersion, proposalId));

        Assert.True(result.Succeeded, result.Refusal?.Message);
        Assert.Equal("applied", GetRecord(proposalId).Status);
        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        var assessments = new AssessmentRepository(database);
        Assert.Equal(olderReadyId, assessments.GetCurrent()!.AssessmentId);
        Assert.Throws<KeyNotFoundException>(() => assessments.Get(newerRegressingId));
        Assert.Equal(new[] { olderReadyId },
            assessments.ListByProposal(CanonicalId.Parse(proposalId)).Select(record => record.AssessmentId));
    }

    [Fact]
    public void WhenBothAssessmentsAreReadyTheNewestIsTheOneRead()
    {
        WriteConfiguration(gateOnRegression: true, purgeOnApply: true);
        var proposalId = FinalizeAndTrial("both-ready", "both ready gloss");
        var intentDigest = GetRecord(proposalId).IntentDigest!;
        PromotePrevious(("alpha", true));
        RecordAssessment(proposalId, intentDigest, "Correctness", ("alpha", true));
        System.Threading.Thread.Sleep(30);
        var newerId = RecordAssessment(proposalId, intentDigest, "Correctness", ("alpha", true));

        var result = ProposalCommands.Apply(new ApplyRequest(_fwDataPath, ProductVersion, proposalId));

        Assert.True(result.Succeeded, result.Refusal?.Message);
        Assert.Equal(newerId, GetCurrentAssessmentId());
    }

    [Fact]
    public void WhenNoAssessmentIsReadyTheRefusalNamesTheNewestInItsFactsAndMessage()
    {
        WriteConfiguration(gateOnRegression: true, purgeOnApply: true);
        var proposalId = FinalizeAndTrial("none-ready", "none ready gloss");
        var intentDigest = GetRecord(proposalId).IntentDigest!;
        PromotePrevious(("alpha", true));
        RecordAssessment(proposalId, intentDigest, "Correctness", ("alpha", false));
        System.Threading.Thread.Sleep(30);
        var newestId = RecordAssessment(proposalId, intentDigest, "Correctness", ("alpha", false));

        var result = ProposalCommands.Apply(new ApplyRequest(_fwDataPath, ProductVersion, proposalId));

        Assert.Equal("apply.regression", result.Refusal!.Code);
        Assert.Equal(newestId, result.Refusal.Facts["assessmentId"]);
        Assert.Equal(proposalId, result.Refusal.Facts["proposalId"]);
        Assert.Contains($"Apply read Correctness Assessment {newestId}", result.Refusal.Message, StringComparison.Ordinal);
        Assert.Contains("the other 1 Assessment(s) of this revision were checked too and none is ready",
            result.Refusal.Message, StringComparison.Ordinal);
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

    [Fact]
    public void AnAutonomousApplyMarksAndThenClearsAMisspellingWithItsIntendedFormReadBack()
    {
        var wordformGuid = CreateWordform("motifmisspelt");
        WriteGateConfiguration();
        var wordform = CanonicalId.FromGuid(wordformGuid).Value;
        var tag = NewLangProjFixture.VernacularTag;

        var mark = FinalizeAndTrial("autonomous-misspelled", draft => Assert.True(
            ProposalCommands.ComposeEditAnalysisDecision(new(_fwDataPath, ProductVersion, draft, JsonSerializer.Serialize(new
            {
                decision = "misspelled", wordform,
                intendedForm = new { form = "motifmisspelled", writingSystem = tag },
                note = "The Bible text spells it this way throughout",
            }))).Succeeded));
        RecordAssessment(mark, GetRecord(mark).IntentDigest!, "Correctness", ("alpha", true));
        RecordGate(mark, "green");
        var marked = ProposalCommands.Apply(new ApplyRequest(_fwDataPath, ProductVersion, mark,
            Attribution: new ApplyAttribution(ApplyModes.Autonomous, "Mark the misspelling")));
        Assert.True(marked.Succeeded, marked.Refusal?.Message);
        Assert.Equal("autonomous", marked.Value!.AppliedLogEntry.Mode);
        Assert.Equal(2, SpellingStatusOf(wordformGuid));
        var shown = ProposalCommands.EvidenceShow(new(_fwDataPath, ProductVersion, wordform));
        var row = Assert.Single(shown.Value!.Records);
        Assert.Equal(("misspelled", WfiWordformSpellingStatusOperationKinds.SetSpellingStatus), (row.Tier, row.Kind));
        Assert.Equal("motifmisspelled", row.Detail["intendedForm"]);
        Assert.Equal(tag, row.Detail["intendedFormWritingSystem"]);
        Assert.Equal("The Bible text spells it this way throughout", row.Detail["note"]);

        WriteGateConfiguration();
        var clear = FinalizeAndTrial("autonomous-cleared", draft => Assert.True(
            ProposalCommands.ComposeEditAnalysisDecision(new(_fwDataPath, ProductVersion, draft, JsonSerializer.Serialize(new
            {
                decision = "clear-misspelled", wordform, note = "The form is attested in the lexicon",
            }))).Succeeded));
        RecordAssessment(clear, GetRecord(clear).IntentDigest!, "Correctness", ("alpha", true));
        RecordGate(clear, "green");
        var cleared = ProposalCommands.Apply(new ApplyRequest(_fwDataPath, ProductVersion, clear,
            Attribution: new ApplyAttribution(ApplyModes.Autonomous, "Clear the misspelling")));
        Assert.True(cleared.Succeeded, cleared.Refusal?.Message);
        Assert.Equal(0, SpellingStatusOf(wordformGuid));
        var history = ProposalCommands.EvidenceShow(new(_fwDataPath, ProductVersion, wordform)).Value!.Records;
        Assert.Equal(2, history.Count);
        Assert.Contains(history, record => record.Tier == "undecided" &&
            record.Kind == WfiWordformSpellingStatusOperationKinds.ClearSpellingStatus &&
            record.Detail["note"] == "The form is attested in the lexicon");
    }

    [Theory]
    [InlineData("loss")]
    [InlineData("failed-latest")]
    public void AnAutonomousSpellingApplyStillMeetsTheTrialGate(string scenario)
    {
        var wordformGuid = CreateWordform("motifmisspelt");
        WriteGateConfiguration();
        var wordform = CanonicalId.FromGuid(wordformGuid).Value;
        var proposalId = FinalizeAndTrial("autonomous-spelling-gate", draft => Assert.True(
            ProposalCommands.ComposeEditAnalysisDecision(new(_fwDataPath, ProductVersion, draft, JsonSerializer.Serialize(new
            {
                decision = "misspelled", wordform,
                intendedForm = new { form = "motifmisspelled", writingSystem = NewLangProjFixture.VernacularTag },
            }))).Succeeded));
        RecordAssessment(proposalId, GetRecord(proposalId).IntentDigest!, "Correctness", ("alpha", true));
        RecordGate(proposalId, scenario);

        var result = ProposalCommands.Apply(new ApplyRequest(_fwDataPath, ProductVersion, proposalId,
            Attribution: new ApplyAttribution(ApplyModes.Autonomous, "Mark the misspelling")));

        Assert.False(result.Succeeded);
        Assert.Equal("apply.trial-gate", result.Refusal!.Code);
        Assert.Equal(0, SpellingStatusOf(wordformGuid));
        Assert.Equal("proposed", GetRecord(proposalId).Status);
    }

    [Fact]
    public void AnAgentStillCannotApplyThroughTheAgentSurface()
    {
        Assert.Equal(SIL.Motif.Commands.Catalog.AgentClass.HumanOnly,
            SIL.Motif.Commands.Catalog.CommandCatalog.All.Single(command => command.Name == "apply").Agent);
        Assert.Equal(SIL.Motif.Commands.Catalog.AgentClass.Draft,
            SIL.Motif.Commands.Catalog.CommandCatalog.All.Single(command => command.Name == "compose-edit-analysis-decision").Agent);
    }

    private Guid CreateWordform(string form)
    {
        var loader = new FwDataProjectLoader();
        using var cache = loader.LoadCache(_fwDataPath);
        IWfiWordform created = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () => created = cache.ServiceLocator
            .GetInstance<IWfiWordformFactory>().Create(TsStringUtils.MakeString(form, cache.DefaultVernWs)));
        loader.Save(cache);
        return created.Guid;
    }

    private int SpellingStatusOf(Guid wordformId)
    {
        using var cache = new FwDataProjectLoader().LoadCache(_fwDataPath);
        return cache.ServiceLocator.GetInstance<IWfiWordformRepository>().GetObject(wordformId).SpellingStatus;
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

    private string FinalizeAndTrial(string draftName, string newGloss) => FinalizeAndTrial(draftName, draft =>
    {
        var canonicalId = CanonicalId.FromGuid(_seed.FirstSenseId);
        Assert.True(ProposalCommands.AddSetGloss(new AddSetGlossRequest(
            _fwDataPath, ProductVersion, draft, canonicalId.Value, NewLangProjFixture.AnalysisTag, newGloss)).Succeeded);
    });

    private string FinalizeAndTrial(string draftName, Action<string> compose)
    {
        Assert.True(ProposalCommands.New(new NewDraftRequest(_fwDataPath, ProductVersion, draftName, null)).Succeeded);
        compose(draftName);
        DraftRationale.Author(_fwDataPath, draftName, "Clarify the first sense gloss", "Exercise apply's promotion and sweep.");

        var finalizeResult = ProposalCommands.Finalize(new FinalizeRequest(_fwDataPath, ProductVersion, draftName));
        Assert.True(finalizeResult.Succeeded);
        var proposalId = finalizeResult.Value!.ProposalId;

        Assert.True(DryRunJobRunner.Run(_fwDataPath, ProductVersion, proposalId).Succeeded);
        return proposalId;
    }

    private void RecordGate(string proposalId, string scenario)
    {
        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        var project = new ProjectLocator(_fwDataPath, Path.GetFileNameWithoutExtension(_fwDataPath));
        var key = ProjectWorkspaceKey.Compute(project);
        var proposal = new ProposalRepository(database).Get(CanonicalId.Parse(proposalId));
        var proposalJson = scenario == "old-revision"
            ? JsonSerializer.Serialize(new { contractVersions = new Dictionary<string, string>(), proposalId, operations = Array.Empty<object>() })
            : proposal.ProposalJson!;
        var saved = new BaselineRepository(database).GetCurrent(key)!;
        var baseline = saved.Token;
        if (scenario == "old-baseline") baseline = new BaselineToken(baseline.ProjectIdentity,
            "sha256:" + new string('f', 64), baseline.ProjectionVersion, baseline.CapturedUtc, baseline.BundleDigest);
        string[] words = ["alpha", "beta"];
        var configuration = new ProjectConfigurationReader().Read(project);
        var manifest = ReviewWordSetManifestReader.Read(Path.Combine(Path.GetDirectoryName(_fwDataPath)!, "gold.jsonl"), "regression");
        var inputs = new FrozenReviewInputs(key, proposalId, "scope", "manifest", null,
            [new ReviewSetBinding("gold", "Gold", "regression", "members", "expectations", manifest.SourceDigest, [])], [],
            words.Select(word => new ReviewPlannedCase("case-" + word, "key-" + word, word, "ws", null,
                [new ReviewMembership("gold", "case-" + word, "regression", 1, null, null, [])], [])).ToArray());
        inputs = inputs with { DeclaredGate = DeclaredTrialGatePolicy.Capture(configuration, inputs) };
        if (scenario == "undeclared-scope") inputs = inputs with { DeclaredGate = null };
        if (scenario == "omitted-required") inputs = inputs with { Sets = [], OmittedSetIds = ["gold"],
            Cases = inputs.Cases.Select(item => item with { Memberships = [] }).ToArray() };
        var reference = ReviewInputArtifacts.Store(database,
            new ReviewInputArtifact(inputs, new Dictionary<string, byte[]>())
                { Baseline = baseline, BaselineSidecarDigest = ProgressSourceIdentity.CaptureSidecars(saved.FwDataPath)!.Digest });
        var input = new TrialJobInput(proposalJson, null, ReviewInputs: reference)
        { Sample = scenario == "working-set" ? TrialSampleKind.WorkingSet : null,
          ExpectedLosses = scenario == "covered-loss" ? words : scenario == "partial-expected" ? ["alpha"] : [] };
        var jobs = new JobRepository(database);
        var job = jobs.Create("gate-" + Guid.NewGuid().ToString("N"), key, "trial",
            JsonSerializer.Serialize(input, MotifJson.CreateOptions()), JobTimestamp.FormatUtc(DateTimeOffset.UtcNow));
        if (scenario == "pending-latest") return;
        jobs.Transition(job.JobId, JobStatus.Running);
        var completion = JsonSerializer.Serialize(new { baselineToken = baseline, assessmentIds = Array.Empty<string>(),
            allWords = false, droppedGrammar = new DroppedGrammar([], 0) }, MotifJson.CreateOptions());
        jobs.Transition(job.JobId, scenario switch
        {
            "failed-latest" => JobStatus.Failed,
            "cancelled-latest" => JobStatus.Cancelled,
            _ => JobStatus.Completed,
        }, completion);
        ParseWordEvidence Before(string word) => new("fieldworks-parse-analysis/v1", 0, word, 0, false, false, false,
            [new ParseAnalysis([new ParseMorph("11111111-1111-1111-1111-111111111111",
                "33333333-3333-3333-3333-333333333333", null, null)])], []);
        var loses = scenario is "loss" or "partial-expected" or "covered-loss";
        TrialReviewEvidence.Save(database, new TrialReviewLedger(job.JobId, reference,
            words.Select(word => new TrialReviewObservation("case-" + word, word, "analysed", Before(word))).ToArray(),
            words.Select(word => new TrialReviewObservation("case-" + word, word,
                scenario == "unmeasured" ? "timed-out" : loses ? "no-analysis" : "analysed",
                scenario == "unmeasured" ? Before(word) with { TimedOut = true, Analyses = [] } :
                loses ? Before(word) with { Analyses = [] } : Before(word))).ToArray(), []));
        var reportId = new TrialReviewPublisher(database).PublishAsync(new ReviewPublishRequest(job.JobId)).GetAwaiter().GetResult();
        if (scenario == "changed-policy")
            File.AppendAllText(Path.ChangeExtension(_fwDataPath, ".motif.toml"), "\n[gain-gate]\nmode = \"hold\"\n");
        if (scenario == "changed-gold-list")
            File.AppendAllText(Path.Combine(Path.GetDirectoryName(_fwDataPath)!, "gold.jsonl"), "\n");
        {
            var report = new ReviewReportRepository(database).ReadManifest(reportId)!.Summary;
            var parser = ParserIdentity.ReadFreshKey(PanGlossExecutable.TryLocate());
            report = report with { Pins = report.Pins with { Checkpoints = report.Pins.Checkpoints
                .Select(pin => pin with { ParserDigest = pin.Side == Side.After ? scenario switch
                {
                    "parser-changed" => "sha256:" + new string('0', 64),
                    "parser-missing" => null,
                    _ => parser,
                } : parser }).ToArray() } };
            if (scenario == "legacy") report = report with { Gate = null, Losses = null, DeclaredGate = null };
            var json = JsonSerializer.Serialize(report, ReviewJson.Options);
            using var connection = database.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE ReviewReports SET SummaryJson=$json,SummaryDigest=$hash WHERE ReportId=$id;";
            command.Parameters.AddWithValue("$json", json);
            command.Parameters.AddWithValue("$hash", Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json))));
            command.Parameters.AddWithValue("$id", reportId.Value);
            command.ExecuteNonQuery();
        }
    }

    // The receipt a green Trial of this Proposal would write, bound to the Baseline as it stands now.
    private string WriteReceipt(string proposalId, string name)
    {
        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        var project = new ProjectLocator(_fwDataPath, Path.GetFileNameWithoutExtension(_fwDataPath));
        var record = new ProposalRepository(database).Get(CanonicalId.Parse(proposalId));
        var job = Trials.NewestFirst(Trials.JobsFor(database, project, CanonicalId.Parse(proposalId))).First();
        var baseline = new BaselineRepository(database).GetCurrent(ProjectWorkspaceKey.Compute(project))!.Token;
        var receipt = TrialReceiptFile.Build(proposalId, Trials.ReadCurrentIntentDigest(record)!, baseline.SemanticSnapshotDigest,
            job.JobId, new TrialExpectations([], []), new ReadingDeltaCounts(1, 1, 0, 0, 0, 0, 0, 0, 0, 0));
        var path = Path.Combine(Path.GetDirectoryName(_fwDataPath)!, name);
        Assert.Null(TrialReceiptFile.Write(path, receipt));
        return path;
    }

    private string ReadyProposal(string draft, string gloss)
    {
        var proposalId = FinalizeAndTrial(draft, gloss);
        RecordAssessment(proposalId, GetRecord(proposalId).IntentDigest!, "Correctness", ("alpha", true));
        return proposalId;
    }

    [Fact]
    public void ApplyWithAReceiptThatBindsTheCurrentRevisionAndBaselineSucceeds()
    {
        var proposalId = ReadyProposal("receipt-valid", "receipt valid gloss");
        var receipt = WriteReceipt(proposalId, "receipt.json");

        var result = ProposalCommands.Apply(new ApplyRequest(_fwDataPath, ProductVersion, proposalId,
            Attribution: new ApplyAttribution(ReceiptPath: receipt)));

        Assert.True(result.Succeeded, result.Refusal?.Message);
        Assert.Equal("applied", GetRecord(proposalId).Status);
    }

    [Fact]
    public void ApplyWithAReceiptIsRefusedAfterAnotherApplyMovedTheBaseline()
    {
        var first = ReadyProposal("receipt-first", "receipt first gloss");
        var second = ReadyProposal("receipt-second", "receipt second gloss");
        var receipt = WriteReceipt(second, "receipt.json");
        Assert.True(ProposalCommands.Apply(new ApplyRequest(_fwDataPath, ProductVersion, first)).Succeeded);

        var refused = ProposalCommands.Apply(new ApplyRequest(_fwDataPath, ProductVersion, second,
            Attribution: new ApplyAttribution(ReceiptPath: receipt)));

        Assert.False(refused.Succeeded);
        Assert.Equal("apply.receipt-stale", refused.Refusal!.Code);
        Assert.Equal("proposed", GetRecord(second).Status);
    }

    [Fact]
    public void ApplyWithAReceiptIsRefusedAfterFieldWorksDataChangedSinceTheBaseline()
    {
        var proposalId = ReadyProposal("receipt-edited", "receipt edited gloss");
        var receipt = WriteReceipt(proposalId, "receipt.json");
        var loader = new FwDataProjectLoader();
        using (var cache = loader.LoadCache(_fwDataPath))
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                cache.LangProject.PartsOfSpeechOA.PossibilitiesOS[0].Name.set_String(cache.DefaultAnalWs,
                    TsStringUtils.MakeString("External category", cache.DefaultAnalWs)));
            loader.Save(cache);
        }

        var refused = ProposalCommands.Apply(new ApplyRequest(_fwDataPath, ProductVersion, proposalId,
            Attribution: new ApplyAttribution(ReceiptPath: receipt)));

        Assert.False(refused.Succeeded);
        Assert.Equal("apply.receipt-stale", refused.Refusal!.Code);
        Assert.Contains("FieldWorks data changed", refused.Refusal.Message, StringComparison.Ordinal);
        Assert.Equal("proposed", GetRecord(proposalId).Status);
    }

    private void WriteGateConfiguration()
    {
        File.WriteAllText(Path.ChangeExtension(_fwDataPath, ".motif.toml"),
            "[gate]\nword-sets = [\"gold\"]\n[[review-word-set]]\nid = \"gold\"\nname = \"Gold\"\nrole = \"regression\"\nmanifest = \"gold.jsonl\"\n");
        var rows = new[] { "alpha", "beta" }.Select(word => JsonSerializer.Serialize(new
        {
            caseId = "case-" + word, word, writingSystemId = "ws", source = new { citation = "Gold list" },
            expectationRefs = Array.Empty<string>(),
        }));
        File.WriteAllLines(Path.Combine(Path.GetDirectoryName(_fwDataPath)!, "gold.jsonl"),
            new[] { "{\"schema\":\"motif.review-word-set\",\"schemaVersion\":1}" }.Concat(rows));
        var baseline = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(_fwDataPath));
        Assert.True(baseline.Succeeded, baseline.Refusal?.Message);
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

        using (var database = ProjectMotifDatabase.Open(_fwDataPath))
        {
            var project = new ProjectLocator(_fwDataPath, Path.GetFileNameWithoutExtension(_fwDataPath));
            baselineToken ??= JsonSerializer.Serialize(new BaselineRepository(database)
                .GetCurrent(ProjectWorkspaceKey.Compute(project))!.Token, MotifJson.CreateOptions());
            new AssessmentRepository(database).Record(new NewAssessmentRecord(
                AssessmentId: assessmentId,
                ProposalId: proposalId is null ? null : CanonicalId.Parse(proposalId),
                ProposalIntentDigest: intentDigest,
                Assessor: "pangloss",
                Kind: kind,
                ScopeJson: """{"perWordLimitMs":1000,"perWordParserWorkLimit":{"steps":200000,"isUnbounded":false}}""",
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
        }
        if (proposalId is not null) StoredTrialJob.Record(_fwDataPath, proposalId, assessmentId);
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
