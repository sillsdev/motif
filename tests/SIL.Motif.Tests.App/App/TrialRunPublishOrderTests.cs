using System.Collections.Concurrent;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Jobs;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// The window's Trial states reach its bound properties through the context it was made on, in order behind
/// whatever that context already holds, so a caller that is not on that context can see a finished Try again still
/// running until the context has run.
/// </summary>
/// <remarks>
/// xUnit resumes a test body on the pool, not on the context the test started on, so a test that makes the window
/// and then awaits a Try again from its body is such a caller. <see cref="SingleThreadPump"/> is the fix for a
/// test; the window's own threads already are on the context.
/// </remarks>
public sealed class TrialRunPublishOrderTests
{
    private const string ProjectPath = @"C:\projects\one.fwdata";
    private const string ProposalId = "proposal/one";

    [Fact]
    public async Task ATryAgainEndedOffTheWindowsContextStaysRunningUntilThatContextRuns()
    {
        var fake = new FakeCommandClient();
        var window = new HeldContext();
        var context = MakeOn(window, fake);
        await context.OpenProjectAsync(ProjectPath);
        window.Run();
        var waiting = new TaskCompletionSource<CommandOutcome<JobStatusResponse>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fake.OnWaitForJob((_, _, _) =>
        {
            reached.SetResult();
            return waiting.Task;
        });

        var trying = Task.Run(() => context.TryAgainAsync(ProposalId, CancellationToken.None));
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(30));
        window.Run();
        Assert.True(context.TrialRun.IsActive);
        waiting.SetResult(CommandOutcome<JobStatusResponse>.Success(Status()));
        var outcome = await trying;

        Assert.True(outcome.Succeeded);
        Assert.True(context.TrialRun.IsActive);
        Assert.True(context.TrialRun.IsRunning(ProposalId));
        window.Run();
        Assert.False(context.TrialRun.IsActive);
        Assert.False(context.TrialRun.IsRunning(ProposalId));
    }

    [Fact]
    public async Task ATryAgainEndedOnTheWindowsContextIsNotRunningWhenItsCallerResumes()
    {
        var fake = new FakeCommandClient();
        var window = new HeldContext();
        var context = MakeOn(window, fake);
        await context.OpenProjectAsync(ProjectPath);
        window.Run();
        var waiting = new TaskCompletionSource<CommandOutcome<JobStatusResponse>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fake.OnWaitForJob((_, _, _) => waiting.Task);

        var trying = window.RunOn(() => context.TryAgainAsync(ProposalId, CancellationToken.None));
        waiting.SetResult(CommandOutcome<JobStatusResponse>.Success(Status()));
        window.Run();
        var outcome = await trying;

        Assert.True(outcome.Succeeded);
        Assert.False(context.TrialRun.IsActive);
    }

    private static SIL.Motif.App.ViewModels.WorkspaceContext MakeOn(HeldContext window, FakeCommandClient fake)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(window);
        try { return WorkspaceContextTests.NewContext(fake); }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private static JobStatusResponse Status() =>
        new("job/test", "project/test", true, "trial", JobStatus.Completed, 1, null, false, null, 1);

    // A context that holds what is posted to it until the test runs it, as a UI thread that is busy would.
    private sealed class HeldContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _held = new();

        public override void Post(SendOrPostCallback d, object? state) => _held.Enqueue((d, state));

        public void Run()
        {
            var previous = Current;
            SetSynchronizationContext(this);
            try
            {
                while (_held.TryDequeue(out var item)) item.Callback(item.State);
            }
            finally { SetSynchronizationContext(previous); }
        }

        // Starts work with this as the current context, so its awaits resume here once Run is called.
        public Task<T> RunOn<T>(Func<Task<T>> work)
        {
            var previous = Current;
            SetSynchronizationContext(this);
            try { return work(); }
            finally { SetSynchronizationContext(previous); }
        }
    }
}
