using System.Text.Json;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using static SIL.Motif.Tests.Composers.AffixTestSupport;

namespace SIL.Motif.Tests.Composers;

[Collection(LcmCacheParallelCollections.Group3)]
public sealed class RemoveFeatureValueComposerTests(PristineProjectFixture pristine)
{
    [Fact]
    public void RemovingAFeatureTheStructureDoesNotSpecifyIsRefused()
    {
        using var cache = pristine.NewScratch();
        var ws = Anal(cache);
        var verb = Created(ApplyOps(cache, AuthorPartOfSpeechComposer.Build(cache,
            new AuthorPartOfSpeechIntent("Verb", ws, "V"))), CmPossibilityListPossibilitiesOperationKinds.Create);
        var affix = AddInflectionalPrefix(cache, "ma", verb);
        var featureProposal = ApplyOps(cache, AuthorInflectionFeatureComposer.Build(cache,
            new("relative concord", ws, [new("Group one", "g1")], "rc")));
        var feature = Created(featureProposal, FsFeatureSystemFeaturesOperationKinds.Create);
        var structure = Created(ApplyOps(cache, AuthorFeatureStructureComposer.Build(cache, new(affix.Msa))),
            MoInflAffMsaInflFeatsOperationKinds.CreateInflFeats);

        var refusal = Assert.Throws<InvalidOperationException>(() =>
            RemoveFeatureValueComposer.Build(cache, new(structure, feature)));
        Assert.Contains("feature-value-not-found", refusal.Message);
    }

    [Fact]
    public void TheIntentIsClosedToUnknownFields()
    {
        using var valid = JsonDocument.Parse(
            """{"featStruc":"AAAAAAAAAAAAAAAAAAAAAA","feature":"AAAAAAAAAAAAAAAAAAAAAQ"}""");
        Assert.Equal("AAAAAAAAAAAAAAAAAAAAAQ", RemoveFeatureValueIntentParser.Parse(valid.RootElement).Feature.Value);
        using var extra = JsonDocument.Parse(
            """{"featStruc":"AAAAAAAAAAAAAAAAAAAAAA","feature":"AAAAAAAAAAAAAAAAAAAAAQ","value":"AAAAAAAAAAAAAAAAAAAAAg"}""");
        Assert.Throws<ContractParseException>(() => RemoveFeatureValueIntentParser.Parse(extra.RootElement));
    }
}
