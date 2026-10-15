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
public sealed class WfiAnalysisAgentEvaluationOperationsTests : IDisposable
{
    private const string AgentName = "Source validation (Motif)";
    private const string Version = "swh-sources-2026-10";
    private readonly LcmCache _cache;

    public WfiAnalysisAgentEvaluationOperationsTests(PristineProjectFixture pristine) => _cache = pristine.NewScratch();

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    [Fact]
    public void ANewNonHumanAgentApprovesAnAnalysisAndNoHumanDecisionMoves()
    {
        var (wordform, analysis) = Seed("sita");
        var humanBefore = HumanOpinion(analysis);
        var spelling = wordform.SpellingStatus;
        var agentId = CanonicalId.Mint();
        var proposal = Add(analysis, agentId, AgentName, Version, "approves");

        var dryRun = ScratchDryRun.Of(_cache, proposal);
        var effect = Assert.Single(dryRun.ExpectedEffects);
        Assert.Equal("none", effect.Before["opinion:" + agentId.Value]);
        Assert.Equal("approves", effect.After["opinion:" + agentId.Value]);
        Assert.Equal($"{AgentName}|{Version}|human=False", effect.After["agent:" + agentId.Value]);
        Assert.False(_cache.ServiceLocator.GetInstance<ICmObjectRepository>().TryGetObject(agentId.ToGuid(), out _));

        ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "tester");

