using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Avalonia.Threading;
using System.Globalization;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host;
using SIL.Motif.App.Composition;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using Xunit;

namespace SIL.Motif.Tests.App.Lifetime;

[Collection(MotifAppHostCollection.Name)]
public sealed class AppStartupCompositionTests(PristineProjectFixture pristine) : IDisposable
{
    private static readonly TimeSpan StepLimit = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan JourneyLimit = TimeSpan.FromSeconds(90);
    private readonly List<string> _roots = [];

    [Fact]
    public void TheRealStartupComposesTheWindowFromOnlyTheSubstitutedInputs()
    {
        var host = MotifAppHost.Shared;
        var defaultRootBefore = Listing(RunnerOptions.DefaultRoot);
        var picker = new RecordingProjectPicker();
        var options = Options(NewRoot(), picker);

        host.Run("start, load Known projects, Browse, and stop", StepLimit, async () =>
        {
            var session = host.Start(options);

            var window = Assert.IsType<MainWindow>(host.Lifetime.MainWindow);
            Assert.Same(session.Window, window);
            Assert.Same(session.Workspace, Assert.IsType<WorkspaceShellViewModel>(window.DataContext));
            await session.KnownProjectsLoaded;
            Assert.True(File.Exists(Path.Combine(options.ManagedRoot, "motif.db")),
                "The Known-project load did not open the machine database under the substituted root.");

            await SelectNewProjectAsync(session, () => picker.Calls == 1);

            await host.StopAsync();
            Assert.True(session.Closed.IsCompleted);
        });

        Assert.Equal(defaultRootBefore, Listing(RunnerOptions.DefaultRoot));
    }

    [Fact]
    public void TheRealStartupStartsAgainInTheSameProcess()
    {
        var host = MotifAppHost.Shared;
        var options = Options(NewRoot(), new RecordingProjectPicker());

        host.Run("start, restart over the same root, and stop", StepLimit, async () =>
        {
            var first = host.Start(options);
            await first.KnownProjectsLoaded;

            var second = await host.RestartAsync();

            Assert.True(first.Closed.IsCompleted, "Restarting did not dispose the first workspace.");
            Assert.False(first.Window.IsVisible, "Restarting did not close the first window.");
            Assert.NotSame(first.Window, second.Window);
            Assert.NotSame(first.Workspace, second.Workspace);
            Assert.Same(second.Window, host.Lifetime.MainWindow);
            Assert.Same(second.Workspace, second.Window.DataContext);
            await second.KnownProjectsLoaded;
            Assert.Empty(second.Workspace.Project.KnownProjects);
            await host.StopAsync();
        });
    }

    [Fact]
    public void TheRealStartupReportsAnErrorThatEscapesTheUiThreadInMotifsErrorWindow()
    {
        var host = MotifAppHost.Shared;
        var options = Options(NewRoot(), new RecordingProjectPicker());

        host.Run("start, let an error escape the UI thread, and stop", StepLimit, async () =>
        {
            var session = host.Start(options);
            await session.KnownProjectsLoaded;

            Dispatcher.UIThread.Post(() => throw new InvalidOperationException("escaped the UI thread"));
            Dispatcher.UIThread.RunJobs();

            var window = Assert.IsType<CrashWindow>(session.Crashes.Window);
            Assert.True(window.IsVisible, "The error window did not open.");
            Assert.Equal("escaped the UI thread", window.Model.Message);
            Assert.False(session.Window.IsEnabled, "The workspace stayed usable after an unhandled error.");

            await host.StopAsync();
            Assert.False(window.IsVisible, "Closing the session left the error window open.");
        });
    }

