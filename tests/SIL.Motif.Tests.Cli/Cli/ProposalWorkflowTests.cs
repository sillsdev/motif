using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.Parser;
using SIL.Motif.Runner.AppliedLog;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Store;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>
/// Proof, on a real project: drives the CLI command handlers directly (never shells out to
/// the built executable) through the full <c>new -&gt; add-set-gloss -&gt; finalize -&gt; dry-run -&gt;
/// apply -&gt; log</c> loop against a real sense's <c>CanonicalId.FromGuid(sense.Guid)</c>, proving
/// the paired project database and the thin CLI drive the real Contract/Runner/Host.
/// </summary>
/// <remarks>
/// A workflow test, not an end-to-end one: no process boundary is crossed here. Whether the shipped
/// executables do this is <c>RunnerSpineTests</c>' subject. <c>dry-run</c> is a job (ADR 0041 decision 7),
/// so <see cref="RunDryRun"/> stands in for the real runner: it records a Baseline pointing at this
/// project's own saved file and drains exactly one queued job through the real <see cref="DryRunJobHandler"/>.
/// </remarks>
[Collection(TestFixtures.LcmCacheTestCollection.Name)]
public sealed class ProposalWorkflowTests
{
    private const string ProductVersion = "1.0";

    private readonly SeededProject _seed;
    private readonly string _fwDataPath;

    public ProposalWorkflowTests(PristineProjectFixture pristine)
    {
        _seed = pristine.Seed;
        using var scratch = pristine.NewScratch();
        _fwDataPath = scratch.ProjectId.Path;
    }

