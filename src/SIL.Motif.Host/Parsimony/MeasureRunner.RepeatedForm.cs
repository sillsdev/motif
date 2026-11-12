using System.Text.Json;
using SIL.Motif.Contract.Parsimony;

namespace SIL.Motif.Host.Parsimony;

public static partial class MeasureRunner
{
    private const int RelationLinesCap = 20;

    // One copy's environment list is empty, a subset of another's, or the same set in another order.
    private enum RepeatedFormRelation { Unconditioned, Subset, SameSet }

    private sealed record RepeatedCopy(string EnvironmentsKey, IReadOnlyList<AllomorphMeasureFact> Members,
        IReadOnlySet<string> EnvironmentSet);

    private sealed record RepeatedFormSubsumption(string Forms, string MorphType, RepeatedCopy Subsumed,
        RepeatedCopy Subsuming, RepeatedFormRelation Relation);

    internal static ParsimonyMeasureResult DecideRepeatedFormAllomorphs(MeasureDefinition measure,
        IReadOnlyList<AllomorphMeasureFact> allomorphs, MeasureContext context)
    {
        var findings = new List<ParsimonyFinding>();
        var eligible = 0L;
        foreach (var entry in allomorphs.GroupBy(item => item.EntryGuid, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var members = entry.ToArray();
            if (members.Length < 2) continue;
            eligible++;
            var subsumptions = SubsumedCopies(members);
            if (subsumptions.Count == 0) continue;
            findings.Add(RepeatedFormFinding(measure, context, entry.Key, subsumptions));
        }
        var result = findings.OrderBy(item => item.FindingId, StringComparer.Ordinal).ToArray();
        return Result(measure, ParsimonyMeasureStatus.Computed, eligible, result.Length, result, null);
    }

    private static List<RepeatedFormSubsumption> SubsumedCopies(IReadOnlyList<AllomorphMeasureFact> entryAllomorphs)
    {
        var found = new List<RepeatedFormSubsumption>();
        var sameForm = entryAllomorphs.GroupBy(item => (item.MorphType, Forms: FormsSignature(item.Forms)))
            .Where(group => group.Count() > 1)
            .OrderBy(group => group.Key.MorphType, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Forms, StringComparer.Ordinal);
        foreach (var group in sameForm)
        {
            // Copies with the exact same environment list are P-allo-duplicate-form's; one copy stands for them here.
            var copies = group.GroupBy(item => EnvironmentsSignature(item.Environments), StringComparer.Ordinal)
                .Select(copy => new RepeatedCopy(copy.Key,
                    copy.OrderBy(item => item.Guid, StringComparer.Ordinal).ToArray(),
                    copy.First().Environments.Select(item => item.Role + "|" + item.EnvironmentGuid)
                        .ToHashSet(StringComparer.Ordinal)))
                .OrderBy(copy => copy.Members[0].Guid, StringComparer.Ordinal).ToArray();
            for (var first = 0; first < copies.Length; first++)
            for (var second = first + 1; second < copies.Length; second++)
            {
                var (a, b) = (copies[first], copies[second]);
                var forms = DescribeForms(a.Members[0]);
                if (a.EnvironmentSet.Count == 0 != (b.EnvironmentSet.Count == 0))
                {
                    var (open, conditioned) = a.EnvironmentSet.Count == 0 ? (a, b) : (b, a);
                    found.Add(new(forms, group.Key.MorphType, conditioned, open, RepeatedFormRelation.Unconditioned));
                }
                else if (a.EnvironmentSet.SetEquals(b.EnvironmentSet))
                    found.Add(new(forms, group.Key.MorphType, b, a, RepeatedFormRelation.SameSet));
                else if (a.EnvironmentSet.IsProperSubsetOf(b.EnvironmentSet))
                    found.Add(new(forms, group.Key.MorphType, a, b, RepeatedFormRelation.Subset));
                else if (b.EnvironmentSet.IsProperSubsetOf(a.EnvironmentSet))
                    found.Add(new(forms, group.Key.MorphType, b, a, RepeatedFormRelation.Subset));
            }
        }
        return found;
    }

    private static string DescribeForms(AllomorphMeasureFact allomorph) => string.Join(" / ",
        allomorph.Forms.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .SelectMany(pair => pair.Value.Select(form => $"\"{form}\" ({pair.Key})")));

    private static ParsimonyFinding RepeatedFormFinding(MeasureDefinition measure, MeasureContext context,
        string entryGuid, IReadOnlyList<RepeatedFormSubsumption> subsumptions)
    {
        var involved = subsumptions.SelectMany(item => item.Subsumed.Members.Concat(item.Subsuming.Members))
            .DistinctBy(item => item.Guid, StringComparer.Ordinal).OrderBy(item => item.Guid, StringComparer.Ordinal)
            .ToArray();
        var subsumed = subsumptions.SelectMany(item => item.Subsumed.Members).Select(item => item.Guid)
            .Distinct(StringComparer.Ordinal).Count();
        var memberIds = involved.Select(item => item.Guid).ToArray();
        var identity = ParsimonyFindingIdentity.RepeatedFormsIdentity(entryGuid, memberIds);
        var digestValue = JsonSerializer.Serialize(new
        {
            entryGuid,
            subsumptions = subsumptions.Select(item => new
            {
                item.Forms, item.MorphType, relation = item.Relation.ToString(),
                subsumed = item.Subsumed.Members.Select(member => member.Guid),
                subsuming = item.Subsuming.Members.Select(member => member.Guid),
                subsumedEnvironments = item.Subsumed.EnvironmentsKey, subsumingEnvironments = item.Subsuming.EnvironmentsKey,
            }),
        });
        var limitations = subsumptions.Take(RelationLinesCap).Select(item => RelationLine(item, context)).ToList();
        if (subsumptions.Count > RelationLinesCap)
            limitations.Add($"{subsumptions.Count - RelationLinesCap} more repeated forms on this entry are not listed.");
        limitations.Add("Only copies that one environment list subsumes are reported; copies with disjoint or only " +
            "overlapping environment lists can be a legitimate disjunction and are not.");
        limitations.Add("Copies with the identical environment list are reported by P-allo-duplicate-form, and " +
            "allomorphs with no forms are skipped.");
        return new ParsimonyFinding(
            ParsimonyFindingIdentity.GroupFindingId(measure.Id, identity), measure.Id, measure.Axis, measure.Tier,
            new ParsimonyFindingAttachment(ParsimonyAttachmentKind.Group, identity,
                GroupKind: ParsimonyGroupKind.AllomorphDuplicateForms),
            ParsimonyFindingIdentity.EntryGroupKey(entryGuid),
            new ParsimonyMeasureNumber(subsumed, involved.Length, measure.Unit), measure.Threshold!,
            MeasureEvidence.Compute(measure, context.Digest, digestValue),
            involved.Select(item => EvidenceReference(context.BundleId, ParsimonyViews.AllomorphContext,
                new { objectGuid = item.Guid })).ToArray(),
            measure.RecipeLink, limitations, ParsimonyVerification.NotRun);
    }

    private static string RelationLine(RepeatedFormSubsumption item, MeasureContext context)
    {
        var head = $"{item.MorphType} {item.Forms}: ";
        var subsumed = $"{ListEnvironments(item.Subsumed, context)} (allomorph {Ids(item.Subsumed)})";
        var subsuming = $"{ListEnvironments(item.Subsuming, context)} (allomorph {Ids(item.Subsuming)})";
        return item.Relation switch
        {
            RepeatedFormRelation.Unconditioned => head + $"the unconditioned copy (allomorph {Ids(item.Subsuming)}) " +
                $"applies in every context, so it subsumes the copy conditioned on {subsumed}.",
            RepeatedFormRelation.Subset => head + $"the copy conditioned on {subsumed} is subsumed by the copy " +
                $"conditioned on {subsuming}, whose list holds all of its environments.",
            _ => head + $"the copies list the same environments in a different order ({subsumed} and {subsuming}).",
        };
    }

    private static string Ids(RepeatedCopy copy) => string.Join(", ", copy.Members.Select(member => member.Guid));

    private static string ListEnvironments(RepeatedCopy copy, MeasureContext context)
    {
        var members = copy.Members[0].Environments.Select(environment => context.Describe(environment.EnvironmentGuid)
            is { } words ? words : environment.EnvironmentGuid);
        return copy.EnvironmentSet.Count == 0 ? "no environment" : string.Join(" or ", members);
    }
}
