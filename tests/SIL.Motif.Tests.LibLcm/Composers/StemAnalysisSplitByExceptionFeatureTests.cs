using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;
using static SIL.Motif.Tests.Composers.AffixTestSupport;

namespace SIL.Motif.Tests.Composers;

/// <summary>A stem whose two analyses differ only by exception features, as LibLCM holds them.</summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class StemAnalysisSplitByExceptionFeatureTests(PristineProjectFixture pristine)
{

    [Fact]
    public void TwoStemAnalysesThatDifferOnlyByExceptionFeaturesAreDistinctAndTheSameSetIsRefused()
    {
        using var cache = pristine.NewScratch();
        var ws = Anal(cache);
        var verb = AddCategory(cache, "Verb");
        var stem = AddStem(cache, "hagad", verb);
        var first = Feature(cache, "Syncope", "sy");
        var second = Feature(cache, "Rhotacism", "rh");

        var plain = Assert.Throws<InvalidOperationException>(() => AddMorphoSyntaxAnalysisComposer.Build(cache, new(
            stem.Entry, new LexicalEntryMsa(LexicalEntryMsaKind.Stem, verb), Gloss: "plain", AnalysisWritingSystem: ws)));
        Assert.Contains("identical analysis", plain.Message, StringComparison.Ordinal);
        ApplyOps(cache, EditExceptionFeaturesComposer.Build(cache, new(stem.Msa, [first])));
        var added = ApplyOps(cache, AddMorphoSyntaxAnalysisComposer.Build(cache, new(stem.Entry,
            new LexicalEntryMsa(LexicalEntryMsaKind.Stem, verb, ExceptionFeatures: [second]),
            Gloss: "hagad (rhotacised)", AnalysisWritingSystem: ws)));
        var created = Created(added, LexEntryMorphoSyntaxAnalysesOperationKinds.Create);

        var entry = Get<ILexEntry>(cache, stem.Entry);
        Assert.Equal(2, entry.MorphoSyntaxAnalysesOC.Count);
        Assert.Equal([second.ToGuid()], Get<IMoStemMsa>(cache, created).ProdRestrictRC.Select(item => item.Guid));
        Assert.Equal([first.ToGuid()], Get<IMoStemMsa>(cache, stem.Msa).ProdRestrictRC.Select(item => item.Guid));
        Assert.Equal(created.ToGuid(), entry.SensesOS[1].MorphoSyntaxAnalysisRA.Guid);

        var again = Assert.Throws<InvalidOperationException>(() => AddMorphoSyntaxAnalysisComposer.Build(cache, new(
            stem.Entry, new LexicalEntryMsa(LexicalEntryMsaKind.Stem, verb, ExceptionFeatures: [second]),
            Gloss: "again", AnalysisWritingSystem: ws)));
        Assert.Contains("identical analysis", again.Message, StringComparison.Ordinal);
        var both = AddMorphoSyntaxAnalysisComposer.Build(cache, new(stem.Entry,
            new LexicalEntryMsa(LexicalEntryMsaKind.Stem, verb, ExceptionFeatures: [first, second]),
            Gloss: "both", AnalysisWritingSystem: ws));
        Assert.Equal(2, both.Count(operation => operation.Kind == MoStemMsaProdRestrictOperationKinds.AddRefProdRestrict));
    }

    [Fact]
    public void ExceptionFeaturesBelongToAStemAnalysisAndMustBeProjectFeatures()
    {
        using var cache = pristine.NewScratch();
        var ws = Anal(cache);
        var verb = AddCategory(cache, "Verb");
        var stem = AddStem(cache, "hagad", verb);
        var suffix = ApplyOps(cache, AuthorLexicalEntryComposer.Build(cache, new AuthorLexicalEntryIntent("un",
            Vern(cache), MorphType(MoMorphTypeTags.kguidMorphSuffix), "un", ws,
            new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, verb))));
        var feature = Feature(cache, "Syncope", "sy");

        var onAffix = Assert.Throws<InvalidOperationException>(() => AddMorphoSyntaxAnalysisComposer.Build(cache, new(
            Created(suffix, LexDbEntriesOperationKinds.Create),
            new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, verb, ExceptionFeatures: [feature]),
            Gloss: "x", AnalysisWritingSystem: ws)));
        Assert.Contains("only a stem msa takes 'exceptionFeatures'", onAffix.Message, StringComparison.Ordinal);
        var unknown = Assert.Throws<InvalidOperationException>(() => AddMorphoSyntaxAnalysisComposer.Build(cache, new(
            stem.Entry, new LexicalEntryMsa(LexicalEntryMsaKind.Stem, verb, ExceptionFeatures: [verb]),
            Gloss: "x", AnalysisWritingSystem: ws)));
        Assert.Contains("is not one of the project's exception features", unknown.Message, StringComparison.Ordinal);

        using var document = JsonDocument.Parse($$$"""
            {"entry":"{{{stem.Entry.Value}}}","gloss":"x","analysisWs":"{{{ws}}}",
             "msa":{"kind":"stem","category":"{{{verb.Value}}}","exceptionFeatures":["{{{feature.Value}}}"]}}
            """);
        var parsed = AddMorphoSyntaxAnalysisIntentParser.Parse(document.RootElement);
        Assert.Equal([feature], parsed.Msa.ExceptionFeatures);
        using var wrongKind = JsonDocument.Parse($$$"""
            {"entry":"{{{stem.Entry.Value}}}","gloss":"x","analysisWs":"{{{ws}}}",
             "msa":{"kind":"unclassified","category":"{{{verb.Value}}}","exceptionFeatures":["{{{feature.Value}}}"]}}
            """);
        Assert.Throws<ContractParseException>(() => AddMorphoSyntaxAnalysisIntentParser.Parse(wrongKind.RootElement));
        using var other = JsonDocument.Parse($$$"""
            {"form":"x","formWs":"{{{Vern(cache)}}}","morphType":"{{{MorphType(MoMorphTypeTags.kguidMorphStem).Value}}}",
             "gloss":"x","analysisWs":"{{{ws}}}",
             "msa":{"kind":"stem","category":"{{{verb.Value}}}","exceptionFeatures":["{{{feature.Value}}}"]}}
            """);
        Assert.Throws<ContractParseException>(() => AuthorLexicalEntryIntentParser.Parse(other.RootElement));
    }

    private static CanonicalId Feature(LcmCache cache, string name, string abbreviation) =>
        Created(ApplyOps(cache, AuthorExceptionFeatureComposer.Build(cache, new(name, Anal(cache), abbreviation))),
            CmPossibilityListPossibilitiesOperationKinds.Create);
}
