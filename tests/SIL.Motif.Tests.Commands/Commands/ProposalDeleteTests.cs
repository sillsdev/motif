using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Responses;
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

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
    }
}
