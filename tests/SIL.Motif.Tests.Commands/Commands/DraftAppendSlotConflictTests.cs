using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group1)]
public sealed class DraftAppendSlotConflictTests(PristineProjectFixture pristine)
{
    private const string Version = "1.0";
    private const string DraftName = "slots";

    [Fact]
    public void ADraftThatAlreadyHoldsASlotPairStillAcceptsAnUnrelatedAppend()
    {
        var (path, _, duplicate) = DraftWithAPair();

        var added = ProposalCommands.AddSetGloss(new AddSetGlossRequest(
            path, Version, DraftName, CanonicalId.Mint().Value, "en", "elsewhere"));

        Assert.True(added.Succeeded, added.Refusal?.Message);
        Assert.NotEqual(duplicate, added.Value!.OperationId);
    }

    [Fact]
    public void RemovingOneOfAPreExistingPairSucceeds()
    {
        var (path, _, duplicate) = DraftWithAPair();

        var removed = ProposalCommands.RemoveOperations(new RemoveOperationsRequest(
            path, Version, DraftName, [duplicate], Force: true));

        Assert.True(removed.Succeeded, removed.Refusal?.Message);
    }

    [Fact]
    public void TheSameTargetAndFieldInAnotherWritingSystemIsAccepted()
    {
        var path = NewDraft();
        var target = CanonicalId.Mint().Value;
        Assert.True(ProposalCommands.AddSetGloss(new AddSetGlossRequest(
            path, Version, DraftName, target, "en", "first")).Succeeded);

        var other = ProposalCommands.AddSetGloss(new AddSetGlossRequest(
            path, Version, DraftName, target, "fr", "second"));

        Assert.True(other.Succeeded, other.Refusal?.Message);
        Assert.Equal(2, Operations(path).Count);
    }

    [Fact]
    public void TheSameFieldAndWritingSystemOnAnotherTargetIsAccepted()
    {
        var path = NewDraft();
        Assert.True(ProposalCommands.AddSetGloss(new AddSetGlossRequest(
            path, Version, DraftName, CanonicalId.Mint().Value, "en", "first")).Succeeded);

        var other = ProposalCommands.AddSetGloss(new AddSetGlossRequest(
            path, Version, DraftName, CanonicalId.Mint().Value, "en", "second"));

        Assert.True(other.Succeeded, other.Refusal?.Message);
        Assert.Equal(2, Operations(path).Count);
    }

    // The pair is written straight into the Draft, since appending through a command is what is being tested.
    private (string Path, string Existing, string Duplicate) DraftWithAPair()
    {
        var path = NewDraft();
        var added = ProposalCommands.AddSetGloss(new AddSetGlossRequest(
            path, Version, DraftName, CanonicalId.Mint().Value, "en", "first"));
        Assert.True(added.Succeeded, added.Refusal?.Message);
        using var database = ProjectMotifDatabase.Open(path);
        var repository = new ProposalRepository(database);
        var loaded = repository.GetDraft(DraftName).ProposalJson!;
        var draft = ProposalCommands.DeserializeDraft(loaded);
        var copy = JsonSerializer.Deserialize<DraftOperation>(JsonSerializer.Serialize(draft.Operations[0]))!;
        copy.OperationId = CanonicalId.Mint().Value;
        draft.Operations.Add(copy);
        Assert.True(repository.TrySaveDraft(DraftName, loaded, ProposalCommands.SerializeDraft(draft)));
        return (path, added.Value!.OperationId, copy.OperationId);
    }

    private static List<DraftOperation> Operations(string path)
    {
        using var database = ProjectMotifDatabase.Open(path);
        return ProposalCommands.DeserializeDraft(new ProposalRepository(database).GetDraft(DraftName).ProposalJson!)
            .Operations;
    }

    private string NewDraft()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        cache.Dispose();
        Assert.True(ProposalCommands.New(new NewDraftRequest(path, Version, DraftName, "Slots")).Succeeded);
        return path;
    }
}
