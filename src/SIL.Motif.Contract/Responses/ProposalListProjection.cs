using System.Collections.Generic;
using System.Linq;

namespace SIL.Motif.Contract.Responses;

/// <summary>One row of the <c>list</c> report.</summary>
public sealed record ProposalListItem(string ProposalId, string Status, string? Label);

/// <summary>The <c>list</c> report: every Proposal currently in a store.</summary>
public sealed record ProposalListProjection(IReadOnlyList<ProposalListItem> Proposals)
{
    /// <summary>Every Proposal the read matched, when <see cref="Proposals"/> is one page of them; null when it is all of them.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? Total { get; init; }

    /// <summary>How many Proposals precede this page; null when <see cref="Proposals"/> is not a page.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? Offset { get; init; }

    /// <summary>The offset that starts the next page; null at the end or when this is not a page.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? NextOffset { get; init; }

    /// <summary>The command that reads the next page with the caller's own flags; null at the end or when not set.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? NextStep { get; init; }

    /// <summary>This page with the command for the next one, built from its offset; unchanged at the end.</summary>
    public ProposalListProjection WithNextStep(System.Func<int, string> step) =>
        NextOffset is { } next ? this with { NextStep = step(next) } : this;

    /// <summary>One page of the Proposals, with the total and the offset that starts the next page.</summary>
    public ProposalListProjection Window(int offset, int limit)
    {
        var page = ResultPaging.Window(Proposals, offset, limit);
        return this with
        {
            Proposals = page, Total = Proposals.Count, Offset = offset,
            NextOffset = ResultPaging.NextOffset(offset, page.Count, Proposals.Count),
        };
    }
}
