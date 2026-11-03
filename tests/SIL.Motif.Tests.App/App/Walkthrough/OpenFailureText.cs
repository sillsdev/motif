using SIL.Motif.App.ViewModels;

namespace SIL.Motif.Tests.App.Walkthrough;

/// <summary>What a failed project open left on the window, worded for a test failure message.</summary>
internal static class OpenFailureText
{
    /// <summary>
    /// The open refusal's sentence and code with the escaped exception, whose stack names the open step that threw.
    /// The window shows the sentence only, so a test that waits on a project must say the rest itself.
    /// </summary>
    public static string Describe(WorkspaceShellViewModel workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (workspace.OpenRefusal is not { } refusal) return "no open refusal";
        var cause = refusal.FailureException?.ToString() ?? refusal.ReportDetails ?? "no exception detail";
        return $"open refusal '{refusal.Sentence}' ({refusal.Code}): {cause}";
    }
}
