using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Assistants;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Preferences;

namespace SIL.Motif.App.Composition;

/// <summary>The replaceable desktop inputs used while composing Motif's real App.</summary>
/// <param name="ManagedRoot">The worker root every command the window runs uses.</param>
/// <param name="ParserPath">The parser the window's commands run, or <see langword="null"/> for none.</param>
/// <param name="RunnerLauncher">Starts the job runner, for the same root and parser.</param>
/// <param name="TimeProvider">
/// The clock every part of the window reads: when an Assessment completed, when a Handoff was written, how long a
/// grammar check has run, and what the freshness sentence calls today.
/// </param>
/// <param name="ProjectPicker">Chooses a project, or <see langword="null"/> for the native dialog.</param>
/// <param name="HandoffFolderPicker">Chooses a Handoff folder, or <see langword="null"/> for the native dialog.</param>
/// <param name="FileDragSource">Drags Handoff files out, or <see langword="null"/> for the native drag.</param>
/// <param name="Clipboard">Takes what a person copies, or <see langword="null"/> for the window's own clipboard.</param>
/// <param name="DiagnosticDialogs">
/// Gives each window that opens and saves diagnostic JSON its dialogs, or <see langword="null"/> for the native
/// dialogs, each owned by the window whose action started it.
/// </param>
/// <param name="CrashWindow">
/// The clipboard, save dialog and mail launcher of the error window, or <see langword="null"/> for that window's
/// own native ones.
/// </param>
/// <param name="RememberBounds">
/// Whether the window restores and saves its size and place in the person's settings; off unless installed,
/// so a composed test window never writes the person's settings.
/// </param>
/// <param name="UserPreferencesStore">The shared per-user App preferences, or process-local defaults when omitted.</param>
/// <param name="AdvancedAiModePreferences">Where the user's Advanced AI mode choice is read and saved.</param>
/// <param name="AssistantConnections">The replaceable assistant discovery and connection service.</param>
public sealed record MotifAppOptions(
    string ManagedRoot,
    string? ParserPath,
    IJobRunnerLauncher RunnerLauncher,
    TimeProvider TimeProvider,
    IProjectPicker? ProjectPicker = null,
    IHandoffFolderPicker? HandoffFolderPicker = null,
    IFileDragSource? FileDragSource = null,
    IClipboard? Clipboard = null,
    IDiagnosticWindowDialogs? DiagnosticDialogs = null,
    CrashWindowServices? CrashWindow = null,
    bool RememberBounds = false,
    IUserPreferencesStore? UserPreferencesStore = null,
    IAdvancedAiModePreferenceStore? AdvancedAiModePreferences = null,
    IAssistantConnectionService? AssistantConnections = null)
{
    /// <summary>The command surface the window may show for the current preference choice.</summary>
    public CommandSurfacePolicy SurfacePolicy =>
        new(developerCommandsEnabled: false, advancedAiModeEnabled: AdvancedAiModePreferences?.IsEnabled ?? false);

    /// <summary>
    /// The installed window's inputs: the root, parser and runner the command line would use
    /// (<see cref="CommandClientOptions.ForInstallation"/>), the system clock, and native desktop adapters.
    /// </summary>
    public static MotifAppOptions ForInstallation()
    {
        var commands = CommandClientOptions.ForInstallation();
        return new MotifAppOptions(commands.ManagedRoot, commands.ParserPath, commands.RunnerLauncher,
            TimeProvider.System, RememberBounds: true,
            UserPreferencesStore: new FileUserPreferencesStore(FileUserPreferencesStore.DefaultPath),
            AdvancedAiModePreferences: FileAdvancedAiModePreferenceStore.ForInstallation(),
            AssistantConnections: AssistantConnectionService.ForInstallation());
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

        var surfacePolicy = options.SurfacePolicy;
        var preferences = options.UserPreferencesStore ?? new MemoryUserPreferencesStore();
        var applicationFacts = ApplicationFacts.ForApp(options.ManagedRoot, options.ParserPath);
        var window = new MainWindow(options.RememberBounds, uriLauncher: null,
            preferencesStore: preferences, applicationFacts: applicationFacts);
        var nativePickers = new AvaloniaStoragePickers(window);
        var diagnosticDialogs = options.DiagnosticDialogs ?? nativePickers;
        var techDemoNotice = new TechDemoNoticeViewModel(
            new TechDemoNoticePreferencesAdapter(preferences), new AvaloniaLauncher(window));
        var commandClient = new CommandClient(new CommandClientOptions(
            options.ManagedRoot, options.ParserPath, options.RunnerLauncher, startGate, options.TimeProvider));
        var selection = new SelectionViewModel(commandClient);
        var assess = new AssessViewModel(commandClient, selection, options.TimeProvider,
            new TraceViewPreferencesAdapter(preferences));
        var workspace = new WorkspaceShellViewModel(
            new ProjectViewModel(commandClient, options.ProjectPicker ?? nativePickers, options.ManagedRoot),
            new BaselineViewModel(commandClient, options.TimeProvider),
            selection,
            assess,
            options.HandoffFolderPicker ?? nativePickers,
            options.FileDragSource ?? nativePickers,
            commandClient,
            options.TimeProvider,
            options.Clipboard ?? new AvaloniaClipboard(window),
            diagnosticDialogs.For(window),
            diagnosticDialogs,
            techDemoNotice,
            advancedAiModeEnabled: surfacePolicy.AdvancedAiModeEnabled,
            advancedAiModePreferences: options.AdvancedAiModePreferences,
            assistantConnections: options.AssistantConnections,
            uriLauncher: new AvaloniaLauncher(window));
        window.Compose(workspace);
        var crashes = new CrashReporter(options.TimeProvider, options.CrashWindow ?? new CrashWindowServices());
        return new MotifAppCompositionResult(window, workspace, crashes, surfacePolicy);
    }
}

/// <summary>The window, workspace and error reporting composed together for one Motif desktop lifetime.</summary>
public sealed record MotifAppCompositionResult(
    MainWindow Window,
    WorkspaceShellViewModel Workspace,
    CrashReporter Crashes,
    CommandSurfacePolicy SurfacePolicy);
