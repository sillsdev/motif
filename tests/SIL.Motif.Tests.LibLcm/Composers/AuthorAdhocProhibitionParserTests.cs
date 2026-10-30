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

/// <summary>A morpheme prohibition between a prefix and a stem removes the parse that uses both, as FieldWorks'
/// HermitCrab loader reads MoMorphAdhocProhib; an allomorph prohibition does the same for the two forms.</summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group2)]
public sealed class AuthorAdhocProhibitionParserTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly LcmCache _cache = pristine.NewScratch();

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    [RealParserFact]
    public async Task AMorphemeProhibitionRemovesTheParseThatUsesBothMorphemes()
    {
        var (prefix, stem) = Seed();
        await AssertProhibitionRemovesParse("morpheme", prefix.MorphoSyntaxAnalysesOC.Single(), stem.MorphoSyntaxAnalysesOC.Single());
    }

    [RealParserFact]
    public async Task AnAllomorphProhibitionRemovesTheParseThatUsesBothForms()
    {
        var (prefix, stem) = Seed();
        await AssertProhibitionRemovesParse("allomorph", prefix.LexemeFormOA, stem.LexemeFormOA);
    }

    private async Task AssertProhibitionRemovesParse(string kind, ICmObject first, ICmObject rest)
    {
        using var invoker = new PanGlossInvoker();
        var baseline = await ParseAsync(invoker, "pabago", "bago");
        Assert.Equal(WordOutcome.Analysed, baseline["pabago"]);
        Assert.Equal(WordOutcome.Analysed, baseline["bago"]);
        SoundSystemComposerTests.Execute(_cache, AuthorAdhocProhibitionComposer.Build(_cache,
            new(kind, CanonicalId.FromGuid(first.Guid), [CanonicalId.FromGuid(rest.Guid)], "anywhere")));
        new FwDataProjectLoader().Save(_cache);
        var after = await ParseAsync(invoker, "pabago", "bago");
        Assert.Equal(WordOutcome.NoAnalysis, after["pabago"]);
        Assert.Equal(WordOutcome.Analysed, after["bago"]);
    }

    private (ILexEntry Prefix, ILexEntry Stem) Seed()
    {
        RealParserProject.PrepareForParsing(_cache, "p", "a", "b", "g", "o");
        ILexEntry prefix = null!, stem = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var services = _cache.ServiceLocator;
            var noun = services.GetInstance<IPartOfSpeechFactory>().Create();
            _cache.LangProject.PartsOfSpeechOA.PossibilitiesOS.Add(noun);
            noun.Name.set_String(_cache.DefaultAnalWs, "Noun");
            var slot = services.GetInstance<IMoInflAffixSlotFactory>().Create();
            noun.AffixSlotsOC.Add(slot);
            slot.Name.set_String(_cache.DefaultAnalWs, "S1");
            slot.Optional = true;
            var template = services.GetInstance<IMoInflAffixTemplateFactory>().Create();
            noun.AffixTemplatesOS.Add(template);
            template.Name.set_String(_cache.DefaultAnalWs, "T1");
            template.Final = true;
            template.PrefixSlotsRS.Add(slot);
            var types = services.GetInstance<IMoMorphTypeRepository>();
            prefix = services.GetInstance<ILexEntryFactory>().Create(types.GetObject(MoMorphTypeTags.kguidMorphPrefix),
                TsStringUtils.MakeString("pa", _cache.DefaultVernWs), "prefix", SandboxGenericMSA.Create(MsaType.kInfl, noun));
            ((IMoInflAffMsa)prefix.MorphoSyntaxAnalysesOC.Single()).SlotsRC.Add(slot);
            stem = services.GetInstance<ILexEntryFactory>().Create(types.GetObject(MoMorphTypeTags.kguidMorphStem),
                TsStringUtils.MakeString("bago", _cache.DefaultVernWs), "stem", SandboxGenericMSA.Create(MsaType.kStem, noun));
        });
        new FwDataProjectLoader().Save(_cache);
        return (prefix, stem);
    }

    private async Task<Dictionary<string, WordOutcome>> ParseAsync(PanGlossInvoker invoker, params string[] words)
    {
        var outcome = await invoker.RunAsync(new PanGlossRequest.Batch(_cache.ProjectId.Path, words,
            TimeSpan.FromSeconds(10)), "author adhoc prohibition", CancellationToken.None);
        var completed = Assert.IsType<PanGlossOutcome.Completed>(outcome);
        return BatchTsvParser.Parse(completed.Output).ToDictionary(row => row.Word, row => row.Outcome);
    }
}
