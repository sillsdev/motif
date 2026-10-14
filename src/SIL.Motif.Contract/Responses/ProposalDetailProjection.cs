using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Responses;

/// <summary>One operation inside the <c>show</c> report, with every id rendered as its text form.</summary>
/// <param name="OperationId">The operation's identity within the Proposal.</param>
/// <param name="Kind">The semantic operation kind that names the requested change.</param>
/// <param name="Target">The target entity's portable identity, when the operation has a target.</param>
/// <param name="EntityId">The portable identity of an entity created by the operation, when present.</param>
/// <param name="DependsOn">The operation identities that this operation declares as prerequisites.</param>
/// <param name="AfterJson"><c>null</c> when the operation carries no after-state at all.</param>
/// <param name="Citation">The source the operation cites, or <c>null</c> when it cites none.</param>
public sealed record ProposalOperationView(
    string OperationId,
    string Kind,
    string? Target,
    string? EntityId,
    IReadOnlyList<string> DependsOn,
    string? AfterJson,
    CitationView? Citation = null);

/// <summary>The source an operation cites: a grammar section, a dictionary entry, a corpus passage.</summary>
/// <param name="Source">The work cited.</param>
/// <param name="Locator">Where in the work, such as a section or page; <c>null</c> when the whole work is cited.</param>
public sealed record CitationView(string Source, string? Locator)
{
    /// <summary>The view of an operation's citation, or <c>null</c> when it has none.</summary>
    public static CitationView? Of(SIL.Motif.Contract.Model.OperationEnvelope operation) =>
        SIL.Motif.Contract.Model.OperationCitation.FromProvenance(operation.Provenance) is { } citation
            ? new CitationView(citation.Source, citation.Locator)
            : null;

    /// <summary>The source followed by its locator, for one line of text.</summary>
    public override string ToString() => Locator is null ? Source : Source + ", " + Locator;
}

/// <summary>The <c>show</c> report: a Proposal's review state and its full operation list.</summary>
/// <param name="ProposalId">The Proposal's portable identity.</param>
/// <param name="Status">The Proposal's current lifecycle status.</param>
/// <param name="Label">The optional name shown for this Proposal.</param>
/// <param name="Comment">The optional explanatory comment attached to this Proposal.</param>
/// <param name="CurrentIntentDigest">
/// <c>null</c> when the Proposal has no committed revision. A reopened Draft keeps the digest of its prior revision.
/// </param>
/// <param name="Operations">The operation views included in the report.</param>
/// <param name="SupersededBy">The replacement Proposal's identity, or <c>null</c> when there is none.</param>
/// <param name="ExtensionsJson">Optional JSON containing non-semantic extension data.</param>
/// <param name="DraftName">The name of the Draft whose operations are shown, or <c>null</c> for a committed revision.</param>
/// <param name="DraftRevision">The SHA-256 revision of the exact stored Draft JSON bytes, or <c>null</c> for a committed revision.</param>
public sealed record ProposalDetailProjection(
    string ProposalId,
    string Status,
    string? Label,
    string? Comment,
    string? CurrentIntentDigest,
    IReadOnlyList<ProposalOperationView> Operations,
    string? SupersededBy = null,
    string? ExtensionsJson = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? DraftName = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? DraftRevision = null);
