using SIL.Motif.Commands;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.SelectionReading;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Host.Texts;
using SIL.Motif.Host.Parser;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class EvidenceSetReadsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SIL.Motif.EvidenceSetReadsTests",
        Guid.NewGuid().ToString("N"));

    public EvidenceSetReadsTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void OverviewForATrialReadsTheTrialsAssessment()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var proposal = fixture.SaveProposal();
        fixture.RecordProjectAssessment("project-root", AssessmentKinds.ParseTime, "no-analysis");
        fixture.RecordAssessment(proposal, "trial-root", AssessmentKinds.ParseTime, "analysed");
        fixture.CreateJob(proposal.Json, "2026-09-24T11:00:00Z", "trial", ["trial-root"]);
        var trial = Trials.LatestFor(fixture.Database, fixture.Project, proposal.Id.Value)!;
        var set = EvidenceSets.ForTrial(fixture.Database, fixture.Project, trial);
        Assert.True(set.Succeeded, set.Refusal?.Message);

        var today = OverviewCommand.Overview(new OverviewRequest(fixture.Project.FullFwDataPath));
        var withProposal = OverviewCommand.Overview(fixture.Database, fixture.Project, set.Value!);

        Assert.True(today.Succeeded, today.Refusal?.Message);
        Assert.True(withProposal.Succeeded, withProposal.Refusal?.Message);
        Assert.NotEqual(today.Value!.TextCoverage, withProposal.Value!.TextCoverage);
    }

    [Fact]
    public void ProjectAssessmentMappingMatchesTheResolvedEvidenceSet()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        fixture.SetDefaultSelection([], ["cat"]);
        fixture.RecordProjectAssessment("project-root", AssessmentKinds.ParseTime, "analysed");

        var snapshot = CurrentEvidenceQuery.ReadCurrentEvidence(fixture.Database, fixture.Project,
            includeWordContext: false).Value!;
        var mapped = EvidenceSet.FromProjectAssessment(snapshot.Assessment!);

        var expected = snapshot.Set!;
        Assert.Equal(expected.Baseline, mapped.Baseline);
        Assert.Equal(expected.RootAssessmentId, mapped.RootAssessmentId);
        Assert.Equal(expected.ReplacementAssessmentIds, mapped.ReplacementAssessmentIds);
        Assert.Equal(expected.MeasurementIds.OrderBy(pair => pair.Key), mapped.MeasurementIds.OrderBy(pair => pair.Key));
        Assert.Equal(expected.GrammarCheckId, mapped.GrammarCheckId);
        Assert.Equal(expected.MeasuredWords, mapped.MeasuredWords);
        Assert.Equal(expected.Origin, mapped.Origin);
    }

    [Fact]
    public void OverviewForATouchedWordTrialCarriesTodaysUnmeasuredValues()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root, includeSecondWord: true);
        var proposal = fixture.SaveProposal();
        fixture.SetDefaultSelection([fixture.TextId], []);
        fixture.RecordProjectAssessment("project-root", AssessmentKinds.ParseTime,
            ParsedWord("cat", 1), ParsedWord("dog", 3));
        fixture.RecordAssessment(proposal, "trial-root", AssessmentKinds.ParseTime, "no-analysis");
        fixture.CreateJob(proposal.Json, "2026-09-24T11:00:00Z", "trial", ["trial-root"], allWords: false);
        var trial = Trials.LatestFor(fixture.Database, fixture.Project, proposal.Id.Value)!;
        var set = EvidenceSets.ForTrial(fixture.Database, fixture.Project, trial);
        Assert.True(set.Succeeded, set.Refusal?.Message);

        var today = OverviewCommand.Overview(new OverviewRequest(fixture.Project.FullFwDataPath));
        var withProposal = OverviewCommand.Overview(fixture.Database, fixture.Project, set.Value!);

        Assert.True(today.Succeeded, today.Refusal?.Message);
        Assert.True(withProposal.Succeeded, withProposal.Refusal?.Message);
        Assert.Equal((2, 4, 4, 100d),
            (today.Value!.TextCoverage.ParsedWords, today.Value.TextCoverage.TotalOccurrences,
                today.Value.TextCoverage.ParsedOccurrences, today.Value.TextCoverage.OccurrenceCoveragePercent));
        Assert.Equal((1, 4, 3, 75d),
            (withProposal.Value!.TextCoverage.ParsedWords, withProposal.Value.TextCoverage.TotalOccurrences,
                withProposal.Value.TextCoverage.ParsedOccurrences,
                withProposal.Value.TextCoverage.OccurrenceCoveragePercent));
    }

    [Fact]
    public void OverviewWithUnknownProposalRefusesTrialNone()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var proposal = fixture.SaveProposal();

        var result = OverviewCommand.Overview(new OverviewRequest(fixture.Project.FullFwDataPath, proposal.Id.Value));

        Assert.False(result.Succeeded);
        Assert.Equal("trial.none", result.Refusal?.Code);
    }

    [Fact]
    public void StoredGrammarCheckForATrialReadsTheTrialsCheck()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var proposal = fixture.SaveProposal();
        fixture.RecordAssessment(proposal, "trial-root", AssessmentKinds.ParseTime);
        fixture.CreateJob(proposal.Json, "2026-09-24T11:00:00Z", "trial", ["trial-root"]);
        var grammar = new GrammarCheckResponse([], HasBaseline: true);
        new TrialGrammarCheckRepository(fixture.Database).Save(fixture.Token, proposal.Id, proposal.Digest,
            "sha256:selection", grammar);

        var trialCheck = StoredGrammarCheckQuery.Query(new GrammarCheckRequest(
            fixture.Project.FullFwDataPath, proposal.Id.Value));
        var currentCheck = StoredGrammarCheckQuery.Query(new GrammarCheckRequest(fixture.Project.FullFwDataPath));

        Assert.True(trialCheck.Succeeded, trialCheck.Refusal?.Message);
        Assert.True(trialCheck.Value!.Check!.HasBaseline);
        Assert.Empty(trialCheck.Value.Check.Findings);
        Assert.True(currentCheck.Succeeded, currentCheck.Refusal?.Message);
        Assert.Null(currentCheck.Value!.Check);
    }

    [Fact]
    public void StoredTrialGrammarCheckKeepsMeasuredProvenanceAfterTheCurrentBaselineChanges()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var proposal = fixture.SaveProposal();
        fixture.RecordAssessment(proposal, "trial-root", AssessmentKinds.ParseTime);
        fixture.CreateJob(proposal.Json, "2026-09-24T11:00:00Z", "trial", ["trial-root"]);
        var measuredSourceSave = DateTimeOffset.Parse("2026-09-24T10:30:00Z");
        new TrialGrammarCheckRepository(fixture.Database).Save(fixture.Token, proposal.Id, proposal.Digest,
            "sha256:selection", new GrammarCheckResponse([], HasBaseline: true), measuredSourceSave);
        var request = new GrammarCheckRequest(fixture.Project.FullFwDataPath, proposal.Id.Value);
        var before = StoredGrammarCheckQuery.Query(request);
        Assert.True(before.Succeeded, before.Refusal?.Message);
        var beforeProvenance = before.Value!.Provenance;
        Assert.NotNull(beforeProvenance);
        Assert.Equal(fixture.Token, beforeProvenance.BaselineToken);
        Assert.Equal("sha256:selection", beforeProvenance.SelectionSha256);
        Assert.Null(beforeProvenance.ParserStamp);
        Assert.Equal(measuredSourceSave, beforeProvenance.SourceLastWriteUtc);
        Assert.Equal(proposal.Id.Value, beforeProvenance.Trial!.ProposalId);
        Assert.Equal(proposal.Digest, beforeProvenance.Trial.IntentDigest);

        fixture.RecordBaseline("sha256:" + new string('b', 64), "2026-09-24T11:30:00Z",
            DateTimeOffset.Parse("2026-09-24T12:30:00Z"));

        var after = StoredGrammarCheckQuery.Query(request);
        Assert.True(after.Succeeded, after.Refusal?.Message);
        Assert.Equal(beforeProvenance, after.Value!.Provenance);
    }

    [Fact]
    public void StoredGrammarCheckForAnIncompleteTrialReturnsWithoutWordAttribution()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var proposal = fixture.SaveProposal();
        fixture.CreateRunningJob(proposal.Json, "2026-09-24T11:00:00Z", "running");
        new TrialGrammarCheckRepository(fixture.Database).Save(fixture.Token, proposal.Id, proposal.Digest,
            "sha256:selection", new GrammarCheckResponse([], HasBaseline: true));

        var result = StoredGrammarCheckQuery.Query(new GrammarCheckRequest(
            fixture.Project.FullFwDataPath, proposal.Id.Value));

        Assert.True(result.Succeeded, result.Refusal?.Message);
        Assert.True(result.Value!.Check!.HasBaseline);
        Assert.All(result.Value.Check.Findings, finding => Assert.Null(finding.YourWords));
    }

    [Fact]
    public void StoredGrammarCheckRefusesWhenATrialAssessmentIsMissing()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var proposal = fixture.SaveProposal();
        var tokenJson = System.Text.Json.JsonSerializer.Serialize(fixture.Token, MotifJson.CreateOptions());
        new TrialGrammarCheckRepository(fixture.Database).Save(fixture.Token, proposal.Id, proposal.Digest,
            "sha256:selection", new GrammarCheckResponse([], HasBaseline: true));
        var set = new EvidenceSet(fixture.Token, "missing-root", [],
            new Dictionary<string, string>(StringComparer.Ordinal), tokenJson, [],
            new EvidenceOrigin(EvidenceOriginKind.Trial, proposal.Id.Value, proposal.Digest, "job/trial"));

        var result = StoredGrammarCheckQuery.Query(fixture.Database, fixture.Project, set);

        Assert.False(result.Succeeded);
        Assert.Equal("assessment.invalid-evidence", result.Refusal?.Code);
    }

    [Fact]
    public async Task ATrialSessionReusesTheSourceBaselinesTextIndex()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root, includeIndexedText: true);
        var proposal = fixture.SaveProposal();
        fixture.RecordAssessment(proposal, "trial-root", AssessmentKinds.ParseTime);
        fixture.CreateJob(proposal.Json, "2026-09-24T11:00:00Z", "trial", ["trial-root"]);
        var trial = Trials.LatestFor(fixture.Database, fixture.Project, proposal.Id.Value)!;
        var set = EvidenceSets.ForTrial(fixture.Database, fixture.Project, trial);
        Assert.True(set.Succeeded, set.Refusal?.Message);

        var before = TextIndexRowCount(fixture);
        var opened = await SelectionReader.OpenAsync(new OpenSelectionReaderRequest(
            fixture.Project.FullFwDataPath, [fixture.TextId], []) { ShownAssessment = set.Value });

        Assert.True(opened.Succeeded, opened.Refusal?.Message);
        await using var reader = opened.Value!;
        Assert.Equal(1, reader.Diagnostics.IndexRowsDeserialized);
        Assert.Equal(before, TextIndexRowCount(fixture));
    }

    private static long TextIndexRowCount(TrialsTests.ProjectFixture fixture)
    {
        using var connection = fixture.Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM BaselineTextReadIndex";
        return (long)command.ExecuteScalar()!;
    }

    private static AssessedWord ParsedWord(string word, int occurrences) =>
        new(word, "analysed", [])
        {
            Morphology = new ParseWordEvidence("v1", 0, "word", 1, false, false, false,
                [new ParseAnalysis([new ParseMorph("form", "v", null, null)])], []),
            ProjectStanding = ProjectStanding.Candidate,
            ReadingGrades = [ReadingGrade.Candidate],
            OccurrenceCount = occurrences,
        };

    [Fact]
    public async Task SelectionReaderOpensOverATrialSetWithTheSourceBaselineToken()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var proposal = fixture.SaveProposal();
        fixture.RecordAssessment(proposal, "trial-root", AssessmentKinds.ParseTime);
        var jobId = fixture.CreateJob(proposal.Json, "2026-09-24T11:00:00Z", "trial", ["trial-root"]);
        var trial = Trials.LatestFor(fixture.Database, fixture.Project, proposal.Id.Value)!;
        var set = EvidenceSets.ForTrial(fixture.Database, fixture.Project, trial);
        Assert.True(set.Succeeded, set.Refusal?.Message);

        var opened = await SelectionReader.OpenAsync(new OpenSelectionReaderRequest(
            fixture.Project.FullFwDataPath, [], []) { ShownAssessment = set.Value });

        Assert.True(opened.Succeeded, opened.Refusal?.Message);
        await using var reader = opened.Value!;
        Assert.Equal(fixture.Token, reader.Context.Baseline);
        Assert.Equal("trial", reader.Context.Evidence!.Origin);
        Assert.Equal(jobId, set.Value!.Origin.JobId);
    }

    [Fact]
    public async Task TrialSelectionWarningsIgnoreLaterProjectParserRefusals()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var proposal = fixture.SaveProposal();
        fixture.RecordAssessment(proposal, "trial-root", AssessmentKinds.ParseTime);
        fixture.CreateJob(proposal.Json, "2026-09-24T11:00:00Z", "trial", ["trial-root"]);
        var trial = Trials.LatestFor(fixture.Database, fixture.Project, proposal.Id.Value)!;
        var set = EvidenceSets.ForTrial(fixture.Database, fixture.Project, trial).Value!;

        var before = await SelectionReader.OpenAsync(new OpenSelectionReaderRequest(
            fixture.Project.FullFwDataPath, [], []) { ShownAssessment = set });
        Assert.True(before.Succeeded, before.Refusal?.Message);
        await using var beforeReader = before.Value!;
        var identities = beforeReader.Context.Evidence!.WarningIdentities;

        new ParserRefusalRepository(fixture.Database).Save(fixture.Token, new Refusal(
            "assess.parser-unavailable", FailureReason.Refused, "The project parser is unavailable."));

        var after = await SelectionReader.OpenAsync(new OpenSelectionReaderRequest(
            fixture.Project.FullFwDataPath, [], []) { ShownAssessment = set });
        Assert.True(after.Succeeded, after.Refusal?.Message);
        await using var afterReader = after.Value!;
        Assert.Equal(identities, afterReader.Context.Evidence!.WarningIdentities);
    }

    [Fact]
    public async Task TrialSelectionReaderResolvesItsCorrectnessCompanionFromTheEvidenceSet()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var proposal = fixture.SaveProposal();
        fixture.RecordAssessment(proposal, "trial-root", AssessmentKinds.ParseTime);
        fixture.RecordAssessment(proposal, "trial-correctness", AssessmentKinds.Correctness);
        fixture.CreateJob(proposal.Json, "2026-09-24T11:00:00Z", "trial/with-correctness",
            ["trial-root", "trial-correctness"]);
        var trial = Trials.LatestFor(fixture.Database, fixture.Project, proposal.Id.Value)!;
        var set = EvidenceSets.ForTrial(fixture.Database, fixture.Project, trial).Value!;

        var opened = await SelectionReader.OpenAsync(new OpenSelectionReaderRequest(
            fixture.Project.FullFwDataPath, [], []) { ShownAssessment = set });

        Assert.True(opened.Succeeded, opened.Refusal?.Message);
        await using var reader = opened.Value!;
        Assert.Equal("trial-correctness", Assert.Single(reader.Context.Evidence!.MeasurementAssessmentIds));
    }

    [Fact]
    public async Task SelectionReaderRejectsATrialRootWithAChangedIntentDigest()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var proposal = fixture.SaveProposal();
        fixture.RecordAssessment(proposal, "trial-root", AssessmentKinds.ParseTime);
        fixture.CreateJob(proposal.Json, "2026-09-24T11:00:00Z", "trial/wrong-digest", ["trial-root"]);
        var set = EvidenceSets.ForTrial(fixture.Database, fixture.Project,
            Trials.LatestFor(fixture.Database, fixture.Project, proposal.Id.Value)!).Value!;

        await AssertTrialEvidenceRefused(fixture, set with
        {
            Origin = set.Origin with { IntentDigest = "sha256:" + new string('f', 64) },
        });
    }

    [Fact]
    public async Task SelectionReaderRejectsATrialRootNamedByAnotherProposal()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var proposal = fixture.SaveProposal();
        fixture.RecordAssessment(proposal, "trial-root", AssessmentKinds.ParseTime);
        fixture.CreateJob(proposal.Json, "2026-09-24T11:00:00Z", "trial/wrong-proposal", ["trial-root"]);
        var set = EvidenceSets.ForTrial(fixture.Database, fixture.Project,
            Trials.LatestFor(fixture.Database, fixture.Project, proposal.Id.Value)!).Value!;

        await AssertTrialEvidenceRefused(fixture, set with
        {
            Origin = set.Origin with { ProposalId = CanonicalId.Mint("proposal/").Value },
        });
    }

    [Fact]
    public async Task SelectionReaderRejectsACompanionFromAnotherTrial()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var proposal = fixture.SaveProposal();
        var otherProposal = fixture.SaveProposal();
        fixture.RecordAssessment(proposal, "trial-root", AssessmentKinds.ParseTime);
        fixture.RecordAssessment(otherProposal, "other-correctness", AssessmentKinds.Correctness);
        fixture.CreateJob(proposal.Json, "2026-09-24T11:00:00Z", "trial/wrong-companion", ["trial-root"]);
        var set = EvidenceSets.ForTrial(fixture.Database, fixture.Project,
            Trials.LatestFor(fixture.Database, fixture.Project, proposal.Id.Value)!).Value!;

        await AssertTrialEvidenceRefused(fixture, set with
        {
            MeasurementIds = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [AssessmentKinds.Correctness] = "other-correctness",
            },
        });
    }

    [Fact]
    public async Task SelectionReaderRejectsAProjectRootLabeledAsTrialEvidence()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var proposal = fixture.SaveProposal();
        fixture.RecordProjectAssessment("project-root", AssessmentKinds.ParseTime, "analysed");
        fixture.CreateJob(proposal.Json, "2026-09-24T11:00:00Z", "trial/project-root", []);
        var set = EvidenceSets.ForTrial(fixture.Database, fixture.Project,
            Trials.LatestFor(fixture.Database, fixture.Project, proposal.Id.Value)!).Value!;

        await AssertTrialEvidenceRefused(fixture, set with { RootAssessmentId = "project-root" });
    }

    [Fact]
    public void ProposalWarningsReadSucceedsWhenNoTrialCheckExists()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var proposal = fixture.SaveProposal();
        fixture.RecordAssessment(proposal, "trial-root", AssessmentKinds.ParseTime);
        fixture.CreateJob(proposal.Json, "2026-09-24T11:00:00Z", "trial/no-check", ["trial-root"]);

        var result = StoredGrammarCheckQuery.Query(new GrammarCheckRequest(
            fixture.Project.FullFwDataPath, proposal.Id.Value));

        Assert.True(result.Succeeded, result.Refusal?.Message);
        Assert.Null(result.Value!.Check);
    }

    [Fact]
    public void TrialInspectUsesItsNamedRootAndWorksWithoutObjectTiming()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var proposal = fixture.SaveProposal();
        fixture.RecordAssessment(proposal, "trial-root", AssessmentKinds.ParseTime);
        var jobId = "trial/inspect-" + Guid.NewGuid().ToString("N");
        fixture.CreateJob(proposal.Json, "2026-09-24T11:00:00Z", jobId, ["trial-root"]);
        var scratch = TrialScratchPath.ForJob(jobId);
        Directory.CreateDirectory(scratch);
        File.WriteAllText(Path.Combine(scratch, "trial.fwdata"), "unreadable navigation is allowed");

        try
        {
            var subject = new InspectorSubject(InspectorSubjectKind.Warning) { WarningCode = "missing" };
            var result = InspectQuery.Query(new InspectRequest(fixture.Project.FullFwDataPath,
                subject, proposal.Id.Value));

            Assert.True(result.Succeeded, result.Refusal?.Message);
            Assert.Equal("trial-root", result.Value!.AssessmentId);
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Fact]
    public void ProposalTimingUsesTrialRootAndRejectsProjectAssessmentsAndOverrides()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var proposal = fixture.SaveProposal();
        fixture.RecordProjectAssessment("project-root", AssessmentKinds.ParseTime, "analysed");
        fixture.RecordProjectAssessment("project-object-timing", AssessmentKinds.ObjectTiming, "analysed");
        fixture.RecordAssessment(proposal, "trial-root", AssessmentKinds.ParseTime);
        fixture.CreateJob(proposal.Json, "2026-09-24T11:00:00Z", "trial/timing", ["trial-root"]);

        var timing = TimingCommand.Timing(new TimingRequest(fixture.Project.FullFwDataPath,
            ProposalId: proposal.Id.Value));
        var wrongRoot = TimingCommand.Timing(new TimingRequest(fixture.Project.FullFwDataPath,
            AssessmentId: "project-root", ProposalId: proposal.Id.Value));
        var wrongOverride = TimingCommand.Timing(new TimingRequest(fixture.Project.FullFwDataPath,
            ProposalId: proposal.Id.Value, OverrideAssessmentIds: ["project-root"]));
        var statsWrong = StatsCommand.Stats(new StatsRequest(fixture.Project.FullFwDataPath,
            "project-object-timing", StatsOutputKind.JsonRows, [], proposal.Id.Value), parserPath: null);
        var statsMissing = StatsCommand.Stats(new StatsRequest(fixture.Project.FullFwDataPath,
            null, StatsOutputKind.JsonRows, [], proposal.Id.Value), parserPath: null);

        Assert.True(timing.Succeeded, timing.Refusal?.Message);
        Assert.Equal("trial-root", timing.Value!.AssessmentId);
        Assert.Equal("timing.wrong-assessment", wrongRoot.Refusal?.Code);
        Assert.Equal("timing.invalid-override", wrongOverride.Refusal?.Code);
        Assert.Equal("stats.wrong-assessment", statsWrong.Refusal?.Code);
        Assert.Equal("stats.no-assessment", statsMissing.Refusal?.Code);
    }

    private static async Task AssertTrialEvidenceRefused(TrialsTests.ProjectFixture fixture, EvidenceSet set)
    {
        var result = await SelectionReader.OpenAsync(new OpenSelectionReaderRequest(
            fixture.Project.FullFwDataPath, [], []) { ShownAssessment = set });

        Assert.False(result.Succeeded);
        Assert.Equal("texts.evidence-changed", result.Refusal?.Code);
    }

    [Fact]
    public async Task ATrialRootIsNeverTheCurrentEvidence()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var proposal = fixture.SaveProposal();
        fixture.RecordAssessment(proposal, "trial-root", AssessmentKinds.ParseTime);
        fixture.CreateJob(proposal.Json, "2026-09-24T11:00:00Z", "trial", ["trial-root"]);
        var trial = Trials.LatestFor(fixture.Database, fixture.Project, proposal.Id.Value)!;
        var set = EvidenceSets.ForTrial(fixture.Database, fixture.Project, trial);
        Assert.True(set.Succeeded, set.Refusal?.Message);
        var opened = await SelectionReader.OpenAsync(new OpenSelectionReaderRequest(
            fixture.Project.FullFwDataPath, [], []) { ShownAssessment = set.Value });
        Assert.True(opened.Succeeded, opened.Refusal?.Message);
        await using var reader = opened.Value!;

        using var connection = fixture.Database.OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: true);
        var isCurrent = ExpectedContextRepository.IsCurrentContext(fixture.Database, connection, transaction,
            ProjectWorkspaceKey.Compute(fixture.Project), reader.Context.ExpectedWriteContext());

        Assert.False(isCurrent);
    }

    [Fact]
    public void TraceForATrialWithoutScratchRefusesScratchGone()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var proposal = fixture.SaveProposal();
        fixture.CreateJob(proposal.Json, "2026-09-24T11:00:00Z", "trial", []);

        var result = WordTraceQuery.Query(
            new WordTraceRequest(fixture.Project.FullFwDataPath, "cat", proposal.Id.Value), parserPath: null);

        Assert.False(result.Succeeded);
        Assert.Equal("trial.scratch-gone", result.Refusal?.Code);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
