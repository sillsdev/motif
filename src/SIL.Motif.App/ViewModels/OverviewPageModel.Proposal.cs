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
        return Overview is { AssessmentId: not null } && ProposalOverview is { AssessmentId: not null };
    }

    private bool WarningsArePaired => Overview?.Warnings is not null && ProposalOverview?.Warnings is not null;

    // Work steps pair only where the Proposals page would show a Work comparison for the same Trial.
    private bool SpeedIsPaired(out OverviewResponse proposal)
    {
        proposal = ProposalOverview!;
        return Overview is { AssessmentId: not null, Timing.Work.MeasuredWordCount: > 0 } &&
            ProposalOverview is { AssessmentId: not null, Timing.Work.MeasuredWordCount: > 0 } &&
            ProposalNumberText.HasWorkComparison(Context.Lens?.Trial?.Work);
    }

    private enum Gap { TodayNone, ProposalNone, NotComparable }

    // Says which headlines show today's alone, grouped by why, so no reason names the wrong side.
    private string UnpairedNote()
    {
        var today = Overview;
        var proposal = ProposalOverview;
        var coverageGap = today?.AssessmentId is null ? Gap.TodayNone : Gap.ProposalNone;
        var gaps = new List<(Gap Gap, string Name)>();
        if (!CoverageIsPaired(out _, out _))
        {
            gaps.Add((coverageGap, MotifAppStrings.TextCoverage));
            gaps.Add((coverageGap, MotifAppStrings.ApprovedAnalysesKept));
        }
        if (!SpeedIsPaired(out _))
            gaps.Add((today is not { AssessmentId: not null, Timing.Work.MeasuredWordCount: > 0 } ? Gap.TodayNone
                : proposal is not { AssessmentId: not null, Timing.Work.MeasuredWordCount: > 0 } ? Gap.ProposalNone
                : Gap.NotComparable, MotifAppStrings.Speed));
        if (!WarningsArePaired)
            gaps.Add((today?.Warnings is null ? Gap.TodayNone : Gap.ProposalNone, MotifAppStrings.GrammarWarnings));
        return string.Join(" ", gaps.GroupBy(gap => gap.Gap).OrderBy(group => group.Key).Select(group =>
            PluralText.Format(MotifAppStrings.OverviewProposalUnpairedLeadPattern, group.Count()) + " " +
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
        today.SelectionWordCount == proposal.SelectionWordCount
            ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.OverviewCoveragePairFormat,
                Number(today.TextCoverage.ParsedWords), Number(proposal.TextCoverage.ParsedWords),
                Number(today.SelectionWordCount))
            : PairedHeadline(CoverageMainOf(today), CoverageMainOf(proposal));

    private static string AccuracyPair(OverviewResponse today, OverviewResponse proposal) =>
        today.Accuracy.ApprovedWordCount == proposal.Accuracy.ApprovedWordCount && today.Accuracy.ApprovedWordCount > 0
            ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.OverviewAccuracyPairFormat,
                Number(today.Accuracy.ApprovedWordsKept), Number(proposal.Accuracy.ApprovedWordsKept),
                Number(today.Accuracy.ApprovedWordCount))
            : PairedHeadline(AccuracyMainOf(today), AccuracyMainOf(proposal));
}
