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

    private sealed record TestResponse(string Value);
}
