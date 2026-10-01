using System.ComponentModel;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
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
    public async Task ACompletedRunIsStampedWithTheComposedClock()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 3, 4, 10, 30, 0, TimeSpan.Zero));
        var fake = new FakeCommandClient();
        var selection = new SelectionViewModel(fake) { AllWordforms = true };
        var assess = new AssessViewModel(fake, selection, clock) { ProjectPath = ProjectPath };
        fake.AssessCompletesWith(NewResponse(), Steps);

        await assess.RunCommand.ExecuteAsync(null);

        Assert.Equal(RunState.Completed, assess.State);
        Assert.Equal(clock.GetLocalNow(), assess.CompletedAt);
        var entry = Assert.Single(fake.UsageEntries);
        Assert.Equal("assess", entry.Command);
        Assert.Equal(new[] { "fwDataPath:text", "selection:object" }, entry.ArgumentShape);
    }

    [Fact]
    public void BeforeAnyRunTheStateIsIdleAndRunIsEnabledOnceAProjectAndSelectionExist()
    {
        var (_, _, assess) = NewViewModel();

        Assert.Equal(RunState.Idle, assess.State);
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
        Assert.Equal(RunState.Completed, assess.State);
        Assert.Same(response, assess.Result);
    }

    [Fact]
    public async Task RerunningPreservesTheSelectionStepCap()
    {
        var (fake, selection, assess) = NewViewModel();
        selection.PerWordStepLimit = 250000;
        fake.AssessCompletesWith(NewResponse() with
        { Measurements = [new ProducedAssessmentReference("assessment/one", AssessmentKinds.ParseTime, "invocation/one")] });

        await assess.RunCommand.ExecuteAsync(null);
        await assess.RerunAsync(["kitabu"], 45000);

        Assert.Equal(new StepCap(250000), fake.AssessRequests[1].Selection!.PerWordStepLimit);
        Assert.Equal("assessment/one", fake.AssessRequests[1].ReplaceAssessmentId);
    }

    [Fact]
    public async Task RerunningCanUseALargerStepCapForTheChosenWords()
    {
        var (fake, _, assess) = NewViewModel();
        fake.AssessCompletesWith(NewResponse());

        await assess.RerunAsync(["kitabu"], 45000, new StepCap(1000000));

        var request = Assert.Single(fake.AssessRequests);
        Assert.Equal(["kitabu"], request.Selection!.Words);
        Assert.Equal(new StepCap(1000000), request.Selection.PerWordStepLimit);
        Assert.Equal(45000, request.PerWordLimitMs);
    }

    [Fact]
    public void RerunningAnotherWordAddsItToTheWordsAlreadyMeasured()
    {
        var first = NewResponse() with
        {
            Words = [new AssessmentWordResult("dogs", "analysed", false, "Finished", 5, null)],
        };
        var second = NewResponse() with
        {
            Words = [new AssessmentWordResult("cats", "analysed", false, "Finished", 8, null)],
        };

        var merged = AssessViewModel.Merge(first, second);

        Assert.Equal(["dogs", "cats"], merged.Words.Select(word => word.Word));
    }

    [Fact]
    public void RerunningRetainsTheStoredTimingNeededToReplaceEarlierWordRows()
    {
        var first = NewResponse() with
        {
            Measurements = [new ProducedAssessmentReference("initial", "ParseTime", "first")],
        };
        var second = NewResponse() with
        {
            Measurements = [new ProducedAssessmentReference("rerun", "ParseTime", "second")],
        };

        var merged = AssessViewModel.Merge(first, second);

        Assert.Equal(["rerun"], merged.TimingOverrideAssessmentIds);
        Assert.Equal("initial", Assert.Single(merged.Measurements).AssessmentId);
    }

    [Fact]
    public async Task CancellingWhileRunningReachesCancelledWithTheCommandsOwnRefusalCode()
    {
        var (fake, _, assess) = NewViewModel();
        var cancelledRefusal = new Refusal(
            "assessment.cancelled", FailureReason.Cancelled, "The Assessment run was cancelled.");
        fake.AssessBlocksUntilCancelled(cancelledRefusal);

        // Cancelling is transient: the run can finish before Execute returns, so record rather than sample.
        var states = new List<RunState>();
        assess.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName == nameof(AssessViewModel.State)) states.Add(assess.State);
        };

        var running = assess.RunCommand.ExecuteAsync(null);
        Assert.Equal(RunState.Running, assess.State);
        Assert.True(assess.IsActive);
        Assert.False(assess.RunCommand.CanExecute(null));
        Assert.True(assess.CancelCommand.CanExecute(null));

        assess.CancelCommand.Execute(null);
        Assert.False(assess.CancelCommand.CanExecute(null));

        await running;

        Assert.Equal(
            new[] { RunState.Running, RunState.Cancelling, RunState.Cancelled }, states);
        Assert.Equal(RunState.Cancelled, assess.State);
        Assert.False(assess.IsActive);
        Assert.Equal("assessment.cancelled", assess.Refusal!.Code);
        Assert.True(assess.RunCommand.CanExecute(null));
        var entry = Assert.Single(fake.UsageEntries);
        Assert.Equal("assess", entry.Command);
        Assert.DoesNotContain(ProjectPath, string.Join(" ", entry.ArgumentShape));
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

        Assert.Equal(RunState.Refused, assess.State);
        Assert.Same(refusal, assess.Refusal);
        Assert.Contains($"projectPath: {ProjectPath}", assess.ShownRefusal!.Details);
        Assert.True(assess.RunCommand.CanExecute(null));
        Assert.Equal("assess", Assert.Single(fake.UsageEntries).Command);
    }

    // A parse can run for minutes; the linguist keeps reading the last results until new ones replace them.
    [Fact]
    public async Task TheLastResultsStayShownWhileAParseRunsAndAfterItIsRefused()
    {
        var (fake, _, assess) = NewViewModel();
        fake.AssessCompletesWith(NewResponse() with
        {
            Words = [new AssessmentWordResult("dogs", "analysed", false, "Finished", 5, null)],
        }, Steps);
        await assess.RunCommand.ExecuteAsync(null);
        Assert.True(assess.Compare.HasWords);
        Assert.False(assess.ShowsEarlierResults);

        fake.AssessBlocksUntilCancelled(new Refusal("assessment.cancelled", FailureReason.Cancelled, "Cancelled."));
        var running = assess.RunCommand.ExecuteAsync(null);
        Assert.True(assess.IsActive);
        Assert.Equal(["dogs"], assess.Words.AllRows.Select(row => row.Word));
        Assert.True(assess.Compare.HasWords);
        Assert.True(assess.ShowsEarlierResults);
        Assert.Equal("These are the results from before. The new ones replace them when parsing finishes.",
            assess.EarlierResultsNote);
        Assert.False(assess.OffersProblemReport);
        assess.CancelCommand.Execute(null);
        await running;

        fake.AssessRefusesWith(new Refusal("assess.parser-unavailable", FailureReason.Refused, "PanGloss is not built here."));
        await assess.RunCommand.ExecuteAsync(null);
        Assert.Equal(RunState.Refused, assess.State);
        Assert.Equal(["dogs"], assess.Words.AllRows.Select(row => row.Word));
        Assert.True(assess.Compare.HasWords);
        Assert.True(assess.ShowsEarlierResults);
        Assert.Equal("These are the results from before this parse.", assess.EarlierResultsNote);
        Assert.True(assess.OffersProblemReport);

        assess.Reset();
        Assert.False(assess.Compare.HasWords);
        Assert.False(assess.ShowsEarlierResults);
    }

    [Fact]
    public async Task DisposalCancelsAndAwaitsAnActiveRun()
    {
        var (fake, _, assess) = NewViewModel();
        var cancelledRefusal = new Refusal(
            "assessment.cancelled", FailureReason.Cancelled, "The Assessment run was cancelled.");
        fake.AssessBlocksUntilCancelled(cancelledRefusal);

        _ = assess.RunCommand.ExecuteAsync(null);
        Assert.Equal(RunState.Running, assess.State);

        await assess.DisposeAsync();

        Assert.Equal(RunState.Cancelled, assess.State);
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

    [Fact]
    public async Task ChoosingAResultsRowPrimesTryAWordWithoutStartingATrace()
    {
        var (fake, _, assess) = NewViewModel();
        fake.AssessCompletesWith(NewResponse() with
        {
            Words = [new AssessmentWordResult("kitabu", "analysed", false, "Search completed", 10, null)],
        });
        await assess.RunCommand.ExecuteAsync(null);

        assess.Words.SelectedRow = assess.Words.Rows.Single();

        Assert.Equal("kitabu", assess.Trace.WordToTry);
        Assert.Empty(fake.TraceWordRequests);
    }

    [Fact]
    public void SettingProjectPathPropagatesToTraceSoItCanRun()
    {
        var fake = new FakeCommandClient();
        var selection = new SelectionViewModel(fake);
        var assess = new AssessViewModel(fake, selection) { Trace = { WordToTry = "kitabu" } };
        Assert.False(assess.Trace.TryCommand.CanExecute(null));

        assess.ProjectPath = ProjectPath;

        Assert.True(assess.Trace.TryCommand.CanExecute(null));
    }
}
