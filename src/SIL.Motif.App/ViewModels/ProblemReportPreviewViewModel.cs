using CommunityToolkit.Mvvm.ComponentModel;
using SIL.Motif.App.Services;

namespace SIL.Motif.App.ViewModels;

/// <summary>Previews the safe problem report before copying it or opening an issue form.</summary>
public sealed partial class ProblemReportPreviewViewModel : ObservableObject
{
    private readonly IClipboard _clipboard;
    private readonly IUriLauncher _launcher;

    /// <summary>Builds the preview with the clipboard and browser owned by its window.</summary>
    /// <param name="report">The safe report and optional local details.</param>
    /// <param name="clipboard">The destination for the report after preview.</param>
    /// <param name="launcher">The browser opened after preview.</param>
    public ProblemReportPreviewViewModel(ProblemReport report, IClipboard clipboard, IUriLauncher launcher)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(launcher);
        Report = report;
        _clipboard = clipboard;
        _launcher = launcher;
    }

    /// <summary>The report whose rendered contents appear in the preview.</summary>
    public ProblemReport Report { get; }

    /// <summary>The report text currently visible in the preview.</summary>
    public string ReportText => Report.ToText(IncludeLocalDetails);

    /// <summary>Whether the person chose to include local diagnostic text in the report.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReportText))]
    private bool _includeLocalDetails;

    /// <summary>The latest result of copying or opening the report.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string? _status;

    /// <summary>Whether the latest action has a result to display.</summary>
    public bool HasStatus => Status is not null;

    /// <summary>Copies the reviewed report text.</summary>
    public async Task CopyReportAsync()
    {
        Status = null;
        try
        {
            await _clipboard.SetTextAsync(ReportText).ConfigureAwait(true);
            Status = "The reviewed report is on the clipboard.";
        }
        catch (Exception)
        {
            Status = "Motif could not copy the report.";
        }
    }

    /// <summary>Opens a new issue form with the reviewed report prefilled; it does not submit the form.</summary>
    public async Task OpenIssueAsync()
    {
        Status = null;
        try
        {
            Status = await _launcher.LaunchAsync(Report.IssueUri(ReportText)).ConfigureAwait(true)
                ? "The issue form should open. Review the report there before submitting it."
                : AppLinks.NewIssue;
        }
        catch (Exception)
        {
            Status = AppLinks.NewIssue;
        }
    }
}
