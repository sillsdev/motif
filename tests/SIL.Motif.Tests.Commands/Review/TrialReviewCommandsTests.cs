using System.Text.Json;
using SIL.Motif.Commands.Review;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Review;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Runtime.Review;
using SIL.Motif.Worker.Parsimony;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands.Review;

public sealed class TrialReviewCommandsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-trial-review-" + Guid.NewGuid().ToString("N"));
    private readonly MotifDatabase _database;
    private readonly CanonicalId _proposal = CanonicalId.Mint("proposal/");
    private const string Job = "job/review";

    public TrialReviewCommandsTests()
    {
        Directory.CreateDirectory(_root);
        var project = new ProjectLocator(Path.Combine(_root, "project.fwdata"), "project");
        _database = MotifDatabase.OpenOwned(Path.Combine(_root, "project.db"), project, MotifSchema.CurrentSchema, new Version(1, 0));
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO Proposals (ProposalId, CurrentIntentDigest, Status) VALUES ($id, 'sha256:intent', 'proposed');";
        command.Parameters.AddWithValue("$id", _proposal.Value);
        command.ExecuteNonQuery();
    }

    [Fact]
    public async Task InterruptedTrialReturnsIncompleteReport()
    {
        CreateInterrupted();
        var id = await new TrialReviewPublisher(_database).PublishAsync(new ReviewPublishRequest(Job), CancellationToken.None);
        var report = new ReviewReportRepository(_database).ReadManifest(id)!.Summary;
        Assert.Equal(1, report.Summary.Incomplete.RequiredCases);
        Assert.Equal(1, report.Summary.Incomplete.AffectedWords);
        Assert.NotEmpty(report.Summary.Incomplete.RunInterruptions);
        Assert.DoesNotContain(report.Summary.Flags, flag => flag.Id == ReviewFlagId.NewWordFailures);
    }

    [Fact]
    public async Task ContextualNegativeRemainsUnknownInPublishedEvidenceAndDetails()
    {
        var assertion = new ReviewAssertion("negative", "", null, ReferenceAuthority.Human,
            "actor", "revision", false, true, false, ["source"]) { Context = "context" };
        var membership = new ReviewMembership("negatives", "member", "negative", 1, null, null, ["negative"]);
        var set = new ReviewSetBinding("negatives", "Negatives", "negative", "members", "expectations", "source", []);
        var inputs = new FrozenReviewInputs("project", _proposal.Value, "scope", "manifest", null, [set], [],
            [new ReviewPlannedCase("case", "word-key", "word", "ws", null, [membership], [assertion])]);
        var digest = "sha256:" + new string('a', 64);
        var baseline = new BaselineToken("project", digest, "v1", "2026-01-01T00:00:00Z", digest);
        var reference = ReviewInputArtifacts.Store(_database, new ReviewInputArtifact(inputs, new Dictionary<string, byte[]>()) { Baseline = baseline });
        var proposalJson = JsonSerializer.Serialize(new { contractVersions = new Dictionary<string, string>(), proposalId = _proposal.Value, operations = Array.Empty<object>() });
        var input = new TrialJobInput(proposalJson, null, ReviewInputs: reference);
        new JobRepository(_database).RecordInterrupted(Job, "project", "trial", JsonSerializer.Serialize(input, MotifJson.CreateOptions()),
            "{}", "2026-01-01T00:00:00Z", JobStatus.Failed);
        var empty = new ParseWordEvidence("fieldworks-parse-analysis/v1", 0, "word", 0, false, false, false, [], []);
        TrialReviewEvidence.Save(_database, new TrialReviewLedger(Job, reference,
            [new("case", "word", "no-analysis", empty)], [new("case", "word", "analysed", empty)], []));
        var id = await new TrialReviewPublisher(_database).PublishAsync(new ReviewPublishRequest(Job));
        var repository = new ReviewReportRepository(_database);
        var report = repository.ReadManifest(id)!.Summary;
        var flag = Assert.Single(report.FlagEvidence, item => item.Id == ReviewFlagId.KnownNonwordsNowAccepted);
        Assert.Equal(EvidenceState.Partial, flag.State);
        Assert.Contains("negative-context-unavailable", flag.Reasons);
        Assert.Contains(report.Limitations, item => item.Code == "negative-context-unavailable");
        var page = repository.ReadChildren(new ReviewChildPageRequest("project", id, "expectations", "case"));
        var row = Assert.IsType<ExpectationRow>(Assert.Single(page.Items));
        Assert.Equal("actor", row.ActorId);
        Assert.Equal(ExpectationOutcome.Unknown, row.After);
    }

    [Fact]
    public async Task IneligibleApprovedReadingIsUnknownInPublishedEvidenceAndDetails()
    {
        var assertion = new ReviewAssertion("approved", "", new ApprovedMorphology([]), ReferenceAuthority.Human,
            "actor", "revision", true, false, false, ["source"]);
        var inputs = new FrozenReviewInputs("project", _proposal.Value, "scope", "manifest", null, [], [],
            [new ReviewPlannedCase("case", "word-key", "word", "ws", null, [], [assertion])]);
        var digest = "sha256:" + new string('a', 64);
        var baseline = new BaselineToken("project", digest, "v1", "2026-01-01T00:00:00Z", digest);
        var reference = ReviewInputArtifacts.Store(_database, new ReviewInputArtifact(inputs, new Dictionary<string, byte[]>()) { Baseline = baseline });
        var proposalJson = JsonSerializer.Serialize(new { contractVersions = new Dictionary<string, string>(), proposalId = _proposal.Value, operations = Array.Empty<object>() });
        var input = new TrialJobInput(proposalJson, null, ReviewInputs: reference);
        new JobRepository(_database).RecordInterrupted(Job, "project", "trial", JsonSerializer.Serialize(input, MotifJson.CreateOptions()),
            "{}", "2026-01-01T00:00:00Z", JobStatus.Failed);
        var empty = new ParseWordEvidence("fieldworks-parse-analysis/v1", 0, "word", 0, false, false, false, [], []);
        TrialReviewEvidence.Save(_database, new TrialReviewLedger(Job, reference,
            [new("case", "word", "no-analysis", empty)], [new("case", "word", "no-analysis", empty)], []));
        var id = await new TrialReviewPublisher(_database).PublishAsync(new ReviewPublishRequest(Job));
        var repository = new ReviewReportRepository(_database);
        var report = repository.ReadManifest(id)!.Summary;
        Assert.Null(report.Summary.Approved.Expected);
        var flag = Assert.Single(report.FlagEvidence, item => item.Id == ReviewFlagId.ConfirmedReadingsLost);
        Assert.Equal(EvidenceState.Partial, flag.State);
        Assert.Contains("expectation-identity-unavailable", flag.Reasons);
        var page = repository.ReadChildren(new ReviewChildPageRequest("project", id, "expectations", "case"));
        var row = Assert.IsType<ExpectationRow>(Assert.Single(page.Items));
        Assert.Equal("actor", row.ActorId);
        Assert.Equal(ExpectationOutcome.Unknown, row.After);
    }

    [Fact]
    public void NoTrialPointsToExplicitProduction()
    {
        var result = new TrialReviewCommands(_database).Read(new ReviewReadRequest("project", _proposal.Value));
        var empty = Assert.IsType<NoTrialResult>(result.Value);
        var target = Assert.Single(empty.Next);
        Assert.Equal("motif_trial", target.Tool);
        Assert.Equal("run", target.Arguments!.Action);
    }

    [Fact]
    public async Task RetainedDryRunContributesItsBoundEffectCount()
    {
        CreateInterrupted();
        var job = new JobRepository(_database).Get(Job)!;
        var input = TrialJobInput.Parse(job.InputJson);
        var intent = SIL.Motif.Contract.Canonicalization.IntentDigest.Compute(
            SIL.Motif.Contract.Parsing.ProposalJsonParser.Parse(input.ProposalJson));
        var json = JsonSerializer.Serialize(new { intentDigest = intent, effectDigest = "effect", expectedEffects = new[] { new { field = "label" } },
            anchor = new { intentDigest = intent, effectDigest = "effect", footprintDigest = "footprint" } });
        using (var connection = _database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE Jobs SET DryRunJson=$json,DryRunPublished=1 WHERE JobId=$job;";
            command.Parameters.AddWithValue("$json", json);
            command.Parameters.AddWithValue("$job", Job);
            command.ExecuteNonQuery();
        }
        var id = await new TrialReviewPublisher(_database).PublishAsync(new ReviewPublishRequest(Job));
        var report = new ReviewReportRepository(_database).ReadManifest(id)!.Summary;
        Assert.Equal(1, report.Summary.Effects.ChangeCount);
        Assert.Equal("effect", report.Pins.DryRun!.EffectDigest);
        Assert.Contains(report.Limitations, item => item.Code == "effects-detail-unavailable");
    }

    [Fact]
    public async Task FailuresIncludesIncompleteCasesWithoutCallingThemFailures()
    {
        CreateInterrupted();
        var id = await new TrialReviewPublisher(_database).PublishAsync(new ReviewPublishRequest(Job));
        var result = new TrialReviewCommands(_database).Read(new ReviewReadRequest("project", _proposal.Value,
            ReportId: id.Value, Detail: DetailLevel.Failures));
        var report = Assert.IsType<AvailableTrialResult>(result.Value).Report;
        var section = Assert.Single(report.Sections, item => item.Id == "incomplete");
        Assert.Equal(new[] { "incomplete/time-limit", "incomplete/not-measured" }, section.Groups.Select(group => group.Cause));
        Assert.All(section.Groups, group => Assert.Single(group.Samples));
        Assert.DoesNotContain(report.Summary.Flags, item => item.Id == ReviewFlagId.NewWordFailures);
    }

    [Theory]
    [InlineData("parser-work")]
    [InlineData("sampled-tokens")]
    [InlineData("paradigm-cells")]
    [InlineData("effects")]
    [InlineData("evidence")]
    public async Task ExplicitFailuresSectionIncludesEvidenceOrAnUnavailableReason(string section)
    {
        CreateInterrupted(configured: true);
        var id = await new TrialReviewPublisher(_database).PublishAsync(new ReviewPublishRequest(Job));
        var result = new TrialReviewCommands(_database).Read(new ReviewReadRequest("project", _proposal.Value,
            ReportId: id.Value, Section: section));
        var report = Assert.IsType<AvailableTrialResult>(result.Value).Report;
        var selected = Assert.Single(report.Sections);
        Assert.Equal(section, selected.Id);
        Assert.True(selected.Links.Count > 0 || selected.Limitations.Count > 0);
    }

    [Fact]
    public async Task AllDetailsShareIdenticalSummary()
    {
        CreateInterrupted();
        var id = await new TrialReviewPublisher(_database).PublishAsync(new ReviewPublishRequest(Job), CancellationToken.None);
        var commands = new TrialReviewCommands(_database);
        var request = new ReviewReadRequest("project", _proposal.Value, ReportId: id.Value);
        var summary = Assert.IsType<AvailableTrialResult>(commands.Read(request).Value).Report;
        var full = Assert.IsType<AvailableTrialResult>(commands.Read(request with { Detail = DetailLevel.Full }).Value).Report;
        Assert.Equal(JsonSerializer.Serialize(summary.Summary, ReviewJson.Options), JsonSerializer.Serialize(full.Summary, ReviewJson.Options));
    }

    [Fact]
    public async Task ReadingNeverRunsParserOrLiveProject()
    {
        CreateInterrupted();
        var id = await new TrialReviewPublisher(_database).PublishAsync(new ReviewPublishRequest(Job), CancellationToken.None);
        Directory.Delete(_database.FullPath + ".review-inputs", true);
        Directory.Delete(_database.FullPath + ".review-evidence", true);
        var result = new TrialReviewCommands(_database).Read(new ReviewReadRequest("project", _proposal.Value, ReportId: id.Value));
        Assert.IsType<AvailableTrialResult>(result.Value);
    }

    [Fact]
    public void UnknownSectionIsRejectedBeforeReadingStoredEvidence()
    {
        var outcome = new TrialReviewCommands(_database).Read(new ReviewReadRequest("project", _proposal.Value,
            Section: "not-a-section"));
        Assert.False(outcome.Succeeded);
        Assert.Equal(FailureReason.InvalidArgument, outcome.Refusal!.Reason);
    }

    [Fact]
    public async Task LatestActiveTrialDoesNotSilentlyFallBack()
    {
        CreateInterrupted();
        await new TrialReviewPublisher(_database).PublishAsync(new ReviewPublishRequest(Job), CancellationToken.None);
        var jobs = new JobRepository(_database);
        var original = jobs.Get(Job)!;
        jobs.Create("job/new", "project", "trial", original.InputJson, "2026-01-02T00:00:00Z");
        var result = new TrialReviewCommands(_database).Read(new ReviewReadRequest("project", _proposal.Value));
        Assert.Equal("job/new", Assert.IsType<PendingTrialResult>(result.Value).JobId);
    }

    [Fact]
    public async Task PrerequisiteEditMarksFreshnessWithoutRewritingFacts()
    {
        var prerequisite = CanonicalId.Mint("proposal/");
        var json = JsonSerializer.Serialize(new { contractVersions = new Dictionary<string, string>(), proposalId = prerequisite.Value,
            operations = Array.Empty<object>() });
        var frozen = FrozenProposalRevision.Create(json);
        using (var connection = _database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO Proposals (ProposalId, CurrentIntentDigest, Status) VALUES ($id, $digest, 'proposed');";
            command.Parameters.AddWithValue("$id", prerequisite.Value);
            command.Parameters.AddWithValue("$digest", frozen.IntentDigest);
            command.ExecuteNonQuery();
        }
        CreateInterrupted([frozen]);
        var id = await new TrialReviewPublisher(_database).PublishAsync(new ReviewPublishRequest(Job), CancellationToken.None);
        var commands = new TrialReviewCommands(_database);
        var request = new ReviewReadRequest("project", _proposal.Value, ReportId: id.Value);
        var before = Assert.IsType<AvailableTrialResult>(commands.Read(request).Value).Report;
        using (var connection = _database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE Proposals SET CurrentIntentDigest='changed' WHERE ProposalId=$id;";
            command.Parameters.AddWithValue("$id", prerequisite.Value);
            command.ExecuteNonQuery();
        }
        var after = Assert.IsType<AvailableTrialResult>(commands.Read(request).Value).Report;
        Assert.Equal(FreshnessRelation.Historical, after.Freshness.Relation);
        Assert.Contains("prerequisite-changed", after.Freshness.Reasons);
        Assert.Equal(JsonSerializer.Serialize(before.Summary, ReviewJson.Options), JsonSerializer.Serialize(after.Summary, ReviewJson.Options));
    }

    [Fact]
    public async Task DraftHasDigestAndNullRevision()
    {
        CreateInterrupted();
        var id = await new TrialReviewPublisher(_database).PublishAsync(new ReviewPublishRequest(Job), CancellationToken.None);
        var report = new ReviewReportRepository(_database).ReadManifest(id)!.Summary;
        Assert.StartsWith("sha256:", report.Pins.IntentDigest);
        Assert.Null(report.Pins.Revision);
    }

    [Fact]
    public async Task EditingDraftMarksHistoricalWithoutChangingFrozenFacts()
    {
        CreateInterrupted();
        var input = TrialJobInput.Parse(new JobRepository(_database).Get(Job)!.InputJson);
        void WriteDraft(string json)
        {
            using var connection = _database.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE Proposals SET CurrentIntentDigest=NULL,DraftName='draft',DraftJson=$json WHERE ProposalId=$id;";
            command.Parameters.AddWithValue("$id", _proposal.Value);
            command.Parameters.AddWithValue("$json", json);
            command.ExecuteNonQuery();
        }
        WriteDraft(input.ProposalJson);
        var id = await new TrialReviewPublisher(_database).PublishAsync(new ReviewPublishRequest(Job));
        var commands = new TrialReviewCommands(_database);
        var request = new ReviewReadRequest("project", _proposal.Value, ReportId: id.Value);
        var before = Assert.IsType<AvailableTrialResult>(commands.Read(request).Value).Report;
        Assert.DoesNotContain("proposal-changed", before.Freshness.Reasons);
        var operation = CanonicalId.Mint("operation/").Value;
        var target = CanonicalId.FromGuid(Guid.NewGuid()).Value;
        var changed = JsonSerializer.Serialize(new { contractVersions = new Dictionary<string, string> { ["lexical"] = "1.0" },
            proposalId = _proposal.Value, operations = new[] { new { operationId = operation, kind = "lexical/lexSense/setGloss",
                target, after = new { ws = "en", text = "changed" } } } });
        WriteDraft(changed);
        var after = Assert.IsType<AvailableTrialResult>(commands.Read(request).Value).Report;
        Assert.Contains("proposal-changed", after.Freshness.Reasons);
        Assert.Equal(JsonSerializer.Serialize(before.Summary, ReviewJson.Options), JsonSerializer.Serialize(after.Summary, ReviewJson.Options));
    }

    [Fact]
    public async Task PurgedDetailReturnsItsReasonAndRetainedSummaryTarget()
    {
        CreateInterrupted();
        var id = await new TrialReviewPublisher(_database).PublishAsync(new ReviewPublishRequest(Job));
        using (var connection = _database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE ReviewReports SET DetailAvailable=0 WHERE ReportId=$id;";
            command.Parameters.AddWithValue("$id", id.Value);
            command.ExecuteNonQuery();
        }
        var commands = new TrialReviewCommands(_database);
        var summary = commands.Read(new ReviewReadRequest("project", _proposal.Value, ReportId: id.Value));
        Assert.True(summary.Succeeded);
        var detail = commands.Read(new ReviewReadRequest("project", _proposal.Value, ReportId: id.Value, Detail: DetailLevel.Full));
        Assert.Equal("review.detail-unavailable", detail.Refusal!.Code);
        Assert.Equal("not-retained", detail.Refusal.Facts["reason"]);
        Assert.Contains(Uri.EscapeDataString(id.Value), detail.Refusal.Facts["summaryUri"]);
    }

    [Fact]
    public async Task PairedWarningsFromTheTrialProduceAFlagEvenWithoutParsimony()
    {
        CreateInterrupted();
        var ledger = TrialReviewEvidence.ReadJob(_database, Job)!;
        var subject = new GrammarWarningPart("subject", GrammarWarningPartRole.Object)
            { SubjectGuid = "11111111-1111-1111-1111-111111111111" };
        var warning = new GrammarWarning(GrammarDiagnosticLevel.Warning, "label", [subject], [], "warning")
            { Code = "new-warning", Origin = GrammarFindingOrigin.Check };
        TrialReviewEvidence.Save(_database, ledger with { BeforeWarnings = [], AfterWarnings = [warning] });
        var id = await new TrialReviewPublisher(_database).PublishAsync(new ReviewPublishRequest(Job));
        var report = new ReviewReportRepository(_database).ReadManifest(id)!.Summary;
        var flag = Assert.Single(report.Summary.Flags, item => item.Id == ReviewFlagId.NewWarningsOrParsimony);
        Assert.Equal(1, Assert.IsType<NewFindingFacts>(flag.Facts).Warnings);
        Assert.DoesNotContain(report.Limitations, item => item.Code == "warnings-unavailable");
        Assert.Equal(report.Limitations.Count, report.Summary.Incomplete.EvidenceGapCount);
        var warningPage = new ReviewReportRepository(_database).ReadPage(new ReviewPageRequest("project", id, "warnings"));
        Assert.Equal(DiagnosticChange.Added, Assert.IsType<DiagnosticRow>(Assert.Single(warningPage.Items)).Change);
        var failures = Assert.IsType<AvailableTrialResult>(new TrialReviewCommands(_database).Read(
            new ReviewReadRequest("project", _proposal.Value, ReportId: id.Value, Detail: DetailLevel.Failures, Section: "warnings")).Value).Report;
        var group = Assert.Single(Assert.Single(failures.Sections).Groups);
        Assert.Equal("warning/new-warning", group.Cause);
        Assert.IsType<DiagnosticRow>(Assert.Single(group.Samples));
        Assert.Equal(0, group.Remaining);
        Assert.Equal(EvidenceState.Partial, Assert.Single(report.FlagEvidence,
            item => item.Id == ReviewFlagId.NewWarningsOrParsimony).State);
    }

    [Fact]
    public async Task PromotedWarningAppearsInFlaggedAndFailurePages()
    {
        CreateInterrupted();
        var ledger = TrialReviewEvidence.ReadJob(_database, Job)!;
        var subject = new GrammarWarningPart("subject", GrammarWarningPartRole.Object)
            { SubjectGuid = "11111111-1111-1111-1111-111111111111" };
        var warning = new GrammarWarning(GrammarDiagnosticLevel.Warning, "label", [subject], [], "warning")
            { Code = "promoted", Origin = GrammarFindingOrigin.Check };
        var info = warning with { Severity = GrammarDiagnosticLevel.Information };
        TrialReviewEvidence.Save(_database, ledger with { BeforeWarnings = [info], AfterWarnings = [warning] });
        var id = await new TrialReviewPublisher(_database).PublishAsync(new ReviewPublishRequest(Job));
        var repository = new ReviewReportRepository(_database);
        var page = repository.ReadPage(new ReviewPageRequest("project", id, "warnings")
            { Flag = ReviewFlagId.NewWarningsOrParsimony });
        var row = Assert.IsType<DiagnosticRow>(Assert.Single(page.Items));
        Assert.Equal(DiagnosticChange.Changed, row.Change);
        Assert.Equal("info", row.BeforeSeverity);
        var failures = Assert.IsType<AvailableTrialResult>(new TrialReviewCommands(_database).Read(
            new ReviewReadRequest("project", _proposal.Value, ReportId: id.Value, Detail: DetailLevel.Failures, Section: "warnings")).Value).Report;
        Assert.Single(Assert.Single(failures.Sections).Groups);
    }

    [Fact]
    public async Task ConfiguredSetDescriptorsRemainAddressableFromEvidence()
    {
        CreateInterrupted(configured: true);
        var id = await new TrialReviewPublisher(_database).PublishAsync(new ReviewPublishRequest(Job));
        var page = new ReviewReportRepository(_database).ReadPage(new ReviewPageRequest("project", id, "evidence"));
        var set = Assert.IsType<SetRow>(Assert.Single(page.Items));
        Assert.Equal("reference-set", set.Id);
        Assert.Equal(1, set.Memberships);
        Assert.Equal(1, set.Words);
    }

    [Fact]
    public async Task CapturedReadingsRemainAvailableWithoutProjectAccess()
    {
        var analysis = new ParseAnalysis([new ParseMorph("11111111-1111-1111-1111-111111111111",
            "22222222-2222-2222-2222-222222222222", null, null)]);
        CreateInterrupted(analysis: analysis);
        var id = await new TrialReviewPublisher(_database).PublishAsync(new ReviewPublishRequest(Job), CancellationToken.None);
        var page = new ReviewReportRepository(_database).ReadChildren(new ReviewChildPageRequest("project", id, "readings", "case"));
        var row = Assert.IsType<ReadingRow>(Assert.Single(page.Items));
        Assert.Equal(Side.Before, row.Side);
        Assert.Equal("11111111-1111-1111-1111-111111111111", Assert.Single(row.Morphs).FormId);
    }

    [Fact]
    public async Task ExactWordSelectionReadsItsRetainedChildren()
    {
        var analysis = new ParseAnalysis([new ParseMorph("11111111-1111-1111-1111-111111111111",
            "22222222-2222-2222-2222-222222222222", null, null)]);
        CreateInterrupted(analysis: analysis);
        var id = await new TrialReviewPublisher(_database).PublishAsync(new ReviewPublishRequest(Job), CancellationToken.None);
        var result = new TrialReviewCommands(_database).Read(new ReviewReadRequest("project", _proposal.Value,
            ReportId: id.Value, Word: "word"));
        var word = Assert.IsType<AvailableTrialResult>(result.Value).Report.Word!;
        Assert.Equal("case", word.Row.CaseKey);
        Assert.Single(word.Readings.Items);
    }

    [Fact]
    public void LegacyTrialNamesMissingCapabilities()
    {
        var digest = "sha256:" + new string('a', 64);
        var baseline = new BaselineToken("project", digest, "v1", "2026-01-01T00:00:00Z", digest);
        var proposalJson = JsonSerializer.Serialize(new { contractVersions = new Dictionary<string, string>(), proposalId = _proposal.Value, operations = Array.Empty<object>() });
        var input = new TrialJobInput(proposalJson, null);
        var jobs = new JobRepository(_database);
        jobs.Create(Job, "project", "trial", JsonSerializer.Serialize(input, MotifJson.CreateOptions()), "2026-01-01T00:00:00Z");
        using (var connection = _database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE Jobs SET Status='completed', ArchivedUtc=UpdatedUtc, ResultJson=$result WHERE JobId=$id;";
            command.Parameters.AddWithValue("$result", JsonSerializer.Serialize(new { baselineToken = baseline, assessmentIds = Array.Empty<string>() }, MotifJson.CreateOptions()));
            command.Parameters.AddWithValue("$id", Job);
            command.ExecuteNonQuery();
        }
        var report = Assert.IsType<AvailableTrialResult>(new TrialReviewCommands(_database)
            .Read(new ReviewReadRequest("project", _proposal.Value)).Value).Report;
        Assert.Contains(report.Limitations, item => item.Code == "legacy-review-inputs-missing");
        Assert.Equal(DetailAvailability.Unavailable, report.DetailAvailability);
        Assert.All(report.FlagEvidence, item => Assert.Equal(EvidenceState.Unavailable, item.State));
        var reread = Assert.IsType<AvailableTrialResult>(new TrialReviewCommands(_database)
            .Read(new ReviewReadRequest("project", _proposal.Value, ReportId: report.ReportId)).Value).Report;
        Assert.Equal(report.ReportDigest, reread.ReportDigest);
        _ = JsonSerializer.Serialize(reread, ReviewJson.Options);
    }

    [Fact]
    public async Task RefreshBindsLaterEvidenceOnlyInNewReport()
    {
        CreateInterrupted();
        var publisher = new TrialReviewPublisher(_database);
        var firstId = await publisher.PublishAsync(new ReviewPublishRequest(Job));
        var repository = new ReviewReportRepository(_database);
        var first = repository.ReadManifest(firstId)!.Summary;
        var jobs = new JobRepository(_database);
        var trial = jobs.Get(Job)!;
        var baseline = first.Pins.BaselineToken;
        var candidateId = ParsimonyAutoRun.Enqueue(_database, trial, baseline, false)!;
        var candidate = jobs.Get(candidateId)!;
        var input = ParsimonyCandidateJobInput.Parse(candidate.InputJson);
        var digest = new string('4', 64);
        ParsimonyReportResponse MakeReport(string side, string measure) => new("report/" + side + measure,
            new ParsimonyReportInputs("bundle/" + side, baseline, side, side == "candidate" ? "identity" : null,
                "model", new ParsimonyArtifactDigest(1, digest), new ParsimonyArtifactDigest(1, digest), null, null, []),
            side == "candidate" ? [new ParsimonyFinding("finding/one", measure, ParsimonyAxis.Parsimony,
                ParsimonyTier.Static, new ParsimonyFindingAttachment(ParsimonyAttachmentKind.Project, "project"), null,
                new ParsimonyMeasureNumber(1, 1, "items"),
                new ParsimonyMeasureThreshold(ParsimonyThresholdOperator.RankedOnly, null, "threshold/v1"),
                "sha256:" + digest, [], "guide:measure", [], ParsimonyVerification.NotRun)] : [], "text")
        {
            AssessmentIds = [], Notes = [],
            MeasureRuns = [new ParsimonyMeasureRun(measure, ParsimonyMeasureStatus.Computed, 10, 1, "items", null, null, null)],
        };
        var pairs = input.RunMeasureIds.Select(measure => new ParsimonyCandidateMeasureResult(measure,
            MakeReport("baseline", measure), MakeReport("candidate", measure), [])).ToArray();
        var binding = new ParsimonyCandidateBinding("identity", baseline, Job, _proposal.Value, first.Pins.IntentDigest,
            "sha256:c", [], "sha256:e", "sha256:a", "sha256:s", "sha256:t", "sha256:j");
        var response = new ParsimonyCandidateEvidenceResponse(1, pairs[0].MeasureId, binding,
            pairs[0].Before, pairs[0].After, [], new ParsimonyApprovedAnalysisPreservation("preserved", 0, 0, []), pairs.Skip(1).ToArray());
        using (var connection = _database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE Jobs SET Status='completed', ArchivedUtc=UpdatedUtc, ResultJson=$result WHERE JobId=$id;";
            command.Parameters.AddWithValue("$result", JsonSerializer.Serialize(response, MotifJson.CreateOptions()));
            command.Parameters.AddWithValue("$id", candidateId);
            command.ExecuteNonQuery();
        }
        var secondId = await publisher.PublishAsync(new ReviewPublishRequest(Job));
        Assert.NotEqual(firstId, secondId);
        Assert.Empty(first.Summary.Parsimony);
        var second = repository.ReadManifest(secondId)!.Summary;
        Assert.NotEmpty(second.Summary.Parsimony);
        Assert.Contains(second.Summary.Flags, item => item.Id == ReviewFlagId.NewWarningsOrParsimony);
        var page = repository.ReadPage(new ReviewPageRequest("project", secondId, "parsimony"));
        Assert.NotEmpty(page.Items);
        Assert.All(page.Items, row => Assert.Equal(FindingChange.Added, Assert.IsType<FindingRow>(row).Change));
        var failures = Assert.IsType<AvailableTrialResult>(new TrialReviewCommands(_database).Read(
            new ReviewReadRequest("project", _proposal.Value, ReportId: secondId.Value, Detail: DetailLevel.Failures, Section: "parsimony")).Value).Report;
        Assert.All(Assert.Single(failures.Sections).Groups, group => Assert.StartsWith("parsimony/", group.Cause));
        var retainedId = second.Pins.ParsimonyPairs[0].AfterReportId!;
        using (var connection = _database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO Reports (ReportId,ReportJson,CreatedUtc,Kind) VALUES ($id,'{}','2026-01-01T00:00:00Z','parsimony');";
            command.Parameters.AddWithValue("$id", retainedId);
            command.ExecuteNonQuery();
        }
        Assert.False(new EvidenceArtifactRepository(_database).DeleteReport(retainedId));
        Assert.Equal(JsonSerializer.Serialize(first, ReviewJson.Options),
            JsonSerializer.Serialize(repository.ReadManifest(firstId)!.Summary, ReviewJson.Options));
    }

    [Fact]
    public async Task TraceWithoutPinnedParserDoesNotUseTheCurrentParserOrChangeTheReport()
    {
        CreateInterrupted();
        var id = await new TrialReviewPublisher(_database).PublishAsync(new ReviewPublishRequest(Job));
        var repository = new ReviewReportRepository(_database);
        var before = JsonSerializer.Serialize(repository.ReadManifest(id)!.Summary, ReviewJson.Options);
        var command = new ReviewTraceCommands(_database);
        var trace = command.Produce(new ReviewTraceRequest("project", id, "case", Side.After));
        Assert.False(trace.Succeeded);
        Assert.Equal(RefusalCodes.TrialTraceUnavailable, trace.Refusal!.Code);
        Assert.Contains("pin", trace.Refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, JsonSerializer.Serialize(repository.ReadManifest(id)!.Summary, ReviewJson.Options));
    }

    private void CreateInterrupted(IReadOnlyList<FrozenProposalRevision>? prerequisites = null, ParseAnalysis? analysis = null,
        bool configured = false)
    {
        ReviewSetBinding[] sets = configured ? [new("reference-set", "Reference", "reference", "members", "expectations", "source", [])] : [];
        ReviewMembership[] memberships = configured ? [new("reference-set", "member", "reference", 1, null, null, [])] : [];
        var inputs = new FrozenReviewInputs("project", _proposal.Value, "scope", "manifest", null, sets, [],
            [new ReviewPlannedCase("case", "word-key", "word", "ws", null, memberships, [])]);
        var digest = "sha256:" + new string('a', 64);
        var baseline = new BaselineToken("project", digest, "v1", "2026-01-01T00:00:00Z", digest);
        var reference = ReviewInputArtifacts.Store(_database, new ReviewInputArtifact(inputs, new Dictionary<string, byte[]>()) { Baseline = baseline });
        var proposalJson = JsonSerializer.Serialize(new { contractVersions = new Dictionary<string, string>(), proposalId = _proposal.Value, operations = Array.Empty<object>() });
        var input = new TrialJobInput(proposalJson, null, Prerequisites: prerequisites, ReviewInputs: reference);
        new JobRepository(_database).RecordInterrupted(Job, "project", "trial", JsonSerializer.Serialize(input, MotifJson.CreateOptions()),
            "{}", "2026-01-01T00:00:00Z", JobStatus.Failed);
        var row = new ParseWordEvidence("fieldworks-parse-analysis/v1", 0, "word", 0, false, true, false, analysis is null ? [] : [analysis], []);
        var ledger = new TrialReviewLedger(Job, reference, [new("case", "word", "timed-out", row)], [], []);
        TrialReviewEvidence.Save(_database, TrialReviewEvidence.Interrupt(ledger, inputs, "after", "cancelled"));
    }

    public void Dispose()
    {
        _database.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
