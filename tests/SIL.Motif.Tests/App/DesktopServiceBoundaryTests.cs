using SIL.Motif.App.Services;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Handoff;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins the seam view models depend on: the three interfaces they call — <see cref="ICommandClient"/>,
/// <see cref="IProjectPicker"/>, <see cref="IHandoffFolderPicker"/> — carry no Avalonia type, and
/// <see cref="FakeCommandClient"/> can complete, refuse, report progress, and block until cancelled with
/// no process, no Avalonia control, and no wall-clock race.
/// </summary>
public sealed class DesktopServiceBoundaryTests
{
    private const string Digest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string BundleDigest = "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    private static BaselineToken NewToken() =>
        new("project-1", Digest, "1", "2026-09-05T00:00:00Z", BundleDigest);

    [Theory]
    [InlineData(typeof(ICommandClient))]
    [InlineData(typeof(IProjectPicker))]
    [InlineData(typeof(IHandoffFolderPicker))]
    public void ViewModelFacingServiceInterfaceNamesNoAvaloniaType(Type serviceInterface)
    {
        foreach (var method in serviceInterface.GetMethods())
        {
            AssertNoAvaloniaType(method.ReturnType, serviceInterface, method.Name);
            foreach (var parameter in method.GetParameters())
                AssertNoAvaloniaType(parameter.ParameterType, serviceInterface, method.Name);
        }
    }

