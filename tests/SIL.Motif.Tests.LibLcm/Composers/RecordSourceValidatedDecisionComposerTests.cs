using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Model.DryRun;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Composers;

[Collection(LcmCacheParallelCollections.Group1)]
public sealed class RecordSourceValidatedDecisionComposerTests : IDisposable
{
    private const string Verb = "compose-record-source-validated-decision";
    private readonly LcmCache _cache;
    private readonly IWfiWordform _wordform = null!;
    private readonly IWfiAnalysis _analysis = null!;

    public RecordSourceValidatedDecisionComposerTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        IWfiWordform wordform = null!;
        IWfiAnalysis analysis = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            wordform = _cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString("sita", _cache.DefaultVernWs));
            analysis = _cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
            wordform.AnalysesOC.Add(analysis);
        });
        _wordform = wordform;
        _analysis = analysis;
    }

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    private static string Id(ICmObject item) => CanonicalId.FromGuid(item.Guid).Value;

    private const string ApprovedEvidence =
        """{"tier":"source-approved","sources":[{"kind":"dictionary","source":"Kamusi","locator":"sita"},{"kind":"grammar","source":"NALRC 2001","locator":"4"},{"kind":"corpus","source":"swh build","locator":"42"}],"detail":{"corpusCount":42},"stagedBy":"stager"}""";

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private string Approve(string member = "approves") =>
        $$"""{"target":"{{Id(_analysis)}}","agent":{"name":"Source validation (Motif)","version":"swh-2026-10"},"member":"{{member}}","evidence":{{ApprovedEvidence}}}""";

    [Fact]
    public void AnApprovalLowersToOneAgentEvaluationCarryingItsEvidence()
    {
        var operations = ProposalCommands.DraftComposers[Verb].Compose(_cache, Json(Approve()), CanonicalId.Mint(),
            () => CanonicalId.Mint());

        var operation = Assert.Single(operations);
        Assert.Equal(WfiAnalysisAgentEvaluationOperationKinds.Add, operation.Kind);
        Assert.Equal(Id(_analysis), operation.Target!.Value.Value);
        var payload = AgentEvaluationPayload.Parse(operation.After!.Value);
        Assert.True(payload.Approves);
        Assert.Equal("swh-2026-10", payload.Version);
        Assert.Equal("source-approved", DecisionEvidence.FromProvenance(operation.Provenance)!.Tier);
    }

    [Fact]
    public void TheAgentIdentityIsDeterministicPerProjectNameAndVersion()
    {
        var first = RecordSourceValidatedDecisionComposer.AgentIdentity(_cache, "A", "1");

        Assert.Equal(first, RecordSourceValidatedDecisionComposer.AgentIdentity(_cache, "A", "1"));
        Assert.NotEqual(first, RecordSourceValidatedDecisionComposer.AgentIdentity(_cache, "A", "2"));
        Assert.NotEqual(first, RecordSourceValidatedDecisionComposer.AgentIdentity(_cache, "B", "1"));
    }

    [Fact]
    public void EvidenceAloneLowersToAnEvidenceRecordForAWordform()
    {
        var authored = "{\"target\":\"" + Id(_wordform) + "\",\"evidence\":{\"tier\":\"misspelled-typo\"," +
            "\"sources\":[],\"detail\":{\"intendedForm\":\"sita\",\"editType\":\"vowel\"}}}";

        var operation = Assert.Single(ProposalCommands.DraftComposers[Verb].Compose(_cache, Json(authored),
            CanonicalId.Mint(), () => CanonicalId.Mint()));

        Assert.Equal(DecisionEvidenceOperationKinds.Record, operation.Kind);
        Assert.Equal("misspelled-typo", DecisionEvidence.FromProvenance(operation.Provenance)!.Tier);
    }

    [Fact]
    public void TheStagedDecisionAppliesAsANonHumanApprovalAndLeavesPeopleAlone()
    {
        var operations = ProposalCommands.DraftComposers[Verb].Compose(_cache, Json(Approve()), CanonicalId.Mint(),
            () => CanonicalId.Mint());
        var proposal = new Proposal(new Dictionary<string, string> { ["analysis"] = "1.0" }, CanonicalId.Mint(), null,
            operations);
        var spelling = _wordform.SpellingStatus;

        var dryRun = ScratchDryRun.Of(_cache, proposal);
        ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "tester");

        var agent = Assert.Single(_cache.LangProject.AnalyzingAgentsOC, candidate => candidate.Version == "swh-2026-10");
        Assert.False(agent.Human);
        Assert.Equal(Opinions.approves, _analysis.GetAgentOpinion(agent));
        Assert.Equal(Opinions.noopinion, _analysis.GetAgentOpinion(_cache.LangProject.DefaultUserAgent));
        Assert.Equal(spelling, _wordform.SpellingStatus);
    }

    [Fact]
    public void StagingUnderTheHumanAgentsNameIsRefusedAtDryRunAndNeverTouchesThePersonsOpinion()
    {
        var human = _cache.LangProject.DefaultUserAgent;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            if (string.IsNullOrEmpty(human.Name.get_String(_cache.DefaultAnalWs).Text))
                human.Name.set_String(_cache.DefaultAnalWs, TsStringUtils.MakeString("Person", _cache.DefaultAnalWs));
        });
        var humanName = human.Name.get_String(_cache.DefaultAnalWs).Text;
        var authored = Approve().Replace("Source validation (Motif)", humanName);
        var operations = ProposalCommands.DraftComposers[Verb].Compose(_cache, Json(authored), CanonicalId.Mint(),
            () => CanonicalId.Mint());
        var proposal = new Proposal(new Dictionary<string, string> { ["analysis"] = "1.0" }, CanonicalId.Mint(), null,
            operations);

        var refusal = Assert.ThrowsAny<Exception>(() => ScratchDryRun.Of(_cache, proposal)).GetBaseException();

        Assert.Equal(DecisionRefusalCodes.AgentHumanName, Assert.IsType<DecisionRefusalException>(refusal).Code);
        Assert.Equal(Opinions.noopinion, _analysis.GetAgentOpinion(human));
    }

    [Fact]
    public void AnApprovalAndATypoRecordChainedInOneProposalApplyWithoutTouchingPeopleOrSpelling()
    {
        IWfiWordform typo = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            typo = _cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString("mutu", _cache.DefaultVernWs)));
        var typoRecord = "{\"target\":\"" + Id(typo) + "\",\"evidence\":{\"tier\":\"misspelled-typo\"," +
            "\"sources\":[],\"detail\":{\"intendedForm\":\"mutu\",\"editType\":\"vowel\"}}}";
        var compose = ProposalCommands.DraftComposers[Verb];
        var operations = compose.Compose(_cache, Json(Approve()), CanonicalId.Mint(), () => CanonicalId.Mint())
            .Concat(compose.Compose(_cache, Json(typoRecord), CanonicalId.Mint(), () => CanonicalId.Mint())).ToArray();
        var proposal = new Proposal(new Dictionary<string, string> { ["analysis"] = "1.0" }, CanonicalId.Mint(), null,
            operations);
        var spellingBefore = (_wordform.SpellingStatus, typo.SpellingStatus);

        var dryRun = ScratchDryRun.Of(_cache, proposal);
        ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "tester");

        var agent = Assert.Single(_cache.LangProject.AnalyzingAgentsOC, candidate => candidate.Version == "swh-2026-10");
        Assert.False(agent.Human);
        Assert.Equal(Opinions.approves, _analysis.GetAgentOpinion(agent));
        Assert.Empty(_wordform.HumanApprovedAnalyses);
        Assert.Empty(typo.HumanApprovedAnalyses);
        Assert.Equal(Opinions.noopinion, _analysis.GetAgentOpinion(_cache.LangProject.DefaultUserAgent));
        Assert.Equal(spellingBefore, (_wordform.SpellingStatus, typo.SpellingStatus));
    }

    [Theory]
    [InlineData("agentWithoutMember")]
    [InlineData("memberWithoutAgent")]
    [InlineData("badMember")]
    [InlineData("extraProperty")]
    [InlineData("noEvidence")]
    [InlineData("typoWithoutDetail")]
    public void AMalformedIntentIsRefused(string problem)
    {
        var id = Id(_analysis);
        var agent = """{"name":"N","version":"1"}""";
        var typo = """{"tier":"misspelled-typo","sources":[]}""";
        var authored = problem switch
        {
            "agentWithoutMember" => $$"""{"target":"{{id}}","agent":{{agent}},"evidence":{{ApprovedEvidence}}}""",
            "memberWithoutAgent" => $$"""{"target":"{{id}}","member":"approves","evidence":{{ApprovedEvidence}}}""",
            "badMember" => $$"""{"target":"{{id}}","agent":{{agent}},"member":"maybe","evidence":{{ApprovedEvidence}}}""",
            "extraProperty" => $$"""{"target":"{{id}}","evidence":{{ApprovedEvidence}},"by":"x"}""",
            "noEvidence" => $$"""{"target":"{{id}}"}""",
            _ => $$"""{"target":"{{id}}","evidence":{{typo}}}""",
        };

        Assert.ThrowsAny<Exception>(() => ProposalCommands.DraftComposers[Verb].Compose(_cache, Json(authored),
            CanonicalId.Mint(), () => CanonicalId.Mint()));
    }

    [Fact]
    public void ADecisionOnAWordformOrAnUnknownIdentityIsRefused()
    {
        var onWordform = $$"""{"target":"{{Id(_wordform)}}","agent":{"name":"N","version":"1"},"member":"approves","evidence":{{ApprovedEvidence}}}""";
        var unknown = $$"""{"target":"{{CanonicalId.Mint().Value}}","evidence":{{ApprovedEvidence}}}""";

        var wrongClass = Assert.ThrowsAny<Exception>(() => ProposalCommands.DraftComposers[Verb].Compose(_cache,
            Json(onWordform), CanonicalId.Mint(), () => CanonicalId.Mint()));
        var missing = Assert.ThrowsAny<Exception>(() => ProposalCommands.DraftComposers[Verb].Compose(_cache,
            Json(unknown), CanonicalId.Mint(), () => CanonicalId.Mint()));

        Assert.Contains("analysis", wrongClass.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("does not exist", missing.Message, StringComparison.Ordinal);
    }
}
