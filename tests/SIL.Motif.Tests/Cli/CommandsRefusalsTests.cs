using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>
/// Pins <see cref="ProposalCommands"/>'s fail-closed refusals across every verb: name collisions, missing
/// drafts and Proposals, malformed ids, store-consistency guards, and the "another program has this
/// project open" and "no bound DryRun" hard stops on the apply path. Each refusal test asserts the stable
/// code, the closed reason, the specific message a reader must act on, its branchable facts, and that the
/// store or draft was left byte-for-byte unchanged, not merely that the outcome was a refusal.
/// </summary>
[Collection(TestFixtures.LcmCacheTestCollection.Name)]
public sealed class CommandsRefusalsTests
{
    private const string ProductVersion = "1.0";

    private readonly string _fwDataPath;
    private readonly string _target;

    public CommandsRefusalsTests(PristineProjectFixture pristine)
    {
        var seed = pristine.Seed;
        using var scratch = pristine.NewScratch();
        _fwDataPath = scratch.ProjectId.Path;
        _target = CanonicalId.FromGuid(seed.FirstSenseId).Value;
    }

    // --- New ---

    [Fact]
    public void New_DraftNameAlreadyExists_RefusesAndLeavesTheOriginalDraftUntouched()
    {
        Assert.True(New("dup", "original label").Succeeded);
        var before = ReadDraftJson("dup");

        var result = New("dup", "second label");

        Assert.False(result.Succeeded);
        var refusal = result.Refusal!;
        Assert.Equal("draft.name-collision", refusal.Code);
        Assert.Equal(FailureReason.Refused, refusal.Reason);
        Assert.Contains("already exists", refusal.Message);
        // Discarding the existing "dup" draft would not free the name for this call either.
        Assert.Contains("Finalize it, or use another name", refusal.Message);
        Assert.DoesNotContain("delete it", refusal.Message, StringComparison.Ordinal);
        Assert.Equal("dup", refusal.Facts["draftName"]);
        Assert.Equal(before, ReadDraftJson("dup"));
    }

    // --- AddSetGloss ---

    [Fact]
    public void AddSetGloss_EmptyWritingSystem_RefusesAndAddsNoOperation()
    {
        New("d", null);
        var before = ReadDraftJson("d");

        var result = AddSetGloss("d", _target, "", "some text");

        Assert.False(result.Succeeded);
        var refusal = result.Refusal!;
        Assert.Equal("operation.invalid-writing-system", refusal.Code);
        Assert.Equal(FailureReason.InvalidArgument, refusal.Reason);
        Assert.Contains("--ws must not be empty.", refusal.Message);
        Assert.Equal(before, ReadDraftJson("d"));
    }

    [Fact]
    public void AddSetGloss_InvalidDependsOnIdFormat_RefusesAndAddsNoOperation()
    {
        New("d", null);
        var before = ReadDraftJson("d");

        var result = AddSetGloss("d", _target, "en", "text", dependsOn: new[] { "not-a-canonical-id" });

        Assert.False(result.Succeeded);
        var refusal = result.Refusal!;
        Assert.Equal("operation.invalid-dependency", refusal.Code);
        Assert.Equal(FailureReason.Refused, refusal.Reason);
        Assert.Contains("--depends-on 'not-a-canonical-id' is not a valid canonical operation id", refusal.Message);
        Assert.Equal(before, ReadDraftJson("d"));
    }

    // --- AddDeleteLexemeForm ---

    [Fact]
    public void AddDeleteLexemeForm_DraftNotFound_Refuses()
    {
        var result = AddDeleteLexemeForm("no-such-draft", _target);

        Assert.False(result.Succeeded);
        var refusal = result.Refusal!;
        Assert.Equal("draft.not-found", refusal.Code);
        Assert.Equal(FailureReason.NotFound, refusal.Reason);
        Assert.Contains("not found in store", refusal.Message);
        Assert.Contains("Run 'new --draft no-such-draft' first.", refusal.Message);
        Assert.Equal("no-such-draft", refusal.Facts["draftName"]);
    }