    [Fact]
    public void TheRealStartupRunsTheSubstitutedParserRunnerAndClock()
    {
        var host = MotifAppHost.Shared;
        using var project = new WalkthroughProject(pristine);
        const string word = "startup-word";
        var draft = SeedPendingChange(project, word);
        var parser = FakeParser.CopyRecordingInvocations(Path.Combine(NewRoot(), "parser"));
        var completedAt = new DateTime(2026, 3, 4, 10, 30, 0, DateTimeKind.Local);
        var picker = new RecordingProjectPicker(project.FwDataPath);
        var handoffParent = NewRoot();
        var options = new MotifAppOptions(
            project.ManagedRoot,
            parser,
            IsolatedRunner.Process(project.ManagedRoot, parser),
            new FixedClock(new DateTimeOffset(completedAt), TimeZoneInfo.Local),
            picker,
            new ChosenFolderPicker(Path.Combine(handoffParent, "handoff")),
            new NoOpDragSource());

        host.Run("open, assess, write a Handoff, and check the pending change", JourneyLimit, async () =>
        {
            var culture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            try
            {
                var session = host.Start(options);
                var workspace = session.Workspace;
                await session.KnownProjectsLoaded;
                await SelectNewProjectAsync(session, () => picker.Calls == 1);
                await Until(() => workspace.Baseline.HasBaseline,
                    () => "the chosen project did not open with its Baseline: project " +
                        workspace.Context.ProjectPath + ", Baseline " + workspace.Baseline.HasBaseline + " (" +
                        workspace.Baseline.ShownRefusal?.Sentence + ")");
                Assert.NotEqual(true, workspace.Context.Setup?.IsOpen);

                workspace.Selection.PastedWords = word;
                await workspace.Assess.RunCommand.ExecuteAsync(null);
                Assert.True(workspace.Assess.State == RunState.Completed, "The Assessment did not complete: " +
                    workspace.Assess.Refusal?.Code + " " + workspace.Assess.Refusal?.Message);
                await ShowPageAsync(session, WorkspacePage.AiHandoff);
                var handoff = workspace.PageModel<AiHandoffPageModel>().Handoff;
                await handoff.RunCommand.ExecuteAsync(null);
                Assert.True(handoff.State == RunState.Completed, "The Handoff was not written: " +
                    handoff.Refusal?.Code + " " + handoff.Refusal?.Message);
                Assert.Equal("Last written 10:30", await UntilFound(session.Window,
                    () => "written is '" + handoff.WrittenAtText + "'",
                    text => text.StartsWith("Last written", StringComparison.Ordinal)));
                Assert.StartsWith("Covers the Assessment of Wed 4 Mar, 10:30 AM: ", await UntilFound(session.Window,
                    () => "coverage is '" + handoff.CoverageText + "'",
                    text => text.StartsWith("Covers the Assessment of", StringComparison.Ordinal)));

                var windowRuns = FakeParser.Invocations(parser).Count(command => command == "batch");
                Assert.True(windowRuns > 0, "The window's runs did not reach the substituted parser.");

                await ShowPageAsync(session, WorkspacePage.Review);
                var review = workspace.PageModel<ReviewPageModel>();
                await Until(() => review.MeasureCommand.CanExecute(null), "the pending change did not reach Review");
                Assert.Equal(draft, workspace.Context.Changes.Snapshot.DraftId);
                await review.MeasureCommand.ExecuteAsync(null);
                Assert.Null(review.MeasurementRefusal);
                Assert.True(review.CanApply, "The Trial did not run through the substituted runner and parser: " +
                    review.ApplyBlockReason);
                Assert.True(FakeParser.Invocations(parser).Count(command => command == "batch") > windowRuns,
                    "The Trial did not reach the substituted parser through the runner.");
            }
            finally
            {
                CultureInfo.CurrentCulture = culture;
            }
        });
    }

