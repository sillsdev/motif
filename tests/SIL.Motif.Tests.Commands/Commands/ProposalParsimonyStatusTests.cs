using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Parsimony;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class ProposalParsimonyStatusTests : IDisposable
{
    private static readonly JsonSerializerOptions Options = MotifJson.CreateOptions();
    private readonly string _root = Path.Combine(Path.GetTempPath(), nameof(ProposalParsimonyStatusTests),
        Guid.NewGuid().ToString("N"));
    private readonly string _fwData;
    private readonly string _parser;
    private readonly ProjectLocator _project;
    private readonly string _projectKey;
    private readonly string _proposalId = CanonicalId.Mint("proposal/").Value;
    private readonly BaselineToken _token = new("project", "sha256:" + new string('1', 64), "projection-v1",
        "2026-10-05T00:00:00Z", "sha256:" + new string('2', 64));

    public ProposalParsimonyStatusTests()
    {
        Directory.CreateDirectory(_root);
        _fwData = Path.Combine(_root, "project.fwdata");
        File.WriteAllText(_fwData, "project marker");
        _parser = Path.Combine(_root, "pangloss");
        File.WriteAllText(_parser, "parser one");
        _project = new ProjectLocator(Path.GetFullPath(_fwData), "project");
        _projectKey = ProjectWorkspaceKey.Compute(_project);
        using (OpenDatabase()) { }
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private MotifDatabase OpenDatabase() => MotifDatabase.OpenOwned(ProjectDatabaseCatalog.DatabasePathFor(_project),
        _project, MotifSchema.CurrentSchema, new Version(1, 0));

    private ProposalParsimonyStatus Read(string? parser, string? proposalId = null)
    {
        var read = ProposalParsimonyStatusCommand.Read(
            new ReadProposalParsimonyStatusRequest(_fwData, "1.0", proposalId ?? _proposalId), parser);
        Assert.True(read.Succeeded, read.Refusal?.Message);
        return read.Value!;
    }

    private string ProposalJson(string gloss = "a gloss")
    {
        var operationId = CanonicalId.Mint("operation/").Value;
        var target = CanonicalId.FromGuid(Guid.NewGuid()).Value;
        var quoted = JsonSerializer.Serialize(gloss);
        return $$$"""
            {"contractVersions": {"lexical": "1.0"}, "proposalId": "{{{_proposalId}}}",
             "operations": [{"operationId": "{{{operationId}}}", "kind": "lexical/lexSense/setGloss",
              "target": "{{{target}}}", "after": {"ws": "en", "text": {{{quoted}}} }}]}
            """;
    }

    private JobRecord CommitTrial(MotifDatabase database, string? proposalJson = null, BaselineToken? token = null)
    {
        var jobs = new JobRepository(database);
        var input = new TrialJobInput(proposalJson ?? ProposalJson(), null, ["word"], false, []);
        var trial = jobs.Create(CanonicalId.Mint("job/").Value, _projectKey, TrialJobHandler.TrialKind,
            JobParserPin.Stamp(JsonSerializer.Serialize(input, Options), _parser), "2026-11-01T00:00:00Z");
        Finish(jobs, trial, JobStatus.Completed, JsonSerializer.Serialize(new
        {
            baselineToken = token ?? _token,
            assessmentIds = Array.Empty<string>(),
            allWords = false,
        }, Options));
        return jobs.Get(trial.JobId)!;
    }

    private static void Finish(JobRepository jobs, JobRecord job, JobStatus status, string? result,
        JobFailureCategory category = JobFailureCategory.None)
    {
        var claimed = new JobClaims(jobs.Database, null, JobParserPin.KeyOf(job.InputJson)).Claim(job.ProjectKey, "status-test",
            "2026-11-01T00:00:01.0000000Z", TimeSpan.FromMinutes(5));
        Assert.Equal(job.JobId, claimed!.JobId);
        new ClaimedJob(jobs, claimed).Transition(status, category, result);
    }

    private JobRecord QueueParsimony(MotifDatabase database, JobRecord trial) =>
        new JobRepository(database).Get(ParsimonyAutoRun.Enqueue(database, trial, _token, retryFailed: false)!)!;

    private string Response(JobRecord candidate, ParsimonyMeasureStatus status = ParsimonyMeasureStatus.Computed)
    {
        var input = ParsimonyCandidateJobInput.Parse(candidate.InputJson);
        var digest = new string('4', 64);
        ParsimonyReportResponse Report(string kind, string measure) => new("report/" + kind + measure,
            new ParsimonyReportInputs("bundle/" + kind, _token, kind, kind == "candidate" ? "identity" : null, "model",
                new ParsimonyArtifactDigest(1, digest), new ParsimonyArtifactDigest(1, digest), null, null, []),
            [], "text")
        {
            AssessmentIds = [],
            Notes = [],
            MeasureRuns = [new ParsimonyMeasureRun(measure, status, 10, 1, "items", null, null, null)],
        };
        ParsimonyCandidateFindingChange Change(string id, string kind) => new(id, kind, null, null);
        var changes = new[] { Change("a", "added"), Change("b", "added"), Change("c", "removed"),
            Change("d", "unchanged"), Change("e", "evidence-changed") };
        var first = input.RunMeasureIds[0];
        var more = input.RunMeasureIds.Skip(1).Select(id => new ParsimonyCandidateMeasureResult(id,
            Report("baseline", id), Report("candidate", id), changes)).ToArray();
        var binding = new ParsimonyCandidateBinding("identity", _token, input.DryRunJobId, "proposal/x", "sha256:i",
            "sha256:c", [], "sha256:e", "sha256:a", "sha256:s", "sha256:t", "sha256:j");
        return JsonSerializer.Serialize(new ParsimonyCandidateEvidenceResponse(1, first, binding,
            Report("baseline", first), Report("candidate", first), changes,
            new ParsimonyApprovedAnalysisPreservation("preserved", 0, 0, []), more), Options);
    }

    [Fact]
    public void WithoutAParserParsimonyIsNotSetUpAndTheTextGroupSaysWhyItWillNotRun()
    {
        var status = Read(parser: null);

        Assert.Equal(ProposalParsimonyState.NotSetUp, status.State);
        Assert.Equal(ProposalParsimonyStatusCommand.ParserMissingCode, status.StateReason!.Code);
        Assert.True(status.Advisory);
        Assert.Equal([ParsimonyMeasureGroup.Grammar, ParsimonyMeasureGroup.Text], status.Groups.Select(g => g.Group));
        Assert.Equal(ProposalParsimonyGroupState.NotSetUp, status.Groups[1].State);
        Assert.Equal(ParsimonyCommands.TextGroupSkippedNote, status.Groups[1].Note);
    }

    [Fact]
    public void WithNoFinishedTrialItIsNotYetRunAndSaysNoTrialHasFinished()
    {
        var status = Read(_parser);

        Assert.Equal(ProposalParsimonyState.NotYetRun, status.State);
        Assert.Equal("parsimony.no-trial", status.StateReason!.Code);
        Assert.Null(status.TrialJobId);
    }

    [Fact]
    public void AFinishedTrialWithNoQueuedRunIsNotYetRunAndTheQueryQueuesNothing()
    {
        string trialId;
        using (var database = OpenDatabase()) trialId = CommitTrial(database).JobId;

        var status = Read(_parser);

        Assert.Equal(ProposalParsimonyState.NotYetRun, status.State);
        Assert.Equal("parsimony.not-queued", status.StateReason!.Code);
        Assert.Equal(trialId, status.TrialJobId);
        using var after = OpenDatabase();
        Assert.Empty(new JobRepository(after).ListByProjectAndKind(_projectKey,
            ParsimonyCandidateEvidenceBuilder.JobKind));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("parsimony-report", "parse")]
    [InlineData("trial", "trial")]
    public void AQueuedRunIsQueuedAndNamesWhatItWaitsBehind(string? otherKind, string? behind)
    {
        using (var database = OpenDatabase())
        {
            _ = QueueParsimony(database, CommitTrial(database));
            if (otherKind is not null)
                _ = new JobRepository(database).Create(CanonicalId.Mint("job/").Value, _projectKey, otherKind,
                    "{}", "2026-11-01T00:00:02.0000000Z");
        }

        var status = Read(_parser);

        Assert.Equal(ProposalParsimonyState.Queued, status.State);
        Assert.Equal("queued", status.Job!.Status);
        Assert.Equal(behind, status.Job.QueuedBehind);
    }

    [Fact]
    public void ARunningRunShowsItsStage()
    {
        using (var database = OpenDatabase())
        {
            var job = QueueParsimony(database, CommitTrial(database));
            var claimed = new JobClaims(database, null, JobParserPin.KeyOf(job.InputJson)).Claim(_projectKey, "runner", "2026-11-01T00:00:03.0000000Z",
                TimeSpan.FromMinutes(5))!;
            new ClaimedJob(new JobRepository(database), claimed).PublishStageProgress(
                new JobStageProgress("facts", 3, 5, "candidate"));
            Assert.Equal(job.JobId, claimed.JobId);
        }

        var status = Read(_parser);

        Assert.Equal(ProposalParsimonyState.Running, status.State);
        Assert.Equal("facts", status.Job!.Stage!.Stage);
        Assert.Equal(3, status.Job.Stage.Step);
    }

    [Fact]
    public void AFinishedRunWithTheSameInputsIsReadyWithPerMeasureCountsAndNoScore()
    {
        using (var database = OpenDatabase())
        {
            var job = QueueParsimony(database, CommitTrial(database));
            Finish(new JobRepository(database), job, JobStatus.Completed, Response(job));
        }

        var status = Read(_parser);

        Assert.Equal(ProposalParsimonyState.Ready, status.State);
        var grammar = status.Groups[0];
        Assert.Equal(ProposalParsimonyGroupState.Ready, grammar.State);
        Assert.Equal(2, grammar.Changes[0].NewFindings);
        Assert.Equal(1, grammar.Changes[0].ResolvedFindings);
        Assert.Equal(1, grammar.Changes[0].UnchangedFindings);
        Assert.Equal(1, grammar.Changes[0].EvidenceChangedFindings);
        Assert.True(grammar.Changes.Count > 1);
        Assert.Equal(grammar.Changes.Select(change => change.MeasureId).Distinct().Count(), grammar.Changes.Count);
        Assert.Equal("bundle/candidate", grammar.Candidate!.AfterBundleId);
        Assert.Null(status.Stale);
    }

    [Fact]
    public void AGroupWhoseMeasuresAllCouldNotJudgeAbstainsWhileTheRunIsStillReady()
    {
        using (var database = OpenDatabase())
        {
            var job = QueueParsimony(database, CommitTrial(database));
            Finish(new JobRepository(database), job, JobStatus.Completed,
                Response(job, ParsimonyMeasureStatus.NotAvailable));
        }

        var status = Read(_parser);

        Assert.Equal(ProposalParsimonyState.Ready, status.State);
        Assert.Equal(ProposalParsimonyGroupState.Abstained, status.Groups[0].State);
    }

    [Fact]
    public void AReplacedParserMakesAFinishedRunStaleForThatReasonAlone()
    {
        using (var database = OpenDatabase())
        {
            var job = QueueParsimony(database, CommitTrial(database));
            Finish(new JobRepository(database), job, JobStatus.Completed, Response(job));
        }
        var replacement = Path.Combine(_root, "pangloss-two");
        File.WriteAllText(replacement, "parser two");

        var status = Read(replacement);

        Assert.Equal(ProposalParsimonyState.Stale, status.State);
        Assert.Equal([ProposalParsimonyStaleReason.ParserChanged], status.Stale!.Reasons);
        Assert.NotEqual(status.Stale.ReportKey, status.Stale.CurrentKey);
    }

    [Fact]
    public void ANewerTrialOfAnEditedProposalMakesTheOlderRunStaleForTheProposal()
    {
        using (var database = OpenDatabase())
        {
            var job = QueueParsimony(database, CommitTrial(database));
            Finish(new JobRepository(database), job, JobStatus.Completed, Response(job));
            _ = CommitTrial(database, ProposalJson("an edited gloss"));
        }

        var status = Read(_parser);

        Assert.Equal(ProposalParsimonyState.Stale, status.State);
        Assert.Equal([ProposalParsimonyStaleReason.ProposalChanged], status.Stale!.Reasons);
    }

    [Theory]
    [InlineData(JobStatus.Failed, JobFailureCategory.Infrastructure, "PanGloss exceeded the wall-clock time limit.",
        ProposalParsimonyFailureCategory.TimedOut, false)]
    [InlineData(JobStatus.Failed, JobFailureCategory.Infrastructure, "PanGloss changed during the comparison.",
        ProposalParsimonyFailureCategory.ParserUnavailable, true)]
    [InlineData(JobStatus.Failed, JobFailureCategory.ParserRefusal, "The facts were refused.",
        ProposalParsimonyFailureCategory.FactsFailed, true)]
    [InlineData(JobStatus.Failed, JobFailureCategory.Semantic, "The Dry Run did not reproduce.",
        ProposalParsimonyFailureCategory.EvidenceFailed, true)]
    [InlineData(JobStatus.Cancelled, JobFailureCategory.Cancellation, null, ProposalParsimonyFailureCategory.Cancelled,
        true)]
    [InlineData(JobStatus.Failed, JobFailureCategory.Unknown, "boom", ProposalParsimonyFailureCategory.Internal, true)]
    public void AFailedRunReadsAsOneClosedCategoryAndLeavesTheTrialOutOfIt(JobStatus ending,
        JobFailureCategory category, string? detail, ProposalParsimonyFailureCategory expected, bool retryable)
    {
        JobRecord trial;
        using (var database = OpenDatabase())
        {
            trial = CommitTrial(database);
            var job = QueueParsimony(database, trial);
            Finish(new JobRepository(database), job, ending, detail is null ? null : JsonSerializer.Serialize(new { detail }),
                category);
        }

        var status = Read(_parser);

        Assert.Equal(ProposalParsimonyState.Failed, status.State);
        Assert.Equal(expected, status.Failure!.Category);
        Assert.Equal(retryable, status.Failure.Retryable);
        using var after = OpenDatabase();
        Assert.Equal(trial, new JobRepository(after).Get(trial.JobId));
    }

    [Fact]
    public void ASavedParsimonySelectionMovesTheTextGroupFromNotSetUpToNotYetRun()
    {
        using (var database = OpenDatabase())
            new NamedSelectionRepository(database).SetParsimony("evidence", [], ["kitabu"],
                new SelectionParsingLimits(StepCap.Default, SelectionTimeLimitMode.Estimated, null), null);

        var status = Read(_parser);

        Assert.Equal(ProposalParsimonyGroupState.NotYetRun, status.Groups[1].State);
    }

    [Fact]
    public void ABadProposalIdIsRefused()
    {
        var read = ProposalParsimonyStatusCommand.Read(new ReadProposalParsimonyStatusRequest(_fwData, "1.0", "nope"),
            _parser);

        Assert.False(read.Succeeded);
    }

    [Fact]
    public void EachInputFlipsExactlyItsOwnStaleReason()
    {
        var proposal = ProposalJson();
        var baseline = ParsimonyAutoInputs.For(TrialInput(proposal), _token);
        var other = new BaselineToken("project", "sha256:" + new string('9', 64), "projection-v1",
            "2026-10-05T00:00:00Z", "sha256:" + new string('8', 64));
        (string Name, ParsimonyAutoInputs Changed, ProposalParsimonyStaleReason Reason)[] table =
        [
            ("baseline", ParsimonyAutoInputs.For(TrialInput(proposal), other), ProposalParsimonyStaleReason.BaselineChanged),
            ("proposal", baseline with { Proposal = "other" }, ProposalParsimonyStaleReason.ProposalChanged),
            ("prerequisites", baseline with { Prerequisites = "other" }, ProposalParsimonyStaleReason.PrerequisiteChanged),
            ("parser", baseline with { Parser = "other" }, ProposalParsimonyStaleReason.ParserChanged),
            ("schema", baseline with { Schema = "other" }, ProposalParsimonyStaleReason.SchemaChanged),
            ("measures", baseline with { Measures = "other" }, ProposalParsimonyStaleReason.MeasuresChanged),
        ];

        foreach (var (name, changed, reason) in table)
            Assert.True(new[] { reason }.SequenceEqual(ProposalParsimonyStatusCommand.Differences(baseline, changed)),
                name);
        Assert.Empty(ProposalParsimonyStatusCommand.Differences(baseline, baseline));
    }

    private string TrialInput(string proposalJson) => JsonSerializer.Serialize(
        new TrialJobInput(proposalJson, null, ["word"], false, []), Options);
}
