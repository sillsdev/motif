using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Model.DryRun;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.AppliedLog;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.DryRun;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Composers;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group2)]
public sealed class SetNoDefaultCompoundingParserTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly LcmCache _cache = pristine.NewScratch();

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    [RealParserFact]
    public async Task TheParserStopsAcceptingAWordThatOnlyDefaultCompoundingBuilt()
    {
        var ws = _cache.DefaultVernWs;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var stem = _cache.ServiceLocator.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphStem);
            foreach (var form in new[] { "ka", "ta" })
                _cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create(stem,
                    TsStringUtils.MakeString(form, ws), "gloss " + form, new SandboxGenericMSA { MsaType = MsaType.kStem });
        });
        RealParserProject.PrepareForParsing(_cache, "k", "t", "a");
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            _cache.LangProject.MorphologicalDataOA.ParserParameters = "<ParserParameters><HC><Strata /></HC></ParserParameters>");
        new FwDataProjectLoader().Save(_cache);

        using var invoker = new PanGlossInvoker();
        var before = await ParseAsync(invoker, "ka", "kata");
        Assert.Equal(WordOutcome.Analysed, before["ka"]);
        Assert.Equal(WordOutcome.Analysed, before["kata"]);

        SoundSystemComposerTests.Execute(_cache, SetNoDefaultCompoundingComposer.Build(_cache, new(false, true)));
        new FwDataProjectLoader().Save(_cache);

        var after = await ParseAsync(invoker, "ka", "kata");
        Assert.Equal(WordOutcome.Analysed, after["ka"]);
        Assert.Equal(WordOutcome.NoAnalysis, after["kata"]);
    }

    private async Task<Dictionary<string, WordOutcome>> ParseAsync(PanGlossInvoker invoker, params string[] words)
    {
        var outcome = await invoker.RunAsync(new PanGlossRequest.Batch(_cache.ProjectId.Path, words,
            TimeSpan.FromSeconds(10)), "no default compounding", CancellationToken.None);
        var completed = Assert.IsType<PanGlossOutcome.Completed>(outcome);
        return BatchTsvParser.Parse(completed.Output).ToDictionary(row => row.Word, row => row.Outcome);
    }
}
