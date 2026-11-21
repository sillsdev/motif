using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using SIL.Motif.Contract.Model;

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
    CitationView? Citation = null)
{
    /// <summary>The intended sequence position, or null for an operation with no relative anchor.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public OperationPlacementView? Placement { get; init; }

    /// <summary>The authored storage identity when it differs from the default realization.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? StorageIdOverride { get; init; }

    /// <summary>
    /// The project's current value of each field this operation changes, read from the saved project with no Trial.
    /// Present only when the read asked for current values and the operation's field could be read; null otherwise.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CurrentFieldValue>? Current { get; init; }

    /// <summary>Projects the operation's authored intent into the shared response.</summary>
    public static ProposalOperationView From(OperationEnvelope operation) =>
        new(operation.OperationId.Value, operation.Kind, operation.Target?.Value, operation.EntityId?.Value,
            operation.DependsOn.Select(dependency => dependency.OperationId.Value).ToArray(),
            operation.After?.GetRawText(), CitationView.Of(operation))
        {
            Placement = operation.Placement is { } placement
                ? new OperationPlacementView(placement.After?.Value, placement.Before?.Value) : null,
            StorageIdOverride = operation.StorageIdOverride?.Value,
        };
}

/// <summary>One field an operation changes and the value the project holds for it now.</summary>
/// <param name="Target">The portable identity of the entity that holds the field.</param>
/// <param name="Field">The canonical field name, such as <c>lexical/sense/gloss</c>.</param>
/// <param name="Value">The current value as alternatives keyed by writing-system tag; empty when the field holds nothing.</param>
public sealed record CurrentFieldValue(string Target, string Field, IReadOnlyDictionary<string, string> Value);

/// <summary>The portable identities of the preceding and following neighbors of an operation.</summary>
public sealed record OperationPlacementView(string? After, string? Before);

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

/// <summary>How many operations were added and removed between two revisions of a Proposal.</summary>
/// <param name="Added">Operations present now that the older revision lacks.</param>
/// <param name="Removed">Operations the older revision had that are gone now.</param>
/// <param name="Changed">Operations in both revisions, by id, whose content differs.</param>
public sealed record ProposalChangesSince(int Added, int Removed, int Changed);

/// <summary>
/// The builder's explanation of a Proposal: what it changes and why, in Markdown. Whoever builds the Proposal,
/// usually an agent, writes it.
/// </summary>
/// <param name="Markdown">The text.</param>
/// <param name="WrittenForIntentDigest">The Proposal content the text was written for; null before a Draft's first finalize.</param>
/// <param name="UpdatedUtc">When the text was last written.</param>
/// <param name="OutOfDate">Whether the Proposal's content changed after the text was written.</param>
/// <param name="ChangesSince">How the Proposal changed since the text was written; null when it did not.</param>
/// <param name="New">Whether the person has not looked at this text yet.</param>
public sealed record RationaleView(
    string Markdown, string? WrittenForIntentDigest, string UpdatedUtc, bool OutOfDate,
    ProposalChangesSince? ChangesSince, bool New);

/// <summary>One Comment on a Proposal, in Markdown.</summary>
/// <param name="CommentId">The Comment's number, which an edit or a delete names.</param>
/// <param name="Kind"><c>standard</c>, or <c>system</c> for a Comment Motif wrote.</param>
/// <param name="AuthorLabel">What an agent said of itself, as typed; null when the person wrote it. A label for readers only.</param>
/// <param name="PersonName">The person the Comment was written by or for; empty when unknown.</param>
/// <param name="Markdown">The text.</param>
/// <param name="WrittenForIntentDigest">The Proposal content the Comment was last written for.</param>
/// <param name="CreatedUtc">When it was added.</param>
/// <param name="EditedUtc">When a person last edited it; null when it was never edited.</param>
/// <param name="OutOfDate">Whether the Proposal's content changed after the Comment was written.</param>
/// <param name="ChangesSince">How the Proposal changed since the Comment was written; null when it did not.</param>
public sealed record CommentView(
    long CommentId, string Kind, string? AuthorLabel, string PersonName, string Markdown, string? WrittenForIntentDigest,
    string CreatedUtc, string? EditedUtc, bool OutOfDate, ProposalChangesSince? ChangesSince)
{
    /// <summary>
    /// Findings from checking the Comment against the current project. A missing value means it was not checked;
    /// an empty list means it was checked and clean; a non-empty list contains the findings.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<TextFinding>? Findings { get; init; }
}

/// <summary>The <c>show</c> report: a Proposal's review state and its full operation list.</summary>
/// <param name="ProposalId">The Proposal's portable identity.</param>
/// <param name="Status">The Proposal's current lifecycle status.</param>
/// <param name="Label">The optional name shown for this Proposal.</param>
/// <param name="Rationale">What the Proposal changes and why, or <c>null</c> before one is written.</param>
/// <param name="CurrentIntentDigest">
/// <c>null</c> when the Proposal has no committed revision. A reopened Draft keeps the digest of its prior revision.
/// </param>
/// <param name="Operations">The operation views included in the report.</param>
/// <param name="SupersededBy">The replacement Proposal's identity, or <c>null</c> when there is none.</param>
/// <param name="ExtensionsJson">Optional JSON containing non-semantic extension data.</param>
/// <param name="DraftName">The name of the Draft whose operations are shown, or <c>null</c> for a committed revision.</param>
/// <param name="DraftRevision">The SHA-256 revision of the exact stored Draft JSON bytes, or <c>null</c> for a committed revision.</param>
/// <param name="Comments">The Proposal's Comments, oldest first.</param>
/// <param name="CommentsChanged">Whether the person wrote a Comment after the builder last wrote the Rationale or a Comment.</param>
/// <param name="CurrentUnavailable">Why current values were asked for and not read, such as no saved project; null otherwise.</param>
/// <param name="AppliedRecord">What Apply recorded, for an Applied Proposal that has a Receipt; null otherwise.</param>
public sealed record ProposalDetailProjection(
    string ProposalId,
    string Status,
    string? Label,
    RationaleView? Rationale,
    string? CurrentIntentDigest,
    IReadOnlyList<ProposalOperationView> Operations,
    string? SupersededBy = null,
    string? ExtensionsJson = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? DraftName = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? DraftRevision = null,
    IReadOnlyList<CommentView>? Comments = null,
    bool CommentsChanged = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? CurrentUnavailable = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    AppliedRecordView? AppliedRecord = null)
{
    /// <summary>Every operation the Proposal holds, when <see cref="Operations"/> is one page of them; null when it is all of them.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? OperationTotal { get; init; }

    /// <summary>How many operations precede this page; null when <see cref="Operations"/> is not a page.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? OperationOffset { get; init; }

    /// <summary>The offset that starts the next page of operations; null at the end or when this is not a page.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? NextOffset { get; init; }

    /// <summary>The command that reads the next page with the caller's own flags; null at the end or when not set.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NextStep { get; init; }

    /// <summary>This page with the command for the next one, built from its offset; unchanged at the end.</summary>
    public ProposalDetailProjection WithNextStep(System.Func<int, string> step) =>
        NextOffset is { } next ? this with { NextStep = step(next) } : this;

    /// <summary>One page of the operations, with the total and the offset that starts the next page.</summary>
    public ProposalDetailProjection Window(int offset, int limit)
    {
        var page = ResultPaging.Window(Operations, offset, limit);
        return this with
        {
            Operations = page, OperationTotal = Operations.Count, OperationOffset = offset,
            NextOffset = ResultPaging.NextOffset(offset, page.Count, Operations.Count),
        };
    }
}
