using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using static SIL.Motif.Tests.Composers.AffixTestSupport;

namespace SIL.Motif.Tests.Composers;

/// <summary>Checks that exception features are created, carried by stems and required by inflectional affixes.</summary>
[Collection(LcmCacheParallelCollections.Group1)]
public sealed class ExceptionFeatureComposerTests(PristineProjectFixture pristine)
{
    private static CanonicalId NewFeature(LcmCache cache, string name, string? abbreviation = null) =>
        Created(ApplyOps(cache, AuthorExceptionFeatureComposer.Build(cache,
            new(name, Anal(cache), abbreviation))), CmPossibilityListPossibilitiesOperationKinds.Create);

    [Fact]
    public void AnExceptionFeatureIsCreatedInTheExceptionFeaturesListWithItsNameAndAbbreviation()
    {
        using var cache = pristine.NewScratch();
        var id = NewFeature(cache, "Loan stem", "loan");

        var feature = Get<ICmPossibility>(cache, id);
        Assert.Equal(cache.LangProject.MorphologicalDataOA.ProdRestrictOA, feature.Owner);
        Assert.Equal("Loan stem", feature.Name.get_String(cache.DefaultAnalWs).Text);
        Assert.Equal("loan", feature.Abbreviation.get_String(cache.DefaultAnalWs).Text);
    }

    [Fact]
    public void ADuplicateNameOrBlankAbbreviationIsRefused()
    {
        using var cache = pristine.NewScratch();
        var existing = NewFeature(cache, "Loan stem");

        var duplicate = Assert.Throws<InvalidOperationException>(() => AuthorExceptionFeatureComposer.Build(cache,
            new("Loan stem", Anal(cache))));
        Assert.Contains(existing.Value, duplicate.Message);
        Assert.Throws<InvalidOperationException>(() => AuthorExceptionFeatureComposer.Build(cache,
            new("Kin", Anal(cache), " ")));
        Assert.Throws<InvalidOperationException>(() => AuthorExceptionFeatureComposer.Build(cache,
            new("Kin", "zz-unknown")));
    }

    [Fact]
    public void AStemCarriesTheExactSetAndAnEmptySetClearsIt()
    {
        using var cache = pristine.NewScratch();
        var verb = AddCategory(cache, "Verb");
        var stem = AddStem(cache, "kaun", verb);
        var loan = NewFeature(cache, "Loan stem");
        var kin = NewFeature(cache, "Kin");

        ApplyOps(cache, EditExceptionFeaturesComposer.Build(cache, new(stem.Msa, [loan, kin])));
        Assert.Equal(new[] { loan.ToGuid(), kin.ToGuid() }.Order(),
            Get<IMoStemMsa>(cache, stem.Msa).ProdRestrictRC.Select(item => item.Guid).Order());

        var change = EditExceptionFeaturesComposer.Build(cache, new(stem.Msa, [kin]));
        Assert.Equal([MoStemMsaProdRestrictOperationKinds.RemoveRefProdRestrict], change.Select(item => item.Kind));
        ApplyOps(cache, change);
        Assert.Equal(kin.ToGuid(), Assert.Single(Get<IMoStemMsa>(cache, stem.Msa).ProdRestrictRC).Guid);

        Assert.Empty(EditExceptionFeaturesComposer.Build(cache, new(stem.Msa, [kin])));
        ApplyOps(cache, EditExceptionFeaturesComposer.Build(cache, new(stem.Msa, [])));
        Assert.Empty(Get<IMoStemMsa>(cache, stem.Msa).ProdRestrictRC);
    }

    [Fact]
    public void AnInflectionalAffixRequiresTheSetAndADerivationalOrUnclassifiedAffixIsRefused()
    {
        using var cache = pristine.NewScratch();
        var verb = AddCategory(cache, "Verb");
        var prefix = AddInflectionalPrefix(cache, "ku", verb);
        var loan = NewFeature(cache, "Loan stem");

        ApplyOps(cache, EditExceptionFeaturesComposer.Build(cache, new(prefix.Msa, [loan])));
        Assert.Equal(loan.ToGuid(), Assert.Single(Get<IMoInflAffMsa>(cache, prefix.Msa).FromProdRestrictRC).Guid);

        var derivational = ApplyOps(cache, AuthorLexicalEntryComposer.Build(cache, new AuthorLexicalEntryIntent("pag",
            Vern(cache), MorphType(MoMorphTypeTags.kguidMorphPrefix), "pag", Anal(cache),
            new LexicalEntryMsa(LexicalEntryMsaKind.Derivational, verb, AddCategory(cache, "Noun")))));
        var refusedDerivational = Assert.Throws<InvalidOperationException>(() => EditExceptionFeaturesComposer.Build(cache,
            new(Created(derivational, LexEntryMorphoSyntaxAnalysesOperationKinds.Create), [loan])));
        Assert.Contains("compose-edit-derivational-affix-restriction", refusedDerivational.Message);

        var unclassified = ApplyOps(cache, AuthorLexicalEntryComposer.Build(cache, new AuthorLexicalEntryIntent("tu",
            Vern(cache), MorphType(MoMorphTypeTags.kguidMorphSuffix), "tu", Anal(cache),
            new LexicalEntryMsa(LexicalEntryMsaKind.Unclassified, verb))));
        var refusedUnclassified = Assert.Throws<InvalidOperationException>(() => EditExceptionFeaturesComposer.Build(cache,
            new(Created(unclassified, LexEntryMorphoSyntaxAnalysesOperationKinds.Create), [loan])));
        Assert.Contains("unclassified", refusedUnclassified.Message);
    }

    [Fact]
    public void AFeatureOutsideTheListOrRepeatedIsRefused()
    {
        using var cache = pristine.NewScratch();
        var verb = AddCategory(cache, "Verb");
        var stem = AddStem(cache, "kaun", verb);
        var loan = NewFeature(cache, "Loan stem");

        Assert.Contains("not one of the project's exception features", Assert.Throws<InvalidOperationException>(() =>
            EditExceptionFeaturesComposer.Build(cache, new(stem.Msa, [verb]))).Message);
        Assert.Contains("must not repeat", Assert.Throws<InvalidOperationException>(() =>
            EditExceptionFeaturesComposer.Build(cache, new(stem.Msa, [loan, loan]))).Message);
    }

    [Fact]
    public void TheIntentIsClosedAndNeedsTheFeatureList()
    {
        var msa = CanonicalId.Mint().Value;
        Assert.Empty(EditExceptionFeaturesIntentParser.Parse(JsonDocument.Parse(
            $$"""{"msa":"{{msa}}","exceptionFeatures":[]}""").RootElement).ExceptionFeatures);
        Assert.ThrowsAny<Exception>(() => EditExceptionFeaturesIntentParser.Parse(JsonDocument.Parse(
            $$"""{"msa":"{{msa}}"}""").RootElement));
        Assert.ThrowsAny<Exception>(() => EditExceptionFeaturesIntentParser.Parse(JsonDocument.Parse(
            $$"""{"msa":"{{msa}}","exceptionFeatures":[],"extra":1}""").RootElement));
        Assert.ThrowsAny<Exception>(() => AuthorExceptionFeatureIntentParser.Parse(JsonDocument.Parse(
            """{"name":"x","ws":"en","color":"red"}""").RootElement));
    }
}
