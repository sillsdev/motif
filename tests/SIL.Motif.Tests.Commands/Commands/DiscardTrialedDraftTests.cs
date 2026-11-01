using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Phonology;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>A never-finalized Draft that was tried leaves Assessments and jobs behind; discarding it must not trip a key.</summary>
public sealed class DiscardTrialedDraftTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SIL.Motif.DiscardTrialedDraftTests",
        Guid.NewGuid().ToString("N"));

    public DiscardTrialedDraftTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void ATrialedNeverFinalizedDraftIsDiscardedWithItsTrialsEvidenceAndDifference()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var draft = Trialed(fixture, "d", "trial-1", "draft-assessment");
        PublishDifference(fixture, draft.Id.Value);

        var discarded = Discard(fixture, "d");

        Assert.True(discarded.Succeeded, discarded.Refusal?.Message);
        Assert.False(new ProposalRepository(fixture.Database).DraftNameExists("d"));
        Assert.Null(new JobRepository(fixture.Database).Get("trial-1"));
        Assert.Throws<KeyNotFoundException>(() => new AssessmentRepository(fixture.Database).Get("draft-assessment"));
        Assert.Equal(0, Count(fixture, "SELECT COUNT(*) FROM ProposalDifferences;"));
        Assert.Equal(0, Count(fixture, "SELECT COUNT(*) FROM Proposals;"));
    }

    [Fact]
    public void ADraftWhoseTrialIsStillRunningIsKeptWithANamedRefusal()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var draft = Trialed(fixture, "d", "trial-1", "draft-assessment");
        fixture.CreateRunningJob(draft.Json, "2026-09-24T12:00:00Z", "running");

        var discarded = Discard(fixture, "d");

        Assert.Equal("draft.trial-running", discarded.Refusal?.Code);
        Assert.True(new ProposalRepository(fixture.Database).DraftNameExists("d"));
        Assert.NotNull(new JobRepository(fixture.Database).Get("trial-1"));
        Assert.Equal("draft-assessment", new AssessmentRepository(fixture.Database).Get("draft-assessment").AssessmentId);
    }

    [Fact]
    public void ADraftWhoseResultAnotherProposalsReportCitesIsKeptWithANamedRefusalAndNothingChanges()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var draft = Trialed(fixture, "d", "trial-1", "draft-assessment");
        var other = fixture.SaveProposal();
        Sql(fixture, "INSERT INTO Reports (ReportId, ProposalId, AssessmentId, ReportJson, CreatedUtc) VALUES " +
            $"('other-report', '{other.Id.Value}', 'draft-assessment', '{{\"kind\":\"test\"}}', '2026-09-24T12:00:00Z');");

        var discarded = Discard(fixture, "d");

        Assert.Equal("draft.evidence-retained", discarded.Refusal?.Code);
        Assert.True(new ProposalRepository(fixture.Database).DraftNameExists("d"));
        Assert.NotNull(new JobRepository(fixture.Database).Get("trial-1"));
        Assert.Equal(1, Count(fixture, "SELECT COUNT(*) FROM Reports WHERE ReportId = 'other-report';"));
    }

    [Fact]
    public void ADraftAnExampleSetRetainsIsKeptWithANamedRefusal()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var draft = Trialed(fixture, "d", "trial-1", "draft-assessment");
        new PhonologyExampleSetRepository(fixture.Database).Save("Retained examples",
            [new PhonologyExample("example-1", "underlying", ["surface"], new PhonologyGenerationContext.HcXmlEntry("entry-1"))],
            null, draft.Id.Value);

        var discarded = Discard(fixture, "d");

        Assert.Equal("draft.retained-by-example-set", discarded.Refusal?.Code);
        Assert.True(new ProposalRepository(fixture.Database).DraftNameExists("d"));
        Assert.NotNull(new JobRepository(fixture.Database).Get("trial-1"));
    }

    private static CommandOutcome<DraftDiscardedResponse> Discard(TrialsTests.ProjectFixture fixture, string name) =>
        ProposalCommands.DiscardDraft(new DiscardDraftRequest(fixture.Project.FullFwDataPath, "1.0", name));

    private static TrialsTests.SavedProposal Trialed(TrialsTests.ProjectFixture fixture, string name, string jobId,
        string assessmentId)
    {
        var path = fixture.Project.FullFwDataPath;
        Assert.True(ProposalCommands.New(new NewDraftRequest(path, "1.0", name, null)).Succeeded);
        Assert.True(ProposalCommands.AddSetGloss(new AddSetGlossRequest(path, "1.0", name, CanonicalId.Mint().Value, "en",
            "hello")).Succeeded);
        var record = new ProposalRepository(fixture.Database).GetDraft(name);
        var json = ProposalCommands.BuildProposalJson(ProposalCommands.DeserializeDraft(record.ProposalJson!));
        var saved = new TrialsTests.SavedProposal(record.ProposalId, json,
            SIL.Motif.Contract.Canonicalization.IntentDigest.Compute(SIL.Motif.Contract.Parsing.ProposalJsonParser.Parse(json)));
        fixture.RecordAssessment(saved, assessmentId, AssessmentKinds.ParseTime);
        fixture.CreateJob(json, "2026-09-24T11:00:00Z", jobId, [assessmentId]);
        return saved;
    }

    private static void PublishDifference(TrialsTests.ProjectFixture fixture, string proposalId)
    {
        var pin = new ProposalDifferenceSidePin(null, null, "sha256:grammar", "Default", "sha256:selection", "whitespace", "1");
        new ProposalDifferenceRepository(fixture.Database).Publish(new NewProposalDifference(new ProposalDifferenceSummary(
            "difference/1", proposalId, "trial-1", "draft-assessment", "draft-assessment", "text/1", "Text", "baseline/1",
            new ProposalDifferencePolicy(), 0, 0, 0, 0, 0, 0, [], [], "pangloss", null, pin, pin, [], 0, [],
            "2026-10-13T00:00:00Z"), []));
    }

    private static void Sql(TrialsTests.ProjectFixture fixture, string sql)
    {
        using var connection = fixture.Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static long Count(TrialsTests.ProjectFixture fixture, string sql)
    {
        using var connection = fixture.Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }
}
