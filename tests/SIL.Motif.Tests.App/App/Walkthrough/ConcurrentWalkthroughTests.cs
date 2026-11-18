using System.Diagnostics;
using SIL.LCModel;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

[Collection(LcmCacheTestCollection.Name)]
public sealed class ConcurrentWalkthroughTests
{
    [Fact]
    public void TwoWindowsShareTheManagedRootWhileAssessmentsRunOneAtATime()
    {
        var managedRoot = Path.Combine(
            Path.GetTempPath(), "SIL.Motif.Walkthrough.Concurrent.Managed", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(managedRoot);
        var firstProjectPath = CreateSeededProject(Path.Combine(managedRoot, "first-project"));
        var secondProjectPath = CreateSeededProject(Path.Combine(managedRoot, "second-project"));
        var firstParserPath = FakeParser.CopyRecordingInvocations(Path.Combine(managedRoot, "first-parser"));
        var secondParserPath = FakeParser.CopyRecordingInvocations(Path.Combine(managedRoot, "second-parser"));
        var firstStarted = Path.Combine(managedRoot, "first-assessment-started");
        var secondStarted = Path.Combine(managedRoot, "second-assessment-started");
        var secondStartOrder = Path.Combine(managedRoot, "second-assessment-start-order");
        var firstRelease = Path.Combine(managedRoot, "first-assessment-release");
        var secondRelease = Path.Combine(managedRoot, "second-assessment-release");
        const string parserWaitMessage = "Waiting for parser capacity.";
        var deadline = WalkthroughSteps.DeadlineIn(120);

        try
        {
            AvaloniaHeadlessFixture.RunUntilComplete(() =>
            {
                using var firstWalkthrough = new WalkthroughWindow(
                    managedRoot, firstProjectPath, parserPath: firstParserPath);
                using var secondWalkthrough = new WalkthroughWindow(
                    managedRoot, secondProjectPath, parserPath: secondParserPath);
                WalkthroughSteps.ChooseProjectAndCaptureBaseline(firstWalkthrough, deadline);
                WalkthroughSteps.ChooseProjectAndCaptureBaseline(secondWalkthrough, deadline);

                var firstToken = Assert.IsType<SIL.Motif.Contract.Baselines.BaselineToken>(
                    firstWalkthrough.Workspace.Baseline.Token);
                var secondToken = Assert.IsType<SIL.Motif.Contract.Baselines.BaselineToken>(
                    secondWalkthrough.Workspace.Baseline.Token);
                Assert.NotEqual(firstToken.ProjectIdentity, secondToken.ProjectIdentity);

                firstWalkthrough.TypePastedWords(string.Join(Environment.NewLine,
                    SeededProject.FirstForm, SeededProject.SecondForm));
                secondWalkthrough.TypePastedWords(string.Join(Environment.NewLine,
                    SeededProject.FirstForm, SeededProject.SecondForm));
                FakeParser.BehaveBesideExecutable(firstParserPath, new
                {
                    subcommands = new Dictionary<string, object>
                    {
                        ["batch"] = new { startedPath = firstStarted, holdUntilPath = firstRelease },
                    },
                });
                FakeParser.BehaveBesideExecutable(secondParserPath, new
                {
                    subcommands = new Dictionary<string, object>
                    {
                        ["batch"] = new
                        {
                            startedPath = secondStarted,
                            holdUntilPath = secondRelease,
                            startOrderPath = secondStartOrder,
                            startOrderAfterPath = firstRelease,
                        },
                    },
                });

                firstWalkthrough.Workspace.Assess.RunCommand.Execute(null);
                var firstExecution = firstWalkthrough.Workspace.Assess.RunCommand.ExecutionTask;
                Assert.NotNull(firstExecution);

                try
                {
                    firstWalkthrough.WaitUntil(
                        () => File.Exists(firstStarted) &&
                            firstWalkthrough.Workspace.Assess.State == RunState.Running,
                        WalkthroughSteps.Remaining(deadline), "the first fake Assessment did not start");
                    Assert.NotEmpty(PanglossProcesses.Snapshot(firstParserPath));
                    secondWalkthrough.Workspace.Assess.RunCommand.Execute(null);
                    var secondExecution = secondWalkthrough.Workspace.Assess.RunCommand.ExecutionTask;
                    Assert.NotNull(secondExecution);
                    secondWalkthrough.WaitUntil(
                        () => File.Exists(secondStarted) ||
                            secondWalkthrough.Workspace.Assess.Progress?.Message == parserWaitMessage,
                        WalkthroughSteps.Remaining(deadline),
                        "the second Assessment neither waited for parser capacity nor reached its fake parser");
                    Assert.False(File.Exists(secondStarted),
                        "the second fake parser started while the first parser was held");
                    Assert.Equal(parserWaitMessage, secondWalkthrough.Workspace.Assess.Progress?.Message);
                    Assert.Empty(PanglossProcesses.Snapshot(secondParserPath));

                    File.WriteAllText(firstRelease, string.Empty);
                    firstWalkthrough.WaitUntil(
                        () => firstExecution.IsCompleted && firstWalkthrough.Workspace.Assess.State == RunState.Completed &&
                            firstWalkthrough.Workspace.Context.EvidencePublication.IsCompleted && File.Exists(secondStarted) &&
                            File.Exists(secondStartOrder) &&
                            secondWalkthrough.Workspace.Assess.State == RunState.Running,
                        WalkthroughSteps.Remaining(deadline), "the second fake Assessment did not follow the first");
                    Assert.Equal("after", File.ReadAllText(secondStartOrder));
                    Assert.NotEmpty(PanglossProcesses.Snapshot(secondParserPath));

                    File.WriteAllText(secondRelease, string.Empty);
                    secondWalkthrough.WaitUntil(
                        () => secondExecution.IsCompleted && secondWalkthrough.Workspace.Assess.State == RunState.Completed &&
                            secondWalkthrough.Workspace.Context.EvidencePublication.IsCompleted,
                        WalkthroughSteps.Remaining(deadline), "the second Assessment did not finish");
                }
                finally
                {
                    File.WriteAllText(firstRelease, string.Empty);
                    File.WriteAllText(secondRelease, string.Empty);
                }

                var expectedWords = new[] { SeededProject.FirstForm, SeededProject.SecondForm }
                    .Order(StringComparer.Ordinal).ToArray();
                var firstRuns = WalkthroughStoreAssertions.ListInvocations(firstProjectPath)
                    .Where(invocation => invocation.BaselineToken == firstToken).ToArray();
                Assert.Contains(firstRuns, invocation => invocation.Selection.PastedWords
                    .Order(StringComparer.Ordinal).SequenceEqual(expectedWords));
                var secondRuns = WalkthroughStoreAssertions.ListInvocations(secondProjectPath)
                    .Where(invocation => invocation.BaselineToken == secondToken).ToArray();
                Assert.Contains(secondRuns, invocation => invocation.Selection.PastedWords
                    .Order(StringComparer.Ordinal).SequenceEqual(expectedWords));
                return Task.CompletedTask;
            }, WalkthroughSteps.Remaining(deadline));
        }
        finally
        {
            WalkthroughTestFiles.DeleteDirectory(managedRoot);
        }
    }

    private static string CreateSeededProject(string projectRoot)
    {
        using var cache = NewLangProjFixture.CreateCache(projectRoot);
        var seed = SeededProject.Seed(cache);
        SeededProject.SeedText(cache, seed);
        new FwDataProjectLoader().Save(cache);
        return cache.ProjectId.Path;
    }
}
