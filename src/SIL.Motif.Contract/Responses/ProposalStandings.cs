namespace SIL.Motif.Contract.Responses;

/// <summary>Where a Proposal stands for the person deciding what to look at next.</summary>
public enum ProposalGroup
{
    /// <summary>A complete Trial matches the project and the Proposal as they are now.</summary>
    WaitingForYou,

    /// <summary>The Proposal, or the project, moved since the Trial, or the Trial is running or did not finish.</summary>
    NeedsAnotherTry,

    /// <summary>The Proposal has never been tried.</summary>
    NotTried,

    /// <summary>The person's own collected changes, which no FieldWorks save has applied yet.</summary>
    NotAppliedYet,

    /// <summary>Closed: applied, rejected, superseded or withdrawn.</summary>
    SetAside,
}

/// <summary>How many operations of one kind a Proposal carries, in the words a person reads.</summary>
/// <param name="Kind">The kind's stable key, such as <c>rule</c> or <c>parse-approved</c>.</param>
/// <param name="Count">How many changes of this kind the Proposal holds.</param>
/// <param name="Text">The count and its noun in the right number, such as "3 parses approved".</param>
public sealed record ChangeKindCount(string Kind, int Count, string Text);

/// <summary>The numbers a Proposal's latest Trial measured, each beside today's.</summary>
/// <param name="JobId">The Trial's durable job id.</param>
/// <param name="CompletedUtc">When the Trial finished, or null while it runs.</param>
/// <param name="Freshness">Whether the Trial still matches the project and the Proposal.</param>
/// <param name="WordScope">Which words the Trial parsed: the chosen words, the Default Selection or every word.</param>
/// <param name="TextCoveragePercent">The share of Selection words that parse.</param>
/// <param name="ApprovedKept">Approved words whose analysis the parser still builds.</param>
/// <param name="ApprovedTotal">Words with an approved analysis.</param>
/// <param name="WordTimeMs">Parse time spent on words, in milliseconds.</param>
/// <param name="WarningCount">Grammar findings at any level.</param>
/// <param name="LostApproved">Words whose approved analysis the Trial no longer builds.</param>
/// <param name="NoLongerFits">Changes that no longer fit the project.</param>
public sealed record TrialSummary(
    string JobId,
    DateTimeOffset? CompletedUtc,
    TrialFreshness Freshness,
    TrialWordScope WordScope,
    Paired<double?> TextCoveragePercent,
    Paired<int> ApprovedKept,
    Paired<int> ApprovedTotal,
    Paired<double?> WordTimeMs,
    Paired<int?> WarningCount,
    int LostApproved,
    int NoLongerFits)
{
    /// <summary>Parser work measured for this Trial against its Baseline, when available.</summary>
    public TrialWork? Work { get; init; }
}

/// <summary>One Proposal as the Proposals page and <c>motif proposals</c> list it.</summary>
/// <param name="ProposalId">The Proposal's canonical id.</param>
/// <param name="Label">What the Proposal is called.</param>
/// <param name="Status">The stored workflow status.</param>
/// <param name="Group">Where the Proposal stands.</param>
/// <param name="IsPendingDraft">Whether this is the person's own pending changes.</param>
/// <param name="ChangeKinds">What the Proposal changes, counted by kind.</param>
/// <param name="Standing">Whether the latest Trial still describes the Proposal; not tried when there is none.</param>
/// <param name="Trial">The latest Trial's numbers, or null when none was ever tried.</param>
/// <param name="AlsoChangedBy">The ids of other Proposals that change one of the same words.</param>
public sealed record ProposalCard(
    string ProposalId,
    string Label,
    string Status,
    ProposalGroup Group,
    bool IsPendingDraft,
    IReadOnlyList<ChangeKindCount> ChangeKinds,
    TrialStanding Standing,
    TrialSummary? Trial,
    IReadOnlyList<string> AlsoChangedBy)
{
    /// <summary>Whether the Proposal is a Draft that is not finalized yet.</summary>
    public bool IsDraft { get; init; }

    /// <summary>Whether the Proposal has a Rationale.</summary>
    public bool HasRationale { get; init; }

    /// <summary>Whether the person has not looked at the Rationale yet.</summary>
    public bool RationaleNew { get; init; }

    /// <summary>Whether the person left a note.</summary>
    public bool HasNote { get; init; }
}

/// <summary>Every Proposal in the project, grouped by what the person should do with it.</summary>
/// <param name="Proposals">The listed Proposals, grouped in display order.</param>
/// <param name="SetAsideCount">How many closed (applied, rejected, superseded, withdrawn) Proposals exist.</param>
public sealed record ProposalStandings(IReadOnlyList<ProposalCard> Proposals, int SetAsideCount);
