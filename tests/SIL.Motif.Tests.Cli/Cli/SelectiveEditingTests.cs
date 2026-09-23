using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Model.DryRun;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>
/// Selective Proposal editing (duplicate, remove, split) — the removal rule decided by
/// ADR 0021, decision 6:
/// <list type="number">
/// <item>a removal with no dependents just happens;</item>
/// <item>a removal that orphans a dependent warns and names every consequence, then requires
/// <c>--force</c>;</item>
/// <item>force never means "guess" — if the consequences cannot be enumerated, the removal is
/// refused outright, not forced;</item>
/// <item><c>proposalId</c> stays frozen and the intent digest moves; removal produces a new
/// revision, never a mutation of an approved one.</item>
/// </list>
/// None of this touches a live LibLCM project — the dependency graph is entirely declared in the
/// Proposal document (<c>dependsOn</c>, <c>entityId</c>/<c>target</c>), so these tests use synthetic
/// canonical ids rather than a live project via
/// <see cref="SIL.Motif.Tests.TestFixtures.PristineProjectFixture"/>, unlike <see cref="ReopenAmendTests"/>.
/// </summary>
public sealed class SelectiveEditingTests : IDisposable
{
    private const string ProductVersion = "1.0";

    private readonly string _fwDataPath = PlaceholderProject.Create("SIL.Motif.Tests.SelectiveEditing");

