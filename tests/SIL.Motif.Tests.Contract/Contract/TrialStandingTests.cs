using System.Text.Json;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Contract;

public sealed class TrialStandingTests
{
    [Theory]
    [InlineData(JobStatus.Queued)]
    [InlineData(JobStatus.WaitingForBaseline)]
    [InlineData(JobStatus.WaitingForProjectHost)]
    [InlineData(JobStatus.Running)]
    public void AnActiveTrialIsTrying(JobStatus status) =>
        Assert.Equal(TrialStanding.Trying, TrialStanding.From(status, TrialFreshness.Incomplete));

    [Theory]
    [InlineData(JobStatus.Failed)]
    [InlineData(JobStatus.Cancelled)]
    [InlineData(JobStatus.Interrupted)]
    [InlineData(JobStatus.CompletedWithAssessmentFailure)]
    [InlineData(JobStatus.CompletedDryRunOnly)]
    [InlineData(JobStatus.Completed)]
    public void ATrialThatStoppedWithoutACompleteResultFailed(JobStatus status) =>
        Assert.Equal(TrialStanding.Failed, TrialStanding.From(status, TrialFreshness.Incomplete));

    [Fact]
    public void ACompletedTrialIsCurrentOrNeedsAnotherTryWithItsReason()
    {
        Assert.Equal(TrialStanding.Current, TrialStanding.From(JobStatus.Completed, TrialFreshness.Current));
        Assert.Equal(TrialStanding.NeedsAnotherTry(TrialStaleReason.BaselineMoved),
            TrialStanding.From(JobStatus.Completed, TrialFreshness.BaselineMoved));
        Assert.Equal(TrialStanding.NeedsAnotherTry(TrialStaleReason.ProposalChanged),
            TrialStanding.From(JobStatus.Completed, TrialFreshness.ProposalChanged));
    }

    [Fact]
    public void NoTrialIsNotTriedAndOnlyARunningTrialBlocksTryingAgain()
    {
        Assert.Equal(TrialStanding.NotTried, TrialStanding.Of(null));
        Assert.True(TrialStanding.NotTried.CanTryAgain);
        Assert.True(TrialStanding.Failed.CanTryAgain);
        Assert.True(TrialStanding.NeedsAnotherTry(TrialStaleReason.ProposalChanged).CanTryAgain);
        Assert.False(TrialStanding.Trying.CanTryAgain);
    }

    [Fact]
    public void ARecordReportsItsStandingInItsJson()
    {
        var record = new TrialRecord("job", "proposal", "sha256:digest",
            new BaselineToken("project", "sha256:" + new string('1', 64), "projection-v1", "2026-09-24T10:00:00Z",
                "sha256:" + new string('2', 64)), null,
            new Dictionary<string, string>(), [], false, null, JobStatus.Completed, null,
            TrialFreshness.ProposalChanged);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(record, MotifJson.CreateOptions()));

        var standing = json.RootElement.GetProperty(nameof(TrialRecord.Standing));
        Assert.Equal("NeedsAnotherTry", standing.GetProperty(nameof(TrialStanding.Kind)).GetString());
        Assert.Equal("ProposalChanged", standing.GetProperty(nameof(TrialStanding.Reason)).GetString());
    }
}
