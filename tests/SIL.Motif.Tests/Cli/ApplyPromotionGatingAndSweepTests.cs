using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Config;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.Parser;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>
/// The trap ADR 0042's Trial amendment names, driven through the real <c>apply</c> verb on a real project:
/// applying promotes the Proposal's candidate Assessment to current, and a sweep of the Proposal's own
/// working Assessments must not take the promoted one with it. Also covers regression gating (ADR 0042
/// decision 5) — off by default, blocking when configured, and an override recorded as a Decision.
/// </summary>
[Collection(TestFixtures.LcmCacheTestCollection.Name)]
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

        var result = ProposalCommands.Apply(new ApplyRequest(_fwDataPath, ProductVersion, proposalId, "tester"));
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
    public void ApplyWithPurgeOff_LeavesEveryAssessmentReadable()
    {
        WriteConfiguration(gateOnRegression: false, purgeOnApply: false);
        var proposalId = FinalizeAndTrial("trap-purge-off", "purge-off test gloss");
        var intentDigest = GetRecord(proposalId).IntentDigest!;
        var candidateId = RecordAssessment(proposalId, intentDigest, "Correctness", ("alpha", true));
        var scratchId = RecordAssessment(proposalId, intentDigest, "ParseTime", ("alpha", true));

        var result = ProposalCommands.Apply(new ApplyRequest(_fwDataPath, ProductVersion, proposalId, "tester"));
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

        var result = ProposalCommands.Apply(new ApplyRequest(_fwDataPath, ProductVersion, proposalId, "tester"));

        Assert.False(result.Succeeded);
        Assert.Equal("apply.not-ready", result.Refusal!.Code);
        Assert.Equal(FailureReason.Refused, result.Refusal.Reason);
        Assert.Contains("not ready to apply", result.Refusal.Message, StringComparison.Ordinal);
        Assert.Equal("proposed", GetRecord(proposalId).Status);
    }

    [Fact]
    public void ApplyWithNoAssessmentAtAll_IsRefused_BecauseNothingHasMeasuredWhatItWouldDo()
    {
        var proposalId = FinalizeAndTrial("unmeasured", "unmeasured gloss");

        var refused = ProposalCommands.Apply(new ApplyRequest(_fwDataPath, ProductVersion, proposalId, "tester"));

        Assert.False(refused.Succeeded);
        Assert.Equal("apply.not-ready", refused.Refusal!.Code);
        Assert.Contains("no Assessment covers its current content", refused.Refusal.Message, StringComparison.Ordinal);
        Assert.Equal("proposed", GetRecord(proposalId).Status);

        Assert.True(ProposalCommands.Apply(new ApplyRequest(
            _fwDataPath, ProductVersion, proposalId, "tester", Force: true)).Succeeded);
        Assert.Equal("applied", GetRecord(proposalId).Status);
    }

    [Fact]
    public void TimingOnlyAssessmentsKeepApplyGatedAndExplainRequiredCorrectness()
    {
        var proposalId = FinalizeAndTrial("timing-only", "timing only gloss");
        var intentDigest = GetRecord(proposalId).IntentDigest!;
        RecordAssessment(proposalId, intentDigest, "ParseTime", ("alpha", true));
        RecordAssessment(proposalId, intentDigest, "ObjectTiming", ("alpha", true));

        var result = ProposalCommands.Apply(new ApplyRequest(_fwDataPath, ProductVersion, proposalId, "tester"));

        Assert.False(result.Succeeded);
        Assert.Equal("apply.not-ready", result.Refusal!.Code);
        Assert.Contains("Apply requires a Correctness Assessment", result.Refusal.Message, StringComparison.Ordinal);
        Assert.Contains("approved morphology comparisons", result.Refusal.Message, StringComparison.Ordinal);
        Assert.Contains("timing-only evidence cannot satisfy", result.Refusal.Message, StringComparison.Ordinal);
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

        var result = ProposalCommands.Apply(new ApplyRequest(_fwDataPath, ProductVersion, proposalId, "tester"));

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

        var result = ProposalCommands.Apply(new ApplyRequest(_fwDataPath, ProductVersion, proposalId, "tester"));

        Assert.False(result.Succeeded);
        Assert.Equal("apply.not-ready", result.Refusal!.Code);
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
            _fwDataPath, ProductVersion, proposalId, "tester", Force: true));

        Assert.True(result.Succeeded);
        Assert.Equal("applied", GetRecord(proposalId).Status);
        Assert.Equal(candidateId, GetCurrentAssessmentId());
    }

    // --- helpers ---

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
        RecordAssessment(proposalId, intentDigest, kind, "{}", words);

    private string RecordAssessment(
        string? proposalId, string? intentDigest, string kind, string baselineToken,
        params (string Word, bool Analysed)[] words)
    {
        var selection = Selection.Create("test", words.Select(w => w.Word));
        var assessmentId = CanonicalId.Mint("assessment/").Value;
        var assessedWords = words
            .Select(w => CorrectnessFixture.Word(w.Word, w.Analysed))
            .ToArray();

        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        new AssessmentRepository(database).Record(new NewAssessmentRecord(
            AssessmentId: assessmentId,
            ProposalId: proposalId is null ? null : CanonicalId.Parse(proposalId),
            ProposalIntentDigest: intentDigest,
            Assessor: "pangloss",
            Kind: kind,
            ScopeJson: """{"perWordLimitMs":1000,"perWordStepLimit":200000}""",
            ScopeDigest: "sha256:" + new string('a', 64),
            TokeniserName: "none",
            TokeniserVersion: "1",
            BaselineToken: baselineToken,
            Selection: selection,
            OutcomeDigest: "sha256:" + new string('b', 64),
            SemanticDigest: "sha256:" + new string('c', 64),
            GrammarSourceSha256: GrammarSha,
            ModelFingerprint: "model",
            Pipeline: "pipeline",
            DiagnosticCount: 0,
            Words: assessedWords));
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
