using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;
using static SIL.Motif.Tests.Composers.AffixTestSupport;
using static SIL.Motif.Tests.Composers.RealParserReadings;

namespace SIL.Motif.Tests.Composers;

/// <summary>
/// What limits the hosts of an enclitic: the categories it attaches to do, and an exception feature on the clitic's own
/// stem analysis does not (HermitCrab and PanGloss read a clitic's exception features as features the clitic carries,
/// never as a requirement on the host). The language is synthetic and Swahili-shaped (kinship enclitics).
/// </summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class RealParserCliticHostTests(PristineProjectFixture pristine, ITestOutputHelper output)
{
    private static readonly string[] Letters = ["m", "a", "t", "k", "i", "s", "u"];

    [RealParserFact]
    public async Task AnEncliticAttachesToItsCategoriesAndAnExceptionFeatureOnTheCliticDoesNotChooseTheHost()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, Letters);
        var ws = Anal(cache);
        var kinship = AddCategory(cache, "Kinship noun");
        var verb = AddCategory(cache, "Verb");
        var names = new Dictionary<CanonicalId, string>(CanonicalId.ByGuid);
        var tagged = AddStem(cache, "mama", kinship);
        names[tagged.Form] = "mama";
        names[AddStem(cache, "tata", kinship).Form] = "tata";
        names[AddStem(cache, "kita", verb).Form] = "kita";
        var created = ApplyOps(cache, AuthorLexicalEntryComposer.Build(cache, new AuthorLexicalEntryIntent("si",
            Vern(cache), MorphType(MoMorphTypeTags.kguidMorphEnclitic), "my", ws,
            new LexicalEntryMsa(LexicalEntryMsaKind.Stem, kinship))));
        names[Created(created, LexEntryLexemeFormOperationKinds.CreateLexemeForm)] = "=si";
        var clitic = Created(created, LexEntryMorphoSyntaxAnalysesOperationKinds.Create);

        // A clitic that lists no categories attaches to every host.
        Assert.Equal(["mama+=si"], await Parse(cache, output, names, "mamasi"));
        Assert.Equal(["kita+=si"], await Parse(cache, output, names, "kitasi"));

        ApplyOps(cache, EditCliticAttachmentComposer.Build(cache, new(clitic, [], [kinship])));
        Assert.Equal(["mama+=si"], await Parse(cache, output, names, "mamasi"));
        Assert.Equal(["tata+=si"], await Parse(cache, output, names, "tatasi"));
        Assert.Empty(await Parse(cache, output, names, "kitasi"));

        // The feature sits on the clitic and on one host; the other host still takes the clitic.
        var feature = Created(ApplyOps(cache, AuthorExceptionFeatureComposer.Build(cache,
            new("Takes the clitic", ws, "tc"))), CmPossibilityListPossibilitiesOperationKinds.Create);
        ApplyOps(cache, EditExceptionFeaturesComposer.Build(cache, new(clitic, [feature])));
        ApplyOps(cache, EditExceptionFeaturesComposer.Build(cache, new(tagged.Msa, [feature])));
        Assert.Equal(["mama+=si"], await Parse(cache, output, names, "mamasi"));
        Assert.Equal(["tata+=si"], await Parse(cache, output, names, "tatasi"));
        Assert.Empty(await Parse(cache, output, names, "kitasi"));
    }
}
