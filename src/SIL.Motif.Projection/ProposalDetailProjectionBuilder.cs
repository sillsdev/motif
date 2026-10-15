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
        NoteView? note = manifest.Note is null ? null : new NoteView(
            manifest.Note, manifest.NoteIntentDigest, manifest.NoteUpdatedUtc ?? "",
            IsOutOfDate(manifest.NoteIntentDigest, current),
            ChangesSince(manifest.NoteIntentDigest, current, envelope, revisionOf));

        return new ProposalDetailProjection(
            proposalId, manifest.Status, manifest.Label, rationale, manifest.CurrentIntentDigest, operations,
            manifest.SupersededBy, envelope.Extensions?.GetRawText(), Note: note,
            NoteChanged: NoteIsNewer(manifest));
    }

    /// <summary>
    /// Whether the person wrote the note after the Rationale was last written. It needs no reader identity: a
    /// later builder write clears it, and a note over no Rationale at all counts as newer.
    /// </summary>
    public static bool NoteIsNewer(ManifestDocument manifest) =>
        manifest.Note is not null && (manifest.Rationale is null || Later(manifest.NoteUpdatedUtc, manifest.RationaleUpdatedUtc));

    private static bool Later(string? note, string? rationale) =>
        DateTimeOffset.TryParse(note, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var written) &&
        (!DateTimeOffset.TryParse(rationale, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var built) ||
            written > built);

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
