using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Review;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>The window's Review and <c>motif apply</c> must reach the same Readiness verdict from the same store.</summary>
public sealed class ReviewReadinessParityTests : IDisposable
{
    private static readonly string[] ReadinessRefusals = ["apply.not-ready", "apply.regression", "apply.dry-run-missing"];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "SIL.Motif.ReviewReadinessParityTests",
        Guid.NewGuid().ToString("N"));

    public ReviewReadinessParityTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    [Fact]
    public void ReviewReadinessMatchesApplyAfterBaselineMoves()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var proposal = fixture.SaveProposal();
        var before = CurrentToken(fixture);
        var promoted = Record(fixture, proposal: null, before);
        new AssessmentRepository(fixture.Database).PromoteToCurrent(promoted);
        var moved = fixture.RecordBaseline("sha256:" + new string('c', 64), "2026-09-24T13:00:00Z",
            semanticDigest: "sha256:" + new string('d', 64));
        fixture.CreateJob(proposal.Json, "2026-09-24T14:00:00Z", "trial-b", [], baseline: moved);
        Record(fixture, proposal, moved);
        Bind(fixture, proposal.Id);

        Assert.True(AppliesWithoutForce(fixture, proposal.Id));
        Assert.True(ReviewIsReady(fixture, proposal.Id));
    }

    [Theory]
    [InlineData("ready", true)]
    [InlineData("baseline-moved-without-a-new-trial", false)]
    [InlineData("stale-trial-digest", false)]
    [InlineData("judgment-only-anchored", true)]
    [InlineData("judgment-only-anchor-mismatch", false)]
    [InlineData("no-anchor", false)]
    public void ReviewReadinessIsApplyAuthorizationInEveryCase(string scenario, bool expected)
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var id = Arrange(fixture, scenario);

        var applies = AppliesWithoutForce(fixture, id);
        var review = ReviewIsReady(fixture, id);

        Assert.Equal(expected, applies);
        Assert.Equal(applies, review);
    }

    private const string NoCoveringAssessment =
        "no Assessment covers its current content, so nothing has measured what it would do";

    private const string NoDryRunBound = "no Dry Run is bound to it, so nothing has shown what it would change";

    [Theory]
    [InlineData("baseline-moved-without-a-new-trial", "its Assessment was measured against a different project state than the current one, so it has not been re-run since the project moved")]
    [InlineData("stale-trial-digest", NoCoveringAssessment)]
    [InlineData("judgment-only-anchor-mismatch", NoCoveringAssessment)]
    [InlineData("no-anchor", NoDryRunBound)]
    public void ReviewGivesTheExactReasonForEachNotReadyCase(string scenario, string expected)
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var id = Arrange(fixture, scenario);

        var reasons = ProposalReview.Read(fixture.Database, fixture.Project, id.Value).Value!.Readiness!.Reasons;

        Assert.Equal(expected, string.Join(" | ", reasons));
    }

    [Fact]
    public void APendingDraftChangedAfterItsTrialIsNotReadyInReviewAndRefusedByPendingApply()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var id = CanonicalId.Mint("proposal/");
        var tried = Draft(id, "before");
        var proposals = new ProposalRepository(fixture.Database);
        proposals.CreateDraft(PendingChanges.DraftName, id, JsonSerializer.Serialize(tried, DraftOptions),
            ProposalRoles.PendingChanges);
        fixture.CreateJob(ProposalCommands.BuildProposalJson(tried), "2026-09-24T11:00:00Z", "trial", []);
        proposals.SaveDraft(PendingChanges.DraftName, JsonSerializer.Serialize(Draft(id, "after"), DraftOptions));

        var readiness = ProposalReview.Read(fixture.Database, fixture.Project, id.Value).Value!.Readiness!;
        var applied = PendingChangesWorkflow.Apply(new ApplyProposalRequest(fixture.Project.FullFwDataPath, id.Value, null));

        Assert.False(readiness.IsReady);
        Assert.Equal(NoDryRunBound, string.Join(" | ", readiness.Reasons));
        Assert.Equal("apply.trial-stale", applied.Refusal?.Code);
    }

    private static CanonicalId Arrange(TrialsTests.ProjectFixture fixture, string scenario)
    {
        var proposals = new ProposalRepository(fixture.Database);
        if (scenario.StartsWith("judgment-only", StringComparison.Ordinal))
        {
            var (json, effects) = JudgmentProposal();
            var judgment = ProposalJsonParser.Parse(json);
            proposals.SaveRevision(new ProposalRevisionRecord(judgment.ProposalId, IntentDigest.Compute(judgment),
                json, "proposed", null, null, null));
            fixture.CreateJob(json, "2026-09-24T11:00:00Z", "trial", [], effects: effects);
            Bind(fixture, judgment.ProposalId, mismatched: scenario.EndsWith("mismatch", StringComparison.Ordinal));
            return judgment.ProposalId;
        }

        var proposal = fixture.SaveProposal();
        fixture.CreateJob(proposal.Json, "2026-09-24T11:00:00Z", "trial", []);
        var token = CurrentToken(fixture);
        Record(fixture, proposal, token);
        var anchor = TrialAnchorJson(fixture, proposal.Id);
        switch (scenario)
        {
            case "baseline-moved-without-a-new-trial":
                fixture.RecordBaseline("sha256:" + new string('c', 64), "2026-09-24T13:00:00Z",
                    semanticDigest: "sha256:" + new string('d', 64));
                break;
            case "stale-trial-digest":
                var changed = ProposalCommands.BuildProposalJson(Draft(proposal.Id, "changed after the trial"));
                proposals.SaveRevision(new ProposalRevisionRecord(proposal.Id,
                    IntentDigest.Compute(ProposalJsonParser.Parse(changed)), changed, "proposed", null, null, null));
                break;
        }
        if (scenario != "no-anchor") proposals.SetAnchor(proposal.Id, anchor);
        return proposal.Id;
    }

    private static bool AppliesWithoutForce(TrialsTests.ProjectFixture fixture, CanonicalId id)
    {
        try
        {
            var outcome = ProposalCommands.BuildApplyProjection(fixture.Database, fixture.Project, id.Value);
            return outcome.Refusal is null || !ReadinessRefusals.Contains(outcome.Refusal.Code);
        }
        catch (Exception)
        {
            return true;
        }
    }

    private static bool ReviewIsReady(TrialsTests.ProjectFixture fixture, CanonicalId id) =>
        ProposalReview.Read(fixture.Database, fixture.Project, id.Value).Value!.Readiness?.IsReady == true;

    private static BaselineToken CurrentToken(TrialsTests.ProjectFixture fixture) =>
        new BaselineRepository(fixture.Database).GetCurrent(ProjectWorkspaceKey.Compute(fixture.Project))!.Token;

    private static string TrialAnchorJson(TrialsTests.ProjectFixture fixture, CanonicalId id) =>
        JsonSerializer.Serialize(ProposalCommands.TrialDryRun(fixture.Database, fixture.Project, id)!.Anchor);

    private static void Bind(TrialsTests.ProjectFixture fixture, CanonicalId id, bool mismatched = false)
    {
        var anchor = ProposalCommands.TrialDryRun(fixture.Database, fixture.Project, id)!.Anchor;
        if (mismatched) anchor = anchor with { EffectDigest = "sha256:" + new string('9', 64) };
        new ProposalRepository(fixture.Database).SetAnchor(id, JsonSerializer.Serialize(anchor));
    }

    private static string Record(TrialsTests.ProjectFixture fixture, TrialsTests.SavedProposal? proposal,
        BaselineToken token) => Record(fixture, proposal, token, "cat");

    private static string Record(TrialsTests.ProjectFixture fixture, TrialsTests.SavedProposal? proposal,
        BaselineToken token, string word)
    {
        var id = CanonicalId.Mint("assessment/").Value;
        new AssessmentRepository(fixture.Database).Record(new NewAssessmentRecord(
            AssessmentId: id,
            ProposalId: proposal?.Id,
            ProposalIntentDigest: proposal?.Digest,
            Assessor: "pangloss",
            Kind: RegressionChecker.RequiredKind,
            ScopeJson: """{"perWordLimitMs":1000,"perWordStepLimit":{"steps":200000,"isUnbounded":false}}""",
            ScopeDigest: "sha256:" + new string('a', 64),
            Subject: new AssessmentSubject.Words(Selection.Create("test", [word]), "none", "1"),
            BaselineToken: JsonSerializer.Serialize(token, MotifJson.CreateOptions()),
            OutcomeDigest: "sha256:" + new string('b', 64),
            SemanticDigest: "sha256:" + new string('c', 64),
            GrammarSourceSha256: "sha256:" + new string('d', 64),
            ModelFingerprint: "model",
            Pipeline: "pipeline",
            DiagnosticCount: 0,
            Material: new AssessmentRecordMaterial.Words([CorrectnessFixture.Word(word, true)], null, null, [])));
        return id;
    }

    private static readonly JsonSerializerOptions DraftOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static DraftDocument Draft(CanonicalId proposalId, string text)
    {
        using var after = JsonDocument.Parse(JsonSerializer.Serialize(new { ws = "en", text }));
        return new DraftDocument
        {
            ProposalId = proposalId.Value,
            ContractVersions = new Dictionary<string, string>(StringComparer.Ordinal) { ["lexical"] = "1.0" },
            Operations =
            [
                new DraftOperation
                {
                    OperationId = CanonicalId.Mint("operation/").Value,
                    Kind = "lexical/lexSense/setGloss",
                    Target = CanonicalId.Mint("lexSense/").Value,
                    After = after.RootElement.EnumerateObject()
                        .ToDictionary(property => property.Name, property => property.Value.Clone(),
                            StringComparer.Ordinal),
                },
            ],
        };
    }

    // One Notebook record carrying a judgment: the smallest Proposal the judgment-only exemption accepts.
    private static (string Json, IReadOnlyList<ExpectedEffect> Effects) JudgmentProposal()
    {
        string Mint(string prefix) => CanonicalId.Mint(prefix).Value;
        JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);
        var notebook = Mint("notebook/");
        var record = Mint("record/");
        var description = Mint("description/");
        var paragraph = Mint("paragraph/");
        DraftOperation Op(string kind, string target, string? entity, object after) => new()
        {
            OperationId = Mint("operation/"), Kind = kind, Target = target, EntityId = entity,
            After = Json(after).EnumerateObject().ToDictionary(property => property.Name,
                property => property.Value.Clone(), StringComparer.Ordinal),
        };
        var operations = new List<DraftOperation>
        {
            Op(RnResearchNbkRecordsOperationKinds.Create, notebook, record, new { }),
            Op(RnGenericRecTypeOperationKinds.SetType, record, null, new { @ref = Mint("type/") }),
            Op(RnGenericRecTitleOperationKinds.Set, record, null, new { ws = "en", text = "A judgment" }),
            Op(RnGenericRecDescriptionOperationKinds.Create, record, description, new { }),
            Op(StTextParagraphsOperationKinds.Create, description, paragraph, new { @class = "StTxtPara" }),
            Op(StTxtParaContentsOperationKinds.Set, paragraph, null, new { ws = "en", text = "The sentence." }),
            Op(HumanJudgmentCustomFieldOperationKinds.Set, record, null, new { text = "data" }),
        };
        var draft = new DraftDocument
        {
            ProposalId = Mint("proposal/"),
            ContractVersions = operations.Select(operation => operation.Kind.Split('/')[0]).Distinct()
                .ToDictionary(group => group, _ => "1.0", StringComparer.Ordinal),
            Operations = operations,
        };
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RnResearchNbkRecordsOperationKinds.Create] = SnapshotFields.RnResearchNbkRecords,
            [RnGenericRecDescriptionOperationKinds.Create] = SnapshotFields.RnGenericRecDescription,
            [StTextParagraphsOperationKinds.Create] = SnapshotFields.StTextParagraphs,
            [RnGenericRecTypeOperationKinds.SetType] = SnapshotFields.RnGenericRecType,
            [RnGenericRecTitleOperationKinds.Set] = SnapshotFields.RnGenericRecTitle,
            [StTxtParaContentsOperationKinds.Set] = SnapshotFields.StTxtParaContents,
            [HumanJudgmentCustomFieldOperationKinds.Set] = SnapshotFields.RnGenericRecMotifHumanJudgment,
        };
        var empty = new Dictionary<string, string>();
        var effects = operations.Select(operation => new ExpectedEffect(CanonicalId.Parse(operation.Target!),
            fields[operation.Kind], empty, new Dictionary<string, string> { ["x"] = operation.OperationId })).ToArray();
        return (ProposalCommands.BuildProposalJson(draft), effects);
    }
}
