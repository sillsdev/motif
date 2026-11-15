using SIL.Motif.App;
using System.Text.Json;
using Avalonia;
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

/// <summary>Checks when recorded statistics are read and how partial reruns affect their scope.</summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class TimingStatisticsRefreshTests
{
    private const string ProjectPath = @"C:\projects\one.fwdata";

    private static readonly DateTimeOffset Saved = new(2026, 9, 5, 10, 58, 0, TimeSpan.Zero);

    private static readonly BaselineToken Token = new(
        "project-1", "sha256:" + new string('a', 64), "1", "2026-09-05T11:02:00Z", "sha256:" + new string('b', 64));

    [Fact]
    public void DiagnosticsDoesNotStartASeparateStatisticsRead()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (fake, workspace) = NewWorkspace();
            fake.OnTiming((request, _) => Task.FromResult(CommandOutcome<TimingResponse>.Success(Timing(request.By))));
            fake.StatsCompletesWith(new StatsCommandResponse("assessment-parse", "grammar.json", "cache.sqlite", null, []));
            await workspace.SetProjectAsync(ProjectPath);
            if (workspace.Context.Setup is { IsOpen: true } setup) await setup.SkipCommand.ExecuteAsync(null);
            workspace.Context.Diagnostics = true;
            workspace.Context.PublishEvidence(new WorkspaceEvidence(Run(), Saved, WasRerun: false));
            await workspace.Context.EvidencePublication;
            workspace.Context.OpenDiagnostics(DiagnosticsTab.SpeedByRule);
            Assert.Empty(fake.StatsRequests);

            var statistics = new StatisticsViewModel(fake) { ProjectPath = ProjectPath, AssessmentId = "assessment-timing" };
            await statistics.LoadCommand.ExecuteAsync(null);
            Assert.Single(fake.StatsRequests);
            await workspace.DisposeAsync();
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void DetailedStatisticsKeepTheOriginalRunsCompletionWhenAWordTimingWasReplaced()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (fake, workspace) = NewWorkspace();
            fake.OnTiming((request, _) => Task.FromResult(CommandOutcome<TimingResponse>.Success(Timing(request.By))));
            var rawWord = JsonDocument.Parse(
                "{\"form\":\"motifa\",\"attempts\":12,\"passes\":1,\"elapsed_ns\":1000000000,\"capped\":true,\"timed_out\":false}")
                .RootElement.Clone();
            fake.StatsCompletesWith(new StatsCommandResponse("assessment-timing", "grammar.json", "cache.sqlite",
                null, [rawWord]));
            await workspace.SetProjectAsync(ProjectPath);
            if (workspace.Context.Setup is { IsOpen: true } setup) await setup.SkipCommand.ExecuteAsync(null);
            var statistics = new StatisticsViewModel(fake)
            {
                ProjectPath = ProjectPath, AssessmentId = "assessment-timing", HasTimingOverrides = true,
            };

            var run = Run() with
            {
                Words = [new AssessmentWordResult("motifa", "analysed", false, "Search completed", 4, null)],
                TimingOverrideAssessmentIds = ["assessment-rerun"],
            };
            workspace.Assess.Result = run;
            Assert.NotNull(workspace.Assess.Words.Find("motifa"));
            workspace.Context.PublishEvidence(new WorkspaceEvidence(run, Saved, WasRerun: false));
            await workspace.Context.EvidencePublication;
            await statistics.LoadCommand.ExecuteAsync(null);

            Assert.True(Assert.Single(statistics.Rows).IsIncomplete);
            Assert.Contains("original Parse all words run", statistics.ScopeNote, StringComparison.Ordinal);
            Assert.Contains("later word reruns appear above in Timing", statistics.ScopeNote, StringComparison.Ordinal);
            await workspace.DisposeAsync();
        }, TimeSpan.FromSeconds(30));
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
