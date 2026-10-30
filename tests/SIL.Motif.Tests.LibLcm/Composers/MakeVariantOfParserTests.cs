using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Composers;

/// <summary>PanGloss, like FieldWorks' HermitCrab loader, reads a variant reference only for an entry with no senses
/// and only when the main entry has a stem analysis, so a variant with senses parses exactly as before.</summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group2)]
public sealed class MakeVariantOfParserTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly LcmCache _cache = pristine.NewScratch();

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    [RealParserFact]
    public async Task ASenselessStemVariantParsesAsItsMainEntryOnlyAfterTheLinkIsApplied()
    {
        var (main, variant, type) = Seed(variantHasSense: false);
        RealParserProject.PrepareForParsing(_cache, "b", "a", "g", "o", "u");
        new FwDataProjectLoader().Save(_cache);
        using var invoker = new PanGlossInvoker();
        Assert.Equal(WordOutcome.NoAnalysis, (await ParseAsync(invoker, "bagu"))["bagu"]);
        Link(variant, main, type);
        new FwDataProjectLoader().Save(_cache);
        Assert.Equal(WordOutcome.Analysed, (await ParseAsync(invoker, "bagu"))["bagu"]);
    }

    [RealParserFact]
    public async Task AVariantWithItsOwnSenseParsesTheSameBeforeAndAfterTheLink()
    {
        var (main, variant, type) = Seed(variantHasSense: true);
        RealParserProject.PrepareForParsing(_cache, "b", "a", "g", "o", "u");
        new FwDataProjectLoader().Save(_cache);
        using var invoker = new PanGlossInvoker();
        var before = await ParseAsync(invoker, "bagu", "bago");
        Link(variant, main, type);
        new FwDataProjectLoader().Save(_cache);
        Assert.Equal(before, await ParseAsync(invoker, "bagu", "bago"));
        Assert.Equal(WordOutcome.Analysed, before["bagu"]);
    }

    private void Link(ILexEntry variant, ILexEntry main, ILexEntryType type) =>
        SoundSystemComposerTests.Execute(_cache, MakeVariantOfComposer.Build(_cache,
            new(CanonicalId.FromGuid(variant.Guid), CanonicalId.FromGuid(main.Guid), CanonicalId.FromGuid(type.Guid))));

    private (ILexEntry Main, ILexEntry Variant, ILexEntryType Type) Seed(bool variantHasSense)
    {
        ILexEntry main = null!, variant = null!;
        ILexEntryType type = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var services = _cache.ServiceLocator;
            var stem = services.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphStem);
            var ws = _cache.DefaultVernWs;
            main = services.GetInstance<ILexEntryFactory>().Create(stem, TsStringUtils.MakeString("bago", ws), "main",
                new SandboxGenericMSA { MsaType = MsaType.kStem });
            if (variantHasSense)
                variant = services.GetInstance<ILexEntryFactory>().Create(stem, TsStringUtils.MakeString("bagu", ws), "variant",
                    new SandboxGenericMSA { MsaType = MsaType.kStem });
            else
            {
                variant = services.GetInstance<ILexEntryFactory>().Create();
                var form = services.GetInstance<IMoStemAllomorphFactory>().Create();
                variant.LexemeFormOA = form;
                form.MorphTypeRA = stem;
                form.Form.set_String(ws, "bagu");
            }
            var list = _cache.LangProject.LexDbOA.VariantEntryTypesOA;
            if (list is null)
            {
                list = services.GetInstance<ICmPossibilityListFactory>().Create();
                _cache.LangProject.LexDbOA.VariantEntryTypesOA = list;
            }
            type = services.GetInstance<ILexEntryTypeFactory>().Create();
            list.PossibilitiesOS.Add(type);
        });
        return (main, variant, type);
    }

    private async Task<Dictionary<string, WordOutcome>> ParseAsync(PanGlossInvoker invoker, params string[] words)
    {
        var outcome = await invoker.RunAsync(new PanGlossRequest.Batch(_cache.ProjectId.Path, words,
            TimeSpan.FromSeconds(10)), "make variant of", CancellationToken.None);
        var completed = Assert.IsType<PanGlossOutcome.Completed>(outcome);
        return BatchTsvParser.Parse(completed.Output).ToDictionary(row => row.Word, row => row.Outcome);
    }
}
