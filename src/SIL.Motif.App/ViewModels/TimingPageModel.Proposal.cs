using System.Globalization;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

public sealed partial class TimingPageModel
{
    // Only what the page shows is kept from the Proposal's read; its per-word rows are dropped with the response.
    private sealed record ProposalTiming(WordWorkSummary Work, int StoppedWords, WordSetDigest Words,
        TimingCostComparison KindComparison, TimingCostComparison RuleComparison);

    // What a held read belongs to; the chosen words count by reference, as changing them replaces the list.
    private sealed record ProposalTimingKey(string Project, string ProposalId, string TrialId, string WordSet, int Top,
        IReadOnlyList<string>? ExplicitWords);

    private ProposalTiming? _proposalTiming;
    private ProposalTimingKey? _heldKey;
    private ProposalTimingKey? _readingKey;
    private ProposalTimingKey? _failedKey;
    private Task _proposalRead = Task.CompletedTask;
    private int _proposalGeneration;
    private WordSetDigest _todayWords;
    private int _kindVersion;
    private int _todayWordsVersion = -1;

    private int RequestTop => _wordSet is TimingWordSet.Slowest ? Math.Max(1, SlowestCount) : 10;

    // The count the last read used, so editing the count box alone does not change what is held.
    private int _appliedTop = 10;

    private ProposalTimingKey? CurrentKey() => Context.ProjectPath is { } path &&
        Context.EvidenceLens is { InProposalMode: true, Proposal: { } trial }
        ? new ProposalTimingKey(path, trial.Trial.ProposalId, trial.Trial.JobId, WordSet, _appliedTop, _explicitWords)
        : null;

    // What is held counts only for the key it was read for, so an older word set's numbers can never show.
    private ProposalTiming? Held => _heldKey is { } held && held == CurrentKey() ? _proposalTiming : null;

    // A failed read is not held: the page says so and reads again when next shown or published to.
    private bool ProposalReading => CurrentKey() is { } key && key != _heldKey && key != _failedKey;

    private bool ProposalUnreadable => _failedKey is { } failed && failed == CurrentKey();

    private bool ShowsPairs => Context.Lens is not null && ProposalViewScope.ViewOf(Context) == MarkupView.TodayToProposal;

    private bool ShowsProposalAlone => Context.Lens is not null && ProposalViewScope.ViewOf(Context) == MarkupView.Proposal;

    private bool OffToday => Context.Lens is not null && ProposalViewScope.ViewOf(Context) != MarkupView.Today;

    /// <summary>Whether absolute Baseline work details are appropriate for the current view.</summary>
    public bool ShowBaselineWorkDetails => !OffToday;

    /// <summary>Whether the selected rule's absolute Baseline work is shown.</summary>
    public bool ShowBaselineRuleDetails => !OffToday && HasRule;

    /// <summary>Whether the Baseline-only percentile card is shown.</summary>
    public bool ShowBaselinePercentiles => !OffToday && HasSelectedWords;

    private bool ProposalMeasured => Held is { Work.MeasuredWordCount: > 0 };

    // Hashed on first use after today's words change, by version so no response is kept and none is stale.
    private WordSetDigest TodayWords
    {
        get
        {
            if (_todayWordsVersion == _kindVersion) return _todayWords;
            _todayWords = KindTiming is null ? default : WordSetDigest.Of(KindTiming.Words.Select(word => word.Word));
            _todayWordsVersion = _kindVersion;
            return _todayWords;
        }
    }

    // True only when both sides were read and cover the same words; no read today makes no claim either way.
    private bool WordsChecked => KindTiming is not null && Held is { } held &&
        held.Words == TodayWords && held.Work.MeasuredWordCount == KindTiming.Work.MeasuredWordCount;

    private bool SameWords => ShowsPairs && ProposalMeasured && (KindTiming is null || WordsChecked);

    // The Proposals page pairs two work totals only when it can compare them; so does this page.
    private bool WorkComparable => !HasHeadline || ProposalNumberText.HasWorkComparison(Context.Lens?.Trial?.Work);

    private bool WorkIsPaired => SameWords && WordsChecked && WorkComparable;

    private string? ProposalStopped => ProposalMeasured ? Held!.StoppedWords.ToString("N0", CultureInfo.CurrentCulture) : null;

    /// <summary>Whether the headline card shows: today's measured words, or the Proposal's alone in its own view.</summary>
    public bool ShowHeadlineCard => HasHeadline || ShowsProposalAlone || WorkIsPaired;

    /// <summary>Whether the page body shows: today's parse, or in the other views the Proposal's when today has none.</summary>
    public bool ShowPageBody => !Context.NeedsAssessment || (OffToday && ProposalMeasured);

    /// <summary>Whether the card offering a first parse shows: not once the Proposal's own numbers do.</summary>
    public bool ShowParsePromptCard => !ShowPageBody;

    /// <summary>Whether the by-kind card shows: today's chosen words, or the Proposal's own in its own view.</summary>
    public bool ShowKindCard => HasSelectedWords || (OffToday && ProposalMeasured && (ShowsPairs || WorkComparable));

