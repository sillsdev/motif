using System.Text.Json;
using SIL.Motif.Contract.Parsimony;

namespace SIL.Motif.Host.Parsimony;

public static partial class MeasureRunner
{
    private const int BroadEnvironmentMinimumWordTypes = 3;
    private const int BroadEnvironmentMinimumStems = 2;

    // The evidence detail keeps every name; a limit line is read by a person, so it stops at a short list.
    internal static string Listed(IReadOnlyList<string> names) =>
        names.Count <= ParsimonyViewLimits.LimitLineNames
            ? string.Join(", ", names)
            : string.Join(", ", names.Take(ParsimonyViewLimits.LimitLineNames)) +
              $", and {names.Count - ParsimonyViewLimits.LimitLineNames} more";

    internal static ParsimonyMeasureResult DecideEnvironmentExcess(MeasureDefinition measure,
        EnvironmentExcessDiscovery discovery, MeasureContext context)
    {
        var bundleId = context.BundleId;
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
                $"attested trigger: {Listed(observed)}.",
                $"extra licensed context: {Listed(extras)}.",
                $"Support: {site.WordTypes} word types across {site.Stems} stems; the trigger is at least 3 word types across at least 2 stems.",
                "Licensed but unobserved does not mean invalid; this is a question for the linguist, not a recommendation to remove the condition.",
                "The exact observed segment list and any listed class are comparison candidates, not default grammar fixes.",
                "Only simple one-neighbor phoneme, class, and explicit-boundary contexts are measured; unsupported shapes abstain.",
                "Sibling selection uses compiled order; each authored environment is treated as an OR alternative.",
            };
            if (site.ExistingClassesCoveringObserved.Count > 0)
                limitations.Add("Existing class alternatives that retain every attested trigger: " +
                    Listed(site.ExistingClassesCoveringObserved) + ".");
            if (site.FeatureIntersection.Count == 0)
                limitations.Add("The observed feature intersection is empty, so its candidate is unconstrained and is not called useful.");
            else
                limitations.Add("Feature-intersection candidate: " + string.Join(", ", site.FeatureIntersection) +
                    $"; it definitely includes {featureExtension.Length} phoneme(s).");
            if (unknownFeatureMembers.Length > 0)
                limitations.Add("Feature-intersection membership stays unknown for: " +
                    Listed(unknownFeatureMembers) + ".");
            var current = site.Licensed.ToHashSet(StringComparer.Ordinal);
            var candidate = site.IntersectionExtension.ToHashSet(StringComparer.Ordinal);
            if (site.UnknownFeatureMembers.Count == 0 && candidate.Count > 0 &&
                !current.IsSubsetOf(candidate) && !candidate.IsSubsetOf(current))
                limitations.Add("The current condition and feature-intersection candidate are incomparable by set inclusion.");
            var references = new List<ParsimonyEvidenceReference>
            {
                EvidenceReference(bundleId, ParsimonyViews.EnvironmentExcess, new { objectGuid = site.AllomorphGuid }),
                EvidenceReference(bundleId, ParsimonyViews.AllomorphContext, new { objectGuid = site.AllomorphGuid }),
                EvidenceReference(bundleId, ParsimonyViews.ApprovedMorphSequences, new { objectGuid = (string?)null }),
            };
            references.AddRange(site.EnvironmentGuids.Select(environmentGuid =>
                EvidenceReference(bundleId, ParsimonyViews.EnvironmentExcess, new { objectGuid = environmentGuid })));
            findings.Add(new ParsimonyFinding(
                ParsimonyFindingIdentity.SiteFindingId(measure.Id, site.AllomorphGuid, site.Bucket + "/" + site.Side), measure.Id,
                measure.Axis, measure.Tier,
                new ParsimonyFindingAttachment(measure.AttachesTo, site.AllomorphGuid, measure.AuthoredObjectKind),
                groupKey, new ParsimonyMeasureNumber(site.Extras.Count, site.Denominator, "context-types"),
                measure.Threshold!, MeasureEvidence.Compute(measure, context.Digest, detail),
                references,
                measure.RecipeLink, limitations, ParsimonyVerification.NotRun));
        }
        var result = findings.OrderBy(item => item.FindingId, StringComparer.Ordinal).ToArray();
        var status = discovery.UnsupportedSites == 0 && discovery.AmbiguousAlignments == 0 && missingAttested == 0
            ? ParsimonyMeasureStatus.Computed
            : ParsimonyMeasureStatus.Inconclusive;
        var abstentions = new MeasureAbstentions();
        abstentions.Add("unsupported-site", discovery.UnsupportedSites,
            $"{discovery.UnsupportedSites} unsupported site(s) have an environment shape this check does not measure.");
        abstentions.Add("ambiguous-alignment", discovery.AmbiguousAlignments,
            $"{discovery.AmbiguousAlignments} ambiguous alignment(s) of a morph to its word were set aside.");
        abstentions.Add("missing-attested-trigger", missingAttested,
            $"{missingAttested} site(s) miss attested triggers.");
        abstentions.Add("below-support-floor", belowFloor,
            $"{belowFloor} site(s) did not meet the support floor.");
        var detailText = status == ParsimonyMeasureStatus.Inconclusive ? abstentions.Detail()
            : belowFloor == 0 ? null
            : $"Abstained on 0 unsupported site(s), 0 ambiguous alignment(s), and 0 site(s) that miss attested triggers; " +
              $"{belowFloor} site(s) did not meet the support floor.";
        var causes = new List<string>();
        if (discovery.UnsupportedSites > 0) causes.Add("some sites have unsupported shapes");
        if (discovery.AmbiguousAlignments > 0) causes.Add("some sites have ambiguous alignments");
        if (missingAttested > 0) causes.Add("some sites miss attested triggers");
        return Result(measure, status, discovery.Sites.Count, result.Length, result, detailText,
            status == ParsimonyMeasureStatus.Inconclusive
                ? MeasureAbstentions.JoinClauses(causes) + ", so they were not checked."
                : null);
    }

    internal static ParsimonyMeasureResult DecideNaturalClassExcess(MeasureDefinition measure,
        NaturalClassExcessDiscovery discovery, MeasureContext context)
    {
        var bundleId = context.BundleId;
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
                $"attested trigger: {Listed(observed)}.",
                $"extra class member: {Listed(extras)}.",
                $"Support: {site.WordTypes} word types across {site.Stems} stems; this measure reports even sparse observations.",
                "An unobserved class member can be an intentional generalization; unobserved does not mean invalid.",
                site.FeatureIntersection.Count == 0
                    ? "The observed feature intersection is empty, so the feature candidate is unconstrained and is not called useful."
                    : "Shared closed feature assignment(s): " + string.Join(", ", site.FeatureIntersection) + ".",
                $"The feature-intersection candidate definitely includes {site.IntersectionExtension.Count} phoneme(s).",
            };
            if (site.MissingObserved.Count > 0)
                limitations.Add("The current class misses attested trigger(s): " +
                    Listed(site.MissingObserved.Select(guid => site.ContextNames.GetValueOrDefault(guid, guid)).ToArray()) + ".");
            if (unknownFeatureMembers.Length > 0)
                limitations.Add("unknown feature membership: " + Listed(unknownFeatureMembers) +
                    "; no exact excess rate or tightest-class claim is made.");
            if (unknownClassMembers.Count > 0)
                limitations.Add("The compiler reported class members without source phoneme identities; membership is unknown.");
            findings.Add(new ParsimonyFinding(
                ParsimonyFindingIdentity.SiteFindingId(measure.Id, site.ClassGuid, site.UsageSite), measure.Id,
                measure.Axis, measure.Tier,
                new ParsimonyFindingAttachment(measure.AttachesTo, site.ClassGuid, measure.AuthoredObjectKind),
                ParsimonyFindingIdentity.ClassSiteGroupKey(site.ClassGuid, site.UsageSite),
                new ParsimonyMeasureNumber(site.Extras.Count, site.Denominator, "phoneme-types"),
                measure.Threshold!, MeasureEvidence.Compute(measure, context.Digest, detail),
                [EvidenceReference(bundleId, ParsimonyViews.NaturalClassExcess, new { objectGuid = site.ClassGuid }),
                 EvidenceReference(bundleId, ParsimonyViews.NaturalClassContext, new { objectGuid = site.ClassGuid }),
                 EvidenceReference(bundleId, ParsimonyViews.ApprovedMorphSequences, new { objectGuid = (string?)null })],
                measure.RecipeLink, limitations, ParsimonyVerification.NotRun));
        }
        var result = findings.OrderBy(item => item.FindingId, StringComparer.Ordinal).ToArray();
        var status = discovery.UnsupportedSites == 0 && discovery.AmbiguousAlignments == 0
            ? ParsimonyMeasureStatus.Computed
            : ParsimonyMeasureStatus.Inconclusive;
        var abstentions = new MeasureAbstentions();
        abstentions.Add("unsupported-usage-site", discovery.UnsupportedSites,
            $"{discovery.UnsupportedSites} unsupported usage site(s) have an environment shape this check does not measure.");
        abstentions.Add("ambiguous-alignment", discovery.AmbiguousAlignments,
            $"{discovery.AmbiguousAlignments} ambiguous alignment(s) of a morph to its word were set aside.");
        var causes = new List<string>();
        if (discovery.UnsupportedSites > 0) causes.Add("some usage sites have unsupported shapes");
        if (discovery.AmbiguousAlignments > 0) causes.Add("some usage sites have ambiguous alignments");
        return Result(measure, status, discovery.Sites.Count, result.Length, result,
            abstentions.Any ? abstentions.Detail() : null,
            status == ParsimonyMeasureStatus.Inconclusive
                ? MeasureAbstentions.JoinClauses(causes) + ", so they were not checked."
                : null);
    }
}
