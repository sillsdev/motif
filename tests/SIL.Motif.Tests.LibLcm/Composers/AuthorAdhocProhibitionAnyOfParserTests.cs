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

/// <summary>One prohibition over a set of alternatives bans the first from each member and from no one else, which a
/// single prohibition whose rest holds the same morphemes does not, because the rest is one sequence that must all be present.</summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group2)]
public sealed class AuthorAdhocProhibitionAnyOfParserTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly LcmCache _cache = pristine.NewScratch();

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    [RealParserFact]
    public async Task ASetOfAlternativesBansEachMemberAndNoOtherStem()
    {
        var (prefix, stems) = Seed();
        using var invoker = new PanGlossInvoker();
        var before = await ParseAsync(invoker, "pabago", "pagabo", "pagoba", "bago");
        Assert.All(before.Values, outcome => Assert.Equal(WordOutcome.Analysed, outcome));

        var operations = AuthorAdhocProhibitionComposer.Build(_cache, new("morpheme", Msa(prefix), [], "anywhere",
            [Msa(stems[0]), Msa(stems[1])]));
        Assert.Equal(2, operations.Count);
        SoundSystemComposerTests.Execute(_cache, operations);
        new FwDataProjectLoader().Save(_cache);

        var after = await ParseAsync(invoker, "pabago", "pagabo", "pagoba", "bago");
        Assert.Equal(WordOutcome.NoAnalysis, after["pabago"]);
        Assert.Equal(WordOutcome.NoAnalysis, after["pagabo"]);
        Assert.Equal(WordOutcome.Analysed, after["pagoba"]);
        Assert.Equal(WordOutcome.Analysed, after["bago"]);
    }

    [RealParserFact]
    public async Task OneProhibitionWhoseRestHoldsTheSameMorphemesBansNeitherBecauseTheRestIsASequence()
    {
        var (prefix, stems) = Seed();
        using var invoker = new PanGlossInvoker();
        SoundSystemComposerTests.Execute(_cache, AuthorAdhocProhibitionComposer.Build(_cache,
            new("morpheme", Msa(prefix), [Msa(stems[0]), Msa(stems[1])], "anywhere")));
        new FwDataProjectLoader().Save(_cache);

        var after = await ParseAsync(invoker, "pabago", "pagabo");
        Assert.Equal(WordOutcome.Analysed, after["pabago"]);
        Assert.Equal(WordOutcome.Analysed, after["pagabo"]);
    }

    private static CanonicalId Msa(ILexEntry entry) => CanonicalId.FromGuid(entry.MorphoSyntaxAnalysesOC.Single().Guid);

    private (ILexEntry Prefix, ILexEntry[] Stems) Seed()
    {
        RealParserProject.PrepareForParsing(_cache, "p", "a", "b", "g", "o");
        ILexEntry prefix = null!;
        var stems = new List<ILexEntry>();
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
            foreach (var form in new[] { "bago", "gabo", "goba" })
                stems.Add(services.GetInstance<ILexEntryFactory>().Create(types.GetObject(MoMorphTypeTags.kguidMorphStem),
                    TsStringUtils.MakeString(form, _cache.DefaultVernWs), form, SandboxGenericMSA.Create(MsaType.kStem, noun)));
        });
        new FwDataProjectLoader().Save(_cache);
        return (prefix, stems.ToArray());
    }

    private async Task<Dictionary<string, WordOutcome>> ParseAsync(PanGlossInvoker invoker, params string[] words)
    {
        var outcome = await invoker.RunAsync(new PanGlossRequest.Batch(_cache.ProjectId.Path, words,
            TimeSpan.FromSeconds(10)), "adhoc any-of", CancellationToken.None);
        var completed = Assert.IsType<PanGlossOutcome.Completed>(outcome);
        return BatchTsvParser.Parse(completed.Output).ToDictionary(row => row.Word, row => row.Outcome);
    }
}
