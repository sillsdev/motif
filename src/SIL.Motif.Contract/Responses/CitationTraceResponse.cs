using System.Collections.Generic;

namespace SIL.Motif.Contract.Responses;

/// <summary>
/// The <c>citations</c> report: every operation of every Applied Proposal in the project, with the source it cites,
/// so a grammar built from published descriptions can show where each change came from.
/// </summary>
/// <param name="AppliedProposals">How many Applied Proposals the report read.</param>
/// <param name="Operations">How many operations those Proposals hold.</param>
/// <param name="Cited">How many of those operations cite a source.</param>
/// <param name="Rows">One row per operation, or per uncited operation when only those were asked for.</param>
public sealed record CitationTraceResponse(
    int AppliedProposals,
    int Operations,
    int Cited,
    IReadOnlyList<CitedOperationRow> Rows);

/// <summary>One Applied operation and the source it cites.</summary>
/// <param name="ProposalId">The Applied Proposal holding the operation.</param>
/// <param name="Label">The Proposal's short description, when it has one.</param>
/// <param name="OperationId">The operation's identity within the Proposal.</param>
/// <param name="Kind">The operation kind.</param>
/// <param name="Target">The existing entity the operation changed, when it has a target.</param>
/// <param name="EntityId">The entity the operation created, when it created one.</param>
/// <param name="Citation">The source the operation cites, or <c>null</c> when it cites none.</param>
public sealed record CitedOperationRow(
    string ProposalId,
    string? Label,
    string OperationId,
    string Kind,
    string? Target,
    string? EntityId,
    CitationView? Citation);
