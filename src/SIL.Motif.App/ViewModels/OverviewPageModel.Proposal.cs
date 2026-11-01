using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

public sealed partial class OverviewPageModel
{
    private bool _proposalReading;
    private bool _proposalUnreadable;

    // The Overview of the open Proposal's Trial, read by the same query as Overview; null when there is none.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextCoverageMain))]
    [NotifyPropertyChangedFor(nameof(AccuracyMain))]
    [NotifyPropertyChangedFor(nameof(SpeedMain))]
    [NotifyPropertyChangedFor(nameof(WarningsCount))]
    [NotifyPropertyChangedFor(nameof(ShowTiles))]
    private OverviewResponse? _proposalOverview;

    /// <summary>
    /// What to tell the reader about the numbers while a Proposal is open: how a pair reads, or why there is no
    /// second number. Null outside Proposal mode and while the Trial's numbers are still being read.
    /// </summary>
    public string? ProposalNote
    {
        get
        {
            if (Context.Lens is null || _proposalReading) return null;
            if (ProposalViewScope.ViewOf(Context) == MarkupView.Today && ProposalOverview is not null)
                return MotifAppStrings.OverviewTodayViewNote;
            if (ProposalOverview is not null && Overview?.AssessmentId is null && ProposalOverview.AssessmentId is null &&
                ProposalViewScope.ViewOf(Context) != MarkupView.Today)
                return MotifAppStrings.OverviewNeitherParseNote;
            if (ShowsProposalAlone) return ProposalAloneNote();
            if (ProposalOverview is not null)
            {
                var paired = TrialStandingWords.For(Context.ProposalStanding).OldNumbers is { } old
                    ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.OverviewProposalOldPairedNoteFormat, old)
                    : MotifAppStrings.OverviewProposalPairedNote;
                return UnpairedNote() is { Length: > 0 } unpaired ? paired + " " + unpaired : paired;
            }
            var why = _proposalUnreadable
                ? MotifAppStrings.OverviewProposalUnreadableNote
                : TrialStandingWords.For(Context.ProposalStanding).Sentence;
            return why + " " + MotifAppStrings.OverviewProposalTodayOnlyNote;
        }
    }

    // Under the Proposal view each headline reads the Proposal's value alone, so no pairing or today's value shows.
    private bool ShowsProposalAlone => Context.Lens is not null && ProposalViewScope.ViewOf(Context) == MarkupView.Proposal;

    // The open Proposal has a parse the non-Today views can show even when today has none.
    private bool ProposalHasParse => Context.Lens is not null && ProposalViewScope.ViewOf(Context) != MarkupView.Today &&
        ProposalOverview?.AssessmentId is not null;

    private bool ShowsPairs => Context.Lens is not null && ProposalViewScope.ViewOf(Context) == MarkupView.TodayToProposal;

    // A value the Proposal lacks is said so, never filled with today's.
    private string ProposalAloneHeadline(string? value) =>
        value ?? (_proposalReading ? MotifAppStrings.OverviewProposalReading : MotifAppStrings.OverviewProposalNone);

    private string ProposalAloneNote()
    {
        if (ProposalOverview is not { } read)
        {
            var why = _proposalUnreadable
                ? MotifAppStrings.OverviewProposalUnreadableNote
                : TrialStandingWords.For(Context.ProposalStanding).Sentence;
            return why + " " + MotifAppStrings.OverviewProposalNoNumbersNote;
        }
        var note = TrialStandingWords.For(Context.ProposalStanding).OldNumbers is { } old
            ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.OverviewProposalViewOldNoteFormat, old)
            : MotifAppStrings.OverviewProposalViewNote;
        var missing = new List<string>();
        if (read.AssessmentId is null)
        {
            missing.Add(MotifAppStrings.TextCoverage);
            missing.Add(MotifAppStrings.ApprovedAnalysesKept);
        }
        if (read.Timing.Work.MeasuredWordCount <= 0 || read.AssessmentId is null) missing.Add(MotifAppStrings.Speed);
        if (read.Warnings is null) missing.Add(MotifAppStrings.GrammarWarnings);
        return missing.Count == 0 ? note : note + " " +
            PluralText.Format(MotifAppStrings.OverviewProposalViewGapLeadPattern, missing.Count) + " " +
            string.Join(", ", missing) + ".";
    }

    private void RaiseHeadlines()
    {
        OnPropertyChanged(nameof(TextCoverageMain));
        OnPropertyChanged(nameof(AccuracyMain));
        OnPropertyChanged(nameof(SpeedMain));
        OnPropertyChanged(nameof(WarningsCount));
        OnPropertyChanged(nameof(ShowTiles));
        OnPropertyChanged(nameof(ShowTodayDetail));
        OnPropertyChanged(nameof(HidesTodayDetail));
    }

    /// <summary>Whether the Overview has something to say about the Proposal's numbers.</summary>
    public bool HasProposalNote => ProposalNote is not null;

    // A Trial that measured nothing, or is still running, has no numbers to read.
    private string? ProposalToRead() =>
        Context.Lens is { } lens && Context.EvidenceLens.Proposal is not null ? lens.ProposalId : null;

    private void SetProposalReading(bool reading)
    {
        _proposalReading = reading;
        RaiseProposalNote();
    }

    private void ShowProposalRead(CommandOutcome<OverviewResponse>? read)
    {
        _proposalReading = false;
        _proposalUnreadable = read is { Succeeded: false };
        ProposalOverview = read is { Succeeded: true } ? read.Value : null;
        RaiseProposalNote();
    }

    private void RaiseProposalNote()
    {
        OnPropertyChanged(nameof(ProposalNote));
        OnPropertyChanged(nameof(HasProposalNote));
    }

    // A headline pairs only when both sides measured it; otherwise it shows today's alone and the note says so.
    private bool CoverageIsPaired(out OverviewResponse today, out OverviewResponse proposal)
    {
        today = Overview!;
        proposal = ProposalOverview!;
        return ShowsPairs && Overview is not null && ProposalOverview is { AssessmentId: not null };
    }

    private bool WarningsArePaired => ShowsPairs && Overview is not null && ProposalOverview?.Warnings is not null;

    private static bool Measured(OverviewResponse? overview) =>
        overview is { AssessmentId: not null, Timing.Work.MeasuredWordCount: > 0 };

    // Work steps pair when the Proposal has them and today has none, or the Proposals page would compare the two.
    private bool SpeedIsPaired(out OverviewResponse proposal)
    {
        proposal = ProposalOverview!;
        return ShowsPairs && Overview is not null && Measured(ProposalOverview) &&
            (!Measured(Overview) || ProposalNumberText.HasWorkComparison(Context.Lens?.Trial?.Work));
    }

    private enum Gap { TodayNone, ProposalNone, NotComparable }

    // Says which headlines lack a side, grouped by why, so no reason names the wrong side.
    private string UnpairedNote()
    {
        var today = Overview;
        var gaps = new List<(Gap Gap, string Name)>();
        var coverageGap = !CoverageIsPaired(out _, out _) ? Gap.ProposalNone
            : today?.AssessmentId is null ? Gap.TodayNone : (Gap?)null;
        if (coverageGap is { } coverage)
        {
            gaps.Add((coverage, MotifAppStrings.TextCoverage));
            gaps.Add((coverage, MotifAppStrings.ApprovedAnalysesKept));
        }
        if (!SpeedIsPaired(out _))
            gaps.Add((!Measured(ProposalOverview) ? Gap.ProposalNone : Gap.NotComparable, MotifAppStrings.Speed));
        else if (!Measured(today)) gaps.Add((Gap.TodayNone, MotifAppStrings.Speed));
        if (!WarningsArePaired) gaps.Add((Gap.ProposalNone, MotifAppStrings.GrammarWarnings));
        else if (today?.Warnings is null) gaps.Add((Gap.TodayNone, MotifAppStrings.GrammarWarnings));
        return string.Join(" ", gaps.GroupBy(gap => gap.Gap).OrderBy(group => group.Key).Select(group =>
            PluralText.Format(group.Key == Gap.TodayNone ? MotifAppStrings.OverviewProposalTodayNoneLeadPattern
                : MotifAppStrings.OverviewProposalUnpairedLeadPattern, group.Count()) + " " +
            string.Join(", ", group.Select(gap => gap.Name)) + ". " + group.Key switch
            {
                Gap.TodayNone => MotifAppStrings.OverviewProposalUnpairedTodayNone,
                Gap.ProposalNone => MotifAppStrings.OverviewProposalUnpairedProposalNone,
                _ => MotifAppStrings.OverviewProposalUnpairedNotComparable,
            }));
    }

    private static string PairedHeadline(string today, string proposal) =>
        string.Format(CultureInfo.CurrentCulture, MotifAppStrings.OverviewPairFormat, today, proposal);

    // Two headlines over the same Selection share one denominator, so the pair reads "7 → 8 of 9 words parse".
    private static string CoveragePair(OverviewResponse today, OverviewResponse proposal) =>
        today.AssessmentId is not null && today.SelectionWordCount == proposal.SelectionWordCount
            ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.OverviewCoveragePairFormat,
                Number(today.TextCoverage.ParsedWords), Number(proposal.TextCoverage.ParsedWords),
                Number(today.SelectionWordCount))
            : PairedHeadline(CoverageMainOf(today), CoverageMainOf(proposal));

    private static string AccuracyPair(OverviewResponse today, OverviewResponse proposal) =>
        today.AssessmentId is not null && today.Accuracy.ApprovedWordCount == proposal.Accuracy.ApprovedWordCount &&
        today.Accuracy.ApprovedWordCount > 0
            ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.OverviewAccuracyPairFormat,
                Number(today.Accuracy.ApprovedWordsKept), Number(proposal.Accuracy.ApprovedWordsKept),
                Number(today.Accuracy.ApprovedWordCount))
            : PairedHeadline(AccuracyMainOf(today), AccuracyMainOf(proposal));
}
