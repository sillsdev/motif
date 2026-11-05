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
/// Whether solid whole-stem reduplication of a verb stem, written as one word, parses inside a verb template: the
/// subject and tense prefixes surround the reduplicated stem and the copy must be exact. The language is synthetic
/// and Swahili-shaped (a-li-pikapika).
/// </summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class RealParserSwahiliReduplicationTests(PristineProjectFixture pristine, ITestOutputHelper output)
{
    private static readonly string[] Letters = ["p", "i", "k", "a", "s", "o", "m", "l", "u"];

    [RealParserFact]
    public async Task SolidWholeStemCopyParsesBetweenTheSubjectAndTensePrefixesAndTheCopyMustBeExact()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, Letters);
        var ws = Anal(cache);
        var verb = Created(ApplyOps(cache, AuthorPartOfSpeechComposer.Build(cache,
            new AuthorPartOfSpeechIntent("Verb", ws, "V"))), CmPossibilityListPossibilitiesOperationKinds.Create);
        var subject = Created(ApplyOps(cache, AuthorAffixSlotComposer.Build(cache,
            new AuthorAffixSlotIntent(verb, "Subject", ws, true))), PartOfSpeechAffixSlotsOperationKinds.Create);
        var tense = Created(ApplyOps(cache, AuthorAffixSlotComposer.Build(cache,
            new AuthorAffixSlotIntent(verb, "Tense", ws, true))), PartOfSpeechAffixSlotsOperationKinds.Create);
        ApplyOps(cache, AuthorAffixTemplateComposer.Build(cache,
            new AuthorAffixTemplateIntent(verb, "Verb template", ws, [subject, tense], [], true)));
        var names = new Dictionary<CanonicalId, string>(CanonicalId.ByGuid);
        foreach (var stem in new[] { "pika", "soma", "mola" })
            names[AddStem(cache, stem, verb).Form] = stem;
        foreach (var (form, slot) in new[] { ("a", subject), ("li", tense) })
        {
            var created = ApplyOps(cache, AuthorLexicalEntryComposer.Build(cache, new AuthorLexicalEntryIntent(form,
                Vern(cache), MorphType(MoMorphTypeTags.kguidMorphPrefix), form, ws,
                new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, verb, Slots: [slot]))));
            names[Created(created, LexEntryLexemeFormOperationKinds.CreateLexemeForm)] = form;
        }
        var redup = ApplyOps(cache, AuthorAffixProcessComposer.Build(cache, new AuthorAffixProcessIntent(
            MorphType(MoMorphTypeTags.kguidMorphPrefix), "repeatedly", ws,
            new LexicalEntryMsa(LexicalEntryMsaKind.Derivational, verb, verb),
            [new("rest", Remaining: true)], [new(Copy: "rest"), new(Copy: "rest")])));
        names[Created(redup, LexEntryLexemeFormOperationKinds.CreateLexemeForm)] = "RED";

        Assert.Equal(["RED+pika"], await Parse(cache, output, names, "pikapika"));
        Assert.Equal(["a+RED+pika"], await Parse(cache, output, names, "apikapika"));
        Assert.Equal(["a+li+RED+soma"], await Parse(cache, output, names, "alisomasoma"));
        Assert.Equal(["li+RED+mola"], await Parse(cache, output, names, "limolamola"));
        // The plain stem is untouched, and an inexact copy is no reduplication.
        Assert.Equal(["a+li+pika"], await Parse(cache, output, names, "alipika"));
        Assert.Empty(await Parse(cache, output, names, "alipikasoma"));
        Assert.Empty(await Parse(cache, output, names, "alipikapik"));
        Assert.Empty(await Parse(cache, output, names, "alipikapikapika"));
    }
}
