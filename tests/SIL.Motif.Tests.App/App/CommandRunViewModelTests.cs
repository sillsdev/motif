using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.App.ViewModels;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class CommandRunViewModelTests
{
    [Fact]
    public async Task CompletesAndReportsProgressThroughTheSharedLifecycle()
    {
        var run = new TestRunViewModel();
        var progress = new AssessmentProgress(AssessmentStage.Parsing, 1, 2, "Parsing...");
        run.Execute = (token, reporter) =>
        {
            reporter.Report(progress);
            return Task.FromResult(CommandOutcome<TestResponse>.Success(new TestResponse("done")));
        };

        await run.RunCommand.ExecuteAsync(null);

        Assert.Equal(RunState.Completed, run.State);
        Assert.Equal(progress, run.Progress);
        Assert.Equal(new TestResponse("done"), run.Result);
        Assert.Null(run.Refusal);
        Assert.Equal(0.5, run.ProgressFraction);
    }

    [Fact]
    public void ProgressFromAnotherThreadGoesToTheOwnersContextOrAppliesDirectlyWithoutOne()
    {
        var progress = new AssessmentProgress(AssessmentStage.Parsing, 1, 2, "Parsing...");
        var recording = new RecordingSynchronizationContext();
        var previous = SynchronizationContext.Current;
        TestRunViewModel withContext;
        SynchronizationContext.SetSynchronizationContext(recording);
        try { withContext = new TestRunViewModel(); }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        var withoutContext = new TestRunViewModel();

        // A dedicated thread, since a pool thread could be the one that created the runs.
        var reporter = new Thread(() =>
        {
            ((IProgress<AssessmentProgress>)withContext).Report(progress);
            ((IProgress<AssessmentProgress>)withoutContext).Report(progress);
        });
        reporter.Start();
        reporter.Join();

        Assert.Null(withContext.Progress);
        Assert.Equal(1, recording.Posted);
        recording.RunPosted();
        Assert.Equal(progress, withContext.Progress);
        Assert.Equal(progress, withoutContext.Progress);
    }

    private sealed class RecordingSynchronizationContext : SynchronizationContext
    {
        private readonly List<(SendOrPostCallback Callback, object? State)> _posted = [];

        public int Posted => _posted.Count;

        public override void Post(SendOrPostCallback d, object? state) => _posted.Add((d, state));

        public void RunPosted()
        {
            foreach (var (callback, state) in _posted) callback(state);
            _posted.Clear();
        }
    }

    [Theory]
    [InlineData(FailureReason.Cancelled, RunState.Cancelled)]
    [InlineData(FailureReason.Refused, RunState.Refused)]
    public async Task ClassifiesCancellationAndOtherRefusalsByTypedReason(
        FailureReason reason, RunState expectedState)
    {
        var run = new TestRunViewModel();
        var refusal = new Refusal("run.refused", reason, "The run was refused.");
        run.Execute = (_, _) => Task.FromResult(CommandOutcome<TestResponse>.Refused(refusal));

        await run.RunCommand.ExecuteAsync(null);

        Assert.Equal(expectedState, run.State);
        Assert.Same(refusal, run.Refusal);
        Assert.Null(run.Result);
    }

    [Fact]
    public void ResetClearsTheSharedStateFromEveryRunState()
    {
        var run = new TestRunViewModel();

        foreach (var state in Enum.GetValues<RunState>())
        {
            run.State = state;
            run.Progress = new AssessmentProgress(AssessmentStage.Parsing, 1, 2, "Parsing...");
            run.Result = new TestResponse("result");
            run.Refusal = new Refusal("run.refused", FailureReason.Refused, "refused");

            run.Reset();

            Assert.Equal(RunState.Idle, run.State);
            Assert.Null(run.Progress);
            Assert.Null(run.Result);
            Assert.Null(run.Refusal);
        }

        Assert.Equal(Enum.GetValues<RunState>().Length, run.ResetCount);
    }

    [Fact]
    public async Task DisposeCancelsAndAwaitsAnInFlightRun()
    {
        var run = new TestRunViewModel();
        run.Execute = async (token, _) =>
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return CommandOutcome<TestResponse>.Success(new TestResponse("unreachable"));
            }
            catch (OperationCanceledException)
            {
                return CommandOutcome<TestResponse>.Refused(
                    new Refusal("run.cancelled", FailureReason.Cancelled, "The run was cancelled."));
            }
        };

        _ = run.RunCommand.ExecuteAsync(null);
        Assert.Equal(RunState.Running, run.State);

        await run.DisposeAsync();

        Assert.Equal(RunState.Cancelled, run.State);
        Assert.False(run.IsActive);
    }

    [Fact]
    public async Task DisposeDoesNotCancelACompletedRunWhileItsCommandIsFinishing()
    {
        var run = new FinishingRunViewModel();
        var execution = run.RunCommand.ExecuteAsync(null);
        run.Completion.SetResult(CommandOutcome<TestResponse>.Success(new TestResponse("done")));
        await run.CompletedStateEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var disposal = run.DisposeAsync().AsTask();
        var cancellationWasRequested = run.RunToken.IsCancellationRequested;
        run.ReleaseCompletedState.TrySetResult();
        await disposal;

        Assert.False(cancellationWasRequested);
        Assert.Equal(RunState.Completed, run.State);
    }

    private sealed class TestRunViewModel : CommandRunViewModel<TestResponse>
    {
        public Func<CancellationToken, IProgress<AssessmentProgress>,
            Task<CommandOutcome<TestResponse>>> Execute { get; set; } =
            (_, _) => Task.FromResult(CommandOutcome<TestResponse>.Success(new TestResponse("default")));

        public int ResetCount { get; private set; }

        protected override bool CanStartCore() => true;

        protected override Task<CommandOutcome<TestResponse>> ExecuteCoreAsync(
            CancellationToken cancellationToken) => Execute(cancellationToken, this);

        protected override void OnReset() => ResetCount++;
    }

    private sealed class FinishingRunViewModel : CommandRunViewModel<TestResponse>
    {
        public TaskCompletionSource<CommandOutcome<TestResponse>> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource CompletedStateEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseCompletedState { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationToken RunToken { get; private set; }

        protected override bool CanStartCore() => true;

        protected override Task<CommandOutcome<TestResponse>> ExecuteCoreAsync(CancellationToken cancellationToken)
        {
            RunToken = cancellationToken;
            return Completion.Task;
        }

        protected override void OnRunStateChanged(RunState value)
        {
            if (value != RunState.Completed) return;
            CompletedStateEntered.TrySetResult();
            ReleaseCompletedState.Task.GetAwaiter().GetResult();
        }
    }

    private sealed record TestResponse(string Value);
}
