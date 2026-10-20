using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Host.Installation;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>Tests that hold the machine's update gate; they run alone so no sibling test sees it held.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class UpdateGateCollection
{
    public const string Name = "Update gate";
}

[Collection(UpdateGateCollection.Name)]
public sealed class TrialUpdateGateTests
{
    [Fact]
    public void ATrialIsRefusedWithoutAssessingWhileAnUpdateHoldsTheGate()
    {
        var request = new EnqueueTrialRequest("missing.fwdata", "1.0", "proposal/one", Words: ["sita"]);
        var assessed = false;

        using var update = MotifUpdateGate.TryAcquireForUpdate();
        Assert.NotNull(update);
        var refused = TrialWorkflow.Enqueue(request, _ =>
        {
            assessed = true;
            throw new InvalidOperationException("must not assess");
        }, BaselineMode.BestEffort);

        Assert.Equal("trial.update-in-progress", refused.Refusal!.Code);
        Assert.Equal("Motif is installing an update. Try the Trial again in a moment.", refused.Refusal.Message);
        Assert.False(assessed);
    }
}
