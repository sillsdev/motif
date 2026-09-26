using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.Composition;
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

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            StartDesktop(desktop, _options ?? MotifAppOptions.ForInstallation());

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Composes Motif's window into <paramref name="desktop"/>, starts loading the Known projects, and closes
    /// the session when the lifetime exits. This is the whole of the App's desktop startup:
    /// <see cref="OnFrameworkInitializationCompleted"/> calls it, and so does a test host that starts the real
    /// App again in one process. Avalonia allows one setup and one lifetime per process, and a lifetime's
    /// shutdown also ends its dispatcher, so such a host closes each session with
    /// <see cref="MotifDesktopSession.CloseAsync"/> and composes the next into the same lifetime.
    /// </summary>
    public MotifDesktopSession StartDesktop(IClassicDesktopStyleApplicationLifetime desktop, MotifAppOptions options)
    {
        ArgumentNullException.ThrowIfNull(desktop);
        var composition = MotifAppComposition.Create(options);
        desktop.MainWindow = composition.Window;
        var session = new MotifDesktopSession(composition, LoadKnownProjects(composition.Workspace.Project));
        EventHandler<ControlledApplicationLifetimeExitEventArgs> onExit = (_, _) => session.CloseAsync();
        desktop.Exit += onExit;
        session.DetachWith(() => desktop.Exit -= onExit);
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
