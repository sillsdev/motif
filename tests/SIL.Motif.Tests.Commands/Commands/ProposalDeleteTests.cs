using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Review;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Phonology;
using SIL.Motif.Contract.Responses;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class ProposalDeleteTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SIL.Motif.ProposalDeleteTests",
        Guid.NewGuid().ToString("N"));

    public ProposalDeleteTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("rejected")]
    [InlineData("superseded")]
    [InlineData("withdrawn")]
    public void AClosedProposalThatNeverAppliedIsDeletedWithItsTrials(string status)
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var proposal = fixture.SaveProposal();
        fixture.RecordAssessment(proposal, "trial-root", AssessmentKinds.ParseTime);
        fixture.CreateJob(proposal.Json, "2026-09-24T11:00:00Z", "trial", ["trial-root"]);
        new ProposalRepository(fixture.Database).SetStatus(proposal.Id, status,
            status == "superseded" ? proposal.Id.Value : null);

        var deleted = ProposalLifecycleCommand.Delete(fixture.Database, fixture.Project, proposal.Id.Value);

        Assert.True(deleted.Succeeded, deleted.Refusal?.Message);
        Assert.Equal(1, deleted.Value!.TrialsDeleted);
        Assert.Throws<KeyNotFoundException>(() => new ProposalRepository(fixture.Database).Get(proposal.Id));
        Assert.Null(new JobRepository(fixture.Database).Get("trial"));
        Assert.Throws<KeyNotFoundException>(() => new AssessmentRepository(fixture.Database).Get("trial-root"));
    }

    [Fact]
    public void AnAppliedProposalIsFinalAndIsNeverDeleted()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var proposal = fixture.SaveProposal();
        new ProposalRepository(fixture.Database).SetStatus(proposal.Id, "applied", null);

        var deleted = ProposalLifecycleCommand.Delete(fixture.Database, fixture.Project, proposal.Id.Value);

        Assert.Equal("proposal.applied-final", deleted.Refusal?.Code);
        Assert.Equal("applied", new ProposalRepository(fixture.Database).Get(proposal.Id).Status);
    }

    [Theory]
    [InlineData("proposed")]
    [InlineData("deferred")]
    public void AnOpenProposalIsNotDeleted(string status)
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var proposal = fixture.SaveProposal();
        if (status != "proposed") new ProposalRepository(fixture.Database).SetStatus(proposal.Id, status, null);

        var deleted = ProposalLifecycleCommand.Delete(fixture.Database, fixture.Project, proposal.Id.Value);

        Assert.Equal("proposal.not-closed", deleted.Refusal?.Code);
        Assert.Equal(status, new ProposalRepository(fixture.Database).Get(proposal.Id).Status);
    }

    [Fact]
    public void AProposalWhoseTrialIsRunningIsNotDeleted()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var proposal = fixture.SaveProposal();
        fixture.CreateRunningJob(proposal.Json, "2026-09-24T11:00:00Z", "running");
        new ProposalRepository(fixture.Database).SetStatus(proposal.Id, "rejected", null);

        var deleted = ProposalLifecycleCommand.Delete(fixture.Database, fixture.Project, proposal.Id.Value);

        Assert.Equal("proposal.trial-running", deleted.Refusal?.Code);
        Assert.Equal(JobStatus.Running, new JobRepository(fixture.Database).Get("running")!.Status);
    }

    [Fact]
    public void AnExampleSetReferenceRefusesDeletionWithoutChangingEvidence()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var proposal = fixture.SaveProposal();
        fixture.RecordAssessment(proposal, "retained-assessment", AssessmentKinds.ParseTime);
        fixture.CreateJob(proposal.Json, "2026-09-24T11:00:00Z", "retained-trial", ["retained-assessment"]);
        new ProposalRepository(fixture.Database).SetStatus(proposal.Id, "rejected", null);
        var exampleSet = new PhonologyExampleSetRepository(fixture.Database).Save("Retained examples",
            [new PhonologyExample("example-1", "underlying", ["surface"],
                new PhonologyGenerationContext.HcXmlEntry("entry-1"))], null, proposal.Id.Value);
        ReviewMarks.Put(fixture.Database, proposal.Id.Value, "entry-1", "content-digest", "evidence-digest");
        InsertReport(fixture.Database, proposal.Id.Value, "retained-assessment", "retained-report");
        var proposalBefore = new ProposalRepository(fixture.Database).Get(proposal.Id);
        var jobBefore = new JobRepository(fixture.Database).Get("retained-trial");
        var assessmentBefore = new AssessmentRepository(fixture.Database).Get("retained-assessment");
        var reportBefore = ReadReport(fixture.Database, "retained-report");
        var reviewMarkBefore = ReviewMarks.Read(fixture.Database, proposal.Id.Value)["entry-1"];

        var deleted = ProposalLifecycleCommand.Delete(fixture.Database, fixture.Project, proposal.Id.Value);

        Assert.Equal("proposal.retained-by-example-set", deleted.Refusal?.Code);
        Assert.Contains("phonology Example Set", deleted.Refusal!.Message, StringComparison.Ordinal);
        Assert.Equal(JsonSerializer.Serialize(proposalBefore), JsonSerializer.Serialize(
            new ProposalRepository(fixture.Database).Get(proposal.Id)));
        Assert.Equal(jobBefore, new JobRepository(fixture.Database).Get("retained-trial"));
        Assert.Equal(JsonSerializer.Serialize(assessmentBefore), JsonSerializer.Serialize(
            new AssessmentRepository(fixture.Database).Get("retained-assessment")));
        Assert.Equal(reportBefore, ReadReport(fixture.Database, "retained-report"));
        Assert.Equal(reviewMarkBefore, ReviewMarks.Read(fixture.Database, proposal.Id.Value)["entry-1"]);
        Assert.Equal(exampleSet, new PhonologyExampleSetRepository(fixture.Database).Read(
            exampleSet.Revision.RevisionId, 20, null).ExampleSet);
    }

    [Fact]
    public void AFailureAtTheFinalProposalDeleteRollsBackAllAssociatedCleanup()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var proposal = fixture.SaveProposal();
        fixture.RecordAssessment(proposal, "rollback-assessment", AssessmentKinds.ParseTime);
        fixture.CreateJob(proposal.Json, "2026-09-24T11:00:00Z", "rollback-trial", ["rollback-assessment"]);
        new ProposalRepository(fixture.Database).SetStatus(proposal.Id, "rejected", null);
        ReviewMarks.Put(fixture.Database, proposal.Id.Value, "entry-1", "content-digest", "evidence-digest");
        InsertReport(fixture.Database, proposal.Id.Value, "rollback-assessment", "rollback-report");
        var proposalBefore = new ProposalRepository(fixture.Database).Get(proposal.Id);
        var jobBefore = new JobRepository(fixture.Database).Get("rollback-trial");
        var assessmentBefore = new AssessmentRepository(fixture.Database).Get("rollback-assessment");
        var reportBefore = ReadReport(fixture.Database, "rollback-report");
        var reviewMarkBefore = ReviewMarks.Read(fixture.Database, proposal.Id.Value)["entry-1"];
        Execute(fixture.Database, "CREATE TRIGGER FailFinalProposalDelete BEFORE DELETE ON Proposals " +
            "BEGIN SELECT RAISE(ABORT, 'injected final delete failure'); END;");

        Assert.ThrowsAny<SqliteException>(() =>
            ProposalLifecycleCommand.Delete(fixture.Database, fixture.Project, proposal.Id.Value));

        Assert.Equal(JsonSerializer.Serialize(proposalBefore), JsonSerializer.Serialize(
            new ProposalRepository(fixture.Database).Get(proposal.Id)));
        Assert.Equal(jobBefore, new JobRepository(fixture.Database).Get("rollback-trial"));
        Assert.Equal(JsonSerializer.Serialize(assessmentBefore), JsonSerializer.Serialize(
            new AssessmentRepository(fixture.Database).Get("rollback-assessment")));
        Assert.Equal(reportBefore, ReadReport(fixture.Database, "rollback-report"));
        Assert.Equal(reviewMarkBefore, ReviewMarks.Read(fixture.Database, proposal.Id.Value)["entry-1"]);
    }

    private static void InsertReport(SIL.Motif.Host.Store.MotifDatabase database, string proposalId,
        string assessmentId, string reportId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO Reports (ReportId, ProposalId, AssessmentId, ReportJson, CreatedUtc) " +
            "VALUES ($reportId, $proposalId, $assessmentId, '{\"kind\":\"test\"}', '2026-09-24T12:00:00Z');";
        command.Parameters.AddWithValue("$reportId", reportId);
        command.Parameters.AddWithValue("$proposalId", proposalId);
        command.Parameters.AddWithValue("$assessmentId", assessmentId);
        command.ExecuteNonQuery();
    }

    private static string ReadReport(SIL.Motif.Host.Store.MotifDatabase database, string reportId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ProposalId || '|' || AssessmentId || '|' || ReportJson || '|' || CreatedUtc " +
            "FROM Reports WHERE ReportId = $reportId;";
        command.Parameters.AddWithValue("$reportId", reportId);
        return (string)command.ExecuteScalar()!;
    }

    private static void Execute(SIL.Motif.Host.Store.MotifDatabase database, string sql)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    [Fact]
    public void AProposalWhoseAssessmentIsPinnedIsRefusedAndKeepsItsTrialsAndReviewMarks()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var proposal = fixture.SaveProposal();
        fixture.RecordAssessment(proposal, "pinned-root", AssessmentKinds.ParseTime);
        fixture.CreateJob(proposal.Json, "2026-09-24T11:00:00Z", "trial", ["pinned-root"]);
        new ProposalRepository(fixture.Database).SetStatus(proposal.Id, "rejected", null);
        SIL.Motif.Commands.Review.ReviewMarks.Put(fixture.Database, proposal.Id.Value, "entry", "content", "evidence");
        using (var connection = fixture.Database.OpenConnection())
        using (var pin = connection.CreateCommand())
        {
            pin.CommandText = "INSERT INTO AssessmentPins (AssessmentId, PinnedBy, PinnedUtc) " +
                "VALUES ('pinned-root', 'test', '2026-09-24T11:00:00Z');";
            pin.ExecuteNonQuery();
        }

        var deleted = ProposalLifecycleCommand.Delete(fixture.Database, fixture.Project, proposal.Id.Value);

        Assert.Equal("proposal.applied-final", deleted.Refusal?.Code);
        Assert.NotNull(new ProposalRepository(fixture.Database).Get(proposal.Id));
        Assert.NotNull(new JobRepository(fixture.Database).Get("trial"));
        Assert.NotNull(new AssessmentRepository(fixture.Database).Get("pinned-root"));
        Assert.Single(SIL.Motif.Commands.Review.ReviewMarks.Read(fixture.Database, proposal.Id.Value));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
    }
}