    [Fact]
    public void FullLoop_New_AddSetGloss_Finalize_DryRun_Apply_Log_DrivesRealProjectEndToEnd()
    {
        var senseGuid = _seed.FirstSenseId;
        var wsTag = NewLangProjFixture.AnalysisTag;
        var originalGloss = SeededProject.FirstGloss;
        var canonicalId = SIL.Motif.Contract.Ids.CanonicalId.FromGuid(senseGuid);
        var newGloss = originalGloss + " (revised sense, Stage E CLI)";
        const string draftName = "stage-e-demo";
        const string label = "Stage E end-to-end demo";
        const string shortDescription = "Clarify the first sense gloss";
        const string extendedExplanation = "Replace the ambiguous gloss with the intended analysis.";
        const string applier = "motif-cli-tests";

        // --- new ---
        var newResult = ProposalCommands.New(new NewDraftRequest(_fwDataPath, ProductVersion, draftName, label));
        Assert.True(newResult.Succeeded);
        Assert.Equal(draftName, newResult.Value!.DraftName);
        Assert.Equal(label, newResult.Value.Label);
        var proposalId = newResult.Value.ProposalId;
        Assert.True(DraftExists(draftName));

        // --- add-set-gloss ---
        var addResult = ProposalCommands.AddSetGloss(new AddSetGlossRequest(
            _fwDataPath, ProductVersion, draftName, canonicalId.Value, wsTag, newGloss));
        Assert.True(addResult.Succeeded);
        Assert.Equal(draftName, addResult.Value!.DraftName);
        Assert.Equal(canonicalId.Value, addResult.Value.Target);
        Assert.Equal(newGloss, addResult.Value.Text);
        Assert.Equal(1, addResult.Value.OperationCount);
        DraftRationale.Author(_fwDataPath, draftName, shortDescription, extendedExplanation);

        // --- finalize ---
        var finalizeResult = ProposalCommands.Finalize(new FinalizeRequest(_fwDataPath, ProductVersion, draftName));
        Assert.True(finalizeResult.Succeeded);
        Assert.False(DraftExists(draftName)); // draft cleared on finalize

        Assert.Equal(proposalId, finalizeResult.Value!.ProposalId);
        var intentDigest = finalizeResult.Value.IntentDigest;
        var committed = GetRecord(proposalId);
        Assert.Equal(intentDigest, committed.IntentDigest);
        Assert.Equal("proposed", committed.Status);

        // --- list ---
        var listResult = ProposalCommands.List(new ListProposalsRequest(_fwDataPath, ProductVersion));
        Assert.True(listResult.Succeeded);
        var listed = Assert.Single(listResult.Value!.Proposals);
        Assert.Equal(proposalId, listed.ProposalId);
        Assert.Equal("proposed", listed.Status);

        // --- show ---
        var showResult = ProposalCommands.Show(new ShowProposalRequest(_fwDataPath, ProductVersion, proposalId));
        Assert.True(showResult.Succeeded);
        Assert.Equal(proposalId, showResult.Value!.ProposalId);
        Assert.Equal(shortDescription, showResult.Value.Label);
        Assert.Equal(extendedExplanation, showResult.Value.Comment);
        var operation = Assert.Single(showResult.Value.Operations);
        Assert.Equal(canonicalId.Value, operation.Target);
        Assert.Equal("lexical/lexSense/setGloss", operation.Kind);
        var showJsonResult = ProposalCommands.Show(new ShowProposalRequest(_fwDataPath, ProductVersion, proposalId));
        Assert.True(showJsonResult.Succeeded);
        Assert.Equal(shortDescription, showJsonResult.Value!.Label);
        Assert.Equal(extendedExplanation, showJsonResult.Value.Comment);

        // --- dry-run: real before/after from LibLCM, non-mutating ---
        var dryRunResult = RunDryRun(proposalId);
        Assert.True(dryRunResult.Succeeded);
        var dryRunEffect = Assert.Single(dryRunResult.Value!.Effects);
        Assert.Equal(canonicalId.Value, dryRunEffect.CanonicalId);
        Assert.Equal("lexical/sense/gloss", dryRunEffect.Field);
        var dryRunChange = Assert.Single(dryRunEffect.Changes);
        Assert.Equal(originalGloss, dryRunChange.Before);
        Assert.Equal(newGloss, dryRunChange.After);
        Assert.StartsWith("sha256:", dryRunResult.Value.EffectDigest, StringComparison.Ordinal);

        // The dry run must not have mutated the project: gloss unchanged when re-read from disk.
        AssertGlossOnDisk(senseGuid, wsTag, originalGloss);

        // --- apply: real commit + save ---
        var applyResult = ProposalCommands.Apply(
            new ApplyRequest(_fwDataPath, ProductVersion, proposalId, applier, Force: true));
        Assert.True(applyResult.Succeeded);
        Assert.False(applyResult.Value!.AlreadyApplied);
        Assert.Equal(proposalId, applyResult.Value.ProposalId);
        Assert.Equal(applier, applyResult.Value.AppliedLogEntry.User);
        var appliedEffect = Assert.Single(applyResult.Value.Effects);
        Assert.Equal(canonicalId.Value, appliedEffect.CanonicalId);
        var appliedChange = Assert.Single(appliedEffect.Changes);
        Assert.Equal(originalGloss, appliedChange.Before);
        Assert.Equal(newGloss, appliedChange.After);
        var appliedRecord = GetRecord(proposalId);
        Assert.Equal("applied", appliedRecord.Status);
        Assert.Equal(shortDescription, appliedRecord.Label);
        Assert.Equal(extendedExplanation, appliedRecord.Comment);
        AssertReceiptCount(proposalId, intentDigest, 1);

        // Apply persists: re-open the saved project from disk and check the gloss + one applied-log entry.
        AssertGlossOnDisk(senseGuid, wsTag, newGloss);
        AssertAppliedLogEntryCount(1);
        AssertReceiptCount(proposalId, intentDigest, 1);

        // --- log ---
        var logResult = ProposalCommands.Log(new LogRequest(_fwDataPath));
        Assert.True(logResult.Succeeded);
        Assert.Equal(1, logResult.Value!.EntryCount);
        Assert.Equal(applier, Assert.Single(logResult.Value.Entries).User);

        // --- apply again: idempotent, no duplicate log entry, no re-mutation ---
        var secondApplyResult = ProposalCommands.Apply(
            new ApplyRequest(_fwDataPath, ProductVersion, proposalId, applier, Force: true));
        Assert.True(secondApplyResult.Succeeded);
        Assert.True(secondApplyResult.Value!.AlreadyApplied);

        AssertGlossOnDisk(senseGuid, wsTag, newGloss);
        AssertAppliedLogEntryCount(1);
    }

