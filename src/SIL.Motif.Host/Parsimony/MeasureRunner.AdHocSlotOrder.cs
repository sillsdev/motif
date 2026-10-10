using System.Text.Json;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Host.Parsimony;

public static partial class MeasureRunner
{
    private static ParsimonyMeasureResult RunAdhocSlotOrder(MeasureDefinition measure,
        ParsimonyQuerySession session, string bundleId)
    {
        var rules = session.ReadAdhocProhibitions();
        var facts = session.ReadTemplateSequenceFacts();
        var compiledMorphRuleMsas = session.ReadCompiledMorphRuleMsas();
        var loaderReasons = session.ReadAdhocProhibitionLoaderReasons();
        var reviews = new List<AdhocSlotOrderReview>();
        var eligible = 0L;
        var unknown = false;

        foreach (var rule in rules)
        {
            var review = ReviewAdhocSlotOrderRule(rule, facts, compiledMorphRuleMsas, loaderReasons);
            reviews.Add(review);
            if (review.Eligible) eligible++;
            if (review.Unknown) unknown = true;
        }

        var findings = new List<ParsimonyFinding>();
        long candidatePairs = 0;
        foreach (var theme in reviews.Where(review => review.ExactCandidate)
                     .GroupBy(review => review.ThemeKey, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var distinctPairs = theme.GroupBy(review => PairIdentity(review.Rule), StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal).ToArray();
            candidatePairs += distinctPairs.Length;
            var family = distinctPairs.Length >= 2;
            var members = distinctPairs.SelectMany(group => group).ToArray();
            findings.Add(CreateAdhocSlotOrderFinding(measure, bundleId, theme.Key, members,
                distinctPairs.Length, eligible, family, false, session.GroupedAdhocFactsAvailable));
        }

        foreach (var review in reviews.Where(review => review.AdjacencyQuestion))
        {
            candidatePairs++;
            findings.Add(CreateAdhocSlotOrderFinding(measure, bundleId,
                review.ThemeKey + "/adjacency-question/" + review.Rule.Guid, [review], 1, eligible,
                false, true, session.GroupedAdhocFactsAvailable));
        }

        var details = reviews.Select(review => review.Detail).ToList();
        if (rules.Count == 0) details.Add("No ad hoc prohibitions are present in this facts artifact.");
        if (!session.GroupedAdhocFactsAvailable)
            details.Add("Grouped ad hoc provenance is unavailable; the flat prohibition facts still determine these findings.");
        details.Add("No parser comparison or paired verification was run; template entailment is not a global equivalence proof.");
        var result = findings.OrderBy(item => item.FindingId, StringComparer.Ordinal).ToArray();
        var status = unknown ? ParsimonyMeasureStatus.Inconclusive : ParsimonyMeasureStatus.Computed;
        var detail = details.Count == 0 ? null : string.Join(Environment.NewLine, details);
        return Result(measure, status, eligible, candidatePairs, result, detail,
            unknown ? "the loader state or compiled order of some ad hoc prohibitions is unknown, so their slot order was not checked." : null);
    }

    private static AdhocSlotOrderReview ReviewAdhocSlotOrderRule(AdhocProhibitionFact rule,
        TemplateSequenceMeasureFacts facts, IReadOnlySet<string> compiledMorphRuleMsas,
        IReadOnlyDictionary<string, string> loaderReasons)
    {
        if (rule.Disabled)
            return Excluded(rule, "disabled prohibition is outside the measured catalog");
        if (rule.Kind != "morpheme")
            return Excluded(rule, "allomorph prohibition remains visible and is outside the measured catalog");
        if (rule.Loaded is null)
            return Excluded(rule, "final loader state is unknown", unknown: true);
        if (rule.Loaded == false)
        {
            var reason = loaderReasons.GetValueOrDefault(rule.Guid);
            return Excluded(rule, reason is null
                ? "final loader state excludes this prohibition"
                : $"final loader state excludes this prohibition ({reason})");
        }
        if (rule.PrimaryTargetKind != "msa" || rule.Others.Any(item => item.Kind != "msa"))
            return Excluded(rule, "targets are not all MSA identities");
        if (rule.Others.Count != 1)
            return Excluded(rule,
                $"multi-target conjunctive prohibition has {rule.Others.Count} other targets and stays intact");

        var direction = rule.Adjacency switch
        {
            "somewhereToLeft" => AdhocOrderDirection.Left,
            "adjacentToLeft" => AdhocOrderDirection.Left,
            "somewhereToRight" => AdhocOrderDirection.Right,
            "adjacentToRight" => AdhocOrderDirection.Right,
            "anywhere" => AdhocOrderDirection.None,
            _ => AdhocOrderDirection.None,
        };
        if (direction == AdhocOrderDirection.None)
            return Excluded(rule, rule.Adjacency == "anywhere"
                ? "Anywhere mode has no direction to replace with template order"
                : $"unsupported adjacency mode '{rule.Adjacency}'");

        var other = rule.Others[0];
        if (!facts.MsaKinds.TryGetValue(rule.PrimaryGuid, out var primaryKind) || primaryKind != "inflectional")
            return Excluded(rule, $"cross-root target {rule.PrimaryGuid} is not an inflectional MSA");
        if (!facts.MsaKinds.TryGetValue(other.Guid, out var otherKind) || otherKind != "inflectional")
            return Excluded(rule, $"cross-root target {other.Guid} is not an inflectional MSA");
        if (!compiledMorphRuleMsas.Contains(rule.PrimaryGuid) || !compiledMorphRuleMsas.Contains(other.Guid))
            return Excluded(rule, "one or both targets lack a compiled morphRule mapping");

        var commonCategories = facts.MsaCategories.GetValueOrDefault(rule.PrimaryGuid, [])
            .Intersect(facts.MsaCategories.GetValueOrDefault(other.Guid, []), StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
        if (commonCategories.Length == 0)
            return Excluded(rule, "cross-root targets have no common part-of-speech category");

        var placements = new List<AdhocSlotOrderPlacement>();
        var admittedProhibitedOrder = new SortedSet<string>(StringComparer.Ordinal);
        var incompleteOrder = false;
        foreach (var category in commonCategories)
        {
            var rootCategories = DescendantCategories(facts.CategoryParents, category, out var validHierarchy);
            if (!validHierarchy)
            {
                return Excluded(rule, "category hierarchy is incomplete while checking applicable templates", unknown: true);
            }
            foreach (var rootCategory in rootCategories)
            {
                if (!TryGetApplicableTemplates(facts, rootCategory, out var templates))
                    return Excluded(rule, "category hierarchy is incomplete while checking applicable templates", unknown: true);
                foreach (var applicable in templates)
                {
                    var rawTemplate = facts.Templates.First(item => item.Guid == applicable.Template.Guid);
                    foreach (var side in new[] { "prefix", "suffix" })
                    {
                        var rawSideSlots = rawTemplate.Slots.Where(slot => slot.Side == side).ToArray();
                        if (!rawSideSlots.Any(slot => slot.MsaGuids.Contains(rule.PrimaryGuid)) ||
                            !rawSideSlots.Any(slot => slot.MsaGuids.Contains(other.Guid))) continue;
                        var targetSlots = rawSideSlots.Where(slot => slot.MsaGuids.Contains(rule.PrimaryGuid) ||
                            slot.MsaGuids.Contains(other.Guid)).ToArray();
                        if (targetSlots.Any(slot => !slot.CompiledOrder.HasValue))
                        {
                            incompleteOrder = true;
                            continue;
                        }

                        var slots = applicable.Slots.Where(slot => slot.Side == side).ToArray();
                        var allowedOccurrences = direction == AdhocOrderDirection.Left
                            ? new[] { new TemplateEmbeddingOccurrence(0, rule.PrimaryGuid, side),
                                new TemplateEmbeddingOccurrence(1, other.Guid, side) }
                            : new[] { new TemplateEmbeddingOccurrence(0, other.Guid, side),
                                new TemplateEmbeddingOccurrence(1, rule.PrimaryGuid, side) };
                        var prohibitedOccurrences = direction == AdhocOrderDirection.Left
                            ? new[] { new TemplateEmbeddingOccurrence(0, other.Guid, side),
                                new TemplateEmbeddingOccurrence(1, rule.PrimaryGuid, side) }
                            : new[] { new TemplateEmbeddingOccurrence(0, rule.PrimaryGuid, side),
                                new TemplateEmbeddingOccurrence(1, other.Guid, side) };
                        var allowed = TemplateSequenceEmbedding.Enumerate(allowedOccurrences, slots);
                        var prohibited = TemplateSequenceEmbedding.Enumerate(prohibitedOccurrences, slots);
                        if (!allowed.Complete || !prohibited.Complete)
                        {
                            incompleteOrder = true;
                            continue;
                        }
                        if (allowed.Embeddings.Count == 0 && prohibited.Embeddings.Count == 0) continue;
                        if (prohibited.Embeddings.Count > 0)
                            admittedProhibitedOrder.Add(applicable.Template.Guid);
                        var placementEmbeddings = allowed.Embeddings.Count > 0
                            ? allowed.Embeddings
                            : prohibited.Embeddings;
                        foreach (var embedding in placementEmbeddings)
                        {
                            var assignment = embedding.ToDictionary(item => item.MorphOrdinal, item => item.SlotGuid);
                            var allowedOrder = allowed.Embeddings.Count > 0;
                            var otherSlot = allowedOrder
                                ? direction == AdhocOrderDirection.Left ? assignment[1] : assignment[0]
                                : direction == AdhocOrderDirection.Left ? assignment[0] : assignment[1];
                            var primarySlot = allowedOrder
                                ? direction == AdhocOrderDirection.Left ? assignment[0] : assignment[1]
                                : direction == AdhocOrderDirection.Left ? assignment[1] : assignment[0];
                            placements.Add(new AdhocSlotOrderPlacement(category, rootCategory,
                                applicable.Template.Guid, side, otherSlot, primarySlot,
                                allowedOrder ? direction == AdhocOrderDirection.Right :
                                    direction == AdhocOrderDirection.Left));
                        }
                    }

                    var orderedSlots = applicable.Slots;
                    if (!rule.Adjacency.StartsWith("adjacent", StringComparison.Ordinal))
                    {
                        var rawPrimarySlots = rawTemplate.Slots.Where(slot =>
                            slot.MsaGuids.Contains(rule.PrimaryGuid)).ToArray();
                        var rawOtherSlots = rawTemplate.Slots.Where(slot =>
                            slot.MsaGuids.Contains(other.Guid)).ToArray();
                        if (rawPrimarySlots.Any(primary => rawOtherSlots.Any(otherSlot =>
                                primary.Side != otherSlot.Side &&
                                (!primary.CompiledOrder.HasValue || !otherSlot.CompiledOrder.HasValue))))
                            incompleteOrder = true;

                        var primarySlots = orderedSlots.Select((slot, index) => (slot, index))
                            .Where(item => item.slot.MsaGuids.Contains(rule.PrimaryGuid)).ToArray();
                        var otherSlots = orderedSlots.Select((slot, index) => (slot, index))
                            .Where(item => item.slot.MsaGuids.Contains(other.Guid)).ToArray();
                        foreach (var primarySlot in primarySlots)
                        foreach (var otherSlot in otherSlots)
                        {
                            if (primarySlot.index == otherSlot.index ||
                                primarySlot.slot.Side == otherSlot.slot.Side) continue;
                            var otherBeforePrimary = otherSlot.index < primarySlot.index;
                            var prohibitedOrder = direction == AdhocOrderDirection.Left
                                ? otherBeforePrimary
                                : !otherBeforePrimary;
                            if (prohibitedOrder)
                                admittedProhibitedOrder.Add(applicable.Template.Guid);

                            placements.Add(new AdhocSlotOrderPlacement(category, rootCategory,
                                applicable.Template.Guid, primarySlot.slot.Side + "/" + otherSlot.slot.Side,
                                otherSlot.slot.Guid, primarySlot.slot.Guid, otherBeforePrimary));
                        }
                    }
                }
            }
        }

        if (incompleteOrder)
            return Excluded(rule, "one or more target placements have no complete compiled order", unknown: true);
        if (placements.Count == 0)
            return Excluded(rule, "no active applicable template has both targets in ordered placements");

        var themeKey = PlacementTheme(placements);
        if (admittedProhibitedOrder.Count > 0)
            return new AdhocSlotOrderReview(rule, true, false, false, false, false, themeKey,
                placements, [], [],
                $"Ad hoc rule {rule.Guid}: eligible; not entailed because applicable template(s) " +
                $"{string.Join(", ", admittedProhibitedOrder)} allow the prohibited order.");

        var relevantAnalyses = facts.Analyses.Where(item =>
                item.RootCategoryGuids.Any(root => commonCategories.Any(category =>
                    IsDescendantCategory(facts.CategoryParents, category, root))) &&
                item.Evidence.Morphs.Any(morph => morph.MsaGuid == rule.PrimaryGuid) &&
                item.Evidence.Morphs.Any(morph => morph.MsaGuid == other.Guid))
            .Select(item => item.Evidence).OrderBy(item => item.AnalysisGuid, StringComparer.Ordinal).ToArray();
        var counterexamples = relevantAnalyses.Where(analysis =>
                SequenceSatisfiesDirection(analysis, rule.PrimaryGuid, other.Guid, rule.Adjacency))
            .ToArray();
        if (counterexamples.Length > 0)
        {
            var ids = string.Join(", ", counterexamples.Select(item => item.AnalysisGuid));
            return new AdhocSlotOrderReview(rule, true, false, false, true, false, themeKey,
                placements, relevantAnalyses, counterexamples,
                $"Ad hoc rule {rule.Guid}: eligible; Approved counterexample(s) {ids} violate {rule.Adjacency}; " +
                "the order replacement is blocked.");
        }

        var adjacent = rule.Adjacency.StartsWith("adjacent", StringComparison.Ordinal);
        var detail = adjacent
            ? $"Ad hoc rule {rule.Guid}: eligible; template precedence excludes the prohibited direction in " +
              $"{placements.Select(item => item.TemplateGuid).Distinct(StringComparer.Ordinal).Count()} applicable " +
              $"placement(s), while {rule.Adjacency} prohibits only the adjacent sequence; ask whether the broader " +
              "order restriction is intended and verify with a Trial."
            : $"Ad hoc rule {rule.Guid}: eligible; every applicable placement excludes the prohibited direction; " +
              "no Approved counterexample was found.";
        return new AdhocSlotOrderReview(rule, true, !adjacent, adjacent, false, false, themeKey,
            placements, relevantAnalyses, [], detail);
    }

    private static ParsimonyFinding CreateAdhocSlotOrderFinding(MeasureDefinition measure, string bundleId,
        string themeKey, IReadOnlyList<AdhocSlotOrderReview> members, long pairCount, long eligible,
        bool family, bool adjacencyQuestion, bool groupedFactsAvailable)
    {
        var rules = members.Select(item => item.Rule).DistinctBy(item => item.Guid)
            .OrderBy(item => item.Guid, StringComparer.Ordinal).ToArray();
        var placements = members.SelectMany(item => item.Placements)
            .Distinct().OrderBy(item => item.TemplateGuid, StringComparer.Ordinal)
            .ThenBy(item => item.RootCategoryGuid, StringComparer.Ordinal)
            .ThenBy(item => item.Side, StringComparer.Ordinal).ThenBy(item => item.OtherSlotGuid, StringComparer.Ordinal)
            .ThenBy(item => item.PrimarySlotGuid, StringComparer.Ordinal).ToArray();
        var analyses = members.SelectMany(item => item.RelevantAnalyses)
            .DistinctBy(item => item.AnalysisGuid).OrderBy(item => item.AnalysisGuid, StringComparer.Ordinal).ToArray();
        var evidence = JsonSerializer.Serialize(new
        {
            themeKey,
            family,
            adjacencyQuestion,
            rules = rules.Select(item => new
            {
                item.Guid,
                item.Adjacency,
                primary = item.PrimaryGuid,
                other = item.Others.Single().Guid,
            }),
            placements,
            approvedSequences = analyses.Select(item => item.AnalysisGuid),
            policy = "adhoc-slot-order/v1",
        });
        var references = rules.Select(item => EvidenceReference(bundleId, "adhoc-context",
                new { objectGuid = item.Guid })).ToList();
        foreach (var templateGuid in placements.Select(item => item.TemplateGuid).Distinct(StringComparer.Ordinal))
            references.Add(EvidenceReference(bundleId, "template-order", new { objectGuid = templateGuid }));
        foreach (var analysis in analyses)
            references.Add(EvidenceReference(bundleId, "approved-morph-sequences",
                new { objectGuid = analysis.AnalysisGuid }));

        var limitations = new List<string>();
        if (family)
            limitations.Add("Two or more distinct MSA pairs share this category, side, and ordered slot theme.");
        else if (adjacencyQuestion)
            limitations.Add("Template order excludes the whole direction, while this rule excludes only adjacent co-occurrence. Ask whether the broader restriction is intended and verify with a Trial before disabling the prohibition.");
        else
            limitations.Add("One already-entailed prohibition is an individual cleanup candidate.");
        limitations.Add("The check covers compiled same-side template placements and the available Approved sequences only.");
        limitations.Add("No parser Trial was run, and this finding does not establish global equivalence.");
        if (!groupedFactsAvailable)
            limitations.Add("Grouped ad hoc provenance is unavailable; the finding uses flat prohibition facts only.");

        var identity = "adhoc-slot-order/" + themeKey;
        var findingId = $"{measure.Id}:{(adjacencyQuestion ? "adjacency:" : "theme:")}{Digest(themeKey)}";
        return new ParsimonyFinding(findingId, measure.Id, measure.Axis, measure.Tier,
            new ParsimonyFindingAttachment(ParsimonyAttachmentKind.Group, identity,
                GroupKind: ParsimonyGroupKind.AdhocSlotOrder), themeKey,
            new ParsimonyMeasureNumber(pairCount, eligible, measure.Unit), measure.Threshold!, Digest(evidence),
            references, measure.RecipeLink, limitations, ParsimonyVerification.NotRun);
    }

    private static string PlacementTheme(IEnumerable<AdhocSlotOrderPlacement> placements)
    {
        var ordered = placements.Select(item =>
                $"{item.CategoryGuid}/{item.RootCategoryGuid}/{item.TemplateGuid}/{item.Side}/" +
                $"{(item.OtherBeforePrimary ? "other-before-primary" : "primary-before-other")}/" +
                $"{item.OtherSlotGuid}/{item.PrimarySlotGuid}")
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        return string.Join("/", ordered);
    }

    private static IReadOnlyList<string> DescendantCategories(
        IReadOnlyDictionary<string, string?> parents, string categoryGuid, out bool valid)
    {
        valid = true;
        var result = new List<string>();
        foreach (var candidate in parents.Keys.Order(StringComparer.Ordinal))
        {
            if (IsDescendantCategory(parents, categoryGuid, candidate, out var candidateValid)) result.Add(candidate);
            if (!candidateValid) valid = false;
        }
        return result;
    }

    private static bool IsDescendantCategory(IReadOnlyDictionary<string, string?> parents,
        string ancestorGuid, string categoryGuid) =>
        IsDescendantCategory(parents, ancestorGuid, categoryGuid, out _);

    private static bool IsDescendantCategory(IReadOnlyDictionary<string, string?> parents,
        string ancestorGuid, string categoryGuid, out bool valid)
    {
        valid = true;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var current = categoryGuid;
        while (true)
        {
            if (!parents.TryGetValue(current, out var parent) || !visited.Add(current))
            {
                valid = false;
                return false;
            }
            if (current == ancestorGuid) return true;
            if (parent is null) return false;
            current = parent;
        }
    }

    private static bool SequenceSatisfiesDirection(ParsimonyApprovedMorphSequenceViewRow analysis,
        string primaryGuid, string otherGuid, string adjacency)
    {
        var sequence = analysis.Morphs.OrderBy(morph => morph.Ordinal).ToArray();
        var primaryPositions = sequence.Select((morph, index) => (morph, index))
            .Where(item => item.morph.MsaGuid == primaryGuid).Select(item => item.index).ToArray();
        var otherPositions = sequence.Select((morph, index) => (morph, index))
            .Where(item => item.morph.MsaGuid == otherGuid).Select(item => item.index).ToArray();
        if (primaryPositions.Length == 0 || otherPositions.Length == 0) return false;
        return adjacency switch
        {
            "somewhereToLeft" => otherPositions.Any(index => index < primaryPositions[0]),
            "somewhereToRight" => otherPositions.Any(index => index > primaryPositions[^1]),
            "adjacentToLeft" => primaryPositions[0] > 0 &&
                otherPositions.FirstOrDefault(index => index < primaryPositions[0], -1) ==
                primaryPositions[0] - 1,
            "adjacentToRight" => primaryPositions[^1] + 1 < sequence.Length &&
                otherPositions.LastOrDefault(index => index > primaryPositions[^1], -1) ==
                primaryPositions[^1] + 1,
            _ => false,
        };
    }

    private static string PairIdentity(AdhocProhibitionFact rule) =>
        $"{rule.PrimaryGuid}\u001f{rule.Others[0].Guid}";

    private static AdhocSlotOrderReview Excluded(AdhocProhibitionFact rule, string reason, bool unknown = false) =>
        new(rule, false, false, false, false, unknown, string.Empty, [], [], [],
            $"Ad hoc rule {rule.Guid}: ineligible; {reason}.");

    private sealed record AdhocSlotOrderReview(AdhocProhibitionFact Rule, bool Eligible,
        bool ExactCandidate, bool AdjacencyQuestion, bool HasCounterexample, bool Unknown,
        string ThemeKey, IReadOnlyList<AdhocSlotOrderPlacement> Placements,
        IReadOnlyList<ParsimonyApprovedMorphSequenceViewRow> RelevantAnalyses,
        IReadOnlyList<ParsimonyApprovedMorphSequenceViewRow> Counterexamples, string Detail);

    private sealed record AdhocSlotOrderPlacement(string CategoryGuid, string RootCategoryGuid,
        string TemplateGuid, string Side, string OtherSlotGuid, string PrimarySlotGuid, bool OtherBeforePrimary);

    private enum AdhocOrderDirection
    {
        None,
        Left,
        Right,
    }
}
