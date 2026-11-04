using SIL.LCModel;
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

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class SetEntryDoNotParseParserTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly LcmCache _cache = pristine.NewScratch();

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    [RealParserFact]
    public async Task TheDoNotParseFlagIsWrittenAndClearedButPanGlossParsesTheEntryEitherWay()
    {
        Guid retired = default;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            retired = Stem("ayu").Guid;
            Stem("batu");
        });
        RealParserProject.PrepareForParsing(_cache, "a", "y", "u", "b", "t");
        new FwDataProjectLoader().Save(_cache);

        using var invoker = new PanGlossInvoker();
        var before = await ParseAsync(invoker, "ayu", "batu");
        Assert.Equal(WordOutcome.Analysed, before["ayu"]);
        Assert.Equal(WordOutcome.Analysed, before["batu"]);

        SetFlag(retired, expected: false, desired: true);
        var retiredParse = await ParseAsync(invoker, "ayu", "batu");
        // Since 0.15.0 PanGloss parses do-not-parse entries again; FieldWorks never reads the flag.
        Assert.Equal(WordOutcome.Analysed, retiredParse["ayu"]);
        Assert.Equal(WordOutcome.Analysed, retiredParse["batu"]);

        SetFlag(retired, expected: true, desired: false);
        var restored = await ParseAsync(invoker, "ayu", "batu");
        Assert.Equal(WordOutcome.Analysed, restored["ayu"]);
        Assert.Equal(WordOutcome.Analysed, restored["batu"]);
    }

    private void SetFlag(Guid entry, bool expected, bool desired)
    {
        SoundSystemComposerTests.Execute(_cache, SetEntryDoNotParseComposer.Build(_cache,
            new(CanonicalId.FromGuid(entry), expected, desired)));
        new FwDataProjectLoader().Save(_cache);
    }

    private ILexEntry Stem(string form)
    {
        var services = _cache.ServiceLocator;
        var entry = services.GetInstance<ILexEntryFactory>().Create();
        var allomorph = services.GetInstance<IMoStemAllomorphFactory>().Create();
        entry.LexemeFormOA = allomorph;
        allomorph.MorphTypeRA = services.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphStem);
        allomorph.Form.set_String(_cache.DefaultVernWs, form);
        var msa = services.GetInstance<IMoStemMsaFactory>().Create();
        entry.MorphoSyntaxAnalysesOC.Add(msa);
        msa.PartOfSpeechRA = services.GetInstance<IPartOfSpeechRepository>().GetObject(pristine.Seed.PartOfSpeechId);
        var sense = services.GetInstance<ILexSenseFactory>().Create();
        entry.SensesOS.Add(sense);
        sense.Gloss.set_String(_cache.DefaultAnalWs, form);
        sense.MorphoSyntaxAnalysisRA = msa;
        return entry;
    }

    private async Task<Dictionary<string, WordOutcome>> ParseAsync(PanGlossInvoker invoker, params string[] words)
    {
        var outcome = await invoker.RunAsync(new PanGlossRequest.Batch(_cache.ProjectId.Path, words,
            TimeSpan.FromSeconds(10)), "do not parse", CancellationToken.None);
        var completed = Assert.IsType<PanGlossOutcome.Completed>(outcome);
        return BatchTsvParser.Parse(completed.Output).ToDictionary(row => row.Word, row => row.Outcome);
    }
}
