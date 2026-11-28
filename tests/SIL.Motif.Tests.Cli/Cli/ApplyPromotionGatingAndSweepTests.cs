using SIL.Motif.Tests.Parser;
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
public sealed class ApplyPromotionGatingAndSweepTests : ApplyPromotionGatingFixtureBase
{
    public ApplyPromotionGatingAndSweepTests(PristineProjectFixture pristine) : base(pristine)
    {
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
        Assert.Contains("motif baseline capture --project", refused.Refusal.Message, StringComparison.Ordinal);
        Assert.Equal("proposed", GetRecord(proposalId).Status);
    }
}
