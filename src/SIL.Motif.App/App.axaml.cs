using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.Composition;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;

namespace SIL.Motif.App;

public sealed partial class App : Application
{
    private readonly MotifAppOptions _options;
    private readonly bool _rememberBounds;

    public App() : this(MotifAppOptions.ForInstallation(), rememberBounds: true) { }

    public App(MotifAppOptions options) : this(options, rememberBounds: true) { }

    public App(MotifAppOptions options, bool rememberBounds)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _rememberBounds = rememberBounds;
    }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var composition = MotifAppComposition.Create(_options, _rememberBounds);
            desktop.MainWindow = composition.Window;

            desktop.Exit += (_, _) => _ = composition.Workspace.DisposeAsync();
            LoadKnownProjects(composition.Workspace.Project);
        }

        base.OnFrameworkInitializationCompleted();
    }

    // Fire-and-forget by design: nothing else in startup waits for the Known-project list to resolve.
    private static async void LoadKnownProjects(ProjectViewModel project)
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
