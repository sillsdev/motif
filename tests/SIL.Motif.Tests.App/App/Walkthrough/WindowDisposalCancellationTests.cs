using System.ComponentModel;
using System.Diagnostics;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.App.Walkthrough;

[Collection(LcmCacheTestCollection.Name)]
public sealed class WindowDisposalCancellationTests(PristineProjectFixture pristine, ITestOutputHelper output)
{
    [Fact]
    public void DisposingWithAnAssessmentInFlightCancelsTheParserWithoutBlockingTheDispatcher()
    {
        using var project = new WalkthroughProject(pristine);
        var parserPath = FakeParser.CopyRecordingInvocations(project.ManagedRoot);
        var startedPath = Path.Combine(project.ManagedRoot, "disposed-assessment-started");
        var releasePath = Path.Combine(project.ManagedRoot, "disposed-assessment-release");
        var runner = new InProcessRunnerLauncher(new JobRunnerLaunchOptions(project.ManagedRoot, parserPath));
        var deadline = Stopwatch.GetTimestamp() + 60 * Stopwatch.Frequency;
        Exception? failure = null;

        try
        {
            AvaloniaHeadlessFixture.RunUntilComplete(() =>
            {
                var walkthrough = new WalkthroughWindow(
                    project.ManagedRoot, project.FwDataPath, parserPath: parserPath, runnerLauncher: runner);
                var workspace = walkthrough.Workspace;
                int processId;
                try
                {
                    WalkthroughSteps.ChooseProjectAndCaptureBaseline(walkthrough, deadline);
                    walkthrough.TypePastedWords(string.Join(Environment.NewLine,
                        SeededProject.FirstForm, SeededProject.SecondForm));
                    FakeParser.BehaveBesideExecutable(parserPath, new
                    {
                        startedPath,
                        holdUntilPath = releasePath,
                        holdTimeoutMs = 120_000,
                    });
                    walkthrough.Click("Run the Assessment");
                    walkthrough.WaitUntil(
                        () => File.Exists(startedPath) && workspace.Assess.State == RunState.Running,
                        WalkthroughSteps.Remaining(deadline), "the parser did not start before window disposal");
                    processId = Assert.Single(PanglossProcesses.Snapshot(parserPath));
                    Assert.True(PanglossProcesses.AnyAlive(parserPath, [processId]));
                }
                finally
                {
                    walkthrough.Dispose();
                }

                Assert.Equal(RunState.Cancelled, workspace.Assess.State);
                Assert.False(PanglossProcesses.AnyAlive(parserPath, [processId]),
                    "disposing the window left its in-flight PanGloss process alive");
                return Task.CompletedTask;
            }, WalkthroughSteps.Remaining(deadline));
        }
        catch (Exception exception)
        {
            failure = exception;
            throw;
        }
        finally
        {
            try
            {
                ReleaseAndStopParser(releasePath, parserPath, runner);
            }
            catch (Exception exception) when (failure is not null)
            {
                output.WriteLine("Parser cleanup also failed: {0}", exception);
            }
        }
    }

    private static void ReleaseAndStopParser(string releasePath, string parserPath, InProcessRunnerLauncher runner)
    {
        var failures = new List<Exception>();
        Task? stopping = null;
        Attempt(() => File.WriteAllText(releasePath, string.Empty));
        Attempt(() => stopping = runner.DisposeAsync().AsTask());
        Attempt(() => StopPrivateParser(parserPath));
        if (stopping is not null)
            Attempt(() => stopping.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult());
        if (failures.Count > 0)
            throw new AggregateException("Private parser cleanup failed.", failures);

        void Attempt(Action cleanup)
        {
            try
            {
                cleanup();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }
    }

    private static void StopPrivateParser(string parserPath)
    {
        foreach (var processId in PanglossProcesses.Snapshot(parserPath))
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                if (process.HasExited) continue;
                var executablePath = process.MainModule?.FileName;
                if (executablePath is null || !string.Equals(Path.GetFullPath(executablePath),
                        Path.GetFullPath(parserPath), OperatingSystem.IsWindows()
                            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    continue;
                process.Kill(entireProcessTree: true);
                if (!process.WaitForExit(5000))
                    throw new TimeoutException($"The private parser process {processId} did not exit after cleanup.");
            }
            catch (ArgumentException)
            {
            }
            catch (InvalidOperationException)
            {
            }
            catch (Win32Exception) when (!PanglossProcesses.AnyAlive(parserPath, [processId]))
            {
            }
        }
    }
}