    [Fact]
    public void FileDryRun_PreparesOneScratchWithPrerequisites_AndReportsOnlyTheDependentEffect()
    {
        var target = SIL.Motif.Contract.Ids.CanonicalId.FromGuid(_seed.FirstSenseId);
        var intermediateGloss = SeededProject.FirstGloss + " prerequisite file";
        var finalGloss = SeededProject.FirstGloss + " dependent file";
        var prerequisiteId = FinalizeSetGloss("file-prerequisite", target.Value, intermediateGloss);
        var dependentId = FinalizeSetGloss(
            "file-dependent", target.Value, finalGloss, prerequisiteId);

        var result = RunDryRun(dependentId);

        Assert.True(result.Succeeded);
        var effect = Assert.Single(result.Value!.Effects);
        Assert.Equal(intermediateGloss, Assert.Single(effect.Changes).Before);
        Assert.Equal(finalGloss, effect.Changes[0].After);
        AssertGlossOnDisk(_seed.FirstSenseId, NewLangProjFixture.AnalysisTag, SeededProject.FirstGloss);
    }

    /// <remarks>
    /// The receipt boundary: <c>apply</c> commits and saves the mutation to the real project (a
    /// durable, observable fact on disk) before it ever tries to record "applied" in the store. The
    /// paired database is made read-only right beforehand, so that write is the one that fails -- this
    /// must not be reported the way a rolled-back apply is, since nothing here rolled back. See
    /// <see cref="NeedsReconciliationException"/> and <see cref="ReconciliationBoundary.ReceiptRecording"/>.
    /// </remarks>
    [Fact]
    public void Apply_ManifestWriteFails_AfterAGenuineCommitAndSave_ReportsReconciliation_NotRollback()
    {
        var senseGuid = _seed.FirstSenseId;
        var wsTag = NewLangProjFixture.AnalysisTag;
        var originalGloss = SeededProject.FirstGloss;
        var canonicalId = SIL.Motif.Contract.Ids.CanonicalId.FromGuid(senseGuid);
        var newGloss = originalGloss + " (receipt boundary test)";
        const string draftName = "receipt-boundary-demo";
        const string applier = "motif-cli-tests";

        Assert.True(ProposalCommands.New(new NewDraftRequest(_fwDataPath, ProductVersion, draftName, null)).Succeeded);
        Assert.True(ProposalCommands.AddSetGloss(new AddSetGlossRequest(
            _fwDataPath, ProductVersion, draftName, canonicalId.Value, wsTag, newGloss)).Succeeded);
        DraftRationale.Author(
            _fwDataPath, draftName, "Clarify the first sense gloss", "Record the intended analysis before applying the proposal.");
        var finalizeResult = ProposalCommands.Finalize(new FinalizeRequest(_fwDataPath, ProductVersion, draftName));
        Assert.True(finalizeResult.Succeeded);
        var proposalId = finalizeResult.Value!.ProposalId;

        Assert.True(RunDryRun(proposalId).Succeeded);

        var dbPath = PairedDatabasePath();
        File.SetAttributes(dbPath, FileAttributes.ReadOnly);
        try
        {
            var applyResult = ProposalCommands.Apply(
                new ApplyRequest(_fwDataPath, ProductVersion, proposalId, applier, Force: true));

            Assert.False(applyResult.Succeeded);
            Assert.Equal("apply.reconciliation-needed", applyResult.Refusal!.Code);
            Assert.Contains("proposal store failed", applyResult.Refusal.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("rolled back", applyResult.Refusal.Message, StringComparison.OrdinalIgnoreCase);

            // The load-bearing proof: the mutation genuinely committed and saved despite the report above.
            AssertGlossOnDisk(senseGuid, wsTag, newGloss);
            AssertAppliedLogEntryCount(1);
        }
        finally
        {
            File.SetAttributes(dbPath, FileAttributes.Normal);
        }

        // The store itself was left exactly as dry-run wrote it -- never touched by the failed write.
        Assert.Equal("proposed", GetRecord(proposalId).Status);
    }

    [Fact]
    public void UnknownCommands_And_MissingArguments_ReturnNonZeroWithClearErrors()
    {
        var missingDraft = ProposalCommands.AddSetGloss(new AddSetGlossRequest(
            _fwDataPath, ProductVersion, "does-not-exist", "agent_AAECAwQFBgcICQoLDA0ODw", "en", "x"));
        Assert.False(missingDraft.Succeeded);
        Assert.Equal("draft.not-found", missingDraft.Refusal!.Code);
        Assert.Equal("does-not-exist", missingDraft.Refusal.Facts["draftName"]);

        var badTarget = ProposalCommands.New(new NewDraftRequest(_fwDataPath, ProductVersion, "bad-target-draft", null));
        Assert.True(badTarget.Succeeded);
        var invalidTarget = ProposalCommands.AddSetGloss(new AddSetGlossRequest(
            _fwDataPath, ProductVersion, "bad-target-draft", "not-a-canonical-id", "en", "x"));
        Assert.False(invalidTarget.Succeeded);
        Assert.Equal("operation.invalid-target", invalidTarget.Refusal!.Code);
        Assert.Equal("not-a-canonical-id", invalidTarget.Refusal.Facts["target"]);
        DraftRationale.Author(
            _fwDataPath, "bad-target-draft", "Test an empty proposal", "Keep the no-operations refusal independently observable.");

        var emptyFinalize = ProposalCommands.Finalize(new FinalizeRequest(_fwDataPath, ProductVersion, "bad-target-draft"));
        Assert.False(emptyFinalize.Succeeded);
        Assert.Equal("draft.invalid", emptyFinalize.Refusal!.Code);
        Assert.Contains("no operations", emptyFinalize.Refusal.Message, StringComparison.Ordinal);

        var missingProposal = ProposalCommands.Show(new ShowProposalRequest(
            _fwDataPath, ProductVersion, "agent_AAECAwQFBgcICQoLDA0ODw"));
        Assert.False(missingProposal.Succeeded);
        Assert.Equal("proposal.not-found", missingProposal.Refusal!.Code);
        Assert.Equal(FailureReason.NotFound, missingProposal.Refusal.Reason);

        var missingProjectPath = Path.Combine(Path.GetDirectoryName(_fwDataPath)!, "does-not-exist.fwdata");
        var missingProject = ProposalCommands.Log(new LogRequest(missingProjectPath));
        Assert.False(missingProject.Succeeded);
        Assert.Equal("project.not-found", missingProject.Refusal!.Code);
        Assert.Equal(FailureReason.InvalidArgument, missingProject.Refusal.Reason);
    }

    private CommandOutcome<DryRunProjection> RunDryRun(string proposalId) =>
        DryRunJobRunner.Run(_fwDataPath, ProductVersion, proposalId);

    private void AssertGlossOnDisk(Guid senseGuid, string wsTag, string expectedGloss)
    {
        using var cache = new FwDataProjectLoader().LoadScratchCache(_fwDataPath);
        var wsHandle = cache.WritingSystemFactory.GetWsFromStr(wsTag);
        var senseRepo = cache.ServiceLocator.GetInstance<ILexSenseRepository>();
        Assert.Equal(expectedGloss, senseRepo.GetObject(senseGuid).Gloss.get_String(wsHandle).Text);
    }

    private void AssertAppliedLogEntryCount(int expectedCount)
    {
        using var cache = new FwDataProjectLoader().LoadScratchCache(_fwDataPath);
        Assert.Equal(expectedCount, ProjectAppliedLog.ReadAll(cache).Count);
    }

    private string FinalizeSetGloss(
        string draftName, string targetId, string text, params string[] prerequisiteIds)
    {
        Assert.True(ProposalCommands.New(new NewDraftRequest(_fwDataPath, ProductVersion, draftName, null)).Succeeded);
        Assert.True(ProposalCommands.AddSetGloss(new AddSetGlossRequest(
            _fwDataPath, ProductVersion, draftName, targetId, NewLangProjFixture.AnalysisTag, text)).Succeeded);
        if (prerequisiteIds.Length > 0)
        {
            using var database = ProjectMotifDatabase.Open(_fwDataPath);
            var repository = new ProposalRepository(database);
            var draftJson = repository.GetDraft(draftName).ProposalJson!;
            var draft = JsonNode.Parse(draftJson)!.AsObject();
            draft["requires"] = new JsonArray(
                prerequisiteIds.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray());
            repository.SaveDraft(draftName, draft.ToJsonString());
        }

        DraftRationale.Author(
            _fwDataPath, draftName, "Prepare a dependent gloss", "Establish the lexical state required by later proposals.");
        var finalized = ProposalCommands.Finalize(new FinalizeRequest(_fwDataPath, ProductVersion, draftName));
        Assert.True(finalized.Succeeded);
        return finalized.Value!.ProposalId;
    }

    private bool DraftExists(string draftName)
    {
        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        return new ProposalRepository(database).DraftNameExists(draftName);
    }

    private ProposalRecord GetRecord(string proposalId)
    {
        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        return new ProposalRepository(database).Get(CanonicalId.Parse(proposalId));
    }

    private string PairedDatabasePath()
    {
        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        return database.FullPath;
    }

    [Fact]
    public void MissingChangeFingerprintBlocksApplyEvenWithForce()
    {
        var loader = new FwDataProjectLoader();
        Guid wordformGuid;
        using (var cache = loader.LoadCache(_fwDataPath))
        {
            IWfiWordform wordform = null!;
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                wordform = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                    .Create(TsStringUtils.MakeString("unverified-word", cache.DefaultVernWs)));
            wordformGuid = wordform.Guid;
            loader.Save(cache);
        }

        const string draftName = "missing-change-fit";
        var created = ProposalCommands.New(new NewDraftRequest(_fwDataPath, ProductVersion, draftName, null));
        Assert.True(created.Succeeded);
        using (var database = ProjectMotifDatabase.Open(_fwDataPath))
        {
            var repository = new ProposalRepository(database);
            var draft = JsonNode.Parse(repository.GetDraft(draftName).ProposalJson!)!.AsObject();
            draft["contractVersions"]!["analysis"] = "1.0";
            draft["operations"]!.AsArray().Add(new JsonObject
            {
                ["operationId"] = CanonicalId.Mint().Value,
                ["kind"] = "analysis/wfiWordform/setSpellingStatus",
                ["target"] = CanonicalId.FromGuid(wordformGuid).Value,
                ["after"] = new JsonObject { ["value"] = 2 },
                ["extensions"] = new JsonObject
                {
                    ["changeId"] = CanonicalId.Mint().Value,
                    ["changeFit"] = null,
                },
            });
            repository.SaveDraft(draftName, draft.ToJsonString());
        }
        DraftRationale.Author(_fwDataPath, draftName, "Mark spelling incorrect", "The word is misspelled.");
        Assert.True(ProposalCommands.Finalize(new FinalizeRequest(_fwDataPath, ProductVersion, draftName)).Succeeded);
        Assert.True(RunDryRun(created.Value!.ProposalId).Succeeded);

        var preflight = ProposalCommands.Preflight(new PreflightRequest(
            _fwDataPath, ProductVersion, created.Value.ProposalId));
        Assert.True(preflight.Succeeded);
        var fit = Assert.Single(preflight.Value!.Changes);
        Assert.False(fit.StillFits);
        Assert.Contains("fingerprint", fit.Reason, StringComparison.OrdinalIgnoreCase);
        var apply = ProposalCommands.Apply(new ApplyRequest(
            _fwDataPath, ProductVersion, created.Value.ProposalId, "tester", Force: true));
        Assert.Equal("apply.change-no-longer-fits", apply.Refusal?.Code);
    }

