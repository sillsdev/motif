using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Model.DryRun;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Runner;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group1)]
public sealed class DecisionEvidenceOperationsTests : IDisposable
{
    private const string Evidence =
        """{"type":"decisionEvidence","tier":"misspelled-typo","sources":[],"detail":{"intendedForm":"sita","editType":"vowel"}}""";
    private readonly LcmCache _cache;

    public DecisionEvidenceOperationsTests(PristineProjectFixture pristine) => _cache = pristine.NewScratch();

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    [Fact]
    public void RecordingEvidenceForAWordformChangesNothingInTheProject()
    {
        var (wordform, analysis) = Seed("sitaa");
        var spelling = wordform.SpellingStatus;
        var humanBefore = analysis.GetAgentOpinion(_cache.LangProject.DefaultUserAgent);
        var agents = _cache.LangProject.AnalyzingAgentsOC.Count;
        var proposal = Record(wordform.Guid, Evidence);

        var dryRun = ScratchDryRun.Of(_cache, proposal);
        var effect = Assert.Single(dryRun.ExpectedEffects);
        Assert.Equal(effect.Before, effect.After);
        Assert.Equal("WfiWordform", effect.After["target"]);
        var receipt = ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "tester");

        Assert.Single(receipt.ActualEffects);
        Assert.Equal(spelling, wordform.SpellingStatus);
        Assert.Equal(humanBefore, analysis.GetAgentOpinion(_cache.LangProject.DefaultUserAgent));
        Assert.Equal(agents, _cache.LangProject.AnalyzingAgentsOC.Count);
        Assert.Empty(wordform.HumanApprovedAnalyses);
    }

    [Fact]
    public void AnAnalysisIsAcceptedAsATarget()
    {
        var (_, analysis) = Seed("mutu");

        var dryRun = ScratchDryRun.Of(_cache, Record(analysis.Guid, Evidence));

        Assert.Equal("WfiAnalysis", Assert.Single(dryRun.ExpectedEffects).After["target"]);
    }

    [Fact]
    public void WithoutEvidenceOrOnAnotherClassItIsRefused()
    {
        var (wordform, _) = Seed("sita");

        var bare = Assert.ThrowsAny<Exception>(() => ScratchDryRun.Of(_cache, Record(wordform.Guid, null)));
        var wrong = Assert.ThrowsAny<Exception>(() => ScratchDryRun.Of(_cache, Record(_cache.LangProject.Guid, Evidence)));
        var missing = Assert.ThrowsAny<Exception>(() => ScratchDryRun.Of(_cache, Record(Guid.NewGuid(), Evidence)));

        Assert.Contains("decisionEvidence", bare.Message, StringComparison.Ordinal);
        Assert.Contains("wordform or analysis", wrong.Message, StringComparison.Ordinal);
        Assert.False(string.IsNullOrEmpty(missing.Message));
    }

    [Fact]
    public void TwoRecordsForOneTargetInOneProposalConflict()
    {
        var (wordform, _) = Seed("sita");
        var one = Record(wordform.Guid, Evidence).Operations[0];
        var two = Record(wordform.Guid, Evidence).Operations[0];
        var proposal = new Proposal(new Dictionary<string, string> { ["analysis"] = "1.0" }, CanonicalId.Mint(), null,
            [one, two]);

        Assert.ThrowsAny<Exception>(() => ScratchDryRun.Of(_cache, proposal));
    }

    [Theory]
    [InlineData("""{"tier":"undecided"}""")]
    [InlineData("""{"note":1}""")]
    public void ThePayloadIsClosedAndEmpty(string json)
    {
        using var document = JsonDocument.Parse(json);

        Assert.Throws<ContractParseException>(() => DecisionEvidenceRecordPayload.Parse(document.RootElement));
    }

    private (IWfiWordform Wordform, IWfiAnalysis Analysis) Seed(string form)
    {
        IWfiWordform wordform = null!;
        IWfiAnalysis analysis = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            wordform = _cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString(form, _cache.DefaultVernWs));
            analysis = _cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
            wordform.AnalysesOC.Add(analysis);
        });
        return (wordform, analysis);
    }

    private static Proposal Record(Guid target, string? evidence)
    {
        using var after = JsonDocument.Parse("{}");
        var provenance = evidence is null ? [] : new[] { JsonDocument.Parse(evidence).RootElement.Clone() };
        return new Proposal(new Dictionary<string, string> { ["analysis"] = "1.0" }, CanonicalId.Mint(), null,
            [new OperationEnvelope(CanonicalId.Mint(), DecisionEvidenceOperationKinds.Record, null,
                CanonicalId.FromGuid(target), after.RootElement.Clone(), provenance: provenance)]);
    }
}