    [Fact]
    public void AddDeleteLexemeForm_InvalidTargetId_RefusesAndAddsNoOperation()
    {
        New("d", null);
        var before = ReadDraftJson("d");

        var result = AddDeleteLexemeForm("d", "not-a-canonical-id");

        Assert.False(result.Succeeded);
        var refusal = result.Refusal!;
        Assert.Equal("operation.invalid-target", refusal.Code);
        Assert.Equal(FailureReason.InvalidArgument, refusal.Reason);
        Assert.Contains("--target 'not-a-canonical-id' is not a valid canonical id", refusal.Message);
        Assert.Equal("not-a-canonical-id", refusal.Facts["target"]);
        Assert.Equal(before, ReadDraftJson("d"));
    }

    // --- Label / Comment ---

    [Fact]
    public void Label_SetsTheFieldOnTheDraftAndPersistsIt()
    {
        New("d", null);

        var result = ProposalCommands.Label(new LabelRequest(_fwDataPath, ProductVersion, "d", "a linguist-facing label"));

        Assert.True(result.Succeeded);
        Assert.Equal("label", result.Value!.Field);
        Assert.Equal("d", result.Value.DraftName);
        Assert.Equal("a linguist-facing label", ReadDraft("d").Label);
    }

    [Fact]
    public void Comment_SetsTheFieldOnTheDraftAndPersistsIt()
    {
        New("d", null);

        var result =
            ProposalCommands.Comment(new CommentRequest(_fwDataPath, ProductVersion, "d", "a reviewer-facing comment"));

        Assert.True(result.Succeeded);
        Assert.Equal("comment", result.Value!.Field);
        Assert.Equal("a reviewer-facing comment", ReadDraft("d").Comment);
    }

    [Fact]
    public void Label_DraftNotFound_Refuses()
    {
        var result = ProposalCommands.Label(new LabelRequest(_fwDataPath, ProductVersion, "no-such-draft", "x"));

        Assert.False(result.Succeeded);
        var refusal = result.Refusal!;
        Assert.Equal("draft.not-found", refusal.Code);
        Assert.Equal(FailureReason.NotFound, refusal.Reason);
        Assert.Contains("not found in store", refusal.Message);
    }

    // --- Finalize ---

    [Fact]
    public void Finalize_DraftNotFound_Refuses()
    {
        var result = Finalize("no-such-draft");

        Assert.False(result.Succeeded);
        var refusal = result.Refusal!;
        Assert.Equal("draft.not-found", refusal.Code);
        Assert.Equal(FailureReason.NotFound, refusal.Reason);
        Assert.Contains("not found in store", refusal.Message);
    }

