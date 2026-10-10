using System.Text.Json.Serialization;
using SIL.Motif.Contract.Jobs;

namespace SIL.Motif.Contract.Responses;

/// <summary>What a person can do next with a Proposal, judged from its chosen Trial.</summary>
public enum TrialStandingKind
{
    /// <summary>The Proposal has no Trial.</summary>
    NotTried,
    /// <summary>A Trial is queued or running.</summary>
    Trying,
    /// <summary>The chosen Trial stopped without a complete result, and no earlier Trial completed.</summary>
    Failed,
    /// <summary>The chosen Trial completed against today's project and the Proposal's current content.</summary>
    Current,
    /// <summary>The chosen Trial completed, but the project or the Proposal changed since.</summary>
    NeedsAnotherTry,
}

/// <summary>Why a completed Trial no longer describes the Proposal against today's project.</summary>
public enum TrialStaleReason
{
    /// <summary>The project published a different Baseline after the Trial.</summary>
    BaselineMoved,
    /// <summary>The Proposal content changed after the Trial.</summary>
    ProposalChanged,
}

/// <summary>
/// The one classification of a Proposal's Trial that every reader shares: the Proposals page groups on it, the
/// window's context bar and cards word it, and Apply blocks on it.
/// </summary>
/// <remarks>
/// It is derived from the chosen Trial's job status and freshness and nothing else, so a record built anywhere,
/// including a test double, reports the same standing. Which Trial is chosen is the reader's rule: the newest
/// completed or active Trial, else the newest one that stopped. A retry that failed after an earlier Trial
/// completed therefore leaves the earlier Trial chosen, and the standing describes that one.
/// </remarks>
/// <param name="Kind">What the person can do next.</param>
/// <param name="Reason">Why the Trial needs another try; set only for <see cref="TrialStandingKind.NeedsAnotherTry"/>.</param>
public sealed record TrialStanding(
    TrialStandingKind Kind,
    TrialStaleReason? Reason = null)
{
    /// <summary>The standing of a Proposal that has no Trial.</summary>
    public static TrialStanding NotTried { get; } = new(TrialStandingKind.NotTried);

    /// <summary>The standing of a Proposal whose Trial is queued or running.</summary>
    public static TrialStanding Trying { get; } = new(TrialStandingKind.Trying);

    /// <summary>The standing of a Proposal whose only Trials stopped without a complete result.</summary>
    public static TrialStanding Failed { get; } = new(TrialStandingKind.Failed);

    /// <summary>The standing of a Proposal whose Trial still describes it against today's project.</summary>
    public static TrialStanding Current { get; } = new(TrialStandingKind.Current);

    /// <summary>Whether the person may queue a new Trial now; only a running Trial prevents it.</summary>
    [JsonIgnore]
    public bool CanTryAgain => Kind != TrialStandingKind.Trying;

    /// <summary>The standing of a Proposal whose completed Trial is out of date for <paramref name="reason"/>.</summary>
    public static TrialStanding NeedsAnotherTry(TrialStaleReason reason) =>
        new(TrialStandingKind.NeedsAnotherTry, reason);

    /// <summary>The standing of a Proposal whose chosen Trial is <paramref name="trial"/>, or none.</summary>
    public static TrialStanding Of(TrialRecord? trial) => trial?.Standing ?? NotTried;

    /// <summary>Classifies one Trial from its durable job status and its freshness.</summary>
    /// <param name="status">The Trial job's status.</param>
    /// <param name="freshness">How the Trial's evidence relates to the project and the Proposal now.</param>
    public static TrialStanding From(JobStatus status, TrialFreshness freshness)
    {
        if (status is JobStatus.Queued or JobStatus.WaitingForBaseline or JobStatus.WaitingForProjectHost or
            JobStatus.Running)
            return Trying;
        return freshness switch
        {
            TrialFreshness.Current when status == JobStatus.Completed => Current,
            TrialFreshness.BaselineMoved when status == JobStatus.Completed =>
                NeedsAnotherTry(TrialStaleReason.BaselineMoved),
            TrialFreshness.ProposalChanged when status == JobStatus.Completed =>
                NeedsAnotherTry(TrialStaleReason.ProposalChanged),
            TrialFreshness.None => NotTried,
            _ => Failed,
        };
    }
}
