using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>The window's words for one <see cref="TrialStanding"/>.</summary>
/// <param name="Status">A short label for a heading, chip or group, such as "Needs another try".</param>
/// <param name="Sentence">The sentence the context bar and a card show, such as "Trying…".</param>
/// <param name="Action">The label of the button that tries the Proposal: "Try it" before any Trial.</param>
public sealed record TrialStandingText(string Status, string Sentence, string Action)
{
    /// <summary>What to say beside numbers that came from an earlier try, or null when they are not old.</summary>
    public string? OldNumbers { get; init; }
}

/// <summary>
/// The one table of what the window says about a Proposal's Trial, as <see cref="WindowRefusal"/> is for refusals.
/// The bar, the Proposals page, the Review blocker and the word card all read it, so a new standing is one enum
/// value in Contract and one row here. None of it names a Trial, a Draft or a Baseline (ADR 0046 decision 3).
/// </summary>
public static class TrialStandingWords
{
    /// <summary>What the window says for a Proposal with no Trial.</summary>
    public static TrialStandingText NotTried { get; } = new(MotifAppStrings.TrialNotTriedStatus,
        MotifAppStrings.TrialNotTriedSentence, MotifAppStrings.TryIt);

    /// <summary>What the window says while a Trial runs.</summary>
    public static TrialStandingText Trying { get; } = new(MotifAppStrings.TrialTrying, MotifAppStrings.TrialTrying, MotifAppStrings.TryAgain);

    /// <summary>What the window says when the only Trials stopped without a result.</summary>
    public static TrialStandingText Failed { get; } = new(MotifAppStrings.TrialDidNotFinishStatus,
        MotifAppStrings.TrialDidNotFinishSentence, MotifAppStrings.TryAgain);

    /// <summary>What the window says when the Trial still describes the Proposal.</summary>
    public static TrialStandingText Current { get; } = new(MotifAppStrings.TrialCurrentStatus, MotifAppStrings.TrialCurrentStatus, MotifAppStrings.TryAgain);

    /// <summary>What the window says when FieldWorks changed since the Trial.</summary>
    public static TrialStandingText BaselineMoved { get; } = new(MotifAppStrings.TrialNeedsAnotherTryStatus,
        MotifAppStrings.TrialBaselineMovedSentence, MotifAppStrings.TryAgain)
    {
        OldNumbers = MotifAppStrings.TrialBaselineMovedOldNumbers,
    };

    /// <summary>What the window says when the Proposal changed since the Trial.</summary>
    public static TrialStandingText ProposalChanged { get; } = new(MotifAppStrings.TrialNeedsAnotherTryStatus,
        MotifAppStrings.TrialProposalChangedSentence, MotifAppStrings.TryAgain)
    {
        OldNumbers = MotifAppStrings.TrialProposalChangedOldNumbers,
    };

    /// <summary>The window's words for <paramref name="standing"/>.</summary>
    public static TrialStandingText For(TrialStanding standing)
    {
        ArgumentNullException.ThrowIfNull(standing);
        return standing.Kind switch
        {
            TrialStandingKind.Trying => Trying,
            TrialStandingKind.Failed => Failed,
            TrialStandingKind.Current => Current,
            TrialStandingKind.NeedsAnotherTry => standing.Reason == TrialStaleReason.BaselineMoved
                ? BaselineMoved
                : ProposalChanged,
            _ => NotTried,
        };
    }
}
