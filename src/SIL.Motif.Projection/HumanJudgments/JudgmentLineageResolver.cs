using System.Security.Cryptography;
using System.Text;
using SIL.Motif.Contract.HumanJudgments;

namespace SIL.Motif.Projection.HumanJudgments;

/// <summary>One captured Notebook revision and its resolved place in the revision graph.</summary>
public sealed record HumanJudgmentRevisionProjection(
    string RecordId,
    string JudgmentId,
    string RevisionId,
    string ContentDigest,
    string Format,
    int Version,
    string BodyKind,
    string PhysicalValue,
    HumanJudgment Judgment,
    string State);

/// <summary>One current logical judgment head or a set of unresolved competing heads.</summary>
public sealed record HumanJudgmentHeadProjection(
    string JudgmentId,
    string RevisionId,
    string RecordId,
    string State,
    string? Issue);

/// <summary>A malformed value or lineage group retained as visible, non-effective input.</summary>
public sealed record HumanJudgmentUnavailableProjection(
    string RecordId,
    string? JudgmentId,
    string Reason,
    string PhysicalDigest);

/// <summary>The deterministic judgment input captured from one already-loaded project.</summary>
public sealed record HumanJudgmentLineageProjection(
    string ProjectId,
    string Capability,
    string CapabilityMessage,
    string SnapshotDigest,
    string Digest,
    IReadOnlyList<HumanJudgmentRevisionProjection> Revisions,
    IReadOnlyList<HumanJudgmentHeadProjection> Heads,
    IReadOnlyList<HumanJudgmentUnavailableProjection> Unavailable);

/// <summary>Resolves captured Notebook records without choosing a revision by timestamp or row order.</summary>
public static class JudgmentLineageResolver
{
    /// <summary>Validates predecessor identities and classifies effective, competing, and unusable heads.</summary>
    public static HumanJudgmentLineageProjection Resolve(HumanJudgmentProjectSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var revisions = new List<HumanJudgmentRevisionProjection>();
        var heads = new List<HumanJudgmentHeadProjection>();
        var unavailable = snapshot.Unavailable.Select(item => new HumanJudgmentUnavailableProjection(
            item.RecordId, null, item.Message, Hash(item.PhysicalValue.Normalize(NormalizationForm.FormD)))).ToList();

        foreach (var group in snapshot.Judgments.GroupBy(item => item.Judgment.JudgmentId, StringComparer.Ordinal)
                     .OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var items = group.OrderBy(item => item.RecordId, StringComparer.Ordinal).ToArray();
            var issue = ValidateGroup(items);
            var parents = items.ToDictionary(item => item.Judgment.RevisionId, StringComparer.Ordinal);
            if (issue is null)
                issue = ValidateLineage(items, parents);

            if (issue is not null)
            {
                foreach (var item in items)
                {
                    revisions.Add(ToRevision(item, "unavailable"));
                    unavailable.Add(new(item.RecordId, group.Key, issue,
                        Hash(item.PhysicalValue.Normalize(NormalizationForm.FormD))));
                    heads.Add(new(group.Key, item.Judgment.RevisionId, item.RecordId, "unavailable", issue));
                }
                continue;
            }

            var replaced = items.SelectMany(item => item.Judgment.Replaces)
                .Select(item => item.RevisionId).ToHashSet(StringComparer.Ordinal);
            var current = items.Where(item => !replaced.Contains(item.Judgment.RevisionId))
                .OrderBy(item => item.Judgment.RevisionId, StringComparer.Ordinal).ToArray();
            var state = current.Length == 1 ? "effective" : "conflict";
            foreach (var item in items)
                revisions.Add(ToRevision(item, current.Contains(item) ? "head" : "superseded"));
            foreach (var item in current)
                heads.Add(new(group.Key, item.Judgment.RevisionId, item.RecordId, state,
                    current.Length == 1 ? null : "Multiple un-retracted revisions share this logical judgment."));
        }

        var orderedRevisions = revisions.OrderBy(item => item.JudgmentId, StringComparer.Ordinal)
            .ThenBy(item => item.RevisionId, StringComparer.Ordinal).ToArray();
        var orderedHeads = heads.OrderBy(item => item.JudgmentId, StringComparer.Ordinal)
            .ThenBy(item => item.RevisionId, StringComparer.Ordinal).ToArray();
        var orderedUnavailable = unavailable.OrderBy(item => item.RecordId, StringComparer.Ordinal)
            .ThenBy(item => item.Reason, StringComparer.Ordinal).ToArray();
        var digestText = new StringBuilder()
            .Append("human-judgment-lineage-v1\0")
            .Append(snapshot.ProjectId).Append('\0')
            .Append(snapshot.Capability).Append('\0')
            .Append(snapshot.LogicalDigest).Append('\n');
        foreach (var item in orderedRevisions)
            digestText.Append(item.RecordId).Append('\0').Append(item.ContentDigest).Append('\0')
                .Append(item.State).Append('\n');
        foreach (var item in orderedHeads)
            digestText.Append(item.JudgmentId).Append('\0').Append(item.RevisionId).Append('\0')
                .Append(item.State).Append('\0').Append(item.Issue).Append('\n');
        foreach (var item in orderedUnavailable)
            digestText.Append(item.RecordId).Append('\0').Append(item.PhysicalDigest).Append('\0')
                .Append(item.Reason).Append('\n');

        return new(snapshot.ProjectId, snapshot.Capability.ToString(), snapshot.Message, snapshot.LogicalDigest,
            Hash(digestText.ToString()), Array.AsReadOnly(orderedRevisions), Array.AsReadOnly(orderedHeads),
            Array.AsReadOnly(orderedUnavailable));
    }

