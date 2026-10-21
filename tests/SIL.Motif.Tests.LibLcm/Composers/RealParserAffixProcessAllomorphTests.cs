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
/// Whether one Verb-only nasal-substitution prefix with several process forms, added through the allomorph
/// composer, is parsed by the real parser the way a linguist reads it. The language is synthetic and
/// Cebuano-shaped; the stems are chosen so the substituted and unsubstituted shapes can be told apart.
/// </summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class RealParserAffixProcessAllomorphTests(PristineProjectFixture pristine, ITestOutputHelper output)
{
    private static readonly string[] Letters = ["p", "b", "t", "d", "s", "k", "g", "m", "n", "ng", "a", "i", "u"];

    [RealParserFact]
    public async Task OneVerbOnlyNasalPrefixWithFourProcessFormsParsesTheSubstitutedShapesAndKeepsTheControlsOut()
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
        foreach (var prefix in new[] { "ming", "mi" })
            names[Form(ApplyOps(cache, AuthorLexicalEntryComposer.Build(cache, new AuthorLexicalEntryIntent(prefix,
                Vern(cache), CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphPrefix), prefix, ws,
                new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, verb, Slots: [slot])))))] = prefix;

        var prefixType = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphPrefix);
        var created = ApplyOps(cache, AuthorAffixProcessComposer.Build(cache, new AuthorAffixProcessIntent(prefixType,
            "nasal", ws, new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, verb, Slots: [slot]),
            [new("rest", Remaining: true)],
            [new(Insert: [p["p"], p["a"], p["ng"]]), new(Copy: "rest")])));
        var entry = Created(created, LexDbEntriesOperationKinds.Create);
        names[Form(created)] = "paN";
        var entries = cache.LangProject.LexDbOA.Entries.Count();
        var senses = cache.LangProject.LexDbOA.Entries.Sum(item => item.SensesOS.Count);
        CanonicalId? previous = null;
        foreach (var (input, insert) in new (AffixProcessInputPart, CanonicalId)[]
        {
            (new("c", NaturalClass: labials), p["m"]),
            (new("c", NaturalClass: coronals), p["n"]),
            (new("c", Phoneme: p["k"]), p["ng"]),
        })
        {
            var proposal = ApplyOps(cache, AuthorAffixProcessAllomorphComposer.Build(cache,
                new AuthorAffixProcessAllomorphIntent(entry, [input, new("rest", Remaining: true)],
                    [new(Insert: [p["p"], p["a"], insert]), new(Copy: "rest")],
                    Placement: previous is { } after ? new Placement(after, null) : null)));
            previous = Created(proposal, LexEntryAlternateFormsCreateOperationKinds.CreateAlternateForms);
            names[previous.Value] = "paN";
        }
        Assert.Equal(entries, cache.LangProject.LexDbOA.Entries.Count());
        Assert.Equal(senses, cache.LangProject.LexDbOA.Entries.Sum(item => item.SensesOS.Count));
        var nasalEntries = cache.LangProject.LexDbOA.Entries.Where(item => item.LexemeFormOA is IMoAffixProcess).ToArray();
        Assert.Equal(entry.ToGuid(), Assert.Single(nasalEntries).Guid);
        Assert.Equal(3, nasalEntries[0].AlternateFormsOS.Count);
        Assert.All(nasalEntries[0].AlternateFormsOS, form => Assert.IsAssignableFrom<IMoAffixProcess>(form));

        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);
        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);

        Assert.Equal(["paN+palit"], await Readings(tracer, path, "pamalit", names));
        Assert.Contains("paN+dagat", await Readings(tracer, path, "panagat", names));
        Assert.Equal(["ming+kaun"], await Readings(tracer, path, "mingkaun", names));
        Assert.Equal(["mi+kaun"], await Readings(tracer, path, "mikaun", names));
        Assert.Empty(await Readings(tracer, path, "pangalit", names));
        Assert.Equal(["paN+kuha"], await Readings(tracer, path, "panguha", names));
        // A form earlier in the order blocks the elsewhere form for stems it matches; gisa matches none of them.
        Assert.Empty(await Readings(tracer, path, "pangpalit", names));
        Assert.Equal(["paN+gisa"], await Readings(tracer, path, "panggisa", names));
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
