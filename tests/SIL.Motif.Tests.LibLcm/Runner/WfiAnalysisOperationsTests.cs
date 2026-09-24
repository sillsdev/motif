using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Model.DryRun;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.AppliedLog;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Snapshotting;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using Xunit;
using ContractIntentDigest = SIL.Motif.Contract.Canonicalization.IntentDigest;

namespace SIL.Motif.Tests.Runner;

[Collection(TestFixtures.LcmCacheTestCollection.Name)]
public sealed class WfiAnalysisOperationsTests : IDisposable
{
    private readonly LcmCache _cache;
    private readonly IWfiWordform _wordform;
    private readonly IWfiAnalysis _analysis;
    private readonly SeededProject _seed;

    public WfiAnalysisOperationsTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        _seed = pristine.Seed;
        IWfiWordform wordform = null!;
        IWfiAnalysis analysis = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            wordform = _cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString("motif-analysis-test", _cache.DefaultVernWs));
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

    [Fact]
    public void AddHumanApproval_RoundTripsThroughDryRunAndApply()
    {
        var proposal = Proposal("analysis/wfiAnalysis/addRefEvaluations",
            CanonicalId.FromGuid(_analysis.Guid), new { member = "defaultUserApproves" });

        var dryRun = ScratchDryRun.Of(_cache, proposal);
        Assert.Empty(Assert.Single(dryRun.ExpectedEffects).Before);
        Assert.Equal(Opinions.noopinion, _analysis.GetAgentOpinion(_cache.LangProject.DefaultUserAgent));

        var receipt = ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "tester");

        Assert.Equal(Opinions.approves, _analysis.GetAgentOpinion(_cache.LangProject.DefaultUserAgent));
        Assert.Single(Assert.Single(receipt.ActualEffects).After);
    }

    [Fact]
    public void CreateParserCandidate_RoundTripsThroughDryRunAndApply()
    {
        var source = _cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(_seed.FirstEntryId);
        var form = source.LexemeFormOA!;
        var msa = source.MorphoSyntaxAnalysesOC.First();
        var proposedAnalysis = CanonicalId.Mint();
        var proposal = Proposal("analysis/wfiWordform/createAnalyses",
            CanonicalId.FromGuid(_wordform.Guid),
            new { morphs = new[] { new { form = CanonicalId.FromGuid(form.Guid).Value,
                msa = CanonicalId.FromGuid(msa.Guid).Value } } }, proposedAnalysis);

        var dryRun = ScratchDryRun.Of(_cache, proposal);
        Assert.Single(_wordform.AnalysesOC);

        var receipt = ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "tester");

        var created = Assert.Single(_wordform.AnalysesOC.Where(item => item.Guid == proposedAnalysis.ToGuid()));
        var bundle = Assert.Single(created.MorphBundlesOS);
        Assert.Equal(form.Guid, bundle.MorphRA!.Guid);
        Assert.Equal(msa.Guid, bundle.MsaRA!.Guid);
        Assert.Equal(Opinions.approves, created.GetAgentOpinion(_cache.LangProject.DefaultParserAgent));
        Assert.Single(receipt.ActualEffects);
    }

    [Fact]
    public void ParserCandidate_FilesReferencesGuessAndOnlyTheParserOpinion()
    {
        var source = _cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(_seed.FirstEntryId);
        ILexEntryInflType inflType = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var lexDb = _cache.LangProject.LexDbOA;
            if (lexDb.VariantEntryTypesOA is null)
                lexDb.VariantEntryTypesOA = _cache.ServiceLocator.GetInstance<ICmPossibilityListFactory>().Create();
            inflType = _cache.ServiceLocator.GetInstance<ILexEntryInflTypeFactory>().Create();
            lexDb.VariantEntryTypesOA.PossibilitiesOS.Add(inflType);
        });
        var id = CanonicalId.Mint();
        var proposal = Proposal("analysis/wfiWordform/createAnalyses", CanonicalId.FromGuid(_wordform.Guid),
            new { morphs = new object[]
            {
                new { form = CanonicalId.FromGuid(source.LexemeFormOA!.Guid).Value,
                    msa = CanonicalId.FromGuid(source.MorphoSyntaxAnalysesOC.First().Guid).Value,
                    inflType = CanonicalId.FromGuid(inflType.Guid).Value },
                new { guessedString = "unknown-root" },
            } }, id);

        var dryRun = ScratchDryRun.Of(_cache, proposal);
        var preview = Assert.Single(dryRun.ExpectedEffects);
        Assert.Contains(preview.After.Values, value => value.Contains("unknown-root", StringComparison.Ordinal));
        var receipt = ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "tester");

        var created = Assert.Single(_wordform.AnalysesOC, analysis => analysis.Guid == id.ToGuid());
        Assert.Equal(2, created.MorphBundlesOS.Count);
        Assert.Equal(source.LexemeFormOA.Guid, created.MorphBundlesOS[0].MorphRA!.Guid);
        Assert.Equal(source.MorphoSyntaxAnalysesOC.First().Guid, created.MorphBundlesOS[0].MsaRA!.Guid);
        Assert.Equal(inflType.Guid, created.MorphBundlesOS[0].InflTypeRA!.Guid);
        Assert.Equal("unknown-root", created.MorphBundlesOS[1].Form.get_String(_cache.DefaultVernWs)?.Text);
        Assert.Equal(Opinions.approves, created.GetAgentOpinion(_cache.LangProject.DefaultParserAgent));
        Assert.Equal(Opinions.noopinion, created.GetAgentOpinion(_cache.LangProject.DefaultUserAgent));
        Assert.Contains(Assert.Single(receipt.ActualEffects).After.Values,
            value => value.Contains("unknown-root", StringComparison.Ordinal));
    }

    [Fact]
    public void HumanOpinionPayload_RejectsUnknownProperties()
    {
        var proposal = Proposal("analysis/wfiAnalysis/addRefEvaluations",
            CanonicalId.FromGuid(_analysis.Guid), new { member = "defaultUserApproves", extra = true });

        Assert.Throws<ContractParseException>(() => ScratchDryRun.Of(_cache, proposal));
    }

    [Fact]
    public void ClearHumanOpinion_RejectsUnknownProperties()
    {
        var proposal = Proposal("analysis/wfiAnalysis/removeRefEvaluations",
            CanonicalId.FromGuid(_analysis.Guid), new { member = "defaultUserApproves", extra = true });

        Assert.Throws<ContractParseException>(() => ScratchDryRun.Of(_cache, proposal));
    }

    [Fact]
    public void ParserCandidate_RejectsMissingEntityIdAndUnresolvableReferences()
    {
        var target = CanonicalId.FromGuid(_wordform.Guid);
        var payload = new { morphs = new[] { new { form = CanonicalId.Mint().Value } } };
        Assert.Throws<ContractParseException>(() => ScratchDryRun.Of(_cache,
            Proposal("analysis/wfiWordform/createAnalyses", target, payload)));
        Assert.ThrowsAny<Exception>(() => ScratchDryRun.Of(_cache,
            Proposal("analysis/wfiWordform/createAnalyses", target, payload, CanonicalId.Mint())));
    }

    [Fact]
    public void RejectReplacesApproval_AndClearReturnsToCandidate()
    {
        var target = CanonicalId.FromGuid(_analysis.Guid);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            _cache.LangProject.DefaultUserAgent.SetEvaluation(_analysis, Opinions.approves));
        var reject = Proposal("analysis/wfiAnalysis/addRefEvaluations", target,
            new { member = "defaultUserDisapproves" });
        var rejectDryRun = ScratchDryRun.Of(_cache, reject);
        ProposalApplier.Apply(_cache, reject, rejectDryRun.Anchor, "tester");
        Assert.Equal(Opinions.disapproves, _analysis.GetAgentOpinion(_cache.LangProject.DefaultUserAgent));

        var clear = Proposal("analysis/wfiAnalysis/removeRefEvaluations", target,
            new { member = "defaultUserDisapproves" });
        var clearDryRun = ScratchDryRun.Of(_cache, clear);
        ProposalApplier.Apply(_cache, clear, clearDryRun.Anchor, "tester");
        Assert.Equal(Opinions.noopinion, _analysis.GetAgentOpinion(_cache.LangProject.DefaultUserAgent));
    }

    [Fact]
    public void ApproveParserReading_ComposesCandidateThenHumanOpinion()
    {
        var source = _cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(_seed.FirstEntryId);
        var form = source.LexemeFormOA!;
        var msa = source.MorphoSyntaxAnalysesOC.First();
        var reading = new ParseAnalysis([new ParseMorph(form.Guid.ToString("D"), msa.Guid.ToString("D"), null, null)]);
        var operations = AnalysisChangeComposer.Build(_cache,
            new AnalysisChangeIntent("approve", CanonicalId.FromGuid(_wordform.Guid), reading));
        Assert.Equal(2, operations.Count);
        Assert.Equal(operations[0].OperationId, Assert.Single(operations[1].DependsOn).OperationId);
        var proposal = new Proposal(new Dictionary<string, string> { ["analysis"] = "1.0" },
            CanonicalId.Mint(), null, operations);

        var dryRun = ScratchDryRun.Of(_cache, proposal);
        Assert.Single(_wordform.AnalysesOC);
        ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "tester");

        var created = Assert.Single(_wordform.AnalysesOC, item => item.Guid == operations[0].EntityId!.Value.ToGuid());
        Assert.Equal(Opinions.approves, created.GetAgentOpinion(_cache.LangProject.DefaultUserAgent));
        Assert.Equal(Opinions.approves, created.GetAgentOpinion(_cache.LangProject.DefaultParserAgent));
    }

    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    [InlineData("candidate")]
    public void GuessedReading_SelectsOnlyTheAnalysisWithTheSameForm(string kind)
    {
        IWfiAnalysis matching = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            foreach (var value in new[] { "xyz", "abc" })
            {
                var analysis = _cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
                _wordform.AnalysesOC.Add(analysis);
                var bundle = _cache.ServiceLocator.GetInstance<IWfiMorphBundleFactory>().Create();
                analysis.MorphBundlesOS.Add(bundle);
                bundle.Form.set_String(_cache.DefaultVernWs,
                    TsStringUtils.MakeString(value, _cache.DefaultVernWs));
                if (value == "abc") matching = analysis;
            }
            _cache.LangProject.DefaultUserAgent.SetEvaluation(matching, Opinions.approves);
        });
        var reading = new ParseAnalysis([new ParseMorph(null, null, null, "abc")]);

        var operations = AnalysisChangeComposer.Build(_cache,
            new AnalysisChangeIntent(kind, CanonicalId.FromGuid(_wordform.Guid), reading));

        Assert.Equal(CanonicalId.FromGuid(matching.Guid), Assert.Single(operations).Target);
    }

    [Fact]
    public void DifferentGuessedForm_DoesNotBlockAddingCandidate()
    {
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var bundle = _cache.ServiceLocator.GetInstance<IWfiMorphBundleFactory>().Create();
            _analysis.MorphBundlesOS.Add(bundle);
            bundle.Form.set_String(_cache.DefaultVernWs, TsStringUtils.MakeString("xyz", _cache.DefaultVernWs));
        });
        var reading = new ParseAnalysis([new ParseMorph(null, null, null, "abc")]);

        var operations = AnalysisChangeComposer.Build(_cache,
            new AnalysisChangeIntent("add-candidate", CanonicalId.FromGuid(_wordform.Guid), reading));

        Assert.Equal("analysis/wfiWordform/createAnalyses", Assert.Single(operations).Kind);
    }

    [Fact]
    public void ApprovalAddressesEveryStoredAnalysisMatchingTheParserReading()
    {
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            foreach (var analysis in new[] { _analysis,
                _cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create() })
            {
                if (analysis != _analysis) _wordform.AnalysesOC.Add(analysis);
                var bundle = _cache.ServiceLocator.GetInstance<IWfiMorphBundleFactory>().Create();
                analysis.MorphBundlesOS.Add(bundle);
                bundle.Form.set_String(_cache.DefaultVernWs,
                    TsStringUtils.MakeString("same-root", _cache.DefaultVernWs));
            }
        });
        var reading = new ParseAnalysis([new ParseMorph(null, null, null, "same-root")]);

        var operations = AnalysisChangeComposer.Build(_cache,
            new AnalysisChangeIntent("approve", CanonicalId.FromGuid(_wordform.Guid), reading));

        Assert.Equal(2, operations.Count);
        Assert.Equal(2, operations.Select(operation => operation.Target).Distinct().Count());
    }

    [Fact]
    public void AnalysisSnapshots_DiffEvaluationMembershipByCanonicalIdentity()
    {
        var before = AnalysisFieldSnapshots.Read(_analysis);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            _cache.LangProject.DefaultUserAgent.SetEvaluation(_analysis, Opinions.approves));
        var after = AnalysisFieldSnapshots.Read(_analysis);

        var effect = Assert.Single(AnalysisFieldSnapshots.Diff(before, after));
        Assert.Equal(SnapshotFields.WfiAnalysisEvaluations, effect.Field);
        Assert.Empty(effect.Before);
        Assert.Single(effect.After);
        Assert.Contains(SnapshotFields.WfiAnalysisEvaluations, ObjectSnapshotJsonWriter.WriteJson(after));
    }

    [Fact]
    public void FailedSecondOperation_RollsBackHumanOpinion()
    {
        var first = new OperationEnvelope(CanonicalId.Mint(), "analysis/wfiAnalysis/addRefEvaluations",
            target: CanonicalId.FromGuid(_analysis.Guid),
            after: JsonSerializer.SerializeToElement(new { member = "defaultUserApproves" }));
        var second = new OperationEnvelope(CanonicalId.Mint(), "analysis/wfiWordform/createAnalyses",
            entityId: CanonicalId.Mint(), target: CanonicalId.FromGuid(_wordform.Guid),
            after: JsonSerializer.SerializeToElement(new
            {
                morphs = new[] { new { form = CanonicalId.Mint().Value, msa = CanonicalId.Mint().Value } },
            }));
        var proposal = new Proposal(new Dictionary<string, string> { ["analysis"] = "1.0" },
            CanonicalId.Mint(), null, [first, second]);
        var anchor = new BoundDryRunAnchor(ContractIntentDigest.Compute(proposal),
            FootprintProbe.ComputeCurrentFootprintDigest(_cache, proposal),
            "sha256:" + new string('0', 64), "test", "test", SnapshotFields.ProjectionVersion,
            "20260101T000000Z");

        Assert.ThrowsAny<Exception>(() => ProposalApplier.Apply(_cache, proposal, anchor, "tester"));
        Assert.Equal(Opinions.noopinion, _analysis.GetAgentOpinion(_cache.LangProject.DefaultUserAgent));
        Assert.Empty(ProjectAppliedLog.ReadAll(_cache));
    }

    [Fact]
    public void FailedSecondOperation_RollsBackCreatedCandidate()
    {
        var candidateId = CanonicalId.Mint();
        var create = new OperationEnvelope(CanonicalId.Mint(), "analysis/wfiWordform/createAnalyses",
            entityId: candidateId, target: CanonicalId.FromGuid(_wordform.Guid),
            after: JsonSerializer.SerializeToElement(new { morphs = new[] { new { guessedString = "new-root" } } }));
        var invalid = new OperationEnvelope(CanonicalId.Mint(), "analysis/wfiWordform/createAnalyses",
            entityId: CanonicalId.Mint(), target: CanonicalId.FromGuid(_wordform.Guid),
            after: JsonSerializer.SerializeToElement(new { morphs = new[]
                { new { form = CanonicalId.Mint().Value } } }));
        var proposal = new Proposal(new Dictionary<string, string> { ["analysis"] = "1.0" },
            CanonicalId.Mint(), null, [create, invalid]);
        var anchor = new BoundDryRunAnchor(ContractIntentDigest.Compute(proposal),
            FootprintProbe.ComputeCurrentFootprintDigest(_cache, proposal),
            "sha256:" + new string('0', 64), "test", "test", SnapshotFields.ProjectionVersion,
            "20260101T000000Z");

        Assert.ThrowsAny<Exception>(() => ProposalApplier.Apply(_cache, proposal, anchor, "tester"));
        Assert.DoesNotContain(_wordform.AnalysesOC, analysis => analysis.Guid == candidateId.ToGuid());
        Assert.Empty(ProjectAppliedLog.ReadAll(_cache));
    }

    [Fact]
    public void OpinionChangedAfterDryRun_IsDriftAndCannotApply()
    {
        var proposal = Proposal("analysis/wfiAnalysis/addRefEvaluations",
            CanonicalId.FromGuid(_analysis.Guid), new { member = "defaultUserApproves" });
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            _cache.LangProject.DefaultUserAgent.SetEvaluation(_analysis, Opinions.disapproves));

        Assert.ThrowsAny<Exception>(() => ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "tester"));
        Assert.Equal(Opinions.disapproves, _analysis.GetAgentOpinion(_cache.LangProject.DefaultUserAgent));
        Assert.Empty(ProjectAppliedLog.ReadAll(_cache));
    }

    private static Proposal Proposal(string kind, CanonicalId target, object after, CanonicalId? entityId = null)
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(after));
        return new Proposal(new Dictionary<string, string> { ["analysis"] = "1.0" }, CanonicalId.Mint(), null,
            new[] { new OperationEnvelope(CanonicalId.Mint(), kind, entityId, target, json.RootElement.Clone()) });
    }
}

