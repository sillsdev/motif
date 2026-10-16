using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Store;
using SIL.Motif.Host.Texts;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class TrialsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SIL.Motif.TrialsTests",
        Guid.NewGuid().ToString("N"));

    public TrialsTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void LatestForReadsTheNewestCompletedTrialOfTheProposal()
    {
        using var fixture = new ProjectFixture(_root);
        var proposal = fixture.SaveProposal();
        fixture.RecordAssessment(proposal, "correctness", AssessmentKinds.Correctness);
        var first = fixture.CreateJob(proposal.Json, "2026-09-24T11:00:00Z", "first",
            ["correctness"], allWords: false);
        var latest = fixture.CreateJob(proposal.Json, "2026-09-24T12:00:00Z", "latest",
            ["correctness"], allWords: true);

        var trial = Trials.LatestFor(fixture.Database, fixture.Project, proposal.Id.Value);

        Assert.NotNull(trial);
        Assert.Equal(latest, trial.JobId);
        Assert.NotEqual(first, trial.JobId);
        Assert.Equal(proposal.Digest, trial.IntentDigest);
        Assert.Equal("correctness", trial.AssessmentIds[AssessmentKinds.Correctness]);
        Assert.Equal(["cat"], trial.Words);
        Assert.True(trial.AllWords);
        Assert.Equal(proposal.Digest, trial.DryRun!.IntentDigest);
        Assert.Equal(TrialFreshness.Current, trial.Freshness);
    }

    [Theory]
    [InlineData(JobStatus.Failed, JobFailureCategory.Unknown)]
    [InlineData(JobStatus.Cancelled, JobFailureCategory.Cancellation)]
    public void LatestForKeepsTheLastCompletedTrialAfterANewerFailure(JobStatus terminalStatus,
        JobFailureCategory failureCategory)
    {
        using var fixture = new ProjectFixture(_root);
        var proposal = fixture.SaveProposal();
        fixture.RecordAssessment(proposal, "parse-time", AssessmentKinds.ParseTime);
        var completed = fixture.CreateJob(proposal.Json, "2026-09-24T11:00:00Z", "completed", ["parse-time"]);
        fixture.CreateTerminalJob(proposal.Json, "2026-09-24T12:00:00Z", "newer", terminalStatus,
            failureCategory);

        var trial = Trials.LatestFor(fixture.Database, fixture.Project, proposal.Id.Value);

        Assert.Equal(completed, trial!.JobId);
        Assert.Equal(JobStatus.Completed, trial.Status);
    }

    [Fact]
    public void LatestForKeepsTheNewestFailureWhenNoCompletedTrialExists()
    {
        using var fixture = new ProjectFixture(_root);
        var proposal = fixture.SaveProposal();
        fixture.CreateTerminalJob(proposal.Json, "2026-09-24T11:00:00Z", "first", JobStatus.Failed,
            JobFailureCategory.Unknown);
        fixture.CreateTerminalJob(proposal.Json, "2026-09-24T12:00:00Z", "newest", JobStatus.Cancelled,
            JobFailureCategory.Cancellation);

        var trial = Trials.LatestFor(fixture.Database, fixture.Project, proposal.Id.Value);

        Assert.Equal("newest", trial!.JobId);
        Assert.Equal(JobStatus.Cancelled, trial.Status);
    }

    [Fact]
    public void FreshnessIsBaselineMovedWhenTheCurrentBaselineTokenDiffers()
    {
        using var fixture = new ProjectFixture(_root);
        var proposal = fixture.SaveProposal();
        fixture.CreateJob(proposal.Json, "2026-09-24T11:00:00Z", "trial", []);
        fixture.RecordBaseline("sha256:" + new string('c', 64), "2026-09-24T13:00:00Z");

        var trial = Trials.LatestFor(fixture.Database, fixture.Project, proposal.Id.Value);

        Assert.Equal(TrialFreshness.BaselineMoved, trial!.Freshness);
    }

    [Fact]
    public void FreshnessIsProposalChangedWhenTheDraftDigestDiffers()
    {
        using var fixture = new ProjectFixture(_root);
        var id = CanonicalId.Mint("proposal/");
        var before = Draft(id, "before");
        var beforeJson = ProposalCommands.BuildProposalJson(before);
        var digest = IntentDigest.Compute(ProposalJsonParser.Parse(beforeJson));
        var proposals = new ProposalRepository(fixture.Database);
        proposals.CreateDraft("draft", id, JsonSerializer.Serialize(before, DraftOptions));
        fixture.CreateJob(beforeJson, "2026-09-24T11:00:00Z", "trial", []);
        proposals.SaveDraft("draft", JsonSerializer.Serialize(Draft(id, "after"), DraftOptions));

        var trial = Trials.LatestFor(fixture.Database, fixture.Project, id.Value);

        Assert.Equal(digest, trial!.IntentDigest);
        Assert.Equal(TrialFreshness.ProposalChanged, trial.Freshness);
    }

    [Fact]
    public void FreshnessIsIncompleteWhileTheJobRuns()
    {
        using var fixture = new ProjectFixture(_root);
        var proposal = fixture.SaveProposal();
        fixture.CreateRunningJob(proposal.Json, "2026-09-24T11:00:00Z", "running");

        var trial = Trials.LatestFor(fixture.Database, fixture.Project, proposal.Id.Value);

        Assert.Equal(TrialFreshness.Incomplete, trial!.Freshness);
        Assert.Equal(JobStatus.Running, trial.Status);
        Assert.Null(trial.CompletedUtc);
    }

    [Fact]
    public void AProposalWithNoTrialIsNotTried()
    {
        using var fixture = new ProjectFixture(_root);
        var proposal = fixture.SaveProposal();

        Assert.Equal(TrialStanding.NotTried,
            TrialStanding.Of(Trials.LatestFor(fixture.Database, fixture.Project, proposal.Id.Value)));
        Assert.False(Trials.LatestForAll(fixture.Database, fixture.Project).ContainsKey(proposal.Id.Value));
    }

    [Fact]
    public void ARunningTrialIsTryingAndCannotBeTriedAgain()
    {
        using var fixture = new ProjectFixture(_root);
        var proposal = fixture.SaveProposal();
        fixture.RecordAssessment(proposal, "parse-time", AssessmentKinds.ParseTime);
        fixture.CreateJob(proposal.Json, "2026-09-24T11:00:00Z", "completed", ["parse-time"]);
        fixture.CreateRunningJob(proposal.Json, "2026-09-24T12:00:00Z", "running");

        var trial = Trials.LatestFor(fixture.Database, fixture.Project, proposal.Id.Value);

        Assert.Equal("running", trial!.JobId);
        Assert.Equal(TrialStanding.Trying, trial.Standing);
        Assert.False(trial.Standing.CanTryAgain);
        Assert.False(trial.HasComparableNumbers);
    }

    [Fact]
    public void ATrialThatOnlyFailedIsFailed()
    {
        using var fixture = new ProjectFixture(_root);
        var proposal = fixture.SaveProposal();
        fixture.CreateTerminalJob(proposal.Json, "2026-09-24T11:00:00Z", "failed", JobStatus.Failed,
            JobFailureCategory.Unknown);

        var trial = Trials.LatestFor(fixture.Database, fixture.Project, proposal.Id.Value);

        Assert.Equal(TrialStanding.Failed, trial!.Standing);
        Assert.True(trial.Standing.CanTryAgain);
    }

    [Fact]
    public void AFailedRetryAfterAGoodTrialKeepsTheGoodTrialAndItsStandingInBothReads()
    {
        using var fixture = new ProjectFixture(_root);
        var proposal = fixture.SaveProposal();
        fixture.RecordAssessment(proposal, "parse-time", AssessmentKinds.ParseTime);
        fixture.CreateJob(proposal.Json, "2026-09-24T11:00:00Z", "good", ["parse-time"]);
        fixture.CreateTerminalJob(proposal.Json, "2026-09-24T12:00:00Z", "retry", JobStatus.Failed,
            JobFailureCategory.Unknown);

        var one = Trials.LatestFor(fixture.Database, fixture.Project, proposal.Id.Value);
        var all = Trials.LatestForAll(fixture.Database, fixture.Project)[proposal.Id.Value];

        Assert.Equal("good", one!.JobId);
        Assert.Equal("good", all.JobId);
        Assert.Equal(TrialStanding.Current, one.Standing);
        Assert.Equal(one.Standing, all.Standing);
        Assert.True(one.HasComparableNumbers);
    }

    [Fact]
    public void AStaleTrialNeedsAnotherTryAndSaysWhy()
    {
        using var fixture = new ProjectFixture(_root);
        var proposal = fixture.SaveProposal();
        fixture.RecordAssessment(proposal, "parse-time", AssessmentKinds.ParseTime);
        fixture.CreateJob(proposal.Json, "2026-09-24T11:00:00Z", "trial", ["parse-time"]);
        fixture.RecordBaseline("sha256:" + new string('c', 64), "2026-09-24T13:00:00Z");

        var trial = Trials.LatestFor(fixture.Database, fixture.Project, proposal.Id.Value);

        Assert.Equal(TrialStanding.NeedsAnotherTry(TrialStaleReason.BaselineMoved), trial!.Standing);
        Assert.True(trial.HasComparableNumbers);
    }

    [Fact]
    public void LatestForAllChoosesEachProposalsTrialAsLatestForDoes()
    {
        using var fixture = new ProjectFixture(_root);
        var first = fixture.SaveProposal();
        var second = fixture.SaveProposal();
        var untried = fixture.SaveProposal();
        fixture.RecordAssessment(first, "first-parse-time", AssessmentKinds.ParseTime);
        fixture.CreateJob(first.Json, "2026-09-24T11:00:00Z", "first-good", ["first-parse-time"]);
        fixture.CreateTerminalJob(first.Json, "2026-09-24T12:00:00Z", "first-failed", JobStatus.Cancelled,
            JobFailureCategory.Cancellation);
        fixture.CreateTerminalJob(second.Json, "2026-09-24T11:30:00Z", "second-failed", JobStatus.Failed,
            JobFailureCategory.Unknown);
        fixture.CreateRunningJob(second.Json, "2026-09-24T12:30:00Z", "second-running");

        var all = Trials.LatestForAll(fixture.Database, fixture.Project);

        Assert.Equal(2, all.Count);
        foreach (var proposal in new[] { first, second })
        {
            var one = Trials.LatestFor(fixture.Database, fixture.Project, proposal.Id.Value);
            Assert.Equal(one!.JobId, all[proposal.Id.Value].JobId);
            Assert.Equal(one.Standing, all[proposal.Id.Value].Standing);
        }
        Assert.Equal("second-running", all[second.Id.Value].JobId);
        Assert.Equal(TrialStanding.NotTried, TrialStanding.Of(all.GetValueOrDefault(untried.Id.Value)));
    }

    [Fact]
    public void ATrialRecordsWhetherItMeasuredChosenWordsOrAllWords()
    {
        using var fixture = new ProjectFixture(_root);
        var chosen = fixture.SaveProposal();
        var all = fixture.SaveProposal();
        fixture.CreateJob(chosen.Json, "2026-09-24T11:00:00Z", "chosen", []);
        fixture.CreateJob(all.Json, "2026-09-24T11:00:00Z", "all", [], allWords: true);

        Assert.Equal(TrialWordScope.ChosenWords,
            Trials.LatestFor(fixture.Database, fixture.Project, chosen.Id.Value)!.WordScope);
        Assert.Equal(TrialWordScope.AllWords,
            Trials.LatestFor(fixture.Database, fixture.Project, all.Id.Value)!.WordScope);
    }

    [Fact]
    public void ATrialOfTheDefaultSelectionSaysSo()
    {
        using var fixture = new ProjectFixture(_root);
        var proposal = fixture.SaveProposal();
        fixture.CreateRunningJob(proposal.Json, "2026-09-24T11:00:00Z", "selection", words: null);

        Assert.Equal(TrialWordScope.DefaultSelection,
            Trials.LatestFor(fixture.Database, fixture.Project, proposal.Id.Value)!.WordScope);
    }

    [Fact]
    public void LatestForIsNullWithoutATrial()
    {
        using var fixture = new ProjectFixture(_root);

        Assert.Null(Trials.LatestFor(fixture.Database, fixture.Project, CanonicalId.Mint().Value));
    }

    [Fact]
    public void ForTrialBuildsASetWithTrialOriginAndTheSourceBaselineToken()
    {
        using var fixture = new ProjectFixture(_root);
        var proposal = fixture.SaveProposal();
        fixture.RecordAssessment(proposal, "parse-time", AssessmentKinds.ParseTime);
        fixture.RecordAssessment(proposal, "correctness", AssessmentKinds.Correctness);
        var jobId = fixture.CreateJob(proposal.Json, "2026-09-24T11:00:00Z", "trial",
            ["parse-time", "correctness"]);
        var trial = Trials.LatestFor(fixture.Database, fixture.Project, proposal.Id.Value)!;

        var result = EvidenceSets.ForTrial(fixture.Database, fixture.Project, trial);

        Assert.True(result.Succeeded, result.Refusal?.Message);
        Assert.Equal(fixture.Token, result.Value!.Baseline);
        Assert.Equal("parse-time", result.Value.RootAssessmentId);
        Assert.Equal("correctness", result.Value.MeasurementIds[AssessmentKinds.Correctness]);
        Assert.Equal(EvidenceOriginKind.Trial, result.Value.Origin.Kind);
        Assert.Equal(proposal.Id.Value, result.Value.Origin.ProposalId);
        Assert.Equal(proposal.Digest, result.Value.Origin.IntentDigest);
        Assert.Equal(jobId, result.Value.Origin.JobId);
    }

    [Fact]
    public void TrialWordResultsAreLoadedOnlyWhenRequested()
    {
        using var fixture = new ProjectFixture(_root);
        var proposal = fixture.SaveProposal();
        fixture.RecordAssessment(proposal, "parse-time", AssessmentKinds.ParseTime,
            projectStanding: ProjectStanding.Approved);
        var tokenJson = JsonSerializer.Serialize(fixture.Token, MotifJson.CreateOptions());
        var grammar = new GrammarCheckResponse([], HasBaseline: true);
        new TrialGrammarCheckRepository(fixture.Database).Save(fixture.Token, proposal.Id, proposal.Digest,
            "sha256:selection", grammar);
        fixture.CreateJob(proposal.Json, "2026-09-24T11:00:00Z", "trial", ["parse-time"]);

        var summary = Trials.LatestFor(fixture.Database, fixture.Project, proposal.Id.Value)!;
        var read = Trials.Read(new ReadTrialRequest(fixture.Project.FullFwDataPath, proposal.Id.Value,
            IncludeWordResults: true));
        Assert.True(read.Succeeded, read.Refusal?.Message);
        var trial = read.Value!;
        var set = EvidenceSets.ForTrial(fixture.Database, fixture.Project, trial);

        Assert.Empty(summary.WordResults);
        Assert.Equal(ProjectStanding.Approved, Assert.Single(trial.WordResults).ProjectStanding);
        Assert.Equal("trial:" + proposal.Id.Value + ":" + proposal.Digest, trial.GrammarCheckId);
        Assert.Equal(trial.GrammarCheckId, set.Value!.GrammarCheckId);
        Assert.Null(new GrammarCheckRepository(fixture.Database).GetLatest(tokenJson));
    }

    [Fact]
    public void TrialGrammarChecksRemainScopedToTheDigestThatProducedThem()
    {
        using var fixture = new ProjectFixture(_root);
        var first = fixture.SaveProposal();
        var secondJson = ProposalCommands.BuildProposalJson(Draft(first.Id, "second revision"));
        var secondDigest = IntentDigest.Compute(ProposalJsonParser.Parse(secondJson));
        var second = new SavedProposal(first.Id, secondJson, secondDigest);
        fixture.RecordAssessment(first, "root/first", AssessmentKinds.ParseTime);
        fixture.RecordAssessment(second, "root/second", AssessmentKinds.ParseTime);
        fixture.CreateJob(first.Json, "2026-09-24T11:00:00Z", "trial/first", ["root/first"]);
        fixture.CreateJob(second.Json, "2026-09-24T12:00:00Z", "trial/second", ["root/second"]);
        var checks = new TrialGrammarCheckRepository(fixture.Database);
        checks.Save(fixture.Token, first.Id, first.Digest, "sha256:first-selection",
            new GrammarCheckResponse([], HasBaseline: true));
        checks.Save(fixture.Token, second.Id, second.Digest, "sha256:second-selection",
            new GrammarCheckResponse([], HasBaseline: false));
        var tokenJson = JsonSerializer.Serialize(fixture.Token, MotifJson.CreateOptions());

        var repository = new GrammarCheckRepository(fixture.Database);

        Assert.True(repository.GetLatest(tokenJson, first.Id.Value, first.Digest)!.HasBaseline);
        Assert.False(repository.GetLatest(tokenJson, second.Id.Value, second.Digest)!.HasBaseline);
        Assert.Null(repository.GetLatest(tokenJson, first.Id.Value, "sha256:unknown"));
        var trials = Trials.ListFor(fixture.Database, fixture.Project, first.Id.Value);
        Assert.Equal(2, trials.Count);
        Assert.Contains(trials, trial => trial.IntentDigest == first.Digest &&
            trial.GrammarCheckId == "trial:" + first.Id.Value + ":" + first.Digest);
        Assert.Contains(trials, trial => trial.IntentDigest == second.Digest &&
            trial.GrammarCheckId == "trial:" + second.Id.Value + ":" + second.Digest);
    }

    [Fact]
    public void LatestForSkipsATrialWhoseAssessmentRowsWerePruned()
    {
        using var fixture = new ProjectFixture(_root);
        var proposal = fixture.SaveProposal();
        fixture.RecordAssessment(proposal, "root/valid", AssessmentKinds.ParseTime);
        var validJob = fixture.CreateJob(proposal.Json, "2026-09-24T11:00:00Z", "trial/valid", ["root/valid"]);
        fixture.CreateJob(proposal.Json, "2026-09-24T12:00:00Z", "trial/pruned", ["root/pruned"]);

        var latest = Trials.LatestFor(fixture.Database, fixture.Project, proposal.Id.Value);

        Assert.Equal(validJob, latest!.JobId);
        Assert.Equal([validJob], Trials.ListFor(fixture.Database, fixture.Project, proposal.Id.Value)
            .Select(trial => trial.JobId));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private static JsonSerializerOptions DraftOptions { get; } = new()
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
                        .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal),
                },
            ],
        };
    }

    internal sealed class ProjectFixture : IDisposable
    {
        private readonly string _root;
        private readonly string _tokenJson;
        private readonly ProposalRepository _proposals;
        private readonly JobRepository _jobs;
        private readonly AssessmentRepository _assessments;
        private readonly Guid _textId = Guid.NewGuid();
        private readonly bool _includeIndexedText;
        private readonly bool _includeSecondWord;

        public ProjectFixture(string testRoot, bool includeIndexedText = false, bool includeSecondWord = false)
        {
            _includeIndexedText = includeIndexedText;
            _includeSecondWord = includeSecondWord;
            _root = Path.Combine(testRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            var path = Path.Combine(_root, "project.fwdata");
            File.WriteAllText(path, "synthetic project marker");
            Project = new ProjectLocator(path, "project");
            Database = MotifDatabase.OpenOwned(Path.Combine(_root, "project.motif.db"), Project,
                MotifSchema.CurrentSchema, new Version(1, 0));
            Token = new BaselineToken("project-id", "sha256:" + new string('1', 64), "projection-v1",
                "2026-09-24T10:00:00Z", "sha256:" + new string('a', 64));
            _tokenJson = JsonSerializer.Serialize(Token, MotifJson.CreateOptions());
            _proposals = new ProposalRepository(Database);
            _jobs = new JobRepository(Database);
            _assessments = new AssessmentRepository(Database);
            RecordBaseline(Token.BundleDigest, Token.CapturedUtc);
        }

        public ProjectLocator Project { get; }
        public MotifDatabase Database { get; }
        public BaselineToken Token { get; }
        public Guid TextId => _textId;

        public void SetDefaultSelection(IReadOnlyList<Guid> textIds, IReadOnlyList<string> addedWords) =>
            new NamedSelectionRepository(Database).SetDefault("Default", textIds, addedWords,
                new SelectionParsingLimits(StepCap.Default, SelectionTimeLimitMode.Estimated, null),
                expectedRevision: null);

        public SavedProposal SaveProposal()
        {
            var id = CanonicalId.Mint("proposal/");
            var json = ProposalCommands.BuildProposalJson(Draft(id, "proposal"));
            var digest = IntentDigest.Compute(ProposalJsonParser.Parse(json));
            _proposals.SaveRevision(new ProposalRevisionRecord(id, digest, json, "proposed", null, null, null));
            return new SavedProposal(id, json, digest);
        }

        public BaselineToken RecordBaseline(string bundleDigest, string capturedUtc,
            DateTimeOffset? sourceLastWriteUtc = null, string? semanticDigest = null)
        {
            var token = new BaselineToken("project-id", semanticDigest ?? "sha256:" + new string('1', 64),
                "projection-v1", capturedUtc, bundleDigest);
            var baselineRoot = Path.Combine(_root, "baseline-" + capturedUtc.Replace(':', '-'));
            new BaselineRepository(Database).Record(ProjectWorkspaceKey.Compute(Project),
                new BaselinePublication(baselineRoot, Path.Combine(baselineRoot, "project.fwdata"), token),
                sourceLastWriteUtc ?? DateTimeOffset.Parse("2026-09-24T10:30:00Z"),
                DateTimeOffset.Parse(capturedUtc),
                _includeIndexedText
                    ? new TextWordsProjection([new TextWordsProjectedText(_textId, "Text", [], [])], [])
                    : TestTextWords.Empty,
                _includeSecondWord
                    ? new ProjectSummarySnapshot(2, 4, 2, 0, 0, ["cat", "dog"],
                        [new ProjectTextSummary(_textId, "Text", 2, 4,
                            new Dictionary<string, int>(StringComparer.Ordinal) { ["cat"] = 1, ["dog"] = 3 })])
                    : new ProjectSummarySnapshot(2, 2, 2, 0, 0, ["cat"],
                        [new ProjectTextSummary(_textId, "Text", 1, 1,
                            new Dictionary<string, int>(StringComparer.Ordinal) { ["cat"] = 1 })]));
            return token;
        }

        public string CreateJob(string proposalJson, string createdUtc, string jobId,
            IReadOnlyList<string> assessmentIds, bool allWords = false,
            IReadOnlyList<SIL.Motif.Model.Effects.ExpectedEffect>? effects = null, BaselineToken? baseline = null)
        {
            var job = CreateRunningJob(proposalJson, createdUtc, jobId, allWords);
            var dryRun = PublishedDryRun(ProposalJsonParser.Parse(proposalJson), effects);
            job = _jobs.PublishDryRun(job.JobId, dryRun, job.Version);
            var completion = JsonSerializer.Serialize(new
            {
                baselineToken = baseline ?? Token,
                assessmentIds,
                allWords,
            }, MotifJson.CreateOptions());
            _jobs.Transition(job.JobId, JobStatus.Completed, completion);
            return jobId;
        }

        public string CreateDefaultSelectionJob(string proposalJson, string createdUtc, string jobId,
            IReadOnlyList<string> assessmentIds)
        {
            var job = CreateRunningJob(proposalJson, createdUtc, jobId, null);
            var dryRun = PublishedDryRun(ProposalJsonParser.Parse(proposalJson));
            job = _jobs.PublishDryRun(job.JobId, dryRun, job.Version);
            var completion = JsonSerializer.Serialize(new { baselineToken = Token, assessmentIds, allWords = false },
                MotifJson.CreateOptions());
            _jobs.Transition(job.JobId, JobStatus.Completed, completion);
            return jobId;
        }

        public JobRecord CreateRunningJob(string proposalJson, string createdUtc, string jobId, bool allWords = false)
            => CreateRunningJob(proposalJson, createdUtc, jobId, ["cat"], allWords);

        public JobRecord CreateRunningJob(string proposalJson, string createdUtc, string jobId,
            IReadOnlyList<string>? words, bool allWords = false)
        {
            var input = JsonSerializer.Serialize(new
            {
                proposalJson,
                scope = (string?)null,
                words,
                allWords,
                limits = (TrialLimits?)null,
            }, MotifJson.CreateOptions());
            var job = _jobs.Create(jobId, ProjectWorkspaceKey.Compute(Project), TrialJobHandler.TrialKind,
                input, createdUtc);
            return _jobs.Transition(job.JobId, JobStatus.Running);
        }

        public JobRecord CreateTerminalJob(string proposalJson, string createdUtc, string jobId,
            JobStatus terminalStatus, JobFailureCategory failureCategory)
        {
            var job = CreateRunningJob(proposalJson, createdUtc, jobId);
            return _jobs.Transition(job.JobId, terminalStatus, job.Version, failureCategory);
        }

        public void RecordAssessment(SavedProposal proposal, string assessmentId, string kind,
            string outcome = "analysed", string? projectStanding = null)
        {
            _assessments.Record(new NewAssessmentRecord(
            AssessmentId: assessmentId,
            ProposalId: proposal.Id,
            ProposalIntentDigest: proposal.Digest,
            Assessor: "pangloss",
            Kind: kind,
            ScopeJson: "{}",
            ScopeDigest: "sha256:scope",
            Subject: new AssessmentSubject.Words(Selection.Create("Default", ["cat"]), "whitespace", "1"),
            BaselineToken: _tokenJson,
            OutcomeDigest: "sha256:outcome",
            SemanticDigest: "sha256:semantic",
            GrammarSourceSha256: "sha256:grammar",
            ModelFingerprint: "fingerprint",
            Pipeline: "pipeline",
            DiagnosticCount: 0,
            Material: new AssessmentRecordMaterial.Words([new AssessedWord("cat", outcome, []) { ProjectStanding = projectStanding }], null, null, []),
            SavedUtc: "2026-09-24T11:00:00Z"
        ));
        }

        public void RecordProjectAssessment(string assessmentId, string kind, string outcome)
            => RecordProjectAssessment(assessmentId, kind, new AssessedWord("cat", outcome, []));

        public void RecordProjectAssessment(string assessmentId, string kind, params AssessedWord[] words)
        {
            _assessments.Record(new NewAssessmentRecord(
            AssessmentId: assessmentId,
            ProposalId: null,
            ProposalIntentDigest: null,
            Assessor: "pangloss",
            Kind: kind,
            ScopeJson: "{}",
            ScopeDigest: "sha256:scope",
            Subject: new AssessmentSubject.Words(Selection.Create("Default", _includeSecondWord ? ["cat", "dog"] : ["cat"]), "whitespace", "1"),
            BaselineToken: _tokenJson,
            OutcomeDigest: "sha256:outcome",
            SemanticDigest: "sha256:semantic",
            GrammarSourceSha256: "sha256:grammar",
            ModelFingerprint: "fingerprint",
            Pipeline: "pipeline",
            DiagnosticCount: 0,
            Material: new AssessmentRecordMaterial.Words(words, null, null, []),
            SavedUtc: "2026-09-24T10:45:00Z"
        ));
        }

        private static string PublishedDryRun(Proposal proposal,
            IReadOnlyList<SIL.Motif.Model.Effects.ExpectedEffect>? effects = null)
        {
            var digest = IntentDigest.Compute(proposal);
            var effectDigest = effects is null ? "sha256:" + new string('2', 64)
                : SIL.Motif.Model.Effects.ExpectedEffectSetDigest.Compute(effects);
            return JsonSerializer.Serialize(new
            {
                intentDigest = digest,
                baselineNote = "baseline",
                expectedEffects = effects is null ? JsonDocument.Parse("[]").RootElement
                    : JsonDocument.Parse(SIL.Motif.Model.Effects.ExpectedEffectSetJsonWriter.WritePublishedJson(effects))
                        .RootElement,
                effectDigest,
                anchor = new
                {
                    intentDigest = digest,
                    footprintDigest = "sha256:" + new string('3', 64),
                    effectDigest,
                    runnerVersion = "1.0.0",
                    libLcmVersion = "11.0.0",
                    projectionVersion = "projection-v1",
                    dryRunAtUtc = "2026-09-24T11:00:00Z",
                },
            }, MotifJson.CreateOptions());
        }

        public void Dispose() => Database.Dispose();
    }

    internal sealed record SavedProposal(CanonicalId Id, string Json, string Digest);
}
