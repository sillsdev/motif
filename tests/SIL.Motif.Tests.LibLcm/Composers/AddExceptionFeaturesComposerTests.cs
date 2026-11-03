using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using static SIL.Motif.Tests.Composers.AffixTestSupport;

namespace SIL.Motif.Tests.Composers;

/// <summary>Checks that one call tags many stems and keeps whatever each already carries.</summary>
[Collection(LcmCacheParallelCollections.Group1)]
public sealed class AddExceptionFeaturesComposerTests(PristineProjectFixture pristine)
{
    private static CanonicalId NewFeature(LcmCache cache, string name) =>
        Created(ApplyOps(cache, AuthorExceptionFeatureComposer.Build(cache, new(name, Anal(cache)))),
            CmPossibilityListPossibilitiesOperationKinds.Create);

    [Fact]
    public void ManyStemsGainTheFeaturesAndKeepTheOnesTheyHave()
    {
        using var cache = pristine.NewScratch();
        var verb = AddCategory(cache, "Verb");
        var first = AddStem(cache, "kaun", verb);
        var second = AddStem(cache, "inum", verb);
        var third = AddStem(cache, "tulog", verb);
        var loan = NewFeature(cache, "Loan stem");
        var kin = NewFeature(cache, "Kin");
        ApplyOps(cache, EditExceptionFeaturesComposer.Build(cache, new(second.Msa, [kin])));

        var change = AddExceptionFeaturesComposer.Build(cache, new([first.Msa, second.Msa, third.Msa], [loan, kin]));
        Assert.Equal(5, change.Count);
        Assert.All(change, item => Assert.Equal(MoStemMsaProdRestrictOperationKinds.AddRefProdRestrict, item.Kind));
        ApplyOps(cache, change);

        foreach (var stem in new[] { first, second, third })
            Assert.Equal(new[] { loan.ToGuid(), kin.ToGuid() }.Order(),
                Get<IMoStemMsa>(cache, stem.Msa).ProdRestrictRC.Select(item => item.Guid).Order());
        Assert.Empty(AddExceptionFeaturesComposer.Build(cache, new([first.Msa, second.Msa, third.Msa], [loan, kin])));
    }

    [Fact]
    public void AnInflectionalAffixGainsRequiredFeaturesAndAnUnclassifiedAffixHasNone()
    {
        using var cache = pristine.NewScratch();
        var verb = AddCategory(cache, "Verb");
        var prefix = AddInflectionalPrefix(cache, "ku", verb);
        var loan = NewFeature(cache, "Loan stem");
        var kin = NewFeature(cache, "Kin");
        ApplyOps(cache, EditExceptionFeaturesComposer.Build(cache, new(prefix.Msa, [kin])));

        ApplyOps(cache, AddExceptionFeaturesComposer.Build(cache, new([prefix.Msa], [loan])));
        Assert.Equal(new[] { loan.ToGuid(), kin.ToGuid() }.Order(),
            Get<IMoInflAffMsa>(cache, prefix.Msa).FromProdRestrictRC.Select(item => item.Guid).Order());

        var toSide = Assert.Throws<InvalidOperationException>(() =>
            AddExceptionFeaturesComposer.Build(cache, new([prefix.Msa], [loan], "to")));
        Assert.Contains("only a derivational affix", toSide.Message);
    }

    [Fact]
    public void ManyDerivationalAffixesGainFromFeaturesByDefaultAndToFeaturesOnRequest()
    {
        using var cache = pristine.NewScratch();
        var verb = AddCategory(cache, "Verb");
        var noun = AddCategory(cache, "Noun");
        var msas = new List<CanonicalId>();
        foreach (var form in new[] { "pag", "mag", "tag" })
        {
            var entry = ApplyOps(cache, AuthorLexicalEntryComposer.Build(cache, new AuthorLexicalEntryIntent(form,
                Vern(cache), MorphType(MoMorphTypeTags.kguidMorphPrefix), form, Anal(cache),
                new LexicalEntryMsa(LexicalEntryMsaKind.Derivational, verb, noun))));
            msas.Add(Created(entry, LexEntryMorphoSyntaxAnalysesOperationKinds.Create));
        }
        var loan = NewFeature(cache, "Loan stem");
        var kin = NewFeature(cache, "Kin");

        var from = AddExceptionFeaturesComposer.Build(cache, new(msas, [loan, kin]));
        Assert.Equal(6, from.Count);
        Assert.All(from, item => Assert.Equal(MoDerivAffMsaFromProdRestrictOperationKinds.AddRefFromProdRestrict, item.Kind));
        ApplyOps(cache, from);
        Assert.Empty(AddExceptionFeaturesComposer.Build(cache, new(msas, [loan, kin], "from")));

        var to = AddExceptionFeaturesComposer.Build(cache, new(msas, [kin], "to"));
        Assert.Equal(3, to.Count);
        Assert.All(to, item => Assert.Equal(MoDerivAffMsaToProdRestrictOperationKinds.AddRefToProdRestrict, item.Kind));
        ApplyOps(cache, to);
        foreach (var msa in msas)
        {
            var derivational = Get<IMoDerivAffMsa>(cache, msa);
            Assert.Equal(new[] { loan.ToGuid(), kin.ToGuid() }.Order(), derivational.FromProdRestrictRC.Select(item => item.Guid).Order());
            Assert.Equal(kin.ToGuid(), Assert.Single(derivational.ToProdRestrictRC).Guid);
        }
        Assert.Empty(AddExceptionFeaturesComposer.Build(cache, new(msas, [kin], "to")));
    }

