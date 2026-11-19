using SIL.Motif.Commands;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Parser;
using SIL.Motif.Tests.TestFixtures;
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
        using var parser = AmbientEnvironment.Push(new Dictionary<string, string?>
        {
            [PanGlossExecutable.PathVariable] = FakeParser.ExecutablePath,
        });
        var proposal = fixture.SaveProposal();
        var before = fixture.RecordPinnedAssessmentPair(proposal, "after", AssessmentKinds.Correctness);
        fixture.CreateJob(proposal.Json, "2026-09-24T11:00:00Z", "trial", ["after"], baselineAssessmentIds: [before]);

        var dryRun = ProposalCommands.TrialDryRun(fixture.Database, fixture.Project, proposal.Id);

        Assert.NotNull(dryRun);
        Assert.Equal(proposal.Digest, dryRun.IntentDigest);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnUnverifiedOrStaleTrialsDryRunIsNotUsed(bool baselineMoved)
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        using var parser = AmbientEnvironment.Push(new Dictionary<string, string?>
        {
            [PanGlossExecutable.PathVariable] = FakeParser.ExecutablePath,
        });
        var proposal = fixture.SaveProposal();
        if (baselineMoved)
        {
            var before = fixture.RecordPinnedAssessmentPair(proposal, "after", AssessmentKinds.Correctness);
            fixture.CreateJob(proposal.Json, "2026-09-24T11:00:00Z", "trial", ["after"], baselineAssessmentIds: [before]);
            fixture.RecordBaseline("sha256:" + new string('c', 64), "2026-09-24T13:00:00Z",
                semanticDigest: "sha256:" + new string('2', 64));
        }
        else
        {
            fixture.CreateJob(proposal.Json, "2026-09-24T11:00:00Z", "trial", []);
        }

        Assert.Null(ProposalCommands.TrialDryRun(fixture.Database, fixture.Project, proposal.Id));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
