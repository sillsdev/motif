using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Composers;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class SetNotOnCliticsParserTests(PristineProjectFixture pristine)
{
    [RealParserFact]
    public async Task ARuleChangesAnEncliticOnlyAfterTheProjectRunsRulesOnTheClitic()
    {
        using var cache = pristine.NewScratch();
        var operations = new List<OperationEnvelope>();
        var (first, second) = AddFeatures(cache);
        var phonemes = new Dictionary<string, CanonicalId>();
        foreach (var (letter, features) in new[]
                 {
                     ("k", new[] { first.Plus, second.Plus }), ("a", new[] { first.Minus, second.Plus }),
                     ("n", new[] { first.Plus, second.Minus }), ("m", new[] { first.Minus, second.Minus }),
                 })
            phonemes.Add(letter, SoundSystemComposerTests.Compose(cache, operations,
                () => AuthorPhonemeComposer.Build(cache, new(letter, [letter], features))));
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            cache.LangProject.MorphologicalDataOA.ParserParameters =
                "<ParserParameters><HC><NoDefaultCompounding>true</NoDefaultCompounding><Strata /></HC></ParserParameters>";
            OwnedBoundaryMarkerFixture.EnsureReservedMarkers(cache, cache.LangProject.PhonologicalDataOA.PhonemeSetsOS.Single());
        });
        SoundSystemComposerTests.Execute(cache, AuthorPhonologicalRuleComposer.Build(cache,
            new AuthorPhonologicalRuleIntent("n becomes m after a", PhonologicalRuleDirection.Simultaneous,
                [new(Phoneme: phonemes["n"])], [new(Phoneme: phonemes["m"])],
                [new(Phoneme: phonemes["a"])], [])));

        var ws = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs);
        var stemType = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphStem);
        SoundSystemComposerTests.Execute(cache, AuthorLexemeFormComposer.Build(cache,
            new(CanonicalId.FromGuid(pristine.Seed.FirstEntryId), stemType, ws, "ka")));
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create(
                cache.ServiceLocator.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphEnclitic),
                TsStringUtils.MakeString("n", cache.DefaultVernWs), "clitic", new SandboxGenericMSA { MsaType = MsaType.kStem }));
        new FwDataProjectLoader().Save(cache);

        using var invoker = new PanGlossInvoker();
        var before = await ParseAsync(invoker, cache, "kan", "kam");
        Assert.Equal(WordOutcome.Analysed, before["kan"]);
        Assert.Equal(WordOutcome.NoAnalysis, before["kam"]);

        SoundSystemComposerTests.Execute(cache, SetNotOnCliticsComposer.Build(cache, new(true, false)));
        new FwDataProjectLoader().Save(cache);

        var after = await ParseAsync(invoker, cache, "kan", "kam");
        Assert.Equal(WordOutcome.NoAnalysis, after["kan"]);
        Assert.Equal(WordOutcome.Analysed, after["kam"]);
    }

    private static async Task<Dictionary<string, WordOutcome>> ParseAsync(
        PanGlossInvoker invoker, LcmCache cache, params string[] words)
    {
        var outcome = await invoker.RunAsync(new PanGlossRequest.Batch(cache.ProjectId.Path, words,
            TimeSpan.FromSeconds(10)), "not on clitics", CancellationToken.None);
        var completed = Assert.IsType<PanGlossOutcome.Completed>(outcome);
        return BatchTsvParser.Parse(completed.Output).ToDictionary(row => row.Word, row => row.Outcome);
    }

    private static (Feature First, Feature Second) AddFeatures(LcmCache cache)
    {
        var made = new List<(IFsClosedFeature Feature, IFsSymFeatVal Plus, IFsSymFeatVal Minus)>();
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            foreach (var name in new[] { "First segment feature", "Second segment feature" })
            {
                var feature = cache.ServiceLocator.GetInstance<IFsClosedFeatureFactory>().Create();
                cache.LangProject.PhFeatureSystemOA.FeaturesOC.Add(feature);
                feature.Name.set_String(cache.DefaultAnalWs, name);
                var plus = cache.ServiceLocator.GetInstance<IFsSymFeatValFactory>().Create();
                var minus = cache.ServiceLocator.GetInstance<IFsSymFeatValFactory>().Create();
                feature.ValuesOC.Add(plus);
                feature.ValuesOC.Add(minus);
                plus.Name.set_String(cache.DefaultAnalWs, "positive");
                minus.Name.set_String(cache.DefaultAnalWs, "negative");
                made.Add((feature, plus, minus));
            }
        });
        Feature Of((IFsClosedFeature Feature, IFsSymFeatVal Plus, IFsSymFeatVal Minus) item) => new(
            new(CanonicalId.FromGuid(item.Feature.Guid), CanonicalId.FromGuid(item.Plus.Guid)),
            new(CanonicalId.FromGuid(item.Feature.Guid), CanonicalId.FromGuid(item.Minus.Guid)));
        return (Of(made[0]), Of(made[1]));
    }

    private sealed record Feature(PhonologicalFeatureValue Plus, PhonologicalFeatureValue Minus);
}
