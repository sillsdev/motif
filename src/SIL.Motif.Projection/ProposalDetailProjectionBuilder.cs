using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Projection.Store;
using System.Collections.Generic;
using System.Linq;

namespace SIL.Motif.Projection;

/// <summary>Shapes a manifest and its current object content into a <see cref="ProposalDetailProjection"/>.</summary>
public static class ProposalDetailProjectionBuilder
{
    /// <summary>The review-mark entry key under which a person's "looked at" mark of the Rationale is kept.</summary>
    public const string RationaleEntryKey = "proposal.rationale";

    /// <summary>The digest a review mark of the Rationale carries: SHA-256 of its UTF-8 text.</summary>
    public static string RationaleContentDigest(string rationale) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rationale))).ToLowerInvariant();

    /// <summary>Builds the report for one Proposal.</summary>
    /// <param name="proposalId">The Proposal's id.</param>
    /// <param name="manifest">The stored row, including both texts.</param>
    /// <param name="envelope">The Proposal's current content.</param>
    /// <param name="rationaleLookedAt">Whether the person's mark matches the current Rationale text.</param>
    /// <param name="revisionOf">Finds the stored content of an earlier revision by its intent digest.</param>
    public static ProposalDetailProjection Build(string proposalId, ManifestDocument manifest, Proposal envelope,
        bool rationaleLookedAt = false, Func<string, Proposal?>? revisionOf = null)
    {
        var operations = envelope.Operations.Select(op => new ProposalOperationView(
            OperationId: op.OperationId.Value,
            Kind: op.Kind,
            Target: op.Target?.Value,
            EntityId: op.EntityId?.Value,
            DependsOn: op.DependsOn.Select(d => d.OperationId.Value).ToList(),
            AfterJson: op.After?.GetRawText(),
            Citation: CitationView.Of(op))).ToList();

        var current = IntentDigest.Compute(envelope);
        RationaleView? rationale = manifest.Rationale is null ? null : new RationaleView(
            manifest.Rationale, manifest.RationaleIntentDigest, manifest.RationaleUpdatedUtc ?? "",
            IsOutOfDate(manifest.RationaleIntentDigest, current),
            ChangesSince(manifest.RationaleIntentDigest, current, envelope, revisionOf), !rationaleLookedAt);
        var comments = manifest.Comments.Select(comment => new CommentView(
            comment.CommentId, comment.Kind, comment.AuthorLabel, comment.PersonName, comment.Text,
            comment.IntentDigest, comment.CreatedUtc, comment.EditedUtc,
            IsOutOfDate(comment.IntentDigest, current),
            ChangesSince(comment.IntentDigest, current, envelope, revisionOf))).ToList();

        return new ProposalDetailProjection(
            proposalId, manifest.Status, manifest.Label, rationale, manifest.CurrentIntentDigest, operations,
            manifest.SupersededBy, envelope.Extensions?.GetRawText(), Comments: comments,
            NoteChanged: PersonCommentIsNewer(manifest));
    }

    /// <summary>
    /// Whether a person wrote a Comment after the builder last wrote, that is after the Rationale or after an agent's
    /// Comment. It needs no reader identity: the builder's next write clears it, and a person's Comment over no
    /// Rationale at all counts as newer.
    /// </summary>
    public static bool PersonCommentIsNewer(ManifestDocument manifest)
    {
        var person = manifest.Comments.Where(comment => comment.AuthorLabel is null)
            .Select(comment => Parse(comment.EditedUtc ?? comment.CreatedUtc)).Where(time => time is not null).ToList();
        if (person.Count == 0) return false;
        if (manifest.Rationale is null) return true;
        var builder = manifest.Comments.Where(comment => comment.AuthorLabel is not null)
            .Select(comment => Parse(comment.CreatedUtc)).Append(Parse(manifest.RationaleUpdatedUtc))
            .Where(time => time is not null).ToList();
        return builder.Count == 0 || person.Max() > builder.Max();
    }

    private static DateTimeOffset? Parse(string? utc) =>
        DateTimeOffset.TryParse(utc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time) ? time : null;

    private static bool IsOutOfDate(string? writtenFor, string current) =>
        writtenFor is not null && !string.Equals(writtenFor, current, StringComparison.Ordinal);

    private static ProposalChangesSince? ChangesSince(string? writtenFor, string current, Proposal envelope,
        Func<string, Proposal?>? revisionOf)
    {
        if (!IsOutOfDate(writtenFor, current) || revisionOf?.Invoke(writtenFor!) is not { } older) return null;
        var before = older.Operations.ToDictionary(op => op.OperationId.Value, ContentOf, StringComparer.Ordinal);
        var after = envelope.Operations.ToDictionary(op => op.OperationId.Value, ContentOf, StringComparer.Ordinal);
        return new ProposalChangesSince(
            after.Keys.Count(id => !before.ContainsKey(id)),
            before.Keys.Count(id => !after.ContainsKey(id)),
            after.Count(pair => before.TryGetValue(pair.Key, out var was) && was != pair.Value));
    }

    // What an operation does, so an edit in place counts as changed rather than as nothing.
    private static string ContentOf(OperationEnvelope op) => string.Join("\u001f", op.Kind, op.Target?.Value,
        op.EntityId?.Value, op.After?.GetRawText(), string.Join(",", op.DependsOn.Select(d => d.OperationId.Value)));
}
