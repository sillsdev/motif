using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using SIL.Motif.App.Composition;
using SIL.Motif.App.Services;
using SIL.Motif.App.Views;
using SIL.Motif.Commands;
using Xunit;

namespace SIL.Motif.Tests.App.Lifetime;

public sealed class AppStartupCompositionTests
{
    [Fact]
    public void WorkerReadsExplicitRootAndParserArguments()
    {
        var root = Path.Combine(Path.GetTempPath(), "worker-root");
        var parser = Path.Combine(Path.GetTempPath(), "pangloss.exe");
        var options = SIL.Motif.Worker.RunnerOptions.Read(
            [SIL.Motif.Worker.RunnerOptions.RootArgument, root,
                SIL.Motif.Worker.RunnerOptions.ParserArgument, parser]);

        Assert.Equal(root, options.Root);
        Assert.Equal(parser, options.ParserPath);
        Assert.Null(SIL.Motif.Worker.RunnerOptions.Read(
            [SIL.Motif.Worker.RunnerOptions.NoParserArgument]).ParserPath);
    }

    [Fact]
    public void AppComposesItsMainWindowThroughTheClassicDesktopLifetime()
    {
        var managedRoot = Path.Combine(Path.GetTempPath(), "SIL.Motif.AppStartup", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(managedRoot);
        var options = new MotifAppOptions(
            managedRoot,
            Path.Combine(managedRoot, "pangloss.exe"),
            new NoOpRunnerLauncher(),
            new FixedTimeProvider(new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero)),
            new CancelProjectPicker(),
            new CancelFolderPicker(),
            new NoOpDragSource());
        var lifetime = new ClassicDesktopStyleApplicationLifetime
        {
            Args = [],
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
        };

        try
        {
            AppBuilder.Configure(() => new SIL.Motif.App.App(options, rememberBounds: false))
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithLifetime(lifetime);

            var window = Assert.IsType<MainWindow>(lifetime.MainWindow);
            Assert.Same(lifetime, Application.Current!.ApplicationLifetime);
            Assert.NotNull(window.DataContext);

            window.Show();
            window.ApplyTemplate();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }
        finally
        {
            lifetime.Shutdown();
            Dispatcher.UIThread.RunJobs();
            if (Directory.Exists(managedRoot)) Directory.Delete(managedRoot, recursive: true);
        }
    }

    private sealed class NoOpRunnerLauncher : IJobRunnerLauncher
    {
        public void Start(string projectPath, JobRunnerLaunchOptions options,
            Action<string>? reportWarning = null) { }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class CancelProjectPicker : IProjectPicker
    {
        public Task<string?> PickProjectFileAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
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