public sealed class WfiAnalysisPayloadTests
{
    [Theory]
    [InlineData("{\"morphs\":[]}")]
    [InlineData("{\"morphs\":[{}]}")]
    [InlineData("{\"morphs\":[{\"guessedString\":\"x\",\"extra\":true}]}")]
    [InlineData("{\"morphs\":[{\"guessedString\":12}]}")]
    public void InvalidCandidatePayload_IsAContractError(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.Throws<ContractParseException>(() =>
            SIL.Motif.Runner.Operations.CreateAnalysisPayload.Parse(document.RootElement));
    }

    [Fact]
    public void NullOptionalReferencesAndGuess_AreTreatedAsAbsent()
    {
        using var document = JsonDocument.Parse("""
            {"morphs":[{"form":"aaaaaaaaaaaaaaaaaaaaaa","msa":null,"inflType":null,"guessedString":null}]}
            """);
        var morph = Assert.Single(SIL.Motif.Runner.Operations.CreateAnalysisPayload.Parse(document.RootElement));
        Assert.Null(morph.Msa);
        Assert.Null(morph.InflType);
        Assert.Null(morph.GuessedString);
    }
}

[Collection(TestFixtures.LcmCacheTestCollection.Name)]
public sealed class WfiAnalysisConformanceTests
{
    [Fact]
    public void ParserCandidate_AppliesAgainstSyntheticFieldWorksProject()
    {
        using var project = new ConformanceProject();
        using var cache = new FwDataProjectLoader().LoadScratchCache(project.FwDataPath);
        var source = cache.ServiceLocator.GetInstance<ILexEntryRepository>().AllInstances()
            .First(entry => entry.LexemeFormOA is not null && entry.MorphoSyntaxAnalysesOC.Count > 0);
        IWfiWordform wordform = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            wordform = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString("conformance-candidate", cache.DefaultVernWs)));
        var reading = new ParseAnalysis([new ParseMorph(
            source.LexemeFormOA!.Guid.ToString("D"),
            source.MorphoSyntaxAnalysesOC.First().Guid.ToString("D"), null, null)]);
        var operations = AnalysisChangeComposer.Build(cache, new AnalysisChangeIntent(
            "add-candidate", CanonicalId.FromGuid(wordform.Guid), reading));
        var proposal = new Proposal(new Dictionary<string, string> { ["analysis"] = "1.0" },
            CanonicalId.Mint(), null, operations);

        var dryRun = ScratchDryRun.Of(cache, proposal);
        ProposalApplier.Apply(cache, proposal, dryRun.Anchor, "tester");

        var created = Assert.Single(wordform.AnalysesOC);
        Assert.Equal(source.LexemeFormOA.Guid, Assert.Single(created.MorphBundlesOS).MorphRA!.Guid);
        Assert.Equal(Opinions.approves, created.GetAgentOpinion(cache.LangProject.DefaultParserAgent));
    }
}