    private static string SeedPendingChange(WalkthroughProject project, string word)
    {
        Guid wordformId = Guid.Empty;
        new FieldWorksSimulator(project.FwDataPath).SaveEdit(cache =>
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                wordformId = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                    .Create(TsStringUtils.MakeString(word, cache.DefaultVernWs)).Guid));
        var captured = BaselineCaptureCommand.Capture(
            new BaselineCaptureRequest(project.FwDataPath), project.ManagedRoot);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        var version = MotifProductVersion.CurrentText;
        var initial = PendingChanges.Load(new PendingChangesRequest(project.FwDataPath, version)).Value!;
        var put = PendingChanges.Put(new PutPendingChangeRequest(project.FwDataPath, version, initial.Revision,
            new ChangeIntent(CanonicalId.Mint().Value, "incorrect-spelling",
                CanonicalId.FromGuid(wordformId).Value, word)));
        Assert.True(put.Succeeded, put.Refusal?.Message);
        // Setup is skipped up front: whether its dialog opens races the Baseline's load, and it is not under test.
        var skipped = ProjectSetupCommands.Skip(new SkipSetupRequest(project.FwDataPath));
        Assert.True(skipped.Succeeded, skipped.Refusal?.Message);
        return put.Value!.DraftId!;
    }

    private static async Task ShowPageAsync(MotifDesktopSession session, WorkspacePage page)
    {
        if (session.Workspace.CurrentPage == page) return;
        var name = session.Workspace.PageOf(page).AutomationName;
        var entry = session.Window.GetLogicalDescendants().OfType<ListBoxItem>()
            .Single(item => AutomationProperties.GetName(item) == name);
        Click(session.Window, entry);
        await Until(() => session.Workspace.CurrentPage == page, "the " + name + " page did not open");
    }

    private static async Task<string> UntilFound(Window window, Func<string> state, Func<string, bool> matches)
    {
        string? found = null;
        await Until(() =>
        {
            window.UpdateLayout();
            found = window.GetVisualDescendants().OfType<TextBlock>()
                .Where(block => block.IsEffectivelyVisible)
                .Select(block => block.Text).FirstOrDefault(text => text is not null && matches(text));
            return found is not null;
        }, () => "the window never showed the expected text; " + state());
        return found!;
    }

    // The entry's command runs as the walkthrough runs it: a flyout's overlay takes no synthetic click.
    private static async Task SelectNewProjectAsync(MotifDesktopSession session, Func<bool> pickerCalled)
    {
        var menu = session.Window.FindControl<Button>("ProjectMenuButton")
            ?? throw new InvalidOperationException("The window has no project menu.");
        Click(session.Window, menu);
        var flyout = Assert.IsType<Flyout>(menu.Flyout);
        Assert.True(flyout.IsOpen, "The project menu did not open.");
        var entry = (flyout.Content as Control)?.GetLogicalDescendants().OfType<Button>().Single(button =>
            AutomationProperties.GetName(button) == "Select a new project")
            ?? throw new InvalidOperationException("The project menu has no content.");
        Assert.Same(session.Workspace.SelectNewProjectCommand, entry.Command);
        await Until(() => entry.IsEffectivelyEnabled, "'Select a new project' never became enabled");
        entry.Command!.Execute(entry.CommandParameter);
        await Until(pickerCalled, "choosing a new project did not ask the substituted picker");
        flyout.Hide();
    }

    private static Task Until(Func<bool> condition, string failure) => Until(condition, () => failure);

    private static async Task Until(Func<bool> condition, Func<string> failure)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, failure());
            await Task.Delay(20);
        }
    }

    private static void Click(Window window, Control control)
    {
        Assert.True(control.IsEffectivelyEnabled, $"'{AutomationProperties.GetName(control)}' is disabled.");
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("The control is not placed in the window.");
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static MotifAppOptions Options(string managedRoot, IProjectPicker picker)
    {
        var parser = FakeParser.ExecutablePath;
        return new MotifAppOptions(
            managedRoot,
            parser,
            new NoRunnerLauncher(new JobRunnerLaunchOptions(managedRoot, parser)),
            new FixedClock(new DateTimeOffset(2026, 3, 4, 10, 30, 0, TimeSpan.Zero)),
            picker,
            new CancelFolderPicker(),
            new NoOpDragSource());
    }

    private string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "SIL.Motif.AppStartup", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        _roots.Add(root);
        return root;
    }

    private static string[] Listing(string directory) => Directory.Exists(directory)
        ? Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.OrdinalIgnoreCase).ToArray()
        : [];

    public void Dispose()
    {
        foreach (var root in _roots)
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private sealed class RecordingProjectPicker(string? chosen = null) : IProjectPicker
    {
        public int Calls { get; private set; }

        public Task<string?> PickProjectFileAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(chosen);
        }
    }

    private sealed class ChosenFolderPicker(string folder) : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(folder);
    }

    private sealed class CancelFolderPicker : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class NoOpDragSource : IFileDragSource
    {
        public Task<DragDropEffects> StartDragAsync(
            PointerPressedEventArgs trigger, IReadOnlyList<string> filePaths, DragDropEffects allowedEffects) =>
            Task.FromResult(DragDropEffects.None);
    }
}
