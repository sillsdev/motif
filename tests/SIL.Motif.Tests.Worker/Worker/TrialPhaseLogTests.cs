using System.Text.Json;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Worker.Jobs;
using Xunit;

namespace SIL.Motif.Tests.Worker;

/// <summary>Pins the Trial receipt's phase list: stable names, every phase present, and old receipts still reading.</summary>
public sealed class TrialPhaseLogTests
{
    [Fact]
    public void ASnapshotListsEveryPhaseInOrderAndNotesThoseThatDidNotRun()
    {
        var log = new TrialPhaseLog();
        log.AddRange([new TrialPhase(TrialPhaseNames.CliPrequeue, 1.5), new TrialPhase(TrialPhaseNames.ParseBefore, 0.2) { Note = TrialPhaseNames.Reused }]);
        log.Add(TrialPhaseNames.ParseAfter, 2);
        log.Add(TrialPhaseNames.ParseAfter, 3);

        var snapshot = log.Snapshot();

        Assert.Equal(TrialPhaseNames.All, snapshot.Select(phase => phase.Name));
        Assert.Equal(5, snapshot.Single(phase => phase.Name == TrialPhaseNames.ParseAfter).Seconds);
        Assert.Equal(TrialPhaseNames.Reused, snapshot.Single(phase => phase.Name == TrialPhaseNames.ParseBefore).Note);
        Assert.Equal(TrialPhaseNames.NotRun, snapshot.Single(phase => phase.Name == TrialPhaseNames.Cleanup).Note);
    }

    [Fact]
    public void AReceiptWithoutPhasesStillReads()
    {
        var record = JsonSerializer.Deserialize<TrialRecord>("{}", SIL.Motif.Contract.MotifJson.CreateOptions());
        Assert.Null(record?.Phases);
    }
}