    [Fact]
    public void TheSideMustBeFromOrTo()
    {
        using var json = JsonDocument.Parse($$"""{"msas":["{{CanonicalId.Mint().Value}}"],"exceptionFeatures":["{{CanonicalId.Mint().Value}}"],"side":"both"}""");
        Assert.Throws<SIL.Motif.Contract.Parsing.ContractParseException>(() => AddExceptionFeaturesIntentParser.Parse(json.RootElement));
        using var ok = JsonDocument.Parse($$"""{"msas":["{{CanonicalId.Mint().Value}}"],"exceptionFeatures":["{{CanonicalId.Mint().Value}}"],"side":"to"}""");
        Assert.Equal("to", AddExceptionFeaturesIntentParser.Parse(ok.RootElement).Side);
    }

    [Fact]
    public void AnUnknownFeatureOrARepeatedIdIsRefusedAndStagesNothing()
    {
        using var cache = pristine.NewScratch();
        var verb = AddCategory(cache, "Verb");
        var stem = AddStem(cache, "kaun", verb);
        var loan = NewFeature(cache, "Loan stem");

        Assert.Contains("not one of the project's exception features", Assert.Throws<InvalidOperationException>(() =>
            AddExceptionFeaturesComposer.Build(cache, new([stem.Msa], [verb]))).Message);
        Assert.Contains("'msas' must not repeat", Assert.Throws<InvalidOperationException>(() =>
            AddExceptionFeaturesComposer.Build(cache, new([stem.Msa, stem.Msa], [loan]))).Message);
        Assert.Contains("'exceptionFeatures' must not repeat", Assert.Throws<InvalidOperationException>(() =>
            AddExceptionFeaturesComposer.Build(cache, new([stem.Msa], [loan, loan]))).Message);
        Assert.Throws<InvalidOperationException>(() =>
            AddExceptionFeaturesComposer.Build(cache, new([stem.Msa, CanonicalId.Mint()], [loan])));
        Assert.Empty(Get<IMoStemMsa>(cache, stem.Msa).ProdRestrictRC);
    }

    [Fact]
    public void TheIntentIsClosedAndNeedsBothNonEmptyLists()
    {
        var msa = CanonicalId.Mint().Value;
        var feature = CanonicalId.Mint().Value;
        var parsed = AddExceptionFeaturesIntentParser.Parse(JsonDocument.Parse(
            $$"""{"msas":["{{msa}}"],"exceptionFeatures":["{{feature}}"]}""").RootElement);
        Assert.Single(parsed.Msas);
        Assert.Single(parsed.ExceptionFeatures);
        foreach (var json in new[]
                 {
                     $$"""{"msas":[],"exceptionFeatures":["{{feature}}"]}""",
                     $$"""{"msas":["{{msa}}"],"exceptionFeatures":[]}""",
                     $$"""{"msas":["{{msa}}"]}""",
                     $$"""{"msas":["{{msa}}"],"exceptionFeatures":["{{feature}}"],"extra":1}""",
                     $$"""{"msas":["{{msa}}","{{msa}}"],"exceptionFeatures":["{{feature}}"]}""",
                 })
            Assert.ThrowsAny<Exception>(() => AddExceptionFeaturesIntentParser.Parse(JsonDocument.Parse(json).RootElement));
    }

    [Fact]
    public void ANewStemEntryCarriesTheExceptionFeaturesItsMsaNamesAndRefusesUnknownRepeatedOrAffixOnes()
    {
        using var cache = pristine.NewScratch();
        var verb = AddCategory(cache, "Verb");
        var loan = NewFeature(cache, "Loan stem");
        string Json(string kind, string features) => "{\"form\":\"kaun\",\"formWs\":\"" + Vern(cache) +
            "\",\"morphType\":\"" + MorphType(kind == "stem" ? MoMorphTypeTags.kguidMorphStem : MoMorphTypeTags.kguidMorphSuffix).Value +
            "\",\"gloss\":\"eat\",\"analysisWs\":\"" + Anal(cache) + "\",\"msa\":{\"kind\":\"" + kind +
            "\",\"category\":\"" + verb.Value + "\",\"exceptionFeatures\":" + features + "}}";
        AuthorLexicalEntryIntent Parse(string json) =>
            AuthorLexicalEntryIntentParser.Parse(JsonDocument.Parse(json).RootElement);

        var created = ApplyOps(cache, AuthorLexicalEntryComposer.Build(cache, Parse(Json("stem", "[\"" + loan.Value + "\"]"))));
        var msa = Assert.IsAssignableFrom<IMoStemMsa>(Get<IMoMorphSynAnalysis>(cache,
            Created(created, LexEntryMorphoSyntaxAnalysesOperationKinds.Create)));
        Assert.Equal([loan.ToGuid()], msa.ProdRestrictRC.Select(item => item.Guid));

        Assert.Contains("not one of the project's exception features", Assert.Throws<InvalidOperationException>(() =>
            AuthorLexicalEntryComposer.Build(cache, Parse(Json("stem", "[\"" + CanonicalId.Mint().Value + "\"]")))).Message);
        Assert.Contains("duplicate ids", Assert.Throws<SIL.Motif.Contract.Parsing.ContractParseException>(() =>
            Parse(Json("stem", "[\"" + loan.Value + "\",\"" + loan.Value + "\"]"))).Message);
        Assert.Contains("only a stem msa takes 'exceptionFeatures'", Assert.Throws<SIL.Motif.Contract.Parsing.ContractParseException>(() =>
            Parse(Json("inflectional", "[\"" + loan.Value + "\"]"))).Message);
    }
}
