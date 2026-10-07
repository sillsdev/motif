using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using System.Text.Json;
using SIL.Motif.App.Composition;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App;

public sealed partial class App : Application
{
    private readonly MotifAppOptions? _options;

    public App() { }

    public App(MotifAppOptions options) =>
        _options = options ?? throw new ArgumentNullException(nameof(options));

    /// <summary>The session of the desktop lifetime most recently started, or <see langword="null"/>.</summary>
    public MotifDesktopSession? Session { get; private set; }

    public override void Initialize()
    {
        TextStyles.RegisterUiFontCollection(Avalonia.Media.FontManager.Current);
        AvaloniaXamlLoader.Load(this);
        WindowZoomPolicy.Apply(this, ZoomPolicy.DefaultPercent);
        TextStyles.ApplyApplicationFontResources(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            StartDesktop(desktop, _options ?? MotifAppOptions.ForInstallation());
            if (desktop.Args.Contains("--release-launch-check", StringComparer.Ordinal))
                CompleteReleaseLaunchCheck(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void CompleteReleaseLaunchCheck(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var resultPath = Environment.GetEnvironmentVariable("MOTIF_RELEASE_LAUNCH_RESULT");
        if (string.IsNullOrWhiteSpace(resultPath))
            throw new InvalidOperationException("MOTIF_RELEASE_LAUNCH_RESULT is required for the launch check.");
        var window = desktop.MainWindow ?? throw new InvalidOperationException("The App did not create its window.");
        window.Opened += (_, _) =>
        {
            try
            {
                var fullPath = Path.GetFullPath(resultPath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                File.WriteAllText(fullPath, JsonSerializer.Serialize(new
                {
                    opened = true,
                    title = window.Title,
                    width = window.Bounds.Width,
                    height = window.Bounds.Height,
                }));
                Dispatcher.UIThread.Post(() => desktop.Shutdown(0), DispatcherPriority.Background);
            }
            catch (Exception)
            {
                Dispatcher.UIThread.Post(() => desktop.Shutdown(CrashExitCode), DispatcherPriority.Background);
            }
        };
    }

    /// <summary>The exit code Motif ends with when an error that escaped the UI thread closed it.</summary>
    public const int CrashExitCode = 1;

    /// <summary>
    /// Composes Motif's window into <paramref name="desktop"/>, starts loading the Known projects, reports an
    /// error that escapes the UI thread in Motif's error window, and closes the session when the lifetime exits.
    /// This is the whole of the App's desktop startup: <see cref="OnFrameworkInitializationCompleted"/> calls it,
    /// and so does a test host that starts the real App again in one process: it closes each session with
    /// <see cref="MotifDesktopSession.CloseAsync"/> and composes the next into the same lifetime, pinned by
    /// `TheRealStartupStartsAgainInTheSameProcess`.
    /// </summary>
    public MotifDesktopSession StartDesktop(IClassicDesktopStyleApplicationLifetime desktop, MotifAppOptions options)
    {
        ArgumentNullException.ThrowIfNull(desktop);
        var composition = MotifAppComposition.Create(options);
        desktop.MainWindow = composition.Window;
        var session = new MotifDesktopSession(composition, LoadKnownProjects(composition.Workspace.Project));
        EventHandler<ControlledApplicationLifetimeExitEventArgs> onExit = (_, _) => session.CloseAsync();
        desktop.Exit += onExit;
        var stopReporting = composition.Crashes.Attach(Dispatcher.UIThread, composition.Window,
            () => desktop.Shutdown(CrashExitCode));
        session.DetachWith(() =>
        {
            desktop.Exit -= onExit;
            stopReporting();
        });
        Session = session;
        return session;
    }

    // Nothing in startup waits for the Known-project list, so this never faults and is not awaited there.
    private static async Task LoadKnownProjects(ProjectViewModel project)
    {
        try
        {
            await project.LoadKnownProjectsAsync();
        }
        catch (Exception)
        {
            // A missing or unreadable machine store still leaves Browse available; startup must not crash.
        }
    }
}
