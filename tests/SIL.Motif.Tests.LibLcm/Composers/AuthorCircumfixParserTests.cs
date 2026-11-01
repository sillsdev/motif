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

/// <summary>A circumfix authored through the composer parses a stem that carries both parts, and neither part alone does.</summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group2)]
public sealed class AuthorCircumfixParserTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly LcmCache _cache = pristine.NewScratch();

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    [RealParserFact]
    public async Task ACircumfixParsesAStemWithBothPartsAndNeitherPartAloneAttaches()
    {
        RealParserProject.PrepareForParsing(_cache, "k", "a", "n", "b", "g", "o");
        CanonicalId verb = default, noun = default;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var services = _cache.ServiceLocator;
            IPartOfSpeech Pos(string name)
            {
                var pos = services.GetInstance<IPartOfSpeechFactory>().Create();
                _cache.LangProject.PartsOfSpeechOA.PossibilitiesOS.Add(pos);
                pos.Name.set_String(_cache.DefaultAnalWs, name);
                return pos;
            }
            var verbPos = Pos("Verb");
            var nounPos = Pos("Noun");
            verb = CanonicalId.FromGuid(verbPos.Guid);
            noun = CanonicalId.FromGuid(nounPos.Guid);
            services.GetInstance<ILexEntryFactory>().Create(
                services.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphStem),
                TsStringUtils.MakeString("bago", _cache.DefaultVernWs), "stem", SandboxGenericMSA.Create(MsaType.kStem, verbPos));
        });
        new FwDataProjectLoader().Save(_cache);
        using var invoker = new PanGlossInvoker();
        var before = await ParseAsync(invoker, "bago", "kabagoan", "kabago", "bagoan");
        Assert.Equal(WordOutcome.Analysed, before["bago"]);
        Assert.Equal(WordOutcome.NoAnalysis, before["kabagoan"]);

        var ws = _cache.WritingSystemFactory.GetStrFromWs(_cache.DefaultVernWs);
        var analysis = _cache.WritingSystemFactory.GetStrFromWs(_cache.DefaultAnalWs);
        SoundSystemComposerTests.Execute(_cache, AuthorCircumfixComposer.Build(_cache, new("ka", "an", ws, "abstract", analysis,
            new LexicalEntryMsa(LexicalEntryMsaKind.Derivational, verb, noun))));
        new FwDataProjectLoader().Save(_cache);

        var after = await ParseAsync(invoker, "bago", "kabagoan", "kabago", "bagoan");
        Assert.Equal(WordOutcome.Analysed, after["kabagoan"]);
        Assert.Equal(WordOutcome.NoAnalysis, after["kabago"]);
        Assert.Equal(WordOutcome.NoAnalysis, after["bagoan"]);
        Assert.Equal(WordOutcome.Analysed, after["bago"]);
    }

    private async Task<Dictionary<string, WordOutcome>> ParseAsync(PanGlossInvoker invoker, params string[] words)
    {
        var outcome = await invoker.RunAsync(new PanGlossRequest.Batch(_cache.ProjectId.Path, words,
            TimeSpan.FromSeconds(10)), "author circumfix", CancellationToken.None);
        var completed = Assert.IsType<PanGlossOutcome.Completed>(outcome);
        return BatchTsvParser.Parse(completed.Output).ToDictionary(row => row.Word, row => row.Outcome);
    }
}
