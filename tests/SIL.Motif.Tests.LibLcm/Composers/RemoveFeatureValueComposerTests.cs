using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
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
    public void ANegatedFeatureValueOnAStemIsRemovedLikeAClosedOne()
    {
        using var cache = pristine.NewScratch();
        var ws = Anal(cache);
        var verb = Created(ApplyOps(cache, AuthorPartOfSpeechComposer.Build(cache,
            new AuthorPartOfSpeechIntent("Verb", ws, "V"))), CmPossibilityListPossibilitiesOperationKinds.Create);
        var stem = AddStem(cache, "omba", verb);
        var feature = Created(ApplyOps(cache, AuthorInflectionFeatureComposer.Build(cache,
            new("relative concord", ws, [new("Group one", "g1")], "rc"))),
            FsFeatureSystemFeaturesOperationKinds.Create);
        var structure = Created(ApplyOps(cache, AuthorFeatureStructureComposer.Build(cache, new(stem.Msa))),
            MoStemMsaMsFeaturesOperationKinds.CreateMsFeatures);
        var services = cache.ServiceLocator;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var negated = services.GetInstance<IFsNegatedValueFactory>().Create();
            services.GetInstance<IFsFeatStrucRepository>().GetObject(structure.ToGuid()).FeatureSpecsOC.Add(negated);
            negated.FeatureRA = services.GetInstance<IFsFeatDefnRepository>().GetObject(feature.ToGuid());
            negated.ValueRA = services.GetInstance<IFsSymFeatValRepository>().AllInstances().First();
        });
        Assert.Single(services.GetInstance<IFsNegatedValueRepository>().AllInstances());

        ApplyOps(cache, RemoveFeatureValueComposer.Build(cache, new(structure, feature)));

        Assert.Empty(services.GetInstance<IFsNegatedValueRepository>().AllInstances());
    }

    [Theory]
    [InlineData("from")]
    [InlineData("to")]
    public void ASpecificationOnADerivationalAffixStructureIsRemovedAndTheStructureStays(string side)
    {
        using var cache = pristine.NewScratch();
        var ws = Anal(cache);
        var verb = Created(ApplyOps(cache, AuthorPartOfSpeechComposer.Build(cache,
            new AuthorPartOfSpeechIntent("Verb", ws, "V"))), CmPossibilityListPossibilitiesOperationKinds.Create);
        var msa = DerivationalMsa(cache, verb);
        var featureProposal = ApplyOps(cache, AuthorInflectionFeatureComposer.Build(cache,
            new("transitivity", ws, [new("Transitive", "tr")], "tv")));
        var feature = Created(featureProposal, FsFeatureSystemFeaturesOperationKinds.Create);
        var value = featureProposal.Operations.Single(op => op.Kind == FsClosedFeatureValuesOperationKinds.Create).EntityId!.Value;
        var create = ApplyOps(cache, AuthorFeatureStructureComposer.Build(cache, new(msa, side)));
        var structure = create.Operations.Single().EntityId!.Value;
        ApplyOps(cache, AuthorFeatureValueComposer.Build(cache, new(structure, feature, value)));
        Assert.Single(Get<IFsFeatStruc>(cache, structure).FeatureSpecsOC);

        var removal = ApplyOps(cache, RemoveFeatureValueComposer.Build(cache, new(structure, feature)));

        Assert.Single(removal.Operations, op => op.Kind == FsFeatStrucFeatureSpecsOperationKinds.DeleteFeatureSpecs);
        Assert.Empty(Get<IFsFeatStruc>(cache, structure).FeatureSpecsOC);
        var owner = Get<IMoDerivAffMsa>(cache, msa);
        Assert.Equal(structure.ToGuid(), (side == "to" ? owner.ToMsFeaturesOA : owner.FromMsFeaturesOA)!.Guid);
    }

    internal static CanonicalId DerivationalMsa(LcmCache cache, CanonicalId category)
    {
        var created = ApplyOps(cache, AuthorLexicalEntryComposer.Build(cache, new AuthorLexicalEntryIntent("pa",
            Vern(cache), MorphType(MoMorphTypeTags.kguidMorphPrefix), "pa", Anal(cache),
            new LexicalEntryMsa(LexicalEntryMsaKind.Derivational, category, category))));
        return Created(created, LexEntryMorphoSyntaxAnalysesOperationKinds.Create);
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
