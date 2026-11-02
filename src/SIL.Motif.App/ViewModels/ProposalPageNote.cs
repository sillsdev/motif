using System.Globalization;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// The note a page that follows the Proposal bar's view shows: what is paired, what is today's only, or why
/// the Proposal has nothing to show. One composer keeps every page's note shaped the same.
/// </summary>
internal static class ProposalPageNote
{
    /// <summary>What the page has to say about the Proposal's side, passed to <see cref="For"/>.</summary>
    /// <param name="Noun">What the page shows, in lower case, such as "findings".</param>
    /// <param name="Reading">Whether the Proposal's values are still being read.</param>
    /// <param name="Unreadable">Whether reading them failed.</param>
    /// <param name="HasValue">Whether the Proposal has a value the page can show.</param>
    /// <param name="Paired">The sentence that says exactly what a pair reads and what stays today's.</param>
    /// <param name="Alone">The sentence that says what the Proposal view reads and what stays today's.</param>
    /// <param name="Unpaired">Extra sentence naming what lacks a side, or null when nothing does.</param>
    internal sealed record Parts(string Noun, bool Reading, bool Unreadable, bool HasValue, string Paired, string Alone,
        string? Unpaired = null);

    /// <summary>The note, or null outside Proposal mode and while the Proposal's values are still being read.</summary>
    public static string? For(WorkspaceContext context, Parts parts)
    {
        if (context.Lens is null || parts.Reading) return null;
        var view = ProposalViewScope.ViewOf(context);
        if (!parts.HasValue)
        {
            var why = parts.Unreadable
                ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalPageUnreadableFormat, parts.Noun)
                : TrialStandingWords.For(context.ProposalStanding).Sentence;
            return why + " " + string.Format(CultureInfo.CurrentCulture, view == MarkupView.Proposal
                ? MotifAppStrings.ProposalPageNothingFormat : MotifAppStrings.ProposalPageTodayOnlyFormat, parts.Noun);
        }
        if (view == MarkupView.Today)
            return string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalPageTodayViewNoteFormat, parts.Noun);
        var core = view == MarkupView.Proposal ? parts.Alone : parts.Paired;
        if (TrialStandingWords.For(context.ProposalStanding).OldNumbers is { } old)
            core = string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalPageOldNoteFormat, old, core);
        return parts.Unpaired is { Length: > 0 } unpaired ? core + " " + unpaired : core;
    }
}
