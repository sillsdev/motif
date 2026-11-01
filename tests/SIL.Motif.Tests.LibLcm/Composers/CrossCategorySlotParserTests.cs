using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Composers;

/// <summary>An inflectional affix may sit in a slot of its own category, an ancestor, or a subcategory, as FieldWorks'
/// loader unifies a template's category with the affix's; a slot of an unrelated category loads but never matches.</summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group2)]
public sealed class CrossCategorySlotParserTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly LcmCache _cache = pristine.NewScratch();

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    [RealParserFact]
    public async Task AVerbAffixAuthoredInASlotOfAVerbSubcategoryParsesACopulaStem()
    {
        var seed = Seed(copulaUnderVerb: true);
        Author(seed, seed.Verb);
        Assert.Equal(WordOutcome.Analysed, await ParseAsync("bagoan"));
    }

    [RealParserFact]
    public async Task ACopulaCategoryAffixInASlotOfAnUnrelatedCategoryParsesAndAVerbOneIsRefusedBecauseItNeverCould()
    {
        var seed = Seed(copulaUnderVerb: false);
        var refusal = Assert.Throws<InvalidOperationException>(() => Author(seed, seed.Verb));
        Assert.Contains("never matches", refusal.Message, StringComparison.Ordinal);
        Author(seed, seed.Copula);
        Assert.Equal(WordOutcome.Analysed, await ParseAsync("bagoan"));
    }

    [RealParserFact]
    public async Task AVerbAffixPlacedInAnUnrelatedCopulaSlotByHandNeverParsesWhichIsWhatTheRefusalPrevents()
    {
        var seed = Seed(copulaUnderVerb: false);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var services = _cache.ServiceLocator;
            var types = services.GetInstance<IMoMorphTypeRepository>();
            var suffix = services.GetInstance<ILexEntryFactory>().Create(types.GetObject(MoMorphTypeTags.kguidMorphSuffix),
                TsStringUtils.MakeString("an", _cache.DefaultVernWs), "rel",
                SandboxGenericMSA.Create(MsaType.kInfl, _cache.ServiceLocator.GetObject(seed.Verb.Guid) as IPartOfSpeech));
            ((IMoInflAffMsa)suffix.MorphoSyntaxAnalysesOC.Single()).SlotsRC.Add(
                (IMoInflAffixSlot)_cache.ServiceLocator.GetObject(seed.Slot.Guid));
        });
        new FwDataProjectLoader().Save(_cache);
        Assert.Equal(WordOutcome.NoAnalysis, await ParseAsync("bagoan"));
    }

    private sealed record Setup(IPartOfSpeech Verb, IPartOfSpeech Copula, IMoInflAffixSlot Slot);

    private void Author(Setup seed, IPartOfSpeech category)
    {
        var ws = _cache.WritingSystemFactory.GetStrFromWs(_cache.DefaultVernWs);
        var analysis = _cache.WritingSystemFactory.GetStrFromWs(_cache.DefaultAnalWs);
        SoundSystemComposerTests.Execute(_cache, AuthorLexicalEntryComposer.Build(_cache, new(
            "an", ws, CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphSuffix), "rel", analysis,
            new(LexicalEntryMsaKind.Inflectional, CanonicalId.FromGuid(category.Guid),
                Slots: [CanonicalId.FromGuid(seed.Slot.Guid)]))));
        new FwDataProjectLoader().Save(_cache);
    }

    private Setup Seed(bool copulaUnderVerb)
    {
        RealParserProject.PrepareForParsing(_cache, "b", "a", "g", "o", "n");
        IPartOfSpeech verb = null!, copula = null!;
        IMoInflAffixSlot slot = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var services = _cache.ServiceLocator;
            IPartOfSpeech Pos(string name, IPartOfSpeech? parent)
            {
                var pos = services.GetInstance<IPartOfSpeechFactory>().Create();
                if (parent is null) _cache.LangProject.PartsOfSpeechOA.PossibilitiesOS.Add(pos);
                else parent.SubPossibilitiesOS.Add(pos);
                pos.Name.set_String(_cache.DefaultAnalWs, name);
                return pos;
            }
            verb = Pos("Verb", null);
            copula = Pos("Copula", copulaUnderVerb ? verb : null);
            slot = services.GetInstance<IMoInflAffixSlotFactory>().Create();
            copula.AffixSlotsOC.Add(slot);
            slot.Name.set_String(_cache.DefaultAnalWs, "S1");
            slot.Optional = true;
            var template = services.GetInstance<IMoInflAffixTemplateFactory>().Create();
            copula.AffixTemplatesOS.Add(template);
            template.Name.set_String(_cache.DefaultAnalWs, "T1");
            template.Final = true;
            template.SuffixSlotsRS.Add(slot);
            var types = services.GetInstance<IMoMorphTypeRepository>();
            services.GetInstance<ILexEntryFactory>().Create(types.GetObject(MoMorphTypeTags.kguidMorphStem),
                TsStringUtils.MakeString("bago", _cache.DefaultVernWs), "stem", SandboxGenericMSA.Create(MsaType.kStem, copula));
        });
        new FwDataProjectLoader().Save(_cache);
        return new(verb, copula, slot);
    }

    private async Task<WordOutcome> ParseAsync(string word)
    {
        using var invoker = new PanGlossInvoker();
        var outcome = await invoker.RunAsync(new PanGlossRequest.Batch(_cache.ProjectId.Path, [word],
            TimeSpan.FromSeconds(10)), "cross category", CancellationToken.None);
        var completed = Assert.IsType<PanGlossOutcome.Completed>(outcome);
        return BatchTsvParser.Parse(completed.Output).Single().Outcome;
    }
}
