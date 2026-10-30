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
public sealed class MoveSenseToEntryParserTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly LcmCache _cache = pristine.NewScratch();

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    [RealParserFact]
    public async Task AWordFoldedIntoItsRootAsASenseParsesOnlyOnceItHasAnEntryOfItsOwn()
    {
        Guid derivedSense = default;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var services = _cache.ServiceLocator;
            var entry = services.GetInstance<ILexEntryFactory>().Create();
            var form = services.GetInstance<IMoStemAllomorphFactory>().Create();
            entry.LexemeFormOA = form;
            form.MorphTypeRA = services.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphStem);
            form.Form.set_String(_cache.DefaultVernWs, "ayu");
            var msa = services.GetInstance<IMoStemMsaFactory>().Create();
            entry.MorphoSyntaxAnalysesOC.Add(msa);
            msa.PartOfSpeechRA = services.GetInstance<IPartOfSpeechRepository>().GetObject(pristine.Seed.PartOfSpeechId);
            foreach (var gloss in new[] { "tree", "kaayu" })
            {
                var sense = services.GetInstance<ILexSenseFactory>().Create();
                entry.SensesOS.Add(sense);
                sense.Gloss.set_String(_cache.DefaultAnalWs, gloss);
                sense.MorphoSyntaxAnalysisRA = msa;
                derivedSense = sense.Guid;
            }
        });
        RealParserProject.PrepareForParsing(_cache, "a", "y", "u", "k");
        new FwDataProjectLoader().Save(_cache);

        using var invoker = new PanGlossInvoker();
        var before = await ParseAsync(invoker, "ayu", "kaayu");
        Assert.Equal(WordOutcome.Analysed, before["ayu"]);
        Assert.Equal(WordOutcome.NoAnalysis, before["kaayu"]);

        var intent = new MoveSenseToEntryIntent(CanonicalId.FromGuid(derivedSense), "kaayu",
            _cache.ServiceLocator.WritingSystems.DefaultVernacularWritingSystem.Id,
            CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphStem));
        SoundSystemComposerTests.Execute(_cache, MoveSenseToEntryComposer.Build(_cache, intent));
        new FwDataProjectLoader().Save(_cache);

        var after = await ParseAsync(invoker, "ayu", "kaayu");
        Assert.Equal(WordOutcome.Analysed, after["ayu"]);
        Assert.Equal(WordOutcome.Analysed, after["kaayu"]);
    }

    private async Task<Dictionary<string, WordOutcome>> ParseAsync(PanGlossInvoker invoker, params string[] words)
    {
        var outcome = await invoker.RunAsync(new PanGlossRequest.Batch(_cache.ProjectId.Path, words,
            TimeSpan.FromSeconds(10)), "move sense", CancellationToken.None);
        var completed = Assert.IsType<PanGlossOutcome.Completed>(outcome);
        return BatchTsvParser.Parse(completed.Output).ToDictionary(row => row.Word, row => row.Outcome);
    }
}
