using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Model.AppliedLog;

namespace SIL.Motif.Tests.App.Walkthrough;

/// <summary>
/// What a replay waits for and fixes before it draws a capture, so two runs of one script draw the same picture
/// unless the window really differs.
/// </summary>
internal static partial class WalkthroughCaptureSettle
{
    private const string ManagedRootStandIn = "[walkthrough folder]";

    [GeneratedRegex(@"\b\d{8}T\d{6}Z\b")]
    private static partial Regex WallClockStamp();

    [GeneratedRegex(@"(?<=\bTried |\bBaseline of )(?:(?:Mon|Tue|Wed|Thu|Fri|Sat|Sun) \d{1,2} [A-Z][a-z]{2}(?: \d{4})?, )?\d{1,2}:\d{2}\s[AP]M")]
    private static partial Regex TrialTimeInBar();

    [GeneratedRegex(@"\[walkthrough folder\]\S*")]
    private static partial Regex MaskedFolderPath();

    /// <summary>Moves the pointer off every control, so no button is drawn pressed or hovered by where the last click ended.</summary>
    internal static void ParkPointer(this WalkthroughWindow walkthrough)
    {
        walkthrough.Window.MouseMove(new Point(-1, -1));
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Waits until nothing that still changes what a page shows is running: the project open, evidence publication,
    /// a parse or Refresh, the automatic grammar answer and the retained Parsimony read.
    /// </summary>
    internal static void WaitUntilQuiet(this WalkthroughWindow walkthrough, TimeSpan timeout, string why)
    {
        var workspace = walkthrough.Workspace;
        var grammar = workspace.PageModel<WarningsPageModel>().Grammar;
        var parsimony = workspace.PageModel<ParsimonyPageModel>();
        walkthrough.WaitUntil(() => !workspace.IsProjectOpening && !workspace.Context.IsOpeningProject &&
            workspace.Context.EvidencePublication.IsCompleted && !workspace.Assess.IsActive &&
            !workspace.RefreshCommand.IsRunning &&
            (!workspace.Baseline.HasBaseline || grammar.HasChecked && !grammar.IsLoading) &&
            parsimony.AutomaticWork.IsCompleted, timeout, why,
            () => $"IsProjectOpening={workspace.IsProjectOpening}; Assess.IsActive={workspace.Assess.IsActive}; " +
                $"Refresh.IsRunning={workspace.RefreshCommand.IsRunning}; Grammar.HasChecked={grammar.HasChecked}; " +
                $"Grammar.IsLoading={grammar.IsLoading}; Parsimony.Work={parsimony.AutomaticWork.Status}; " +
                $"EvidencePublication={workspace.Context.EvidencePublication.Status}");
    }

    /// <summary>
    /// Waits until two frames drawn one after the other are the same picture, so no state is still settling. A replay
    /// that draws no pictures compares two empty frames, so this is then two layout passes and layout checks.
    /// </summary>
    internal static void WaitUntilFrameIsStable(this WalkthroughWindow walkthrough, TimeSpan timeout, string why)
    {
        byte[]? previous = null;
        var passes = 0;
        walkthrough.WaitUntil(() =>
        {
            passes++;
            walkthrough.Window.UpdateLayout();
            var frame = WalkthroughArtifacts.CaptureFrame(walkthrough.Window);
            var same = previous is not null && previous.AsSpan().SequenceEqual(frame);
            previous = frame;
            return same;
        }, timeout, why, () => $"{passes} frames were drawn and none matched the one before it.");
    }

    /// <summary>
    /// Replaces text that names the run, not the product, with a fixed stand-in: the walkthrough's own folder, which
    /// holds this process's id, and a Receipt's UTC stamp, which the Apply command takes from the machine's clock.
    /// The rest of a masked path is written with backslashes, so a Windows and a Linux run draw the same text. The two
    /// times in the Proposal bar's "Tried ... on Baseline of ..." sentence come from the Trial's own run, which takes
    /// the machine's clock, so they are shown as the capture time.
    /// </summary>
    internal static void MaskRunVaryingText(this WalkthroughWindow walkthrough)
    {
        var stamp = AppliedLogFormat.FormatTimestamp(WalkthroughReplayTestSupport.CaptureTime.UtcDateTime);
        const string TrialTime = "12:00 PM";
        foreach (var block in walkthrough.Window.GetVisualDescendants().OfType<TextBlock>())
        {
            if (string.IsNullOrEmpty(block.Text) || block.Inlines?.Count > 0) continue;
            var masked = TrialTimeInBar().Replace(WallClockStamp().Replace(MaskedFolderPath().Replace(
                block.Text.Replace(walkthrough.ManagedRoot, ManagedRootStandIn, StringComparison.OrdinalIgnoreCase),
                path => path.Value.Replace('/', '\\')), stamp), TrialTime);
            if (!string.Equals(masked, block.Text, StringComparison.Ordinal)) block.Text = masked;
        }
        Dispatcher.UIThread.RunJobs();
    }
}
