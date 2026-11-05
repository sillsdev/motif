using SIL.LCModel;
using SIL.LCModel.Infrastructure;
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

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class RealParserFeatureRuleTests(PristineProjectFixture pristine, ITestOutputHelper output)
{
    [RealParserFact]
    public async Task AlphaPlaceAssimilationAcrossAMorphemeBoundaryPreservesMannerAndRejectsTheWrongNasal()
    {
        using var cache = pristine.NewScratch();
        var f = new FeaturedSegments(cache);
        var category = AddCategory(cache, "Word");
        var names = new Dictionary<CanonicalId, string>(CanonicalId.ByGuid);
        foreach (var stem in new[] { "pa", "ta", "ka" }) names[AddStem(cache, stem, category).Form] = stem;
        var prefix = ApplyOps(cache, AuthorLexicalEntryComposer.Build(cache, new AuthorLexicalEntryIntent("N",
            Vern(cache), MorphType(MoMorphTypeTags.kguidMorphPrefix), "nasal prefix", Anal(cache),
            new LexicalEntryMsa(LexicalEntryMsaKind.Unclassified, category))));
        names[Created(prefix, LexEntryLexemeFormOperationKinds.CreateLexemeForm)] = "N";
        ApplyOps(cache, AuthorPhonologicalRuleComposer.Build(cache, AuthorPhonologicalRuleIntentParser.Parse(f.Assimilation())));
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            cache.LangProject.MorphologicalDataOA.ParserParameters =
                "<ParserParameters><HC><NoDefaultCompounding>true</NoDefaultCompounding><Strata /></HC></ParserParameters>";
            OwnedBoundaryMarkerFixture.EnsureReservedMarkers(cache, cache.LangProject.PhonologicalDataOA.PhonemeSetsOS.Single());
        });
        new FwDataProjectLoader().Save(cache);
        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);
        foreach (var (word, expected) in new[] { ("mpa", "N+pa"), ("nta", "N+ta"), ("ŋka", "N+ka"), ("pa", "pa") })
            Assert.Contains(expected, await Readings(word));
        foreach (var word in new[] { "npa", "mta", "ŋpa", "nka", "mka", "ŋta", "Npa", "Nta", "Nka" })
            Assert.Empty(await Readings(word));

        async Task<string[]> Readings(string word)
        {
            var outcome = Assert.IsType<PanGlossTraceOutcome.Completed>(await tracer.TraceAsync(
                cache.ProjectId.Path, word, CancellationToken.None, Wait.Scaled(TimeSpan.FromSeconds(120))));
            Assert.True(outcome.Details!.SearchCompleted, word);
            Assert.False(outcome.Details.Capped || outcome.Details.TimedOut || outcome.Details.InvalidShape, word);
            var readings = outcome.Document!.Analyses.Select(analysis => string.Join("+", analysis.Morphs.Select(morph =>
                names.TryGetValue(CanonicalId.FromGuid(Guid.Parse(morph.FormId!)), out var name) ? name : "?")))
                .Distinct().Order().ToArray();
            output.WriteLine($"{word}: {string.Join(", ", readings)}");
            return readings;
        }
    }

    [RealParserFact]
    public async Task AFeatureChangePreservesPlaceOnThreeDifferentNasals()
    {
        using var cache = pristine.NewScratch();
        var f = new FeaturedSegments(cache);
        var category = AddCategory(cache, "Word");
        foreach (var stem in new[] { "ma", "na", "ŋa" }) AddStem(cache, stem, category);
        var manner = cache.LangProject.PhFeatureSystemOA.FeaturesOC.OfType<IFsClosedFeature>()
            .Single(feature => feature.Name.BestAnalysisAlternative.Text == "manner");
        var stop = manner.ValuesOC.Single(value => value.Name.BestAnalysisAlternative.Text == "stop");
        ApplyOps(cache, AuthorPhonologicalRuleComposer.Build(cache, new("denasalize", PhonologicalRuleDirection.Simultaneous,
            [new(NaturalClass: f.Nasals)], [new(Change: [new(CanonicalId.FromGuid(manner.Guid), CanonicalId.FromGuid(stop.Guid))])], [], [])));
        new FwDataProjectLoader().Save(cache);
        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);
        foreach (var word in new[] { "pa", "ta", "ka" })
        {
            var result = Assert.IsType<PanGlossTraceOutcome.Completed>(await tracer.TraceAsync(
                cache.ProjectId.Path, word, CancellationToken.None, Wait.Scaled(TimeSpan.FromSeconds(120))));
            Assert.NotEmpty(result.Document!.Analyses);
        }
        foreach (var word in new[] { "ma", "na", "ŋa" })
        {
            var result = Assert.IsType<PanGlossTraceOutcome.Completed>(await tracer.TraceAsync(
                cache.ProjectId.Path, word, CancellationToken.None, Wait.Scaled(TimeSpan.FromSeconds(120))));
            Assert.Empty(result.Document!.Analyses);
        }
    }
    [RealParserFact]
    public async Task SingleAndAllNegativeEnvironmentReferencesParseWithABinaryFeature()
    {
        foreach (var (polarity, bothContexts, repeat) in new[]
                 { ("+", false, false), ("-", false, false), ("-", true, false),
                     ("+", false, true), ("-", false, true), ("-", true, true) })
        {
            using var cache = pristine.NewScratch();
            var f = new FeaturedSegments(cache);
            var category = AddCategory(cache, "Word");
            foreach (var stem in bothContexts ? new[] { "mmma", "NmNa", "mmNa", "Nmma" } : new[] { "mma" })
                AddStem(cache, stem, category);
            var manner = cache.LangProject.PhFeatureSystemOA.FeaturesOC.OfType<IFsClosedFeature>()
                .Single(feature => feature.Name.BestAnalysisAlternative.Text == "manner");
            var stop = manner.ValuesOC.Single(value => value.Name.BestAnalysisAlternative.Text == "stop");
            EnvironmentContext[] context = [new(NaturalClass: f.Nasals, Variables: [new("alpha", f.Surface.Feature, polarity)],
                Minimum: repeat ? 1 : null, Maximum: repeat ? -1 : null)];
            ApplyOps(cache, AuthorPhonologicalRuleComposer.Build(cache,
                new("environment agreement", PhonologicalRuleDirection.Simultaneous, [new(NaturalClass: f.Nasals)],
                    [new(Change: [new(CanonicalId.FromGuid(manner.Guid), CanonicalId.FromGuid(stop.Guid))])],
                    bothContexts ? context : [], context)));
            var loader = new FwDataProjectLoader();
            loader.Save(cache);
            using var reopened = loader.LoadScratchCache(cache.ProjectId.Path);
            var rule = Assert.IsAssignableFrom<IPhRegularRule>(Assert.Single(reopened.LangProject.PhonologicalDataOA.PhonRulesOS));
            Assert.Single(rule.FeatureConstraints);
            Assert.NotNull(PhonologicalRuleReader.Read(rule));
            using var invoker = new PanGlossInvoker();
            var tracer = new PanGlossTracer(invoker);
            foreach (var word in bothContexts ? new[] { "mpma", "NpNa" } : new[] { "pma" })
                Assert.True(await AnalysisCount(word) > 0, word);
            foreach (var word in bothContexts ? new[] { "mpNa", "Npma" } : new[] { "mma" })
                Assert.Equal(0, await AnalysisCount(word));

            async Task<int> AnalysisCount(string word)
            {
                var result = Assert.IsType<PanGlossTraceOutcome.Completed>(await tracer.TraceAsync(
                    cache.ProjectId.Path, word, CancellationToken.None, Wait.Scaled(TimeSpan.FromSeconds(120))));
                Assert.True(result.Details!.SearchCompleted, word);
                Assert.False(result.Details.Capped || result.Details.TimedOut || result.Details.InvalidShape, word);
                output.WriteLine($"{polarity}, both contexts {bothContexts}, repeated {repeat}, {word}: {result.Document!.Analyses.Count} analyses");
                return result.Document.Analyses.Count;
            }
        }
    }

}
