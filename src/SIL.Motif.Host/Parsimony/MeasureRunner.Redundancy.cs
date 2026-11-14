using System.Text.Json;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Parsimony.RuleAttempts;

namespace SIL.Motif.Host.Parsimony;

public static partial class MeasureRunner
{
    /// <summary>The fewest stems a rule's or affix's category scope needs before a tag majority means anything.</summary>
    internal const int PolarityMinimumHosts = 5;

    // O(entries): one hash bucket per folded form; forms that already match exactly are P-entry-duplicate's.
    internal static ParsimonyMeasureResult DecideNearDuplicateLexemes(MeasureDefinition measure, LexemeFormInput input,
        MeasureContext context)
    {
        var allomorphOf = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var fact in input.Forms) allomorphOf[fact.EntryGuid] = fact.AllomorphGuid;
        var groups = RedundancyMeasures.NearDuplicateLexemes(input.Forms.Select(f => new LexemeForm(f.EntryGuid, f.Form)));
        var findings = new List<ParsimonyFinding>(groups.Count);
        var redundant = 0L;
        foreach (var group in groups)
        {
            var members = group.OrderBy(m => m.EntryId, StringComparer.Ordinal).ToArray();
            var ids = members.Select(m => m.EntryId).ToArray();
            redundant += members.Length - 1;
            var identity = ParsimonyFindingIdentity.NearDuplicateLexemesIdentity(ids);
            findings.Add(new ParsimonyFinding(
                ParsimonyFindingIdentity.GroupFindingId(measure.Id, identity), measure.Id, measure.Axis, measure.Tier,
                new ParsimonyFindingAttachment(ParsimonyAttachmentKind.Group, identity,
                    GroupKind: ParsimonyGroupKind.NearDuplicateLexemes),
                ParsimonyFindingIdentity.NearDuplicateLexemesGroupKey,
                new ParsimonyMeasureNumber(members.Length - 1, input.EntryCount, measure.Unit), measure.Threshold!,
                MeasureEvidence.Compute(measure, context.Digest, JsonSerializer.Serialize(new { members = ids })),
                members.Select(m => EvidenceReference(context.BundleId, ParsimonyViews.AllomorphContext,
                    new { objectGuid = allomorphOf[m.EntryId] })).ToArray(),
                measure.RecipeLink,
                [$"Entries whose lexeme forms differ only in case, Unicode form or iy/y: " +
                 string.Join("; ", members.Select(m => $"\"{m.Form}\" ({m.EntryId})")) + ".",
                 "Exact repeats are reported by P-entry-duplicate, not here."],
                ParsimonyVerification.NotRun));
        }
        return Result(measure, ParsimonyMeasureStatus.Computed, input.EntryCount, redundant, findings.OrderBy(
            f => f.FindingId, StringComparer.Ordinal).ToArray(), null);
    }

    // O(variant links): one shape key per link, hash-grouped; five or more of one shape suggest a rule (ADR 0116).
    internal static ParsimonyMeasureResult DecideVariantSameShape(MeasureDefinition measure, VariantShapeInput input,
        MeasureContext context)
    {
        var shapes = input.Variants.Select(v => new AddedObjectShape(v.VariantEntryGuid, v.TypeGuid, v.EnvironmentGuids,
            [], StringCorrespondence.Of(v.MainForm, v.VariantForm))).ToArray();
        var allomorphOf = input.Variants.GroupBy(v => v.VariantEntryGuid, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().VariantAllomorphGuid, StringComparer.Ordinal);
        var findings = new List<ParsimonyFinding>();
        var redundant = 0L;
        foreach (var group in SameShapeGroups.Find(shapes))
        {
            var ids = group.ObjectIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            if (ids.Length < SameShapeGroups.RuleAttemptThreshold) continue;
            redundant += ids.Length - 1;
            var identity = ParsimonyFindingIdentity.VariantShapeIdentity(group.ShapeKey, ids);
            findings.Add(new ParsimonyFinding(
                ParsimonyFindingIdentity.GroupFindingId(measure.Id, identity), measure.Id, measure.Axis, measure.Tier,
                new ParsimonyFindingAttachment(ParsimonyAttachmentKind.Group, identity,
                    GroupKind: ParsimonyGroupKind.VariantSameShape),
                ParsimonyFindingIdentity.VariantShapeGroupKey(group.ShapeKey),
                new ParsimonyMeasureNumber(ids.Length - 1, input.Variants.Count, measure.Unit), measure.Threshold!,
                MeasureEvidence.Compute(measure, context.Digest,
                    JsonSerializer.Serialize(new { shape = group.ShapeKey, members = ids })),
                ids.Take(10).Select(id => EvidenceReference(context.BundleId, ParsimonyViews.AllomorphContext,
                    new { objectGuid = allomorphOf[id] })).ToArray(),
                measure.RecipeLink,
                [$"{ids.Length} variants share the shape {group.ShapeKey} (same variant type, environments and " +
                 "spelling change). A rule may state them once.",
                 "Name a rule attempt (the rule, its prediction test, why it failed) before adding more of this shape."],
                ParsimonyVerification.NotRun));
        }
        return Result(measure, ParsimonyMeasureStatus.Computed, input.Variants.Count, redundant,
            findings.OrderBy(f => f.FindingId, StringComparer.Ordinal).ToArray(), null);
    }

    // O(MSAs + rules): counts are grouped in SQL; the decision is one pass over the tag-and-requirer rows.
    internal static ParsimonyMeasureResult DecideTagPolarity(MeasureDefinition measure, TagPolarityInput input,
        MeasureContext context)
    {
        var required = input.Rows.Where(r => r.Polarity == "required" && r.Hosts >= PolarityMinimumHosts).ToArray();
        var byKey = required.ToDictionary(r => (r.TagGuid, r.RequirerKind + "/" + r.RequirerGuid));
        var flips = RedundancyMeasures.PolarityFlips(required.Select(r =>
            new TagPolarityFacts(r.TagGuid, r.RequirerKind + "/" + r.RequirerGuid, (int)r.Hosts, (int)r.Carriers)));
        var findings = new List<ParsimonyFinding>(flips.Count);
        foreach (var flip in flips)
        {
            var row = byKey[(flip.TagId, flip.RuleId)];
            var identity = ParsimonyFindingIdentity.TagPolarityIdentity(row.TagGuid, row.RequirerKind, row.RequirerGuid);
            findings.Add(new ParsimonyFinding(
                ParsimonyFindingIdentity.GroupFindingId(measure.Id, identity), measure.Id, measure.Axis, measure.Tier,
                new ParsimonyFindingAttachment(ParsimonyAttachmentKind.Group, identity,
                    GroupKind: ParsimonyGroupKind.TagPolarity),
                ParsimonyFindingIdentity.TagPolarityGroupKey,
                new ParsimonyMeasureNumber(row.Carriers, row.Hosts, measure.Unit), measure.Threshold!,
                MeasureEvidence.Compute(measure, context.Digest, JsonSerializer.Serialize(new
                {
                    tag = row.TagGuid, row.RequirerKind, row.RequirerGuid, row.Hosts, row.Carriers,
                })),
                [], measure.RecipeLink,
                [$"The {row.RequirerKind} {row.RequirerGuid} requires tag {row.TagGuid}, which {row.Carriers} of " +
                 $"{row.Hosts} stems in its category scope carry. When most hosts carry a tag, tag the exceptions " +
                 "and flip the polarity.",
                 "Hosts are stem MSAs whose part of speech is in the rule's or affix's category scope; a requirer with " +
                 "no category scope counts every stem."],
                ParsimonyVerification.NotRun));
        }
        return Result(measure, ParsimonyMeasureStatus.Computed, required.Length, findings.Count,
            findings.OrderBy(f => f.FindingId, StringComparer.Ordinal).ToArray(), null);
    }
}
