using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group2)]
public sealed class RewriteLexemeFormParserTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly LcmCache _cache = pristine.NewScratch();

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    [RealParserFact]
    public async Task AWordWhoseLexemeFormCarriedAStressMarkParsesAfterApply()
    {
        var ws = _cache.DefaultVernWs;
        ILexEntry entry = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var stem = _cache.ServiceLocator.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphStem);
            entry = _cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create(stem,
                TsStringUtils.MakeString("ba\u0301go", ws), "gloss", new SandboxGenericMSA { MsaType = MsaType.kStem });
        });
        RealParserProject.PrepareForParsing(_cache, "b", "a", "g", "o");
        new FwDataProjectLoader().Save(_cache);

        using var invoker = new PanGlossInvoker();
        Assert.Equal(WordOutcome.NoAnalysis, (await ParseAsync(invoker, "bago"))["bago"]);

        var intent = new RewriteLexemeFormIntent(
            [new(CanonicalId.FromGuid(entry.Guid), _cache.ServiceLocator.WritingSystems.DefaultVernacularWritingSystem.Id,
                "ba\u0301go", null)], SetEmptyCitationForm: true, FoldStressMarks: true);
        SoundSystemComposerTests.Execute(_cache, RewriteLexemeFormComposer.Build(_cache, intent));
        new FwDataProjectLoader().Save(_cache);

        Assert.Equal(WordOutcome.Analysed, (await ParseAsync(invoker, "bago"))["bago"]);
    }

    private async Task<Dictionary<string, WordOutcome>> ParseAsync(PanGlossInvoker invoker, params string[] words)
    {
        var outcome = await invoker.RunAsync(new PanGlossRequest.Batch(_cache.ProjectId.Path, words,
            TimeSpan.FromSeconds(10)), "rewrite lexeme form", CancellationToken.None);
        var completed = Assert.IsType<PanGlossOutcome.Completed>(outcome);
        return BatchTsvParser.Parse(completed.Output).ToDictionary(row => row.Word, row => row.Outcome);
    }
}