    public void Dispose()
    {
        try { Directory.Delete(Path.GetDirectoryName(_fwDataPath)!, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    private static CommandOutcome<DraftCreatedResponse> NewDraft(
        string fwDataPath, string productVersion, string draftName, string? label) =>
        ProposalCommands.New(new NewDraftRequest(fwDataPath, productVersion, draftName, label));

    private static CommandOutcome<SetGlossAddedResponse> AddSetGloss(
        string fwDataPath, string productVersion, string draftName, string target, string ws, string text,
        IReadOnlyList<string>? dependsOn = null) =>
        ProposalCommands.AddSetGloss(new AddSetGlossRequest(
            fwDataPath, productVersion, draftName, target, ws, text, dependsOn));

    private static CommandOutcome<DeleteLexemeFormAddedResponse> AddDeleteLexemeForm(
        string fwDataPath, string productVersion, string draftName, string target) =>
        ProposalCommands.AddDeleteLexemeForm(new AddDeleteLexemeFormRequest(
            fwDataPath, productVersion, draftName, target));

    private static CommandOutcome<ProposalFinalizedResponse> FinalizeDraft(
        string fwDataPath, string productVersion, string draftName) =>
        ProposalCommands.Finalize(new FinalizeRequest(fwDataPath, productVersion, draftName));

    private static CommandOutcome<ReopenedResponse> ReopenDraft(
        string fwDataPath, string productVersion, string draftName, string proposalId) =>
        ProposalCommands.Reopen(new ReopenRequest(fwDataPath, productVersion, draftName, proposalId));

    private static CommandOutcome<OperationsRemovedResponse> RemoveOperations(
        string fwDataPath, string productVersion, string draftName, IReadOnlyList<string> operationIds,
        bool force) =>
        ProposalCommands.RemoveOperations(new RemoveOperationsRequest(
            fwDataPath, productVersion, draftName, operationIds, force));

    private static CommandOutcome<DuplicatedResponse> DuplicateProposal(
        string fwDataPath, string productVersion, string sourceProposalId, string newDraftName) =>
        ProposalCommands.Duplicate(new DuplicateRequest(
            fwDataPath, productVersion, sourceProposalId, newDraftName));

    private static CommandOutcome<ProposalSplitResponse> SplitProposal(
        string fwDataPath, string productVersion, string sourceProposalId, IReadOnlyList<SplitGroup> groups,
        bool force) =>
        ProposalCommands.Split(new SplitRequest(fwDataPath, productVersion, sourceProposalId, groups, force));

    // ---- helpers -----------------------------------------------------------------------------

    private static string NewTarget() => CanonicalId.Mint().Value;

    private string CommitProposal(string draftName, params string[] setGlossTargets)
    {
        Assert.True(NewDraft(_fwDataPath, ProductVersion, draftName, "label for " + draftName).Succeeded);
        foreach (var target in setGlossTargets)
        {
            Assert.True(AddSetGloss(_fwDataPath, ProductVersion, draftName, target, "en", "gloss for " + target).Succeeded);
        }
        DraftRationale.Author(
            _fwDataPath, draftName, "Edit selected lexical entries", "Apply the authored gloss changes to the selected targets.");
        var finalize = FinalizeDraft(_fwDataPath, ProductVersion, draftName);
        Assert.True(finalize.Succeeded);
        return finalize.Value!.ProposalId;
    }

    /// <summary>Stands in for a real dry-run anchor, without a FieldWorks project load per test.</summary>
    private void BindSyntheticAnchor(string proposalId)
    {
        var anchor = new BoundDryRunAnchor(
            IntentDigest: "sha256:" + new string('c', 64),
            FootprintDigest: "sha256:" + new string('a', 64),
            EffectDigest: "sha256:" + new string('b', 64),
            RunnerVersion: "1.0.0.0",
            LibLcmVersion: "1.0.0.0",
            ProjectionVersion: "1",
            DryRunAtUtc: "20260101T000000Z");
        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        new ProposalRepository(database).SetAnchor(CanonicalId.Parse(proposalId), JsonSerializer.Serialize(anchor));
    }

    private BoundDryRunAnchor? ReadAnchor(string proposalId)
    {
        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        var anchorJson = new ProposalRepository(database).Get(CanonicalId.Parse(proposalId)).AnchorJson;
        return anchorJson is null ? null : JsonSerializer.Deserialize<BoundDryRunAnchor>(anchorJson);
    }

    private DraftDocument ReadDraft(string draftName)
    {
        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        var json = new ProposalRepository(database).GetDraft(draftName).ProposalJson!;
        return JsonSerializer.Deserialize<DraftDocument>(
            json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    // ---- remove: no dependents -> just happens, and clears the anchor on amend -----------------

    [Fact]
    public void RemoveOperations_NoDependents_JustHappens_AndClearsAnchorOnAmend()
    {
        var t1 = NewTarget();
        var t2 = NewTarget();
        Assert.True(NewDraft(_fwDataPath, ProductVersion, "v1", "two independent ops").Succeeded);
        Assert.True(AddSetGloss(_fwDataPath, ProductVersion, "v1", t1, "en", "gloss1").Succeeded);
        Assert.True(AddSetGloss(_fwDataPath, ProductVersion, "v1", t2, "en", "gloss2").Succeeded);
        DraftRationale.Author(
            _fwDataPath, "v1", "Update two independent glosses", "Correct both lexical analyses in one proposal.");
        var finalize = FinalizeDraft(_fwDataPath, ProductVersion, "v1");
        Assert.True(finalize.Succeeded);
        var proposalId = finalize.Value!.ProposalId;

        // Reopen to get at the real operation ids as recorded in the committed object.
        Assert.True(ReopenDraft(_fwDataPath, ProductVersion, "v2", proposalId).Succeeded);
        var reopened = ReadDraft("v2");
        Assert.Equal(2, reopened.Operations.Count);
        var toRemove = reopened.Operations.First(o => o.Target == t2).OperationId;

        BindSyntheticAnchor(proposalId);
        Assert.NotNull(ReadAnchor(proposalId));

        var removeResult = RemoveOperations(_fwDataPath, ProductVersion, "v2", new[] { toRemove }, force: false);
        Assert.True(removeResult.Succeeded);
        Assert.Empty(removeResult.Value!.ForcedDependents);

        var afterRemove = ReadDraft("v2");
        Assert.Single(afterRemove.Operations);
        Assert.Equal(t1, afterRemove.Operations[0].Target);

        var amend = FinalizeDraft(_fwDataPath, ProductVersion, "v2");
        Assert.True(amend.Succeeded);
        Assert.True(amend.Value!.IsAmend);
        Assert.Equal(proposalId, amend.Value!.ProposalId); // id frozen

        Assert.Null(ReadAnchor(proposalId)); // stale anchor cleared by the edit
    }

    // ---- remove: orphans a dependent -> warns, enumerates, blocked without --force -------------

    [Fact]
    public void RemoveOperations_WithDependent_WarnsAndRefusesWithoutForce()
    {
        var t1 = NewTarget();
        var t2 = NewTarget();
        Assert.True(NewDraft(_fwDataPath, ProductVersion, "v1", null).Succeeded);
        var add1 = AddSetGloss(_fwDataPath, ProductVersion, "v1", t1, "en", "base");
        Assert.True(add1.Succeeded);
        var op1Id = add1.Value!.OperationId;
        var add2 = AddSetGloss(_fwDataPath, ProductVersion, "v1", t2, "en", "dependent", new[] { op1Id });
        Assert.True(add2.Succeeded);
        var op2Id = add2.Value!.OperationId;

        var result = RemoveOperations(_fwDataPath, ProductVersion, "v1", new[] { op1Id }, force: false);
        Assert.False(result.Succeeded);
        Assert.Equal("operation.invalid-dependency", result.Refusal!.Code);
        Assert.Contains("orphan", result.Refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(op1Id, result.Refusal.Message);
        Assert.Contains(op2Id, result.Refusal.Message);
        Assert.Contains("--force", result.Refusal.Message);

        // Nothing was mutated: the draft still has both operations.
        var draft = ReadDraft("v1");
        Assert.Equal(2, draft.Operations.Count);
    }

    // ---- remove: --force accepts the enumerated set and cascades to the dependent --------------

    [Fact]
    public void RemoveOperations_WithDependent_ForceCascadesAndClearsAnchor()
    {
        var t1 = NewTarget();
        var t2 = NewTarget();
        var t3 = NewTarget();
        Assert.True(NewDraft(_fwDataPath, ProductVersion, "v1", null).Succeeded);
        var add1 = AddSetGloss(_fwDataPath, ProductVersion, "v1", t1, "en", "base");
        Assert.True(add1.Succeeded);
        var op1Id = add1.Value!.OperationId;
        var add2 = AddSetGloss(_fwDataPath, ProductVersion, "v1", t2, "en", "dependent", new[] { op1Id });
        Assert.True(add2.Succeeded);
        var op2Id = add2.Value!.OperationId;
        Assert.True(AddSetGloss(_fwDataPath, ProductVersion, "v1", t3, "en", "independent").Succeeded);
        DraftRationale.Author(
            _fwDataPath, "v1", "Update related glosses", "Keep dependent edits together while preserving the independent edit.");

        var finalize = FinalizeDraft(_fwDataPath, ProductVersion, "v1");
        Assert.True(finalize.Succeeded);
        var proposalId = finalize.Value!.ProposalId;
        BindSyntheticAnchor(proposalId);

        Assert.True(ReopenDraft(_fwDataPath, ProductVersion, "v2", proposalId).Succeeded);

        var refused = RemoveOperations(_fwDataPath, ProductVersion, "v2", new[] { op1Id }, force: false);
        Assert.False(refused.Succeeded);

        var forced = RemoveOperations(_fwDataPath, ProductVersion, "v2", new[] { op1Id }, force: true);
        Assert.True(forced.Succeeded);
        Assert.Contains(forced.Value!.ForcedDependents, dependent => dependent.OperationId == op2Id);

        var afterRemove = ReadDraft("v2");
        Assert.Single(afterRemove.Operations);
        Assert.Equal(t3, afterRemove.Operations[0].Target);

        var amend = FinalizeDraft(_fwDataPath, ProductVersion, "v2");
        Assert.True(amend.Succeeded);
        Assert.Null(ReadAnchor(proposalId));
    }

    // ---- remove: transitive dependents are enumerated, not just the direct one -----------------

    [Fact]
    public void RemoveOperations_TransitiveDependents_AllEnumerated()
    {
        Assert.True(NewDraft(_fwDataPath, ProductVersion, "v1", null).Succeeded);
        var add1 = AddSetGloss(_fwDataPath, ProductVersion, "v1", NewTarget(), "en", "a");
        Assert.True(add1.Succeeded);
        var op1Id = add1.Value!.OperationId;
        var add2 = AddSetGloss(_fwDataPath, ProductVersion, "v1", NewTarget(), "en", "b", new[] { op1Id });
        Assert.True(add2.Succeeded);
        var op2Id = add2.Value!.OperationId;
        var add3 = AddSetGloss(_fwDataPath, ProductVersion, "v1", NewTarget(), "en", "c", new[] { op2Id });
        Assert.True(add3.Succeeded);
        var op3Id = add3.Value!.OperationId;

        var result = RemoveOperations(_fwDataPath, ProductVersion, "v1", new[] { op1Id }, force: false);
        Assert.False(result.Succeeded);
        Assert.Contains(op2Id, result.Refusal!.Message);
        Assert.Contains(op3Id, result.Refusal.Message); // transitive: op3 depends on op2 depends on op1

        var forced = RemoveOperations(_fwDataPath, ProductVersion, "v1", new[] { op1Id }, force: true);
        Assert.True(forced.Succeeded);
        Assert.Empty(ReadDraft("v1").Operations);
    }

    // ---- remove: cascading delete cannot be enumerated -> refused even with --force ------------

    [Fact]
    public void RemoveOperations_CascadingDelete_RefusedEvenWithForce()
    {
        var entryTarget = NewTarget();
        Assert.True(NewDraft(_fwDataPath, ProductVersion, "v1", null).Succeeded);
        var addDelete = AddDeleteLexemeForm(_fwDataPath, ProductVersion, "v1", entryTarget);
        Assert.True(addDelete.Succeeded);
        var deleteOpId = addDelete.Value!.OperationId;
        Assert.True(AddSetGloss(_fwDataPath, ProductVersion, "v1", NewTarget(), "en", "unrelated").Succeeded);

        var withoutForce = RemoveOperations(_fwDataPath, ProductVersion, "v1", new[] { deleteOpId }, force: false);
        Assert.False(withoutForce.Succeeded);
        Assert.Equal("operation.cascading-delete", withoutForce.Refusal!.Code);

        var withForce = RemoveOperations(_fwDataPath, ProductVersion, "v1", new[] { deleteOpId }, force: true);
        Assert.False(withForce.Succeeded);
        Assert.Equal("operation.cascading-delete", withForce.Refusal!.Code);

        // Refused either way: nothing was mutated.
        Assert.Equal(2, ReadDraft("v1").Operations.Count);
    }

    [Fact]
    public void RemoveOperations_UnknownOperationId_Fails()
    {
        Assert.True(NewDraft(_fwDataPath, ProductVersion, "v1", null).Succeeded);
        Assert.True(AddSetGloss(_fwDataPath, ProductVersion, "v1", NewTarget(), "en", "a").Succeeded);

        var bogus = CanonicalId.Mint().Value;
        var result = RemoveOperations(_fwDataPath, ProductVersion, "v1", new[] { bogus }, force: false);
        Assert.False(result.Succeeded);
        Assert.Contains(bogus, result.Refusal!.Message);
    }

    [Fact]
    public void AddSetGloss_DependsOnUnknownOperation_Fails()
    {
        Assert.True(NewDraft(_fwDataPath, ProductVersion, "v1", null).Succeeded);
        var bogus = CanonicalId.Mint().Value;
        var result = AddSetGloss(_fwDataPath, ProductVersion, "v1", NewTarget(), "en", "a", new[] { bogus });
        Assert.False(result.Succeeded);
        Assert.Contains(bogus, result.Refusal!.Message);
    }

    // ---- duplicate: fresh identity, copies content, leaves the source untouched ----------------

    [Fact]
    public void Duplicate_CreatesNewProposalId_CopiesOperations_SourceUnchanged()
    {
        var t1 = NewTarget();
        var t2 = NewTarget();
        var sourceId = CommitProposal("source", t1, t2);
        BindSyntheticAnchor(sourceId);

        var duplicate = DuplicateProposal(_fwDataPath, ProductVersion, sourceId, "dup");
        Assert.True(duplicate.Succeeded);
        Assert.NotEqual(sourceId, duplicate.Value!.ProposalId);

        var dupDraft = ReadDraft("dup");
        Assert.Equal(2, dupDraft.Operations.Count);
        Assert.NotEqual(sourceId, dupDraft.ProposalId);
        Assert.Contains(dupDraft.Operations, o => o.Target == t1);
        Assert.Contains(dupDraft.Operations, o => o.Target == t2);

        // The source Proposal's row (including its anchor) is untouched by duplicating it.
        Assert.NotNull(ReadAnchor(sourceId));

        var dupFinalize = FinalizeDraft(_fwDataPath, ProductVersion, "dup");
        Assert.True(dupFinalize.Succeeded);
        Assert.False(dupFinalize.Value!.IsAmend); // a first commit, not an amend
        var dupProposalId = dupFinalize.Value!.ProposalId;
        Assert.NotEqual(sourceId, dupProposalId);
        Assert.Null(ReadAnchor(dupProposalId)); // brand-new Proposal, never dry-run yet
        Assert.NotNull(ReadAnchor(sourceId)); // still untouched
    }

    [Fact]
    public void Duplicate_UnknownProposalId_Fails()
    {
        var bogus = CanonicalId.Mint().Value;
        var result = DuplicateProposal(_fwDataPath, ProductVersion, bogus, "dup");
        Assert.False(result.Succeeded);
        Assert.Equal("proposal.not-found", result.Refusal!.Code);
    }

    // ---- split: partitions operations into new Proposals, source untouched ---------------------

    /// <summary>
    /// Models "keep only rules 1 and 4, not 5" as a split into two successor Proposals rather than a
    /// discard, since split's job is partitioning, not dropping — that composes with <c>remove</c>: split,
    /// then reopen+remove on whichever fragment should drop rule 5.
    /// </summary>
    [Fact]
    public void Split_PartitionsOperationsIntoNewProposals_SourceUnchanged()
    {
        Assert.True(NewDraft(_fwDataPath, ProductVersion, "source", "three rules").Succeeded);
        var add1 = AddSetGloss(_fwDataPath, ProductVersion, "source", NewTarget(), "en", "rule1");
        Assert.True(add1.Succeeded);
        var op1Id = add1.Value!.OperationId;
        var add2 = AddSetGloss(_fwDataPath, ProductVersion, "source", NewTarget(), "en", "rule4");
        Assert.True(add2.Succeeded);
        var op2Id = add2.Value!.OperationId;
        var add3 = AddSetGloss(_fwDataPath, ProductVersion, "source", NewTarget(), "en", "rule5");
        Assert.True(add3.Succeeded);
        var op3Id = add3.Value!.OperationId;
        DraftRationale.Author(
            _fwDataPath, "source", "Partition authored gloss rules", "Split the rules into independently reviewable proposals.");
        var finalize = FinalizeDraft(_fwDataPath, ProductVersion, "source");
        Assert.True(finalize.Succeeded);
        var sourceId = finalize.Value!.ProposalId;
        BindSyntheticAnchor(sourceId);

        var groups = new[]
        {
            new SplitGroup("keep", new[] { op1Id, op2Id }),
            new SplitGroup("rest", new[] { op3Id }),
        };
        var split = SplitProposal(_fwDataPath, ProductVersion, sourceId, groups, force: false);
        Assert.True(split.Succeeded);

        var keepDraft = ReadDraft("keep");
        var restDraft = ReadDraft("rest");
        Assert.Equal(2, keepDraft.Operations.Count);
        Assert.Single(restDraft.Operations);
        Assert.NotEqual(keepDraft.ProposalId, restDraft.ProposalId);
        Assert.NotEqual(sourceId, keepDraft.ProposalId);
        Assert.NotEqual(sourceId, restDraft.ProposalId);

        // Source is untouched.
        Assert.NotNull(ReadAnchor(sourceId));

        Assert.True(FinalizeDraft(_fwDataPath, ProductVersion, "keep").Succeeded);
        Assert.True(FinalizeDraft(_fwDataPath, ProductVersion, "rest").Succeeded);
    }

    [Fact]
    public void Split_SeveredDependency_WarnsAndRefusesWithoutForce_ThenForceProceeds()
    {
        Assert.True(NewDraft(_fwDataPath, ProductVersion, "source", null).Succeeded);
        var add1 = AddSetGloss(_fwDataPath, ProductVersion, "source", NewTarget(), "en", "a");
        Assert.True(add1.Succeeded);
        var op1Id = add1.Value!.OperationId;
        var add2 = AddSetGloss(_fwDataPath, ProductVersion, "source", NewTarget(), "en", "b", new[] { op1Id });
        Assert.True(add2.Succeeded);
        var op2Id = add2.Value!.OperationId;
        DraftRationale.Author(
            _fwDataPath, "source", "Separate dependent gloss rules", "Preserve declared dependencies while partitioning the proposal.");
        var finalize = FinalizeDraft(_fwDataPath, ProductVersion, "source");
        Assert.True(finalize.Succeeded);
        var sourceId = finalize.Value!.ProposalId;

        var groups = new[]
        {
            new SplitGroup("groupA", new[] { op1Id }),
            new SplitGroup("groupB", new[] { op2Id }),
        };

        var refused = SplitProposal(_fwDataPath, ProductVersion, sourceId, groups, force: false);
        Assert.False(refused.Succeeded);
        Assert.Equal("operation.invalid-dependency", refused.Refusal!.Code);
        Assert.Contains("sever", refused.Refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(op1Id, refused.Refusal.Message);
        Assert.Contains(op2Id, refused.Refusal.Message);

        Assert.False(DraftExists("groupA"));
        Assert.False(DraftExists("groupB"));

        var forced = SplitProposal(_fwDataPath, ProductVersion, sourceId, groups, force: true);
        Assert.True(forced.Succeeded);

        var groupBDraft = ReadDraft("groupB");
        // The severed dependency is kept exactly as authored: it names an id outside groupB's own operations.
        Assert.Contains(op1Id, groupBDraft.Operations.Single().DependsOn);
    }

    [Fact]
    public void Split_UnassignedOperation_Fails()
    {
        Assert.True(NewDraft(_fwDataPath, ProductVersion, "source", null).Succeeded);
        var add1 = AddSetGloss(_fwDataPath, ProductVersion, "source", NewTarget(), "en", "a");
        Assert.True(add1.Succeeded);
        var op1Id = add1.Value!.OperationId;
        Assert.True(AddSetGloss(_fwDataPath, ProductVersion, "source", NewTarget(), "en", "b").Succeeded);
        DraftRationale.Author(
            _fwDataPath, "source", "Partition independent gloss rules", "Keep every authored edit assigned to a resulting proposal.");
        var finalize = FinalizeDraft(_fwDataPath, ProductVersion, "source");
        Assert.True(finalize.Succeeded);
        var sourceId = finalize.Value!.ProposalId;

        var groups = new[] { new SplitGroup("only", new[] { op1Id }) };
        var result = SplitProposal(_fwDataPath, ProductVersion, sourceId, groups, force: false);
        Assert.False(result.Succeeded);
        Assert.Equal("operation.invalid-id", result.Refusal!.Code);
    }

    [Fact]
    public void Split_DuplicateAssignedOperation_Fails()
    {
        Assert.True(NewDraft(_fwDataPath, ProductVersion, "source", null).Succeeded);
        var add1 = AddSetGloss(_fwDataPath, ProductVersion, "source", NewTarget(), "en", "a");
        Assert.True(add1.Succeeded);
        var op1Id = add1.Value!.OperationId;
        DraftRationale.Author(
            _fwDataPath, "source", "Partition one gloss rule", "Ensure each rule is assigned to at most one resulting proposal.");
        var finalize = FinalizeDraft(_fwDataPath, ProductVersion, "source");
        Assert.True(finalize.Succeeded);
        var sourceId = finalize.Value!.ProposalId;

        var groups = new[]
        {
            new SplitGroup("groupA", new[] { op1Id }),
            new SplitGroup("groupB", new[] { op1Id }),
        };
        var result = SplitProposal(_fwDataPath, ProductVersion, sourceId, groups, force: false);
        Assert.False(result.Succeeded);
        Assert.Equal("proposal.split-duplicate-operation", result.Refusal!.Code);
    }

    private bool DraftExists(string draftName)
    {
        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        return new ProposalRepository(database).DraftNameExists(draftName);
    }

}
