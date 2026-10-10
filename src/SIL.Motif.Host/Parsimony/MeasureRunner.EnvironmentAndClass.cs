using System.Text.Json;
using SIL.Motif.Contract.Parsimony;

namespace SIL.Motif.Host.Parsimony;

public static partial class MeasureRunner
{
    private const int BroadEnvironmentMinimumWordTypes = 3;
    private const int BroadEnvironmentMinimumStems = 2;

    private static ParsimonyMeasureResult RunEnvironmentExcess(MeasureDefinition measure,
        ParsimonyQuerySession session, string bundleId)
    {
        var discovery = session.ReadEnvironmentExcess();
        var findings = new List<ParsimonyFinding>();
        var belowFloor = 0;
        var missingAttested = 0;
        foreach (var site in discovery.Sites)
        {
            if (site.MissingObserved.Count > 0)
            {
                missingAttested++;
                continue;
            }
            if (site.Extras.Count == 0) continue;
            if (site.WordTypes < BroadEnvironmentMinimumWordTypes || site.Stems < BroadEnvironmentMinimumStems)
            {
                belowFloor++;
                continue;
            }
            var observed = site.Observed.Select(guid => site.ContextNames.GetValueOrDefault(guid, guid)).ToArray();
            var extras = site.Extras.Select(guid => site.ContextNames.GetValueOrDefault(guid, guid)).ToArray();
            var featureExtension = site.IntersectionExtension
                .Select(guid => site.ContextNames.GetValueOrDefault(guid, guid)).ToArray();
            var unknownFeatureMembers = site.UnknownFeatureMembers
                .Select(guid => site.ContextNames.GetValueOrDefault(guid, guid)).ToArray();
            var groupKey = $"owner/{site.OwnerKey}/bucket/{site.Bucket}/side/{site.Side}";
            var detail = JsonSerializer.Serialize(new
            {
                site.AllomorphGuid,
                site.OwnerKey,
                site.MsaGuid,
                site.Bucket,
                site.Side,
                environments = site.EnvironmentGuids,
                site.Licensed,
                universe = site.Universe,
                observed = site.Observed,
                extras = site.Extras,
                site.FeatureIntersection,
                site.IntersectionExtension,
                site.UnknownFeatureMembers,
                site.WordTypes,
                site.Stems,
                site.Witnesses,
                policy = "one-neighbor/v1",
            });
            var limitations = new List<string>
            {
                $"Conditioned side: {site.Side}; morphology bucket: {site.Bucket}.",
                "Environment condition(s): " + string.Join(", ", site.EnvironmentNames) + ".",
                $"attested trigger: {string.Join(", ", observed)}.",
                $"extra licensed context: {string.Join(", ", extras)}.",
                $"Support: {site.WordTypes} word types across {site.Stems} stems; the trigger is at least 3 word types across at least 2 stems.",
                "Licensed but unobserved does not mean invalid; this is a question for the linguist, not a recommendation to remove the condition.",
                "The exact observed segment list and any listed class are comparison candidates, not default grammar fixes.",
                "Only simple one-neighbor phoneme, class, and explicit-boundary contexts are measured; unsupported shapes abstain.",
                "Sibling selection uses compiled order; each authored environment is treated as an OR alternative.",
            };
            if (site.ExistingClassesCoveringObserved.Count > 0)
                limitations.Add("Existing class alternatives that retain every attested trigger: " +
                    string.Join(", ", site.ExistingClassesCoveringObserved) + ".");
            if (site.FeatureIntersection.Count == 0)
                limitations.Add("The observed feature intersection is empty, so its candidate is unconstrained and is not called useful.");
            else
                limitations.Add("Feature-intersection candidate: " + string.Join(", ", site.FeatureIntersection) +
                    $"; it definitely includes {featureExtension.Length} phoneme(s).");
            if (unknownFeatureMembers.Length > 0)
                limitations.Add("Feature-intersection membership stays unknown for: " +
                    string.Join(", ", unknownFeatureMembers) + ".");
            var current = site.Licensed.ToHashSet(StringComparer.Ordinal);
            var candidate = site.IntersectionExtension.ToHashSet(StringComparer.Ordinal);
            if (site.UnknownFeatureMembers.Count == 0 && candidate.Count > 0 &&
                !current.IsSubsetOf(candidate) && !candidate.IsSubsetOf(current))
                limitations.Add("The current condition and feature-intersection candidate are incomparable by set inclusion.");
            var references = new List<ParsimonyEvidenceReference>
            {
                EvidenceReference(bundleId, "environment-excess", new { objectGuid = site.AllomorphGuid }),
                EvidenceReference(bundleId, "allomorph-context", new { objectGuid = site.AllomorphGuid }),
                EvidenceReference(bundleId, "approved-morph-sequences", new { objectGuid = (string?)null }),
            };
            references.AddRange(site.EnvironmentGuids.Select(environmentGuid =>
                EvidenceReference(bundleId, "environment-excess", new { objectGuid = environmentGuid })));
            findings.Add(new ParsimonyFinding(
                $"{measure.Id}:{site.AllomorphGuid}:{Digest(site.Bucket + "/" + site.Side)}", measure.Id,
                measure.Axis, measure.Tier,
                new ParsimonyFindingAttachment(measure.AttachesTo, site.AllomorphGuid, measure.AuthoredObjectKind),
                groupKey, new ParsimonyMeasureNumber(site.Extras.Count, site.Denominator, "context-types"),
                measure.Threshold!, Digest(detail),
                references,
                measure.RecipeLink, limitations, ParsimonyVerification.NotRun));
        }
        var result = findings.OrderBy(item => item.FindingId, StringComparer.Ordinal).ToArray();
        var status = discovery.UnsupportedSites == 0 && discovery.AmbiguousAlignments == 0 && missingAttested == 0
            ? ParsimonyMeasureStatus.Computed
            : ParsimonyMeasureStatus.Inconclusive;
        var detailText = discovery.UnsupportedSites == 0 && discovery.AmbiguousAlignments == 0 &&
                         missingAttested == 0 && belowFloor == 0 ? null :
            $"Abstained on {discovery.UnsupportedSites} unsupported site(s), " +
            $"{discovery.AmbiguousAlignments} ambiguous alignment(s), and {missingAttested} site(s) that miss attested triggers; " +
            $"{belowFloor} site(s) did not meet the support floor.";
        return Result(measure, status, discovery.Sites.Count, result.Length, result, detailText,
            status == ParsimonyMeasureStatus.Inconclusive
                ? "some sites have unsupported shapes, ambiguous alignments, or missing attested triggers, so they were not checked."
                : null);
    }

