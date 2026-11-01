using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed partial class ProposalDifferenceCommandsTests
{
    private static readonly Guid SitaAnalysis = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid MutuWordform = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid SourceAgentA = Guid.Parse("88888888-8888-8888-8888-888888888888");
    private static readonly Guid SourceAgentB = Guid.Parse("99999999-9999-9999-9999-999999999999");

    private static CommandOutcome<ProposalDifferenceResponse> ProduceSourceValidated(
        ProposalDifferenceProduceRequest request) =>
        ProposalDifferenceCommands.Produce(request, ProposalDifferenceReferences.SourceValidated);

    private static void LoadAgentEvaluationKind() => System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(
        typeof(SIL.Motif.Runner.Operations.WfiAnalysisAgentEvaluationOperationKinds).Module.ModuleHandle);

    [Fact]
    public void ADifferenceReadAgainstSourceValidatedDecisionsCountsOnlyWhatAnAgentStaged()
    {
        LoadAgentEvaluationKind();
        using var fixture = new Fixture(_root);
        var human = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!.Summary;
        var none = ProduceSourceValidated(fixture.ProduceRequest());

        Assert.True(none.Succeeded, none.Refusal?.Message);
        Assert.Equal(ProposalDifferenceReferences.Human, human.Reference);
        Assert.Equal(1, human.WordsWithApprovedDecisions);
        Assert.Equal(ProposalDifferenceReferences.SourceValidated, none.Value!.Summary.Reference);
        Assert.Equal(0, none.Value.Summary.WordsWithApprovedDecisions);

        fixture.StageAppliedAgentApproval(SitaAnalysis);
        var staged = ProduceSourceValidated(fixture.ProduceRequest());

        Assert.Equal(1, staged.Value!.Summary.WordsWithApprovedDecisions);
        Assert.Equal(ProposalDifferenceReferences.SourceValidated, ProposalDifferenceCommands.Show(
            new ProposalDifferenceShowRequest(fixture.ProjectPath, DifferenceId: staged.Value.Summary.DifferenceId))
            .Value!.Summary.Reference);
    }

    [Fact]
    public void ASourceValidatedDifferenceSaysSoInItsSummaryNotesAndEachWordsEvidence()
    {
        LoadAgentEvaluationKind();
        using var fixture = new Fixture(_root);
        fixture.StageAppliedAgentApproval(SitaAnalysis);

        var difference = ProduceSourceValidated(fixture.ProduceRequest()).Value!;
        var human = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!;

        Assert.Contains(difference.Summary.Warnings, warning =>
            warning.StartsWith(ProposalDifferenceWarnings.SourceValidated, StringComparison.Ordinal) &&
            warning.Contains("not a person's", StringComparison.Ordinal));
        var sita = Assert.Single(difference.Words, word => word.Word == "sita");
        var evidence = Assert.Single(sita.Evidence!);
        Assert.Equal(("source-approved", "Source validation", "approves"), (evidence.Tier, evidence.Agent, evidence.Opinion));
        Assert.Equal(CanonicalId.FromGuid(SitaAnalysis).Value, evidence.Subject);
        Assert.Equal("analysis", evidence.Level);
        Assert.Null(Assert.Single(difference.Words, word => word.Word == "mutu").Evidence);
        Assert.DoesNotContain(human.Summary.Warnings, warning =>
            warning.StartsWith(ProposalDifferenceWarnings.SourceValidated, StringComparison.Ordinal));
        Assert.All(human.Words, word => Assert.Null(word.Evidence));
    }

    [Fact]
    public void ProducingAgainWithTheSameStagedDecisionsReturnsTheSavedDifferenceAndNewStagingReadsAgain()
    {
        LoadAgentEvaluationKind();
        using var fixture = new Fixture(_root);
        fixture.StageAppliedAgentApproval(SitaAnalysis);
        var first = ProduceSourceValidated(fixture.ProduceRequest()).Value!.Summary;

        var again = ProduceSourceValidated(fixture.ProduceRequest()).Value!.Summary;
        fixture.StageAppliedAgentApproval(SitaAnalysis, agentName: "Another source");
        var afterNewStaging = ProduceSourceValidated(fixture.ProduceRequest()).Value!.Summary;

        Assert.Equal(first.DifferenceId, again.DifferenceId);
        Assert.NotNull(first.DecisionsDigest);
        Assert.NotEqual(first.DifferenceId, afterNewStaging.DifferenceId);
        Assert.NotEqual(first.DecisionsDigest, afterNewStaging.DecisionsDigest);
    }

    [Fact]
    public void AnAgentEvaluationWithoutDecisionEvidenceIsNotSourceValidated()
    {
        LoadAgentEvaluationKind();
        using var fixture = new Fixture(_root);
        fixture.StageAppliedAgentApproval(SitaAnalysis, includeEvidence: false);

        var difference = ProduceSourceValidated(fixture.ProduceRequest());

        Assert.Equal(0, difference.Value!.Summary.WordsWithApprovedDecisions);
    }

    [Fact]
    public void OpposingAgentEvaluationsDoNotDependOnOperationOrder()
    {
        LoadAgentEvaluationKind();
        using var forward = new Fixture(_root);
        using var reverse = new Fixture(_root);
        forward.StageOpposingAgentEvaluations(SitaAnalysis, reverse: false);
        reverse.StageOpposingAgentEvaluations(SitaAnalysis, reverse: true);

        var forwardResult = ProduceSourceValidated(forward.ProduceRequest());
        var reverseResult = ProduceSourceValidated(reverse.ProduceRequest());

        Assert.True(forwardResult.Succeeded, forwardResult.Refusal?.Message);
        Assert.True(reverseResult.Succeeded, reverseResult.Refusal?.Message);
        var forwardSita = Assert.Single(forwardResult.Value!.Words, word => word.Word == "sita");
        var reverseSita = Assert.Single(reverseResult.Value!.Words, word => word.Word == "sita");
        Assert.Equal(0, forwardResult.Value.Summary.WordsWithApprovedDecisions);
        Assert.Equal(0, reverseResult.Value.Summary.WordsWithApprovedDecisions);
        Assert.Equal(HumanAgreementApprovalState.ConflictingDecisions, forwardSita.Facts.After.ApprovedState);
        Assert.Equal(HumanAgreementApprovalState.ConflictingDecisions, reverseSita.Facts.After.ApprovedState);
        Assert.Equal(forwardResult.Value.Summary.Matrix, reverseResult.Value.Summary.Matrix);
    }

    [Fact]
    public void ATypoRecordMakesTheWordformIncorrectForTheSourceValidatedReadingAndNeverForTheHumanOne()
    {
        LoadAgentEvaluationKind();
        using var fixture = new Fixture(_root, humanMarksMutuIncorrect: false);
        var humanBefore = Assert.Single(ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!.Words,
            word => word.Word == "mutu");
        var before = Assert.Single(ProduceSourceValidated(fixture.ProduceRequest()).Value!.Words,
            word => word.Word == "mutu");
        Assert.Equal(HumanAgreementSpellingAcceptance.NotApplicable, before.Facts.After.SpellingAcceptance);

        fixture.StageAppliedEvidenceOnly(MutuWordform);
        var staged = Assert.Single(ProduceSourceValidated(fixture.ProduceRequest()).Value!.Words, word => word.Word == "mutu");
        var humanAfter = Assert.Single(ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!.Words,
            word => word.Word == "mutu");

        Assert.Equal(HumanAgreementSpellingAcceptance.AcceptedMisspelling, staged.Facts.After.SpellingAcceptance);
        Assert.Equal(JsonSerializer.Serialize(humanBefore, MotifJson.CreateOptions()),
            JsonSerializer.Serialize(humanAfter, MotifJson.CreateOptions()));
        Assert.Equal(HumanAgreementSpellingAcceptance.NotApplicable, humanAfter.Facts.After.SpellingAcceptance);
    }

    [Fact]
    public void AWordformRecordShowsItsTierOnTheWordItBelongsToAndOnNoOther()
    {
        LoadAgentEvaluationKind();
        using var fixture = new Fixture(_root);
        fixture.StageAppliedEvidenceOnly(MutuWordform);
        fixture.StageAppliedAgentApproval(SitaAnalysis);

        var difference = ProduceSourceValidated(fixture.ProduceRequest()).Value!;
        var shown = ProposalDifferenceCommands.Show(new ProposalDifferenceShowRequest(
            fixture.ProjectPath, DifferenceId: difference.Summary.DifferenceId)).Value!;

        var mutu = Assert.Single(shown.Words, word => word.Word == "mutu");
        var record = Assert.Single(mutu.Evidence!);
        Assert.Equal(("wordform", "misspelled-typo", CanonicalId.FromGuid(MutuWordform).Value),
            (record.Level, record.Tier, record.Subject));
        Assert.Null(record.Agent);
        var sita = Assert.Single(shown.Words, word => word.Word == "sita");
        Assert.Equal(["analysis"], sita.Evidence!.Select(item => item.Level));
        Assert.Equal(CanonicalId.FromGuid(MutuWordform).Value, mutu.Facts.WordformId);
        var human = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!;
        Assert.All(human.Words, word => Assert.Null(word.Facts.WordformId));
    }

    [Fact]
    public void ARestagedDecisionByTheSameAgentReplacesItsEarlierEvidenceOnTheWord()
    {
        LoadAgentEvaluationKind();
        using var fixture = new Fixture(_root);
        var agent = CanonicalId.Mint("agent/").Value;
        fixture.StageAppliedAgentApprovalBy(SitaAnalysis, agent);
        fixture.StageAppliedAgentApprovalBy(SitaAnalysis, agent);
        fixture.StageAppliedAgentApproval(SitaAnalysis, agentName: "Another source");

        var difference = ProduceSourceValidated(fixture.ProduceRequest()).Value!;
        var shown = ProposalDifferenceCommands.Show(new ProposalDifferenceShowRequest(
            fixture.ProjectPath, DifferenceId: difference.Summary.DifferenceId)).Value!;

        var sita = Assert.Single(shown.Words, word => word.Word == "sita");
        Assert.Equal(["Another source", "Source validation"], sita.Evidence!.Select(item => item.Agent).Order());
    }

    [Fact]
    public void AnAgentApprovalIsSeenBySourceValidatedAndNotByTheHumanReadingWhenNoPersonApproved()
    {
        LoadAgentEvaluationKind();
        using var fixture = new Fixture(_root, humanApprovesSita: false);
        fixture.StageAppliedAgentApproval(SitaAnalysis);

        var human = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!;
        var sourceValidated = ProduceSourceValidated(fixture.ProduceRequest()).Value!;

        Assert.Equal(0, human.Summary.WordsWithApprovedDecisions);
        Assert.Equal(1, sourceValidated.Summary.WordsWithApprovedDecisions);
        Assert.Equal(HumanAgreementApprovalState.NoApprovedExpectations,
            Assert.Single(human.Words, word => word.Word == "sita").Facts.After.ApprovedState);
        Assert.NotEqual(HumanAgreementApprovalState.NoApprovedExpectations,
            Assert.Single(sourceValidated.Words, word => word.Word == "sita").Facts.After.ApprovedState);
    }

    [Fact]
    public void OnlyAppliedProposalsCountAndTheComparedProposalsOwnStagedDecisionsNeverDo()
    {
        LoadAgentEvaluationKind();
        using var fixture = new Fixture(_root, humanApprovesSita: false);
        fixture.StageProposal("proposed", Fixture.AgentApproval(SitaAnalysis));
        fixture.StageProposal("finalized", Fixture.AgentApproval(SitaAnalysis));
        fixture.StageProposal("rejected", Fixture.AgentApproval(SitaAnalysis));
        fixture.StageComparedProposalAgentApproval(SitaAnalysis);

        var none = ProduceSourceValidated(fixture.ProduceRequest()).Value!;

        Assert.Equal(0, none.Summary.WordsWithApprovedDecisions);
        Assert.Null(Assert.Single(none.Words, word => word.Word == "sita").Evidence);

        fixture.StageProposal("applied", Fixture.AgentApproval(SitaAnalysis));
        var applied = ProduceSourceValidated(fixture.ProduceRequest()).Value!;

        Assert.Equal(1, applied.Summary.WordsWithApprovedDecisions);
    }

    [Fact]
    public void AnUnreadableAppliedRevisionIsNamedAndDoesNotStopProduceOrShow()
    {
        LoadAgentEvaluationKind();
        using var fixture = new Fixture(_root, humanApprovesSita: false);
        fixture.StageAppliedAgentApproval(SitaAnalysis);
        var broken = fixture.StageUnreadableApplied("Broken stager");

        var produced = ProduceSourceValidated(fixture.ProduceRequest());

        Assert.True(produced.Succeeded, produced.Refusal?.Message);
        var unreadable = Assert.Single(produced.Value!.UnreadableEvidence!);
        Assert.Equal((broken, "Broken stager"), (unreadable.ProposalId, unreadable.Label));
        Assert.False(string.IsNullOrWhiteSpace(unreadable.Reason));
        Assert.Contains(produced.Value.Summary.Warnings, warning =>
            warning.StartsWith(ProposalDifferenceWarnings.EvidenceUnreadable, StringComparison.Ordinal) &&
            warning.Contains("Broken stager", StringComparison.Ordinal) && warning.Contains(broken, StringComparison.Ordinal));
        Assert.Equal(1, produced.Value.Summary.WordsWithApprovedDecisions);
        var shown = ProposalDifferenceCommands.Show(new ProposalDifferenceShowRequest(
            fixture.ProjectPath, DifferenceId: produced.Value.Summary.DifferenceId));
        Assert.True(shown.Succeeded, shown.Refusal?.Message);
        Assert.Equal(broken, Assert.Single(shown.Value!.UnreadableEvidence!).ProposalId);
        Assert.NotEmpty(Assert.Single(shown.Value.Words, word => word.Word == "sita").Evidence!);
    }

    [Fact]
    public void ARevisionThatBecomesReadableIsReadAgainAndTheDifferenceIsMadeAgain()
    {
        LoadAgentEvaluationKind();
        using var fixture = new Fixture(_root, humanApprovesSita: false);
        fixture.StageAppliedAgentApproval(SitaAnalysis);
        var broken = fixture.StageUnreadableApplied("Broken stager", Fixture.UndecidedRecord(MutuWordform));
        var first = ProduceSourceValidated(fixture.ProduceRequest()).Value!;

        fixture.RestoreRevision(broken);
        var second = ProduceSourceValidated(fixture.ProduceRequest()).Value!;

        // The restored revision stages nothing, so only the unreadable ids tell the two readings apart.
        Assert.Single(first.UnreadableEvidence!);
        Assert.Null(second.UnreadableEvidence);
        Assert.NotEqual(first.Summary.DecisionsDigest, second.Summary.DecisionsDigest);
        Assert.NotEqual(first.Summary.DifferenceId, second.Summary.DifferenceId);
        Assert.Equal(1, second.Summary.WordsWithApprovedDecisions);
    }

    [Fact]
    public void TheStagedDecisionsAreReadOnceUntilAnAppliedRevisionChanges()
    {
        LoadAgentEvaluationKind();
        using var fixture = new Fixture(_root, humanApprovesSita: false);
        fixture.StageAppliedAgentApproval(SitaAnalysis);

        var first = SourceValidatedDecisions.Read(fixture.Database);
        var second = SourceValidatedDecisions.Read(fixture.Database);
        fixture.StageAppliedAgentApproval(SitaAnalysis, agentName: "Another source");
        var third = SourceValidatedDecisions.Read(fixture.Database);

        Assert.Same(first, second);
        Assert.NotSame(first, third);
        Assert.Equal(2, third.Opinions.Count);
    }

    [Fact]
    public void ASourceValidatedDifferenceReadAfterItsProposalIsDecidedKeepsItsReferenceAndNotes()
    {
        LoadAgentEvaluationKind();
        using var fixture = new Fixture(_root, humanApprovesSita: false);
        fixture.StageAppliedAgentApproval(SitaAnalysis);
        var produced = ProduceSourceValidated(fixture.ProduceRequest()).Value!;
        Assert.NotEmpty(produced.Words);

        new ProposalRepository(fixture.Database).SetStatus(CanonicalId.Parse(fixture.ProposalId), "rejected", null);
        var shown = ProposalDifferenceCommands.Show(new ProposalDifferenceShowRequest(
            fixture.ProjectPath, DifferenceId: produced.Summary.DifferenceId));

        Assert.True(shown.Succeeded, shown.Refusal?.Message);
        Assert.False(shown.Value!.WordDetailKept);
        Assert.Empty(shown.Value.Words);
        Assert.Equal(ProposalDifferenceReferences.SourceValidated, shown.Value.Summary.Reference);
        Assert.Contains(shown.Value.Summary.Warnings, warning =>
            warning.StartsWith(ProposalDifferenceWarnings.SourceValidated, StringComparison.Ordinal));
        Assert.Equal(1, shown.Value.Summary.WordsWithApprovedDecisions);
    }

    [Fact]
    public void ABothOrUnknownReferenceIsRefusedWithAnExplanation()
    {
        using var fixture = new Fixture(_root);

        foreach (var reference in new[] { "both", "gold", "" })
        {
            var refused = ProposalDifferenceCommands.Produce(fixture.ProduceRequest(), reference);
            Assert.Equal(RefusalCodes.DifferenceInvalidReference, refused.Refusal?.Code);
            Assert.Contains("human or source-validated", refused.Refusal!.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ASummarySavedBeforeTheReferenceExistedReadsBackAsHuman()
    {
        using var fixture = new Fixture(_root);
        var summary = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!.Summary;
        var options = MotifJson.CreateOptions();
        var node = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(summary, options))!.AsObject();
        node.Remove(nameof(ProposalDifferenceSummary.Reference));
        node.Remove(nameof(ProposalDifferenceSummary.DecisionsDigest));

        var old = JsonSerializer.Deserialize<ProposalDifferenceSummary>(node.ToJsonString(), options)!;

        Assert.Equal(ProposalDifferenceReferences.Human, old.Reference);
        Assert.Null(old.DecisionsDigest);
    }

    [Fact]
    public void ASourceValidatedSummaryStaysSmall()
    {
        LoadAgentEvaluationKind();
        using var fixture = new Fixture(_root, extraWords: 2000);
        fixture.StageAppliedAgentApproval(SitaAnalysis);

        var summary = ProduceSourceValidated(fixture.ProduceRequest()).Value!.Summary;

        Assert.InRange(fixture.SummaryBytes(summary.DifferenceId), 1, 8 * 1024);
    }

    // Run by hand: MOTIF_MEASURE_DIFFERENCE_READ=<file> writes the elapsed times of a 100,000-word Difference read.
    [Fact]
    public void AHundredThousandWordDifferencePageReadsInAFewSecondsWithManyAppliedProposals()
    {
        var report = Environment.GetEnvironmentVariable("MOTIF_MEASURE_DIFFERENCE_READ");
        if (string.IsNullOrEmpty(report)) return;
        LoadAgentEvaluationKind();
        using var fixture = new Fixture(_root, extraWords: 100_000, humanApprovesSita: false);
        for (var proposal = 0; proposal < 300; proposal++)
            fixture.StageProposal("applied", string.Join(",", Enumerable.Range(0, 50).Select(_ =>
                Fixture.AgentApproval(proposal % 7 == 0 ? SitaAnalysis : Guid.NewGuid()))));
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var produced = ProduceSourceValidated(fixture.ProduceRequest());
        var produceMs = timer.Elapsed.TotalMilliseconds;
        Assert.True(produced.Succeeded, produced.Refusal?.Message);
        var request = new ProposalDifferenceShowRequest(fixture.ProjectPath, DifferenceId: produced.Value!.Summary.DifferenceId);
        timer.Restart();
        var warm = ProposalDifferenceCommands.Show(request);
        var warmMs = timer.Elapsed.TotalMilliseconds;
        SourceValidatedDecisions.ForgetKept();
        timer.Restart();
        var cold = ProposalDifferenceCommands.Show(request);
        var coldMs = timer.Elapsed.TotalMilliseconds;
        timer.Restart();
        ProposalDifferenceCommands.Show(request with { CouldBeBad = true });
        var filterMs = timer.Elapsed.TotalMilliseconds;
        Assert.Equal(25, warm.Value!.Words.Count);
        Assert.Equal(produced.Value.Summary.Words, produced.Value.MatchingWords);
        var line = string.Join(' ', "words=" + produced.Value.Summary.Words, "appliedProposals=300", "evidenceRecords=15000",
            "produceMs=" + produceMs.ToString("F0", System.Globalization.CultureInfo.InvariantCulture),
            "pageWarmMs=" + warmMs.ToString("F0", System.Globalization.CultureInfo.InvariantCulture),
            "pageColdMs=" + coldMs.ToString("F0", System.Globalization.CultureInfo.InvariantCulture),
            "filterClickMs=" + filterMs.ToString("F0", System.Globalization.CultureInfo.InvariantCulture));
        File.WriteAllText(report, line + Environment.NewLine);
    }

    private sealed partial class Fixture
    {
        public void StageAppliedAgentApproval(Guid analysis, bool includeEvidence = true, string agentName = "Source validation")
        {
            var evidence = includeEvidence ? "," + SourceApprovedEvidence : string.Empty;
            StageApplied(Evaluation(analysis, CanonicalId.Mint("agent/").Value, agentName, true) + evidence + "}");
        }

        public void StageAppliedAgentApprovalBy(Guid analysis, string agentId) =>
            StageApplied(Evaluation(analysis, agentId, "Source validation", true) + "," + SourceApprovedEvidence + "}");

        public string StageProposal(string status, string operations) => StageApplied(operations, status);

        // Stages an agent approval in the very Proposal the Difference compares, which is not Applied.
        public void StageComparedProposalAgentApproval(Guid analysis)
        {
            var json = "{\"contractVersions\":{\"analysis\":\"1.0\"},\"proposalId\":\"" + ProposalId +
                "\",\"requires\":[],\"operations\":[" + AgentApproval(analysis) + "]}";
            var proposal = ProposalJsonParser.Parse(json);
            new ProposalRepository(Database).SaveRevision(new ProposalRevisionRecord(
                CanonicalId.Parse(ProposalId), SIL.Motif.Contract.Canonicalization.IntentDigest.Compute(proposal), json,
                "proposed", null, null, null));
        }

        // An Applied Proposal whose current revision no longer parses, so its envelope cannot be read.
        public string StageUnreadableApplied(string label, string? operations = null)
        {
            var id = StageApplied(operations ?? AgentApproval(SitaAnalysis), "applied", label);
            using var connection = Database.OpenConnection();
            using var read = connection.CreateCommand();
            read.CommandText = "SELECT IntentDigest, ProposalJson FROM ProposalRevisions WHERE ProposalId = $id;";
            read.Parameters.AddWithValue("$id", id);
            using (var reader = read.ExecuteReader())
            {
                reader.Read();
                _removedRevisions[id] = (reader.GetString(0), System.Text.Encoding.UTF8.GetString((byte[])reader[1]));
            }
            Sql("UPDATE ProposalRevisions SET ProposalJson = CAST('{\"broken\"' AS BLOB) WHERE ProposalId = '" + id + "';");
            return id;
        }

        public void RestoreRevision(string proposalId)
        {
            var (digest, json) = _removedRevisions[proposalId];
            Sql("UPDATE ProposalRevisions SET ProposalJson = CAST('" + json.Replace("'", "''") +
                "' AS BLOB) WHERE ProposalId = '" + proposalId + "' AND IntentDigest = '" + digest + "';");
        }

        private readonly Dictionary<string, (string Digest, string Json)> _removedRevisions = [];

        // A record that decides nothing, so reading it adds no opinion and no incorrect word form.
        public static string UndecidedRecord(Guid target) => "{\"operationId\":\"" +
            CanonicalId.Mint("operation/").Value + "\",\"kind\":\"analysis/decisionEvidence/record\",\"target\":\"" +
            CanonicalId.FromGuid(target).Value + "\",\"after\":{},\"provenance\":[{\"type\":\"decisionEvidence\"," +
            "\"tier\":\"undecided\",\"sources\":[],\"detail\":{}}]}";

        public void StageAppliedEvidenceOnly(Guid target) => StageApplied("{\"operationId\":\"" +
            CanonicalId.Mint("operation/").Value + "\",\"kind\":\"analysis/decisionEvidence/record\",\"target\":\"" +
            CanonicalId.FromGuid(target).Value + "\",\"after\":{},\"provenance\":[{\"type\":\"decisionEvidence\"," +
            "\"tier\":\"misspelled-typo\",\"sources\":[],\"detail\":{\"intendedForm\":\"mutu\",\"editType\":\"vowel\"}}]}");

        public void StageOpposingAgentEvaluations(Guid analysis, bool reverse)
        {
            var evaluations = new[] { (SourceAgentA, "Source validation A", true), (SourceAgentB, "Source validation B", false) };
            if (reverse) Array.Reverse(evaluations);
            StageApplied(string.Join(",", evaluations.Select(evaluation =>
                Evaluation(analysis, CanonicalId.FromGuid(evaluation.Item1).Value, evaluation.Item2, evaluation.Item3) +
                "," + SourceApprovedEvidence + "}")));
        }

        public static string AgentApproval(Guid analysis) =>
            Evaluation(analysis, CanonicalId.Mint("agent/").Value, "Source validation", true) + "," + SourceApprovedEvidence + "}";

        private const string SourceApprovedEvidence =
            "\"provenance\":[{\"type\":\"decisionEvidence\",\"tier\":\"source-approved\",\"sources\":[" +
            "{\"kind\":\"dictionary\",\"source\":\"Test dictionary\"},{\"kind\":\"grammar\",\"source\":\"Test grammar\"}," +
            "{\"kind\":\"corpus\",\"source\":\"Test corpus\"}]}]";

        // The operation is left open at its last property so a caller can append provenance.
        private static string Evaluation(Guid analysis, string agentId, string agentName, bool approves) =>
            "{\"operationId\":\"" + CanonicalId.Mint("operation/").Value +
            "\",\"kind\":\"analysis/wfiAnalysis/addAgentEvaluations\",\"target\":\"" + CanonicalId.FromGuid(analysis).Value +
            "\",\"after\":{\"agent\":{\"id\":\"" + agentId + "\",\"name\":\"" + agentName + "\",\"version\":\"1\"}," +
            "\"member\":\"" + (approves ? "approves" : "disapproves") + "\"}";

        private string StageApplied(string operations, string status = "applied", string? label = null)
        {
            var proposalId = CanonicalId.Mint("proposal/");
            var json = "{\"contractVersions\":{\"analysis\":\"1.0\"},\"proposalId\":\"" + proposalId.Value +
                "\",\"requires\":[],\"operations\":[" + operations + "]}";
            var proposal = ProposalJsonParser.Parse(json);
            var repository = new ProposalRepository(Database);
            repository.SaveRevision(new ProposalRevisionRecord(
                proposalId, SIL.Motif.Contract.Canonicalization.IntentDigest.Compute(proposal), json, "proposed", label, null, null));
            repository.SetStatus(proposalId, status, null);
            return proposalId.Value;
        }
    }
}
