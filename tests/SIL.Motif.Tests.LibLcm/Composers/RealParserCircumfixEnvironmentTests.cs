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
/// Whether the two halves of a circumfix each keep their own environment on the real parser, so a linker spelled
/// differently after a vowel and after a consonant is chosen by the stem's last sound. The language is synthetic and
/// Cebuano-shaped: ma- plus -ang after a consonant or -x (the velar nasal) after a vowel.
/// </summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class RealParserCircumfixEnvironmentTests(PristineProjectFixture pristine, ITestOutputHelper output)
{
    private static readonly string[] Letters = ["m", "a", "b", "u", "l", "y", "x", "i", "t"];

    [RealParserFact]
    public async Task EachHalfOfACircumfixKeepsItsEnvironmentAndTheLinkerFollowsTheStemFinalSound()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, Letters);
        var ws = Anal(cache);
        var noun = AddCategory(cache, "Noun");
        var adjective = AddCategory(cache, "Adjective");
        var p = Phonemes(cache);
        var consonants = ClassOf(cache, "Consonant", "C", p["m"], p["b"], p["l"], p["y"], p["x"], p["t"]);
        var vowels = ClassOf(cache, "Vowel", "V", p["a"], p["u"], p["i"]);
        CanonicalId Environment(string name, EnvironmentContext[] left, EnvironmentContext[] right) =>
            Created(ApplyOps(cache, AuthorEnvironmentComposer.Build(cache, new AuthorEnvironmentIntent(name, left, right))),
                PhPhonDataEnvironmentsOperationKinds.Create);
        var beforeConsonant = Environment("before consonant", [], [new(NaturalClass: consonants)]);
        var afterConsonant = Environment("after consonant", [new(NaturalClass: consonants)], []);
        var afterVowel = Environment("after vowel", [new(NaturalClass: vowels)], []);

        var names = new Dictionary<CanonicalId, string>(CanonicalId.ByGuid);
        foreach (var stem in new[] { "bul", "luya", "ubat" })
            names[AddStem(cache, stem, noun).Form] = stem;
        void Circumfix(string suffix, string label, CanonicalId suffixEnvironment)
        {
            var proposal = ApplyOps(cache, AuthorCircumfixComposer.Build(cache, new AuthorCircumfixIntent("ma", suffix,
                Vern(cache), label, ws, new LexicalEntryMsa(LexicalEntryMsaKind.Derivational, noun, adjective),
                PrefixEnvironments: [beforeConsonant], SuffixEnvironments: [suffixEnvironment])));
            var halves = proposal.Operations.Where(operation =>
                operation.Kind == LexEntryAlternateFormsCreateOperationKinds.CreateAlternateForms).ToArray();
            Assert.Equal(2, halves.Length);
            names[halves[0].EntityId!.Value] = "ma-";
            names[halves[1].EntityId!.Value] = "-" + suffix;
        }
        Circumfix("ang", "after a consonant", afterConsonant);
        Circumfix("x", "after a vowel", afterVowel);

        Assert.Equal(["ma-+bul+-ang"], await Parse(cache, output, names, "mabulang"));
        Assert.Equal(["ma-+luya+-x"], await Parse(cache, output, names, "maluyax"));
        // Each half is checked on its own: the wrong linker, a missing half and the wrong stem start all fail.
        Assert.Empty(await Parse(cache, output, names, "mabulx"));
        Assert.Empty(await Parse(cache, output, names, "maluyaang"));
        Assert.Empty(await Parse(cache, output, names, "mabul"));
        Assert.Empty(await Parse(cache, output, names, "bulang"));
        Assert.Empty(await Parse(cache, output, names, "maubatang"));
    }
}