    /// <summary>The work by kind: today's, paired with the Proposal's, or the Proposal's alone in its own view.</summary>
    public IReadOnlyList<TimingShare> KindShares => OffToday
        ? WorkIsPaired || (ShowsProposalAlone && WordsChecked && WorkComparable)
            ? PairedKindShares() : []
        : TodayKindShares;

    /// <summary>The note saying what is paired, what stays today's and why the Proposal may have nothing to show.</summary>
    public string? ProposalNote => ProposalPageNote.For(Context, new ProposalPageNote.Parts(
        MotifAppStrings.TimingNoun, ProposalReading, ProposalUnreadable, ProposalMeasured, PairedNote(), AloneNote()));

    /// <summary>Paired rule percentages and explicit missing or new objects from the retained Trial scope.</summary>
    public IReadOnlyList<TimingRuleChange> ProposalRuleChanges => ShowsPairs && Held is { } held
        ? held.RuleComparison.Objects.Select(row => new TimingRuleChange(row.Kind == "unattributed"
            ? MotifAppStrings.TimingNotAttributedLabel : row.Name,
            row.Kind == "unattributed" ? string.Empty : row.Kind + ":" + row.Key +
                (row.Scope is null ? string.Empty : " (" + row.Scope + ")"),
            row.Change.PercentChange is { } percent ? percent.ToString("+0.##;-0.##;0", CultureInfo.CurrentCulture) + "%"
                : row.Status switch { "new" => MotifAppStrings.TimingRuleNew, "missing" => MotifAppStrings.TimingRuleMissing,
                    _ => MotifAppStrings.TimingNotRecorded })).ToArray() : [];

    /// <summary>Explains when the retained Trial has no comparable rule detail.</summary>
    public string? ProposalRuleNote => ShowsPairs && Held is { RuleComparison.Objects.Count: 0 }
        ? MotifAppStrings.TimingRuleUnavailable : null;

    /// <summary>Whether the rule-comparison note is displayed.</summary>
    public bool HasProposalRuleNote => ProposalRuleNote is not null;

    /// <summary>Whether paired rule evidence is displayed.</summary>
    public bool HasProposalRuleChanges => ProposalRuleChanges.Count > 0;

    /// <summary>Whether the page has a note to show.</summary>
    public bool HasProposalNote => ProposalNote is not null;

    // Says "for the same words" only when the words were checked to be the same.
    private string PairedNote()
    {
        if (KindTiming is null) return MotifAppStrings.TimingProposalViewNote;
        if (!WordsChecked) return MotifAppStrings.TimingProposalDifferentWordsNote;
        return WorkComparable ? MotifAppStrings.TimingProposalPairedNote : MotifAppStrings.TimingProposalStoppedOnlyNote;
    }

    private string AloneNote() => WorkComparable
        ? MotifAppStrings.TimingProposalViewNote : MotifAppStrings.TimingProposalViewNotComparableNote;

    private string WorkHeadline => ProposalReading
        ? MotifAppStrings.OverviewProposalReading
        : WordsChecked && WorkComparable ? WorkComparison(Held?.KindComparison.Total.PercentChange)
        : MotifAppStrings.ProposalWorkNotComparable;

    private static string WorkComparison(double? change)
    {
        if (change is null) return MotifAppStrings.ProposalWorkNotComparable;
        var whole = TrialWorkNotice.WholePercent(change.Value);
        return whole == 0 ? MotifAppStrings.ParserWorkComparisonSame : string.Format(CultureInfo.CurrentCulture,
            change > 0 ? MotifAppStrings.ParserWorkComparisonMoreFormat : MotifAppStrings.ParserWorkComparisonLessFormat, whole);
    }

    private string ProposalHeadline(string today, string? proposal, bool work)
    {
        var none = MotifAppStrings.ProposalPageNone;
        if (ShowsProposalAlone)
            return work && ProposalMeasured && !WorkComparable ? none
                : proposal ?? (ProposalReading ? MotifAppStrings.OverviewProposalReading : none);
        if (ShowsPairs && ProposalReading) return Pair(today, MotifAppStrings.OverviewProposalReading);
        return (work ? WorkIsPaired : SameWords) ? Pair(today, proposal ?? none) : today;
    }

    private static string Pair(string today, string proposal) => string.Format(CultureInfo.CurrentCulture,
        MotifAppStrings.OverviewPairFormat, today.Length > 0 ? today : MotifAppStrings.TimingNoneToday, proposal);

    private string ProposalCaption()
    {
        if (!ProposalMeasured) return string.Empty;
        var count = Held!.Work.MeasuredWordCount;
        return count == 1 ? MotifAppStrings.TimingCaptionOneWord
            : string.Format(CultureInfo.CurrentCulture, IsAllSelected
                ? MotifAppStrings.TimingCaptionAllWordsFormat : MotifAppStrings.TimingCaptionTheseWordsFormat,
                count.ToString("N0", CultureInfo.CurrentCulture));
    }

