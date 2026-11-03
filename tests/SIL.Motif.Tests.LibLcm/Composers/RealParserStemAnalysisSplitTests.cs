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

/// <summary>Checks with PanGloss that a stem split by exception feature yields both outcomes of two bleeding rules.</summary>
[Collection(LcmCacheParallelCollections.Group3)]
[Trait("MotifTestLevel", "System")]
public sealed class RealParserStemAnalysisSplitTests(PristineProjectFixture pristine, ITestOutputHelper output)
{
    private const string Version = "1.0";
    private static readonly string[] Letters = ["h", "a", "g", "d", "r", "u", "n"];

    [RealParserFact]
    public async Task OneStemTaggedForEachOfTwoBleedingRulesYieldsBothFormsOnTheComposerPath()
    {
        using var cache = pristine.NewScratch();
        var setup = Prepare(cache);
        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);
        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);

        // One analysis carrying both tags: syncope runs first and bleeds the d-to-r rule, so only hagdun parses.
        Assert.Equal(["hagad+un"], await Readings(tracer, path, "hagdun", setup.Names));
        Assert.Empty(await Readings(tracer, path, "hagarun", setup.Names));

        ApplyOps(cache, EditExceptionFeaturesComposer.Build(cache, new(setup.StemMsa, [setup.Syncope])));
        ApplyOps(cache, AddMorphoSyntaxAnalysisComposer.Build(cache, new(setup.StemEntry,
            new LexicalEntryMsa(LexicalEntryMsaKind.Stem, setup.Verb, ExceptionFeatures: [setup.Rhotacism]),
            Gloss: "hagad (rhotacised)", AnalysisWritingSystem: Anal(cache))));
        new FwDataProjectLoader().Save(cache);

        Assert.Equal(["hagad+un"], await Readings(tracer, path, "hagdun", setup.Names));
        Assert.Equal(["hagad+un"], await Readings(tracer, path, "hagarun", setup.Names));
        Assert.Empty(await Readings(tracer, path, "hagadun", setup.Names));
    }

    [RealParserFact]
    public async Task TheSameSplitStagedAsADraftPassesTheDryRunAndParsesBothFormsAfterApply()
    {
        string path;
        Setup setup;
        using (var cache = pristine.NewScratch())
        {
            setup = Prepare(cache);
            path = cache.ProjectId.Path;
            new FwDataProjectLoader().Save(cache);
        }
        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);
        Assert.Equal(["hagad+un"], await Readings(tracer, path, "hagdun", setup.Names));
        Assert.Empty(await Readings(tracer, path, "hagarun", setup.Names));

        const string draft = "split-hagad";
        Assert.True(ProposalCommands.New(new(path, Version, draft, "Split hagad by exception feature")).Succeeded);
        var retag = ProposalCommands.ComposeIntent(new(path, Version, draft, "compose-edit-exception-features",
            JsonSerializer.Serialize(new { msa = setup.StemMsa.Value, exceptionFeatures = new[] { setup.Syncope.Value } })));
        Assert.True(retag.Succeeded, retag.Refusal?.Message);
        var add = ProposalCommands.ComposeIntent(new(path, Version, draft, "compose-add-morphosyntax-analysis",
            JsonSerializer.Serialize(new
            {
                entry = setup.StemEntry.Value,
                msa = new
                {
                    kind = "stem", category = setup.Verb.Value, exceptionFeatures = new[] { setup.Rhotacism.Value },
                },
                gloss = "hagad (rhotacised)", analysisWs = setup.AnalysisWs,
            })));
        Assert.True(add.Succeeded, add.Refusal?.Message);
        Assert.Contains(add.Value!.Operations, item => item.Kind == MoStemMsaProdRestrictOperationKinds.AddRefProdRestrict);

        DraftRationale.Author(path, draft, "Split hagad", "hagad surfaces both as hagdun and as hagarun.");
        var finalized = ProposalCommands.Finalize(new(path, Version, draft));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        var dryRun = DryRunJobRunner.Run(path, Version, finalized.Value!.ProposalId);
        Assert.True(dryRun.Succeeded, dryRun.Refusal?.Message);
        var applied = ProposalCommands.Apply(new(path, Version, finalized.Value.ProposalId, Force: true));
        Assert.True(applied.Succeeded, applied.Refusal?.Message);

        Assert.Equal(["hagad+un"], await Readings(tracer, path, "hagdun", setup.Names));
        Assert.Equal(["hagad+un"], await Readings(tracer, path, "hagarun", setup.Names));
    }

    private static CanonicalId Feature(LcmCache cache, string name, string abbreviation) =>
        Created(ApplyOps(cache, AuthorExceptionFeatureComposer.Build(cache, new(name, Anal(cache), abbreviation))),
            CmPossibilityListPossibilitiesOperationKinds.Create);

    private sealed record Setup(CanonicalId Verb, CanonicalId StemEntry, CanonicalId StemMsa, CanonicalId Syncope,
        CanonicalId Rhotacism, string AnalysisWs, IReadOnlyDictionary<CanonicalId, string> Names);

    /// <summary>hagad+un, syncope before d-to-r, each rule requiring its own tag, the stem carrying both.</summary>
    private static Setup Prepare(LcmCache cache)
    {
        RealParserProject.PrepareForParsing(cache, Letters);
        var ws = Anal(cache);
        var verb = AddCategory(cache, "Verb");
        var names = new Dictionary<CanonicalId, string>(CanonicalId.ByGuid);
        var stem = AddStem(cache, "hagad", verb);
        names[stem.Form] = "hagad";
        var suffix = ApplyOps(cache, AuthorLexicalEntryComposer.Build(cache, new AuthorLexicalEntryIntent("un",
            Vern(cache), MorphType(MoMorphTypeTags.kguidMorphSuffix), "un", ws,
            new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, verb))));
        names[Created(suffix, LexEntryLexemeFormOperationKinds.CreateLexemeForm)] = "un";
        var syncope = Feature(cache, "Syncope", "sy");
        var rhotacism = Feature(cache, "Rhotacism", "rh");
        var syncopeRule = Created(ApplyOps(cache, AuthorPhonologicalRuleComposer.Build(cache, new("a is lost between g and d",
            PhonologicalRuleDirection.Simultaneous, [new(Phoneme: Phoneme(cache, "a"))], [],
            [new(Phoneme: Phoneme(cache, "g"))], [new(Phoneme: Phoneme(cache, "d"))]))),
            PhPhonDataPhonRulesOperationKinds.Create);
        var rhotacismRule = Created(ApplyOps(cache, AuthorPhonologicalRuleComposer.Build(cache, new("d becomes r between a and u",
            PhonologicalRuleDirection.Simultaneous, [new(Phoneme: Phoneme(cache, "d"))],
            [new(Phoneme: Phoneme(cache, "r"))], [new(Phoneme: Phoneme(cache, "a"))],
            [new(Phoneme: Phoneme(cache, "u"))], new Placement(syncopeRule, null)))),
            PhPhonDataPhonRulesOperationKinds.Create);
        ApplyOps(cache, EditPhonologicalRuleFeaturesComposer.Build(cache, new(syncopeRule, RequiredFeatures: [syncope])));
        ApplyOps(cache, EditPhonologicalRuleFeaturesComposer.Build(cache, new(rhotacismRule, RequiredFeatures: [rhotacism])));
        ApplyOps(cache, EditExceptionFeaturesComposer.Build(cache, new(stem.Msa, [syncope, rhotacism])));
        return new Setup(verb, stem.Entry, stem.Msa, syncope, rhotacism, ws, names);
    }

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

    private static CanonicalId Phoneme(LcmCache cache, string text) =>
        CanonicalId.FromGuid(cache.LangProject.PhonologicalDataOA.PhonemeSetsOS[0].PhonemesOC
            .SelectMany(phoneme => phoneme.CodesOS.Select(code => (Code: code, Phoneme: phoneme)))
            .Single(pair => pair.Code.Representation.get_String(cache.DefaultVernWs)?.Text == text).Phoneme.Guid);
}
