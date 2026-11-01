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

/// <summary>Checks with PanGloss that changing an affix's kind in place changes how it parses and flips back.</summary>
[Collection(LcmCacheParallelCollections.Group3)]
[Trait("MotifTestLevel", "System")]
public sealed class RealParserChangeAffixKindTests(PristineProjectFixture pristine, ITestOutputHelper output)
{
    private static readonly string[] Letters = ["p", "a", "g", "k", "u", "n", "l", "i", "t"];

    [RealParserFact]
    public async Task TheSameReduplicatingAffixParsesAsDerivationalAndAsInflectionalInASlotAndFlipsBack()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, Letters);
        var ws = Anal(cache);
        var verb = Created(ApplyOps(cache, AuthorPartOfSpeechComposer.Build(cache,
            new AuthorPartOfSpeechIntent("Verb", ws, "V"))), CmPossibilityListPossibilitiesOperationKinds.Create);
        var slot = Created(ApplyOps(cache, AuthorAffixSlotComposer.Build(cache,
            new AuthorAffixSlotIntent(verb, "Outer", ws, true))), PartOfSpeechAffixSlotsOperationKinds.Create);
        ApplyOps(cache, AuthorAffixTemplateComposer.Build(cache,
            new AuthorAffixTemplateIntent(verb, "Verb template", ws, [slot], [], true)));
        var names = new Dictionary<CanonicalId, string>(CanonicalId.ByGuid);
        names[AddStem(cache, "kaun", verb).Form] = "kaun";
        var created = ApplyOps(cache, AuthorAffixProcessComposer.Build(cache, new AuthorAffixProcessIntent(
            MorphType(MoMorphTypeTags.kguidMorphPrefix), "repeat", ws,
            new LexicalEntryMsa(LexicalEntryMsaKind.Derivational, verb, verb),
            [new("rest", Remaining: true)], [new(Copy: "rest"), new(Copy: "rest")])));
        var info = Created(created, LexEntryMorphoSyntaxAnalysesOperationKinds.Create);
        names[Created(created, LexEntryLexemeFormOperationKinds.CreateLexemeForm)] = "red";
        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);
        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);

        Assert.Equal(["red+kaun"], await Readings(tracer, path, "kaunkaun", names));

        info = Flip(cache, new ChangeAffixKindIntent(info, LexicalEntryMsaKind.Inflectional, Slots: [slot]));
        new FwDataProjectLoader().Save(cache);
        Assert.Equal([slot.ToGuid()], Get<IMoInflAffMsa>(cache, info).SlotsRC.Select(item => item.Guid));
        Assert.Equal(["red+kaun"], await Readings(tracer, path, "kaunkaun", names));

        info = Flip(cache, new ChangeAffixKindIntent(info, LexicalEntryMsaKind.Derivational));
        new FwDataProjectLoader().Save(cache);
        Assert.IsAssignableFrom<IMoDerivAffMsa>(Get<IMoMorphSynAnalysis>(cache, info));
        Assert.Equal(["red+kaun"], await Readings(tracer, path, "kaunkaun", names));
    }

    private static CanonicalId Flip(LcmCache cache, ChangeAffixKindIntent intent) =>
        Created(ApplyOps(cache, ChangeAffixKindComposer.Build(cache, intent)), LexEntryMorphoSyntaxAnalysesOperationKinds.Create);

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
}
