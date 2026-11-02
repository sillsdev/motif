using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
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
/// An environment the parser cannot read is dropped, not enforced: the form it was written to restrict is then
/// unconstrained, while a readable environment on a sister form restricts it. This is the behaviour the
/// allomorph-environment-ignored-by-parser warning describes.
/// </summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class RealParserInvalidEnvironmentTests(PristineProjectFixture pristine, ITestOutputHelper output)
{
    private static readonly string[] Letters = ["t", "u", "n", "l", "o", "k", "a"];

    [RealParserFact]
    public async Task AStemWhoseEnvironmentHasABoundaryIsNotRestrictedByItWhileAReadableOneRestrictsItsSister()
    {
        using var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, Letters);
        var ws = Anal(cache);
        var noun = Created(ApplyOps(cache, AuthorPartOfSpeechComposer.Build(cache,
            new AuthorPartOfSpeechIntent("Noun", ws, "N"))), CmPossibilityListPossibilitiesOperationKinds.Create);
        var readable = AddStem(cache, "tun", noun);
        var unreadable = AddStem(cache, "lok", noun);
        Restrict(cache, readable.Form, "/ _ a");
        Restrict(cache, unreadable.Form, "/ _ +a");
        new FwDataProjectLoader().Save(cache);

        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);
        var path = cache.ProjectId.Path;
        output.WriteLine("readable env: " + string.Join(" | ", await Readings(tracer, path, "tun")));
        output.WriteLine("unreadable env: " + string.Join(" | ", await Readings(tracer, path, "lok")));
        Assert.Empty(await Readings(tracer, path, "tun"));
        Assert.Single(await Readings(tracer, path, "lok"));
    }

    private static void Restrict(LcmCache cache, CanonicalId form, string environmentText) =>
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var environment = cache.ServiceLocator.GetInstance<IPhEnvironmentFactory>().Create();
            cache.LangProject.PhonologicalDataOA.EnvironmentsOS.Add(environment);
            environment.StringRepresentation = TsStringUtils.MakeString(environmentText, cache.DefaultVernWs);
            Get<IMoStemAllomorph>(cache, form).PhoneEnvRC.Add(environment);
        });

    private static async Task<string[]> Readings(PanGlossTracer tracer, string path, string word)
    {
        var outcome = Assert.IsType<PanGlossTraceOutcome.Completed>(
            await tracer.TraceAsync(path, word, CancellationToken.None));
        Assert.True(outcome.Details!.SearchCompleted, word);
        return outcome.Document!.Analyses.Select(analysis => string.Join("+", analysis.Morphs.Select(morph => morph.Form)))
            .Distinct().Order(StringComparer.Ordinal).ToArray();
    }
}
