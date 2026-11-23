using System.Text.Json;
using SIL.Motif.Commands.Review;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Review;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Host.Baselines;
using Xunit;

namespace SIL.Motif.Tests.Commands.Review;

/// <summary>Follows real Proposal operations through a real Baseline copy to the entry of a lost Reading.</summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group2)]
public sealed class RegressionOverlapBaselineTests(PristineProjectFixture pristine)
{
    private static OperationEnvelope Operation(string kind, Guid? target = null, Guid? after = null, string? owner = null) =>
        new(CanonicalId.Mint("op/"), kind, target: target is { } t ? CanonicalId.FromGuid(t) : null,
            after: owner is null ? null : JsonDocument.Parse(JsonSerializer.Serialize(new { owner })).RootElement.Clone(),
            placement: after is { } a ? new Placement(CanonicalId.FromGuid(a), null) : null);

    [Fact]
    public void AnEntryTargetACreateOwnerAndAPlacementNeighbourEachOverlapTheLostReadingAndNoOtherOperationDoes()
    {
        var seed = pristine.Seed;
        var proposal = new Proposal(new Dictionary<string, string> { ["lexicon"] = "1.0" }, CanonicalId.Mint("proposal/"), null,
        [
            Operation("lexicon/entry.set", target: seed.FirstEntryId),
            Operation("lexicon/allomorph.create", owner: CanonicalId.FromGuid(seed.FirstEntryId).Value),
            Operation("lexicon/allomorph.move", target: Guid.NewGuid(), after: seed.FirstLexemeFormId),
            Operation("lexicon/entry.set", target: seed.SecondEntryId),
        ]);
        using var context = RegressionContext.Build(proposal, BaselineReadCache.Open(pristine.CopyProjectFile()), null);
        var form = CanonicalId.FromGuid(seed.FirstLexemeFormId).ToString();
        var entry = context.EntryOf(form);
        Assert.Equal(CanonicalId.FromGuid(seed.FirstEntryId).ToString(), entry);
        var reading = new RegressedReading("x", [new RegressedMorph(entry, null, form)]);
        var touched = RegressionContext.Overlap(context, [reading]);
        Assert.Equal([1, 2, 3], touched.Select(item => item.Position));
        Assert.All(touched, item => Assert.NotEmpty(item.SharedIds));
        Assert.Contains(entry!, touched[1].SharedIds);
        Assert.Contains(form, touched[2].SharedIds);
    }
}
