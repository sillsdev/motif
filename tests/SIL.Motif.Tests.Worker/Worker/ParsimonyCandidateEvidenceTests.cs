using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Retirement;
using SIL.Motif.Host.Baselines;
using SIL.Motif.Host.Config;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parsimony;
using SIL.Motif.Host.Store;
using SIL.Motif.Host.Texts;
using SIL.Motif.LiveHost.Baselines;
using SIL.Motif.Model.DryRun;
using SIL.Motif.Runner.AppliedLog;
using SIL.Motif.Runner.DryRun;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Parsimony;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Scheduling;
using SIL.Motif.Worker.Store;
using SIL.Motif.Worker;
using CandidateEvidenceBuilder = SIL.Motif.Worker.Parsimony.ParsimonyCandidateEvidenceBuilder;
using Xunit;

namespace SIL.Motif.Tests.Worker;

[Collection(LcmCacheTestCollection.Name)]
public sealed class ParsimonyCandidateEvidenceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), nameof(ParsimonyCandidateEvidenceTests),
        Guid.NewGuid().ToString("N"));
    private readonly PristineProjectFixture _pristine;
    private readonly SeededProject _seed;
    private readonly ProjectLocator _project;
    private readonly MotifDatabase _database;
    private readonly JobRepository _jobs;
    private readonly BaselineRepository _baselines;
    private readonly BaselineToken _token;
    private readonly string _projectKey;
    private readonly string _publishedRoot;
    private readonly string _publishedFwDataPath;
    private Guid _prohibitionGuid;

    public ParsimonyCandidateEvidenceTests(PristineProjectFixture pristine)
    {
        _pristine = pristine;
        _seed = pristine.Seed;
        Directory.CreateDirectory(_root);
        var template = PublishedBaselineTemplate<AdhocIds>.Get("adhoc-prohibition-text", pristine, master =>
        {
            var prohibitionGuid = SeedAdhocProhibition(master);
            SeededProject.SeedText(master, _seed);
            return new AdhocIds(prohibitionGuid);
        });
        _prohibitionGuid = template.Metadata.ProhibitionGuid;
        _publishedRoot = Path.Combine(_root, "baseline");
        _publishedFwDataPath = template.CopyTo(_publishedRoot);
        _project = new ProjectLocator(Path.Combine(_root, "live", NewLangProjFixture.ProjectName + ".fwdata"),
            "candidate-evidence-project");
        Directory.CreateDirectory(Path.GetDirectoryName(_project.FullFwDataPath)!);
        File.WriteAllText(_project.FullFwDataPath, "the live project is not candidate storage");
        _database = MotifDatabase.OpenOwned(Path.Combine(_root, "motif.db"), _project,
            MotifSchema.CurrentSchema, new Version(1, 0));
        _jobs = new JobRepository(_database);
        _baselines = new BaselineRepository(_database);
        _projectKey = ProjectWorkspaceKey.Compute(_project);
        _token = new BaselineToken("candidate-evidence-project", "sha256:" + new string('1', 64),
            "projection-v1", "2026-10-07T00:00:00Z", "sha256:" + new string('2', 64));
        _baselines.Record(_projectKey,
            new BaselinePublication(_publishedRoot, _publishedFwDataPath, _token),
            DateTimeOffset.Parse("2026-10-07T00:00:00Z"), DateTimeOffset.Parse("2026-10-07T00:00:00Z"),
            template.Projection, template.Summary);
    }

    public void Dispose()
    {
        _database.Dispose();
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void PrerequisiteRevisionRemainsPinnedAfterItsDraftChanges()
    {
        var prerequisiteId = CanonicalId.Mint("proposal/");
        var prerequisiteJson = Proposal(prerequisiteId, Guid.NewGuid(), "original prerequisite");
        var prerequisite = FrozenProposalRevision.Create(prerequisiteJson);
        var requested = Proposal(CanonicalId.Mint("proposal/"), Guid.NewGuid(), "requested change",
            [prerequisiteId.Value]);
        var frozenRequested = FrozenProposalRevision.Create(requested);
        var frozenJob = new DryRunJobInput(DryRunJobInput.CurrentSchemaVersion, frozenRequested,
            [prerequisite], null);
        var serialized = JsonSerializer.Serialize(frozenJob);

        var laterDraft = Proposal(prerequisiteId, Guid.NewGuid(), "edited draft");
        Assert.NotEqual(prerequisiteJson, laterDraft);

        var reloaded = DryRunJobInput.Parse(serialized);
        var plan = reloaded.BuildExecutionPlan([]);

        Assert.Equal(prerequisite.ProposalJson, reloaded.Prerequisites.Single().ProposalJson);
        Assert.Equal("original prerequisite", plan.Prerequisites.Single().Operations.Single().After!
            .Value.GetProperty("text").GetString());
    }

    [Fact]
    public void CandidateIdentityChangesWithProposalMaterialAtTheSameParserFingerprint()
    {
        var proposalId = CanonicalId.Mint("proposal/");
        var proposalOne = FrozenProposalRevision.Create(Proposal(proposalId, Guid.NewGuid(), "first value"));
        var proposalTwo = FrozenProposalRevision.Create(Proposal(proposalId, Guid.NewGuid(), "second value"));
        var token = new BaselineToken("project", "sha256:" + new string('1', 64), "projection-v1",
            "2026-10-07T00:00:00Z", "sha256:" + new string('2', 64));
        var anchorDigest = "sha256:" + new string('3', 64);
        var effectDigest = "sha256:" + new string('4', 64);
        var sourceDigest = "sha256:" + new string('5', 64);
        var scope = new ParsimonyScopeBinding(ParsimonyEvidenceScopeKind.ProjectApproved, null);

        var one = CandidateEvidenceBuilder.ComputeCandidateIdentity(proposalOne, [], token, effectDigest,
            anchorDigest, sourceDigest, "sha256:" + new string('6', 64), "sha256:model", "sha256:parser", 6,
            1, scope, "sha256:judgments");
        var two = CandidateEvidenceBuilder.ComputeCandidateIdentity(proposalTwo, [], token, effectDigest,
            anchorDigest, sourceDigest, "sha256:" + new string('6', 64), "sha256:model", "sha256:parser", 6,
            1, scope, "sha256:judgments");

        Assert.NotEqual(one, two);
    }

    [Fact]
    public void CandidateReadbackMustMatchThePublishedDryRunAnchorAndEffectDigest()
    {
        var effectDigest = "sha256:" + new string('4', 64);
        var intentDigest = "sha256:" + new string('7', 64);
        var anchor = new BoundDryRunAnchor(intentDigest, "sha256:" + new string('8', 64), effectDigest,
            "1.0.0.0", "1.0.0.0", "projection-v1", "20261007T000000Z");
        var dryRun = new DryRun(intentDigest, "note", [], effectDigest, anchor);
        var expected = JsonSerializer.Serialize(new
        {
            intentDigest,
            baselineNote = "note",
            expectedEffects = Array.Empty<object>(),
            effectDigest,
            anchor,
        }, SIL.Motif.Contract.MotifJson.CreateOptions());

        CandidateEvidenceBuilder.VerifyReconstructedDryRun(expected, dryRun);
        var rerun = dryRun with { Anchor = anchor with { DryRunAtUtc = "20261007T000001Z" } };
        Assert.Equal(anchor, CandidateEvidenceBuilder.VerifyReconstructedDryRun(expected, rerun));

        var changedAnchor = anchor with { FootprintDigest = "sha256:" + new string('9', 64) };
        var changed = dryRun with { Anchor = changedAnchor };
        Assert.Throws<InvalidDataException>(() => CandidateEvidenceBuilder.VerifyReconstructedDryRun(expected, changed));
        var changedEffect = dryRun with { EffectDigest = "sha256:" + new string('a', 64) };
        Assert.Throws<InvalidDataException>(() => CandidateEvidenceBuilder.VerifyReconstructedDryRun(expected, changedEffect));
    }

    [Fact]
    public void DeletedApprovedAnalysisIsInconclusiveUntilAnExplicitMappingExists()
    {
        var approved = new ParsimonyApprovedAnalysisTuple("wordform-guid", "analysis-guid",
            "sha256:" + new string('a', 64));

        var preservation = CandidateEvidenceBuilder.CompareApprovedAnalyses([approved], []);

        Assert.Equal("inconclusive", preservation.Status);
        Assert.Equal(1, preservation.BaselineCount);
        Assert.Equal(0, preservation.PreservedCount);
        Assert.Equal(approved, Assert.Single(preservation.UnmatchedBaselineTuples));
    }

    [Fact]
    public async Task CandidateFactsAndEvidenceUseTheFrozenDisabledProhibitionAndKeepTheBaselineUnchanged()
    {
        var sourceDigest = DigestDirectory(_publishedRoot);
        var liveDigest = DigestFile(_project.FullFwDataPath);
        var (outcome, candidateClaim, parserPath) = await RunCandidateAsync((invoker, _) => invoker);

        var completed = Assert.IsType<JobOutcome>(outcome);
        Assert.True(completed.Status == JobStatus.Completed, completed.ResultJson);
        candidateClaim.Transition(completed.Status, completed.Category, completed.ResultJson);
        var response = JsonSerializer.Deserialize<ParsimonyCandidateEvidenceResponse>(completed.ResultJson!,
            SIL.Motif.Contract.MotifJson.CreateOptions())!;
        Assert.Equal("baseline", response.Before.Inputs.InputKind);
        Assert.Equal("candidate", response.After.Inputs.InputKind);
        Assert.Contains(response.StageTimings!, t => t is { Stage: "facts", Side: "candidate" });
        Assert.Equal("total", response.StageTimings![^1].Stage);
        Assert.NotEqual(response.Before.Inputs.ModelFingerprint, response.After.Inputs.ModelFingerprint);
        Assert.NotEqual(response.Before.Inputs.Evidence.Sha256, response.After.Inputs.Evidence.Sha256);
        Assert.Contains(response.FindingChanges, item => item.Change is "evidence-changed" or "removed");
        Assert.Equal("preserved", response.ApprovedAnalysisPreservation.Status);

        var artifacts = new EvidenceArtifactRepository(_database);
        var before = artifacts.Get(response.Before.Inputs.BundleId)!;
        var after = artifacts.Get(response.After.Inputs.BundleId)!;
        Assert.Equal("0", ReadText(before.GrammarFactsPath,
            $"SELECT disabled FROM adhoc_prohibition WHERE prohibition_guid='{_prohibitionGuid:D}';"));
        Assert.Equal("1", ReadText(after.GrammarFactsPath,
            $"SELECT disabled FROM adhoc_prohibition WHERE prohibition_guid='{_prohibitionGuid:D}';"));
        Assert.Equal("complete", ReadText(before.GrammarFactsPath,
            "SELECT status FROM artifact_section WHERE section='adhoc_groups';"));
        Assert.Equal("complete", ReadText(after.GrammarFactsPath,
            "SELECT status FROM artifact_section WHERE section='adhoc_groups';"));
        Assert.Equal("one authored group rationale", ReadText(before.GrammarFactsPath,
            "SELECT text FROM adhoc_group_text WHERE group_guid=" +
            "'40000000-0000-0000-0000-000000000001' AND field='description' AND writing_system='qaa';"));
        Assert.Equal("3", ReadText(before.GrammarFactsPath, "SELECT COUNT(*) FROM adhoc_group_member;"));
        Assert.Equal("baseline", ReadText(before.EvidencePath,
            "SELECT input_kind FROM artifact_metadata WHERE singleton=1;"));
        Assert.Equal("candidate", ReadText(after.EvidencePath,
            "SELECT input_kind FROM artifact_metadata WHERE singleton=1;"));
        Assert.Equal(sourceDigest, DigestDirectory(_publishedRoot));
        Assert.Equal(liveDigest, DigestFile(_project.FullFwDataPath));
        Assert.DoesNotContain("batch", FakeParser.Invocations(parserPath));
    }

    [Fact]
    public async Task ReplacingTheParserDuringTheComparisonRefuses()
    {
        var (outcome, _, parserPath) = await RunCandidateAsync((invoker, path) =>
            new ParserReplacingInvoker(invoker, path));

        var failed = Assert.IsType<JobOutcome>(outcome);
        Assert.Equal(JobStatus.Failed, failed.Status);
        Assert.Contains("PanGloss changed during the comparison", failed.ResultJson, StringComparison.Ordinal);
        Assert.Contains("grammar-health", FakeParser.Invocations(parserPath));
    }

    [Fact]
    public async Task ACommittedTrialIsASourceThatReadsAsItsDryRunDoes()
    {
        ParsimonyCandidateSource? fromDryRun = null;
        ParsimonyCandidateSource? fromTrial = null;
        JobRecord? trialJob = null;
        var (outcome, _, _) = await RunCandidateAsync((invoker, _) => invoker, sourceOf: (dryRun, json) =>
        {
            trialJob = CompleteTrial(dryRun.DryRunJson!, json, JobStatus.Completed);
            fromDryRun = ParsimonyCandidateSource.Read(_database, dryRun.JobId);
            fromTrial = ParsimonyCandidateSource.Read(_database, trialJob.JobId);
            return trialJob;
        });

        Assert.Equal(fromDryRun!.DryRunInput.Proposal, fromTrial!.DryRunInput.Proposal);
        Assert.Equal(fromDryRun.DryRunInput.Prerequisites, fromTrial.DryRunInput.Prerequisites);
        Assert.Equal(fromDryRun.Source, fromTrial.Source);
        Assert.Equal(fromDryRun.PublishedDryRunJson, fromTrial.PublishedDryRunJson);
        var completed = Assert.IsType<JobOutcome>(outcome);
        Assert.True(completed.Status == JobStatus.Completed, completed.ResultJson);
        var response = JsonSerializer.Deserialize<ParsimonyCandidateEvidenceResponse>(completed.ResultJson!,
            SIL.Motif.Contract.MotifJson.CreateOptions())!;
        Assert.Equal(trialJob!.JobId, response.Candidate.DryRunJobId);
        using var published = JsonDocument.Parse(fromTrial.PublishedDryRunJson);
        Assert.Equal(published.RootElement.GetProperty("effectDigest").GetString(),
            response.Candidate.DryRunEffectDigest);
    }

    [Theory]
    [InlineData(JobStatus.CompletedWithAssessmentFailure)]
    [InlineData(JobStatus.Failed)]
    public void ATrialThatDidNotCommitIsNotASource(JobStatus status)
    {
        var trial = CompleteTrial("{}", DisableProhibitionProposal(_prohibitionGuid), status);

        Assert.Null(ParsimonyCandidateSource.Read(_database, trial.JobId));
    }

    [Fact]
    public void ACommittedTrialIsNotASourceOnceTheBaselineIsNoLongerCurrent()
    {
        var trial = CompleteTrial("{}", DisableProhibitionProposal(_prohibitionGuid),
            JobStatus.Completed, new BaselineToken("candidate-evidence-project", "sha256:" + new string('3', 64),
                "projection-v1", "2026-10-08T00:00:00Z", "sha256:" + new string('4', 64)));

        var exception = Assert.Throws<InvalidDataException>(() => ParsimonyCandidateSource.Read(_database, trial.JobId));
        Assert.Contains("Baseline changed", exception.Message, StringComparison.Ordinal);
    }

    private JobRecord CompleteTrial(string publishedDryRunJson, string proposalJson, JobStatus status,
        BaselineToken? measured = null)
    {
        var options = SIL.Motif.Contract.MotifJson.CreateOptions();
        var input = new TrialJobInput(proposalJson, null, ["word"], false, []);
        var trial = _jobs.Create(CanonicalId.Mint("job/").Value, _projectKey, "trial",
            JsonSerializer.Serialize(input, options), "2026-10-07T00:00:30Z");
        var claim = Claim(trial.JobId);
        claim.PublishDryRun(publishedDryRunJson);
        var result = JsonSerializer.Serialize(new
        {
            baselineToken = measured ?? _token,
            assessmentIds = Array.Empty<string>(),
            allWords = false,
        }, options);
        claim.Transition(status, status == JobStatus.Failed ? JobFailureCategory.Unknown : JobFailureCategory.None,
            result);
        return _jobs.Get(trial.JobId)!;
    }

    private async Task<(JobOutcome? Outcome, ClaimedJob Claim, string ParserPath)> RunCandidateAsync(
        Func<PanGlossInvoker, string, IPanGlossInvoker> wrapInvoker, string? proposalText = null,
        Func<JobRecord, string, JobRecord>? sourceOf = null, bool baselineReportFirst = false,
        IReadOnlyList<string>? groupMeasureIds = null, string? existingParserPath = null,
        ParsimonyScopeBinding? candidateScope = null, SIL.Motif.Contract.Retirement.RetirementExpectationTranslation? translation = null,
        bool serialSides = false, bool baselineFromAutomaticRun = false)
    {
        var proposalJson = proposalText ?? DisableProhibitionProposal(_prohibitionGuid);
        var source = new DryRunSourceBinding(_token, _publishedRoot, _publishedFwDataPath,
            "sha256:" + DigestFile(_publishedFwDataPath));
        var dryRunInput = new DryRunJobInput(DryRunJobInput.CurrentSchemaVersion,
            FrozenProposalRevision.Create(proposalJson), [], source);
        var dryRunJob = _jobs.Create(CanonicalId.Mint("job/").Value, _projectKey,
            ProjectJobHandlers.DryRunKind, JsonSerializer.Serialize(dryRunInput,
                SIL.Motif.Contract.MotifJson.CreateOptions()), "2026-10-07T00:00:00Z");
        using var lanes = new ProjectLaneRegistry(_ => _token);
        var loader = new FwDataProjectLoader();
        var scratchFactory = new BaselineScratchFactory(loader);
        var dryRunHandler = new DryRunJobHandler(_baselines, new ProposalRepository(_database), lanes,
            _ => null,
            (path, _) =>
            {
                var scratch = scratchFactory.OpenSingleUse(path);
                var applied = ProjectAppliedLog.ReadAll(scratch.PeekCache())
                    .Select(entry => entry.ProposalId).ToArray();
                return Task.FromResult<(IReadOnlyCollection<Guid>, DryRunScratch?)>((applied, scratch));
            },
            (scratch, plan, _) => Task.FromResult(ProposalDryRunner.Run(scratch!, plan)));
        var dryRunClaim = DryRunJobTestHarness.Claim(_jobs, dryRunJob.JobId);
        var completedDryRun = await DryRunJobTestHarness.RunAndFinishAsync(_jobs, dryRunHandler,
            dryRunClaim, _project);
        Assert.Equal(JobStatus.CompletedDryRunOnly, completedDryRun.Status);

        var parserPath = existingParserPath;
        if (parserPath is null)
        {
            parserPath = FakeParser.CopyRecordingInvocations(Path.Combine(_root, "parser-" + Guid.NewGuid().ToString("N")));
            FakeParser.WriteFactsFixture(parserPath, FactsFixture(disableChangedRule: false));
            FakeParser.WriteCandidateFactsFixture(parserPath, FactsFixture(disableChangedRule: true));
        }
        using var invoker = new PanGlossInvoker(parserPath);
        var scope = new ParsimonyScopeBinding(ParsimonyEvidenceScopeKind.ProjectApproved, null);
        if (baselineFromAutomaticRun) await RunQueuedBaselineBundleAsync(invoker, parserPath);
        else if (baselineReportFirst) await RunBaselineReportAsync(invoker, parserPath, scope, groupMeasureIds);
        scope = candidateScope ?? scope;
        _invocationsBeforeCandidate = FakeParser.Invocations(parserPath).Count;
        var sourceJob = sourceOf is null ? dryRunJob : sourceOf(_jobs.Get(dryRunJob.JobId)!, proposalJson);
        var candidateInput = new ParsimonyCandidateJobInput(ParsimonyCandidateJobInput.CurrentSchemaVersion,
            sourceJob.JobId, groupMeasureIds?[0] ?? "P-adhoc-duplicate", scope,
            RetirementExpectationTranslation: translation, MeasureIds: groupMeasureIds);
        var candidateJob = _jobs.Create(CanonicalId.Mint("job/").Value, _projectKey,
            ParsimonyCandidateEvidenceBuilder.JobKind,
            JsonSerializer.Serialize(candidateInput, SIL.Motif.Contract.MotifJson.CreateOptions()),
            "2026-10-07T00:01:00Z");
        var candidateClaim = Claim(candidateJob.JobId);
        var handler = RetainedFacts.Builder(_database, _projectKey, _project,
            new RunnerOptions { Root = Path.Combine(_root, "worker"), ParserPath = parserPath },
            wrapInvoker(invoker, parserPath), lanes);

        handler.RunSidesInSequence = serialSides;
        var outcome = await handler.RunAsync(candidateClaim, CancellationToken.None);
        return (outcome, candidateClaim, parserPath);
    }

    private int _invocationsBeforeCandidate;

    public static TheoryData<bool> BaselineSideStored => new() { false, true };

    [Theory]
    [MemberData(nameof(BaselineSideStored))]
    public async Task AGroupRunMeasuresEveryGroupMeasureOnBothSidesInOneCandidateBuild(bool baselineStored)
    {
        var group = ParsimonyAdmission.MeasureIdsFor(ParsimonyGroupRequest.Grammar);
        Assert.True(group.Count > 1);

        var (outcome, claim, parserPath) = await RunCandidateAsync((invoker, _) => invoker,
            baselineReportFirst: baselineStored, groupMeasureIds: group);

        var completed = Assert.IsType<JobOutcome>(outcome);
        Assert.True(completed.Status == JobStatus.Completed, completed.ResultJson);
        claim.Transition(completed.Status, completed.Category, completed.ResultJson);
        var response = JsonSerializer.Deserialize<ParsimonyCandidateEvidenceResponse>(completed.ResultJson!,
            SIL.Motif.Contract.MotifJson.CreateOptions())!;
        Assert.Equal(group[0], response.MeasureId);
        Assert.Equal(group.Skip(1), response.MoreMeasures!.Select(item => item.MeasureId));
        Assert.All(response.MoreMeasures!, item =>
        {
            Assert.NotEqual(item.Before.ReportId, item.After.ReportId);
            Assert.Equal(response.Before.Inputs.BundleId, item.Before.Inputs.BundleId);
            Assert.Equal(response.After.Inputs.BundleId, item.After.Inputs.BundleId);
        });
        var invocations = FakeParser.Invocations(parserPath).Skip(_invocationsBeforeCandidate)
            .Where(command => command != "describe").ToArray();
        Assert.Equal(baselineStored ? new[] { "grammar-health" } : new[] { "grammar-health", "grammar-health" },
            invocations);
    }

    [Fact]
    public void AGroupInputKeepsItsFirstMeasureAndRefusesUnknownOrRepeatedMeasures()
    {
        var group = ParsimonyAdmission.MeasureIdsFor(ParsimonyGroupRequest.Grammar);
        var scope = new ParsimonyScopeBinding(ParsimonyEvidenceScopeKind.ProjectApproved, null);
        string Json(string measureId, IReadOnlyList<string> ids) => JsonSerializer.Serialize(
            new ParsimonyCandidateJobInput(ParsimonyCandidateJobInput.CurrentSchemaVersion, "job/x", measureId,
                scope, MeasureIds: ids), SIL.Motif.Contract.MotifJson.CreateOptions());

        Assert.Equal(group, ParsimonyCandidateJobInput.Parse(Json(group[0], group)).RunMeasureIds);
        var pinned = SIL.Motif.Worker.Jobs.JobParserPin.Stamp(Json(group[0], group), Environment.ProcessPath);
        Assert.Equal(group, ParsimonyCandidateJobInput.Parse(pinned).RunMeasureIds);
        Assert.Throws<InvalidDataException>(() => ParsimonyCandidateJobInput.Parse(Json(group[1], group)));
        Assert.Throws<InvalidDataException>(() => ParsimonyCandidateJobInput.Parse(Json(group[0], [group[0], group[0]])));
        Assert.Throws<InvalidDataException>(() => ParsimonyCandidateJobInput.Parse(Json(group[0], [group[0], "P-nope"])));
        Assert.Equal([group[0]], ParsimonyCandidateJobInput.Parse(JsonSerializer.Serialize(
            new ParsimonyCandidateJobInput(3, "job/x", group[0], scope),
            SIL.Motif.Contract.MotifJson.CreateOptions())).RunMeasureIds);
    }

    private async Task RunBaselineReportAsync(PanGlossInvoker invoker, string parserPath, ParsimonyScopeBinding scope,
        IReadOnlyList<string>? groupMeasureIds)
    {
        var job = _jobs.Create(CanonicalId.Mint("job/").Value, _projectKey, ParsimonyJobHandler.JobKind,
            JsonSerializer.Serialize(groupMeasureIds is null ? new ParsimonyJobInput("P-adhoc-duplicate", scope)
                : new ParsimonyJobInput(ParsimonyAdmission.AllMeasures, scope, null, groupMeasureIds),
                SIL.Motif.Contract.MotifJson.CreateOptions()), "2026-10-07T00:00:30Z");
        var claim = Claim(job.JobId);
        var handler = RetainedFacts.Handler(_database, _baselines, _projectKey, _project,
            new RunnerOptions { Root = Path.Combine(_root, "worker"), ParserPath = parserPath }, invoker);
        var outcome = await handler.RunAsync(claim, CancellationToken.None);
        Assert.True(outcome!.Status == JobStatus.Completed, outcome.ResultJson);
        claim.Transition(outcome.Status, outcome.Category, outcome.ResultJson);
    }

    // Queues the Baseline bundle the way the Dry Run does, and runs that job.
    private async Task RunQueuedBaselineBundleAsync(PanGlossInvoker invoker, string parserPath)
    {
        var queued = ParsimonyAutoRun.EnqueueBaselineBundle(_database, _projectKey, _token, parserPath);
        Assert.NotNull(queued);
        Assert.Null(ParsimonyAutoRun.EnqueueBaselineBundle(_database, _projectKey, _token, parserPath));
        var claimed = new JobClaims(_database, parserKey: ParserIdentity.KeyOf(parserPath)).Claim(_projectKey,
            "candidate-evidence-test", AfterTheBaselineBundleDelay, TimeSpan.FromMinutes(5));
        Assert.Equal(queued, claimed!.JobId);
        var claim = new ClaimedJob(_jobs, claimed);
        var handler = RetainedFacts.Handler(_database, _baselines, _projectKey, _project,
            new RunnerOptions { Root = Path.Combine(_root, "worker"), ParserPath = parserPath }, invoker);
        var outcome = await handler.RunAsync(claim, CancellationToken.None);
        Assert.True(outcome!.Status == JobStatus.Completed, outcome.ResultJson);
        claim.Transition(outcome.Status, outcome.Category, outcome.ResultJson);
        Assert.Null(ParsimonyAutoRun.EnqueueBaselineBundle(_database, _projectKey, _token, parserPath));
    }

    [Fact]
    public async Task TheFirstCandidateAfterTheQueuedBaselineBundleReusesItsBeforeSide()
    {
        var group = ParsimonyAdmission.MeasureIdsFor(ParsimonyGroupRequest.Grammar);

        var (outcome, claim, parserPath) = await RunCandidateAsync((invoker, _) => invoker,
            groupMeasureIds: group, baselineFromAutomaticRun: true);

        var completed = Assert.IsType<JobOutcome>(outcome);
        Assert.True(completed.Status == JobStatus.Completed, completed.ResultJson);
        claim.Transition(completed.Status, completed.Category, completed.ResultJson);
        Assert.Equal(["grammar-health"], FakeParser.Invocations(parserPath).Skip(_invocationsBeforeCandidate));
        var response = JsonSerializer.Deserialize<ParsimonyCandidateEvidenceResponse>(completed.ResultJson!,
            SIL.Motif.Contract.MotifJson.CreateOptions())!;
        Assert.Equal(group.Skip(1), response.MoreMeasures!.Select(item => item.MeasureId));
        Assert.DoesNotContain(response.StageTimings!, timing => timing is { Stage: "facts", Side: "baseline" });
    }

    [Fact]
    public void ABaselineBundleIsQueuedAgainOnlyForANewBaselineToken()
    {
        var parserPath = FakeParser.CopyRecordingInvocations(Path.Combine(_root, "parser-" + Guid.NewGuid().ToString("N")));
        Assert.NotNull(ParsimonyAutoRun.EnqueueBaselineBundle(_database, _projectKey, _token, parserPath));
        Assert.Null(ParsimonyAutoRun.EnqueueBaselineBundle(_database, _projectKey, _token, parserPath));
        var next = new BaselineToken(_token.ProjectIdentity, "sha256:" + new string('3', 64), _token.ProjectionVersion,
            _token.CapturedUtc, "sha256:" + new string('4', 64));
        Assert.NotNull(ParsimonyAutoRun.EnqueueBaselineBundle(_database, _projectKey, next, parserPath));
    }

    [Fact]
    public async Task SidesBuiltAtTheSameTimeWriteTheSameReportsAsSidesBuiltInSequence()
    {
        var group = ParsimonyAdmission.MeasureIdsFor(ParsimonyGroupRequest.Grammar);
        var proposal = DisableProhibitionProposal(_prohibitionGuid);
        var (parallelOutcome, parallelClaim, parserPath) = await RunCandidateAsync((invoker, _) => invoker,
            proposalText: proposal, groupMeasureIds: group);
        var parallel = Assert.IsType<JobOutcome>(parallelOutcome);
        Assert.True(parallel.Status == JobStatus.Completed, parallel.ResultJson);
        parallelClaim.Transition(parallel.Status, parallel.Category, parallel.ResultJson);
        AssertBothSidesBuilt(CandidateCommands(parserPath));

        // A changed executable keeps the stored before side from being reused, so the second run builds both again.
        using (var stream = new FileStream(parserPath, FileMode.Append, FileAccess.Write)) stream.WriteByte(0);
        var (serialOutcome, serialClaim, _) = await RunCandidateAsync((invoker, _) => invoker,
            proposalText: proposal, groupMeasureIds: group, existingParserPath: parserPath, serialSides: true);
        var serial = Assert.IsType<JobOutcome>(serialOutcome);
        Assert.True(serial.Status == JobStatus.Completed, serial.ResultJson);
        serialClaim.Transition(serial.Status, serial.Category, serial.ResultJson);
        Assert.Equal(["grammar-health", "grammar-health"], CandidateCommands(parserPath));

        static string Normal(string answer) => System.Text.RegularExpressions.Regex.Replace(answer,
            "parsimony-bundle-[0-9a-f]{32}", "bundle");
        Assert.Equal(Normal(Answer(parallel.ResultJson!)), Normal(Answer(serial.ResultJson!)));
    }

    [Fact]
    public async Task TheCandidateReusesTheStoredBaselineSideAndParsesOnlyTheCandidate()
    {
        var (outcome, claim, parserPath) = await RunCandidateAsync((invoker, _) => invoker,
            baselineReportFirst: true);

        var completed = Assert.IsType<JobOutcome>(outcome);
        Assert.True(completed.Status == JobStatus.Completed, completed.ResultJson);
        claim.Transition(completed.Status, completed.Category, completed.ResultJson);
        Assert.Equal(["grammar-health"], FakeParser.Invocations(parserPath).Skip(_invocationsBeforeCandidate));
        var response = JsonSerializer.Deserialize<ParsimonyCandidateEvidenceResponse>(completed.ResultJson!,
            SIL.Motif.Contract.MotifJson.CreateOptions())!;
        Assert.Equal("baseline", response.Before.Inputs.InputKind);
        Assert.Equal("candidate", response.After.Inputs.InputKind);
        Assert.Contains(response.FindingChanges, item => item.Change is "evidence-changed" or "removed");
    }

    [Fact]
    public async Task TheCandidateBuildsBothSidesWhenNoBaselineSideIsStored()
    {
        var (outcome, _, parserPath) = await RunCandidateAsync((invoker, _) => invoker);

        Assert.Equal(JobStatus.Completed, Assert.IsType<JobOutcome>(outcome).Status);
        AssertBothSidesBuilt(FakeParser.Invocations(parserPath)
            .Skip(_invocationsBeforeCandidate).Where(command => command != "describe"));
    }

    [Fact]
    public async Task ASecondRunOnTheSameBaselineReusesTheFirstRunsBeforeSideAndAnswersByteForByteTheSame()
    {
        var group = ParsimonyAdmission.MeasureIdsFor(ParsimonyGroupRequest.Grammar);
        var proposal = DisableProhibitionProposal(_prohibitionGuid);
        var (firstOutcome, firstClaim, parserPath) = await RunCandidateAsync((invoker, _) => invoker,
            proposalText: proposal, groupMeasureIds: group);
        var first = Assert.IsType<JobOutcome>(firstOutcome);
        Assert.True(first.Status == JobStatus.Completed, first.ResultJson);
        firstClaim.Transition(first.Status, first.Category, first.ResultJson);
        AssertBothSidesBuilt(FakeParser.Invocations(parserPath)
            .Skip(_invocationsBeforeCandidate).Where(command => command != "describe"));

        var (secondOutcome, secondClaim, _) = await RunCandidateAsync((invoker, _) => invoker,
            proposalText: proposal, groupMeasureIds: group, existingParserPath: parserPath);
        var second = Assert.IsType<JobOutcome>(secondOutcome);
        Assert.True(second.Status == JobStatus.Completed, second.ResultJson);
        secondClaim.Transition(second.Status, second.Category, second.ResultJson);

        Assert.Equal(["grammar-health"], FakeParser.Invocations(parserPath).Skip(_invocationsBeforeCandidate)
            .Where(command => command != "describe"));
        Assert.Equal(Answer(first.ResultJson!), Answer(second.ResultJson!));
    }

    [Fact]
    public async Task ASecondRunBuildsBothSidesAgainWhenThePanGlossExecutableHasChanged()
    {
        var proposal = DisableProhibitionProposal(_prohibitionGuid);
        var (firstOutcome, firstClaim, parserPath) = await RunCandidateAsync((invoker, _) => invoker,
            proposalText: proposal);
        var first = Assert.IsType<JobOutcome>(firstOutcome);
        firstClaim.Transition(first.Status, first.Category, first.ResultJson);
        using (var stream = new FileStream(parserPath, FileMode.Append, FileAccess.Write)) stream.WriteByte(0);

        var (secondOutcome, _, _) = await RunCandidateAsync((invoker, _) => invoker, proposalText: proposal,
            existingParserPath: parserPath);

        Assert.Equal(JobStatus.Completed, Assert.IsType<JobOutcome>(secondOutcome).Status);
        AssertBothSidesBuilt(FakeParser.Invocations(parserPath)
            .Skip(_invocationsBeforeCandidate).Where(command => command != "describe"));
    }

    // The two sides run at once, so only the multiset of commands is fixed, not their interleaving.
    [Fact]
    public void ATrialAndACandidateQueuedAfterABaselineBundleAreClaimedBeforeIt()
    {
        var parserPath = FakeParser.CopyRecordingInvocations(Path.Combine(_root, "parser-" + Guid.NewGuid().ToString("N")));
        var prebuild = ParsimonyAutoRun.EnqueueBaselineBundle(_database, _projectKey, _token, parserPath)!;
        var trial = _jobs.Create(CanonicalId.Mint("job/").Value, _projectKey, TrialJobHandler.TrialKind, "{}",
            "2026-10-07T00:00:05Z").JobId;
        var candidate = _jobs.Create(CanonicalId.Mint("job/").Value, _projectKey,
            ParsimonyCandidateEvidenceBuilder.JobKind, "{}", "2026-10-07T00:00:00Z").JobId;
        var claims = new JobClaims(_database, parserKey: ParserIdentity.KeyOf(parserPath));
        var order = new List<string>();
        for (var step = 0; step < 3; step++)
        {
            var head = claims.PeekHead(_projectKey, AfterTheBaselineBundleDelay)!.Value;
            order.Add(head.JobId);
            Assert.NotNull(claims.Claim(_projectKey, "owner", AfterTheBaselineBundleDelay, TimeSpan.FromMinutes(5)));
        }

        Assert.Equal([trial, candidate, prebuild], order);
    }

    // The prebuild has no not-before, so any later instant claims it; a fixed one keeps the tests clock-free.
    private const string AfterTheBaselineBundleDelay = "2999-01-01T00:00:00Z";

    [Fact]
    public void TheBaselineBundleHasNoNotBeforeAndIsClaimableAtOnce()
    {
        var parserPath = FakeParser.CopyRecordingInvocations(Path.Combine(_root, "parser-" + Guid.NewGuid().ToString("N")));
        var prebuild = ParsimonyAutoRun.EnqueueBaselineBundle(_database, _projectKey, _token, parserPath)!;
        var claims = new JobClaims(_database, parserKey: ParserIdentity.KeyOf(parserPath));

        Assert.Null(_jobs.Get(prebuild)!.NotBeforeUtc);
        Assert.Equal(prebuild, claims.PeekHead(_projectKey, JobTimestamp.FormatUtc(DateTimeOffset.UtcNow))!.Value.JobId);
    }

    [Fact]
    public void ATrialQueuedWhileTheBaselineBundleWaitsIsClaimedBeforeIt()
    {
        var parserPath = FakeParser.CopyRecordingInvocations(Path.Combine(_root, "parser-" + Guid.NewGuid().ToString("N")));
        var prebuild = ParsimonyAutoRun.EnqueueBaselineBundle(_database, _projectKey, _token, parserPath)!;
        var trial = _jobs.Create(CanonicalId.Mint("job/").Value, _projectKey, TrialJobHandler.TrialKind, "{}",
            JobTimestamp.FormatUtc(DateTimeOffset.UtcNow)).JobId;
        var claims = new JobClaims(_database, parserKey: ParserIdentity.KeyOf(parserPath));

        var first = claims.Claim(_projectKey, "owner", AfterTheBaselineBundleDelay, TimeSpan.FromMinutes(5));

        Assert.Equal(trial, first!.JobId);
        Assert.Equal(prebuild, claims.PeekHead(_projectKey, AfterTheBaselineBundleDelay)!.Value.JobId);
    }

    [Fact]
    public async Task ACaptureQueuesTheBaselineBundleOnlyAfterItCompletes()
    {
        var parserPath = FakeParser.CopyRecordingInvocations(Path.Combine(_root, "parser-" + Guid.NewGuid().ToString("N")));
        long BundleJobs()
        {
            using var connection = _database.OpenConnection();
            using var count = connection.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM Jobs WHERE Kind = $kind;";
            count.Parameters.AddWithValue("$kind", ParsimonyJobHandler.JobKind);
            return (long)count.ExecuteScalar()!;
        }
        long duringCapture = -1;
        var handler = ProjectJobHandlers.WithBaselineBundlePrebuildAfter(
            (_, _) => { if (duringCapture < 0) duringCapture = BundleJobs(); return Task.FromResult<JobOutcome?>(new JobOutcome(JobStatus.Completed)); },
            () => _ = ParsimonyAutoRun.EnqueueBaselineBundle(_database, _projectKey, _token, parserPath));

        await handler(null!, CancellationToken.None);
        await handler(null!, CancellationToken.None);

        Assert.Equal(0, duringCapture);
        Assert.Equal(1, BundleJobs());
    }

    [Fact]
    public async Task AFailedCaptureQueuesNoBaselineBundle()
    {
        var enqueued = 0;
        var handler = ProjectJobHandlers.WithBaselineBundlePrebuildAfter(
            (_, _) => Task.FromResult<JobOutcome?>(new JobOutcome(JobStatus.Failed)), () => enqueued++);

        await handler(null!, CancellationToken.None);

        Assert.Equal(0, enqueued);
    }

    [Fact]
    public async Task AFailingBaselineBundleEnqueueDoesNotFailTheCapture()
    {
        var expected = new JobOutcome(JobStatus.Completed);
        var handler = ProjectJobHandlers.WithBaselineBundlePrebuildAfter(
            (_, _) => Task.FromResult<JobOutcome?>(expected), () => throw new IOException("x"));

        Assert.Same(expected, await handler(null!, CancellationToken.None));
    }

    [Fact]
    public async Task ADryRunQueuesTheBaselineBundleExactlyOnce()
    {
        var parserPath = FakeParser.CopyRecordingInvocations(Path.Combine(_root, "parser-" + Guid.NewGuid().ToString("N")));
        var dryRuns = 0;
        var handler = ProjectJobHandlers.WithBaselineBundlePrebuild(
            (_, _) => { dryRuns++; return Task.FromResult<JobOutcome?>(null); },
            () => _ = ParsimonyAutoRun.EnqueueBaselineBundle(_database, _projectKey, _token, parserPath));

        await handler(null!, CancellationToken.None);
        await handler(null!, CancellationToken.None);

        Assert.Equal(2, dryRuns);
        using var connection = _database.OpenConnection();
        using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM Jobs WHERE Kind = $kind AND json_extract(InputJson, '$.note') = $note;";
        count.Parameters.AddWithValue("$kind", ParsimonyJobHandler.JobKind);
        count.Parameters.AddWithValue("$note", ParsimonyAutoRun.BaselineBundleNote(_token));
        Assert.Equal(1L, count.ExecuteScalar());
    }

    [Fact]
    public async Task AFailingBaselineBundleEnqueueDoesNotFailTheDryRun()
    {
        var expected = new JobOutcome(JobStatus.Completed);
        foreach (Exception failure in new Exception[]
                 { new InvalidOperationException("x"), new IOException("x"), new Microsoft.Data.Sqlite.SqliteException("x", 5) })
        {
            var handler = ProjectJobHandlers.WithBaselineBundlePrebuild(
                (_, _) => Task.FromResult<JobOutcome?>(expected), () => throw failure);

            Assert.Same(expected, await handler(null!, CancellationToken.None));
        }
    }

    [Fact]
    public async Task ABaselineBundleForAReplacedBaselineIsSkippedWhenItRuns()
    {
        var parserPath = FakeParser.CopyRecordingInvocations(Path.Combine(_root, "parser-" + Guid.NewGuid().ToString("N")));
        var other = new BaselineToken(_token.ProjectIdentity, "sha256:" + new string('5', 64), _token.ProjectionVersion,
            _token.CapturedUtc, "sha256:" + new string('6', 64));
        var jobId = ParsimonyAutoRun.EnqueueBaselineBundle(_database, _projectKey, other, parserPath)!;
        var claimed = new JobClaims(_database, parserKey: ParserIdentity.KeyOf(parserPath)).Claim(_projectKey,
            "candidate-evidence-test", AfterTheBaselineBundleDelay, TimeSpan.FromMinutes(5));
        Assert.Equal(jobId, claimed!.JobId);
        using var invoker = new PanGlossInvoker(parserPath);
        var handler = RetainedFacts.Handler(_database, _baselines, _projectKey, _project,
            new RunnerOptions { Root = Path.Combine(_root, "worker"), ParserPath = parserPath }, invoker);

        var outcome = await handler.RunAsync(new ClaimedJob(_jobs, claimed), CancellationToken.None);

        Assert.Equal(JobStatus.Completed, outcome!.Status);
        Assert.Contains("stale-baseline", outcome.ResultJson, StringComparison.Ordinal);
        Assert.Empty(FakeParser.Invocations(parserPath));
    }

    public static TheoryData<string, bool> SideFailures => new()
    {
        { "baseline-refused", false }, { "baseline-cancelled", false }, { "candidate-refused", false },
        { "baseline-refused", true }, { "baseline-cancelled", true }, { "candidate-refused", true },
    };

    [Theory]
    [MemberData(nameof(SideFailures))]
    public async Task AFailedOrCancelledSideEndsTheJobAsInSequenceAndLeavesNothingBehind(string failure, bool _)
    {
        var unobserved = new List<string>();
        // The event is process-wide, so only this test's own failures count.
        void Watch(object? sender, UnobservedTaskExceptionEventArgs args)
        {
            var text = args.Exception.ToString();
            if (text.Contains("boom", StringComparison.Ordinal) || text.Contains(nameof(CandidateEvidenceBuilder), StringComparison.Ordinal))
                lock (unobserved) unobserved.Add(text);
        }
        TaskScheduler.UnobservedTaskException += Watch;
        try
        {
            var (concurrent, _, _) = await RunCandidateAsync((invoker, _) => new SideFailingInvoker(invoker, failure));
            for (var i = 0; i < 3; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            var (serial, _, _) = await RunCandidateAsync((invoker, _) => new SideFailingInvoker(invoker, failure),
                serialSides: true);

            Assert.NotEqual(JobStatus.Completed, concurrent!.Status);
            Assert.Equal(serial!.Status, concurrent.Status);
            Assert.Equal(serial.Category, concurrent.Category);
            Assert.Equal(serial.ResultJson, concurrent.ResultJson);
            Assert.Empty(new EvidenceArtifactPublisher(Path.Combine(_root, "worker"), _projectKey)
                .ListWorkingDirectories());
            Assert.True(unobserved.Count == 0, string.Join("\n", unobserved));
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= Watch;
        }
    }

    private static void AssertBothSidesBuilt(IEnumerable<string> commands) =>
        Assert.Equal(["grammar-health", "grammar-health"], commands.Order(StringComparer.Ordinal));

    private string[] CandidateCommands(string parserPath) => [.. FakeParser.Invocations(parserPath)
        .Skip(_invocationsBeforeCandidate).Where(command => command != "describe")];

    [Fact]
    public async Task ASecondRunOnADifferentSelectionDoesNotReuseTheFirstRunsBeforeSide()
    {
        var proposal = DisableProhibitionProposal(_prohibitionGuid);
        var (firstOutcome, firstClaim, parserPath) = await RunCandidateAsync((invoker, _) => invoker,
            proposalText: proposal);
        var first = Assert.IsType<JobOutcome>(firstOutcome);
        firstClaim.Transition(first.Status, first.Category, first.ResultJson);
        var selection = new ParsimonySelectionSnapshot("other", [], [], [], "sha256:" + new string('7', 64));

        var (secondOutcome, _, _) = await RunCandidateAsync((invoker, _) => invoker, proposalText: proposal,
            existingParserPath: parserPath,
            candidateScope: new ParsimonyScopeBinding(ParsimonyEvidenceScopeKind.DefaultSelection, selection));

        Assert.True(Assert.IsType<JobOutcome>(secondOutcome).Status == JobStatus.Completed);
        AssertBothSidesBuilt(CandidateCommands(parserPath));
    }

    [Fact]
    public async Task ABaselineJobDoesNotReuseTheBeforeSideACandidateJobBuilt()
    {
        var proposal = DisableProhibitionProposal(_prohibitionGuid);
        var (firstOutcome, firstClaim, parserPath) = await RunCandidateAsync((invoker, _) => invoker,
            proposalText: proposal);
        var first = Assert.IsType<JobOutcome>(firstOutcome);
        firstClaim.Transition(first.Status, first.Category, first.ResultJson);
        var before = FakeParser.Invocations(parserPath).Count;

        using var invoker = new PanGlossInvoker(parserPath);
        await RunBaselineReportAsync(invoker, parserPath,
            new ParsimonyScopeBinding(ParsimonyEvidenceScopeKind.ProjectApproved, null), null);

        Assert.Equal(["grammar-health"], FakeParser.Invocations(parserPath).Skip(before)
            .Where(command => command != "describe"));
    }

    [Fact]
    public async Task ASecondRunRebuildsWhenTheStoredBeforeSideIsCorrupt()
    {
        var proposal = DisableProhibitionProposal(_prohibitionGuid);
        var (firstOutcome, firstClaim, parserPath) = await RunCandidateAsync((invoker, _) => invoker,
            proposalText: proposal);
        var first = Assert.IsType<JobOutcome>(firstOutcome);
        firstClaim.Transition(first.Status, first.Category, first.ResultJson);
        var stored = new EvidenceArtifactRepository(_database).ListAvailableBaselineBundles(_projectKey,
            EvidenceBundlePipeline.RemoveDigestPrefix(_token.BundleDigest)).Single();
        using (var stream = new FileStream(stored.EvidencePath, FileMode.Append, FileAccess.Write))
            stream.WriteByte(0);

        var (secondOutcome, _, _) = await RunCandidateAsync((invoker, _) => invoker, proposalText: proposal,
            existingParserPath: parserPath);

        Assert.True(Assert.IsType<JobOutcome>(secondOutcome).Status == JobStatus.Completed);
        AssertBothSidesBuilt(CandidateCommands(parserPath));
    }

    [Fact]
    public async Task ARetirementTranslationJobBuildsBothSidesAndDoesNotReuseTheStoredBeforeSide()
    {
        var proposal = DisableProhibitionProposal(_prohibitionGuid);
        var (firstOutcome, firstClaim, parserPath) = await RunCandidateAsync((invoker, _) => invoker,
            proposalText: proposal);
        var first = Assert.IsType<JobOutcome>(firstOutcome);
        firstClaim.Transition(first.Status, first.Category, first.ResultJson);
        var digest = "sha256:" + new string('3', 64);
        FrozenExpectationSet Frozen() => new(_token, digest, digest, "revision-1", [], [], []);
        var mappings = new[]
        {
            new RetirementExpectationMapping(CanonicalId.Mint("form/").Value, CanonicalId.Mint("msa/").Value, null,
                "stem", CanonicalId.Mint("form/").Value),
        };
        var translation = new RetirementExpectationTranslation(Frozen(), Frozen(), mappings,
            [new RetirementBundleTextEffect(CanonicalId.Mint("bundle/").Value, new Dictionary<string, string>(),
                new Dictionary<string, string>())],
            RetirementExpectationTranslationCodec.ComputeMappingDigest(mappings), digest, digest, digest);

        var (secondOutcome, _, _) = await RunCandidateAsync((invoker, _) => invoker, proposalText: proposal,
            existingParserPath: parserPath, translation: translation);

        var second = Assert.IsType<JobOutcome>(secondOutcome);
        Assert.True(second.Status == JobStatus.Completed, second.ResultJson);
        AssertBothSidesBuilt(CandidateCommands(parserPath));
    }

    private static readonly HashSet<string> PerRun = new(
        ["reportId", "bundleId", "stageTimings", "dryRunJobId", "identitySha256", "candidate"],
        StringComparer.OrdinalIgnoreCase);

    // The before side, the changes and the after findings; the after digests carry a per-run identity.
    private static string Answer(string resultJson)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(resultJson)!.AsObject();
        var picked = new System.Text.Json.Nodes.JsonObject();
        foreach (var (name, value) in node)
        {
            if (name.Equals("before", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("findingChanges", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("approvedAnalysisPreservation", StringComparison.OrdinalIgnoreCase))
                picked[name] = value?.DeepClone();
            else if (name.Equals("after", StringComparison.OrdinalIgnoreCase))
                picked[name] = value!.AsObject().First(pair => pair.Key.Equals("findings",
                    StringComparison.OrdinalIgnoreCase)).Value?.DeepClone();
            else if (name.Equals("moreMeasures", StringComparison.OrdinalIgnoreCase) && value is not null)
                picked[name] = new System.Text.Json.Nodes.JsonArray([.. value.AsArray().Select(item =>
                    (System.Text.Json.Nodes.JsonNode?)new System.Text.Json.Nodes.JsonObject(item!.AsObject()
                        .Where(pair => !pair.Key.Equals("after", StringComparison.OrdinalIgnoreCase))
                        .Select(pair => KeyValuePair.Create(pair.Key, pair.Value?.DeepClone()))))]);
        }
        Strip(picked);
        return picked.ToJsonString();

        static void Strip(System.Text.Json.Nodes.JsonNode? item)
        {
            if (item is System.Text.Json.Nodes.JsonObject map)
            {
                foreach (var name in map.Select(pair => pair.Key).Where(key => PerRun.Contains(key)).ToArray())
                    map.Remove(name);
                foreach (var child in map.Select(pair => pair.Value).ToArray()) Strip(child);
            }
            else if (item is System.Text.Json.Nodes.JsonArray list)
                foreach (var child in list) Strip(child);
        }
    }

    /// <summary>Fails or cancels the facts run for one side of the comparison.</summary>
    private sealed class SideFailingInvoker(PanGlossInvoker inner, string failure) : IPanGlossInvoker
    {
        public Task<PanGlossOutcome> RunAsync(PanGlossRequest request, string label,
            CancellationToken cancellationToken, TimeSpan? wallClockCap = null)
        {
            if (request is PanGlossRequest.GrammarHealth facts)
            {
                var baseline = facts.FactsContextJson!.Contains("\"inputKind\":\"baseline\"", StringComparison.Ordinal);
                if (failure == "baseline-refused" && baseline || failure == "candidate-refused" && !baseline)
                    return Task.FromResult<PanGlossOutcome>(new PanGlossOutcome.Refused(1, "boom", "", "boom"));
                if (failure == "baseline-cancelled" && baseline)
                    return Task.FromResult<PanGlossOutcome>(new PanGlossOutcome.Cancelled());
            }
            return inner.RunAsync(request, label, cancellationToken, wallClockCap);
        }
    }

    /// <summary>Atomic replacement preserves any running executable inode.</summary>
    private sealed class ParserReplacingInvoker(PanGlossInvoker inner, string parserPath) : IPanGlossInvoker
    {
        private int _factsRuns;

        public Task<PanGlossOutcome> RunAsync(PanGlossRequest request, string label,
            CancellationToken cancellationToken, TimeSpan? wallClockCap = null)
        {
            if (request is PanGlossRequest.GrammarHealth && ++_factsRuns == 2)
            {
                var replacement = parserPath + ".replacement";
                File.Copy(parserPath, replacement);
                using (var stream = new FileStream(replacement, FileMode.Append, FileAccess.Write))
                    stream.WriteByte(0);
                File.Move(replacement, parserPath, overwrite: true);
            }
            return inner.RunAsync(request, label, cancellationToken, wallClockCap);
        }
    }

    private static string Proposal(CanonicalId proposalId, Guid targetId, string text,
        IReadOnlyList<string>? requires = null)
    {
        var requiresJson = requires is null ? "" :
            "\"requires\": [" + string.Join(",", requires.Select(id => JsonSerializer.Serialize(id))) + "],";
        var operationId = CanonicalId.Mint("operation/").Value;
        var target = CanonicalId.FromGuid(targetId).Value;
        var textJson = JsonSerializer.Serialize(text);
        return $$"""
            {
              "contractVersions": {"lexical": "1.0"},
              "proposalId": "{{proposalId.Value}}",
              {{requiresJson}}
              "operations": [
                {
                  "operationId": "{{operationId}}",
                  "kind": "lexical/lexSense/setGloss",
                  "target": "{{target}}",
                  "after": {"ws": "en", "text": {{textJson}}}
                }
              ]
            }
            """;
    }

    private sealed record AdhocIds(Guid ProhibitionGuid);

    private Guid SeedAdhocProhibition(LcmCache cache)
    {
        Guid prohibitionGuid = default;
        var services = cache.ServiceLocator;
        var entries = services.GetInstance<ILexEntryRepository>();
        var first = entries.GetObject(_seed.FirstEntryId);
        var second = entries.GetObject(_seed.SecondEntryId);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var prohibition = services.GetInstance<IMoAlloAdhocProhibFactory>().Create();
            cache.LangProject.MorphologicalDataOA.AdhocCoProhibitionsOC.Add(prohibition);
            prohibition.FirstAllomorphRA = first.LexemeFormOA;
            prohibition.RestOfAllosRS.Add(second.LexemeFormOA);
            prohibitionGuid = prohibition.Guid;
        });
        return prohibitionGuid;
    }

    private string DisableProhibitionProposal(Guid targetId)
    {
        var proposalId = CanonicalId.Mint("proposal/").Value;
        var operationId = CanonicalId.Mint("operation/").Value;
        var target = CanonicalId.FromGuid(targetId).Value;
        return $$"""
            {
              "contractVersions": {"grammar": "1.0"},
              "proposalId": "{{proposalId}}",
              "operations": [
                {
                  "operationId": "{{operationId}}",
                  "kind": "grammar/moAdhocProhib/setDisabled",
                  "target": "{{target}}",
                  "after": {"value": true}
                }
              ]
            }
            """;
    }

    private object FactsFixture(bool disableChangedRule)
    {
        var primary = _seed.FirstLexemeFormId.ToString("D").ToLowerInvariant();
        var other = _seed.SecondLexemeFormId.ToString("D").ToLowerInvariant();
        var ids = new[] { _prohibitionGuid.ToString("D").ToLowerInvariant(),
            "30000000-0000-0000-0000-000000000001", "30000000-0000-0000-0000-000000000002" };
        return new
        {
            prohibitions = ids.Select((id, index) => new
            {
                prohibitionGuid = id,
                kind = "allomorph",
                disabled = index == 0 && disableChangedRule,
                adjacency = "somewhereToLeft",
                primaryGuid = primary,
                targetKind = "allomorph",
                others = new[] { new { targetGuid = other, targetKind = "allomorph" } },
                loaded = true,
                compacted = false,
            }).ToArray(),
            groups = new[]
            {
                new
                {
                    groupGuid = "40000000-0000-0000-0000-000000000001",
                    texts = new[]
                    {
                        new { field = "name", writingSystem = "qaa", text = "Fixture group" },
                        new
                        {
                            field = "description", writingSystem = "qaa", text = "one authored group rationale",
                        },
                    },
                    members = ids,
                },
            },
        };
    }

    private ClaimedJob Claim(string jobId)
    {
        var claimed = new JobClaims(_database).Claim(_projectKey, "candidate-evidence-test",
            JobTimestamp.FormatUtc(DateTimeOffset.UtcNow), TimeSpan.FromMinutes(5));
        Assert.NotNull(claimed);
        Assert.Equal(jobId, claimed.JobId);
        return new ClaimedJob(_jobs, claimed);
    }

    private static string ReadText(string path, string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(path),
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture)!;
    }

    private static string DigestFile(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private static string DigestDirectory(string root) => string.Join("\n",
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/') + ":" + DigestFile(path)));
}
