using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;

namespace SIL.Motif.App;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            var workspace = ComposeWorkspace(window);
            window.Compose(workspace);
            desktop.MainWindow = window;

            desktop.Exit += (_, _) => _ = workspace.DisposeAsync();
            LoadKnownProjects(workspace.Project);
        }

        base.OnFrameworkInitializationCompleted();
    }

    // The one composition root: nowhere else names a concrete ICommandClient, picker, or drag source.
    private static HandoffWorkspaceViewModel ComposeWorkspace(MainWindow window)
    {
        var commandClient = new CommandClient();
        var pickers = new AvaloniaStoragePickers(window);
        var selection = new SelectionViewModel(commandClient);

        return new HandoffWorkspaceViewModel(
            new ProjectViewModel(commandClient, pickers),
            new BaselineViewModel(commandClient),
            selection,
            new AssessViewModel(commandClient, selection),
            new StatisticsViewModel(commandClient),
            new HandoffViewModel(commandClient, selection, pickers, pickers));
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
