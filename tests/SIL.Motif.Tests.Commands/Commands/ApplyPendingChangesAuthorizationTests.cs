using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class ApplyPendingChangesAuthorizationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "apply-pending-authorization-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void EmptyDraftAndApplyingTheDraftDoNotTriggerTheRefusal()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var target = fixture.SaveProposal().Id;
        var draft = new DraftDocument { ProposalId = CanonicalId.Mint("proposal/").Value };
        Save(fixture, draft);
        Assert.Null(ApplyAuthorization.PendingChangesRefusal(fixture.Database, target));
        draft.Operations.Add(Operation("one", "approval"));
        Save(fixture, draft);
        Assert.Null(ApplyAuthorization.PendingChangesRefusal(fixture.Database, CanonicalId.Parse(draft.ProposalId)));
    }

    [Fact]
    public void WordApprovalsAreCountedByChangeAndNamed()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var target = fixture.SaveProposal().Id;
        var draft = new DraftDocument { ProposalId = CanonicalId.Mint("proposal/").Value };
        draft.Operations.Add(Operation("one", "approval"));
        draft.Operations.Add(Operation("two", "approval"));
        draft.ComposerProvenance.Add(JsonSerializer.SerializeToElement(new { changeId = "approval", word = "cat" }));
        Save(fixture, draft);
        var refusal = ApplyAuthorization.PendingChangesRefusal(fixture.Database, target)!;
        Assert.Equal(RefusalCodes.ApplyPendingChanges, refusal.Code);
        Assert.Equal("You have 1 change not applied yet (word 'cat'). Apply or discard it first.", refusal.Message);
    }

    [Fact]
    public void EveryUnlabelledOperationCountsAndLongListsAreTruncated()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var target = fixture.SaveProposal().Id;
        var draft = new DraftDocument { ProposalId = CanonicalId.Mint("proposal/").Value };
        foreach (var id in new[] { "one", "two", "three", "four" })
            draft.Operations.Add(new DraftOperation { OperationId = id, Target = id });
        draft.ComposerProvenance.Add(JsonSerializer.SerializeToElement(new
        {
            input = new { pendingName = "GDR 'Plural'" }, operationIds = new[] { "one" },
        }));
        Save(fixture, draft);
        var refusal = ApplyAuthorization.PendingChangesRefusal(fixture.Database, target)!;
        Assert.Equal("You have 4 changes not applied yet (GDR 'Plural', change 'two', change 'three' and 1 more). Apply or discard them first.", refusal.Message);
    }

    private static DraftOperation Operation(string id, string changeId) => new()
    {
        OperationId = id,
        Extensions = JsonSerializer.SerializeToElement(new { changeId }),
    };

    private static void Save(TrialsTests.ProjectFixture fixture, DraftDocument draft)
    {
        var repository = new ProposalRepository(fixture.Database);
        var json = JsonSerializer.Serialize(draft);
        if (repository.FindPendingChangesDraft() is null)
            repository.CreateDraft(PendingChanges.DraftName, CanonicalId.Parse(draft.ProposalId), json, ProposalRoles.PendingChanges);
        else repository.SaveDraft(PendingChanges.DraftName, json);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
