using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Baselines;
using SIL.Motif.Host.Config;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Preferences;
using SIL.Motif.Host.Store;
using SIL.Motif.LiveHost.Baselines;
using SIL.Motif.Runner.AppliedLog;
using SIL.Motif.Runner.DryRun;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Scheduling;
using SIL.Motif.Worker.Store;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.Worker;

/// <summary>
/// Proves a Trial's job-handler contract: it opens the published Baseline exactly once, does not move the
/// Proposal it measures, and a Draft with no committed revision still produces citable Assessments.
/// </summary>
[Collection(LcmCacheTestCollection.Name)]
public sealed class TrialJobHandlerTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "SIL.Motif.TrialJobHandlerTests", Guid.NewGuid().ToString("N"));
    private readonly string _publishedRoot;
    private readonly CountingFwDataProjectLoader _loader = new();
    private readonly ProjectLocator _project;
    private readonly MotifDatabase _database;
    private readonly JobRepository _jobs;
    private readonly BaselineRepository _baselines;
    private readonly ProposalRepository _proposals;
    private readonly AssessmentRepository _assessments;
    private readonly List<string> _trialScratchPaths = [];
    private readonly BaselineToken _token;
    private readonly SeededProject _seed;
    private readonly Guid _opinionAnalysisId;
    private readonly Guid _phonologicalDataId;
    private readonly ITestOutputHelper _output;

    public TrialJobHandlerTests(PristineProjectFixture pristine, ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(_root);

        _seed = pristine.Seed;
        var template = PublishedBaselineTemplate<TrialTemplateIds>.Get("trial-wordforms", pristine, master =>
            new TrialTemplateIds(SeedWordformsForSelectionTests(master), master.LangProject.PhonologicalDataOA.Guid));
        _opinionAnalysisId = template.Metadata.OpinionAnalysisId;
        _phonologicalDataId = template.Metadata.PhonologicalDataId;

        _publishedRoot = Path.Combine(_root, "published");
        var fwDataPath = template.CopyTo(_publishedRoot);
        _project = new ProjectLocator(Path.Combine(_root, "live", NewLangProjFixture.ProjectName + ".fwdata"),
            "live-project-identity");

        _database = MotifDatabase.OpenOwned(Path.Combine(_root, "motif.db"), _project,
            MotifSchema.CurrentSchema, new Version(1, 0));
        _jobs = new JobRepository(_database);
        _baselines = new BaselineRepository(_database);
        _proposals = new ProposalRepository(_database);
        _assessments = new AssessmentRepository(_database);

        _token = new BaselineToken("live-project-identity", "sha256:" + new string('a', 64), "1",
            "2026-08-29T00:00:00Z", "sha256:" + new string('b', 64));
        _baselines.Record(ProjectWorkspaceKey.Compute(_project),
            new BaselinePublication(_publishedRoot, fwDataPath, _token),
            DateTimeOffset.Parse("2026-08-29T00:00:00Z"), DateTimeOffset.Parse("2026-08-29T00:00:00Z"),
            template.Projection);
    }

    public void Dispose()
    {
        foreach (var path in _trialScratchPaths)
        {
            try { Directory.Delete(path, recursive: true); } catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        _database.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public void ATrialJobOpensThePublishedBaselineExactlyOnce()
    {
        using var lanes = new ProjectLaneRegistry(_ => _token);
        var handler = BuildHandler(lanes, new FakeAssessor("pangloss", [AssessmentKind.Correctness]));
        var proposalId = CanonicalId.Mint("proposal/");
        var proposalJson = BuildSetGlossProposalJson(proposalId, _seed.FirstSenseId, "trial text");
        SaveCommittedProposal(proposalId, proposalJson);

        var job = CreateTrialJob(proposalJson);
        var completed = RunAndFinish(handler, job.JobId);

        Assert.Equal(JobStatus.Completed, completed.Status);
        Assert.True(completed.DryRunPublished);
        Assert.Equal(1, _loader.LoadScratchCacheCount);
    }

    [Fact]
    public void TrialRetainsItsStagedSourceWithoutPublishingASecondCopy()
    {
        using var lanes = new ProjectLaneRegistry(_ => _token);
        var assessor = new TrialArtifactAssessor(Path.Combine(_root, "assessment-runs"));
        var handler = BuildHandler(lanes, assessor);
        var proposalId = CanonicalId.Mint("proposal/");
        var proposalJson = BuildSetGlossProposalJson(proposalId, _seed.FirstSenseId, "retained source");
        SaveCommittedProposal(proposalId, proposalJson);
        var job = CreateTrialJob(proposalJson);

        var completed = RunAndFinish(handler, job.JobId);

        Assert.Equal(JobStatus.Completed, completed.Status);
        var header = Assert.Single(_assessments.ListByProposal(proposalId));
        var assessment = _assessments.Get(header.AssessmentId);
        var invocation = Assert.IsType<BatchInvocationEvidence>(assessment.Invocation);
        var sourcePath = Path.GetFullPath(invocation.SourcePath);
        var sharedSources = Path.Combine(_root, "assessment-sources");
        var sourceCopies = Directory.GetFiles(assessor.RunRoot, "source.fwdata", SearchOption.AllDirectories)
            .Concat(Directory.Exists(sharedSources)
                ? Directory.GetFiles(sharedSources, "*.fwdata", SearchOption.TopDirectoryOnly)
                : [])
            .Where(path => BatchInvocationEvidence.DigestFile(path) == invocation.SourceBytesSha256)
            .ToArray();
        var sourceBytes = new FileInfo(sourcePath).Length;
        _output.WriteLine($"Trial source fixture copies={sourceCopies.Length}; bytes={sourceCopies.Sum(path => new FileInfo(path).Length)}; single-copy-bytes={sourceBytes}.");

        var relativeSourcePath = Path.GetRelativePath(Path.GetFullPath(assessor.RunRoot), sourcePath);
        Assert.False(Path.IsPathRooted(relativeSourcePath));
        Assert.DoesNotContain("..", relativeSourcePath.Split(Path.DirectorySeparatorChar));
        Assert.Equal(invocation.InvocationId, Path.GetFileName(Path.GetDirectoryName(sourcePath)));
        Assert.Equal(assessor.SourceBytes, File.ReadAllBytes(sourcePath));
        Assert.Equal(invocation.SourceBytesSha256, BatchInvocationEvidence.DigestFile(sourcePath));
        Assert.True(Directory.Exists(assessor.InvocationDirectory));
        Assert.False(Directory.Exists(sharedSources));
        Assert.Single(sourceCopies);
        Assert.Equal(sourceBytes, sourceCopies.Sum(path => new FileInfo(path).Length));

        using var replay = StatsEvidenceReplay.Create(invocation, assessment.CachePath!, assessment.CacheDigest!);
        Assert.Equal(invocation.SourceBytesSha256, BatchInvocationEvidence.DigestFile(replay.GrammarPath));
    }

    [Fact]
    public void HistoricalSharedAssessmentSourceRemainsReadableForStatsReplay()
    {
        var stagingDirectory = Path.Combine(_root, "legacy-staging");
        Directory.CreateDirectory(stagingDirectory);
        var stagedSource = Path.Combine(stagingDirectory, "source.fwdata");
        var sourceBytes = System.Text.Encoding.UTF8.GetBytes("historical shared source fixture");
        File.WriteAllBytes(stagedSource, sourceBytes);
        var sourceDigest = BatchInvocationEvidence.DigestFile(stagedSource);
        var sharedSource = AssessmentSourceArtifactStore.Persist(_root, stagedSource, sourceDigest);
        AssessmentSourceArtifactStore.Verify(sharedSource, sourceDigest);
        var cachePath = Path.Combine(_root, "legacy-stats.sqlite");
        File.WriteAllBytes(cachePath, [1, 2, 3, 4]);
        var cacheDigest = BatchInvocationEvidence.DigestFile(cachePath);
        var invocation = new BatchInvocationEvidence("historical-invocation", sharedSource, sourceDigest,
            "sha256:" + new string('1', 64), "words.txt", "sha256:" + new string('2', 64),
            "out.tsv", "sha256:" + new string('3', 64), "stderr.txt", "sha256:" + new string('4', 64),
            null, StepCap.Default, 1, true);
        var assessmentId = CanonicalId.Mint("assessment/").Value;
        var selection = Selection.Create("historical", []);
        _assessments.Record(new NewAssessmentRecord(assessmentId, null, null, "pangloss",
            AssessmentKind.ObjectTiming.ToStoredKind(), "{}", "sha256:" + new string('5', 64),
            new AssessmentSubject.Words(selection, "none", "1"), "{}", null, null, sourceDigest,
            null, null, null,
            new AssessmentRecordMaterial.Words([], cachePath, cacheDigest, [], invocation)));

        var stored = _assessments.Get(assessmentId);
        Assert.Equal(sharedSource, stored.Invocation!.SourcePath);
        using var replay = StatsEvidenceReplay.Create(stored.Invocation, stored.CachePath!, stored.CacheDigest!);
        Assert.Equal(sourceBytes, File.ReadAllBytes(replay.GrammarPath));
        Assert.Equal(sourceDigest, BatchInvocationEvidence.DigestFile(replay.GrammarPath));
    }

    [Fact]
    public async Task TrialDisposesItsStagedSourceWhenCancellationPreventsAssessmentCommit()
    {
        using var lanes = new ProjectLaneRegistry(_ => _token);
        using var cancellation = new CancellationTokenSource();
        var assessor = new TrialArtifactAssessor(Path.Combine(_root, "assessment-runs"), cancellation.Cancel);
        var handler = BuildHandler(lanes, assessor);
        var proposalId = CanonicalId.Mint("proposal/");
        var proposalJson = BuildSetGlossProposalJson(proposalId, _seed.FirstSenseId, "cancel retained source");
        SaveCommittedProposal(proposalId, proposalJson);
        var job = CreateTrialJob(proposalJson);
        var claim = ClaimedJob.Of(_jobs, job.JobId);

        var outcome = await handler.RunAsync(claim, _project, cancellation.Token);

        Assert.Equal(JobStatus.Cancelled, outcome!.Status);
        claim.Transition(outcome.Status, outcome.Category, outcome.ResultJson);
        Assert.NotNull(assessor.InvocationDirectory);
        Assert.False(Directory.Exists(assessor.InvocationDirectory));
        Assert.Empty(_assessments.ListByProposal(proposalId));
        Assert.False(Directory.Exists(Path.Combine(_root, "assessment-sources")));
    }

    [Fact]
    public void AReversedTrialOperationArrayPreparesItsScratchInDeclaredRuleOrder()
    {
        using var lanes = new ProjectLaneRegistry(_ => _token);
        var proposalId = CanonicalId.Mint("proposal/");
        var proposal = BuildOrderedRuleCreateProposalJson(proposalId, _phonologicalDataId);
        var authoredRuleOrder = ProposalJsonParser.Parse(proposal.Json).Operations
            .Select(operation => operation.EntityId!.Value).ToArray();
        Assert.Equal(proposal.RuleIds.Reverse().ToArray(), authoredRuleOrder);
        SaveCommittedProposal(proposalId, proposal.Json);
        var job = CreateTrialJob(proposal.Json);
        IReadOnlyList<Guid>? preparedRuleOrder = null;
        var handler = BuildHandler(lanes, new FakeAssessor("pangloss", [AssessmentKind.Correctness]),
            prepareForAssessment: (cache, ct) =>
            {
                preparedRuleOrder = cache!.LangProject.PhonologicalDataOA.PhonRulesOS
                    .Select(rule => rule.Guid).ToArray();
                return DefaultPrepareForAssessment(cache, ct);
            });

        var completed = RunAndFinish(handler, job.JobId);

        Assert.Equal(JobStatus.Completed, completed.Status);
        Assert.NotNull(preparedRuleOrder);
        Assert.Equal(proposal.RuleIds.Select(id => id.ToGuid()), preparedRuleOrder);
    }

    [Fact]
    public void ATrialStoresAGrammarCheckOfTheScratchUnderItsProposalId()
    {
        using var lanes = new ProjectLaneRegistry(_ => _token);
        string? checkedPath = null;
        var handler = BuildHandler(lanes, new FakeAssessor("pangloss", [AssessmentKind.Correctness]),
            grammarHealth: (path, _, _, _) =>
            {
                checkedPath = path;
                return Task.FromResult<GrammarCheckResponse?>(new GrammarCheckResponse([], HasBaseline: true));
            });
        var proposalId = CanonicalId.Mint("proposal/");
        var proposalJson = BuildSetGlossProposalJson(proposalId, _seed.FirstSenseId, "grammar checked");
        SaveCommittedProposal(proposalId, proposalJson);
        var job = CreateTrialJob(proposalJson);

        var completed = RunAndFinish(handler, job.JobId);

        Assert.Equal(JobStatus.Completed, completed.Status);
        Assert.NotNull(checkedPath);
        Assert.True(File.Exists(checkedPath));
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ProposalId, IntentDigest, SelectionSha256, ResponseJson FROM GrammarChecks WHERE BaselineToken = $token;";
        command.Parameters.AddWithValue("$token", JsonSerializer.Serialize(_token,
            SIL.Motif.Contract.MotifJson.CreateOptions()));
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(proposalId.Value, reader.GetString(0));
        Assert.Equal(IntentDigest.Compute(ProposalJsonParser.Parse(proposalJson)), reader.GetString(1));
        Assert.False(string.IsNullOrWhiteSpace(reader.GetString(2)));
        using var response = JsonDocument.Parse(reader.GetString(3));
        Assert.True(response.RootElement.GetProperty("HasBaseline").GetBoolean());
        var provenance = response.RootElement.GetProperty("Provenance");
        Assert.Equal(DateTimeOffset.Parse("2026-08-29T00:00:00Z"),
            provenance.GetProperty("SourceLastWriteUtc").GetDateTimeOffset());
        Assert.Equal(proposalId.Value, provenance.GetProperty("Trial").GetProperty("ProposalId").GetString());
    }

    [Fact]
    public async Task AProposalTrialUsesTheSavedDefaultProposalChecksChoice()
    {
        var parserPath = FakeParser.CopyRecordingInvocations(Path.Combine(_root, "recording-parser"));
        var defaultChecks = new FileDefaultProposalChecksStore(
            Path.Combine(_root, "preferences", "default-proposal-checks.json"));
        defaultChecks.SetStoredAnalyses(StoredAnalysisComparison.Off);
        using var invoker = new PanGlossInvoker(parserPath);
        using var lanes = new ProjectLaneRegistry(_ => _token);
        var workspaceKey = ProjectWorkspaceKey.Compute(_project);
        var loop = ProjectJobHandlers.CreateLoop(_database, _baselines, workspaceKey, _project,
            new RunnerOptions { Root = Path.Combine(_root, "worker"), ParserPath = parserPath },
            invoker, "trial-default-checks", lanes, defaultChecks);
        var proposalId = CanonicalId.Mint("proposal/");
        var proposalJson = BuildSetGlossProposalJson(proposalId, _seed.FirstSenseId, "saved checks");
        SaveCommittedProposal(proposalId, proposalJson);
        var job = CreateTrialJob(proposalJson, words: [], startRunning: false);

        await loop.RunUntilIdleAsync(CancellationToken.None);

        Assert.Equal(JobStatus.Completed, _jobs.Get(job.JobId)!.Status);
        Assert.Equal(["off"], FakeParser.StoredAnalysesRequested(parserPath));
        Assert.Contains("grammar-health", FakeParser.Invocations(parserPath));
    }

    [Fact]
    public void GrammarCheckFailureIsRecordedAsATrialWarning()
    {
        using var lanes = new ProjectLaneRegistry(_ => _token);
        var handler = BuildHandler(lanes, new FakeAssessor("pangloss", [AssessmentKind.Correctness]),
            grammarHealth: (_, _, _, _) => throw new IOException("grammar-health unavailable"));
        var proposalId = CanonicalId.Mint("proposal/");
        var proposalJson = BuildSetGlossProposalJson(proposalId, _seed.FirstSenseId, "grammar warning");
        SaveCommittedProposal(proposalId, proposalJson);
        var job = CreateTrialJob(proposalJson);

        var completed = RunAndFinish(handler, job.JobId);

        Assert.Equal(JobStatus.Completed, completed.Status);
        Assert.Contains("grammar-health unavailable", completed.ResultJson, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ATrialCanMeasureOnlyTheWordsTouchedByAChange(bool externalList)
    {
        using var lanes = new ProjectLaneRegistry(_ => _token);
        var handler = BuildHandler(lanes, new FakeAssessor("pangloss", [AssessmentKind.Correctness]));
        var proposalId = CanonicalId.Mint("proposal/");
        var proposalJson = BuildSetGlossProposalJson(proposalId, _seed.FirstSenseId, "one word");
        SaveCommittedProposal(proposalId, proposalJson);

        var list = externalList ? JobWordLists.Store(_database, [UnanalysedWordform]) : null;
        var job = CreateTrialJob(proposalJson, externalList ? null : [UnanalysedWordform], wordList: list);
        var completed = RunAndFinish(handler, job.JobId);

        Assert.Equal(JobStatus.Completed, completed.Status);
        var assessmentId = JsonDocument.Parse(completed.ResultJson!).RootElement
            .GetProperty("assessmentIds")[0].GetString()!;
        Assert.Equal([UnanalysedWordform], _assessments.Get(assessmentId).Selection.Words);
    }

    [Fact]
    public void ATrialRecordsProjectStandingAfterTheProposalOpinionIsApplied()
    {
        using var lanes = new ProjectLaneRegistry(_ => _token);
        var proposalId = CanonicalId.Mint("proposal/");
        var proposalJson = BuildApproveAnalysisProposalJson(proposalId, _opinionAnalysisId);
        SaveCommittedProposal(proposalId, proposalJson);
        var assessor = new FakeAssessor("pangloss", [AssessmentKind.Correctness],
            rawFor: _ => new AssessmentRaw.WordMeasurements(
                [new AssessedWord(OpinionWordform, "analysed", [])]));
        var handler = BuildHandler(lanes, assessor);
        var job = CreateTrialJob(proposalJson, [OpinionWordform]);

        var completed = RunAndFinish(handler, job.JobId);

        Assert.Equal(JobStatus.Completed, completed.Status);
        var assessmentId = JsonDocument.Parse(completed.ResultJson!).RootElement
            .GetProperty("assessmentIds")[0].GetString()!;
        Assert.Equal(ProjectStanding.Approved, Assert.Single(_assessments.Get(assessmentId).Words!).ProjectStanding);
    }

    [Fact]
    public void ATrialPublishesItsCurrentWordWhileTheAssessorRuns()
    {
        using var lanes = new ProjectLaneRegistry(_ => _token);
        var proposalId = CanonicalId.Mint("proposal/");
        var proposalJson = BuildSetGlossProposalJson(proposalId, _seed.FirstSenseId, "progress text");
        SaveCommittedProposal(proposalId, proposalJson);
        var job = CreateTrialJob(proposalJson, [UnanalysedWordform]);
        TrialWordProgress? whileRunning = null;
        var assessor = new FakeAssessor("pangloss", [AssessmentKind.Correctness])
        {
            EmitProgress = publish =>
            {
                publish(new TrialWordProgress(0, 1, UnanalysedWordform));
                whileRunning = JsonSerializer.Deserialize<TrialWordProgress>(_jobs.Get(job.JobId)!.ProgressJson!);
                publish(new TrialWordProgress(1, 1, null));
            },
        };
        var handler = BuildHandler(lanes, assessor);

        var completed = RunAndFinish(handler, job.JobId);

        Assert.Equal(UnanalysedWordform, whileRunning?.CurrentWord);
        Assert.Equal(0, whileRunning?.Completed);
        Assert.Equal(1, JsonSerializer.Deserialize<TrialWordProgress>(completed.ProgressJson!)?.Completed);
    }

    [Fact]
    public async Task TrialWaitingForParserCapacityPublishesStageBeforeAnyWordStarts()
    {
        var parserPath = FakeParser.CopyRecordingInvocations(Path.Combine(_root, "trial-parser-wait"));
        var slotNames = UniqueParserSlotNames("trial-wait");
        using var firstSlot = new WorkerMutexOwner(slotNames[0], machineWide: true);
        using var secondSlot = new WorkerMutexOwner(slotNames[1], machineWide: true);
        Assert.True(firstSlot.TryAcquire());
        Assert.True(secondSlot.TryAcquire());

        using var cancellation = new CancellationTokenSource();
        using var queue = new MachinePanGlossQueue(slotNames);
        var waitStarted = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        queue.SlotWaitStarted = label =>
        {
            if (label == "trial-grammar-slot-wait") waitStarted.TrySetResult(label);
        };
        using var invoker = new PanGlossInvoker(parserPath, queue);
        using var lanes = new ProjectLaneRegistry(_ => _token);
        var grammarHealthStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var wordsStarted = 0;
        var assessor = new FakeAssessor("pangloss", [AssessmentKind.Correctness])
        {
            EmitProgress = _ => Interlocked.Exchange(ref wordsStarted, 1),
        };
        var handler = BuildHandler(lanes, assessor, grammarHealth: async
            (grammarPath, projectName, onAdmissionWait, token) =>
        {
            grammarHealthStarted.TrySetResult();
            try
            {
                return await TrialGrammarCheck.RunAsync(invoker, "trial-grammar-slot-wait",
                    StoredAnalysisComparison.Budget, grammarPath, projectName, token, onAdmissionWait);
            }
            catch (Exception) when (token.IsCancellationRequested)
            {
                throw new OperationCanceledException(token);
            }
        });
        var proposalId = CanonicalId.Mint("proposal/");
        var proposalJson = BuildSetGlossProposalJson(proposalId, _seed.FirstSenseId, "trial waiting");
        SaveCommittedProposal(proposalId, proposalJson);
        var job = CreateTrialJob(proposalJson, [UnanalysedWordform], startRunning: false);
        var claimed = new JobClaims(_database).Claim(ProjectWorkspaceKey.Compute(_project), "trial-wait-worker",
            JobTimestamp.FormatUtc(DateTimeOffset.UtcNow), TimeSpan.FromMinutes(5));
        Assert.NotNull(claimed);
        Assert.Equal(job.JobId, claimed!.JobId);
        Assert.False(string.IsNullOrWhiteSpace(claimed.ClaimToken));
        var claim = new ClaimedJob(_jobs, claimed);

        Task<JobOutcome?>? pending = null;
        try
        {
            pending = handler.RunAsync(claim, _project, cancellation.Token);
            await grammarHealthStarted.Task.WaitAsync(TimeSpan.FromSeconds(60));
            Assert.Equal("trial-grammar-slot-wait",
                await waitStarted.Task.WaitAsync(TimeSpan.FromSeconds(60)));

            var waiting = await WaitForStageProgressAsync(job.JobId, "waiting-for-parser-slot");

            Assert.True(waiting is not null,
                "SlotWaitStarted confirmed a failed parser-slot acquisition, but the Trial row did not publish a durable waiting stage.");
            Assert.Equal("waiting-for-parser-slot", waiting!.Stage);
            Assert.Contains("grammar-health", waiting.Detail, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, Volatile.Read(ref wordsStarted));
            Assert.Empty(FakeParser.Invocations(parserPath));
        }
        finally
        {
            cancellation.Cancel();
            firstSlot.Release();
            secondSlot.Release();
            if (pending is not null)
            {
                try { await pending.WaitAsync(TimeSpan.FromSeconds(60)); }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            }
        }
    }

    [Fact]
    public async Task ATrialDoesNotInvokeItsAssessorWhenAnotherOwnerHoldsTheProjectLease()
    {
        using var lanes = new ProjectLaneRegistry(_ => _token);
        var proposalId = CanonicalId.Mint("proposal/");
        var proposalJson = BuildSetGlossProposalJson(proposalId, _seed.FirstSenseId, "lease text");
        SaveCommittedProposal(proposalId, proposalJson);
        var job = CreateTrialJob(proposalJson, [UnanalysedWordform]);
        var assessorCalled = false;
        var assessor = new FakeAssessor("pangloss", [AssessmentKind.Correctness])
        {
            EmitProgress = _ => assessorCalled = true,
        };
        var handler = BuildHandler(lanes, assessor);
        using var lease = ProjectParseLease.TryAcquire(_database);
        Assert.NotNull(lease);
        var claim = ClaimedJob.Of(_jobs, job.JobId);

        var outcome = await handler.RunAsync(claim, _project, CancellationToken.None);

        Assert.Equal(JobStatus.Failed, outcome!.Status);
        Assert.Contains("already running", outcome.ResultJson, StringComparison.OrdinalIgnoreCase);
        Assert.False(assessorCalled);
        claim.Transition(outcome.Status, outcome.Category, outcome.ResultJson);
        Assert.Equal(JobStatus.Failed, _jobs.Get(job.JobId)!.Status);
    }

    [Fact]
    public void TrialDoesNotChangeTheProposalsStatus()
    {
        using var lanes = new ProjectLaneRegistry(_ => _token);
        var handler = BuildHandler(lanes, new FakeAssessor("pangloss", [AssessmentKind.Correctness]));
        var proposalId = CanonicalId.Mint("proposal/");
        var proposalJson = BuildSetGlossProposalJson(proposalId, _seed.FirstSenseId, "status text");
        SaveCommittedProposal(proposalId, proposalJson);

        var statusBefore = _proposals.Get(proposalId).Status;

        var job = CreateTrialJob(proposalJson);
        var completed = RunAndFinish(handler, job.JobId);

        Assert.Equal(JobStatus.Completed, completed.Status);
        var statusAfter = _proposals.Get(proposalId).Status;
        Assert.Equal(statusBefore, statusAfter);
        Assert.Equal("proposed", statusAfter);
    }

    [Fact]
    public void ATrialOfAnUncommittedDraftSucceedsAndCitesTheDraftsIntentDigestWithNoRevision()
    {
        using var lanes = new ProjectLaneRegistry(_ => _token);
        var handler = BuildHandler(lanes, new FakeAssessor("pangloss", [AssessmentKind.Correctness]));
        var proposalId = CanonicalId.Mint("proposal/");
        var draftJson = BuildSetGlossProposalJson(proposalId, _seed.FirstSenseId, "draft text");
        _proposals.CreateDraft("working", proposalId, draftJson);
        var expectedDigest = IntentDigest.Compute(ProposalJsonParser.Parse(draftJson));

        var job = CreateTrialJob(draftJson);
        var completed = RunAndFinish(handler, job.JobId);

        Assert.Equal(JobStatus.Completed, completed.Status);

        var draftAfter = _proposals.Get(proposalId);
        Assert.Equal("draft", draftAfter.Status);
        Assert.Null(draftAfter.IntentDigest);

        var recorded = _assessments.ListByProposal(proposalId);
        Assert.NotEmpty(recorded);
        foreach (var assessment in recorded)
        {
            Assert.Equal(expectedDigest, assessment.ProposalIntentDigest);
            Assert.Equal(proposalId, assessment.ProposalId);
        }
    }

    [Fact]
    public void ASecondTrialOfTheSameProposalProducesASecondAssessmentSetWithoutDisturbingTheFirst()
    {
        using var lanes = new ProjectLaneRegistry(_ => _token);
        var handler = BuildHandler(lanes, new FakeAssessor("pangloss", [AssessmentKind.Correctness]));
        var proposalId = CanonicalId.Mint("proposal/");
        var proposalJson = BuildSetGlossProposalJson(proposalId, _seed.FirstSenseId, "repeat text");
        SaveCommittedProposal(proposalId, proposalJson);

        var firstJob = CreateTrialJob(proposalJson);
        RunAndFinish(handler, firstJob.JobId);
        var afterFirst = _assessments.ListByProposal(proposalId);
        Assert.Single(afterFirst);
        var firstAssessmentId = afterFirst[0].AssessmentId;

        var secondJob = CreateTrialJob(proposalJson, createdUtc: "2026-08-29T00:00:01Z");
        RunAndFinish(handler, secondJob.JobId);
        var afterSecond = _assessments.ListByProposal(proposalId);

        Assert.Equal(2, afterSecond.Count);
        Assert.Contains(afterSecond, a => a.AssessmentId == firstAssessmentId);
        Assert.Equal(2, afterSecond.Select(a => a.AssessmentId).Distinct().Count());
        Assert.False(Directory.Exists(TrialScratchPath.ForJob(firstJob.JobId)));
        Assert.True(Directory.Exists(TrialScratchPath.ForJob(secondJob.JobId)));

        // The first is still readable, byte-for-byte the same header it was recorded with.
        var firstStillThere = _assessments.Get(firstAssessmentId);
        Assert.Equal(proposalId, firstStillThere.ProposalId);
    }

    [Fact]
    public void StartupSweepKeepsTheLatestTrialScratchRemovesOlderCopiesAndLeavesUnattributedFolders()
    {
        using var lanes = new ProjectLaneRegistry(_ => _token);
        var handler = BuildHandler(lanes, new FakeAssessor("pangloss", [AssessmentKind.Correctness]));
        var proposalId = CanonicalId.Mint("proposal/");
        var proposalJson = BuildSetGlossProposalJson(proposalId, _seed.FirstSenseId, "scratch retention");
        SaveCommittedProposal(proposalId, proposalJson);
        var firstJob = CreateTrialJob(proposalJson, createdUtc: "2026-08-29T11:00:00Z");
        RunAndFinish(handler, firstJob.JobId);
        var latestJob = CreateTrialJob(proposalJson, createdUtc: "2026-08-29T12:00:00Z");
        RunAndFinish(handler, latestJob.JobId);

        var scratchRoot = Path.Combine(_root, "startup-trial-scratch");
        var olderPath = Path.Combine(scratchRoot, Path.GetFileName(TrialScratchPath.ForJob(firstJob.JobId))!);
        var latestPath = Path.Combine(scratchRoot, Path.GetFileName(TrialScratchPath.ForJob(latestJob.JobId))!);
        var orphanPath = Path.Combine(scratchRoot, Path.GetFileName(TrialScratchPath.ForJob("orphan-job"))!);
        Directory.CreateDirectory(olderPath);
        Directory.CreateDirectory(latestPath);
        Directory.CreateDirectory(orphanPath);

        TrialScratchRetention.Sweep([(_database, ProjectWorkspaceKey.Compute(_project))], scratchRoot);

        Assert.False(Directory.Exists(olderPath));
        Assert.True(Directory.Exists(latestPath));
        Assert.True(Directory.Exists(orphanPath));
    }

    [Fact]
    public void TrialScratchRootIsThisProcessesPrivateTestRoot()
    {
        Assert.StartsWith(ProcessRunnerEnvironmentInitializer.Root, TrialScratchPath.Root, StringComparison.Ordinal);
        Assert.StartsWith(TrialScratchPath.Root, TrialScratchPath.ForJob("any-job"), StringComparison.Ordinal);
    }

    [Fact]
    public void StartupSweepKeepsTheLastCompletedScratchWhileANewerTrialIsActive()
    {
        using var lanes = new ProjectLaneRegistry(_ => _token);
        var handler = BuildHandler(lanes, new FakeAssessor("pangloss", [AssessmentKind.Correctness]));
        var proposalId = CanonicalId.Mint("proposal/");
        var proposalJson = BuildSetGlossProposalJson(proposalId, _seed.FirstSenseId, "active scratch retention");
        SaveCommittedProposal(proposalId, proposalJson);
        var completedJob = CreateTrialJob(proposalJson, createdUtc: "2026-08-29T11:00:00Z");
        RunAndFinish(handler, completedJob.JobId);
        var activeJob = CreateTrialJob(proposalJson, createdUtc: "2026-08-29T12:00:00Z", startRunning: false);

        var scratchRoot = Path.Combine(_root, "active-trial-scratch");
        var completedPath = Path.Combine(scratchRoot, Path.GetFileName(TrialScratchPath.ForJob(completedJob.JobId))!);
        var activePath = Path.Combine(scratchRoot, Path.GetFileName(TrialScratchPath.ForJob(activeJob.JobId))!);
        var orphanPath = Path.Combine(scratchRoot, Path.GetFileName(TrialScratchPath.ForJob("orphan-job"))!);
        Directory.CreateDirectory(completedPath);
        Directory.CreateDirectory(activePath);
        Directory.CreateDirectory(orphanPath);

        TrialScratchRetention.Sweep([(_database, ProjectWorkspaceKey.Compute(_project))], scratchRoot);

        Assert.True(Directory.Exists(completedPath));
        Assert.True(Directory.Exists(activePath));
        Assert.True(Directory.Exists(orphanPath));
    }

    [Fact]
    public void ADefaultTrialMeasuresUnanalysedWordsToo()
    {
        using var lanes = new ProjectLaneRegistry(_ => _token);
        var handler = BuildHandler(lanes, new FakeAssessor("pangloss", [AssessmentKind.Correctness]));
        var proposalId = CanonicalId.Mint("proposal/");
        var proposalJson = BuildSetGlossProposalJson(proposalId, _seed.FirstSenseId, "selection text");
        SaveCommittedProposal(proposalId, proposalJson);

        var job = CreateTrialJob(proposalJson);
        var completed = RunAndFinish(handler, job.JobId);

        Assert.Equal(JobStatus.Completed, completed.Status);
        var recorded = Assert.Single(_assessments.ListByProposal(proposalId));
        Assert.Contains(AnalysedWordform, recorded.Selection.Words);
        Assert.Contains(UnanalysedWordform, recorded.Selection.Words);

        // The words are what this run resolved to; the query is what the scope was told, and both are kept.
        Assert.Contains("\"query\"", recorded.ScopeJson, StringComparison.Ordinal);
        Assert.Contains(AssessmentScopeConfiguration.DefaultQueryText, recorded.ScopeJson, StringComparison.Ordinal);
    }

    [Fact]
    public void AllWordsOverridesAConfiguredManualAnalysisQuery()
    {
        using var lanes = new ProjectLaneRegistry(_ => _token);
        var configuration = new ProjectConfiguration(
            [new AssessmentScopeConfiguration(AssessmentScopeConfiguration.DefaultName,
                WordQueryResolver.ManualAnalysisQueryText, "pangloss", [], TimeSpan.FromSeconds(1))],
            gateOnRegression: true, purgeOnApply: true);
        var handler = BuildHandler(lanes, new FakeAssessor("pangloss", [AssessmentKind.Correctness]),
            configuration: new FixedConfigurationReader(configuration));
        var proposalId = CanonicalId.Mint("proposal/");
        var proposalJson = BuildSetGlossProposalJson(proposalId, _seed.FirstSenseId, "all words");
        SaveCommittedProposal(proposalId, proposalJson);

        var job = CreateTrialJob(proposalJson, allWords: true);
        var completed = RunAndFinish(handler, job.JobId);

        Assert.Equal(JobStatus.Completed, completed.Status);
        using var result = JsonDocument.Parse(completed.ResultJson!);
        Assert.True(result.RootElement.GetProperty("allWords").GetBoolean());
        var assessment = Assert.Single(_assessments.ListByProposal(proposalId));
        Assert.Contains(AnalysedWordform, assessment.Selection.Words);
        Assert.Contains(UnanalysedWordform, assessment.Selection.Words);
    }

    [Fact]
    public void AnObjectTimingAssessmentRecordsTheCachePathAndDigestWithNoWordRows()
    {
        const string cachePath = @"C:\stats\pangloss-cache.db";
        var cacheDigest = "sha256:" + new string('7', 64);
        using var lanes = new ProjectLaneRegistry(_ => _token);
        var handler = BuildHandler(lanes, new FakeAssessor("pangloss", [AssessmentKind.ObjectTiming],
            _ => new AssessmentRaw.FileCache(cachePath, cacheDigest)));
        var proposalId = CanonicalId.Mint("proposal/");
        var proposalJson = BuildSetGlossProposalJson(proposalId, _seed.FirstSenseId, "object timing text");
        SaveCommittedProposal(proposalId, proposalJson);

        var job = CreateTrialJob(proposalJson);
        var completed = RunAndFinish(handler, job.JobId);

        Assert.Equal(JobStatus.Completed, completed.Status);
        var header = Assert.Single(_assessments.ListByProposal(proposalId));
        var full = _assessments.Get(header.AssessmentId);
        Assert.Equal(cachePath, full.CachePath);
        Assert.Equal(cacheDigest, full.CacheDigest);
        Assert.Empty(full.Words!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationBeforePublicationCannotPublishAssessmentsOrRetainArtifacts(bool recover)
    {
        using var lanes = new ProjectLaneRegistry(_ => _token);
        var proposalId = CanonicalId.Mint("proposal/");
        var proposalJson = BuildSetGlossProposalJson(proposalId, _seed.FirstSenseId, "stale trial text");
        SaveCommittedProposal(proposalId, proposalJson);
        var job = CreateTrialJob(proposalJson, startRunning: false);
        var clock = new TrialTestClock(DateTimeOffset.Parse("2026-08-29T01:00:00Z"));
        var jobs = new JobRepository(_database, clock);
        var claims = new JobClaims(_database, clock);
        var held = claims.Claim(ProjectWorkspaceKey.Compute(_project), "runner-old", clock.UtcNow.ToString("O"),
            TimeSpan.FromSeconds(2))!;
        var claim = ClaimedJob.Of(jobs, held.JobId);
        var assessor = new TrialArtifactAssessor(Path.Combine(_root, "stale-trial-artifacts"));
        var (assessmentProxy, barrier) = AssessmentRepositoryBarrier.Create(_assessments);
        var handler = BuildHandler(lanes, assessor, assessments: assessmentProxy);
        var running = Task.Run(() => handler.RunAsync(claim, _project, CancellationToken.None));

        Assert.True(barrier.Entered.Wait(TimeSpan.FromSeconds(30)),
            "the Trial did not reach the Assessment publication boundary.");
        AssessmentPublicationSnapshot before;
        AssessmentPublicationSnapshot afterRecovery;
        JobRecord cancelled;
        string? dryRunBeforeRecovery;
        try
        {
            Assert.True(Directory.Exists(assessor.InvocationDirectory));
            dryRunBeforeRecovery = jobs.Get(job.JobId)!.DryRunJson;
            before = ReadAssessmentPublicationSnapshot();
            jobs.CancelActive(job.JobId);
            if (recover)
            {
                clock.UtcNow = clock.UtcNow.AddSeconds(3);
                new WorkerRecovery(jobs, clock, ownerId: "runner-new").RecoverInterruptedJobs(clock.UtcNow);
            }
            cancelled = jobs.Get(job.JobId)!;
            afterRecovery = ReadAssessmentPublicationSnapshot();
        }
        finally
        {
            barrier.Release.Set();
        }

        var outcome = await running.WaitAsync(TimeSpan.FromSeconds(30));
        var afterStaleTrial = ReadAssessmentPublicationSnapshot();
        _output.WriteLine("Publication snapshots before recovery: {0}; after recovery: {1}; after stale Trial: {2}; " +
            "outcome: {3}; artifact directory exists: {4}.", before, afterRecovery, afterStaleTrial,
            outcome?.Status, assessor.InvocationDirectory is { } directory && Directory.Exists(directory));

        if (!recover)
        {
            Assert.Equal(JobStatus.Running, cancelled.Status);
            Assert.True(cancelled.CancellationRequested);
            Assert.True(new JobClaims(_database).Finish(job.JobId, held.ClaimToken!,
                JobStatus.Cancelled, JobFailureCategory.Cancellation, null));
            cancelled = jobs.Get(job.JobId)!;
        }
        Assert.Equal(JobStatus.Cancelled, cancelled.Status);
        Assert.Equal("{\"assessmentDisposition\":\"cancelled\"}", cancelled.ResultJson);
        Assert.True(cancelled.DryRunPublished);
        Assert.NotNull(dryRunBeforeRecovery);
        Assert.Equal(dryRunBeforeRecovery, cancelled.DryRunJson);
        Assert.Equal(1, cancelled.Attempt);
        Assert.Equal(before, afterRecovery);
        Assert.True(afterRecovery == afterStaleTrial,
            $"stale Trial publication changed the database; before {afterRecovery}, after {afterStaleTrial}.");
        Assert.Equal(JobStatus.Cancelled, outcome?.Status);
        Assert.False(Directory.Exists(assessor.InvocationDirectory));
    }

    [Fact]
    public async Task ACommittedTrialSurvivesLateCancellationRecoveryAndStaleOwnerActions()
    {
        using var lanes = new ProjectLaneRegistry(_ => _token);
        using var cancellation = new CancellationTokenSource();
        var proposalId = CanonicalId.Mint("proposal/");
        var proposalJson = BuildSetGlossProposalJson(proposalId, _seed.FirstSenseId, "commit first text");
        SaveCommittedProposal(proposalId, proposalJson);
        var job = CreateTrialJob(proposalJson, startRunning: false);
        var clock = new TrialTestClock(DateTimeOffset.Parse("2026-08-29T01:00:00Z"));
        var jobs = new JobRepository(_database, clock);
        var claims = new JobClaims(_database, clock);
        var held = claims.Claim(ProjectWorkspaceKey.Compute(_project), "runner-commit-first",
            clock.UtcNow.ToString("O"), TimeSpan.FromSeconds(2))!;
        var claim = ClaimedJob.Of(jobs, held.JobId);
        var assessor = new TrialArtifactAssessor(Path.Combine(_root, "commit-first-artifacts"));
        CreateJobBundlePin(job.JobId);
        var (assessmentProxy, barrier) = AssessmentRepositoryBarrier.Create(_assessments);
        barrier.PauseAfterReturn = true;
        var handler = BuildHandler(lanes, assessor, assessments: assessmentProxy);
        var running = Task.Run(() => handler.RunAsync(claim, _project, cancellation.Token));

        Assert.True(barrier.Entered.Wait(TimeSpan.FromSeconds(30)),
            "the Trial did not return from its publication transaction.");
        var savedAtBoundary = jobs.Get(job.JobId)!;
        var dryRunJson = savedAtBoundary.DryRunJson;
        var resultJson = savedAtBoundary.ResultJson;
        var beforeLateCancellation = ReadAssessmentPublicationSnapshot();
        var pinsAtBoundary = CountJobBundlePins(job.JobId);
        try
        {
            jobs.CancelActive(job.JobId);
            clock.UtcNow = clock.UtcNow.AddSeconds(3);
            new WorkerRecovery(jobs, clock, ownerId: "runner-after-commit").RecoverInterruptedJobs(clock.UtcNow);
            cancellation.Cancel();
            Assert.False(claims.Renew(job.JobId, held.ClaimToken!, clock.UtcNow.ToString("O"), TimeSpan.FromMinutes(1)));
            Assert.False(claims.Finish(job.JobId, held.ClaimToken!, JobStatus.Completed,
                JobFailureCategory.None, resultJson));
        }
        finally
        {
            barrier.Release.Set();
        }

        var outcome = await running.WaitAsync(TimeSpan.FromSeconds(30));
        var final = jobs.Get(job.JobId)!;
        var afterLateCancellation = ReadAssessmentPublicationSnapshot();
        _output.WriteLine("Publication row at boundary: {0}/{1}; final: {2}/{3}; outcome null: {4}.",
            savedAtBoundary.Status, resultJson, final.Status, final.ResultJson, outcome is null);

        Assert.Equal(JobStatus.Completed, savedAtBoundary.Status);
        Assert.NotNull(resultJson);
        using var completion = JsonDocument.Parse(resultJson);
        var assessmentIds = completion.RootElement.GetProperty("assessmentIds").EnumerateArray()
            .Select(item => item.GetString()!).ToArray();
        Assert.Single(assessmentIds);
        Assert.All(assessmentIds, id => Assert.Equal(proposalId, _assessments.Get(id).ProposalId));
        Assert.Equal(JobStatus.Completed, final.Status);
        Assert.Equal(resultJson, final.ResultJson);
        Assert.Equal(dryRunJson, final.DryRunJson);
        Assert.Equal(1, final.Attempt);
        Assert.Null(outcome);
        Assert.Equal(beforeLateCancellation, afterLateCancellation);
        Assert.Equal(0, pinsAtBoundary);
        Assert.Equal(0, CountJobBundlePins(job.JobId));
        Assert.True(Directory.Exists(assessor.InvocationDirectory));
        Assert.True(Directory.Exists(TrialScratchPath.ForJob(job.JobId)));
        Assert.Single(jobs.ListAttempts(job.JobId));
    }

    [Fact]
    public async Task AssessmentCollisionRollsBackCompletionAndPinReleaseAndDisposesArtifacts()
    {
        using var lanes = new ProjectLaneRegistry(_ => _token);
        var proposalId = CanonicalId.Mint("proposal/");
        var proposalJson = BuildSetGlossProposalJson(proposalId, _seed.FirstSenseId, "rollback collision text");
        SaveCommittedProposal(proposalId, proposalJson);
        var job = CreateTrialJob(proposalJson, startRunning: false);
        var held = new JobClaims(_database).Claim(ProjectWorkspaceKey.Compute(_project), "runner-collision",
            DateTimeOffset.Parse("2026-08-29T01:00:00Z").ToString("O"), TimeSpan.FromMinutes(2))!;
        var claim = ClaimedJob.Of(_jobs, held.JobId);
        CreateJobBundlePin(job.JobId);
        var assessor = new TrialArtifactAssessor(Path.Combine(_root, "collision-artifacts"),
            kinds: [AssessmentKind.ParseTime, AssessmentKind.ObjectTiming]);
        var (assessmentProxy, barrier) = AssessmentRepositoryBarrier.Create(_assessments);
        barrier.DuplicateSecondAssessmentId = true;
        barrier.Release.Set();
        var handler = BuildHandler(lanes, assessor, assessments: assessmentProxy);
        var before = ReadAssessmentPublicationSnapshot();

        var outcome = await handler.RunAsync(claim, _project, CancellationToken.None);

        var after = ReadAssessmentPublicationSnapshot();
        var unchanged = _jobs.Get(job.JobId)!;
        Assert.Equal(JobStatus.CompletedWithAssessmentFailure, outcome?.Status);
        Assert.Equal(JobStatus.Running, unchanged.Status);
        Assert.Null(unchanged.ResultJson);
        Assert.Equal(1, CountJobBundlePins(job.JobId));
        Assert.Equal(before, after);
        Assert.Empty(_assessments.ListByProposal(proposalId));
        Assert.NotNull(assessor.InvocationDirectory);
        Assert.False(Directory.Exists(assessor.InvocationDirectory));
    }

    [Fact]
    public async Task EarlierScratchCleanupFailureDoesNotUndoCommittedResultOrRetainedArtifacts()
    {
        using var lanes = new ProjectLaneRegistry(_ => _token);
        var proposalId = CanonicalId.Mint("proposal/");
        var proposalJson = BuildSetGlossProposalJson(proposalId, _seed.FirstSenseId, "cleanup warning text");
        SaveCommittedProposal(proposalId, proposalJson);
        var job = CreateTrialJob(proposalJson);
        var assessor = new TrialArtifactAssessor(Path.Combine(_root, "cleanup-warning-artifacts"));
        var (assessmentProxy, barrier) = AssessmentRepositoryBarrier.Create(_assessments);
        barrier.AfterInnerPublication = _database.Dispose;
        barrier.Release.Set();
        var handler = BuildHandler(lanes, assessor, assessments: assessmentProxy);
        var databasePath = _database.FullPath;

        var outcome = await handler.RunAsync(ClaimedJob.Of(_jobs, job.JobId), _project, CancellationToken.None);

        Assert.Null(outcome);
        using var reopened = MotifDatabase.OpenOwned(databasePath, _project,
            MotifSchema.CurrentSchema, new Version(1, 0));
        var savedJob = new JobRepository(reopened).Get(job.JobId)!;
        var savedAssessment = Assert.Single(new AssessmentRepository(reopened).ListByProposal(proposalId));
        Assert.Equal(JobStatus.Completed, savedJob.Status);
        Assert.NotNull(savedJob.ResultJson);
        Assert.True(Directory.Exists(assessor.InvocationDirectory));
        Assert.True(Directory.Exists(TrialScratchPath.ForJob(job.JobId)));
        Assert.NotNull(new AssessmentRepository(reopened).Get(savedAssessment.AssessmentId).Invocation);
    }

    [Fact]
    public async Task AmbiguousCommitReadsBackAfterTransactionAndConnectionCleanupFailures()
    {
        using var lanes = new ProjectLaneRegistry(_ => _token);
        var proposalId = CanonicalId.Mint("proposal/");
        var proposalJson = BuildSetGlossProposalJson(proposalId, _seed.FirstSenseId, "ambiguous commit cleanup text");
        SaveCommittedProposal(proposalId, proposalJson);
        var job = CreateTrialJob(proposalJson);
        var assessor = new TrialArtifactAssessor(Path.Combine(_root, "ambiguous-cleanup-artifacts"));
        var storage = new FaultingTrialPublicationStorageBoundary
        {
            CommitOperation = transaction =>
            {
                transaction.Commit();
                throw new IOException("injected post-commit exception");
            },
            TransactionDisposeException = new IOException("injected transaction-dispose exception"),
            ConnectionCloseException = new IOException("injected connection-close exception"),
            ConnectionDisposeException = new IOException("injected connection-dispose exception"),
        };
        var handler = BuildHandler(lanes, assessor,
            assessments: new AssessmentRepository(_database, storage));

        var outcome = await handler.RunAsync(ClaimedJob.Of(_jobs, job.JobId), _project, CancellationToken.None);

        _output.WriteLine("Outcome null: {0}; read-backs: {1}; cleanup path: {2}; artifacts exist: {3}.",
            outcome is null, storage.ReadBackCount, string.Join(",", storage.Events),
            assessor.InvocationDirectory is { } directory && Directory.Exists(directory));
        Assert.Equal(1, storage.ReadBackCount);
        Assert.Null(outcome);
        Assert.Equal(["commit", "transaction-dispose", "connection-close", "connection-dispose", "read-back"],
            storage.Events);
        var savedJob = _jobs.Get(job.JobId)!;
        Assert.Equal(JobStatus.Completed, savedJob.Status);
        Assert.NotNull(savedJob.ResultJson);
        var savedAssessment = Assert.Single(_assessments.ListByProposal(proposalId));
        Assert.NotNull(_assessments.Get(savedAssessment.AssessmentId).Invocation);
        Assert.True(Directory.Exists(assessor.InvocationDirectory));
        Assert.True(Directory.Exists(TrialScratchPath.ForJob(job.JobId)));
    }

    [Fact]
    public async Task UnreadableCommitOutcomeRetainsArtifactsAndReportsAllStorageFailures()
    {
        using var lanes = new ProjectLaneRegistry(_ => _token);
        var proposalId = CanonicalId.Mint("proposal/");
        var proposalJson = BuildSetGlossProposalJson(proposalId, _seed.FirstSenseId, "unreadable cleanup text");
        SaveCommittedProposal(proposalId, proposalJson);
        var job = CreateTrialJob(proposalJson);
        var assessor = new TrialArtifactAssessor(Path.Combine(_root, "unreadable-cleanup-artifacts"));
        var storage = new FaultingTrialPublicationStorageBoundary
        {
            CommitOperation = transaction =>
            {
                transaction.Commit();
                throw new IOException("injected ambiguous commit failure");
            },
            TransactionDisposeException = new IOException("injected disposal failure"),
            ConnectionCloseException = new IOException("injected close failure"),
            ReadBackException = new IOException("injected read-back failure"),
        };
        var handler = BuildHandler(lanes, assessor,
            assessments: new AssessmentRepository(_database, storage));

        var outcome = await handler.RunAsync(ClaimedJob.Of(_jobs, job.JobId), _project, CancellationToken.None);

        Assert.Equal(JobStatus.CompletedWithAssessmentFailure, outcome?.Status);
        Assert.Contains("injected ambiguous commit failure", outcome!.ResultJson, StringComparison.Ordinal);
        Assert.Contains("injected disposal failure", outcome.ResultJson, StringComparison.Ordinal);
        Assert.Contains("injected close failure", outcome.ResultJson, StringComparison.Ordinal);
        Assert.Contains("injected read-back failure", outcome.ResultJson, StringComparison.Ordinal);
        Assert.Equal(["commit", "transaction-dispose", "connection-close", "connection-dispose", "read-back"],
            storage.Events);
        Assert.Equal(JobStatus.Completed, _jobs.Get(job.JobId)!.Status);
        Assert.Single(_assessments.ListByProposal(proposalId));
        Assert.True(Directory.Exists(assessor.InvocationDirectory));
        Assert.True(Directory.Exists(TrialScratchPath.ForJob(job.JobId)));
    }

    [Fact]
    public async Task ProvenPreCommitFailureKeepsOriginalErrorAndDisposesUnpublishedArtifacts()
    {
        using var lanes = new ProjectLaneRegistry(_ => _token);
        var proposalId = CanonicalId.Mint("proposal/");
        var proposalJson = BuildSetGlossProposalJson(proposalId, _seed.FirstSenseId, "pre-commit cleanup text");
        SaveCommittedProposal(proposalId, proposalJson);
        var job = CreateTrialJob(proposalJson);
        var assessor = new TrialArtifactAssessor(Path.Combine(_root, "pre-commit-cleanup-artifacts"));
        var storage = new FaultingTrialPublicationStorageBoundary
        {
            CommitOperation = _ => throw new IOException("injected pre-commit failure"),
            TransactionDisposeException = new IOException("injected disposal failure"),
            ConnectionCloseException = new IOException("injected close failure"),
        };
        var handler = BuildHandler(lanes, assessor,
            assessments: new AssessmentRepository(_database, storage));

        var outcome = await handler.RunAsync(ClaimedJob.Of(_jobs, job.JobId), _project, CancellationToken.None);

        Assert.Equal(JobStatus.CompletedWithAssessmentFailure, outcome?.Status);
        Assert.Contains("injected pre-commit failure", outcome!.ResultJson, StringComparison.Ordinal);
        Assert.DoesNotContain("injected disposal failure", outcome.ResultJson, StringComparison.Ordinal);
        Assert.Equal(["commit", "transaction-dispose", "connection-close", "connection-dispose", "read-back"],
            storage.Events);
        var unchanged = _jobs.Get(job.JobId)!;
        Assert.Equal(JobStatus.Running, unchanged.Status);
        Assert.Null(unchanged.ResultJson);
        Assert.Empty(_assessments.ListByProposal(proposalId));
        Assert.False(Directory.Exists(assessor.InvocationDirectory));
        Assert.False(Directory.Exists(TrialScratchPath.ForJob(job.JobId)));
    }

    [Fact]
    public async Task ImplicitPostCommitCleanupFailuresDoNotReplaceSavedSuccess()
    {
        using var lanes = new ProjectLaneRegistry(_ => _token);
        var proposalId = CanonicalId.Mint("proposal/");
        var proposalJson = BuildSetGlossProposalJson(proposalId, _seed.FirstSenseId, "implicit cleanup text");
        SaveCommittedProposal(proposalId, proposalJson);
        var job = CreateTrialJob(proposalJson);
        var assessor = new TrialArtifactAssessor(Path.Combine(_root, "implicit-cleanup-artifacts"));
        var storage = new FaultingTrialPublicationStorageBoundary
        {
            TransactionDisposeException = new IOException("injected implicit-dispose failure"),
            ConnectionCloseException = new IOException("injected implicit-close failure"),
            ConnectionDisposeException = new IOException("injected implicit-connection-dispose failure"),
        };
        var handler = BuildHandler(lanes, assessor,
            assessments: new AssessmentRepository(_database, storage));

        var outcome = await handler.RunAsync(ClaimedJob.Of(_jobs, job.JobId), _project, CancellationToken.None);

        Assert.Null(outcome);
        Assert.Equal(["commit", "transaction-dispose", "connection-close", "connection-dispose"], storage.Events);
        Assert.Equal(0, storage.ReadBackCount);
        var savedJob = _jobs.Get(job.JobId)!;
        Assert.Equal(JobStatus.Completed, savedJob.Status);
        Assert.NotNull(savedJob.ResultJson);
        Assert.Single(_assessments.ListByProposal(proposalId));
        Assert.True(Directory.Exists(assessor.InvocationDirectory));
        Assert.True(Directory.Exists(TrialScratchPath.ForJob(job.JobId)));
    }

    [Fact]
    public async Task CurrentClaimCanPublishAssessmentsAndRetainArtifacts()
    {
        using var lanes = new ProjectLaneRegistry(_ => _token);
        var proposalId = CanonicalId.Mint("proposal/");
        var proposalJson = BuildSetGlossProposalJson(proposalId, _seed.FirstSenseId, "current trial text");
        SaveCommittedProposal(proposalId, proposalJson);
        var job = CreateTrialJob(proposalJson, startRunning: false);
        var clock = new TrialTestClock(DateTimeOffset.Parse("2026-08-29T01:00:00Z"));
        var jobs = new JobRepository(_database, clock);
        var claims = new JobClaims(_database, clock);
        var held = claims.Claim(ProjectWorkspaceKey.Compute(_project), "runner-live", clock.UtcNow.ToString("O"),
            TimeSpan.FromMinutes(2))!;
        var claim = ClaimedJob.Of(jobs, held.JobId);
        var assessor = new TrialArtifactAssessor(Path.Combine(_root, "current-trial-artifacts"));
        var (assessmentProxy, barrier) = AssessmentRepositoryBarrier.Create(_assessments);
        var handler = BuildHandler(lanes, assessor, assessments: assessmentProxy);
        var before = ReadAssessmentPublicationSnapshot();
        var running = Task.Run(() => handler.RunAsync(claim, _project, CancellationToken.None));

        Assert.True(barrier.Entered.Wait(TimeSpan.FromSeconds(30)),
            "the Trial did not reach the Assessment publication boundary.");
        barrier.Release.Set();
        var outcome = await running.WaitAsync(TimeSpan.FromSeconds(30));
        var after = ReadAssessmentPublicationSnapshot();

        Assert.Null(outcome);
        Assert.Equal(JobStatus.Completed, jobs.Get(job.JobId)!.Status);
        Assert.False(claims.Finish(held.JobId, held.ClaimToken!, JobStatus.Completed,
            JobFailureCategory.None, jobs.Get(job.JobId)!.ResultJson));
        Assert.Equal(before.AssessmentCount + 1, after.AssessmentCount);
        Assert.Equal(before.InvocationCount + 1, after.InvocationCount);
        Assert.Equal(before.CurrentAssessmentId, after.CurrentAssessmentId);
        Assert.True(Directory.Exists(assessor.InvocationDirectory));
    }

    [Fact]
    public void DryRunIsPublishedDurablyBeforeTheCandidateIsPreparedForAssessment()
    {
        using var lanes = new ProjectLaneRegistry(_ => _token);
        var proposalId = CanonicalId.Mint("proposal/");
        var proposalJson = BuildSetGlossProposalJson(proposalId, _seed.FirstSenseId, "publish-order text");
        SaveCommittedProposal(proposalId, proposalJson);
        var job = CreateTrialJob(proposalJson);
        var publishedBeforePrepare = false;

        var handler = BuildHandler(lanes, new FakeAssessor("pangloss", [AssessmentKind.Correctness]),
            prepareForAssessment: (cache, ct) =>
            {
                publishedBeforePrepare = _jobs.Get(job.JobId)!.DryRunPublished;
                return DefaultPrepareForAssessment(cache, ct);
            });

        var completed = RunAndFinish(handler, job.JobId);

        Assert.Equal(JobStatus.Completed, completed.Status);
        Assert.True(publishedBeforePrepare, "the Dry Run must already be published while the candidate is prepared for Assessment.");
    }

    [Fact]
    public void PrepareForAssessmentFailureAfterPublicationYieldsCompletedWithAssessmentFailure()
    {
        using var lanes = new ProjectLaneRegistry(_ => _token);
        var proposalId = CanonicalId.Mint("proposal/");
        var proposalJson = BuildSetGlossProposalJson(proposalId, _seed.FirstSenseId, "prepare-failure text");
        SaveCommittedProposal(proposalId, proposalJson);
        var handler = BuildHandler(lanes, new FakeAssessor("pangloss", [AssessmentKind.Correctness]),
            prepareForAssessment: (_, _) => throw new IOException("disk full"));

        var job = CreateTrialJob(proposalJson);
        var completed = RunAndFinish(handler, job.JobId);

        Assert.Equal(JobStatus.CompletedWithAssessmentFailure, completed.Status);
        Assert.True(completed.DryRunPublished);
    }

    [Fact]
    public void AssessorFailureYieldsCompletedWithAssessmentFailureWithTheDryRunRetained()
    {
        using var lanes = new ProjectLaneRegistry(_ => _token);
        var proposalId = CanonicalId.Mint("proposal/");
        var proposalJson = BuildSetGlossProposalJson(proposalId, _seed.FirstSenseId, "assessor-failure text");
        SaveCommittedProposal(proposalId, proposalJson);
        var handler = BuildHandler(lanes, new FakeAssessor("pangloss", [AssessmentKind.Correctness],
            _ => throw new IOException("pangloss assess exited 1")));

        var job = CreateTrialJob(proposalJson);
        var completed = RunAndFinish(handler, job.JobId);

        Assert.Equal(JobStatus.CompletedWithAssessmentFailure, completed.Status);
        Assert.True(completed.DryRunPublished);
    }

    [Fact]
    public void CancellationDuringAssessmentRetainsThePublishedDryRunAndCancelsTheJob()
    {
        using var lanes = new ProjectLaneRegistry(_ => _token);
        var proposalId = CanonicalId.Mint("proposal/");
        var proposalJson = BuildSetGlossProposalJson(proposalId, _seed.FirstSenseId, "cancel-assessment text");
        SaveCommittedProposal(proposalId, proposalJson);
        var handler = BuildHandler(lanes, new FakeAssessor("pangloss", [AssessmentKind.Correctness],
            _ => throw new OperationCanceledException()));

        var job = CreateTrialJob(proposalJson);
        var completed = RunAndFinish(handler, job.JobId);

        Assert.Equal(JobStatus.Cancelled, completed.Status);
        Assert.True(completed.DryRunPublished);
    }

    [Fact]
    public void ASuccessfulTrialLeavesThePublishedBaselineUnchangedAndNoScratchDirectoryBehind()
    {
        using var lanes = new ProjectLaneRegistry(_ => _token);
        var proposalId = CanonicalId.Mint("proposal/");
        var proposalJson = BuildSetGlossProposalJson(proposalId, _seed.FirstSenseId, "no-ephemera text");
        SaveCommittedProposal(proposalId, proposalJson);
        var fwDataPath = Path.Combine(_publishedRoot, NewLangProjFixture.ProjectName + ".fwdata");
        var digestBefore = Sha256Of(fwDataPath);
        string? preparedDirectory = null;

        var handler = BuildHandler(lanes, new FakeAssessor("pangloss", [AssessmentKind.Correctness]),
            prepareForAssessment: async (cache, ct) =>
            {
                var directory = await DefaultPrepareForAssessment(cache, ct);
                preparedDirectory = directory;
                return directory;
            });

        var job = CreateTrialJob(proposalJson);
        var completed = RunAndFinish(handler, job.JobId);

        Assert.Equal(JobStatus.Completed, completed.Status);
        Assert.Equal(digestBefore, Sha256Of(fwDataPath));
        Assert.NotNull(preparedDirectory);
        Assert.True(Directory.Exists(preparedDirectory),
            "the scratch directory remains available for reads of this Trial's evidence.");
    }

    // One wordform carries a human-approved analysis; the other has none, and a resolved Selection excludes it.
    private const string AnalysedWordform = "zzmotiftrialanalysed";
    private const string UnanalysedWordform = "zzmotiftrialunanalysed";
    private const string OpinionWordform = "zzmotiftrialopinion";

    private sealed record TrialTemplateIds(Guid OpinionAnalysisId, Guid PhonologicalDataId);

    private static Guid SeedWordformsForSelectionTests(LcmCache cache)
    {
        IWfiAnalysis opinionAnalysis = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var analysed = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString(AnalysedWordform, cache.DefaultVernWs));
            var analysis = cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
            analysed.AnalysesOC.Add(analysis);
            cache.LangProject.DefaultUserAgent.SetEvaluation(analysis, Opinions.approves);

            cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString(UnanalysedWordform, cache.DefaultVernWs));

            var opinionWord = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString(OpinionWordform, cache.DefaultVernWs));
            opinionAnalysis = cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
            opinionWord.AnalysesOC.Add(opinionAnalysis);
        });
        return opinionAnalysis.Guid;
    }

    private void SaveCommittedProposal(CanonicalId proposalId, string proposalJson)
    {
        var digest = IntentDigest.Compute(ProposalJsonParser.Parse(proposalJson));
        _proposals.SaveRevision(new ProposalRevisionRecord(proposalId, digest, proposalJson, "proposed",
            null, null, null));
    }

    private JobRecord CreateTrialJob(string proposalJson, IReadOnlyList<string>? words = null, bool allWords = false,
        string? createdUtc = null, bool startRunning = true, JobWordList? wordList = null)
    {
        var inputJson = JsonSerializer.Serialize(new { proposalJson, scope = (string?)null, words, allWords, wordList },
            SIL.Motif.Contract.MotifJson.CreateOptions());
        var job = _jobs.Create(Guid.NewGuid().ToString("N"), ProjectWorkspaceKey.Compute(_project),
            TrialJobHandler.TrialKind, inputJson, createdUtc ?? "2026-08-29T00:00:00Z");
        if (startRunning)
        {
            var claimTime = DateTimeOffset.Parse(job.CreatedUtc).AddSeconds(1).ToString("O");
            var claimed = new JobClaims(_database).Claim(ProjectWorkspaceKey.Compute(_project), "trial-test-runner",
                claimTime, TimeSpan.FromHours(1));
            if (claimed is null || claimed.JobId != job.JobId)
                throw new InvalidOperationException("The queue returned another Trial job.");
            job = claimed;
        }
        _trialScratchPaths.Add(TrialScratchPath.ForJob(job.JobId));
        return job;
    }

    private async Task<JobStageProgress?> WaitForStageProgressAsync(string jobId, string expectedStage)
    {
        var started = Stopwatch.StartNew();
        while (started.Elapsed < TimeSpan.FromSeconds(10))
        {
            var progressJson = _jobs.Get(jobId)?.ProgressJson;
            if (!string.IsNullOrWhiteSpace(progressJson))
            {
                try
                {
                    var progress = JsonSerializer.Deserialize<JobStageProgress>(progressJson,
                        SIL.Motif.Contract.MotifJson.CreateOptions());
                    if (progress?.Stage == expectedStage) return progress;
                }
                catch (JsonException) { }
            }
            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }
        return null;
    }

    private static string[] UniqueParserSlotNames(string purpose)
    {
        var processNamespace = Environment.GetEnvironmentVariable(MachinePanGlossQueue.TestSlotNamespaceVariable)
            ?? "unscoped";
        var unique = Guid.NewGuid().ToString("N");
        var prefix = $"Global\\MotifPanGlossSlot-{processNamespace}-{purpose}-{unique}";
        return [$"{prefix}-0", $"{prefix}-1"];
    }

    private JobRecord RunAndFinish(TrialJobHandler handler, string jobId)
    {
        var claim = ClaimedJob.Of(_jobs, jobId);
        var outcome = handler.RunAsync(claim, _project, CancellationToken.None).GetAwaiter().GetResult();
        if (outcome is not null) claim.Transition(outcome.Status, outcome.Category, outcome.ResultJson);
        return _jobs.Get(jobId)!;
    }

    private TrialJobHandler BuildHandler(ProjectLaneRegistry lanes, IAssessor assessor,
        Func<LcmCache?, CancellationToken, Task<string>>? prepareForAssessment = null,
        IProjectConfigurationReader? configuration = null,
        Func<string, string, Action<bool>?, CancellationToken, Task<GrammarCheckResponse?>>? grammarHealth = null,
        IAssessmentRepository? assessments = null)
    {
        var catalog = new AssessorCatalog(new[] { assessor });
        var factory = new ScratchCacheFactory(_loader);
        return new TrialJobHandler(_baselines, _proposals, lanes,
            configuration ?? new ProjectConfigurationReader(), catalog, assessments ?? _assessments,
            (fwDataPath, scratchRoot, _) =>
            {
                var cache = factory.CreateFromFileCopy(fwDataPath, scratchRoot);
                var scratch = DryRunScratch.Adopt(cache, $"test file copy under {scratchRoot}");
                var appliedProposalIds = ProjectAppliedLog.ReadAll(scratch.PeekCache())
                    .Select(entry => entry.ProposalId).ToArray();
                return Task.FromResult<(IReadOnlyCollection<Guid>, DryRunScratch?)>((appliedProposalIds, scratch));
            },
            (scratch, plan, _) => Task.FromResult(ProposalDryRunner.Run(scratch!, plan)),
            prepareForAssessment ?? DefaultPrepareForAssessment, _root, grammarHealth);
    }

    private sealed class TrialArtifactAssessor : IAssessor
    {
        private readonly FakeAssessor _assessor;
        private readonly Action? _afterProduction;
        private string? _cachePath;
        private string? _cacheDigest;
        private string? _projectPath;

        public TrialArtifactAssessor(string runRoot, Action? afterProduction = null,
            IReadOnlyList<AssessmentKind>? kinds = null)
        {
            RunRoot = runRoot;
            _afterProduction = afterProduction;
            _assessor = new FakeAssessor("pangloss", kinds ?? [AssessmentKind.ObjectTiming],
                rawForScope: (scope, _, kind) => kind == AssessmentKind.ObjectTiming
                    ? new AssessmentRaw.FileCache(_cachePath!, _cacheDigest!)
                    : new AssessmentRaw.Batch(new BatchAnalysis(scope.WordForms.Select((word, index) =>
                        new WordAnalysis(index, word, 1, WordOutcome.NoAnalysis, "none")).ToArray(),
                        1000, _projectPath!, []) { PerWordStepLimit = scope.PerWordStepLimit }))
            {
                CaptureEvidence = Capture,
            };
        }

        public string RunRoot { get; }
        public string? InvocationDirectory { get; private set; }
        public BatchInvocationEvidence? Invocation { get; private set; }
        public byte[] SourceBytes { get; private set; } = [];
        public string Name => _assessor.Name;
        public IReadOnlyList<AssessmentKind> SupportedKinds => _assessor.SupportedKinds;

        public async Task<IReadOnlyList<ProducedAssessment>> ProduceAsync(
            AssessmentScope scope, string exportedCandidate, CancellationToken cancellationToken)
        {
            var produced = await _assessor.ProduceAsync(scope, exportedCandidate, cancellationToken)
                .ConfigureAwait(false);
            var lease = new AssessmentArtifactLease(InvocationDirectory!);
            var withLease = produced.Select(item => item with { ArtifactLease = lease }).ToArray();
            _afterProduction?.Invoke();
            return withLease;
        }

        private BatchInvocationEvidence Capture(AssessmentScope scope, string candidate)
        {
            var evidence = FakeAssessmentEvidence.Capture(RunRoot, scope, candidate);
            Invocation = evidence;
            InvocationDirectory = Path.GetDirectoryName(evidence.SourcePath)!;
            _projectPath = Directory.GetFiles(candidate, "*.fwdata", SearchOption.AllDirectories).Single();
            SourceBytes = File.ReadAllBytes(evidence.SourcePath);
            _cachePath = Path.Combine(InvocationDirectory, "stats.sqlite");
            File.WriteAllBytes(_cachePath, System.Text.Encoding.UTF8.GetBytes("synthetic statistics cache"));
            _cacheDigest = BatchInvocationEvidence.DigestFile(_cachePath);
            return evidence;
        }
    }

    private AssessmentPublicationSnapshot ReadAssessmentPublicationSnapshot()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT (SELECT COUNT(*) FROM Assessments), " +
            "(SELECT COUNT(*) FROM AssessmentInvocations), " +
            "(SELECT CurrentAssessmentId FROM MotifMetadata WHERE Id = 1);";
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        return new AssessmentPublicationSnapshot(reader.GetInt64(0), reader.GetInt64(1),
            reader.IsDBNull(2) ? null : reader.GetString(2));
    }

    private void CreateJobBundlePin(string jobId)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO ParsimonyBundles " +
            "(BundleId, BaselineTokenJson, ProjectKey, BaselineDigest, MaterialKey, EvidenceDirectory, " +
            "GrammarFactsPath, EvidencePath, BaselineRootDirectory, InputKind, CandidateIdentity, " +
            "ModelFingerprint, GrammarFactsSchemaVersion, GrammarFactsSha256, EvidenceSchemaVersion, " +
            "EvidenceSha256, CreatedUtc) VALUES ($bundle, '{}', $project, 'digest', 'material', 'evidence', " +
            "'facts', 'evidence', 'baseline', 'baseline', NULL, 'model', 1, 'facts-hash', 1, 'evidence-hash', $created); " +
            "INSERT INTO ParsimonyBundleReferences (BundleId, ReferenceKind, ReferenceId, CreatedUtc) " +
            "VALUES ($bundle, 'job', $job, $created);";
        command.Parameters.AddWithValue("$bundle", "bundle-" + Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("$project", ProjectWorkspaceKey.Compute(_project));
        command.Parameters.AddWithValue("$job", jobId);
        command.Parameters.AddWithValue("$created", "2026-08-29T01:00:00.0000000+00:00");
        command.ExecuteNonQuery();
    }

    private int CountJobBundlePins(string jobId)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM ParsimonyBundleReferences " +
            "WHERE ReferenceKind = 'job' AND ReferenceId = $job;";
        command.Parameters.AddWithValue("$job", jobId);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private sealed record AssessmentPublicationSnapshot(long AssessmentCount, long InvocationCount,
        string? CurrentAssessmentId);

    private sealed class TrialTestClock(DateTimeOffset initial) : IJobClock
    {
        public DateTimeOffset UtcNow { get; set; } = initial;
    }

    public class AssessmentRepositoryBarrier : DispatchProxy
    {
        private IAssessmentRepository _inner = null!;

        public ManualResetEventSlim Entered { get; } = new();
        public ManualResetEventSlim Release { get; } = new();
        public bool PauseAfterReturn { get; set; }
        public bool DuplicateSecondAssessmentId { get; set; }
        public Action? AfterInnerPublication { get; set; }

        public static (IAssessmentRepository Proxy, AssessmentRepositoryBarrier Barrier) Create(
            IAssessmentRepository inner)
        {
            var proxy = DispatchProxy.Create<IAssessmentRepository, AssessmentRepositoryBarrier>();
            var barrier = (AssessmentRepositoryBarrier)proxy;
            barrier._inner = inner;
            return (proxy, barrier);
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);
            var publication = targetMethod.Name is "RecordBatch" or "RecordBatchForClaim";
            if (publication && !PauseAfterReturn) Pause();

            if (targetMethod.Name == "RecordBatchForClaim" && DuplicateSecondAssessmentId && args is not null)
            {
                var records = (IReadOnlyList<NewAssessmentRecord>)args[0]!;
                Assert.True(records.Count > 1);
                args[0] = records.Select((record, index) => index == 1
                    ? record with { AssessmentId = records[0].AssessmentId }
                    : record).ToArray();
            }

            object? result;
            try
            {
                result = targetMethod.Invoke(_inner, args);
            }
            catch (TargetInvocationException exception) when (exception.InnerException is { } inner)
            {
                ExceptionDispatchInfo.Capture(inner).Throw();
                throw;
            }

            if (targetMethod.Name == "RecordBatchForClaim") AfterInnerPublication?.Invoke();
            if (publication && PauseAfterReturn) Pause();
            return result;
        }

        private void Pause()
        {
            Entered.Set();
            if (!Release.Wait(TimeSpan.FromSeconds(30)))
                throw new TimeoutException("The Assessment publication barrier was not released.");
        }
    }

    private sealed class FaultingTrialPublicationStorageBoundary : ITrialPublicationStorageBoundary
    {
        public Action<SqliteTransaction>? CommitOperation { get; init; }
        public Exception? TransactionDisposeException { get; init; }
        public Exception? ConnectionCloseException { get; init; }
        public Exception? ConnectionDisposeException { get; init; }
        public Exception? ReadBackException { get; init; }
        public int ReadBackCount { get; private set; }
        public List<string> Events { get; } = [];

        public void Commit(SqliteTransaction transaction)
        {
            Events.Add("commit");
            if (CommitOperation is { } operation) operation(transaction);
            else transaction.Commit();
        }

        public void DisposeTransaction(SqliteTransaction transaction)
        {
            Events.Add("transaction-dispose");
            transaction.Dispose();
            if (TransactionDisposeException is { } exception) throw exception;
        }

        public void CloseConnection(SqliteConnection connection)
        {
            Events.Add("connection-close");
            connection.Close();
            if (ConnectionCloseException is { } exception) throw exception;
        }

        public void DisposeConnection(SqliteConnection connection)
        {
            Events.Add("connection-dispose");
            connection.Dispose();
            if (ConnectionDisposeException is { } exception) throw exception;
        }

        public JobRecord? ReadBack(Func<JobRecord?> readBack)
        {
            Events.Add("read-back");
            ReadBackCount++;
            if (ReadBackException is { } exception) throw exception;
            return readBack();
        }
    }

    private Task<string> DefaultPrepareForAssessment(LcmCache? cache, CancellationToken _)
    {
        _loader.Save(cache!);
        var directory = Path.GetDirectoryName(Path.GetFullPath(cache!.ProjectId.Path))!;
        return Task.FromResult(directory);
    }

    private static string BuildSetGlossProposalJson(CanonicalId proposalId, Guid targetId, string text)
    {
        var afterJson = JsonSerializer.Serialize(new { ws = "en", text });
        var operationId = CanonicalId.Mint().Value;
        var target = CanonicalId.FromGuid(targetId).Value;
        return $$"""
            {
              "contractVersions": {"lexical": "1.0"},
              "proposalId": "{{proposalId.Value}}",
              "operations": [
                {
                  "operationId": "{{operationId}}",
                  "kind": "lexical/lexSense/setGloss",
                  "target": "{{target}}",
                  "after": {{afterJson}}
                }
              ]
            }
            """;
    }

    private static string BuildApproveAnalysisProposalJson(CanonicalId proposalId, Guid analysisId)
    {
        var operationId = CanonicalId.Mint("operation/").Value;
        var target = CanonicalId.FromGuid(analysisId).Value;
        return $$"""
            {
              "contractVersions": {"analysis": "1.0"},
              "proposalId": "{{proposalId.Value}}",
              "operations": [
                {
                  "operationId": "{{operationId}}",
                  "kind": "analysis/wfiAnalysis/addRefEvaluations",
                  "target": "{{target}}",
                  "after": {"member": "defaultUserApproves"}
                }
              ]
            }
            """;
    }

    private static (string Json, CanonicalId[] RuleIds) BuildOrderedRuleCreateProposalJson(
        CanonicalId proposalId, Guid phonologicalDataId)
    {
        var operationIds = Enumerable.Range(0, 6).Select(_ => CanonicalId.Mint("operation/")).ToArray();
        var ruleIds = Enumerable.Range(0, 6).Select(_ => CanonicalId.Mint("entity/")).ToArray();
        var target = CanonicalId.FromGuid(phonologicalDataId).Value;
        var operations = Enumerable.Range(0, ruleIds.Length).Select(index =>
        {
            var placementAndDependency = index == 0 ? string.Empty :
                $",\"placement\":{{\"after\":{JsonSerializer.Serialize(ruleIds[index - 1].Value)}}}," +
                $"\"dependsOn\":[{JsonSerializer.Serialize(operationIds[index - 1].Value)}]";
            return $$"""{"operationId":{{JsonSerializer.Serialize(operationIds[index].Value)}},"kind":"grammar/phPhonData/createPhonRules","entityId":{{JsonSerializer.Serialize(ruleIds[index].Value)}},"target":{{JsonSerializer.Serialize(target)}},"after":{"class":"PhRegularRule"}{{placementAndDependency}}}""";
        }).Reverse();
        var json = $$"""{"contractVersions":{"grammar":"1.0"},"proposalId":{{JsonSerializer.Serialize(proposalId.Value)}},"operations":[{{string.Join(",", operations)}}]}""";
        return (json, ruleIds);
    }

    private static string Sha256Of(string path)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(stream));
    }

    private sealed class FixedConfigurationReader(ProjectConfiguration configuration) : IProjectConfigurationReader
    {
        public ProjectConfiguration Read(ProjectLocator _) => configuration;
    }

    // Counts real saves and scratch opens so a test can prove how many of each a run actually did.
    private sealed class CountingFwDataProjectLoader : FwDataProjectLoader
    {
        public int SaveCount { get; private set; }
        public int LoadScratchCacheCount { get; private set; }

        public override void Save(LcmCache cache)
        {
            SaveCount++;
            base.Save(cache);
        }

        public override LcmCache LoadScratchCache(string fwDataFilePath, string? templatesFolder = null)
        {
            LoadScratchCacheCount++;
            return base.LoadScratchCache(fwDataFilePath, templatesFolder);
        }
    }
}