    private static void AssertNoAvaloniaType(Type type, Type owner, string memberName)
    {
        Assert.False(
            type.Namespace?.StartsWith("Avalonia", StringComparison.Ordinal) == true,
            $"{owner.Name}.{memberName} names Avalonia type {type}.");
        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
                AssertNoAvaloniaType(argument, owner, memberName);
        }
    }

    [Fact]
    public async Task CaptureBaselineAsyncCompletesWithTheConfiguredResponse()
    {
        var fake = new FakeCommandClient();
        var response = new BaselineCaptureResponse(
            NewToken(), @"C:\managed\captures\1\project.fwdata", DateTimeOffset.UtcNow,
            FieldWorksHeldProject: false, ReusedExistingBytes: false);
        fake.CaptureBaselineCompletesWith(response);

        var request = new BaselineCaptureRequest(@"C:\projects\one.fwdata");
        var outcome = await fake.CaptureBaselineAsync(request, CancellationToken.None);

        Assert.True(outcome.Succeeded);
        Assert.Same(response, outcome.Value);
        Assert.Same(request, Assert.Single(fake.CaptureBaselineRequests));
    }

    [Fact]
    public async Task CaptureBaselineAsyncRefusesWithTheConfiguredRefusal()
    {
        var fake = new FakeCommandClient();
        var refusal = new Refusal("baseline.busy", FailureReason.Busy, "The project is held by FieldWorks.");
        fake.CaptureBaselineRefusesWith(refusal);

        var outcome = await fake.CaptureBaselineAsync(
            new BaselineCaptureRequest(@"C:\projects\one.fwdata"), CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Same(refusal, outcome.Refusal);
    }

    [Fact]
    public async Task AssessAsyncReportsProgressInOrderBeforeCompleting()
    {
        var fake = new FakeCommandClient();
        var steps = new[]
        {
            new AssessmentProgress(AssessmentStage.Capturing, 0, null, "Ensuring a current Baseline exists..."),
            new AssessmentProgress(AssessmentStage.Parsing, 0, 3, "Parsing the Selection..."),
            new AssessmentProgress(AssessmentStage.Complete, 3, 3, "Assessment complete."),
        };
        var response = new AssessCommandResponse(
            new BaselineCaptureResponse(NewToken(), @"C:\p.fwdata", DateTimeOffset.UtcNow, false, false),
            new SelectionProjection([], []), [], "(summary)");
        fake.AssessCompletesWith(response, steps);

        var progress = new RecordingProgress();
        var outcome = await fake.AssessAsync(
            new AssessRequest(@"C:\p.fwdata", new SelectionRequest(true, [], [], false, null)),
            progress, CancellationToken.None);

        Assert.True(outcome.Succeeded);
        Assert.Same(response, outcome.Value);
        Assert.Equal(steps, progress.Reported);
    }

    [Fact]
    public async Task AssessAsyncRefusesWithTheConfiguredRefusal()
    {
        var fake = new FakeCommandClient();
        var refusal = new Refusal("assess.parser-unavailable", FailureReason.Refused, "PanGloss is not built.");
        fake.AssessRefusesWith(refusal);

        var outcome = await fake.AssessAsync(
            new AssessRequest(@"C:\p.fwdata", new SelectionRequest(true, [], [], false, null)),
            new RecordingProgress(), CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Same(refusal, outcome.Refusal);
    }

    [Fact]
    public async Task AssessAsyncBlocksUntilItsCallerCancels()
    {
        var fake = new FakeCommandClient();
        var onCancelled = new Refusal(
            "assessment.cancelled", FailureReason.Cancelled, "The Assessment run was cancelled.");
        fake.AssessBlocksUntilCancelled(onCancelled);

        using var cts = new CancellationTokenSource();
        var task = fake.AssessAsync(
            new AssessRequest(@"C:\p.fwdata", new SelectionRequest(true, [], [], false, null)),
            new RecordingProgress(), cts.Token);

        Assert.False(task.IsCompleted);

        cts.Cancel();
        var outcome = await task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(outcome.Succeeded);
        Assert.Same(onCancelled, outcome.Refusal);
    }

    [Fact]
    public async Task StatsAsyncCompletesWithTheConfiguredResponse()
    {
        var fake = new FakeCommandClient();
        var response = new StatsCommandResponse("assessment-1", @"C:\p.fwdata", @"C:\cache.json", "(text)", null);
        fake.StatsCompletesWith(response);

        var request = new StatsRequest(@"C:\p.fwdata", null, StatsOutputKind.Text, []);
        var outcome = await fake.StatsAsync(request, CancellationToken.None);

        Assert.True(outcome.Succeeded);
        Assert.Same(response, outcome.Value);
        Assert.Same(request, Assert.Single(fake.StatsRequests));
    }

    [Fact]
    public async Task StatsAsyncRefusesWithTheConfiguredRefusal()
    {
        var fake = new FakeCommandClient();
        var refusal = new Refusal("stats.no-baseline", FailureReason.NotFound, "No Baseline has been captured.");
        fake.StatsRefusesWith(refusal);

        var outcome = await fake.StatsAsync(
            new StatsRequest(@"C:\p.fwdata", null, StatsOutputKind.Text, []), CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Same(refusal, outcome.Refusal);
    }

    [Fact]
    public async Task HandoffAsyncReportsProgressInOrderBeforeCompleting()
    {
        var fake = new FakeCommandClient();
        var steps = new[]
        {
            new AssessmentProgress(AssessmentStage.ImportingGrammar, 0, null, "Importing the grammar..."),
        };
        var response = new HandoffCommandResponse(
            @"C:\out", new BaselineCaptureResponse(NewToken(), @"C:\p.fwdata", DateTimeOffset.UtcNow, false, false),
            new SelectionProjection([], []), [], []);
        fake.HandoffCompletesWith(response, steps);

        var progress = new RecordingProgress();
        var outcome = await fake.HandoffAsync(
            new HandoffRequest(
                @"C:\p.fwdata", @"C:\out", new SelectionRequest(true, [], [], false, null), false, false),
            progress, CancellationToken.None);

        Assert.True(outcome.Succeeded);
        Assert.Same(response, outcome.Value);
        Assert.Equal(steps, progress.Reported);
    }

    [Fact]
    public async Task HandoffAsyncBlocksUntilItsCallerCancels()
    {
        var fake = new FakeCommandClient();
        var onCancelled = new Refusal(
            "handoff.cancelled", FailureReason.Cancelled, "The Handoff run was cancelled.");
        fake.HandoffBlocksUntilCancelled(onCancelled);

        using var cts = new CancellationTokenSource();
        var task = fake.HandoffAsync(
            new HandoffRequest(
                @"C:\p.fwdata", @"C:\out", new SelectionRequest(true, [], [], false, null), false, false),
            new RecordingProgress(), cts.Token);

        Assert.False(task.IsCompleted);

        cts.Cancel();
        var outcome = await task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(outcome.Succeeded);
        Assert.Same(onCancelled, outcome.Refusal);
    }

    [Fact]
    public async Task UnconfiguredCommandThrowsRatherThanReturningANullBehavior()
    {
        var fake = new FakeCommandClient();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fake.StatsAsync(new StatsRequest(@"C:\p.fwdata", null, StatsOutputKind.Text, []), CancellationToken.None));
    }

    private sealed class RecordingProgress : IProgress<AssessmentProgress>
    {
        public List<AssessmentProgress> Reported { get; } = [];

        public void Report(AssessmentProgress value) => Reported.Add(value);
    }
}
