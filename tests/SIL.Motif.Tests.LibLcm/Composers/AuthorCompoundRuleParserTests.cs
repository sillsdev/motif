using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Composers;

/// <summary>With default compounding off, a word of two noun stems has no analysis until a compound rule joins
/// the two categories, as FieldWorks' HermitCrab loader reads MoEndoCompound and MoExoCompound.</summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group2)]
public sealed class AuthorCompoundRuleParserTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly LcmCache _cache = pristine.NewScratch();

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    [RealParserFact]
    public async Task AnEndocentricRuleMakesTwoNounStemsOneAnalysableWord()
    {
        var noun = Noun();
        using var invoker = new PanGlossInvoker();
        Assert.Equal(WordOutcome.NoAnalysis, (await ParseAsync(invoker, "bagobago"))["bagobago"]);
        SoundSystemComposerTests.Execute(_cache, AuthorCompoundRuleComposer.Build(_cache,
            new(CompoundRulePayload.Endocentric, null, noun, noun, true, null)));
        new FwDataProjectLoader().Save(_cache);
        var after = await ParseAsync(invoker, "bagobago", "bago");
        Assert.Equal(WordOutcome.Analysed, after["bagobago"]);
        Assert.Equal(WordOutcome.Analysed, after["bago"]);
    }

    [RealParserFact]
    public async Task AnExocentricRuleMakesTwoNounStemsOneAnalysableWord()
    {
        var noun = Noun();
        using var invoker = new PanGlossInvoker();
        Assert.Equal(WordOutcome.NoAnalysis, (await ParseAsync(invoker, "bagobago"))["bagobago"]);
        SoundSystemComposerTests.Execute(_cache, AuthorCompoundRuleComposer.Build(_cache,
            new(CompoundRulePayload.Exocentric, null, noun, noun, true, noun)));
        new FwDataProjectLoader().Save(_cache);
        Assert.Equal(WordOutcome.Analysed, (await ParseAsync(invoker, "bagobago"))["bagobago"]);
    }

    private CanonicalId Noun()
    {
        var (_, stem) = ProhibitionParserProject.Seed(_cache);
        return CanonicalId.FromGuid(((IMoStemMsa)stem.MorphoSyntaxAnalysesOC.Single()).PartOfSpeechRA.Guid);
    }

    private async Task<Dictionary<string, WordOutcome>> ParseAsync(PanGlossInvoker invoker, params string[] words)
    {
        var outcome = await invoker.RunAsync(new PanGlossRequest.Batch(_cache.ProjectId.Path, words, null),
            "author compound rule", CancellationToken.None);
        var completed = Assert.IsType<PanGlossOutcome.Completed>(outcome);
        return BatchTsvParser.Parse(completed.Output).ToDictionary(row => row.Word, row => row.Outcome);
    }
}
