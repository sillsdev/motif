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

/// <summary>
/// Two forms of one affix with the same environments (here none) are free variation in HermitCrab, so both parse and
/// neither blocks the other; a form with an environment the lexeme form lacks blocks the lexeme form wherever it matches.
/// </summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group2)]
public sealed class AuthorFreeVariationParserTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly LcmCache _cache = pristine.NewScratch();

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    [RealParserFact]
    public async Task AnUnconditionedAlternateAndTheUnconditionedLexemeFormBothParseAfterAnySound()
    {
        var suffix = Seed();
        using var invoker = new PanGlossInvoker();
        var words = new[] { "bagoan", "bagoang", "bagaan", "bagaang" };
        var before = await ParseAsync(invoker, words);
        Assert.Equal(WordOutcome.Analysed, before["bagoan"]);
        Assert.Equal(WordOutcome.NoAnalysis, before["bagoang"]);

        SoundSystemComposerTests.Execute(_cache, AuthorAlternateFormComposer.Build(_cache, Alternate(suffix, null)));
        new FwDataProjectLoader().Save(_cache);

        var after = await ParseAsync(invoker, words);
        Assert.All(words, word => Assert.Equal(WordOutcome.Analysed, after[word]));
    }

    [RealParserFact]
    public async Task AnAlternateWithAnEnvironmentBlocksTheUnconditionedLexemeFormWhereItMatches()
    {
        var suffix = Seed();
        var afterO = EnvironmentAfter("o");
        SoundSystemComposerTests.Execute(_cache, AuthorAlternateFormComposer.Build(_cache, Alternate(suffix, afterO)));
        new FwDataProjectLoader().Save(_cache);

        using var invoker = new PanGlossInvoker();
        var outcome = await ParseAsync(invoker, "bagoan", "bagoang", "bagaan", "bagaang");
        Assert.Equal(WordOutcome.NoAnalysis, outcome["bagoan"]);
        Assert.Equal(WordOutcome.Analysed, outcome["bagoang"]);
        Assert.Equal(WordOutcome.Analysed, outcome["bagaan"]);
        Assert.Equal(WordOutcome.NoAnalysis, outcome["bagaang"]);
    }

    [RealParserFact]
    public async Task TwoAlternatesWithTheSameEnvironmentBothParseWhereItMatchesAndBlockTheLexemeForm()
    {
        var suffix = Seed();
        var afterO = EnvironmentAfter("o");
        SoundSystemComposerTests.Execute(_cache, AuthorAlternateFormComposer.Build(_cache, Alternate(suffix, afterO)));
        SoundSystemComposerTests.Execute(_cache, AuthorAlternateFormComposer.Build(_cache, Alternate(suffix, afterO) with { Form = "ong" }));
        new FwDataProjectLoader().Save(_cache);

        using var invoker = new PanGlossInvoker();
        var outcome = await ParseAsync(invoker, "bagoan", "bagoang", "bagoong", "bagaan");
        Assert.Equal(WordOutcome.Analysed, outcome["bagoang"]);
        Assert.Equal(WordOutcome.Analysed, outcome["bagoong"]);
        Assert.Equal(WordOutcome.NoAnalysis, outcome["bagoan"]);
        Assert.Equal(WordOutcome.Analysed, outcome["bagaan"]);
    }

    private AuthorAlternateFormIntent Alternate(ILexEntry suffix, CanonicalId? environment) => new(
        CanonicalId.FromGuid(suffix.Guid), "ang", _cache.WritingSystemFactory.GetStrFromWs(_cache.DefaultVernWs),
        CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphSuffix), environment is { } id ? [id] : null);

    private CanonicalId EnvironmentAfter(string phonemeName)
    {
        var phoneme = _cache.LangProject.PhonologicalDataOA.PhonemeSetsOS[0].PhonemesOC
            .Single(item => item.Name.get_String(_cache.DefaultVernWs).Text == phonemeName);
        var existing = _cache.LangProject.PhonologicalDataOA.EnvironmentsOS.Select(item => item.Guid).ToHashSet();
        SoundSystemComposerTests.Execute(_cache, AuthorEnvironmentComposer.Build(_cache,
            new("after " + phonemeName, [new(Phoneme: CanonicalId.FromGuid(phoneme.Guid))], [])));
        return CanonicalId.FromGuid(_cache.LangProject.PhonologicalDataOA.EnvironmentsOS
            .Single(item => !existing.Contains(item.Guid)).Guid);
    }

    private ILexEntry Seed()
    {
        RealParserProject.PrepareForParsing(_cache, "b", "a", "g", "o", "n");
        ILexEntry suffix = null!;
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
            template.SuffixSlotsRS.Add(slot);
            var types = services.GetInstance<IMoMorphTypeRepository>();
            suffix = services.GetInstance<ILexEntryFactory>().Create(types.GetObject(MoMorphTypeTags.kguidMorphSuffix),
                TsStringUtils.MakeString("an", _cache.DefaultVernWs), "suffix", SandboxGenericMSA.Create(MsaType.kInfl, noun));
            ((IMoInflAffMsa)suffix.MorphoSyntaxAnalysesOC.Single()).SlotsRC.Add(slot);
            foreach (var stem in new[] { "bago", "baga" })
                services.GetInstance<ILexEntryFactory>().Create(types.GetObject(MoMorphTypeTags.kguidMorphStem),
                    TsStringUtils.MakeString(stem, _cache.DefaultVernWs), "stem", SandboxGenericMSA.Create(MsaType.kStem, noun));
        });
        new FwDataProjectLoader().Save(_cache);
        return suffix;
    }

    private async Task<Dictionary<string, WordOutcome>> ParseAsync(PanGlossInvoker invoker, params string[] words)
    {
        var outcome = await invoker.RunAsync(new PanGlossRequest.Batch(_cache.ProjectId.Path, words,
            TimeSpan.FromSeconds(10)), "author free variation", CancellationToken.None);
        var completed = Assert.IsType<PanGlossOutcome.Completed>(outcome);
        return BatchTsvParser.Parse(completed.Output).ToDictionary(row => row.Word, row => row.Outcome);
    }
}