    private static string? ValidateGroup(IReadOnlyList<StoredHumanJudgment> items)
    {
        if (items.Select(item => item.Judgment.RevisionId).Distinct(StringComparer.Ordinal).Count() != items.Count)
            return "Duplicate revision identity occurs in this logical judgment.";

        var dispositions = items.Select(item => item.Judgment.Body).OfType<DispositionJudgment>().ToArray();
        if (dispositions.Length > 1)
        {
            var first = FindingIdentity(dispositions[0]);
            if (dispositions.Skip(1).Any(item => FindingIdentity(item) != first))
                return "Revisions under one logical judgment change the exact finding identity.";
        }

        return null;
    }

    private static string? ValidateLineage(IReadOnlyList<StoredHumanJudgment> items,
        IReadOnlyDictionary<string, StoredHumanJudgment> revisions)
    {
        foreach (var item in items)
        foreach (var predecessor in item.Judgment.Replaces)
        {
            if (!revisions.TryGetValue(predecessor.RevisionId, out var parent))
                return "A predecessor revision is missing from the captured Notebook.";
            if (!StringComparer.Ordinal.Equals(parent.Judgment.JudgmentId, item.Judgment.JudgmentId))
                return "A predecessor belongs to a different logical judgment.";
            if (!StringComparer.Ordinal.Equals(parent.ContentDigest, predecessor.ContentDigest))
                return "A predecessor content digest does not match its captured revision.";
        }

        var colors = new Dictionary<string, byte>(StringComparer.Ordinal);
        bool Visit(string revisionId)
        {
            if (colors.TryGetValue(revisionId, out var color)) return color == 2;
            colors[revisionId] = 1;
            foreach (var predecessor in revisions[revisionId].Judgment.Replaces)
            {
                if (colors.TryGetValue(predecessor.RevisionId, out color) && color == 1) return false;
                if (color == 0 && !Visit(predecessor.RevisionId)) return false;
            }
            colors[revisionId] = 2;
            return true;
        }

        foreach (var item in items)
            if (!Visit(item.Judgment.RevisionId)) return "The captured revision lineage contains a cycle.";
        return null;
    }

    private static string FindingIdentity(DispositionJudgment item) =>
        item.MeasureId + "\0" + HumanJudgmentCodec.SubjectKey(item.Subject) + "\0" + item.EvidenceDigest +
        "\0" + item.EvidenceContract;

    private static HumanJudgmentRevisionProjection ToRevision(StoredHumanJudgment item, string state) => new(
        item.RecordId, item.Judgment.JudgmentId, item.Judgment.RevisionId, item.ContentDigest,
        item.Judgment.Format, item.Judgment.Version, item.Judgment.Body switch
        {
            DispositionJudgment => "parsimony-disposition",
            ReviewedNegativeJudgment => "reviewed-negative",
            RetractionJudgment => "retraction",
            _ => "unknown",
        }, item.PhysicalValue, item.Judgment, state);

    private static string Hash(string value) => Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
