using SIL.Motif.Commands;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// A Trial enqueue reads its Proposal, then queues the job in a later write. These tests discard the Draft between
/// the two, in order, so the race needs no timing.
/// </summary>
public sealed class TrialEnqueueDiscardRaceTests : IDisposable
{
    private const string Created = "2026-09-24T12:00:00Z";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "SIL.Motif.TrialEnqueueDiscardRaceTests",
        Guid.NewGuid().ToString("N"));

    public TrialEnqueueDiscardRaceTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void ATrialIsQueuedWhileItsDraftStillExists()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var (repository, draft) = NewDraft(fixture, "d");

        var queued = JobCommands.QueueTrial(new JobRepository(fixture.Database), draft, "job-1", Key(fixture), "{}", null);

        Assert.True(queued.Succeeded, queued.Refusal?.Message);
        Assert.NotNull(new JobRepository(fixture.Database).Get("job-1"));
        Assert.True(repository.DraftNameExists("d"));
    }

    [Fact]
    public void ATrialIsRefusedAndNoJobIsQueuedWhenItsDraftWasDiscardedAfterTheEnqueueReadIt()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var (repository, draft) = NewDraft(fixture, "d");
        Assert.False(repository.DiscardDraft("d"));

        var queued = JobCommands.QueueTrial(new JobRepository(fixture.Database), draft, "job-1", Key(fixture), "{}", null);

        Assert.Equal(RefusalCodes.TrialProposalGone, queued.Refusal?.Code);
        Assert.Null(new JobRepository(fixture.Database).Get("job-1"));
        Assert.Equal(0, Count(fixture, "SELECT COUNT(*) FROM Jobs;"));
    }

    [Fact]
    public void ADraftReopenedFromAProposalAndThenDiscardedRefusesTheTrialToo()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var proposal = fixture.SaveProposal();
        var repository = new ProposalRepository(fixture.Database);
        Sql(fixture, "UPDATE Proposals SET DraftName = 'again', DraftJson = '{}' " +
            $"WHERE ProposalId = '{proposal.Id.Value}';");
        var draft = repository.GetDraft("again");
        Assert.True(repository.DiscardDraft("again"));

        var jobs = new JobRepository(fixture.Database);
        var queued = JobCommands.QueueTrial(jobs, draft, "job-1", Key(fixture), "{}", null);

        Assert.Equal(RefusalCodes.TrialProposalGone, queued.Refusal?.Code);
        Assert.Null(jobs.Get("job-1"));
        Assert.True(JobCommands.QueueTrial(jobs, repository.Get(proposal.Id), "job-2", Key(fixture), "{}", null).Succeeded);
    }

    [Fact]
    public void ADraftFinalizedAfterTheEnqueueReadItRefusesTheTrialWithoutNamingOnlyDiscardOrDelete()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var (_, draft) = NewDraft(fixture, "d");
        Sql(fixture, "UPDATE Proposals SET DraftName = NULL, DraftJson = NULL WHERE DraftName = 'd';");

        var queued = JobCommands.QueueTrial(new JobRepository(fixture.Database), draft, "job-1", Key(fixture), "{}", null);

        Assert.Equal(RefusalCodes.TrialProposalGone, queued.Refusal?.Code);
        Assert.Contains("finalized", queued.Refusal!.Message, StringComparison.Ordinal);
        Assert.Equal(0, Count(fixture, "SELECT COUNT(*) FROM Jobs;"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private static (ProposalRepository Repository, ProposalRecord Draft) NewDraft(TrialsTests.ProjectFixture fixture,
        string name)
    {
        var repository = new ProposalRepository(fixture.Database);
        repository.CreateDraft(name, CanonicalId.Mint("proposal/"), "{}");
        return (repository, repository.GetDraft(name));
    }

    private static string Key(TrialsTests.ProjectFixture fixture) => ProjectWorkspaceKey.Compute(fixture.Project);

    private static int Count(TrialsTests.ProjectFixture fixture, string sql)
    {
        using var connection = fixture.Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void Sql(TrialsTests.ProjectFixture fixture, string sql)
    {
        using var connection = fixture.Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
