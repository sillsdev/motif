using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Host.Analysis;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Store;
using SIL.Motif.Host.Texts;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed partial class ProposalDifferenceCommandsTests : IDisposable
{
    private const string FormA = "11111111-1111-1111-1111-111111111111";
    private const string FormB = "22222222-2222-2222-2222-222222222222";
    private const string Msa = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "SIL.Motif.ProposalDifferenceTests",
        Guid.NewGuid().ToString("N"));

    public ProposalDifferenceCommandsTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void ImprovedApprovedWordAndNewlyAcceptedMisspellingAreFiledSeparately()
    {
        using var fixture = new Fixture(_root);

        var produced = ProposalDifferenceCommands.Produce(fixture.ProduceRequest());

        Assert.True(produced.Succeeded, produced.Refusal?.Message);
        var summary = produced.Value!.Summary;
        Assert.Equal(2, summary.Words);
        Assert.Equal(3, summary.Occurrences);
        Assert.Equal(0, summary.UnboundOccurrences);
        Assert.Equal(2, summary.CouldBeBadWords);
        Assert.Equal(1, summary.ImprovedWords);
        Assert.Contains(summary.Matrix, cell => cell is { BeforeState: "no-approved-match", AfterState: "approved-unique", Words: 1 });
        var sita = Assert.Single(produced.Value.Words, word => word.Word == "sita");
        Assert.Equal(2, sita.Occurrences);
        Assert.Contains(ProposalDifferenceBucket.ImprovedApprovedMatch, sita.Buckets);
        Assert.Contains(ProposalDifferenceBucket.AnalysisChanged, sita.Buckets);
        Assert.DoesNotContain(sita.Buckets, bucket => bucket is ProposalDifferenceBucket.Regression or ProposalDifferenceBucket.Overgeneration);
        var mutu = Assert.Single(produced.Value.Words, word => word.Word == "mutu");
        Assert.Contains(ProposalDifferenceBucket.Overgeneration, mutu.Buckets);
    }

    [Fact]
    public void ShowFiltersToCouldBeBadWordsAndPagesTheMatches()
    {
        using var fixture = new Fixture(_root);
        var produced = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!;

        var bad = ProposalDifferenceCommands.Show(new ProposalDifferenceShowRequest(fixture.ProjectPath,
            DifferenceId: produced.Summary.DifferenceId, CouldBeBad: true, Bucket: "delta.overgeneration")).Value!;
        var onlyBucket = ProposalDifferenceCommands.Show(new ProposalDifferenceShowRequest(fixture.ProjectPath,
            ProposalId: fixture.ProposalId, Bucket: "delta.improved.approved-match")).Value!;
        var paged = ProposalDifferenceCommands.Show(new ProposalDifferenceShowRequest(fixture.ProjectPath,
            DifferenceId: produced.Summary.DifferenceId, Limit: 1)).Value!;

        Assert.Equal(["mutu"], bad.Words.Select(word => word.Word));
        Assert.Equal(["sita"], onlyBucket.Words.Select(word => word.Word));
        Assert.Equal(2, paged.MatchingWords);
        Assert.Equal(1, paged.NextOffset);
        Assert.Equal(JsonSerializer.Serialize(produced.Summary, MotifJson.CreateOptions()),
            JsonSerializer.Serialize(paged.Summary, MotifJson.CreateOptions()));
    }

    [Fact]
    public void ABadBucketCodeAndAMissingTrialAreRefusedWithNamedCodes()
    {
        using var fixture = new Fixture(_root);

        var bucket = ProposalDifferenceCommands.Show(new ProposalDifferenceShowRequest(fixture.ProjectPath,
            ProposalId: fixture.ProposalId, Bucket: "delta.nonsense"));
        var none = ProposalDifferenceCommands.Show(new ProposalDifferenceShowRequest(fixture.ProjectPath,
            ProposalId: fixture.ProposalId));
        var noTrial = ProposalDifferenceCommands.Produce(new ProposalDifferenceProduceRequest(fixture.ProjectPath,
            CanonicalId.Mint("proposal/").Value, fixture.TextId.ToString("D")));

        Assert.Equal("difference.invalid-bucket", bucket.Refusal!.Code);
        Assert.Equal("difference.none", none.Refusal!.Code);
        Assert.Equal("difference.no-trial", noTrial.Refusal!.Code);
    }

    [Fact]
    public void ARecordedBaselineAfterTheTrialRefusesBecauseTheDecisionsMayDiffer()
    {
        using var fixture = new Fixture(_root);
        fixture.RecordBaseline("sha256:" + new string('9', 64), "2026-09-24T12:00:00Z", semantic: '7');

        var outcome = ProposalDifferenceCommands.Produce(fixture.ProduceRequest());

        Assert.Equal("difference.baseline-changed", outcome.Refusal!.Code);
    }

    [Fact]
    public void AWordOnlyMeasuredAfterTheProposalIsKeptWithNotMeasuredBefore()
    {
        using var fixture = new Fixture(_root, beforeOmitsSita: true);

        var produced = ProposalDifferenceCommands.Produce(fixture.ProduceRequest());

        var sita = Assert.Single(produced.Value!.Words, word => word.Word == "sita");
        Assert.Equal(HumanAgreementApprovalState.NotMeasured, sita.Facts.Before.ApprovedState);
        Assert.False(sita.Facts.CompleteComparison);
    }

    [Fact]
    public void AFormSharedByTwoWordformsIsCountedAsUnboundAndNeverGuessed()
    {
        using var fixture = new Fixture(_root, ambiguousMutu: true);

        var produced = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!;

        Assert.Equal(["sita"], produced.Words.Select(word => word.Word));
        Assert.Equal(2, produced.Summary.UnboundOccurrences);
        Assert.Equal(2, produced.Summary.Occurrences);
    }

    [Fact]
    public void PagingPastTheEndReturnsNoWordsAndNoNextOffset()
    {
        using var fixture = new Fixture(_root);
        var id = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!.Summary.DifferenceId;

        var page = ProposalDifferenceCommands.Show(new ProposalDifferenceShowRequest(fixture.ProjectPath,
            DifferenceId: id, Offset: 5)).Value!;

        Assert.Empty(page.Words);
        Assert.Equal(2, page.MatchingWords);
        Assert.Null(page.NextOffset);
    }

    [Fact]
    public void MalformedShowRequestsAndUnknownTextsAreRefusedWithNamedCodes()
    {
        using var fixture = new Fixture(_root);

        Assert.Equal("difference.invalid-page", ProposalDifferenceCommands.Show(new ProposalDifferenceShowRequest(
            fixture.ProjectPath, ProposalId: fixture.ProposalId, Limit: 0)).Refusal!.Code);
        Assert.Equal("difference.invalid-state", ProposalDifferenceCommands.Show(new ProposalDifferenceShowRequest(
            fixture.ProjectPath, ProposalId: fixture.ProposalId, BeforeState: "nonsense")).Refusal!.Code);
        Assert.Equal("difference.text-not-found", ProposalDifferenceCommands.Produce(new ProposalDifferenceProduceRequest(
            fixture.ProjectPath, fixture.ProposalId, Guid.NewGuid().ToString("D"))).Refusal!.Code);
        Assert.Equal("difference.invalid-text", ProposalDifferenceCommands.Produce(new ProposalDifferenceProduceRequest(
            fixture.ProjectPath, fixture.ProposalId, "not-a-guid")).Refusal!.Code);
    }

    [Fact]
    public void ASavedWordWhoseBytesChangedLeavesTheSummaryReadableAndSaysTheWordsCannotBeRead()
    {
        using var fixture = new Fixture(_root);
        var produced = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!.Summary;
        fixture.Sql("UPDATE ProposalDifferenceCases SET FactsJson = REPLACE(FactsJson, 'complete', 'incomplete') WHERE Word = 'sita';");

        var outcome = ProposalDifferenceCommands.Show(new ProposalDifferenceShowRequest(fixture.ProjectPath,
            DifferenceId: produced.DifferenceId));

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        Assert.Equal(produced.DifferenceId, outcome.Value!.Summary.DifferenceId);
        Assert.Equal(produced.Words, outcome.Value.Summary.Words);
        Assert.True(outcome.Value.WordDetailUnreadable);
        Assert.False(outcome.Value.WordDetailKept);
        Assert.Empty(outcome.Value.Words);
        Assert.Equal(0, outcome.Value.MatchingWords);
    }

    [Fact]
    public void ProducingAgainReplacesADifferenceWhoseWordRowIsDamaged()
    {
        using var fixture = new Fixture(_root);
        var first = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!.Summary;
        fixture.Sql("UPDATE ProposalDifferenceCases SET FactsJson = REPLACE(FactsJson, 'complete', 'incomplete') WHERE Word = 'mutu';");

        var again = ProduceAsTheWindowDoes(fixture.ProduceRequest());

        Assert.True(again.Succeeded, again.Refusal?.Message);
        Assert.NotEqual(first.DifferenceId, again.Value!.Summary.DifferenceId);
        Assert.False(again.Value.WordDetailUnreadable);
        Assert.True(again.Value.WordDetailKept);
        Assert.Equal(1, fixture.DifferenceRows());
        Assert.NotEmpty(again.Value.Words);
    }

    [Fact]
    public void AFailedPublishLeavesNoRowsBehind()
    {
        using var fixture = new Fixture(_root);
        var summary = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!.Summary with
        { DifferenceId = "difference/rollback" };
        var facts = HumanAgreementDifferenceClassifier.Compare(null, null,
            HumanAgreementClassifier.Compile([], CapturedSpellingStatus.Undecided));
        var twice = new NewProposalDifferenceWord("same", facts, []);

        Assert.ThrowsAny<Exception>(() => new ProposalDifferenceRepository(fixture.Database)
            .Publish(new NewProposalDifference(summary, [twice, twice])));

        Assert.Equal(0, fixture.Count("ProposalDifferences", "difference/rollback"));
        Assert.Equal(0, fixture.Count("ProposalDifferenceCases", "difference/rollback"));
    }

    [Fact]
    public void DeletingAProposalRemovesItsDifferencesAndEveryChildRow()
    {
        using var fixture = new Fixture(_root);
        var other = CanonicalId.Mint("proposal/");
        var proposals = new ProposalRepository(fixture.Database);
        proposals.SaveRevision(new ProposalRevisionRecord(other, "sha256:" + new string('4', 64), "{}", "proposed", null, null, null));
        var produced = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!;
        var summary = produced.Summary with { DifferenceId = "difference/doomed", ProposalId = other.Value };
        var facts = Assert.Single(produced.Words, word => word.Word == "mutu").Facts;
        Assert.NotEmpty(facts.Buckets);
        new ProposalDifferenceRepository(fixture.Database).Publish(new NewProposalDifference(summary,
            [new NewProposalDifferenceWord("w", facts, [new ProposalDifferenceOccurrence("t", "s", 0, 1)])]));
        Assert.Equal(1, fixture.Count("ProposalDifferenceOccurrences", "difference/doomed"));
        // A store from before word detail was dropped on decision can still hold full rows for a decided Proposal.
        fixture.Sql($"UPDATE Proposals SET Status = 'rejected' WHERE ProposalId = '{other.Value}';");


        Assert.True(fixture.Count("ProposalDifferenceBuckets", "difference/doomed") > 0);
        proposals.DeleteArchived(other);

        foreach (var table in new[] { "ProposalDifferences", "ProposalDifferenceCases", "ProposalDifferenceOccurrences", "ProposalDifferenceBuckets" })
            Assert.Equal(0, fixture.Count(table, "difference/doomed"));
        Assert.Equal(2, fixture.Count("ProposalDifferenceCases", produced.Summary.DifferenceId));
    }

    [Fact]
    public void TwoDifferentParsesOfOneFormInAnAssessmentAreRefusedRatherThanPickedAtRandom()
    {
        using var fixture = new Fixture(_root, duplicateBefore: true);

        var outcome = ProposalDifferenceCommands.Produce(fixture.ProduceRequest());

        Assert.Equal("difference.duplicate-parse-rows", outcome.Refusal!.Code);
    }

    [Theory]
    [InlineData("no-opinion")]
    [InlineData("Approved")]
    public void AnOpinionOutsideTheCapturedVocabularyIsATypedRefusalNotAThrowFromTheClassifier(string opinion)
    {
        var refusal = Assert.Throws<ProposalDifferenceCommands.DifferenceRefusalException>(
            () => ProposalDifferenceCommands.GradeOf(opinion));

        Assert.Equal("difference.unexpected-opinion", refusal.Refusal.Code);
        Assert.Equal(ReadingGrade.Candidate, ProposalDifferenceCommands.GradeOf("unknown"));
    }

    [Fact]
    public void ACandidateAnalysisInTheCaptureIsReadAsAnUnknownReadingAndNotRefused()
    {
        using var fixture = new Fixture(_root);

        var sita = Assert.Single(ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Value!.Words,
            word => word.Word == "sita");

        Assert.Equal(HumanAgreementApprovalState.NoApprovedMatch, sita.Facts.Before.ApprovedState);
        Assert.Contains(sita.Facts.Before.MatchedUnknownAnalysisIds, id => id.Length > 0);
    }

    [Fact]
    public void ATrialWithNoRecordedBaselineAssessmentSaysSoAndNamesTheRemedy()
    {
        using var fixture = new Fixture(_root, noBaselineIds: true);

        var refusal = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Refusal!;

        Assert.Equal("difference.no-before", refusal.Code);
        Assert.Contains("No Baseline Assessment was recorded", refusal.Message);
        Assert.Contains("Re-run the Trial", refusal.Message);
    }

    [Fact]
    public void ABaselineAssessmentBySomeOtherAssessorSaysNoParseTimeAssessmentMatches()
    {
        using var fixture = new Fixture(_root, beforeAssessor: "someone-else");

        var refusal = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Refusal!;

        Assert.Equal("difference.no-before", refusal.Code);
        Assert.Contains("no ParseTime Assessment by the assessor 'pangloss'", refusal.Message);
        Assert.Contains("Re-run the Trial", refusal.Message);
    }

    [Fact]
    public void ABaselineAssessmentOnAnotherBaselineTokenSaysTheBaselineChanged()
    {
        using var fixture = new Fixture(_root, beforeTokenSeed: 'b');

        var refusal = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Refusal!;

        Assert.Equal("difference.no-before", refusal.Code);
        Assert.Contains("The Baseline changed since the Trial", refusal.Message);
        Assert.Contains("Recapture the Baseline, then re-run the Trial", refusal.Message);
    }

    [Fact]
    public void ABoundBeforeProducesTheSameDifferenceAsFreshEvidence()
    {
        using var fresh = new Fixture(_root);
        using var reused = new Fixture(_root, beforeTokenSeed: 'b');
        new BeforeAssessmentBindingRepository(reused.Database).Bind("before", reused.Token.SemanticIdentity);

        var expected = ProposalDifferenceCommands.Produce(fresh.ProduceRequest());
        var actual = ProposalDifferenceCommands.Produce(reused.ProduceRequest());

        Assert.True(expected.Succeeded, expected.Refusal?.Message);
        Assert.True(actual.Succeeded, actual.Refusal?.Message);
        Assert.Equal(expected.Value!.Summary.Matrix, actual.Value!.Summary.Matrix);
        Assert.Equal(expected.Value.Summary.CouldBeBadWords, actual.Value.Summary.CouldBeBadWords);
        Assert.Equal(expected.Value.Summary.ImprovedWords, actual.Value.Summary.ImprovedWords);
    }

    [Fact]
    public void ABaselineAssessmentWithNoWordResultsSaysThereIsNothingToCompare()
    {
        using var fixture = new Fixture(_root, beforeNoWords: true);

        var refusal = ProposalDifferenceCommands.Produce(fixture.ProduceRequest()).Refusal!;

        Assert.Equal("difference.no-before", refusal.Code);
        Assert.Contains("no per-word results", refusal.Message);
    }

    [Fact]
    public void ACliStyleTrialRecordsItsBaselineAssessmentsSoTheDifferenceCanBeProduced()
    {
        using var fixture = new Fixture(_root, noBaselineIds: true);
        IReadOnlyList<string>? assessed = null;

        var queued = TrialBaselines.EnqueueWithBaseline(
            new SIL.Motif.Commands.Requests.EnqueueTrialRequest(fixture.ProjectPath, "1.0", fixture.ProposalId,
                Words: ["sita", "mutu"]),
            words =>
            {
                assessed = words;
                return CommandOutcome<AssessCommandResponse>.Success(new AssessCommandResponse(
                    null!, null!, ["before"], ""));
            });

        Assert.True(queued.Succeeded, queued.Refusal?.Message);
        Assert.Equal(["sita", "mutu"], assessed);
        var job = new JobRepository(fixture.Database).Get(queued.Value!.JobId)!;
        Assert.Equal(["before"], TrialJobInput.Parse(job.InputJson).BaselineAssessmentIds);
        fixture.CompleteTrial(queued.Value.JobId);

        var produced = ProposalDifferenceCommands.Produce(
            fixture.ProduceRequest() with { JobId = queued.Value.JobId });

        Assert.True(produced.Succeeded, produced.Refusal?.Message);
        Assert.Equal(2, produced.Value!.Summary.Words);
    }

    [Fact]
    public void ATrialWhoseBaselineCannotBeAssessedStillQueuesAndReportsWhy()
    {
        using var fixture = new Fixture(_root, noBaselineIds: true);
        string? reported = null;

        var queued = TrialBaselines.EnqueueWithBaseline(
            new SIL.Motif.Commands.Requests.EnqueueTrialRequest(fixture.ProjectPath, "1.0", fixture.ProposalId,
                Words: ["sita"]),
            _ => throw new InvalidOperationException("no parser"), reason => reported = reason);

        Assert.True(queued.Succeeded, queued.Refusal?.Message);
        Assert.Equal("no parser", reported);
        var job = new JobRepository(fixture.Database).Get(queued.Value!.JobId)!;
        Assert.Null(TrialJobInput.Parse(job.InputJson).BaselineAssessmentIds);
    }

    [Fact]
    public void ATrialQueuedThroughTheWorkflowHasABeforeSideAndProducesADifference()
    {
        using var fixture = new Fixture(_root, noBaselineIds: true);

        var queued = TrialWorkflow.Enqueue(
            new SIL.Motif.Commands.Requests.EnqueueTrialRequest(fixture.ProjectPath, "1.0", fixture.ProposalId),
            _ => CommandOutcome<AssessCommandResponse>.Success(new AssessCommandResponse(null!, null!, ["before"], "")),
            BaselineMode.BestEffort);

        Assert.True(queued.Succeeded, queued.Refusal?.Message);
        Assert.Null(queued.Value!.BaselineWarning);
        fixture.CompleteTrial(queued.Value.Job.JobId);
        var produced = ProposalDifferenceCommands.Produce(
            fixture.ProduceRequest() with { JobId = queued.Value.Job.JobId });
        Assert.True(produced.Succeeded, produced.Refusal?.Message);
        Assert.Equal(2, produced.Value!.Summary.Words);
    }

    [Fact]
    public void BestEffortQueuesWithAWarningAndRequiredRefusesWhenTheBaselineCannotBeAssessed()
    {
        using var fixture = new Fixture(_root, noBaselineIds: true);
        var request = new SIL.Motif.Commands.Requests.EnqueueTrialRequest(fixture.ProjectPath, "1.0", fixture.ProposalId,
            Words: ["sita"]);
        Func<IReadOnlyList<string>, CommandOutcome<AssessCommandResponse>> broken =
            _ => throw new InvalidOperationException("no parser");

        var required = TrialWorkflow.Enqueue(request, broken, BaselineMode.Required);
        var bestEffort = TrialWorkflow.Enqueue(request, broken, BaselineMode.BestEffort);

        Assert.Equal("difference.no-before", required.Refusal!.Code);
        Assert.Contains("no parser", required.Refusal.Message);
        Assert.True(bestEffort.Succeeded, bestEffort.Refusal?.Message);
        Assert.Equal("no parser", bestEffort.Value!.BaselineWarning);
        Assert.Single(new JobRepository(fixture.Database)
            .ListByProjectAndKind(ProjectWorkspaceKey.Compute(fixture.Project), TrialJobHandler.TrialKind),
            job => job.JobId != "trial-1");
    }

    [Theory]
    [InlineData(BaselineMode.BestEffort)]
    [InlineData(BaselineMode.Required)]
    public void ACancelledBaselineMeasurementReturnsTheCancellationAndQueuesNothing(BaselineMode mode)
    {
        using var fixture = new Fixture(_root, noBaselineIds: true);
        var request = new SIL.Motif.Commands.Requests.EnqueueTrialRequest(fixture.ProjectPath, "1.0", fixture.ProposalId,
            Words: ["sita"]);
        Func<IReadOnlyList<string>, CommandOutcome<AssessCommandResponse>> cancelled = _ =>
            CommandOutcome<AssessCommandResponse>.Refused(
                new Refusal("assess.cancelled", FailureReason.Cancelled, "The measurement was cancelled."));

        var outcome = TrialWorkflow.Enqueue(request, cancelled, mode);

        Assert.False(outcome.Succeeded);
        Assert.Null(outcome.Value);
        Assert.Equal(FailureReason.Cancelled, outcome.Refusal!.Reason);
        Assert.DoesNotContain(new JobRepository(fixture.Database)
            .ListByProjectAndKind(ProjectWorkspaceKey.Compute(fixture.Project), TrialJobHandler.TrialKind),
            job => job.JobId != "trial-1");
    }

    [Theory]
    [InlineData(BaselineMode.BestEffort)]
    [InlineData(BaselineMode.Required)]
    public void AStoppedBeforeAssessmentRecordsAStoppedTrial(BaselineMode mode)
    {
        using var fixture = new Fixture(_root, noBaselineIds: true);
        var request = new SIL.Motif.Commands.Requests.EnqueueTrialRequest(fixture.ProjectPath, "1.0", fixture.ProposalId,
            Words: ["sita"]);
        Func<IReadOnlyList<string>, CommandOutcome<AssessCommandResponse>> stopped = _ =>
            CommandOutcome<AssessCommandResponse>.Refused(new Refusal(RefusalCodes.AssessmentStoppedByBudget,
                FailureReason.StoppedByBudget, "Parsing stopped at the time limit: 0 words done, 1 not done.",
                new Dictionary<string, string> { ["wordsDone"] = "0", ["wordsNotDone"] = "1" }));
        var result = TrialWorkflow.Enqueue(request, stopped, mode);
        Assert.True(result.Succeeded, result.Refusal?.Message);
        var job = new JobRepository(fixture.Database).Get(result.Value!.Job.JobId)!;
        Assert.Equal(JobStatus.StoppedByBudget, job.Status);
        Assert.Contains("1 not done", job.ResultJson);
        Assert.Null(result.Value.BaselineWarning);
    }

    [Fact]
    public void RequiredQueuesWithTheBaselineWhenItIsAssessed()
    {
        using var fixture = new Fixture(_root, noBaselineIds: true);

        var queued = TrialWorkflow.Enqueue(
            new SIL.Motif.Commands.Requests.EnqueueTrialRequest(fixture.ProjectPath, "1.0", fixture.ProposalId),
            _ => CommandOutcome<AssessCommandResponse>.Success(new AssessCommandResponse(null!, null!, ["before"], "")),
            BaselineMode.Required);

        Assert.True(queued.Succeeded, queued.Refusal?.Message);
        Assert.Null(queued.Value!.BaselineWarning);
        var job = new JobRepository(fixture.Database).Get(queued.Value.Job.JobId)!;
        Assert.Equal(["before"], TrialJobInput.Parse(job.InputJson).BaselineAssessmentIds);
    }

    [Fact]
    public void ARefusedBaselineAssessmentRefusesRequiredAndWarnsBestEffort()
    {
        using var fixture = new Fixture(_root, noBaselineIds: true);
        var request = new SIL.Motif.Commands.Requests.EnqueueTrialRequest(fixture.ProjectPath, "1.0", fixture.ProposalId,
            Words: ["sita"]);
        Func<IReadOnlyList<string>, CommandOutcome<AssessCommandResponse>> refused = _ =>
            CommandOutcome<AssessCommandResponse>.Refused(new Refusal("assess.parser-unavailable",
                FailureReason.Refused, "parser missing"));

        var required = TrialWorkflow.Enqueue(request, refused, BaselineMode.Required);
        var bestEffort = TrialWorkflow.Enqueue(request, refused, BaselineMode.BestEffort);

        Assert.Equal("assess.parser-unavailable", required.Refusal!.Code);
        Assert.True(bestEffort.Succeeded, bestEffort.Refusal?.Message);
        Assert.Equal("parser missing", bestEffort.Value!.BaselineWarning);
    }

    [Fact]
    public void AnEnqueueRefusalIsNotRelabelledAsAMissingBaseline()
    {
        using var fixture = new Fixture(_root, noBaselineIds: true);

        var queued = TrialWorkflow.Enqueue(
            new SIL.Motif.Commands.Requests.EnqueueTrialRequest(fixture.ProjectPath, "1.0", fixture.ProposalId,
                Words: [" bad"]),
            _ => CommandOutcome<AssessCommandResponse>.Success(new AssessCommandResponse(null!, null!, ["before"], "")),
            BaselineMode.Required);

        Assert.NotEqual("difference.no-before", queued.Refusal!.Code);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    private sealed partial class Fixture : IDisposable
    {
        private readonly string _dir;
        private readonly string _tokenJson;
        private readonly bool _beforeOmitsSita;
        private readonly bool _ambiguousMutu;
        private readonly bool _duplicateBefore;
        private readonly string[]? _jobBaselineIds;
        private readonly string _beforeAssessor;
        private readonly char _beforeTokenSeed;
        private readonly bool _beforeNoWords;
        private readonly int _extraWords;
        private readonly bool _variedExtraWords;
        private readonly bool _limitedExtraWords;
        private readonly Func<int, int> _placesOfExtraWord;
        private readonly bool _cappedAfterMutu;
        private readonly string? _beforeParserSha;
        private readonly string? _afterParserSha;
        private readonly string[] _outsideText;
        private readonly bool _humanApprovesSita;
        private readonly bool _humanMarksMutuIncorrect;
        private readonly string _sitaTrial;
        private readonly string _sitaText;
        private string _trialInput = "";
        private string _trialDigest = "";

        public Fixture(string testRoot, bool beforeOmitsSita = false, bool ambiguousMutu = false, bool duplicateBefore = false,
            string[]? jobBaselineIds = null, bool noBaselineIds = false, string beforeAssessor = "pangloss",
            char beforeTokenSeed = 'a', bool beforeNoWords = false, int extraWords = 0, bool cappedAfterMutu = false,
            string? beforeParserSha = null, string? afterParserSha = null, string[]? outsideText = null,
            bool humanApprovesSita = true, bool humanMarksMutuIncorrect = true, string sitaTrial = "sita",
            string sitaText = "sita", bool variedExtraWords = false, Func<int, int>? placesOfExtraWord = null,
            bool limitedExtraWords = false)
        {
            _variedExtraWords = variedExtraWords;
            _limitedExtraWords = limitedExtraWords;
            _placesOfExtraWord = placesOfExtraWord ?? (_ => 1);
            _sitaTrial = sitaTrial;
            _sitaText = sitaText;
            _humanApprovesSita = humanApprovesSita;
            _humanMarksMutuIncorrect = humanMarksMutuIncorrect;
            _extraWords = extraWords;
            _cappedAfterMutu = cappedAfterMutu;
            _beforeParserSha = beforeParserSha;
            _afterParserSha = afterParserSha;
            _outsideText = outsideText ?? [];
            _jobBaselineIds = noBaselineIds ? null : jobBaselineIds ?? ["before"];
            _beforeAssessor = beforeAssessor;
            _beforeTokenSeed = beforeTokenSeed;
            _beforeNoWords = beforeNoWords;
            _duplicateBefore = duplicateBefore;
            _beforeOmitsSita = beforeOmitsSita;
            _ambiguousMutu = ambiguousMutu;
            _dir = System.IO.Path.Combine(testRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            ProjectPath = System.IO.Path.Combine(_dir, "project.fwdata");
            File.WriteAllText(ProjectPath, "synthetic project marker");
            Project = new ProjectLocator(ProjectPath, "project");
            Database = MotifDatabase.OpenOwned(System.IO.Path.Combine(_dir, "project.motif.db"), Project,
                MotifSchema.CurrentSchema, new Version(1, 0));
            Token = new BaselineToken("project-id", "sha256:" + new string('1', 64), "projection-v1",
                "2026-09-24T10:00:00Z", "sha256:" + new string('a', 64));
            _tokenJson = JsonSerializer.Serialize(Token, MotifJson.CreateOptions());
            RecordBaseline(Token.BundleDigest, Token.CapturedUtc);
            Seed();
        }

        public string ProjectPath { get; }
        public ProjectLocator Project { get; }
        public MotifDatabase Database { get; }
        public BaselineToken Token { get; }
        public Guid TextId { get; } = Guid.NewGuid();
        public string ProposalId { get; private set; } = "";

        public ProposalDifferenceProduceRequest ProduceRequest() =>
            new(ProjectPath, ProposalId, TextId.ToString("D"));

        public void RecordBaseline(string bundleDigest, string capturedUtc, char semantic = '1')
        {
            var token = new BaselineToken("project-id", "sha256:" + new string(semantic, 64), "projection-v1", capturedUtc,
                bundleDigest);
            var baselineRoot = System.IO.Path.Combine(_dir, "baseline-" + capturedUtc.Replace(':', '-'));
            var segment = Guid.NewGuid();
            var paragraph = Guid.NewGuid();
            var sitaId = Guid.Parse("33333333-3333-3333-3333-333333333333");
            var mutuId = Guid.Parse("44444444-4444-4444-4444-444444444444");
            var analysisId = Guid.Parse("55555555-5555-5555-5555-555555555555");
            var otherMutuId = Guid.Parse("77777777-7777-7777-7777-777777777777");
            var candidateId = Guid.Parse("66666666-6666-6666-6666-666666666666");
            var candidate = new TextWordsProjectedAnalysis("k2", [])
            {
                AnalysisId = candidateId, Opinion = "unknown",
                Identity = new ApprovedMorphology([new ApprovedMorph(FormB, Msa, null, ["sita"])])
                { SourceAnalysisId = CanonicalId.FromGuid(candidateId).Value },
            };
            var identity = new ApprovedMorphology([new ApprovedMorph(FormA, Msa, null, ["sita"])])
            { SourceAnalysisId = CanonicalId.FromGuid(analysisId).Value };
            TextWordsProjectedToken Token(string form, Guid wordform, int index) => new(form,
                [new WritingSystemText(form, "swh")], wordform, "analysed", null, null, null, null, index, null);
            var nextIndex = 10;
            var extraTokens = Enumerable.Range(0, _extraWords)
                .SelectMany(i => Enumerable.Range(0, _placesOfExtraWord(i)).Select(_ => Token(ExtraWord(i), ExtraId(i), nextIndex++)))
                .ToArray();
            var extraWordforms = Enumerable.Range(0, _extraWords).Select(i =>
            {
                var extraAnalysis = ExtraId(i);
                var approvedExtra = new TextWordsProjectedAnalysis("x" + i, [])
                {
                    AnalysisId = extraAnalysis, Opinion = ReadingGrade.Approved,
                    Identity = new ApprovedMorphology([new ApprovedMorph(FormA, Msa, null, [ExtraWord(i)])])
                    { SourceAnalysisId = CanonicalId.FromGuid(extraAnalysis).Value },
                };
                return new TextWordsProjectedWordform(ExtraId(i), [approvedExtra], [], 0, false, [approvedExtra]);
            }).ToArray();
            var text = new TextWordsProjectedText(TextId, "Hadithi",
                [new TextWordsProjectedLine(1, "sita mutu sita .", [Token(_sitaText, sitaId, 0), Token("mutu", mutuId, 1),
                    Token(_sitaText, sitaId, 2), .. extraTokens,
                    .. _ambiguousMutu ? [Token("mutu", otherMutuId, 4)] : Array.Empty<TextWordsProjectedToken>(),
                    new TextWordsProjectedToken(".", [new WritingSystemText(".", "swh")], null,
                        null, null, null, null, null, 3, null)], paragraph, segment, true)], []);
            var approved = new TextWordsProjectedAnalysis("k1", [])
            {
                AnalysisId = analysisId, Opinion = _humanApprovesSita ? ReadingGrade.Approved : "unknown", Identity = identity,
            };
            new BaselineRepository(Database).Record(ProjectWorkspaceKey.Compute(Project),
                new BaselinePublication(baselineRoot, System.IO.Path.Combine(baselineRoot, "project.fwdata"), token),
                DateTimeOffset.Parse("2026-09-24T10:30:00Z"), DateTimeOffset.Parse(capturedUtc),
                new TextWordsProjection([text],
                [
                    new TextWordsProjectedWordform(sitaId, _humanApprovesSita ? [approved] : [], [], _humanApprovesSita ? 1 : 2, false,
                        [approved, candidate]),
                    new TextWordsProjectedWordform(mutuId, [], [], 0, _humanMarksMutuIncorrect, []),
                    .. _ambiguousMutu ? [new TextWordsProjectedWordform(otherMutuId, [], [], 0, false, [])]
                        : Array.Empty<TextWordsProjectedWordform>(),
                    .. extraWordforms,
                ]),
                new ProjectSummarySnapshot(2 + _extraWords, (_ambiguousMutu ? 4 : 3) + _extraWords, 2 + _extraWords, 0, 0, ["sita", "mutu"],
                    [new ProjectTextSummary(TextId, "Hadithi", 2 + _extraWords, (_ambiguousMutu ? 4 : 3) + _extraWords,
                        new Dictionary<string, int>(StringComparer.Ordinal) { ["sita"] = 2, ["mutu"] = _ambiguousMutu ? 2 : 1 })]));
        }

        private void Seed()
        {
            var proposals = new ProposalRepository(Database);
            var id = CanonicalId.Mint("proposal/");
            var json = ProposalCommands.BuildProposalJson(new DraftDocument
            {
                ProposalId = id.Value,
                ContractVersions = new Dictionary<string, string>(StringComparer.Ordinal) { ["lexical"] = "1.0" },
                Operations =
                [
                    new DraftOperation
                    {
                        OperationId = CanonicalId.Mint("operation/").Value,
                        Kind = "lexical/lexSense/setGloss",
                        Target = CanonicalId.Mint("lexSense/").Value,
                        After = JsonDocument.Parse("{\"ws\":\"en\",\"text\":\"x\"}").RootElement.EnumerateObject()
                            .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal),
                    },
                ],
            });
            var digest = IntentDigest.Compute(ProposalJsonParser.Parse(json));
            proposals.SaveRevision(new ProposalRevisionRecord(id, digest, json, "proposed", null, null, null));
            ProposalId = id.Value;

            var assessments = new AssessmentRepository(Database);
            Record(assessments, "before", null, null, [Word(_sitaTrial, 0, Reading(FormB)), Word("mutu", 1),
                .. ExtraAssessed(before: true)]);
            Record(assessments, "after", id, digest, [Word(_sitaTrial, 0, Reading(FormA)),
                _cappedAfterMutu ? Capped("mutu", 1, Reading(FormB)) : Word("mutu", 1, Reading(FormB)),
                .. ExtraAssessed(before: false)]);
            var jobs = new JobRepository(Database);
            var input = JsonSerializer.Serialize(new
            {
                proposalJson = json, scope = (string?)null, words = new[] { "sita", "mutu" }, allWords = false,
                limits = (TrialLimits?)null, baselineAssessmentIds = _jobBaselineIds,
            }, MotifJson.CreateOptions());
            _trialInput = input;
            _trialDigest = digest;
            AddTrial("trial-1");
        }

        public string AddTrial(string key)
        {
            var jobs = new JobRepository(Database);
            var job = jobs.Create(key, ProjectWorkspaceKey.Compute(Project), TrialJobHandler.TrialKind, _trialInput,
                "2026-09-24T11:00:00Z");
            job = jobs.Transition(job.JobId, JobStatus.Running);
            job = jobs.PublishDryRun(job.JobId, DryRun(_trialDigest), job.Version);
            jobs.Transition(job.JobId, JobStatus.Completed, JsonSerializer.Serialize(new
            {
                baselineToken = Token, assessmentIds = new[] { "after" }, allWords = false,
            }, MotifJson.CreateOptions()));
            return job.JobId;
        }

        public void CompleteTrial(string jobId)
        {
            var jobs = new JobRepository(Database);
            var job = jobs.Transition(jobId, JobStatus.Running);
            job = jobs.PublishDryRun(job.JobId, DryRun(IntentDigest.Compute(ProposalJsonParser.Parse(
                TrialJobInput.Parse(job.InputJson).ProposalJson))), job.Version);
            jobs.Transition(job.JobId, JobStatus.Completed, JsonSerializer.Serialize(new
            {
                baselineToken = Token, assessmentIds = new[] { "after" }, allWords = false,
            }, MotifJson.CreateOptions()));
        }

        private void Record(AssessmentRepository assessments, string id, CanonicalId? proposal, string? digest,
            AssessedWord[] words)
        {
            if (_beforeOmitsSita && id == "before") words = words.Where(word => word.Word != "sita").ToArray();
            if (_duplicateBefore && id == "before") words = [.. words, Word("sita", 5, Reading(FormA))];
            if (_beforeNoWords && id == "before") words = [];
            var before = id == "before";
            var tokenJson = before && _beforeTokenSeed != 'a'
                ? JsonSerializer.Serialize(new BaselineToken("project-id", "sha256:" + new string(_beforeTokenSeed, 64),
                    "projection-v1", "2026-09-24T10:00:00Z", "sha256:" + new string('a', 64)), MotifJson.CreateOptions())
                : _tokenJson;
            assessments.Record(new NewAssessmentRecord(id, proposal, digest, before ? _beforeAssessor : "pangloss",
                AssessmentKinds.ParseTime, "{}", "sha256:scope", new SIL.Motif.Host.Assess.AssessmentSubject.Words(
                    Selection.Create("Default", ["sita", "mutu"]), "whitespace", "1"), tokenJson,
                "sha256:outcome", "sha256:semantic", "sha256:grammar", "fingerprint", "pipeline", 0,
                new AssessmentRecordMaterial.Words(words, null, null, []) with { Invocation = Invocation(id, before) },
                SavedUtc: "2026-09-24T11:00:00Z"));
        }

        private static Guid ExtraId(int index) => new(index + 1, 7, 7, 0, 0, 0, 0, 0, 0, 0, 1);

        private static string ExtraWord(int index) => "w" + index.ToString("D5", System.Globalization.CultureInfo.InvariantCulture);

        // A varied word has no parse, the wrong one, the approved one or both on each side: sixteen cells.
        private static ParseAnalysis[] Pattern(int pattern) => pattern switch
        {
            0 => [],
            1 => [Reading(FormB)],
            2 => [Reading(FormA)],
            _ => [Reading(FormA), Reading(FormB)],
        };

        // A third of the extra words gain a parse, a third change it, and a third stay the same.
        private IEnumerable<AssessedWord> ExtraAssessed(bool before)
        {
            for (var i = 0; i < _extraWords; i++)
            {
                if (_variedExtraWords)
                {
                    var readings = Pattern(before ? i % 4 : i / 4 % 4);
                    yield return _limitedExtraWords ? Limited(ExtraWord(i), 10 + i, i, before, readings)
                        : Word(ExtraWord(i), 10 + i, readings);
                    continue;
                }
                var reading = i % 3 == 0 ? (before ? null : Reading(FormA)) : i % 3 == 1 ? Reading(before ? FormB : FormA) : Reading(FormA);
                yield return Word(ExtraWord(i), 10 + i, reading is null ? [] : [reading]);
            }
            if (!before) yield break;
            foreach (var outside in _outsideText) yield return Word(outside, 90, Reading(FormA));
        }

        // Every few words one side was held back by a different limit, so the Difference fills many limitation groups.
        private static AssessedWord Limited(string word, int index, int i, bool before, ParseAnalysis[] readings) =>
            new(word, "analysed", []) { Morphology = new ParseWordEvidence("fieldworks-parse-analysis/v1", index, word, 1,
                before && i % 5 == 0, !before && i % 7 == 0, before && i % 11 == 0, readings, []) };

        private static AssessedWord Capped(string word, int index, params ParseAnalysis[] readings) =>
            new(word, "analysed", []) { Morphology = new ParseWordEvidence("fieldworks-parse-analysis/v1", index, word, 1,
                true, false, false, readings, []) };

        private BatchInvocationEvidence? Invocation(string id, bool before)
        {
            var sha = before ? _beforeParserSha : _afterParserSha;
            if (sha is null) return null;
            return new BatchInvocationEvidence("invocation-" + id, "src", "sha256:src", sha, "words", "sha256:words",
                "tsv", "sha256:tsv", "err", "sha256:err", null, ParserWorkLimit.Default, 1, false)
            { ExecutableVersion = before ? "0.1.0" : "0.2.0", GrammarExportFormat = GrammarExportFormats.FwData };
        }

        private static AssessedWord Word(string word, int index, params ParseAnalysis[] readings) =>
            new(word, "analysed", []) { Morphology = new ParseWordEvidence("fieldworks-parse-analysis/v1", index, word, 1,
                false, false, false, readings, []) };

        private static ParseAnalysis Reading(string form) => new([new ParseMorph(form, Msa, null, null)]);

        private static string DryRun(string digest) => JsonSerializer.Serialize(new
        {
            intentDigest = digest, baselineNote = "baseline", expectedEffects = Array.Empty<object>(),
            effectDigest = "sha256:" + new string('2', 64),
            anchor = new
            {
                intentDigest = digest, footprintDigest = "sha256:" + new string('3', 64),
                effectDigest = "sha256:" + new string('2', 64), runnerVersion = "1.0.0", libLcmVersion = "11.0.0",
                projectionVersion = "projection-v1", dryRunAtUtc = "2026-09-24T11:00:00Z",
            },
        }, MotifJson.CreateOptions());

        public void Sql(string sql)
        {
            using var connection = Database.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        public (int Cases, int Occurrences, int Buckets) Rows(string differenceId) =>
            (Count("ProposalDifferenceCases", differenceId), Count("ProposalDifferenceOccurrences", differenceId),
                Count("ProposalDifferenceBuckets", differenceId));

        public long DatabaseBytes()
        {
            using var connection = Database.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT page_count * page_size FROM pragma_page_count, pragma_page_size;";
            return Convert.ToInt64(command.ExecuteScalar());
        }

        public long FactsBytes(string differenceId)
        {
            using var connection = Database.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COALESCE(SUM(LENGTH(CAST(FactsJson AS BLOB))), 0) FROM ProposalDifferenceCases WHERE DifferenceId = $id;";
            command.Parameters.AddWithValue("$id", differenceId);
            return Convert.ToInt64(command.ExecuteScalar());
        }

        public int CountWord(string differenceId, string word, string table = "ProposalDifferenceCases")
        {
            using var connection = Database.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {table} WHERE DifferenceId = $id AND {(table == "ProposalDifferenceCases" ? "Word" : "CaseKey")} = $word;";
            command.Parameters.AddWithValue("$id", differenceId);
            command.Parameters.AddWithValue("$word", word);
            return Convert.ToInt32(command.ExecuteScalar());
        }

        public string SummaryJson(string differenceId)
        {
            using var connection = Database.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT SummaryJson FROM ProposalDifferences WHERE DifferenceId = $id;";
            command.Parameters.AddWithValue("$id", differenceId);
            return (string)command.ExecuteScalar()!;
        }

        public int Count(string table, string differenceId)
        {
            using var connection = Database.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {table} WHERE DifferenceId = $id;";
            command.Parameters.AddWithValue("$id", differenceId);
            return Convert.ToInt32(command.ExecuteScalar());
        }

        public void Dispose() => Database.Dispose();
    }
}
