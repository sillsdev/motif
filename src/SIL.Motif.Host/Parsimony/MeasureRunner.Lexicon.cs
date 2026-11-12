using System.Text.Json;
using SIL.Motif.Contract.Parsimony;

namespace SIL.Motif.Host.Parsimony;

public static partial class MeasureRunner
{
    internal static ParsimonyMeasureResult DecideStemMsaWithoutCategory(MeasureDefinition measure,
        StemMsaInput input, MeasureContext context)
    {
        var findings = input.Missing.OrderBy(item => item.MsaGuid, StringComparer.Ordinal).Select(item =>
        {
            var identity = ParsimonyFindingIdentity.MsaIdentity(item.EntryGuid, item.MsaGuid);
            var name = context.Describe(item.EntryGuid);
            var entry = name is null ? $"Entry {item.EntryGuid}" : $"Entry \"{name}\"";
            var reading = item.EntryHasGlosses
                ? "its senses have glosses, so a part of speech is probably missing"
                : "none of its senses has a gloss, so it looks like a stub";
            return new ParsimonyFinding(
                ParsimonyFindingIdentity.GroupFindingId(measure.Id, identity), measure.Id, measure.Axis, measure.Tier,
                new ParsimonyFindingAttachment(ParsimonyAttachmentKind.Group, identity,
                    GroupKind: ParsimonyGroupKind.MsaWithoutCategory),
                ParsimonyFindingIdentity.EntryGroupKey(item.EntryGuid),
                new ParsimonyMeasureNumber(1, input.StemMsaCount, measure.Unit), measure.Threshold!,
                MeasureEvidence.Compute(measure, context.Digest, JsonSerializer.Serialize(new
                {
                    item.EntryGuid, item.MsaGuid, item.EntryHasGlosses,
                })),
                [EvidenceReference(context.BundleId, ParsimonyViews.AffixContext, new { objectGuid = item.MsaGuid })],
                measure.RecipeLink,
                [$"{entry} has a stem MSA with no part of speech; {reading}.",
                 "Only the stem MSA's own part-of-speech row was read; Text content was not used."],
                ParsimonyVerification.NotRun);
        }).ToArray();
        return Result(measure, ParsimonyMeasureStatus.Computed, input.StemMsaCount, findings.LongLength, findings, null);
    }

    internal static string TrimMarks(string value) => value.Trim().Trim('-', '=').Trim();
}
