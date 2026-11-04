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
/// Whether one Verb-only nasal-substitution prefix whose lexeme form is a plain affix allomorph, with affix-process
/// alternates ahead of it, is parsed by the real parser the way a linguist reads it; and whether deleting an old
/// duplicate entry leaves every other word's readings unchanged. The language is synthetic and Cebuano-shaped.
/// </summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class RealParserMixedAllomorphTests(PristineProjectFixture pristine, ITestOutputHelper output)
{
    private static readonly string[] Letters = ["p", "b", "t", "d", "s", "k", "g", "m", "n", "ng", "a", "i", "u"];

    [RealParserFact]
    public async Task APlainElsewhereFormBehindTwoProcessFormsParsesTheSubstitutedShapesAndAReplacedEntryLeavesOtherWordsAlone()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, Letters);
        var ws = Anal(cache);
        var verb = Created(ApplyOps(cache, AuthorPartOfSpeechComposer.Build(cache,
            new AuthorPartOfSpeechIntent("Verb", ws, "V"))), CmPossibilityListPossibilitiesOperationKinds.Create);
        var p = Phonemes(cache);
        var labials = ClassOf(cache, "Labial", "L", p["p"], p["b"]);
        var coronals = ClassOf(cache, "Coronal", "T", p["t"], p["d"], p["s"]);
        var slot = Created(ApplyOps(cache, AuthorAffixSlotComposer.Build(cache,
            new AuthorAffixSlotIntent(verb, "Voice-aspect", ws, false))), PartOfSpeechAffixSlotsOperationKinds.Create);
        ApplyOps(cache, AuthorAffixTemplateComposer.Build(cache,
            new AuthorAffixTemplateIntent(verb, "Verb template", ws, [slot], [], true)));
        var names = new Dictionary<CanonicalId, string>(CanonicalId.ByGuid);
        foreach (var stem in new[] { "palit", "dagat", "sagat", "tindug", "kuha", "kaun", "gisa" })
            names[Form(ApplyOps(cache, AuthorLexicalEntryComposer.Build(cache, new AuthorLexicalEntryIntent(stem,
                Vern(cache), CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphStem), stem, ws,
                new LexicalEntryMsa(LexicalEntryMsaKind.Stem, verb)))))] = stem;
        var prefixType = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphPrefix);
        LexicalEntryMsa PrefixMsa() => new(LexicalEntryMsaKind.Inflectional, verb, Slots: [slot]);
        CanonicalId PlainPrefix(string form, string name)
        {
            var created = ApplyOps(cache, AuthorLexicalEntryComposer.Build(cache, new AuthorLexicalEntryIntent(form,
                Vern(cache), prefixType, form, ws, PrefixMsa())));
            names[Form(created)] = name;
            return Created(created, LexDbEntriesOperationKinds.Create);
        }
        PlainPrefix("mi", "mi");
        PlainPrefix("ming", "ming");
        var old = PlainPrefix("pang", "old-pang");

        Assert.Equal(["old-pang+gisa"], await Parse(cache, names, "panggisa"));
        Assert.Equal(["old-pang+palit"], await Parse(cache, names, "pangpalit"));
        Assert.Empty(await Parse(cache, names, "pamalit"));

        // The replacement: a plain elsewhere form, then two substituting process forms ahead of it.
        var entry = PlainPrefix("pang", "paN");
        var delete = DeleteLexicalEntryComposer.Build(cache, new DeleteLexicalEntryIntent(old));
        ApplyOps(cache, delete);
        Assert.False(Exists(cache, old));
        CanonicalId? previous = null;
        foreach (var (input, insert) in new (AffixProcessInputPart, CanonicalId)[]
        {
            (new("c", NaturalClass: labials), p["m"]),
            (new("c", NaturalClass: coronals), p["n"]),
        })
        {
            var proposal = ApplyOps(cache, AuthorAffixProcessAllomorphComposer.Build(cache,
                new AuthorAffixProcessAllomorphIntent(entry, [input, new("rest", Remaining: true)],
                    [new(Insert: [p["p"], p["a"], insert]), new(Copy: "rest")],
                    Placement: previous is { } after ? new Placement(after, null) : null)));
            previous = Created(proposal, LexEntryAlternateFormsCreateOperationKinds.CreateAlternateForms);
            names[previous.Value] = "paN";
        }
        var replaced = Get<ILexEntry>(cache, entry);
        Assert.IsAssignableFrom<IMoAffixAllomorph>(replaced.LexemeFormOA);
        Assert.Equal(2, replaced.AlternateFormsOS.Count);
        Assert.All(replaced.AlternateFormsOS, form => Assert.IsAssignableFrom<IMoAffixProcess>(form));

        Assert.Equal(["paN+palit"], await Parse(cache, names, "pamalit"));
        Assert.Contains("paN+dagat", await Parse(cache, names, "panagat"));
        Assert.Equal(["paN+kuha"], await Parse(cache, names, "pangkuha"));
        Assert.Equal(["paN+gisa"], await Parse(cache, names, "panggisa"));
        // The substituting forms block the plain elsewhere form for stems they match.
        Assert.Empty(await Parse(cache, names, "pangpalit"));
        Assert.Empty(await Parse(cache, names, "pangdagat"));
        // Words that never touched the replaced entry read as they did before.
        Assert.Equal(["ming+kaun"], await Parse(cache, names, "mingkaun"));
        Assert.Equal(["mi+kaun"], await Parse(cache, names, "mikaun"));
    }

    private async Task<string[]> Parse(LcmCache cache, IReadOnlyDictionary<CanonicalId, string> names, string word)
    {
        new FwDataProjectLoader().Save(cache);
        using var invoker = new PanGlossInvoker();
        return await Readings(new PanGlossTracer(invoker), cache.ProjectId.Path, word, names);
    }

    private static CanonicalId Form(Proposal proposal) =>
        Created(proposal, LexEntryLexemeFormOperationKinds.CreateLexemeForm);

    private static CanonicalId ClassOf(LcmCache cache, string name, string abbreviation, params CanonicalId[] members) =>
        Created(ApplyOps(cache, AuthorNaturalClassComposer.Build(cache,
            new AuthorNaturalClassIntent(name, abbreviation, members))), PhPhonDataNaturalClassesOperationKinds.Create);

    private async Task<string[]> Readings(PanGlossTracer tracer, string path, string word,
        IReadOnlyDictionary<CanonicalId, string> names)
    {
        var outcome = Assert.IsType<PanGlossTraceOutcome.Completed>(
            await tracer.TraceAsync(path, word, CancellationToken.None));
        var details = outcome.Details!;
        Assert.True(details.SearchCompleted, word);
        Assert.False(details.Capped || details.TimedOut || details.InvalidShape, word);
        var readings = outcome.Document!.Analyses.Select(analysis => string.Join("+", analysis.Morphs.Select(morph =>
            names.TryGetValue(CanonicalId.FromGuid(Guid.Parse(morph.FormId!)), out var name) ? name : "?" + morph.Form)))
            .Distinct().Order(StringComparer.Ordinal).ToArray();
        output.WriteLine($"{word}: {(readings.Length == 0 ? "no analysis" : string.Join(" | ", readings))}");
        return readings;
    }

    private static Dictionary<string, CanonicalId> Phonemes(LcmCache cache) =>
        cache.LangProject.PhonologicalDataOA.PhonemeSetsOS[0].PhonemesOC
            .SelectMany(phoneme => phoneme.CodesOS.Select(code =>
                (Text: code.Representation.get_String(cache.DefaultVernWs)?.Text, Phoneme: phoneme)))
            .Where(item => item.Text is not null)
            .ToDictionary(item => item.Text!, item => CanonicalId.FromGuid(item.Phoneme.Guid), StringComparer.Ordinal);
}