    [Fact]
    public void DeletedWordform_IsNoLongerFitAndBlocksApplyEvenWithForce()
    {
        var loader = new FwDataProjectLoader();
        Guid wordformGuid;
        using (var cache = loader.LoadCache(_fwDataPath))
        {
            IWfiWordform wordform = null!;
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                wordform = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                    .Create(TsStringUtils.MakeString("fit-word", cache.DefaultVernWs)));
            wordformGuid = wordform.Guid;
            loader.Save(cache);
        }

        const string draftName = "fit-deleted-word";
        var created = ProposalCommands.New(new NewDraftRequest(_fwDataPath, ProductVersion, draftName, null));
        Assert.True(created.Succeeded);
        var operationId = CanonicalId.Mint();
        using (var database = ProjectMotifDatabase.Open(_fwDataPath))
        {
            var repository = new ProposalRepository(database);
            var draft = JsonNode.Parse(repository.GetDraft(draftName).ProposalJson!)!.AsObject();
            draft["contractVersions"]!["analysis"] = "1.0";
            draft["operations"]!.AsArray().Add(new JsonObject
            {
                ["operationId"] = operationId.Value,
                ["kind"] = "analysis/wfiWordform/setSpellingStatus",
                ["target"] = CanonicalId.FromGuid(wordformGuid).Value,
                ["after"] = new JsonObject { ["value"] = 2 },
                ["extensions"] = new JsonObject { ["changeFit"] = new JsonObject
                {
                    ["wordformId"] = CanonicalId.FromGuid(wordformGuid).Value,
                    ["wordformForm"] = "fit-word",
                    ["baselineToken"] = "captured-baseline",
                } },
            });
            repository.SaveDraft(draftName, draft.ToJsonString());
        }
        DraftRationale.Author(_fwDataPath, draftName, "Mark spelling incorrect", "The selected word is misspelled.");
        Assert.True(ProposalCommands.Finalize(new FinalizeRequest(_fwDataPath, ProductVersion, draftName)).Succeeded);
        Assert.True(RunDryRun(created.Value!.ProposalId).Succeeded);

