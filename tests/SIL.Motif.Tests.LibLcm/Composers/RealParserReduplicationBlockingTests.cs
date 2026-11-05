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
/// Why a shape-restricted solid reduplication form added to the entry of an unrestricted hyphen form made hyphenated
/// words vanish (the loss round 7 saw), and that putting the solid form in its own entry does not. The language is
/// synthetic; C is the consonant class and V the vowel class, and the restricted form matches CVC stems only.
/// </summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class RealParserReduplicationBlockingTests(PristineProjectFixture pristine, ITestOutputHelper output)
{
    private static readonly string[] Letters = ["p", "l", "k", "n", "a", "u", "o", "-"];

    private (LcmCache Cache, Dictionary<CanonicalId, string> Names, CanonicalId Entry, CanonicalId Category,
        AffixProcessInputPart[] Shape, AffixProcessOutputStep[] Solid) Build()
    {
        var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, Letters);
        var ws = Anal(cache);
        var verb = Created(ApplyOps(cache, AuthorPartOfSpeechComposer.Build(cache,
            new AuthorPartOfSpeechIntent("Verb", ws, "V"))), CmPossibilityListPossibilitiesOperationKinds.Create);
        var p = Phonemes(cache);
        var consonants = ClassOf(cache, "Consonant", "C", p["p"], p["l"], p["k"], p["n"]);
        var vowels = ClassOf(cache, "Vowel", "V", p["a"], p["u"], p["o"]);
        var names = new Dictionary<CanonicalId, string>(CanonicalId.ByGuid);
        foreach (var stem in new[] { "pul", "kaun" })
            names[AddStem(cache, stem, verb).Form] = stem;
        var hyphen = ApplyOps(cache, AuthorAffixProcessComposer.Build(cache, new AuthorAffixProcessIntent(
            MorphType(MoMorphTypeTags.kguidMorphPrefix), "red", ws,
            new LexicalEntryMsa(LexicalEntryMsaKind.Derivational, verb, verb),
            [new("rest", Remaining: true)], [new(Copy: "rest"), new(Insert: [p["-"]]), new(Copy: "rest")])));
        names[Created(hyphen, LexEntryLexemeFormOperationKinds.CreateLexemeForm)] = "red";
        AffixProcessInputPart[] shape = [new("c1", NaturalClass: consonants), new("v", NaturalClass: vowels), new("c2", NaturalClass: consonants)];
        AffixProcessOutputStep[] solid = [new(Copy: "c1"), new(Copy: "v"), new(Copy: "c2"), new(Copy: "c1"), new(Copy: "v"), new(Copy: "c2")];
        return (cache, names, Created(hyphen, LexDbEntriesOperationKinds.Create), verb, shape, solid);
    }

    [RealParserFact]
    public async Task ARestrictedSolidFormAheadOfTheUnrestrictedHyphenFormBlocksTheHyphenSpellingForStemsItMatches()
    {
        var (cache, names, entry, _, shape, solid) = Build();
        using var _c = cache;
        var added = ApplyOps(cache, AuthorAffixProcessAllomorphComposer.Build(cache,
            new AuthorAffixProcessAllomorphIntent(entry, shape, solid)));
        names[Created(added, LexEntryAlternateFormsCreateOperationKinds.CreateAlternateForms)] = "red";
        // Default placement puts the restricted form ahead of the unrestricted lexeme form.
        Assert.Single(Get<ILexEntry>(cache, entry).AlternateFormsOS);

        Assert.Equal(["red+pul"], await Parse(cache, output, names, "pulpul"));
        Assert.Empty(await Parse(cache, output, names, "pul-pul"));
        // A stem the shape does not match keeps its hyphen reading.
        Assert.Equal(["red+kaun"], await Parse(cache, output, names, "kaun-kaun"));
        Assert.Empty(await Parse(cache, output, names, "kaunkaun"));
    }

    [RealParserFact]
    public async Task TheSameRestrictedSolidFormInItsOwnEntryLeavesTheHyphenSpellingAlone()
    {
        var (cache, names, _, verb, shape, solid) = Build();
        using var _c = cache;
        var created = ApplyOps(cache, AuthorAffixProcessComposer.Build(cache, new AuthorAffixProcessIntent(
            MorphType(MoMorphTypeTags.kguidMorphPrefix), "red-solid", Anal(cache),
            new LexicalEntryMsa(LexicalEntryMsaKind.Derivational, verb, verb), shape, solid)));
        names[Created(created, LexEntryLexemeFormOperationKinds.CreateLexemeForm)] = "red-solid";

        Assert.Equal(["red-solid+pul"], await Parse(cache, output, names, "pulpul"));
        Assert.Equal(["red+pul"], await Parse(cache, output, names, "pul-pul"));
        Assert.Equal(["red+kaun"], await Parse(cache, output, names, "kaun-kaun"));
        Assert.Empty(await Parse(cache, output, names, "kaunkaun"));
    }
}