    [Theory]
    [InlineData(null, "Explains why the change is needed.")]
    [InlineData("   ", "Explains why the change is needed.")]
    [InlineData("Clarify the analysis", null)]
    [InlineData("Clarify the analysis", "\t")]
    public void Finalize_MissingRationale_RefusesAtomicallyAndPreservesTheDraft(
        string? label, string? comment)
    {
        const string draftName = "missing-rationale";
        New(draftName, null);
        AddSetGloss(draftName, _target, "en", "clarified gloss");
        var draft = ReadDraft(draftName);
        draft.Label = label;
        draft.Comment = comment;
        WriteDraft(draftName, draft);
        var before = ReadDraftJson(draftName);

        var result = Finalize(draftName);

        Assert.False(result.Succeeded);
        var refusal = result.Refusal!;
        Assert.Equal("draft.invalid", refusal.Code);
        Assert.Equal(FailureReason.Refused, refusal.Reason);
        Assert.Contains("short description", refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("label", refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("extended explanation", refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("comment", refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"label --draft {draftName} <text>", refusal.Message, StringComparison.Ordinal);
        Assert.Contains($"comment --draft {draftName} <text>", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(before, ReadDraftJson(draftName));
        Assert.Equal(0, CountCommittedProposals());
    }

    [Fact]
    public void Finalize_ArgvDiagnosticNamesCommandsThatRemedyTheRefusal()
    {
        const string draftName = "argv-rationale";
        Assert.True(New(draftName, null).Succeeded);
        Assert.True(AddSetGloss(draftName, _target, "en", "clarified gloss").Succeeded);

        var refused = RunCli($"finalize --draft {draftName}");

        Assert.NotEqual(0, refused.ExitCode);
        Assert.Contains($"label --draft {draftName} <text>", refused.Error);
        Assert.Contains($"comment --draft {draftName} <text>", refused.Error);
        Assert.Equal(0, RunCli($"label --draft {draftName} \"Clarify the analysis\"").ExitCode);
        Assert.Equal(
            0,
            RunCli($"comment --draft {draftName} \"Explain why this analysis is intended\"").ExitCode);
        Assert.Equal(0, RunCli($"finalize --draft {draftName}").ExitCode);
    }

    [Fact]
    public void Finalize_AmendWithMissingRationale_RefusesWithoutMovingCommittedState()
    {
        var proposalId = CommitOneOperationProposal("original-rationale");
        var recordBefore = GetRecord(proposalId);
        var revisionCountBefore = CountRevisions(proposalId);
        Assert.True(Reopen("amend-rationale", proposalId).Succeeded);
        var draft = ReadDraft("amend-rationale");
        draft.Comment = "  ";
        WriteDraft("amend-rationale", draft);
        var draftBefore = ReadDraftJson("amend-rationale");

        var result = Finalize("amend-rationale");

        Assert.False(result.Succeeded);
        Assert.Equal("draft.invalid", result.Refusal!.Code);
        Assert.Equal(draftBefore, ReadDraftJson("amend-rationale"));
        var recordAfter = GetRecord(proposalId);
        Assert.Equal(recordBefore.Status, recordAfter.Status);
        Assert.Equal(recordBefore.IntentDigest, recordAfter.IntentDigest);
        Assert.Equal(revisionCountBefore, CountRevisions(proposalId));
    }

    [Fact]
    public void Finalize_DraftFailsProposalValidation_RefusesAndCommitsNothing()
    {
        New("d", null);
        AddSetGloss("d", _target, "en", "text");

        // Corrupt contractVersions so it no longer covers the 'lexical' group the one operation uses.
        var draft = ReadDraft("d");
        draft.Label = "Exercise Proposal validation";
        draft.Comment = "Keep contract-version validation independent from the required rationale guard.";
        draft.ContractVersions.Remove("lexical");
        draft.ContractVersions["bogus"] = "1.0";
        WriteDraft("d", draft);

        var result = Finalize("d");

        Assert.False(result.Succeeded);
        var refusal = result.Refusal!;
        Assert.Equal("proposal.inconsistent", refusal.Code);
        Assert.Equal(FailureReason.Refused, refusal.Reason);
        Assert.Contains("failed Proposal validation", refusal.Message);
        Assert.True(DraftExists("d")); // never consumed: finalize did not commit
        Assert.Equal(0, CountCommittedProposals());
    }

    // --- Reopen ---

    [Fact]
    public void Reopen_DraftNameAlreadyExists_Refuses()
    {
        var proposalId = CommitOneOperationProposal("src");
        New("taken", null);
        var before = ReadDraftJson("taken");

        var result = Reopen("taken", proposalId);

        Assert.False(result.Succeeded);
        var refusal = result.Refusal!;
        Assert.Equal("draft.name-collision", refusal.Code);
        Assert.Contains("already exists", refusal.Message);
        Assert.Contains("before reopening a Proposal with this draft name.", refusal.Message);
        Assert.Equal(before, ReadDraftJson("taken"));
    }

    [Fact]
    public void Reopen_StoreInconsistency_Refuses()
    {
        var proposalId = CommitOneOperationProposal("src");
        var recordBefore = GetStatusRow(proposalId);
        DeleteCommittedRevision(proposalId);

        var result = Reopen("reopened", proposalId);

        Assert.False(result.Succeeded);
        var refusal = result.Refusal!;
        Assert.Equal("proposal.inconsistent", refusal.Code);
        Assert.Equal(FailureReason.StoreInconsistent, refusal.Reason);
        Assert.Contains("store inconsistency", refusal.Message);
        Assert.False(DraftExists("reopened"));
        var recordAfter = GetStatusRow(proposalId);
        Assert.Equal(recordBefore.Status, recordAfter.Status);
        Assert.Equal(recordBefore.IntentDigest, recordAfter.IntentDigest);
    }

    [Fact]
    public void Reopen_InvalidProposalId_IsAnInvalidArgument()
    {
        var result = Reopen("reopened", "not-a-canonical-id");

        Assert.False(result.Succeeded);
        Assert.Equal("proposal.invalid-id", result.Refusal!.Code);
        Assert.Equal(FailureReason.InvalidArgument, result.Refusal.Reason);
    }

    // --- Duplicate ---

    [Fact]
    public void Duplicate_DraftNameAlreadyExists_Refuses()
    {
        var proposalId = CommitOneOperationProposal("src");
        New("taken", null);
        var before = ReadDraftJson("taken");

        var result = ProposalCommands.Duplicate(
            new DuplicateRequest(_fwDataPath, ProductVersion, proposalId, "taken"));

        Assert.False(result.Succeeded);
        var refusal = result.Refusal!;
        Assert.Equal("draft.name-collision", refusal.Code);
        Assert.Contains("already exists", refusal.Message);
        Assert.Contains("before duplicating a Proposal into a draft with this name.", refusal.Message);
        Assert.Equal(before, ReadDraftJson("taken"));
    }

    [Fact]
    public void Duplicate_StoreInconsistency_Refuses()
    {
        var proposalId = CommitOneOperationProposal("src");
        DeleteCommittedRevision(proposalId);

        var result =
            ProposalCommands.Duplicate(new DuplicateRequest(_fwDataPath, ProductVersion, proposalId, "copy"));

        Assert.False(result.Succeeded);
        var refusal = result.Refusal!;
        Assert.Equal("proposal.inconsistent", refusal.Code);
        Assert.Equal(FailureReason.StoreInconsistent, refusal.Reason);
        Assert.Contains("store inconsistency", refusal.Message);
        Assert.False(DraftExists("copy"));
    }

    [Fact]
    public void Duplicate_InvalidProposalId_IsAnInvalidArgument()
    {
        var result = ProposalCommands.Duplicate(
            new DuplicateRequest(_fwDataPath, ProductVersion, "not-a-canonical-id", "copy"));

        Assert.False(result.Succeeded);
        Assert.Equal("proposal.invalid-id", result.Refusal!.Code);
        Assert.Equal(FailureReason.InvalidArgument, result.Refusal.Reason);
    }

    // --- RemoveOperations ---

    [Fact]
    public void RemoveOperations_DraftNotFound_Refuses()
    {
        var result = ProposalCommands.RemoveOperations(new RemoveOperationsRequest(
            _fwDataPath, ProductVersion, "no-such-draft", new[] { CanonicalId.Mint().Value }, Force: false));

        Assert.False(result.Succeeded);
        var refusal = result.Refusal!;
        Assert.Equal("draft.not-found", refusal.Code);
        Assert.Contains("not found in store", refusal.Message);
    }

    [Fact]
    public void RemoveOperations_NoOperationIdsSpecified_Refuses()
    {
        New("d", null);

        var result = ProposalCommands.RemoveOperations(
            new RemoveOperationsRequest(_fwDataPath, ProductVersion, "d", Array.Empty<string>(), Force: false));

        Assert.False(result.Succeeded);
        var refusal = result.Refusal!;
        Assert.Equal("operation.invalid-id", refusal.Code);
        Assert.Equal(FailureReason.InvalidArgument, refusal.Reason);
        Assert.Contains("Specify at least one operation id to remove.", refusal.Message);
    }

    [Fact]
    public void RemoveOperations_InvalidOperationIdFormat_Refuses()
    {
        New("d", null);

        var result = ProposalCommands.RemoveOperations(new RemoveOperationsRequest(
            _fwDataPath, ProductVersion, "d", new[] { "not-a-canonical-id" }, Force: false));

        Assert.False(result.Succeeded);
        var refusal = result.Refusal!;
        Assert.Equal("operation.invalid-id", refusal.Code);
        Assert.Contains("'not-a-canonical-id' is not a valid canonical operation id", refusal.Message);
    }

    // --- Split ---

    [Fact]
    public void Split_NoGroups_Refuses()
    {
        var result = ProposalCommands.Split(new SplitRequest(
            _fwDataPath, ProductVersion, CanonicalId.Mint().Value, Array.Empty<SplitGroup>(), Force: false));

        Assert.False(result.Succeeded);
        var refusal = result.Refusal!;
        Assert.Equal("draft.invalid", refusal.Code);
        Assert.Equal(FailureReason.InvalidArgument, refusal.Reason);
        Assert.Contains("Specify at least one group to split into.", refusal.Message);
    }

    [Fact]
    public void Split_ProposalNotFound_Refuses()
    {
        var bogusId = CanonicalId.Mint().Value;

        var result = ProposalCommands.Split(new SplitRequest(
            _fwDataPath, ProductVersion, bogusId,
            new[] { new SplitGroup("g1", new[] { CanonicalId.Mint().Value }) }, Force: false));

        Assert.False(result.Succeeded);
        var refusal = result.Refusal!;
        Assert.Equal("proposal.not-found", refusal.Code);
        Assert.Equal(FailureReason.NotFound, refusal.Reason);
        Assert.Contains("not found in store", refusal.Message);
        Assert.Contains("Run 'list' to see committed proposals.", refusal.Message);
    }

    [Fact]
    public void Split_StoreInconsistency_Refuses()
    {
        var proposalId = CommitOneOperationProposal("src");
        DeleteCommittedRevision(proposalId);

        var result = ProposalCommands.Split(new SplitRequest(
            _fwDataPath, ProductVersion, proposalId,
            new[] { new SplitGroup("g1", new[] { CanonicalId.Mint().Value }) }, Force: false));

        Assert.False(result.Succeeded);
        var refusal = result.Refusal!;
        Assert.Equal("proposal.inconsistent", refusal.Code);
        Assert.Contains("store inconsistency", refusal.Message);
    }

    [Fact]
    public void Split_DraftNameAlreadyExists_Refuses()
    {
        var proposalId = CommitOneOperationProposal("src");
        New("taken", null);
        var before = ReadDraftJson("taken");

        var result = ProposalCommands.Split(new SplitRequest(
            _fwDataPath, ProductVersion, proposalId,
            new[] { new SplitGroup("taken", new[] { CanonicalId.Mint().Value }) }, Force: false));

        Assert.False(result.Succeeded);
        var refusal = result.Refusal!;
        Assert.Equal("draft.name-collision", refusal.Code);
        Assert.Contains("already exists", refusal.Message);
        Assert.Contains("before splitting into a draft with this name.", refusal.Message);
        Assert.Equal(before, ReadDraftJson("taken"));
    }

    [Fact]
    public void Split_GroupsShareADraftName_Refuses()
    {
        var proposalId = CommitOneOperationProposal("src");

        var result = ProposalCommands.Split(new SplitRequest(
            _fwDataPath, ProductVersion, proposalId,
            new[]
            {
                new SplitGroup("same", new[] { CanonicalId.Mint().Value }),
                new SplitGroup("same", Array.Empty<string>()),
            },
            Force: false));

        Assert.False(result.Succeeded);
        var refusal = result.Refusal!;
        Assert.Equal("draft.invalid", refusal.Code);
        Assert.Contains("Each split group must target a distinct draft name.", refusal.Message);
    }

    [Fact]
    public void Split_MalformedOperationIdInGroup_Refuses()
    {
        var proposalId = CommitOneOperationProposal("src");

        var result = ProposalCommands.Split(new SplitRequest(
            _fwDataPath, ProductVersion, proposalId,
            new[] { new SplitGroup("g1", new[] { "not-a-canonical-id" }) }, Force: false));

        Assert.False(result.Succeeded);
        var refusal = result.Refusal!;
        Assert.Equal("operation.invalid-id", refusal.Code);
        Assert.Equal(FailureReason.InvalidArgument, refusal.Reason);
        Assert.Contains("'not-a-canonical-id' is not a valid canonical operation id", refusal.Message);
    }

    [Fact]
    public void Split_UnknownOperationIdInGroup_Refuses()
    {
        var proposalId = CommitOneOperationProposal("src");
        var strangerId = CanonicalId.Mint().Value;

        var result = ProposalCommands.Split(new SplitRequest(
            _fwDataPath, ProductVersion, proposalId,
            new[] { new SplitGroup("g1", new[] { strangerId }) }, Force: false));

        Assert.False(result.Succeeded);
        var refusal = result.Refusal!;
        Assert.Equal("operation.invalid-id", refusal.Code);
        Assert.Equal(FailureReason.Refused, refusal.Reason);
        Assert.Contains($"has no operation(s) '{strangerId}'", refusal.Message);
    }

    // --- List ---

    [Fact]
    public void List_EmptyStore_ReturnsAnEmptyListRatherThanAnError()
    {
        var result = ProposalCommands.List(new ListProposalsRequest(_fwDataPath, ProductVersion));

        Assert.True(result.Succeeded);
        Assert.Empty(result.Value!.Proposals);
    }

    // --- Show ---

    [Fact]
    public void Show_StoreInconsistency_Refuses()
    {
        var proposalId = CommitOneOperationProposal("src");
        DeleteCommittedRevision(proposalId);

        var result =
            ProposalCommands.Show(new ShowProposalRequest(_fwDataPath, ProductVersion, proposalId));

        Assert.False(result.Succeeded);
        var refusal = result.Refusal!;
        Assert.Equal("proposal.inconsistent", refusal.Code);
        Assert.Equal(FailureReason.StoreInconsistent, refusal.Reason);
        Assert.Contains("store inconsistency", refusal.Message);
    }

    // --- DryRun / Apply ---

    [Fact]
    public void DryRun_ProposalNotFound_Refuses()
    {
        var result = LegacyJobCommands.EnqueueDryRun(_fwDataPath, ProductVersion, CanonicalId.Mint().Value);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("not found in store", result.Output);
    }

    [Fact]
    public void DryRun_StoreInconsistency_Refuses()
    {
        var proposalId = CommitOneOperationProposal("src");
        DeleteCommittedRevision(proposalId);

        var writeTimeBefore = File.GetLastWriteTimeUtc(_fwDataPath);
        var result = LegacyJobCommands.EnqueueDryRun(_fwDataPath, ProductVersion, proposalId);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("store inconsistency", result.Output);
        // The refusal is a pure database read: the live project was never opened to reach it.
        Assert.Equal(writeTimeBefore, File.GetLastWriteTimeUtc(_fwDataPath));
    }

    [Fact]
    public void Apply_ProposalNotFound_Refuses()
    {
        var result = Apply(CanonicalId.Mint().Value, "tester");

        Assert.False(result.Succeeded);
        Assert.Contains("not found in store", result.Refusal!.Message);
    }

    [Fact]
    public void Apply_StoreInconsistency_Refuses()
    {
        var proposalId = CommitOneOperationProposal("src");
        DeleteCommittedRevision(proposalId);

        var result = Apply(proposalId, "tester");

        Assert.False(result.Succeeded);
        Assert.Contains("store inconsistency", result.Refusal!.Message);
    }

    [Fact]
    public void Apply_NoBoundDryRun_RefusesAndNamesTheFix()
    {
        var proposalId = CommitOneOperationProposal("src");

        var result = Apply(proposalId, "tester");

        Assert.False(result.Succeeded);
        var refusal = result.Refusal!;
        Assert.Equal("apply.dry-run-missing", refusal.Code);
        Assert.Equal(FailureReason.Refused, refusal.Reason);
        Assert.Contains("has no bound DryRun recorded", refusal.Message);
        Assert.Contains($"dry-run {proposalId} --project <fwdata>", refusal.Message);
        Assert.Equal(proposalId, refusal.Facts["proposalId"]);
    }

    [Fact]
    public void Apply_InvalidProposalId_IsAnInvalidArgument()
    {
        var result = Apply("not-a-canonical-id", "tester");

        Assert.False(result.Succeeded);
        var refusal = result.Refusal!;
        Assert.Equal("proposal.invalid-id", refusal.Code);
        Assert.Equal(FailureReason.InvalidArgument, refusal.Reason);
        Assert.Contains("is not a valid canonical Proposal id", refusal.Message);
    }

    // --- DryRun (store-consistency checks GetFinalized runs before the project is even opened) ---

    [Fact]
    public void DryRun_EnvelopeIdentityDoesNotMatchLookup_Refuses()
    {
        var proposalId = CommitOneOperationProposal("envelope-identity-mismatch");
        var wrongId = CanonicalId.Mint().Value;
        CorruptCommittedRevisionJson(proposalId, envelope => envelope["proposalId"] = wrongId);

        var writeTimeBefore = File.GetLastWriteTimeUtc(_fwDataPath);
        var result = LegacyJobCommands.EnqueueDryRun(_fwDataPath, ProductVersion, proposalId);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(proposalId, result.Output, StringComparison.Ordinal);
        Assert.Contains(wrongId, result.Output, StringComparison.Ordinal);
        Assert.Equal(writeTimeBefore, File.GetLastWriteTimeUtc(_fwDataPath));
    }

    [Fact]
    public void DryRun_ObjectContentDoesNotMatchManifestDigest_Refuses()
    {
        var proposalId = CommitOneOperationProposal("object-digest-mismatch");
        CorruptCommittedRevisionJson(
            proposalId, envelope => envelope["operations"]![0]!["after"]!["text"] = "content changed behind the digest");

        var writeTimeBefore = File.GetLastWriteTimeUtc(_fwDataPath);
        var result = LegacyJobCommands.EnqueueDryRun(_fwDataPath, ProductVersion, proposalId);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("intentDigest", result.Output, StringComparison.Ordinal);
        Assert.Contains("store inconsistency", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(writeTimeBefore, File.GetLastWriteTimeUtc(_fwDataPath));
    }

    // --- Typed-API call helpers (kept thin so the tests above read like the CLI invocations they pin) ---

    private CommandOutcome<DraftCreatedResponse> New(string draftName, string? label) =>
        ProposalCommands.New(new NewDraftRequest(_fwDataPath, ProductVersion, draftName, label));

    private CommandOutcome<SetGlossAddedResponse> AddSetGloss(
        string draftName, string target, string ws, string text, string[]? dependsOn = null) =>
        ProposalCommands.AddSetGloss(
            new AddSetGlossRequest(_fwDataPath, ProductVersion, draftName, target, ws, text, dependsOn));

    private CommandOutcome<DeleteLexemeFormAddedResponse> AddDeleteLexemeForm(
        string draftName, string target) =>
        ProposalCommands.AddDeleteLexemeForm(
            new AddDeleteLexemeFormRequest(_fwDataPath, ProductVersion, draftName, target));

    private CommandOutcome<ProposalFinalizedResponse> Finalize(string draftName) =>
        ProposalCommands.Finalize(new FinalizeRequest(_fwDataPath, ProductVersion, draftName));

    private CommandOutcome<ReopenedResponse> Reopen(string draftName, string proposalId) =>
        ProposalCommands.Reopen(new ReopenRequest(_fwDataPath, ProductVersion, draftName, proposalId));

    private CommandOutcome<ApplyProjection> Apply(string proposalId, string user) =>
        ProposalCommands.Apply(new ApplyRequest(_fwDataPath, ProductVersion, proposalId, user));

    // --- Helpers ---

    private string CommitOneOperationProposal(string draftName)
    {
        New(draftName, null);
        AddSetGloss(draftName, _target, "en", "text for " + draftName);
        DraftRationale.Author(
            _fwDataPath, draftName, "Clarify a lexical gloss", "Record the intended lexical analysis for review.");
        var finalizeResult = Finalize(draftName);
        Assert.True(finalizeResult.Succeeded);
        return finalizeResult.Value!.ProposalId;
    }

    private (int ExitCode, string Output, string Error) RunCli(string arguments)
    {
        var executable = BuildOutput.Cli;
        var start = new ProcessStartInfo(executable)
        {
            Arguments = $"{arguments} --project \"{_fwDataPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(start)!;
        // Both pipes drain concurrently: a sequential read deadlocks past the pipe buffer.
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        var output = outputTask.GetAwaiter().GetResult();
        var error = errorTask.GetAwaiter().GetResult();
        process.WaitForExit();
        return (process.ExitCode, output, error);
    }

    /// <summary>Deletes a committed revision while its own pointer survives.</summary>
    private void DeleteCommittedRevision(string proposalId)
    {
        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        var repository = new ProposalRepository(database);
        var digest = repository.Get(CanonicalId.Parse(proposalId)).IntentDigest!;
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ProposalRevisions WHERE ProposalId = $id AND IntentDigest = $digest;";
        command.Parameters.AddWithValue("$id", proposalId);
        command.Parameters.AddWithValue("$digest", digest);
        command.ExecuteNonQuery();
    }

    /// <summary>Rewrites a committed revision's bytes, leaving the digest that names it untouched.</summary>
    private void CorruptCommittedRevisionJson(string proposalId, Action<JsonObject> mutate)
    {
        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        var repository = new ProposalRepository(database);
        var record = repository.Get(CanonicalId.Parse(proposalId));
        var digest = record.IntentDigest!;
        var envelope = JsonNode.Parse(record.ProposalJson!)!.AsObject();
        mutate(envelope);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE ProposalRevisions SET ProposalJson = $json WHERE ProposalId = $id AND IntentDigest = $digest;";
        command.Parameters.AddWithValue("$json", System.Text.Encoding.UTF8.GetBytes(envelope.ToJsonString()));
        command.Parameters.AddWithValue("$id", proposalId);
        command.Parameters.AddWithValue("$digest", digest);
        command.ExecuteNonQuery();
    }

    private ProposalRecord GetRecord(string proposalId)
    {
        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        return new ProposalRepository(database).Get(CanonicalId.Parse(proposalId));
    }

    /// <summary>Reads status and pointer without requiring the pointed-at revision to exist.</summary>
    private ProposalRecord GetStatusRow(string proposalId)
    {
        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        return new ProposalRepository(database).GetForTransition(CanonicalId.Parse(proposalId));
    }

    private bool DraftExists(string draftName)
    {
        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        return new ProposalRepository(database).DraftNameExists(draftName);
    }

    private string ReadDraftJson(string draftName)
    {
        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        return new ProposalRepository(database).GetDraft(draftName).ProposalJson!;
    }

    private DraftDocument ReadDraft(string draftName) =>
        JsonSerializer.Deserialize<DraftDocument>(
            ReadDraftJson(draftName), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    private void WriteDraft(string draftName, DraftDocument draft)
    {
        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        new ProposalRepository(database).SaveDraft(draftName, JsonSerializer.Serialize(draft));
    }

    /// <summary>Counts Proposals that have a committed revision (excludes still-open drafts).</summary>
    private int CountCommittedProposals()
    {
        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        var repository = new ProposalRepository(database);
        return repository.List(new ProposalListFilter(IncludeArchived: true)).Count(p => p.DraftName is null);
    }

    private int CountRevisions(string proposalId)
    {
        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM ProposalRevisions WHERE ProposalId = $id;";
        command.Parameters.AddWithValue("$id", proposalId);
        return Convert.ToInt32(command.ExecuteScalar());
    }
}
