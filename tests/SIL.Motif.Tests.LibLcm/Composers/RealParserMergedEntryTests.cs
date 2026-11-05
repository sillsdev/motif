using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
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

/// <summary>
/// Whether two entries for one word, once merged, parse as one entry: the surviving entry reads both spellings,
/// its senses are both kept, and a word of an entry the merge did not touch reads as before. Synthetic language.
/// </summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class RealParserMergedEntryTests(PristineProjectFixture pristine, ITestOutputHelper output)
{
    private static readonly string[] Letters = ["t", "n", "d", "k", "g", "b", "l", "m", "i", "o", "u", "a"];

    [RealParserFact]
    public async Task AMergedSpellingParsesAsTheSurvivingEntryAndOtherWordsAreUnchanged()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, Letters);
        var ws = Anal(cache);
        var verb = Created(ApplyOps(cache, AuthorPartOfSpeechComposer.Build(cache,
            new AuthorPartOfSpeechIntent("Verb", ws, "V"))), CmPossibilityListPossibilitiesOperationKinds.Create);
        var names = new Dictionary<CanonicalId, string>(CanonicalId.ByGuid);
        CanonicalId Stem(string form, string name)
        {
            var created = ApplyOps(cache, AuthorLexicalEntryComposer.Build(cache, new AuthorLexicalEntryIntent(form,
                Vern(cache), CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphStem), form, ws,
                new LexicalEntryMsa(LexicalEntryMsaKind.Stem, verb))));
            names[Created(created, LexEntryLexemeFormOperationKinds.CreateLexemeForm)] = name;
            return Created(created, LexDbEntriesOperationKinds.Create);
        }
        var main = Stem("tindug", "tindug");
        var duplicate = Stem("tindog", "tindug");
        Stem("bulan", "bulan");

        Assert.Equal(["tindug"], await Parse(cache, names, "tindug"));
        Assert.Equal(["tindug"], await Parse(cache, names, "tindog"));
        Assert.Empty(await Parse(cache, names, "tindag"));

        ApplyOps(cache, MergeLexicalEntriesComposer.Build(cache, new MergeLexicalEntriesIntent(duplicate, main)));

        Assert.False(Exists(cache, duplicate));
        var survivor = Get<ILexEntry>(cache, main);
        Assert.Equal(2, survivor.SensesOS.Count);
        Assert.Single(survivor.AlternateFormsOS);
        Assert.Equal(["tindug"], await Parse(cache, names, "tindug"));
        Assert.Equal(["tindug"], await Parse(cache, names, "tindog"));
        Assert.Empty(await Parse(cache, names, "tindag"));
        Assert.Equal(["bulan"], await Parse(cache, names, "bulan"));
    }

    private async Task<string[]> Parse(LcmCache cache, IReadOnlyDictionary<CanonicalId, string> names, string word)
    {
        new FwDataProjectLoader().Save(cache);
        using var invoker = new PanGlossInvoker();
        var outcome = Assert.IsType<PanGlossTraceOutcome.Completed>(
            await new PanGlossTracer(invoker).TraceAsync(cache.ProjectId.Path, word, CancellationToken.None));
        var details = outcome.Details!;
        Assert.True(details.SearchCompleted, word);
        Assert.False(details.Capped || details.TimedOut || details.InvalidShape, word);
        var readings = outcome.Document!.Analyses.Select(analysis => string.Join("+", analysis.Morphs.Select(morph =>
            names.TryGetValue(CanonicalId.FromGuid(Guid.Parse(morph.FormId!)), out var name) ? name : "?" + morph.Form)))
            .Distinct().Order(StringComparer.Ordinal).ToArray();
        output.WriteLine($"{word}: {(readings.Length == 0 ? "no analysis" : string.Join(" | ", readings))}");
        return readings;
    }
}
