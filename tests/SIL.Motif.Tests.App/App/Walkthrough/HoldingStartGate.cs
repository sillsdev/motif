using SIL.Motif.App.Services;

namespace SIL.Motif.Tests.App.Walkthrough;

/// <summary>Holds the real command client's Assessment, Handoff or word read-state before it starts, until released.</summary>
internal sealed class HoldingStartGate : ICommandStartGate
{
    private TaskCompletionSource _assess;
    private readonly TaskCompletionSource _handoff;
    private TaskCompletionSource _wordReadState = NewGate(held: false);

    internal HoldingStartGate(bool holdAssess = false, bool holdHandoff = false)
    {
        _assess = NewGate(holdAssess);
        _handoff = NewGate(holdHandoff);
    }

    internal int Waiting { get; private set; }

    internal void HoldAssess() => _assess = NewGate(held: true);

    internal void ReleaseAssess() => _assess.TrySetResult();

    internal void ReleaseHandoff() => _handoff.TrySetResult();

    internal void HoldWordReadState() => _wordReadState = NewGate(held: true);

    internal void ReleaseWordReadState() => _wordReadState.TrySetResult();

    public Task WaitToStartAsync(GatedCommand command)
    {
        Waiting++;
        return command switch
        {
            GatedCommand.Assess => _assess.Task,
            GatedCommand.Handoff => _handoff.Task,
            _ => _wordReadState.Task,
        };
    }

    private static TaskCompletionSource NewGate(bool held)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!held) gate.SetResult();
        return gate;
    }
}
