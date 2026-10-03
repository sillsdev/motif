using CommunityToolkit.Mvvm.ComponentModel;
using SIL.Motif.App.Services;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// What Motif's error window shows about an error that escaped the UI thread.
/// </summary>
/// <remarks>
/// Its full exception details stay local; the problem action opens a separate report preview.
/// </remarks>
public sealed partial class CrashReportViewModel : ObservableObject
{
    /// <summary>The window's heading.</summary>
    public const string Headline = "Motif ran into a problem it could not recover from";

    /// <summary>The plain summary under the heading.</summary>
    public const string Summary =
        "Motif will close when you close this window. Review the problem report before you choose to share it.";

    /// <summary>Thanks the person for a report that can improve the demo.</summary>
    public const string ThankYou = "Thank you for helping improve the demo.";

    /// <summary>Shows <paramref name="report"/> and keeps its local details available in the window.</summary>
    public CrashReportViewModel(CrashReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        Report = report;
    }

    /// <summary>The error the window reports.</summary>
    public CrashReport Report { get; }

    /// <summary>The allowlisted report that the preview window shows before sharing.</summary>
    public ProblemReport ProblemReport => Report.ProblemReport;

    /// <summary>The error's own message.</summary>
    public string Message => Report.Message;

    /// <summary>The error's full account, folded under Details.</summary>
    public string Details => Report.Details;

}
