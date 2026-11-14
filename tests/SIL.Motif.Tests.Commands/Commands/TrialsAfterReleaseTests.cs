using SIL.Motif.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Commands;

// Trials read back correctly once a newer Trial released the Assessments of the ones it superseded (ADR 0113).
public sealed class TrialsAfterReleaseTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SIL.Motif.TrialsAfterReleaseTests",
        Guid.NewGuid().ToString("N"));

    public TrialsAfterReleaseTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void TheLatestTrialReadsBackAfterTheOlderOnesAssessmentsAreReleased()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var proposal = fixture.SaveProposal();
        fixture.RecordAssessment(proposal, "root/old", "ParseTime");
        fixture.RecordAssessment(proposal, "root/new", "ParseTime");
        fixture.CreateJob(proposal.Json, "2026-09-24T11:00:00Z", "trial/old", ["root/old"]);
        var latest = fixture.CreateJob(proposal.Json, "2026-09-24T12:00:00Z", "trial/new", ["root/new"]);
        Release(fixture, "root/old");

        var read = Trials.Read(new ReadTrialRequest(fixture.Project.FullFwDataPath, proposal.Id.Value,
            IncludeWordResults: false));

        Assert.True(read.Succeeded, read.Refusal?.Message);
        Assert.Equal(latest, read.Value!.JobId);
        Assert.Equal(latest, Trials.LatestFor(fixture.Database, fixture.Project, proposal.Id.Value)!.JobId);
        Assert.Equal([latest], Trials.ListFor(fixture.Database, fixture.Project, proposal.Id.Value).Select(t => t.JobId));
        Assert.Equal(latest, Trials.LatestForAll(fixture.Database, fixture.Project)[proposal.Id.Value].JobId);
    }

    [Fact]
    public void ANewestTrialThatCannotBeBuiltReadsAsNotTriedNeverAsAnOlderVerdict()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var proposal = fixture.SaveProposal();
        fixture.RecordAssessment(proposal, "root/old", "ParseTime");
        fixture.RecordAssessment(proposal, "root/new", "ParseTime");
        fixture.CreateJob(proposal.Json, "2026-09-24T11:00:00Z", "trial/old", ["root/old"]);
        fixture.CreateJob(proposal.Json, "2026-09-24T12:00:00Z", "trial/new", ["root/new"]);
        Release(fixture, "root/new");

        Assert.Null(Trials.LatestFor(fixture.Database, fixture.Project, proposal.Id.Value));
        Assert.False(Trials.LatestForAll(fixture.Database, fixture.Project).ContainsKey(proposal.Id.Value));
        Assert.Equal(TrialStanding.NotTried,
            TrialStanding.Of(Trials.LatestFor(fixture.Database, fixture.Project, proposal.Id.Value)));
        var read = Trials.Read(new ReadTrialRequest(fixture.Project.FullFwDataPath, proposal.Id.Value,
            IncludeWordResults: false));
        Assert.True(read.Succeeded);
        Assert.Null(read.Value);
    }

    private static void Release(TrialsTests.ProjectFixture fixture, string assessmentId)
    {
        using var connection = fixture.Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = OFF; DELETE FROM Assessments WHERE AssessmentId = $id;";
        command.Parameters.AddWithValue("$id", assessmentId);
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