        using (var cache = loader.LoadCache(_fwDataPath))
        {
            var wordform = cache.ServiceLocator.GetInstance<IWfiWordformRepository>().GetObject(wordformGuid);
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () => wordform.Delete());
            loader.Save(cache);
        }

        var preflight = ProposalCommands.Preflight(new PreflightRequest(
            _fwDataPath, ProductVersion, created.Value.ProposalId));
        Assert.True(preflight.Succeeded);
        var fit = Assert.Single(preflight.Value!.Changes);
        Assert.False(fit.StillFits);
        Assert.Contains("deleted", fit.Reason, StringComparison.OrdinalIgnoreCase);
        var textPreflight = RunPreflightCli(created.Value.ProposalId, false);
        Assert.Contains("no longer fits", textPreflight, StringComparison.Ordinal);
        var jsonPreflight = JsonNode.Parse(RunPreflightCli(created.Value.ProposalId, true))!;
        Assert.False(jsonPreflight["changes"]![0]!["stillFits"]!.GetValue<bool>());
        var apply = ProposalCommands.Apply(new ApplyRequest(
            _fwDataPath, ProductVersion, created.Value.ProposalId, "tester", Force: true));
        Assert.False(apply.Succeeded);
        Assert.Equal("apply.change-no-longer-fits", apply.Refusal!.Code);
    }

    [Fact]
    public void HeldProject_ApplyIsBusyAndProposalRemainsPendingForRetry()
    {
        var proposalId = FinalizeSetGloss("held-project",
            CanonicalId.FromGuid(_seed.FirstSenseId).Value, "gloss after release");
        Assert.True(RunDryRun(proposalId).Succeeded);

        using (var held = new FileStream(_fwDataPath + ".lock", FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None))
        {
            var refused = ProposalCommands.Apply(new ApplyRequest(
                _fwDataPath, ProductVersion, proposalId, "tester", Force: true));
            Assert.False(refused.Succeeded);
            Assert.Equal("apply.project-in-use", refused.Refusal!.Code);
            Assert.Equal(FailureReason.Busy, refused.Refusal.Reason);
            Assert.Equal("proposed", GetRecord(proposalId).Status);
        }

        var retried = ProposalCommands.Apply(new ApplyRequest(
            _fwDataPath, ProductVersion, proposalId, "tester", Force: true));
        Assert.True(retried.Succeeded);
        Assert.Equal("applied", GetRecord(proposalId).Status);
    }

    [Fact]
    public void PendingChangesKeepTheChosenReadingAndDistinctWordformSlots()
    {
        var loader = new FwDataProjectLoader();
        const string word = "parser-change";
        Guid wordformGuid;
        using (var cache = loader.LoadCache(_fwDataPath))
        {
            IWfiWordform wordform = null!;
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                wordform = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                    .Create(TsStringUtils.MakeString(word, cache.DefaultVernWs)));
            wordformGuid = wordform.Guid;
            loader.Save(cache);
        }
        var managedRoot = Path.Combine(Path.GetDirectoryName(_fwDataPath)!, "managed-parser-change");
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(_fwDataPath), managedRoot);
        Assert.True(captured.Succeeded);
        string formGuid;
        string msaGuid;
        using (var cache = loader.LoadScratchCache(_fwDataPath))
        {
            var entry = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(_seed.FirstEntryId);
            formGuid = entry.LexemeFormOA!.Guid.ToString("D");
            msaGuid = entry.MorphoSyntaxAnalysesOC.First().Guid.ToString("D");
        }
        var assessmentId = CanonicalId.Mint().Value;
        var baselineToken = JsonSerializer.Serialize(captured.Value!.Token);
        using (var database = ProjectMotifDatabase.Open(_fwDataPath))
        {
            new AssessmentRepository(database).Record(new NewAssessmentRecord(
                assessmentId, null, null, "test", "ParseTime", "{}", "sha256:scope",
                "whitespace-and-punctuation", "1", baselineToken,
                Selection.Create("parser-change", [word]), "sha256:outcome", "sha256:semantic",
                "sha256:grammar", "model", "pipeline", 0,
                [new AssessedWord(word, "parsed", [], 0)
                {
                    Morphology = new ParseWordEvidence(ParseMorphEvidence.Schema, 0, word, 0,
                        false, false, false,
                        [new ParseAnalysis([new ParseMorph(formGuid, msaGuid, null, null)]),
                         new ParseAnalysis([new ParseMorph(formGuid, msaGuid, null, "chosen-second")])], []),
                }]));
        }
        var pending = PendingChanges.Load(new PendingChangesRequest(_fwDataPath, ProductVersion));
        Assert.True(pending.Succeeded, pending.Refusal?.Message);
        var secondReading = new ParseAnalysis([new ParseMorph(formGuid, msaGuid, null, "chosen-second")]);
        var candidateChange = PendingChanges.Put(new PutPendingChangeRequest(_fwDataPath, ProductVersion,
            pending.Value!.Revision, new ChangeIntent(CanonicalId.Mint().Value, "add-candidate",
                CanonicalId.FromGuid(wordformGuid).Value, word, assessmentId, secondReading)));
        Assert.True(candidateChange.Succeeded, candidateChange.Refusal?.Message);
        var spellingChange = PendingChanges.Put(new PutPendingChangeRequest(_fwDataPath, ProductVersion,
            candidateChange.Value!.Revision, new ChangeIntent(CanonicalId.Mint().Value, "incorrect-spelling",
                CanonicalId.FromGuid(wordformGuid).Value, word)));
        Assert.True(spellingChange.Succeeded, spellingChange.Refusal?.Message);
        Assert.Equal(2, spellingChange.Value!.Changes.Count);
        using (var database = ProjectMotifDatabase.Open(_fwDataPath))
        {
            var pendingDraft = JsonNode.Parse(new ProposalRepository(database)
                .GetDraft(PendingChanges.DraftName).ProposalJson!)!;
            Assert.Equal("chosen-second", pendingDraft["operations"]![0]!["extensions"]!["changeFit"]!
                ["reading"]!["morphs"]![0]!["guessedString"]!.GetValue<string>());
        }
    }

    private void AssertReceiptCount(string proposalId, string intentDigest, long expectedCount)
    {
        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Receipts WHERE ProposalId = $id AND IntentDigest = $digest;";
        command.Parameters.AddWithValue("$id", proposalId);
        command.Parameters.AddWithValue("$digest", intentDigest);
        Assert.Equal(expectedCount, (long)command.ExecuteScalar()!);
    }

    private string RunPreflightCli(string proposalId, bool json)
    {
        var start = new ProcessStartInfo(BuildOutput.Cli)
        {
            Arguments = $"preflight --project \"{_fwDataPath}\" {proposalId}" + (json ? " --json" : ""),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.Environment["MOTIF_DEVELOPER_COMMANDS"] = "1";
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(60000));
        Assert.Equal(0, process.ExitCode);
        _ = error.GetAwaiter().GetResult();
        return output.GetAwaiter().GetResult();
    }

}
