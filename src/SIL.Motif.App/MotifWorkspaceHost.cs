using Avalonia.Controls;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App;

/// <summary>
/// What a <see cref="Views.MotifWorkspaceView"/> needs from the window that hosts it: the workspace it shows, the
/// shared settings built from that workspace, and the window-owned services the view calls back into (problem
/// reports, the clipboard, online links). The window builds one and hands it to the view, so the view never
/// reaches for its window.
/// </summary>
internal sealed class MotifWorkspaceHost
{
    private readonly ProblemReportWindowServices _problemReportServices;
    private readonly Func<ProblemReport, Task> _showProblemReport;

    /// <summary>Builds the host and the settings model that the workspace's Settings page edits.</summary>
    /// <param name="workspace">The workspace the view shows.</param>
    /// <param name="owner">The window that owns the clipboard, browser and first-frame trace.</param>
    /// <param name="preferences">The shared preferences store.</param>
    /// <param name="applicationFacts">Application details used for problem reports.</param>
    /// <param name="uriLauncher">The adapter that opens online links.</param>
    /// <param name="problemReportServices">The text copier and browser the problem report uses.</param>
    /// <param name="showProblemReport">Opens a reviewed problem report preview over the owner.</param>
    public MotifWorkspaceHost(WorkspaceShellViewModel workspace, TopLevel owner, IUserPreferencesStore preferences,
        ApplicationFacts applicationFacts, IUriLauncher uriLauncher, ProblemReportWindowServices problemReportServices,
        Func<ProblemReport, Task> showProblemReport)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(owner);
        Workspace = workspace;
        Owner = owner;
        ApplicationFacts = applicationFacts;
        UriLauncher = uriLauncher;
        _problemReportServices = problemReportServices;
        _showProblemReport = showProblemReport;
        Settings = new SettingsViewModel(workspace, preferences, applicationFacts);
        workspace.PageModel<SettingsPageModel>().Settings = Settings;
    }

    /// <summary>The workspace the view shows.</summary>
    public WorkspaceShellViewModel Workspace { get; }

    /// <summary>The window that owns the clipboard, browser and first-frame trace.</summary>
    public TopLevel Owner { get; }

    /// <summary>The settings model shared by the Settings page and the window's zoom.</summary>
    public SettingsViewModel Settings { get; }

    /// <summary>Application details used for problem reports.</summary>
    public ApplicationFacts ApplicationFacts { get; }

    /// <summary>The adapter that opens online links.</summary>
    public IUriLauncher UriLauncher { get; }

    /// <summary>Shows the reviewed report before a person copies it or opens an issue form.</summary>
    public Task ShowProblemReportAsync(ProblemReport report) => _showProblemReport(report);

    /// <summary>Copies text with the owner's clipboard.</summary>
    public Task CopyTextAsync(string text) => _problemReportServices.CopyTextAsync(text, Owner);
}
