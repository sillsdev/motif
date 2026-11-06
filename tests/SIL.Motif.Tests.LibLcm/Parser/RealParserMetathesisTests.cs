using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using static SIL.Motif.Tests.Composers.AffixTestSupport;

namespace SIL.Motif.Tests.Parser;

[Collection(LcmCacheParallelCollections.Group1)]
public sealed class RealParserMetathesisTests(PristineProjectFixture pristine)
{
    [RealParserFact]
    public async Task FeaturelessMetathesisMatchesSpellingAndLeavesOtherRootsAlone()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, "a", "t", "k", "p");
        var category = AddCategory(cache, "Word");
        AddStem(cache, "atka", category);
        AddStem(cache, "apka", category);
        AddRule(cache, ["a", "t", "k"], "0 1 -1 2 -1");
        new FwDataProjectLoader().Save(cache);
        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);
        Assert.True(await HasAnalysis(tracer, cache, "akta"));
        Assert.False(await HasAnalysis(tracer, cache, "atka"));
        Assert.True(await HasAnalysis(tracer, cache, "apka"));
        Assert.False(await HasAnalysis(tracer, cache, "akpa"));
    }

    [RealParserFact]
    public async Task AMiddleContextIsOmittedDuringAnalysisAsInFieldWorks()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, "t", "i", "k", "a", "p");
        var category = AddCategory(cache, "Word");
        AddStem(cache, "tika", category);
        AddStem(cache, "pika", category);
        AddRule(cache, ["t", "i", "k"], "-1 0 1:L 2 -1");
        new FwDataProjectLoader().Save(cache);
        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);
        Assert.False(await HasAnalysis(tracer, cache, "kita"));
        Assert.True(await HasAnalysis(tracer, cache, "pika"));
    }

    private static async Task<bool> HasAnalysis(PanGlossTracer tracer, LcmCache cache, string word)
    {
        var result = Assert.IsType<PanGlossTraceOutcome.Completed>(await tracer.TraceAsync(
            cache.ProjectId.Path, word, CancellationToken.None));
        Assert.True(result.Details!.SearchCompleted, word);
        return result.Document!.Analyses.Count > 0;
    }

    private static void AddRule(LcmCache cache, string[] segments, string change) =>
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var services = cache.ServiceLocator;
            var rule = services.GetInstance<IPhMetathesisRuleFactory>().Create();
            cache.LangProject.PhonologicalDataOA.PhonRulesOS.Add(rule);
            rule.Name.set_String(cache.DefaultAnalWs, "metathesis");
            foreach (var segment in segments)
            {
                var context = services.GetInstance<IPhSimpleContextSegFactory>().Create();
                rule.StrucDescOS.Add(context);
                context.FeatureStructureRA = cache.LangProject.PhonologicalDataOA.PhonemeSetsOS[0].PhonemesOC
                    .Single(phone => phone.CodesOS.Any(code =>
                        code.Representation.get_String(cache.DefaultVernWs).Text == segment));
            }
            rule.StrucChange = TsStringUtils.MakeString(change, cache.DefaultAnalWs);
        });
}
