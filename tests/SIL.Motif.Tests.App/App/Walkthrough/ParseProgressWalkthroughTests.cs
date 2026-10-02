using System.Diagnostics;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Tests.App.RealClient;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

[Collection(LcmCacheTestCollection.Name)]
[Trait("MotifTestLevel", "System")]
public sealed class ParseProgressWalkthroughTests(PristineProjectFixture pristine)
{
    [Fact]
    public void StreamedWordsShowMeasuredProgressAndStopsWithoutStartingAnotherParse()
    {
        using var project = new WalkthroughProject(pristine);
        var parser = FakeParser.CopyRecordingInvocations(project.ManagedRoot);
        var release = Path.Combine(project.ManagedRoot, "word-release");
        var deadline = Stopwatch.GetTimestamp() + 180 * Stopwatch.Frequency;
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var window = new WalkthroughWindow(project.ManagedRoot, project.FwDataPath, parserPath: parser);
            WalkthroughSteps.ChooseProjectAndCaptureBaseline(window, deadline);
            FakeParser.BehaveBesideExecutable(parser, new
            {
                streamProgress = true, delayMilliseconds = 200, holdEachWordUntil = release,
                words = new[] { new { word = "c-slow", outcome = "timed-out" },
                    new { word = "d-capped", outcome = "capped" } },
            });
            var assess = window.Workspace.Assess;
            var running = assess.RerunAsync(["a-first", "b-second", "c-slow", "d-capped", "e-last"], 1000);
            try
            {
                window.WaitUntil(() => assess.Progress?.Message.Contains("first", StringComparison.Ordinal) == true,
                    TimeSpan.FromSeconds(15), "the started word was never displayed");
                Assert.Contains("0 of 5", Details(window));
                Assert.Contains("estimating", Details(window));
                Assert.Contains("elapsed", Details(window));
                await assess.RerunAsync(["duplicate"], 1000);
                Assert.Single(FakeParser.Invocations(parser), line => line == "batch");
                for (var index = 0; index < 3; index++)
                {
                    File.WriteAllText(release + "." + index, string.Empty);
                    var done = index + 1;
                    window.WaitUntil(() => assess.Progress?.Completed == done,
                        WalkthroughSteps.Remaining(deadline), "a flushed word did not update progress");
                }
                Assert.Contains("left", Details(window));
                Assert.DoesNotContain("estimating", Details(window));
                Assert.Contains("c-slow", Details(window));
                Assert.Contains("timed out", Details(window));
                File.WriteAllText(release + ".3", string.Empty);
                window.WaitUntil(() => assess.Progress?.Completed == 4,
                    WalkthroughSteps.Remaining(deadline), "the step-limited word did not update progress");
                Assert.Contains("d-capped", Details(window));
                Assert.Contains("step limit", Details(window));
                var stopped = window.Find<Expander>("See stopped words while parsing");
                var header = stopped.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>()
                    .Single(button => button.Name == "ExpanderHeader");
                header.Focus();
                window.Window.KeyPress(Avalonia.Input.Key.Space, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Space, " ");
                window.Window.KeyRelease(Avalonia.Input.Key.Space, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Space, " ");
                PageScreenshots.Settle(window.Window);
                Assert.True(stopped.IsExpanded);
                Assert.Contains("c-slow · Timed out", Details(window));
                InteractiveControlSweep.AssertScene(window, "parse progress with stopped words",
                    InteractiveControlFamily.Progress, InteractiveControlFamily.Action, InteractiveControlFamily.Disclosure);
                Capture(window, "parse-progress");
                Assert.True(assess.CancelCommand.CanExecute(null));
                Assert.Contains("previous results", Details(window));
            }
            finally
            {
                assess.CancelCommand.Execute(null);
                await running.WaitAsync(TimeSpan.FromSeconds(20));
            }
            Assert.Equal(RunState.Cancelled, assess.State);
        }, WalkthroughSteps.Remaining(deadline));
    }

    [Fact]
    public void AWorkerThatFinishesNoWordReportsTheDelayAndCanBeCancelled()
    {
        using var project = new WalkthroughProject(pristine);
        var parser = FakeParser.CopyRecordingInvocations(project.ManagedRoot);
        var release = Path.Combine(project.ManagedRoot, "held-release");
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var deadline = Stopwatch.GetTimestamp() + 180 * Stopwatch.Frequency;
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var window = new WalkthroughWindow(project.ManagedRoot, project.FwDataPath,
                parserPath: parser, timeProvider: clock);
            WalkthroughSteps.ChooseProjectAndCaptureBaseline(window, deadline);
            FakeParser.BehaveBesideExecutable(parser, new { streamProgress = true, holdEachWordUntil = release });
            var assess = window.Workspace.Assess;
            var running = assess.RerunAsync(["held"], 1000);
            try
            {
                window.WaitUntil(() => assess.Progress?.Message.Contains("held", StringComparison.Ordinal) == true,
                    TimeSpan.FromSeconds(15), "the held word was never displayed");
                clock.Advance(TimeSpan.FromSeconds(12));
                window.WaitUntil(() => Details(window).Contains("No word has finished", StringComparison.Ordinal),
                    WalkthroughSteps.Remaining(deadline), "the worker stopped reporting without a warning");
                Assert.True(window.Find<Button>("Cancel parsing all words").IsEffectivelyEnabled);
                Assert.Contains(window.Window.GetVisualDescendants().OfType<Button>(), button =>
                    button.IsEffectivelyVisible && AutomationProperties.GetName(button) == "Report a problem with parsing");
                InteractiveControlSweep.AssertScene(window, "parse with no progress",
                    InteractiveControlFamily.Progress, InteractiveControlFamily.Action);
                Capture(window, "parse-no-progress");
            }
            finally
            {
                assess.CancelCommand.Execute(null);
                await running.WaitAsync(TimeSpan.FromSeconds(20));
            }
        }, WalkthroughSteps.Remaining(deadline));
    }

    [Fact]
    public void AWorkerThatWritesNoRowsStillOffersCancelAndReportsTheLackOfProgress()
    {
        using var project = new WalkthroughProject(pristine);
        var parser = FakeParser.CopyRecordingInvocations(project.ManagedRoot);
        var heartbeat = Path.Combine(project.ManagedRoot, "no-rows-heartbeat");
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var deadline = Stopwatch.GetTimestamp() + 180 * Stopwatch.Frequency;
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var window = new WalkthroughWindow(project.ManagedRoot, project.FwDataPath,
                parserPath: parser, timeProvider: clock);
            WalkthroughSteps.ChooseProjectAndCaptureBaseline(window, deadline);
            FakeParser.BehaveBesideExecutable(parser, new
            {
                subcommands = new Dictionary<string, object> { ["batch"] = new { heartbeatPath = heartbeat } },
            });
            var assess = window.Workspace.Assess;
            var running = assess.RerunAsync(["held"], 1000);
            try
            {
                window.WaitUntil(() => File.Exists(heartbeat) && assess.Progress?.Total == 1,
                    TimeSpan.FromSeconds(15), "the worker never reached parsing");
                Assert.Null(assess.Progress!.CurrentWord);
                clock.Advance(TimeSpan.FromSeconds(12));
                window.WaitUntil(() => Details(window).Contains("No word has finished", StringComparison.Ordinal),
                    TimeSpan.FromSeconds(5), "a worker with no output kept spinning");
                Assert.True(window.Find<Button>("Cancel parsing all words").IsEffectivelyEnabled);
            }
            finally
            {
                assess.CancelCommand.Execute(null);
                await running.WaitAsync(TimeSpan.FromSeconds(20));
            }
        }, WalkthroughSteps.Remaining(deadline));
    }

    [Fact]
    public void TryAWordShowsItsCurrentWordAndElapsedTimeAndDoesNotReplaceARunningTrace()
    {
        using var project = new WalkthroughProject(pristine);
        var parser = FakeParser.CopyRecordingInvocations(project.ManagedRoot);
        var heartbeat = Path.Combine(project.ManagedRoot, "trace-heartbeat");
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var deadline = Stopwatch.GetTimestamp() + 180 * Stopwatch.Frequency;
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var window = new WalkthroughWindow(project.ManagedRoot, project.FwDataPath,
                parserPath: parser, timeProvider: clock);
            WalkthroughSteps.ChooseProjectAndCaptureBaseline(window, deadline);
            FakeParser.BehaveBesideExecutable(parser, new
            {
                subcommands = new Dictionary<string, object> { ["parse"] = new { heartbeatPath = heartbeat } },
            });
            window.ShowPage(WorkspacePage.TryAWord);
            var trace = window.Workspace.Assess.Trace;
            trace.WordToTry = "held";
            var running = trace.TryCommand.ExecuteAsync(null);
            try
            {
                window.WaitUntil(() => File.Exists(heartbeat) && Details(window).Contains("Parsing held", StringComparison.Ordinal),
                    TimeSpan.FromSeconds(15), "Try a Word did not display its running word");
                Assert.Contains("0 of 1", Details(window));
                Assert.Contains("elapsed", Details(window));
                Assert.Contains("estimating", Details(window));
                await trace.TryCommand.ExecuteAsync(null);
                Assert.False(trace.TryCommand.CanExecute(null));
                Assert.Single(FakeParser.Invocations(parser), line => line == "parse");
                clock.Advance(TimeSpan.FromSeconds(61));
                window.WaitUntil(() => Details(window).Contains("no per-word time limit", StringComparison.Ordinal),
                    TimeSpan.FromSeconds(5), "the unbounded trace gave no account of its long wait");
                Capture(window, "try-word-progress");
                window.Click("Cancel parsing all words");
            }
            finally
            {
                trace.CancelCommand.Execute(null);
                await running.WaitAsync(TimeSpan.FromSeconds(20));
            }
            Assert.False(trace.ParseProgress.IsActive);
        }, WalkthroughSteps.Remaining(deadline));
    }

    private static string Details(WalkthroughWindow window) => string.Join(" ",
        window.Window.GetVisualDescendants().OfType<TextBlock>()
            .Where(block => block.IsEffectivelyVisible).Select(block => block.Text));

    private static void Capture(WalkthroughWindow window, string name)
    {
        if (Environment.GetEnvironmentVariable("MOTIF_SCREENSHOTS") is not { Length: > 0 } folder) return;
        Directory.CreateDirectory(folder);
        PageScreenshots.Settle(window.Window);
        PageScreenshots.Save(window.Window, Path.Combine(folder, name + ".png"));
    }
}
