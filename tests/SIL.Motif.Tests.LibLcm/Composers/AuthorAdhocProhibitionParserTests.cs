using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.DryRun;
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
    public async Task ATrialCandidateRestoringBothProhibitionKindsHasTheSameReadingChangeAsApply()
    {
        var (prefix, stem) = Seed();
        foreach (var (kind, first, rest) in new (string, ICmObject, ICmObject)[]
        {
            ("morpheme", prefix.MorphoSyntaxAnalysesOC.Single(), stem.MorphoSyntaxAnalysesOC.Single()),
            ("allomorph", prefix.LexemeFormOA, stem.LexemeFormOA),
        })
            SoundSystemComposerTests.Execute(_cache, AuthorAdhocProhibitionComposer.Build(_cache,
                new(kind, CanonicalId.FromGuid(first.Guid), [CanonicalId.FromGuid(rest.Guid)], "anywhere")));
        var rules = _cache.LangProject.MorphologicalDataOA.AdhocCoProhibitionsOC.ToArray();
        Assert.Equal(2, rules.Length);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            foreach (var rule in rules) rule.Disabled = true;
        });
        var proposal = AffixTestSupport.Proposal(rules.SelectMany(rule =>
            EditAdhocProhibitionComposer.Build(_cache, new(CanonicalId.FromGuid(rule.Guid), true, false))).ToArray());
        var loader = new FwDataProjectLoader();
        loader.Save(_cache);
        using var invoker = new PanGlossInvoker();
        Assert.Equal(WordOutcome.Analysed, (await ParseAsync(invoker, "pabago"))["pabago"]);
        var root = Path.Combine(Path.GetTempPath(), "motif-trial-prohibitions", Guid.NewGuid().ToString("N"));
        try
        {
            using var scratch = DryRunScratch.Adopt(
                new ScratchCacheFactory().CreateFromFileCopy(_cache.ProjectId.Path, root), "mixed prohibition Trial");
            var dryRun = ProposalDryRunner.Run(scratch, proposal);
            loader.Save(scratch.PeekCache());
            var candidate = await ParseAtAsync(invoker, scratch.PeekCache().ProjectId.Path, "pabago", "bago");
            Assert.Equal(WordOutcome.NoAnalysis, candidate["pabago"]);
            Assert.Equal(WordOutcome.Analysed, candidate["bago"]);
            ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "motif-tests");
            loader.Save(_cache);
            Assert.Equal(candidate, await ParseAsync(invoker, "pabago", "bago"));
        }
        finally { Directory.Delete(root, recursive: true); }
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

    private (ILexEntry Prefix, ILexEntry Stem) Seed() => ProhibitionParserProject.Seed(_cache);

    private async Task<Dictionary<string, WordOutcome>> ParseAsync(PanGlossInvoker invoker, params string[] words)
        => await ParseAtAsync(invoker, _cache.ProjectId.Path, words);

    private static async Task<Dictionary<string, WordOutcome>> ParseAtAsync(
        PanGlossInvoker invoker, string path, params string[] words)
    {
        var outcome = await invoker.RunAsync(new PanGlossRequest.Batch(path, words,
            null), "author adhoc prohibition", CancellationToken.None);
        var completed = Assert.IsType<PanGlossOutcome.Completed>(outcome);
        return BatchTsvParser.Parse(completed.Output).ToDictionary(row => row.Word, row => row.Outcome);
    }
}
