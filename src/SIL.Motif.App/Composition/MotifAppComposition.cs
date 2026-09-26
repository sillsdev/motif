using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands;

namespace SIL.Motif.App.Composition;

/// <summary>The replaceable desktop inputs used while composing Motif's real App.</summary>
/// <param name="ManagedRoot">The worker root every command the window runs uses.</param>
/// <param name="ParserPath">The parser the window's commands run, or <see langword="null"/> for none.</param>
/// <param name="RunnerLauncher">Starts the job runner, for the same root and parser.</param>
/// <param name="TimeProvider">
/// The clock read for when an Assessment completed and when a Handoff was written. The shell's
/// FieldWorks-freshness sentence and the grammar check's elapsed time read the system clock instead.
/// </param>
/// <param name="ProjectPicker">Chooses a project, or <see langword="null"/> for the native dialog.</param>
/// <param name="HandoffFolderPicker">Chooses a Handoff folder, or <see langword="null"/> for the native dialog.</param>
/// <param name="FileDragSource">Drags Handoff files out, or <see langword="null"/> for the native drag.</param>
/// <param name="RememberBounds">
/// Whether the window restores and saves its size and place in the person's settings; off unless installed,
/// so a composed test window never writes the person's settings.
/// </param>
public sealed record MotifAppOptions(
    string ManagedRoot,
    string? ParserPath,
    IJobRunnerLauncher RunnerLauncher,
    TimeProvider TimeProvider,
    IProjectPicker? ProjectPicker = null,
    IHandoffFolderPicker? HandoffFolderPicker = null,
    IFileDragSource? FileDragSource = null,
    bool RememberBounds = false)
{
    /// <summary>
    /// The installed window's inputs: the root, parser and runner the command line would use
    /// (<see cref="CommandClientOptions.ForInstallation"/>), the system clock, and native desktop adapters.
    /// </summary>
    public static MotifAppOptions ForInstallation()
    {
        var commands = CommandClientOptions.ForInstallation();
        return new MotifAppOptions(commands.ManagedRoot, commands.ParserPath, commands.RunnerLauncher,
            TimeProvider.System, RememberBounds: true);
    }
}

/// <summary>Builds the window's real command client and page models from desktop or test inputs.</summary>
public static class MotifAppComposition
{
    /// <summary>Creates the window and workspace that the App installs into its desktop lifetime.</summary>
    /// <param name="options">The inputs a test may replace; everything else is the product's own.</param>
    /// <param name="startGate">
    /// Holds the real command client's Assessment or Handoff before it starts, so a test can observe the
    /// window mid-run. It can delay a command but not replace the client, which is always the
    /// <see cref="CommandClient"/> built from <paramref name="options"/>.
    /// </param>
    public static MotifAppCompositionResult Create(MotifAppOptions options, ICommandStartGate? startGate = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ManagedRoot);
        ArgumentNullException.ThrowIfNull(options.RunnerLauncher);
        ArgumentNullException.ThrowIfNull(options.TimeProvider);

        var window = new MainWindow(options.RememberBounds);
        var nativePickers = new AvaloniaStoragePickers(window);
        var commandClient = new CommandClient(new CommandClientOptions(
            options.ManagedRoot, options.ParserPath, options.RunnerLauncher, startGate));
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
