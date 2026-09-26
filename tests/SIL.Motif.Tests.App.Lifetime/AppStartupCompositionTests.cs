using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
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
public sealed class AppStartupCompositionTests : IDisposable
{
    private static readonly TimeSpan StepLimit = TimeSpan.FromSeconds(30);
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
            Assert.Same(session.Workspace, Assert.IsType<HandoffWorkspaceViewModel>(window.DataContext));
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

    private static async Task Until(Func<bool> condition, string failure)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, failure);
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

    private sealed class RecordingProjectPicker : IProjectPicker
    {
        public int Calls { get; private set; }

        public Task<string?> PickProjectFileAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult<string?>(null);
        }
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
