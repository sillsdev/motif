using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.Composers;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Runner;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class PhonologicalRulePlacementPrefixTests(PristineProjectFixture pristine)
{
    [Fact]
    public void ExistingRulePlacementMatchesCanonicalGuidAcrossPrefixes()
    {
        using var cache = pristine.NewScratch();
        var phoneme = SoundSystemComposerTests.Compose(cache, [],
            () => AuthorPhonemeComposer.Build(cache, new("n", ["n"])));
        var existingOperations = AuthorPhonologicalRuleComposer.Build(cache, new(
            "existing complete rule",
            PhonologicalRuleDirection.Simultaneous,
            [new(Phoneme: phoneme)],
            [new(Phoneme: phoneme)],
            [new(Boundary: "word")],
            [new(Boundary: "morpheme")]));
        SoundSystemComposerTests.Execute(cache, existingOperations);
        var existing = Assert.IsAssignableFrom<IPhRegularRule>(
            Assert.Single(cache.LangProject.PhonologicalDataOA.PhonRulesOS));
        Assert.Single(existing.RightHandSidesOS);
        var prefixedId = CanonicalId.FromGuid(existing.Guid, "@canonical/");

        var nextOperations = AuthorPhonologicalRuleComposer.Build(cache, new(
            "following complete rule",
            PhonologicalRuleDirection.Simultaneous,
            [new(Phoneme: phoneme)],
            [new(Phoneme: phoneme)],
            [new(Boundary: "word")],
            [new(Boundary: "morpheme")],
            new Placement(prefixedId, null)));

        var create = Assert.Single(nextOperations, operation =>
            operation.Kind == PhPhonDataPhonRulesOperationKinds.Create);
        Assert.True(prefixedId.SpellingEquals(create.Placement!.After!.Value));
        SoundSystemComposerTests.Execute(cache, nextOperations);
        Assert.Equal(new[] { existing.Guid, create.EntityId!.Value.ToGuid() },
            cache.LangProject.PhonologicalDataOA.PhonRulesOS.Select(rule => rule.Guid));
    }
}
