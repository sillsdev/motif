using SIL.Motif.Commands;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class TrialDryRunExemptionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SIL.Motif.TrialDryRunTests",
        Guid.NewGuid().ToString("N"));

    public TrialDryRunExemptionTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void ACurrentTrialsDryRunIsWhatTheJudgmentOnlyExemptionReads()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var proposal = fixture.SaveProposal();
        fixture.CreateJob(proposal.Json, "2026-09-24T11:00:00Z", "trial", []);

        var dryRun = ProposalCommands.TrialDryRun(fixture.Database, fixture.Project, proposal.Id);

        Assert.Equal(proposal.Digest, dryRun!.IntentDigest);
    }

    [Fact]
    public void AStaleTrialsDryRunIsNotUsed()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var proposal = fixture.SaveProposal();
        fixture.CreateJob(proposal.Json, "2026-09-24T11:00:00Z", "trial", []);
        fixture.RecordBaseline("sha256:" + new string('c', 64), "2026-09-24T13:00:00Z");

        Assert.Null(ProposalCommands.TrialDryRun(fixture.Database, fixture.Project, proposal.Id));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
