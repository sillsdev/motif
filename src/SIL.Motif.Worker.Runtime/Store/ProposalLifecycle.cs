namespace SIL.Motif.Worker.Store;

/// <summary>The statuses a finalized Proposal can hold.</summary>
public enum ProposalStatus
{
    Proposed,
    Deferred,
    Applied,
    Rejected,
    Withdrawn,
    Superseded,
}

/// <summary>The moves a Proposal can make between statuses, or out of the store.</summary>
public enum ProposalTransition
{
    Defer,
    Reject,
    Withdraw,
    Supersede,
    Reopen,
    Delete,
}

/// <summary>
/// The one place a Proposal's status names and the table of which status may move where are written
/// down. Statuses stay lowercase strings on the wire and in the store, because FieldWorks reads them
/// from the JSON of <c>motif apply</c>.
/// </summary>
public static class ProposalLifecycle
{
    /// <summary>The wire string of a status.</summary>
    public static string ToWire(ProposalStatus status) => status switch
    {
        ProposalStatus.Proposed => "proposed",
        ProposalStatus.Deferred => "deferred",
        ProposalStatus.Applied => "applied",
        ProposalStatus.Rejected => "rejected",
        ProposalStatus.Withdrawn => "withdrawn",
        ProposalStatus.Superseded => "superseded",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    /// <summary>The status a wire string names, or <c>null</c> when it names none (a Draft is not one).</summary>
    public static ProposalStatus? Parse(string? wire) => wire switch
    {
        "proposed" => ProposalStatus.Proposed,
        "deferred" => ProposalStatus.Deferred,
        "applied" => ProposalStatus.Applied,
        "rejected" => ProposalStatus.Rejected,
        "withdrawn" => ProposalStatus.Withdrawn,
        "superseded" => ProposalStatus.Superseded,
        _ => null,
    };

    /// <summary>
    /// The statuses a Proposal may leave through <paramref name="transition"/>, in the order a refusal
    /// lists them. Reopen admits Superseded so the command that reopens it can name the replacement
    /// instead of saying the Proposal is not closed.
    /// </summary>
    public static IReadOnlyList<ProposalStatus> AllowedFrom(ProposalTransition transition) => transition switch
    {
        ProposalTransition.Defer => [ProposalStatus.Proposed],
        ProposalTransition.Reject or ProposalTransition.Withdraw => [ProposalStatus.Proposed, ProposalStatus.Deferred],
        ProposalTransition.Supersede => [ProposalStatus.Proposed, ProposalStatus.Deferred, ProposalStatus.Rejected],
        ProposalTransition.Reopen => [ProposalStatus.Rejected, ProposalStatus.Withdrawn, ProposalStatus.Superseded],
        ProposalTransition.Delete => [ProposalStatus.Rejected, ProposalStatus.Superseded, ProposalStatus.Withdrawn],
        _ => throw new ArgumentOutOfRangeException(nameof(transition)),
    };

    /// <summary>The wire strings of <see cref="AllowedFrom"/>, as the guarded store write takes them.</summary>
    public static string[] AllowedFromWire(ProposalTransition transition) =>
        AllowedFrom(transition).Select(ToWire).ToArray();

    /// <summary>Whether <paramref name="transition"/> may move a Proposal that holds <paramref name="wire"/>.</summary>
    public static bool Allows(ProposalTransition transition, string? wire) =>
        Parse(wire) is { } status && AllowedFrom(transition).Contains(status);

    /// <summary>Whether a Proposal in this status has left the open set: nothing more happens to it unprompted.</summary>
    public static bool IsTerminal(string status) =>
        Parse(status) is ProposalStatus.Applied or ProposalStatus.Rejected or ProposalStatus.Superseded
            or ProposalStatus.Withdrawn;
}
