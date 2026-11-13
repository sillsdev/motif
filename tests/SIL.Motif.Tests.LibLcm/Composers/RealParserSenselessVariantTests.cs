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

/// <summary>A sense-less variant parses as the main sense's stem, with PanGloss.</summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class RealParserSenselessVariantTests(PristineProjectFixture pristine, ITestOutputHelper output) : IDisposable
{
    private static readonly string[] Letters = ["p", "a", "l", "i", "t", "k", "u", "n"];
    private readonly LcmCache _cache = pristine.NewScratch();

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    [RealParserFact]
    public async Task ASenseLessVariantParsesAsTheMainSensesStemWhetherAuthoredOrStripped()
    {
        RealParserProject.PrepareForParsing(_cache, Letters);
        var verb = Created(ApplyOps(_cache, AuthorPartOfSpeechComposer.Build(_cache,
            new AuthorPartOfSpeechIntent("Verb", Anal(_cache), "V"))), CmPossibilityListPossibilitiesOperationKinds.Create);
        var type = VariantType();
        var main = AddStem(_cache, "palit", verb);
        var mainSense = Id(Get<ILexEntry>(_cache, main.Entry).SensesOS[0]);
        var copy = AddStem(_cache, "plit", verb);
        ApplyOps(_cache, MakeVariantOfComposer.Build(_cache, new(copy.Entry, main.Entry, type)));
        var path = _cache.ProjectId.Path;
        new FwDataProjectLoader().Save(_cache);
        using var invoker = new PanGlossInvoker();
        var tracer = new PanGlossTracer(invoker);
        var before = Assert.Single(await Readings(tracer, path, "plit"));
        Assert.DoesNotContain(main.Msa.Value, before, StringComparison.Ordinal);

        ApplyOps(_cache, StripVariantSensesComposer.Build(_cache, new StripVariantSensesIntent([new(copy.Entry)])));
        var authored = ApplyOps(_cache, AuthorVariantEntryComposer.Build(_cache, new AuthorVariantEntryIntent(mainSense, type, "palt",
            Vern(_cache), MorphType(MoMorphTypeTags.kguidMorphStem))));
        new FwDataProjectLoader().Save(_cache);

        Assert.Equal(["plit|" + main.Msa.Value + "|palit"], await Readings(tracer, path, "plit"));
        Assert.Equal(["palt|" + main.Msa.Value + "|palit"], await Readings(tracer, path, "palt"));
        Assert.Equal(["palit|" + main.Msa.Value + "|palit"], await Readings(tracer, path, "palit"));
        Assert.Empty(Get<ILexEntry>(_cache, Created(authored, LexDbEntriesOperationKinds.Create)).SensesOS);
    }

    private async Task<string[]> Readings(PanGlossTracer tracer, string path, string word)
    {
        var outcome = Assert.IsType<PanGlossTraceOutcome.Completed>(await tracer.TraceAsync(path, word, CancellationToken.None));
        Assert.True(outcome.Details!.SearchCompleted, word);
        var readings = outcome.Document!.Analyses.Select(analysis => string.Join("+", analysis.Morphs.Select(morph =>
            $"{morph.Form}|{(morph.MsaId is { } msa ? CanonicalId.FromGuid(Guid.Parse(msa)).Value : "")}|{morph.Gloss}")))
            .Distinct().Order(StringComparer.Ordinal).ToArray();
        output.WriteLine($"{word}: {string.Join(" ; ", readings)}");
        return readings;
    }

    private CanonicalId VariantType()
    {
        ILexEntryType type = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var services = _cache.ServiceLocator;
            var list = _cache.LangProject.LexDbOA.VariantEntryTypesOA;
            if (list is null)
            {
                list = services.GetInstance<ICmPossibilityListFactory>().Create();
                _cache.LangProject.LexDbOA.VariantEntryTypesOA = list;
            }
            type = services.GetInstance<ILexEntryTypeFactory>().Create();
            list.PossibilitiesOS.Add(type);
        });
        return Id(type);
    }
}
