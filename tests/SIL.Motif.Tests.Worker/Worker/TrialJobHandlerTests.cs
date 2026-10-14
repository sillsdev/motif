using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
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

    public TrialJobHandlerTests(PristineProjectFixture pristine)
    {
        Directory.CreateDirectory(_root);

        using var master = pristine.NewScratch();
        _seed = pristine.Seed;
        _opinionAnalysisId = SeedWordformsForSelectionTests(master);
        _phonologicalDataId = master.LangProject.PhonologicalDataOA.Guid;
        _loader.Save(master);

        _publishedRoot = Path.Combine(_root, "published");
        var fwDataPath = PublishedBaselineFixture.PublishAsync(master, _publishedRoot).GetAwaiter().GetResult();
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
            TextWordsProjectionBuilder.Build(master, CancellationToken.None));
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
        if (startRunning) _jobs.Transition(_jobs.Get(job.JobId)!, JobStatus.Running);
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
        Func<string, string, Action<bool>?, CancellationToken, Task<GrammarCheckResponse?>>? grammarHealth = null)
    {
        var catalog = new AssessorCatalog(new[] { assessor });
        var factory = new ScratchCacheFactory(_loader);
        return new TrialJobHandler(_baselines, _proposals, lanes,
            configuration ?? new ProjectConfigurationReader(), catalog, _assessments,
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
