using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using Xunit;

namespace SIL.Motif.Tests.Commands.Commands;

public sealed class TrialTouchedIdsTests
{
    private static OperationEnvelope Operation(string kind, Guid? target = null, Guid? after = null, Guid? before = null,
        string? owner = null) => new(CanonicalId.Mint("op/"), kind,
            target: target is { } t ? CanonicalId.FromGuid(t) : null,
            after: owner is null ? null : JsonDocument.Parse(JsonSerializer.Serialize(new { owner })).RootElement.Clone(),
            placement: after is null && before is null ? null
                : new Placement(after is { } a ? CanonicalId.FromGuid(a) : null, before is { } b ? CanonicalId.FromGuid(b) : null));

    [Fact]
    public void TargetsOwnersAndPlacementNeighboursAreTheTouchedObjectsAndNewIdsAreNot()
    {
        var (entry, allomorph, neighbour, owner) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var proposal = new Proposal(new Dictionary<string, string> { ["lexicon"] = "1.0" }, CanonicalId.Mint("proposal/"), null,
        [
            Operation("lexicon/entry.set", target: entry),
            Operation("lexicon/allomorph.move", target: allomorph, after: neighbour),
            Operation("lexicon/allomorph.create", owner: CanonicalId.FromGuid(owner).Value),
            new OperationEnvelope(CanonicalId.Mint("op/"), "lexicon/entry.create", entityId: CanonicalId.Mint("entry/")),
            Operation("lexicon/entry.set", target: entry),
        ]);
        Assert.Equal([entry, allomorph, neighbour, owner], TrialTouchedWords.TouchedIds(proposal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TouchedWithoutAWordListIsRefusedBeforeAnyStoreIsOpened(bool allWords)
    {
        var request = new EnqueueTrialRequest("missing.fwdata", "1.0", "proposal/one", AllWords: allWords) { Touched = true };
        var refused = TrialWorkflow.Enqueue(request, Path.Combine(Path.GetTempPath(), "motif-touched-none"), null,
            BaselineMode.BestEffort, CancellationToken.None);
        Assert.Equal(RefusalCodes.TrialTouchedNeedsWords, refused.Refusal!.Code);
        Assert.Contains("Next:", refused.Refusal.Message);
    }
}
