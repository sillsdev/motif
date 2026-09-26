using Avalonia.Controls;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands;
using SIL.Motif.Host.Parser;

namespace SIL.Motif.App.Composition;

/// <summary>The replaceable desktop inputs used while composing Motif's real App.</summary>
public sealed record MotifAppOptions(
    string ManagedRoot,
    string? ParserPath,
    IJobRunnerLauncher RunnerLauncher,
    TimeProvider TimeProvider,
    IProjectPicker? ProjectPicker = null,
    IHandoffFolderPicker? HandoffFolderPicker = null,
    IFileDragSource? FileDragSource = null)
{
    /// <summary>Uses the installed parser and per-user worker root, with native desktop adapters.</summary>
    public static MotifAppOptions ForInstallation()
    {
        var commandOptions = CommandClientOptions.ForInstallation();
        return new MotifAppOptions(commandOptions.ManagedRoot, commandOptions.ParserPath,
            commandOptions.RunnerLauncher, TimeProvider.System);
    }
}

/// <summary>Builds the window's real command client and page models from desktop or test inputs.</summary>
public static class MotifAppComposition
{
    /// <summary>Creates the window and workspace that the App installs into its desktop lifetime.</summary>
    public static MotifAppCompositionResult Create(MotifAppOptions options, bool rememberBounds = false,
        ICommandClient? commandClientOverride = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ManagedRoot);
        ArgumentNullException.ThrowIfNull(options.RunnerLauncher);
        ArgumentNullException.ThrowIfNull(options.TimeProvider);

        var window = new MainWindow(rememberBounds);
        var nativePickers = new AvaloniaStoragePickers(window);
        var commandClient = commandClientOverride ?? new CommandClient(new CommandClientOptions(
            options.ManagedRoot, options.ParserPath, options.RunnerLauncher));
        var selection = new SelectionViewModel(commandClient);
        var assess = new AssessViewModel(commandClient, selection, options.TimeProvider);
        var workspace = new HandoffWorkspaceViewModel(
            new ProjectViewModel(commandClient, options.ProjectPicker ?? nativePickers),
            new BaselineViewModel(commandClient),
            selection,
            assess,
            options.HandoffFolderPicker ?? nativePickers,
            options.FileDragSource ?? nativePickers,
            commandClient);
        workspace.PageModel<AiHandoffPageModel>().Handoff.SetTimeProvider(options.TimeProvider);
        window.Compose(workspace);
        return new MotifAppCompositionResult(window, workspace);
    }
}

/// <summary>The window and workspace composed together for one Motif desktop lifetime.</summary>
public sealed record MotifAppCompositionResult(MainWindow Window, HandoffWorkspaceViewModel Workspace);