        var agent = Assert.IsAssignableFrom<ICmAgent>(_cache.ServiceLocator.GetInstance<ICmObjectRepository>()
            .GetObject(agentId.ToGuid()));
        Assert.False(agent.Human);
        Assert.Equal(AgentName, agent.Name.get_String(_cache.DefaultAnalWs).Text);
        Assert.Equal(Version, agent.Version);
        Assert.Contains(agent, _cache.LangProject.AnalyzingAgentsOC);
        Assert.Equal(Opinions.approves, analysis.GetAgentOpinion(agent));
        Assert.Equal(humanBefore, HumanOpinion(analysis));
        Assert.DoesNotContain(analysis, wordform.HumanApprovedAnalyses);
        Assert.Equal(spelling, wordform.SpellingStatus);
    }

    [Fact]
    public void ASecondAnalysisReusesTheSameAgentAndADisapprovalReplacesAnApproval()
    {
        var (_, first) = Seed("sita");
        var (_, second) = Seed("mutu");
        var agentId = CanonicalId.Mint();
        Apply(Add(first, agentId, AgentName, Version, "approves"));
        var agents = _cache.LangProject.AnalyzingAgentsOC.Count;

        Apply(Add(second, agentId, AgentName, Version, "approves"));
        Apply(Add(first, agentId, AgentName, Version, "disapproves"));

        Assert.Equal(agents, _cache.LangProject.AnalyzingAgentsOC.Count);
        var agent = (ICmAgent)_cache.ServiceLocator.GetInstance<ICmObjectRepository>().GetObject(agentId.ToGuid());
        Assert.Equal(Opinions.disapproves, first.GetAgentOpinion(agent));
        Assert.Equal(Opinions.approves, second.GetAgentOpinion(agent));
    }

    [Fact]
    public void AHumanApprovalAlreadyInTheProjectIsUntouched()
    {
        var (_, analysis) = Seed("sita");
        var (_, other) = Seed("mutu");
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            _cache.LangProject.DefaultUserAgent.SetEvaluation(other, Opinions.approves));
        var human = HumanOpinion(other);

        Apply(Add(analysis, CanonicalId.Mint(), AgentName, Version, "approves"));

        Assert.Equal(human, HumanOpinion(other));
        Assert.Equal(Opinions.approves, other.GetAgentOpinion(_cache.LangProject.DefaultUserAgent));
    }

    [Fact]
    public void TheBuiltInAgentsAreRefusedByIdentity()
    {
        var (_, analysis) = Seed("sita");
        foreach (var builtIn in new[] { _cache.LangProject.DefaultUserAgent, _cache.LangProject.DefaultParserAgent })
        {
            var failure = Assert.ThrowsAny<Exception>(() => ScratchDryRun.Of(_cache,
                Add(analysis, CanonicalId.FromGuid(builtIn.Guid), AgentName, Version, "approves")));
            Assert.Contains("built-in agent", failure.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AHumanAgentAndANonAgentIdentityAreRefused()
    {
        var (_, analysis) = Seed("sita");
        ICmAgent human = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            human = _cache.ServiceLocator.GetInstance<ICmAgentFactory>().Create();
            _cache.LangProject.AnalyzingAgentsOC.Add(human);
            human.Human = true;
            human.Name.set_String(_cache.DefaultAnalWs, TsStringUtils.MakeString(AgentName, _cache.DefaultAnalWs));
            human.Version = Version;
        });

        var humanFailure = Assert.ThrowsAny<Exception>(() => ScratchDryRun.Of(_cache,
            Add(analysis, CanonicalId.FromGuid(human.Guid), AgentName, Version, "approves")));
        var classFailure = Assert.ThrowsAny<Exception>(() => ScratchDryRun.Of(_cache,
            Add(analysis, CanonicalId.FromGuid(_cache.LangProject.Guid), AgentName, Version, "approves")));

        Assert.Contains("human agent", humanFailure.Message, StringComparison.Ordinal);
        Assert.Contains("semantic conflict", classFailure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ANewVersionUnderTheSameIdentityIsRefusedBecauseAVersionIsAnAgent()
    {
        var (_, analysis) = Seed("sita");
        var agentId = CanonicalId.Mint();
        Apply(Add(analysis, agentId, AgentName, Version, "approves"));

        var failure = Assert.ThrowsAny<Exception>(() => ScratchDryRun.Of(_cache,
            Add(analysis, agentId, AgentName, "swh-sources-2026-11", "approves")));

        Assert.Contains("new version is a new agent identity", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHumanAgentsNameIsRefused()
    {
        var (_, analysis) = Seed("sita");
        var humanName = _cache.LangProject.DefaultUserAgent.Name.get_String(_cache.DefaultAnalWs).Text;
        if (string.IsNullOrEmpty(humanName)) return;

        var failure = Assert.ThrowsAny<Exception>(() => ScratchDryRun.Of(_cache,
            Add(analysis, CanonicalId.Mint(), humanName, Version, "approves")));

        Assert.Contains("human agent's name", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"member":"approves"}""")]
    [InlineData("""{"agent":{"id":"AAAAAAAAAAAAAAAAAAAAAA","name":"N","version":"1"}}""")]
    [InlineData("""{"agent":{"id":"AAAAAAAAAAAAAAAAAAAAAA","name":"N","version":"1"},"member":"maybe"}""")]
    [InlineData("""{"agent":{"id":"AAAAAAAAAAAAAAAAAAAAAA","name":"N","version":"1"},"member":"approves","by":"x"}""")]
    [InlineData("""{"agent":{"id":"AAAAAAAAAAAAAAAAAAAAAA","name":"N","version":"1","human":false},"member":"approves"}""")]
    [InlineData("""{"agent":{"id":"AAAAAAAAAAAAAAAAAAAAAA","name":"","version":"1"},"member":"approves"}""")]
    [InlineData("""{"agent":{"id":"AAAAAAAAAAAAAAAAAAAAAA","name":"N","version":" 1"},"member":"approves"}""")]
    [InlineData("""{"agent":{"id":"AAAAAAAAAAAAAAAAAAAAAA","name":"N","version":"1\u0001"},"member":"approves"}""")]
    [InlineData("""{"agent":{"id":"AAAAAAAAAAAAAAAAAAAAAA","name":"é","version":"1"},"member":"approves"}""")]
    [InlineData("""{"agent":{"name":"N","version":"1"},"member":"approves"}""")]
    public void ThePayloadIsClosed(string json)
    {
        using var document = JsonDocument.Parse(json);

        Assert.Throws<ContractParseException>(() => AgentEvaluationPayload.Parse(document.RootElement));
    }

    private void Apply(Proposal proposal)
    {
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "tester");
    }

    private static Opinions HumanOpinion(IWfiAnalysis analysis) =>
        analysis.GetAgentOpinion(analysis.Cache.LangProject.DefaultUserAgent);

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

    private static Proposal Add(IWfiAnalysis analysis, CanonicalId agent, string name, string version, string member)
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            agent = new { id = agent.Value, name, version },
            member,
        }));
        return new Proposal(new Dictionary<string, string> { ["analysis"] = "1.0" }, CanonicalId.Mint(), null,
            [new OperationEnvelope(CanonicalId.Mint(), WfiAnalysisAgentEvaluationOperationKinds.Add, null,
                CanonicalId.FromGuid(analysis.Guid), json.RootElement.Clone())]);
    }
}
