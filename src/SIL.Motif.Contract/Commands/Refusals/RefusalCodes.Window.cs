namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    /// <summary>A window check found a problem the user must fix before continuing.</summary>
    public const string WindowCheck = "window.check";
    /// <summary>A diagnostic could not be saved or copied.</summary>
    public const string WindowDiagnosticNotWritten = "window.diagnostic-not-written";
    /// <summary>A saved diagnostic file could not be read at all.</summary>
    public const string WindowDiagnosticUnreadable = "window.diagnostic-unreadable";
    /// <summary>The action needs an open project and none is open.</summary>
    public const string WindowNoProject = "window.no-project";
    /// <summary>The window could not read a Proposal's saved Difference.</summary>
    public const string WindowDifferenceReadFailed = "window.difference-read-failed";
    /// <summary>A check stopped because the Refresh run before it was cancelled while it waited.</summary>
    public const string WindowRefreshCancelled = "window.refresh-cancelled";
    /// <summary>A check stopped because the Refresh run before it failed or captured no Baseline.</summary>
    public const string WindowRefreshFailed = "window.refresh-failed";
    /// <summary>A check stopped because the Refresh before it could not start while other work held the project.</summary>
    public const string WindowRefreshUnavailable = "window.refresh-unavailable";
    /// <summary>The window could not make a Proposal's Difference.</summary>
    public const string WindowDifferenceMakeFailed = "window.difference-make-failed";
    /// <summary>The window could not run and record a Proposal's Dry Run.</summary>
    public const string WindowDryRunFailed = "window.dry-run-failed";
    /// <summary>The window could not open the chosen project.</summary>
    public const string WindowOpenFailed = "window.open-failed";
    /// <summary>The window could not open a Proposal.</summary>
    public const string WindowOpenProposalFailed = "window.open-proposal-failed";
    /// <summary>The window could not read an open Proposal document's evidence.</summary>
    public const string WindowProposalDocumentReadFailed = "window.proposal-document-read";
}