    private IReadOnlyList<TimingShare> PairedKindShares() => Held?.KindComparison.Objects.Select(row =>
        new TimingShare(TimingShare.KindName(row.Kind), row.Kind, null, null, 0, null)
        {
            ProposalWorkText = WorkComparison(row.Change.PercentChange), IsProposalOnly = row.Status == "new",
        }).ToArray() ?? [];

    // Reads the open Proposal's timing only while this page is shown and the held read is for another key.
    private Task EnsureProposalSideAsync(CancellationToken cancellationToken)
    {
        if (Context.CurrentPage != WorkspacePage.Timing || CurrentKey() is not { } key || key == _heldKey)
            return Task.CompletedTask;
        return key == _readingKey ? _proposalRead : _proposalRead = ReadProposalSideAsync(key, cancellationToken);
    }

    private async Task ReadShownProposalSideAsync()
    {
        try { await EnsureProposalSideAsync(CancellationToken.None).ConfigureAwait(true); }
        catch (OperationCanceledException) { }
    }

    private async Task ReadProposalSideAsync(ProposalTimingKey key, CancellationToken cancellationToken)
    {
        var generation = ++_proposalGeneration;
        _readingKey = key;
        _failedKey = null;
        RaiseProposalTiming();
        try
        {
            if (await ReadSummaryAsync(key, cancellationToken).ConfigureAwait(true) is not { } read) return;
            var (summary, failed) = read;
            if (generation != _proposalGeneration || key != CurrentKey()) return;
            _proposalTiming = failed ? null : summary;
            _failedKey = failed ? key : null;
            _heldKey = failed ? null : key;
            RaiseProposalTiming();
        }
        finally
        {
            if (generation == _proposalGeneration) _readingKey = null;
        }
    }

    // A refusal or a thrown error is a failed read; a cancelled one is null and silent.
    private async Task<(ProposalTiming? Summary, bool Failed)?> ReadSummaryAsync(ProposalTimingKey key,
        CancellationToken cancellationToken)
    {
        try
        {
            var read = await Context.Commands.TimingAsync(new TimingRequest(key.Project, null, key.WordSet, "kind",
                Top: key.Top, ExplicitWords: key.ExplicitWords, ProposalId: key.ProposalId), cancellationToken)
                .ConfigureAwait(true);
            if (!read.Succeeded) return (null, true);
            if (read.Value is not { } response) return (null, false);
            var rules = await Context.Commands.TimingAsync(new TimingRequest(key.Project, null, key.WordSet, "rule",
                Top: key.Top, ExplicitWords: key.ExplicitWords, ProposalId: key.ProposalId), cancellationToken)
                .ConfigureAwait(true);
            var comparison = rules.Succeeded && rules.Value is { } value ? value.CostComparison
                : TimingCostComparison.Unavailable("Rule detail is unavailable.");
            return (new ProposalTiming(response.Work,
                response.Words.Count(word => word.Completion is TimingCompletion.WorkLimit or TimingCompletion.MemoryLimit),
                WordSetDigest.Of(response.Words.Select(word => word.Word)), response.CostComparison, comparison), false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
        catch (Exception ex) when (ex is not OperationCanceledException) { return (null, true); }
    }

    private void ClearProposalSide()
    {
        _proposalGeneration++;
        _heldKey = null;
        _readingKey = null;
        _proposalTiming = null;
        _failedKey = null;
        RaiseProposalTiming();
    }

    // The Proposal's work for the words both sides measured, as a whole percent of today's, or null.
    private int? WorkChangePercent => WordsChecked && WorkComparable && Held?.KindComparison.Total.PercentChange is { } change
        ? (int)Math.Round(change, MidpointRounding.AwayFromZero) : null;

    private void RaiseProposalTiming()
    {
        ProposalDelta = PageDelta.Of(WorkChangePercent, PageDeltaKind.WorkPercent);
        foreach (var name in new[]
        {
            nameof(HeadlineTotal), nameof(HeadlineTotalCaption), nameof(HeadlineMedian), nameof(HeadlineStopped),
            nameof(ShowHeadlineCard), nameof(ShowKindCard), nameof(ShowPageBody), nameof(ShowParsePromptCard), nameof(KindShares),
            nameof(ProposalNote), nameof(HasProposalNote), nameof(ProposalRuleChanges), nameof(HasProposalRuleChanges),
            nameof(ProposalRuleNote), nameof(HasProposalRuleNote),
            nameof(ShowBaselineWorkDetails), nameof(ShowBaselineRuleDetails), nameof(ShowBaselinePercentiles),
            nameof(ShowStatistics), nameof(HasKindCalls), nameof(ShowBaselineKindCalls),
            nameof(ShareDenominatorText), nameof(ScopeLabel),
        }) OnPropertyChanged(name);
    }
}

/// <summary>A rule's name and paired work change.</summary>
public sealed record TimingRuleChange(string Name, string Identity, string Change);