    private static ParsimonyMeasureResult RunNaturalClassExcess(MeasureDefinition measure,
        ParsimonyQuerySession session, string bundleId)
    {
        var discovery = session.ReadNaturalClassExcess();
        var findings = new List<ParsimonyFinding>();
        foreach (var site in discovery.Sites.Where(item => item.Extras.Count > 0))
        {
            var observed = site.Observed.Select(guid => site.ContextNames.GetValueOrDefault(guid, guid)).ToArray();
            var extras = site.Extras.Select(guid => site.ContextNames.GetValueOrDefault(guid, guid)).ToArray();
            var unknownFeatureMembers = site.UnknownFeatureMembers
                .Select(guid => site.ContextNames.GetValueOrDefault(guid, guid)).ToArray();
            var unknownClassMembers = site.UnknownClassMembers;
            var detail = JsonSerializer.Serialize(new
            {
                site.ClassGuid,
                site.UsageSite,
                site.EnvironmentGuid,
                site.AllomorphGuid,
                site.Side,
                site.Observed,
                site.Extras,
                featureIntersection = site.FeatureIntersection,
                site.IntersectionExtension,
                unknownFeatureMembers = site.UnknownFeatureMembers,
                unknownClassMembers,
                site.WordTypes,
                site.Stems,
                site.Witnesses,
            });
            var limitations = new List<string>
            {
                $"Usage site: {site.UsageSite}; conditioned side: {site.Side}.",
                $"Natural class: {site.ClassName}.",
                $"attested trigger: {string.Join(", ", observed)}.",
                $"extra class member: {string.Join(", ", extras)}.",
                $"Support: {site.WordTypes} word types across {site.Stems} stems; this measure reports even sparse observations.",
                "An unobserved class member can be an intentional generalization; unobserved does not mean invalid.",
                site.FeatureIntersection.Count == 0
                    ? "The observed feature intersection is empty, so the feature candidate is unconstrained and is not called useful."
                    : "Shared closed feature assignment(s): " + string.Join(", ", site.FeatureIntersection) + ".",
                $"The feature-intersection candidate definitely includes {site.IntersectionExtension.Count} phoneme(s).",
            };
            if (site.MissingObserved.Count > 0)
                limitations.Add("The current class misses attested trigger(s): " +
                    string.Join(", ", site.MissingObserved.Select(guid => site.ContextNames.GetValueOrDefault(guid, guid))) + ".");
            if (unknownFeatureMembers.Length > 0)
                limitations.Add("unknown feature membership: " + string.Join(", ", unknownFeatureMembers) +
                    "; no exact excess rate or tightest-class claim is made.");
            if (unknownClassMembers.Count > 0)
                limitations.Add("The compiler reported class members without source phoneme identities; membership is unknown.");
            findings.Add(new ParsimonyFinding(
                $"{measure.Id}:{site.ClassGuid}:{Digest(site.UsageSite)}", measure.Id,
                measure.Axis, measure.Tier,
                new ParsimonyFindingAttachment(measure.AttachesTo, site.ClassGuid, measure.AuthoredObjectKind),
                $"class/{site.ClassGuid}/site/{Digest(site.UsageSite)}",
                new ParsimonyMeasureNumber(site.Extras.Count, site.Denominator, "phoneme-types"),
                measure.Threshold!, Digest(detail),
                [EvidenceReference(bundleId, "natural-class-excess", new { objectGuid = site.ClassGuid }),
                 EvidenceReference(bundleId, "natural-class-context", new { objectGuid = site.ClassGuid }),
                 EvidenceReference(bundleId, "approved-morph-sequences", new { objectGuid = (string?)null })],
                measure.RecipeLink, limitations, ParsimonyVerification.NotRun));
        }
        var result = findings.OrderBy(item => item.FindingId, StringComparer.Ordinal).ToArray();
        var status = discovery.UnsupportedSites == 0 && discovery.AmbiguousAlignments == 0
            ? ParsimonyMeasureStatus.Computed
            : ParsimonyMeasureStatus.Inconclusive;
        var detailText = discovery.UnsupportedSites == 0 && discovery.AmbiguousAlignments == 0 ? null :
            $"Abstained on {discovery.UnsupportedSites} unsupported usage site(s) and " +
            $"{discovery.AmbiguousAlignments} ambiguous alignment(s).";
        return Result(measure, status, discovery.Sites.Count, result.Length, result, detailText,
            status == ParsimonyMeasureStatus.Inconclusive
                ? "some usage sites have unsupported shapes or ambiguous alignments, so they were not checked."
                : null);
    }
}
