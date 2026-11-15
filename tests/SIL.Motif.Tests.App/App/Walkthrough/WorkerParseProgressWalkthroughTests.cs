using System.Diagnostics;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.Commands;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Requests;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Tests.App.RealClient;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Jobs;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

[Collection(LcmCacheTestCollection.Name)]
[Trait("MotifTestLevel", "System")]
public sealed class WorkerParseProgressWalkthroughTests(PristineProjectFixture pristine)
{
    [Fact]
    public void ShippedWorkerWritesWordProgressAndMeasurementAdvancesBeforeCompletion()
    {
        using var project = new WalkthroughProject(pristine);
        var words = new[] { "a-fast", "b-slow", "c-held" };
        var identities = words.Select(word => StoredAnalysisFixture.Add(
            project.FwDataPath, pristine.Seed.FirstEntryId, word).WordformId).ToArray();
        var parser = FakeParser.CopyRecordingInvocations(project.ManagedRoot);
        var release = Path.Combine(project.ManagedRoot, "worker-word-release");
        var options = new JobRunnerLaunchOptions(project.ManagedRoot, parser)
        {
            WorkerExecutable = BuildOutput.Worker,
            OwnerNamespace = "motif-worker-progress-" + Guid.NewGuid().ToString("N"),
            IdleTimeout = TimeSpan.FromSeconds(1),
        };
        var deadline = WalkthroughSteps.DeadlineIn(240);
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var window = new WalkthroughWindow(project.ManagedRoot, project.FwDataPath,
                parserPath: parser, runnerLauncher: new NoRunnerLauncher(options));
            WalkthroughSteps.ChooseProjectAndCaptureBaseline(window, deadline);
            for (var index = 0; index < words.Length; index++)
                await window.Workspace.Context.Changes.PutAsync(new ChangeIntent(CanonicalId.Mint().Value,
                    "incorrect-spelling", CanonicalId.FromGuid(identities[index]).Value, words[index], OriginPage: "Texts"));
            Assert.Equal(3, window.Workspace.Context.Changes.Count);
            FakeParser.BehaveBesideExecutable(parser, new
            {
                streamProgress = true, holdEachWordUntil = release,
                words = new[] { new { word = words[0], outcome = "complete", elapsedMs = 2500 },
                    new { word = words[1], outcome = "complete", elapsedMs = 54000 } },
            });
            window.ShowPage(WorkspacePage.Proposals);
            var measurement = window.Workspace.Context.PendingMeasurement;
            var running = measurement.MeasureCommand.ExecuteAsync(null);
            window.WaitUntil(() => JobProgress.LatestJobId(project.FwDataPath, JobCommands.TrialKind) is not null,
                TimeSpan.FromSeconds(30), "the App did not queue the worker parse");
            var jobId = JobProgress.LatestJobId(project.FwDataPath, JobCommands.TrialKind)!;
            var start = CliProcess.Start(options, ProcessRunnerLauncher.LaunchArguments(options)
                .Concat([RunnerOptions.WakeProjectArgument, project.FwDataPath]).ToArray());
            start.FileName = BuildOutput.Worker;
            start.RedirectStandardInput = true;
            using var worker = Process.Start(start)!;
            worker.StandardInput.Close();
            var output = worker.StandardOutput.ReadToEndAsync();
            var error = worker.StandardError.ReadToEndAsync();
            TrialWordProgress? ReadProgress() => JobProgress.Read(project.FwDataPath, jobId).ProgressJson is { } json
                ? JsonSerializer.Deserialize<TrialWordProgress>(json, MotifJson.CreateOptions()) : null;
            try
            {
                window.WaitUntil(() => !worker.HasExited && ReadProgress()?.CurrentWord == words[0],
                    TimeSpan.FromSeconds(60), "the real worker did not publish its first STARTED row");
                File.WriteAllText(release + ".0", string.Empty);
                window.WaitUntil(() => ReadProgress() is { Completed: 1, CurrentWord: "b-slow" },
                    TimeSpan.FromSeconds(20), "the worker did not persist its first completed word while still running");
                Assert.False(JobStateMachine.IsTerminal(JobProgress.Read(project.FwDataPath, jobId).Status));
                Assert.False(running.IsCompleted);
                window.WaitUntil(() => measurement.ParseProgress.Fraction > 0,
                    TimeSpan.FromSeconds(5), "the worker store advanced but measurement progress stayed at zero");
                Assert.Equal(1.0 / 3, measurement.ParseProgress.Fraction, 5);
                File.WriteAllText(release + ".1", string.Empty);
                window.WaitUntil(() => measurement.ParseProgress.Fraction >= 2.0 / 3,
                    TimeSpan.FromSeconds(20), "the next worker completion never reached measurement progress");
                Assert.Equal(new ParseWordTiming("b-slow", 54000), ReadProgress()!.SlowestWord);
                Assert.False(running.IsCompleted);
                measurement.CancelCommand.Execute(null);
            }
            finally
            {
                measurement.CancelCommand.Execute(null);
                for (var index = 0; index < words.Length; index++) File.WriteAllText(release + "." + index, string.Empty);
                await running.WaitAsync(TimeSpan.FromSeconds(30));
                if (!worker.HasExited) worker.Kill(entireProcessTree: true);
                await worker.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                await Task.WhenAll(output, error);
            }
        }, WalkthroughSteps.Remaining(deadline));
    }

    private static string Details(WalkthroughWindow window) => string.Join(" ",
        window.Window.GetVisualDescendants().OfType<TextBlock>()
            .Where(block => block.IsEffectivelyVisible).Select(block => block.Text));
}
