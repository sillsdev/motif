using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Checks that opening Detailed statistics starts its read once, without a second refresh control on the page.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class TimingStatisticsRefreshTests
{
    private const string ProjectPath = @"C:\projects\one.fwdata";

    private static readonly DateTimeOffset Saved = new(2026, 9, 5, 10, 58, 0, TimeSpan.Zero);

    private static readonly BaselineToken Token = new(
        "project-1", "sha256:" + new string('a', 64), "1", "2026-09-05T11:02:00Z", "sha256:" + new string('b', 64));

    [Fact]
    public void DetailedStatisticsLoadWhenOpenedWithoutASeparateRefreshButton()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (fake, workspace) = NewWorkspace();
            var kindTiming = new TaskCompletionSource<CommandOutcome<TimingResponse>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            fake.OnTiming((request, _) => request.By == "kind"
                ? kindTiming.Task
                : Task.FromResult(CommandOutcome<TimingResponse>.Success(Timing("rule"))));
            fake.StatsCompletesWith(new StatsCommandResponse("assessment-parse", "grammar.json", "cache.sqlite", null, []));
            await workspace.SetProjectAsync(ProjectPath);
            if (workspace.Context.Setup is { IsOpen: true } setup) await setup.SkipCommand.ExecuteAsync(null);
            var window = new MainWindow();
            window.Compose(workspace);
            window.Show();
            try
            {
                workspace.CurrentPage = WorkspacePage.Timing;
                workspace.Context.PublishEvidence(new WorkspaceEvidence(Run(), Saved, WasRerun: false));
                Settle(window);
                var statistics = workspace.PageModel<TimingPageModel>().Statistics;
                Assert.Equal(30, workspace.PageModel<TimingPageModel>().RerunSeconds);
                Assert.Equal(StepCap.DefaultSteps, workspace.PageModel<TimingPageModel>().RerunSteps);
                Assert.DoesNotContain(window.GetLogicalDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == "Refresh statistics");
                Assert.False(workspace.Context.EvidencePublication.IsCompleted);

                var details = window.GetLogicalDescendants().OfType<Expander>()
                    .Single(expander => Equals(expander.Header, "Detailed statistics"));
                details.IsExpanded = true;
                Settle(window);
                Assert.NotNull(statistics.LoadCommand.ExecutionTask);
                await statistics.LoadCommand.ExecutionTask!;
                Assert.Single(fake.StatsRequests);

                kindTiming.SetResult(CommandOutcome<TimingResponse>.Success(Timing("kind")));
                await workspace.Context.EvidencePublication;
                details.IsExpanded = false;
                details.IsExpanded = true;
                Settle(window);
                Assert.Single(fake.StatsRequests);
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromSeconds(30));
    }

    private static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static TimingResponse Timing(string by) => new("assessment-parse", "all", by, 3, 5, 8,
        [new SlowWordTiming("motifa", 9), new SlowWordTiming("motifb", 7), new SlowWordTiming("mofita", 5)],
        [
            new TimingAggregateRow("Plural", "Plural", 12, 0.5, 3),
            new TimingAggregateRow("Stem", "Stem", 8, 0.3, 3),
            new TimingAggregateRow("Suffix", "Suffix", 4, 0.2, 2),
        ],
        [new WordRuleTiming("motifa", 6, 10)])
    {
        Words =
        [
            new TimingWordRow("motifa", 9, "Finished"),
            new TimingWordRow("motifb", 7, "Finished"),
            new TimingWordRow("mofita", 5, "Finished"),
        ],
    };

    private static AssessCommandResponse Run() => new(
        new BaselineCaptureResponse(Token, ProjectPath, Saved, false, false), new SelectionProjection([], []), [],
        "summary")
    {
        InvocationId = "invocation/one",
        Measurements =
        [
            new ProducedAssessmentReference("assessment-parse", AssessmentKinds.ParseTime, "invocation/one"),
            new ProducedAssessmentReference("assessment-timing", AssessmentKinds.ObjectTiming, "invocation/one"),
        ],
    };

    private static (FakeCommandClient Fake, WorkspaceShellViewModel Workspace) NewWorkspace()
    {
        var fake = new FakeCommandClient();
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(Token, Saved, false)
        {
            ProjectLastWriteUtc = Saved,
        });
        fake.ListTextsCompletesWith(new TextInventoryResponse([], HasBaseline: true));
        var selection = new SelectionViewModel(fake);
        var workspace = new WorkspaceShellViewModel(
            new ProjectViewModel(fake, new ProjectPicker()), new BaselineViewModel(fake), selection,
            new AssessViewModel(fake, selection), new FolderPicker(), new DragSource(), fake);
        return (fake, workspace);
    }

    private sealed class ProjectPicker : IProjectPicker
    {
        public Task<string?> PickProjectFileAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class FolderPicker : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class DragSource : IFileDragSource
    {
        public Task<DragDropEffects> StartDragAsync(
            PointerPressedEventArgs trigger, IReadOnlyList<string> filePaths, DragDropEffects allowedEffects) =>
            Task.FromResult(DragDropEffects.None);
    }
}
