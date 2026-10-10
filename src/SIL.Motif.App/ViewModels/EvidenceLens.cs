using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>The numbers a page displays from project evidence and a Proposal Trial.</summary>
public enum MarkupView
{
    /// <summary>Show the project's current values.</summary>
    Today,

    /// <summary>Show the project's values beside the Trial values.</summary>
    TodayToProposal,

    /// <summary>Show the Trial values, falling back to today's values when no Trial exists.</summary>
    Proposal,
}

/// <summary>The Proposal currently open in the window and the Trial that belongs to it.</summary>
public sealed record ProposalLens(
    string ProposalId,
    string Label,
    string IntentDigest,
    TrialRecord? Trial,
    MarkupView View)
{
    /// <summary>The last Trial read refusal, which explains why its values are unavailable.</summary>
    public Refusal? ReadRefusal { get; init; }
}

/// <summary>A Proposal's named evidence and the words measured by its latest Trial.</summary>
public sealed record TrialEvidence(
    EvidenceSet Set,
    TrialRecord Trial,
    IReadOnlyList<AssessmentWordResult> Words);

/// <summary>What a page reads in today's project and, when available, the open Proposal's Trial.</summary>
public sealed class EvidenceLens
{
    private readonly bool _inProposalMode;

    /// <summary>Creates a lens for today's project evidence and an optional Trial.</summary>
    public EvidenceLens(ProjectEvidence today, TrialEvidence? proposal, MarkupView view,
        bool inProposalMode = false)
    {
        Today = today ?? throw new ArgumentNullException(nameof(today));
        Proposal = proposal;
        View = view;
        _inProposalMode = inProposalMode || proposal is not null;
    }

    /// <summary>The project evidence shown when no Proposal is open.</summary>
    public ProjectEvidence Today { get; }

    /// <summary>The open Proposal's latest Trial, or null when there is no Trial.</summary>
    public TrialEvidence? Proposal { get; }

    /// <summary>How a page should present a paired value.</summary>
    public MarkupView View { get; }

    /// <summary>Whether the window has a Proposal open, including before its first Trial.</summary>
    public bool InProposalMode => _inProposalMode;

    /// <summary>The named sets a page can read, omitting evidence that has not been measured.</summary>
    public IReadOnlyList<EvidenceSet> Sets
    {
        get
        {
            var sets = new List<EvidenceSet>(2);
            if (Today.Set is { } today) sets.Add(today);
            if (Proposal is { } proposal) sets.Add(proposal.Set);
            return sets;
        }
    }

    /// <summary>Reads today's and the Proposal's value once per available set.</summary>
    public Paired<T> Pair<T>(Func<EvidenceSet, T?> read, Func<T?, T?, bool> moved)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(moved);
        var todaySet = Today.Set;
        var before = todaySet is not null ? read(todaySet) : default;
        var hasBefore = todaySet is not null;
        var afterEvidence = Proposal;
        var after = afterEvidence is not null ? read(afterEvidence.Set) : default;
        var hasAfter = afterEvidence is not null;
        return new Paired<T>(before, after, hasBefore, hasAfter,
            hasBefore && hasAfter && moved(before, after), null);
    }

    /// <summary>Reads both evidence sets asynchronously and reports meaningful movement with a domain comparer.</summary>
    public async Task<Paired<T>> PairAsync<T>(
        Func<EvidenceSet, CancellationToken, Task<CommandOutcome<T>>> read,
        CancellationToken cancellationToken,
        Func<T?, T?, bool> moved) where T : class?
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(moved);
        var before = Today.Set is { } todaySet
            ? await Read(todaySet).ConfigureAwait(true)
            : (Value: default(T), HasValue: false, Refusal: (Refusal?)null);
        var after = Proposal is { } afterEvidence
            ? await Read(afterEvidence.Set).ConfigureAwait(true)
            : (Value: default(T), HasValue: false, Refusal: (Refusal?)null);
        return new Paired<T>(before.Value, after.Value, before.HasValue, after.HasValue,
            before.HasValue && after.HasValue && moved(before.Value, after.Value), after.Refusal);

        async Task<(T? Value, bool HasValue, Refusal? Refusal)> Read(EvidenceSet set)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var outcome = await read(set, cancellationToken).ConfigureAwait(true);
            var hasValue = outcome.Succeeded && outcome.Value is not null;
            return (hasValue ? outcome.Value : default, hasValue, outcome.Refusal);
        }
    }
}

/// <summary>Chooses a paired value for the selected presentation.</summary>
public static class PairedMarkupExtensions
{
    /// <summary>Returns today's value, the Trial value, or today's side of a side-by-side presentation.</summary>
    public static T? Shown<T>(this Paired<T> pair, MarkupView view) => view switch
    {
        MarkupView.Today => pair.Before,
        MarkupView.TodayToProposal => pair.Before,
        MarkupView.Proposal => pair.HasAfter ? pair.After : pair.Before,
        _ => throw new ArgumentOutOfRangeException(nameof(view), view, "Unknown markup view."),
    };
}
