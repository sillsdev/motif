using System.Text.Json;
using SIL.Motif.Contract.Parsimony;

namespace SIL.Motif.Host.Parsimony;

public static partial class MeasureRunner
{
    /// <summary>An exception feature carried by fewer entries than this, and used, is a finding.</summary>
    internal const int FewCarriersBelow = 4;

    private const int NamedUsersCap = 10;

    internal static ParsimonyMeasureResult DecideFewCarrierFeatures(MeasureDefinition measure,
        ExceptionFeatureInput input, MeasureContext context)
    {
        var features = input.Features;
        var unusedAndCarrierless = features.Count(item =>
            item.Carriers.Count == 0 && !IsUsed(item));
        var slotLine = SlotLine(input.Slots);
        var unusedLine = unusedAndCarrierless == 0 ? null :
            $"{unusedAndCarrierless} exception feature(s) have no carrier and no user; they are left to unused-" +
            "statement reporting and not flagged here.";
        var findings = new List<ParsimonyFinding>();
        foreach (var item in features.Where(item => item.Carriers.Count < FewCarriersBelow &&
                     IsUsed(item)))
        {
            var identity = ParsimonyFindingIdentity.ExceptionFeatureIdentity(item.Guid);
            var digestValue = JsonSerializer.Serialize(new
            {
                item.Guid, item.Carriers, affixes = item.AffixUsers, rules = item.RuleUsers, compounds = item.CompoundUsers,
            });
            var references = item.AffixUsers.Select(user => user.MsaGuid).Distinct(StringComparer.Ordinal)
                .Take(NamedUsersCap * 2).Select(msa => EvidenceReference(context.BundleId,
                    ParsimonyViews.AffixContext, new { objectGuid = msa })).ToArray();
            var limitations = new List<string>(FeatureLines(item, context)) { slotLine };
            if (unusedLine is not null) limitations.Add(unusedLine);
            findings.Add(new ParsimonyFinding(
                ParsimonyFindingIdentity.GroupFindingId(measure.Id, identity), measure.Id, measure.Axis, measure.Tier,
                new ParsimonyFindingAttachment(ParsimonyAttachmentKind.Group, identity,
                    GroupKind: ParsimonyGroupKind.ExceptionFeature),
                ParsimonyFindingIdentity.ExceptionFeatureGroupKey,
                new ParsimonyMeasureNumber(1, features.Count, measure.Unit), measure.Threshold!,
                MeasureEvidence.Compute(measure, context.Digest, digestValue), references, measure.RecipeLink,
                limitations, ParsimonyVerification.NotRun));
        }
        var result = findings.OrderBy(item => item.FindingId, StringComparer.Ordinal).ToArray();
        var detail = unusedLine is null ? slotLine : slotLine + " " + unusedLine;
        return Result(measure, ParsimonyMeasureStatus.Computed, features.Count, result.Length, result, detail);
    }

    private static bool IsUsed(ExceptionFeatureFact item) =>
        item.AffixUsers.Count > 0 || item.RuleUsers.Count > 0 || item.CompoundUsers.Count > 0;

    private static IEnumerable<string> FeatureLines(ExceptionFeatureFact item, MeasureContext context)
    {
        var label = string.IsNullOrWhiteSpace(item.Name) ? item.Abbreviation : item.Name;
        var count = item.Carriers.Count;
        yield return count == 0
            ? $"Exception feature \"{label}\" is carried by no entry on a stem MSA, so what requires it cannot apply."
            : $"Exception feature \"{label}\" is carried by {count} entr{(count == 1 ? "y" : "ies")} on stem MSAs: " +
              string.Join(", ", item.Carriers.Select(entry => Named(context, entry))) + ".";
        if (item.AffixUsers.Count > 0)
        {
            var named = item.AffixUsers.Take(NamedUsersCap).Select(user =>
                $"{Named(context, user.EntryGuid)} (MSA {user.MsaGuid}, {user.Role.Replace('_', ' ')})");
            var more = item.AffixUsers.Count - NamedUsersCap;
            yield return $"Required by {item.AffixUsers.Count} affix MSA(s): {string.Join(", ", named)}" +
                (more > 0 ? $", and {more} more." : ".");
        }
        foreach (var rule in item.RuleUsers.Take(NamedUsersCap))
        {
            var name = string.IsNullOrWhiteSpace(rule.Name) ? rule.RuleGuid : $"\"{rule.Name}\"";
            var effect = count != 1 ? string.Empty : rule.Polarity == "required"
                ? " With one carrier it is a rule that applies to a single word."
                : " With one carrier it is a rule that applies to every word except one.";
            yield return $"Rule {name} {(rule.Polarity == "required" ? "requires" : "excludes")} the feature." + effect;
        }
        foreach (var rule in item.CompoundUsers.Take(NamedUsersCap))
        {
            var name = string.IsNullOrWhiteSpace(rule.Name) ? rule.RuleGuid : $"\"{rule.Name}\"";
            yield return $"Compound rule {name} requires the feature on its {rule.Side} constituent." +
                (count == 1 ? " With one carrier it is a rule that applies to a single word." : string.Empty);
        }
        if (item.RuleUsers.Count > NamedUsersCap)
            yield return $"{item.RuleUsers.Count - NamedUsersCap} more rule(s) use the feature.";
        yield return "Only stem MSAs that require the feature count as carriers; a feature on an affix MSA is a use. " +
            "A feature carried by fewer than four entries is a listed word dressed as a productivity restriction.";
    }

    private static string SlotLine(FeatureSlotCount slots)
    {
        var headroom = FeatureSlotCount.Cap - slots.Total;
        var room = headroom >= 0 ? $"headroom {headroom}" : $"over the cap by {-headroom}";
        return $"PanGloss feature slots used: {slots.Total} of {FeatureSlotCount.Cap} ({room}); " +
            $"{slots.InflectionClasses} inflection classes including subclasses, {slots.ExceptionFeatures} exception " +
            $"features and {slots.InflectionTypes} entry inflection types. The count is approximate where PanGloss " +
            "dedupes differently.";
    }

    private static string Named(MeasureContext context, string guid) => context.Describe(guid) is { } words
        ? $"\"{words}\"" : guid;
}
