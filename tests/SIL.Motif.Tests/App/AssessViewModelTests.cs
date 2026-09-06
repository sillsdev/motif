using System.ComponentModel;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins <see cref="AssessViewModel"/>'s run state machine: Idle to Running to Completed, Idle to Running
/// to Cancelling to Cancelled, and Idle to Running to Refused. Also pins that a second Run is disabled
/// while active, that progress replays the command's own bounded, monotonic sequence without inventing
/// any of its own, that disposal cancels and awaits an active run, and that an unexpected exception
/// surfaces rather than being dressed up as a Refusal.
/// </summary>
public sealed class AssessViewModelTests
{
    private const string Digest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string BundleDigest = "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string ProjectPath = @"C:\projects\one.fwdata";

    private static (FakeCommandClient Fake, SelectionViewModel Selection, AssessViewModel Assess) NewViewModel()
    {
        var fake = new FakeCommandClient();
        var selection = new SelectionViewModel(fake);
        selection.AllWordforms = true;
        var assess = new AssessViewModel(fake, selection) { ProjectPath = ProjectPath };
        return (fake, selection, assess);
    }

    private static AssessCommandResponse NewResponse() => new(
        new BaselineCaptureResponse(
            new BaselineToken("project-1", Digest, "1", "2026-09-05T00:00:00Z", BundleDigest),
            ProjectPath, DateTimeOffset.UtcNow, FieldWorksHeldProject: false, ReusedExistingBytes: true),
        new SelectionProjection([], []),
        ["assessment/one"],
        "(summary)");

    private static readonly AssessmentProgress[] Steps =
    [
        new(AssessmentStage.Capturing, 0, null, "Ensuring a current Baseline exists..."),
        new(AssessmentStage.SelectingWords, 0, null, "Composing the Selection..."),
        new(AssessmentStage.Parsing, 0, 2, "Parsing the Selection..."),
        new(AssessmentStage.ReadingStatistics, 2, 2, "Reading PanGloss's statistics..."),
        new(AssessmentStage.Complete, 2, 2, "Assessment complete."),
    ];

    [Fact]
    public void BeforeAnyRunTheStateIsIdleAndRunIsEnabledOnceAProjectAndSelectionExist()
    {
        var (_, _, assess) = NewViewModel();

        Assert.Equal(AssessRunState.Idle, assess.State);
        Assert.True(assess.RunCommand.CanExecute(null));
        Assert.False(assess.IsActive);
    }

    [Fact]
    public async Task RunningReplaysTheCommandsProgressStepsInOrderWithoutInventingAnyOfItsOwn()
    {
        var (fake, _, assess) = NewViewModel();
        var response = NewResponse();
        fake.AssessCompletesWith(response, Steps);
        var observed = new List<AssessmentProgress>();
        assess.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AssessViewModel.Progress)) observed.Add(assess.Progress!);
        };

        await assess.RunCommand.ExecuteAsync(null);

        Assert.Equal(Steps, observed);
        foreach (var step in observed) Assert.True(step.Total is null || step.Completed <= step.Total);
        Assert.Equal(AssessRunState.Completed, assess.State);
        Assert.Same(response, assess.Result);
    }

    [Fact]
    public async Task CancellingWhileRunningReachesCancelledWithTheCommandsOwnRefusalCode()
    {
        var (fake, _, assess) = NewViewModel();
        var cancelledRefusal = new Refusal(
            "assessment.cancelled", FailureReason.Refused, "The Assessment run was cancelled.");
        fake.AssessBlocksUntilCancelled(cancelledRefusal);

        // Cancelling is transient: the run can finish before Execute returns, so record rather than sample.
        var states = new List<AssessRunState>();
        assess.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName == nameof(AssessViewModel.State)) states.Add(assess.State);
        };

        var running = assess.RunCommand.ExecuteAsync(null);
        Assert.Equal(AssessRunState.Running, assess.State);
        Assert.True(assess.IsActive);
        Assert.False(assess.RunCommand.CanExecute(null));
        Assert.True(assess.CancelCommand.CanExecute(null));

        assess.CancelCommand.Execute(null);
        Assert.False(assess.CancelCommand.CanExecute(null));

        await running;

        Assert.Equal(
            new[] { AssessRunState.Running, AssessRunState.Cancelling, AssessRunState.Cancelled }, states);
        Assert.Equal(AssessRunState.Cancelled, assess.State);
        Assert.False(assess.IsActive);
        Assert.Equal("assessment.cancelled", assess.Refusal!.Code);
        Assert.True(assess.RunCommand.CanExecute(null));
    }

    [Fact]
    public async Task ARefusalThatIsNotCancellationReachesRefusedAndDisplaysTheCommandsOwnMessage()
    {
        var (fake, _, assess) = NewViewModel();
        var refusal = new Refusal(
            "assess.parser-unavailable", FailureReason.Refused, "PanGloss is not built here.",
            new Dictionary<string, string> { ["projectPath"] = ProjectPath });
        fake.AssessRefusesWith(refusal);

        await assess.RunCommand.ExecuteAsync(null);

        Assert.Equal(AssessRunState.Refused, assess.State);
        Assert.Same(refusal, assess.Refusal);
        Assert.Equal([$"projectPath: {ProjectPath}"], assess.RefusalFacts);
        Assert.True(assess.RunCommand.CanExecute(null));
    }

    [Fact]
    public async Task DisposalCancelsAndAwaitsAnActiveRun()
    {
        var (fake, _, assess) = NewViewModel();
        var cancelledRefusal = new Refusal(
            "assessment.cancelled", FailureReason.Refused, "The Assessment run was cancelled.");
        fake.AssessBlocksUntilCancelled(cancelledRefusal);

        _ = assess.RunCommand.ExecuteAsync(null);
        Assert.Equal(AssessRunState.Running, assess.State);

        await assess.DisposeAsync();

        Assert.Equal(AssessRunState.Cancelled, assess.State);
    }

    [Fact]
    public async Task AnUnexpectedExceptionSurfacesRatherThanBecomingARefusal()
    {
        var (fake, _, assess) = NewViewModel();
        fake.OnAssess((_, _, _) => Task.FromException<CommandOutcome<AssessCommandResponse>>(
            new InvalidOperationException("boom")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => assess.RunCommand.ExecuteAsync(null));

        Assert.Null(assess.Refusal);
        Assert.Null(assess.Result);
    }

    [Fact]
    public void ProgressIsIndeterminateOnlyWhenTheReportedStepHasNoTotal()
    {
        var (_, _, assess) = NewViewModel();

        assess.Progress = new AssessmentProgress(AssessmentStage.Capturing, 0, null, "Starting...");
        Assert.True(assess.IsIndeterminate);

        assess.Progress = new AssessmentProgress(AssessmentStage.Parsing, 1, 2, "Parsing...");
        Assert.False(assess.IsIndeterminate);
        Assert.Equal(0.5, assess.ProgressFraction);
    }
}
