using System.Text.Json;
using SIL.Motif.Model.DryRun;
using SIL.Motif.Projection.Store;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands.Store;

/// <summary>
/// Builds the CLI's rendering shapes (<see cref="ManifestDocument"/>) from a repository row, so the
/// projection builders and <see cref="Commands"/> stay written against the same shape regardless of
/// where a Proposal's bytes are persisted.
/// </summary>
public static class ProposalRecordMapping
{
    private static readonly JsonSerializerOptions AnchorJsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Projects one repository row — committed or Draft — into its manifest rendering shape; the Comments are only
    /// those the caller passes, since most callers have no use for them.
    /// </summary>
    public static ManifestDocument ToManifest(ProposalRecord record, IReadOnlyList<ProposalCommentRecord>? comments = null) => new()
    {
        ProposalId = record.ProposalId.Value,
        Status = record.Status,
        Label = record.Label,
        Rationale = record.Rationale,
        RationaleIntentDigest = record.RationaleIntentDigest,
        RationaleUpdatedUtc = record.RationaleUpdatedUtc,
        Comments = comments?.Select(comment => new ManifestComment(comment.CommentId, comment.Kind,
            comment.Author.Label, comment.Author.PersonName, comment.Text, comment.IntentDigest, comment.CreatedUtc,
            comment.EditedUtc)).ToList() ?? [],
        CurrentIntentDigest = record.IntentDigest,
        SupersededBy = record.SupersededBy,
        Anchor = record.AnchorJson is null
            ? null
            : JsonSerializer.Deserialize<BoundDryRunAnchor>(record.AnchorJson, AnchorJsonOptions),
    };
}
