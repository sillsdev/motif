using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Host.Parsimony;

/// <summary>Runs fixed Parsimony queries registered by their versioned query IDs.</summary>
public static partial class MeasureRunner
{
    private static readonly IReadOnlyDictionary<string, Func<MeasureDefinition, ParsimonyQuerySession, string,
        ParsimonyMeasureResult>> QueryRunners = new Dictionary<string, Func<MeasureDefinition, ParsimonyQuerySession,
        string, ParsimonyMeasureResult>>(StringComparer.Ordinal)
    {
        ["adhoc-duplicate/v1"] = RunDuplicateProhibitions,
        ["allomorph-duplicate-form/v1"] = RunDuplicateAllomorphs,
        ["allo-alternation-family/v1"] = RunAlternationFamilies,
        ["statement-unused/v1"] = RunUnusedStatements,
        ["template-precedence/v1"] = RunTemplatePrecedence,
        ["slot-blocking/v1"] = RunSlotBlocking,
        ["adhoc-slot-order/v1"] = RunAdhocSlotOrder,
        ["allo-unconditioned/v1"] = RunUnconditionedAllomorphs,
        ["negative-accepted/v1"] = RunReviewedNegativeAccepted,
        ["disapproved-produced/v1"] = RunDisapprovedProduced,
        ["unslotted-affix/v1"] = RunUnslottedAffixes,
        ["null-optional/v1"] = RunNullOptionalAffixes,
        ["environment-excess/v1"] = RunEnvironmentExcess,
        ["natural-class-excess/v1"] = RunNaturalClassExcess,
    };

    /// <summary>Whether a measure has a fixed query implementation in this build.</summary>
    public static bool Supports(string measureId) => MeasureCatalog.Find(measureId)?.QueryId is { } queryId &&
        QueryRunners.ContainsKey(queryId);

    /// <summary>Runs one fixed query and returns its full-input counts with the matching findings.</summary>
    public static ParsimonyMeasureResult Execute(string measureId, ParsimonyQuerySession session, string bundleId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(measureId);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(bundleId);
        if (!StringComparer.Ordinal.Equals(bundleId, session.BundleId))
            throw new ArgumentException("The measure bundle differs from the attached artifacts.", nameof(bundleId));
        var measure = MeasureCatalog.Find(measureId)
            ?? throw new ArgumentException($"Unknown Parsimony measure '{measureId}'.", nameof(measureId));
        if (measure.QueryId is null || !QueryRunners.TryGetValue(measure.QueryId, out var runner))
            return Unavailable(measure, "No fixed query is registered for this measure in this build.",
                "this check is not available in this build of Motif.");
        try
        {
            return runner(measure, session, bundleId);
        }
        catch (ParsimonyQueryUnavailableException exception)
        {
            return Unavailable(measure, $"Required facts sections are unavailable: {string.Join(", ", exception.Sections)}.",
                ParsimonyNotes.MissingFactsReason(exception.Sections));
        }
        catch (ParsimonyScopeUnavailableException exception)
        {
            return new ParsimonyMeasureResult(new ParsimonyMeasureRun(measure.Id, ParsimonyMeasureStatus.Inconclusive,
                null, null, measure.Unit, null, null, exception.Message), [], exception.Message);
        }
    }

    /// <summary>Returns only the findings for callers that do not need the measure-run envelope.</summary>
    public static IReadOnlyList<ParsimonyFinding> Run(string measureId, ParsimonyQuerySession session, string bundleId) =>
        Execute(measureId, session, bundleId).Findings;

    private static ParsimonyMeasureResult RunDuplicateProhibitions(MeasureDefinition measure,
        ParsimonyQuerySession session, string bundleId)
    {
        var facts = session.ReadAdhocProhibitions();
        var eligible = facts.Where(item => !item.Disabled && item.Loaded == true).ToArray();
        var limitations = new List<string>();
        if (!session.GroupedAdhocFactsAvailable)
            limitations.Add("Grouped ad hoc provenance is unavailable in this facts artifact.");
        var unknown = facts.Count(item => !item.Disabled && item.Loaded is null);
        if (unknown > 0)
            limitations.Add($"Loader state is unknown for {unknown} enabled prohibition(s); they were excluded.");
        limitations.Add("No parser comparison or paired verification was run.");
        var findings = new List<ParsimonyFinding>();
        foreach (var group in eligible.GroupBy(Signature, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var members = group.OrderBy(item => item.Guid, StringComparer.Ordinal).ToArray();
            var groupKey = "adhoc-signature/" + Digest(group.Key);
            var retained = members[0];
            foreach (var duplicate in members.Skip(1))
            {
                var evidence = JsonSerializer.Serialize(new
                {
                    signature = group.Key,
                    retained = retained.Guid,
                    member = duplicate.Guid,
                    members = members.Select(item => item.Guid),
                });
                findings.Add(new ParsimonyFinding(
                    $"{measure.Id}:{duplicate.Guid}", measure.Id, measure.Axis, measure.Tier,
                    new ParsimonyFindingAttachment(measure.AttachesTo, duplicate.Guid, measure.AuthoredObjectKind),
                    groupKey, new ParsimonyMeasureNumber(members.Length - 1, members.Length, measure.Unit),
                    measure.Threshold!,
                    Digest(evidence),
                    [EvidenceReference(bundleId, "adhoc-context", new { objectGuid = duplicate.Guid }),
                     EvidenceReference(bundleId, "adhoc-context", new { objectGuid = retained.Guid })],
                    measure.RecipeLink, limitations, ParsimonyVerification.NotRun));
            }
        }
        var result = findings.OrderBy(item => item.FindingId, StringComparer.Ordinal).ToArray();
        var status = unknown == 0 ? ParsimonyMeasureStatus.Computed : ParsimonyMeasureStatus.Inconclusive;
        var reason = status == ParsimonyMeasureStatus.Inconclusive
            ? "Some enabled prohibitions have unknown loader state; they were excluded from the comparison." : null;
        return Result(measure, status, eligible.LongLength, result.LongLength, result, reason,
            status == ParsimonyMeasureStatus.Inconclusive
                ? "the loader state of some enabled prohibitions is unknown, so they were excluded from the comparison."
                : null);
    }

    private static ParsimonyMeasureResult RunDuplicateAllomorphs(MeasureDefinition measure,
        ParsimonyQuerySession session, string bundleId)
    {
        var allomorphs = session.ReadAllomorphsForMeasure();
        var groups = allomorphs.GroupBy(item => new AllomorphSignature(item.EntryGuid, item.MorphType,
                FormsSignature(item.Forms), EnvironmentsSignature(item.Environments)))
            .Where(group => group.Count() > 1)
            .OrderBy(group => group.Key.EntryGuid, StringComparer.Ordinal)
            .ThenBy(group => group.Key.MorphType, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Forms, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Environments, StringComparer.Ordinal)
            .ToArray();
        var findings = new List<ParsimonyFinding>();
        var redundant = 0L;
        foreach (var group in groups)
        {
            var members = group.OrderBy(item => item.Guid, StringComparer.Ordinal).ToArray();
            redundant += members.Length - 1;
            var memberIds = members.Select(item => item.Guid).ToArray();
            // Member GUIDs name the group so a form edit keeps the finding ID.
            var groupKey = $"entry/{group.Key.EntryGuid}/duplicate-forms/{group.Key.MorphType}/" +
                Digest(string.Join("\n", memberIds));
            var digestValue = JsonSerializer.Serialize(new
            {
                group.Key.EntryGuid,
                group.Key.MorphType,
                forms = group.Key.Forms,
                environments = group.Key.Environments,
                members = memberIds,
            });
            var references = members.Select(item => EvidenceReference(bundleId, "allomorph-context",
                new { objectGuid = item.Guid })).ToArray();
            findings.Add(new ParsimonyFinding(
                $"{measure.Id}:{Digest(groupKey)}", measure.Id, measure.Axis, measure.Tier,
                new ParsimonyFindingAttachment(ParsimonyAttachmentKind.Group, groupKey,
                    GroupKind: ParsimonyGroupKind.AllomorphDuplicateForms),
                "entry/" + group.Key.EntryGuid,
                new ParsimonyMeasureNumber(members.Length - 1, members.Length, measure.Unit),
                measure.Threshold!,
                Digest(digestValue), references, measure.RecipeLink,
                ["The listed allomorphs have identical authored environment lists.",
                 "Allomorphs with different environment lists are excluded from this exact-match finding.",
                 "The duplicate GUIDs identify records for inspection; no similarity or linguistic claim is inferred."],
                ParsimonyVerification.NotRun));
        }
        var result = findings.OrderBy(item => item.FindingId, StringComparer.Ordinal).ToArray();
        return Result(measure, ParsimonyMeasureStatus.Computed, allomorphs.Count, redundant, result, null);
    }

    private static ParsimonyMeasureResult RunUnconditionedAllomorphs(MeasureDefinition measure,
        ParsimonyQuerySession session, string bundleId)
    {
        var discovery = session.ReadUnconditionedAllomorphFamilies();
        var findings = new List<ParsimonyFinding>();
        foreach (var family in discovery.Families)
        {
            var maximumOrder = family.Siblings.Max(item => item.EffectiveOrder);
            var familyDescription = DescribeUnconditionedAllomorphFamily(family);
            foreach (var candidate in family.Siblings.Where(item =>
                         item.ConditionState == UnconditionedAllomorphPhoneConditionState.Unconditioned &&
                         item.EffectiveOrder < maximumOrder))
            {
                var evidence = JsonSerializer.Serialize(new
                {
                    family.EntryGuid,
                    family.MsaGuid,
                    family.Bucket,
                    family.Key,
                    candidate = candidate.Allomorph.Guid,
                    siblings = family.Siblings.Select(item => new
                    {
                        allomorph = item.Allomorph.Guid,
                        forms = item.Allomorph.Forms,
                        item.EffectiveOrder,
                        item.IsFinalElsewhereCase,
                        conditionState = item.ConditionState.ToString(),
                        environments = item.Environments.Select(environment => new
                        {
                            environment.EnvironmentGuid,
                            environment.Name,
                            environment.Representation,
                        }),
                    }),
                });
                var references = family.Siblings.Select(item => EvidenceReference(bundleId,
                    "allomorph-context", new { objectGuid = item.Allomorph.Guid })).ToArray();
                var limitations = new List<string>
                {
                    familyDescription,
                    "Final unconditioned siblings are shown as elsewhere controls, not as findings.",
                    "Effective selected contexts are not published in the grammar facts; selected-context counts are not shown.",
                    "No reviewed-negative attribution or paired tightening was assessed; this static finding does not establish harmful acceptance.",
                };
                if (discovery.InvalidConditions > 0)
                    limitations.Add("Invalid phone restrictions were excluded for grammar-health review.");
                findings.Add(new ParsimonyFinding(
                    $"{measure.Id}:{candidate.Allomorph.Guid}:{Digest(family.Key)}", measure.Id, measure.Axis,
                    measure.Tier,
                    new ParsimonyFindingAttachment(measure.AttachesTo, candidate.Allomorph.Guid,
                        measure.AuthoredObjectKind),
                    family.Key,
                    new ParsimonyMeasureNumber(1, family.Siblings.Count, measure.Unit), measure.Threshold!,
                    Digest(evidence), references, measure.RecipeLink, Array.AsReadOnly(limitations.ToArray()),
                    ParsimonyVerification.NotRun));
            }
        }

        var result = findings.OrderBy(item => item.FindingId, StringComparer.Ordinal).ToArray();
        var status = discovery.UnknownFamilies == 0
            ? ParsimonyMeasureStatus.Computed
            : ParsimonyMeasureStatus.Inconclusive;
        var details = new List<string>();
        if (discovery.InvalidConditions > 0)
            details.Add($"Excluded {discovery.InvalidConditions} invalid phone restriction(s) for grammar-health review.");
        if (discovery.UnknownFamilies > 0)
            details.Add($"Excluded {discovery.UnknownFamilies} family/families with incomplete condition or compiler-order facts.");
        details.Add("Effective selected contexts and extra-context counts are not published in the grammar facts.");
        // Selected contexts are never published, so this check always has something to say, even with no findings.
        var noteReason = discovery.UnknownFamilies == 0
            ? "the effective selected contexts of each allomorph are not published in the grammar facts, so selected-context counts were not checked."
            : "the condition or compiler-order facts for some allomorph families are incomplete, so they were not compared.";
        return Result(measure, status, discovery.EligibleAllomorphs, result.LongLength, result,
            string.Join(" ", details), noteReason);
    }

    private static ParsimonyMeasureResult RunUnslottedAffixes(MeasureDefinition measure,
        ParsimonyQuerySession session, string bundleId)
    {
        var rows = session.ReadUnslottedAffixes();
        var denominator = session.CountLoadedInflectionalAffixes();
        var findings = rows.Select(row =>
        {
            var evidence = JsonSerializer.Serialize(row);
            var references = new List<ParsimonyEvidenceReference>
            {
                EvidenceReference(bundleId, "unslotted-affixes", new { objectGuid = row.MsaGuid }),
                EvidenceReference(bundleId, "affix-context", new { objectGuid = row.MsaGuid }),
            };
            references.AddRange(row.ApprovedAnalysisGuids.Select(analysisGuid =>
                EvidenceReference(bundleId, "approved-morph-sequences", new { objectGuid = analysisGuid })));
            var limitations = new List<string>
            {
                DescribeUnslottedEvidence(row),
                "Position and order evidence comes only from rooted Approved analyses.",
                "No slot identity is inferred from distance; direct pair orders are not transitively closed.",
                "A consistent position is a review suggestion, not a slot law or grammar change.",
            };
            limitations.AddRange(row.Exclusions);
            return new ParsimonyFinding(
                $"{measure.Id}:{row.MsaGuid}", measure.Id, measure.Axis, measure.Tier,
                new ParsimonyFindingAttachment(measure.AttachesTo, row.MsaGuid, measure.AuthoredObjectKind),
                null, new ParsimonyMeasureNumber(1, denominator, measure.Unit), measure.Threshold!,
                Digest(evidence), Array.AsReadOnly(references.ToArray()), measure.RecipeLink,
                Array.AsReadOnly(limitations.Distinct(StringComparer.Ordinal).ToArray()),
                ParsimonyVerification.NotRun);
        }).OrderBy(item => item.FindingId, StringComparer.Ordinal).ToArray();
        return Result(measure, ParsimonyMeasureStatus.Computed, denominator, findings.LongLength, findings, null);
    }

    private static string DescribeUnslottedEvidence(ParsimonyUnslottedAffixViewRow row)
    {
        var positions = row.Positions.Count == 0
            ? "no rooted Approved position was observed"
            : "rooted Approved positions were " + string.Join(", ", row.Positions.Select(position =>
                $"{position.Side} {position.SignedDistance:+#;-#;0} ({position.AnalysisCount} analyses, " +
                $"{position.WordTypeCount} word types, {position.StemCount} stems)"));
        var orders = row.Precedence.Select(pair =>
            $"{pair.FirstDescription} precedes {pair.SecondDescription} in {pair.FirstBeforeSecond} Approved " +
            $"analysis case(s), with {pair.SecondBeforeFirst} contrary case(s) and " +
            $"{pair.ExcludedAnalyses} repeated-MSA exclusion(s)" +
            (pair.SuggestPartialOrder ? "; the direct order meets the review support floor" :
                "; the direct order does not meet the review support floor"));
        return $"No affix slot is assigned; {positions}." +
               (row.Precedence.Count == 0 ? string.Empty : " Same-side order: " + string.Join("; ", orders) + ".");
    }

    private static ParsimonyMeasureResult RunNullOptionalAffixes(MeasureDefinition measure,
        ParsimonyQuerySession session, string bundleId)
    {
        var rows = session.ReadNullOptionalAffixes();
        var loadedZeros = rows.Where(row => row.IsLoadedZeroOnly).ToArray();
        var findings = loadedZeros.Select(row =>
        {
            var evidence = JsonSerializer.Serialize(row);
            var references = new List<ParsimonyEvidenceReference>
            {
                EvidenceReference(bundleId, "null-optional", new { objectGuid = row.MsaGuid }),
                EvidenceReference(bundleId, "affix-context", new { objectGuid = row.MsaGuid }),
            };
            references.AddRange(row.Slots.Select(slot =>
                EvidenceReference(bundleId, "slot-context", new { objectGuid = slot.SlotGuid })));
            references.AddRange(row.Realizations.Where(item => item.AllomorphGuid is not null)
                .Select(item => EvidenceReference(bundleId, "allomorph-context",
                    new { objectGuid = item.AllomorphGuid })));
            references.AddRange(row.ApprovedAnalysisGuids.Select(analysisGuid =>
                EvidenceReference(bundleId, "approved-morph-sequences", new { objectGuid = analysisGuid })));
            var limitations = new List<string>
            {
                row.Classification switch
                {
                    "optional-slot-candidate" => row.IsRemovableCandidate
                        ? "The zero is loaded in an optional slot and is an unreferenced, featureless cleanup candidate."
                        : "The zero is loaded in an optional slot; inspect its contributions and references before any cleanup.",
                    "obligatory-slot-alternative" => "A zero in a required slot represents an absence alternative, not optionality.",
                    _ => "The loaded zero's slot optionality is unavailable or mixed; do not treat it as redundant optionality.",
                },
                "Meaningful feature-bearing or referenced zero morphology must be kept or asked about.",
                "Parser comparison was not run.",
            };
            limitations.AddRange(row.Exclusions);
            return new ParsimonyFinding(
                $"{measure.Id}:{row.MsaGuid}", measure.Id, measure.Axis, measure.Tier,
                new ParsimonyFindingAttachment(measure.AttachesTo, row.MsaGuid, measure.AuthoredObjectKind),
                null, new ParsimonyMeasureNumber(1, loadedZeros.LongLength, measure.Unit), measure.Threshold!,
                Digest(evidence), Array.AsReadOnly(references.ToArray()), measure.RecipeLink,
                Array.AsReadOnly(limitations.Distinct(StringComparer.Ordinal).ToArray()),
                ParsimonyVerification.NotRun);
        }).OrderBy(item => item.FindingId, StringComparer.Ordinal).ToArray();
        return Result(measure, ParsimonyMeasureStatus.Computed, loadedZeros.LongLength, findings.LongLength,
            findings, null);
    }

    private static string DescribeUnconditionedAllomorphFamily(UnconditionedAllomorphFamily family)
    {
        var maximumOrder = family.Siblings.Max(item => item.EffectiveOrder);
        var siblings = family.Siblings.OrderBy(item => item.EffectiveOrder)
            .Select(item =>
            {
                var form = DescribeAllomorphForms(item.Allomorph);
                var condition = item.ConditionState == UnconditionedAllomorphPhoneConditionState.Unconditioned
                    ? item.IsFinalElsewhereCase
                        ? "final elsewhere control"
                        : "unconditioned candidate"
                    : "conditioned by phone environment " + string.Join(" OR ", item.Environments.Select(environment =>
                        DescribeUnconditionedAllomorphEnvironment(environment)));
                return $"{item.EffectiveOrder}: {form} — {condition}";
            });
        return $"Compiler order in the {family.Bucket} bucket (0 to {maximumOrder}): " +
               string.Join("; ", siblings) + ".";
    }

    private static string DescribeUnconditionedAllomorphEnvironment(UnconditionedAllomorphPhoneEnvironmentFact environment)
    {
        var name = string.IsNullOrWhiteSpace(environment.Name) ? null : $"\"{environment.Name}\" ";
        var representation = environment.Representation ?? "representation unavailable";
        return name is null ? $"({representation})" : $"{name}({representation})";
    }

    private static string DescribeAllomorphForms(AllomorphMeasureFact allomorph)
    {
        var forms = allomorph.Forms.SelectMany(pair => pair.Value.Select(form =>
            $"\"{form}\" ({pair.Key})")).ToArray();
        return forms.Length == 0 ? $"allomorph {allomorph.Guid}" : string.Join(" / ", forms);
    }

    private static ParsimonyMeasureResult RunUnusedStatements(MeasureDefinition measure,
        ParsimonyQuerySession session, string bundleId)
    {
        var statements = session.ReadStatementUsage();
        var unused = statements.Where(item => item.Classification is "defined-but-unreferenced"
            or "referenced-but-not-applied").ToArray();
        var findings = unused.Select(item => new ParsimonyFinding(
            $"{measure.Id}:{item.StatementKind}:{item.StatementGuid}", measure.Id, measure.Axis, measure.Tier,
            new ParsimonyFindingAttachment(ParsimonyAttachmentKind.Group,
                $"statement/{item.StatementKind}/{item.StatementGuid}", GroupKind: ParsimonyGroupKind.UnusedStatement),
            $"statement/{item.StatementKind}", new ParsimonyMeasureNumber(1, statements.Count, measure.Unit),
            measure.Threshold!,
            Digest(JsonSerializer.Serialize(item)),
            [EvidenceReference(bundleId, "statement-usage", new
            {
                statementKind = item.StatementKind,
                objectGuid = item.StatementGuid,
            })],
            measure.RecipeLink,
            ["Only fact-table references and compiler load facts were considered; Text content was not used.",
                .. ReferrerNotes(session, item)],
            ParsimonyVerification.NotRun)).OrderBy(item => item.FindingId, StringComparer.Ordinal).ToArray();
        return Result(measure, ParsimonyMeasureStatus.Computed, statements.Count, unused.LongLength, findings, null);
    }

    // Naming the referrers shows which reference the compiler did not apply, so a reader can check it.
    private static string[] ReferrerNotes(ParsimonyQuerySession session, ParsimonyStatementUsageViewRow item)
    {
        var referrers = session.ReadStatementReferrers(item.StatementKind, item.StatementGuid);
        if (referrers.Count == 0) return ["No grammar object references this statement."];
        var named = referrers.Select(referrer => $"{referrer.ReferrerKind} {referrer.ReferrerGuid} " +
            $"({referrer.ParserEffect})");
        return [$"Referenced only by {string.Join(", ", named)}; none of these applies it to parsing."];
    }

    private static ParsimonyMeasureResult RunDisapprovedProduced(MeasureDefinition measure,
        ParsimonyQuerySession session, string bundleId)
    {
        var facts = session.ReadDisapprovedProducedFacts();
        if (!facts.Requested)
            return Unavailable(measure, facts.Detail ?? ParsimonyNotes.NoAssessmentReason,
                facts.Detail ?? ParsimonyNotes.NoAssessmentReason);
        if (!facts.Available)
            return Unavailable(measure, facts.Detail ?? "no parser cases finished, so there is nothing to compare.",
                facts.Detail ?? "no parser cases finished, so there is nothing to compare.");

        var findings = facts.Matches.Select(item =>
        {
            var evidence = JsonSerializer.Serialize(new
            {
                wordformGuid = item.WordformGuid,
                item.Signature,
                disapprovedAnalysisGuids = JsonSerializer.Deserialize<string[]>(item.AnalysisGuidsJson),
                completion = "complete",
                comparison = "ADR 0027 ordered Form/MSA/inflection-type identity",
            });
            return new ParsimonyFinding(
                $"{measure.Id}:{item.WordformGuid}:{item.Signature}", measure.Id, measure.Axis, measure.Tier,
                new ParsimonyFindingAttachment(ParsimonyAttachmentKind.WordCase, item.WordformGuid),
                "wordform/" + item.WordformGuid,
                new ParsimonyMeasureNumber(1, facts.EligibleMorphologies, measure.Unit), measure.Threshold!,
                Digest(evidence),
                [EvidenceReference(bundleId, "parser-cases", new { caseKey = item.CaseKey })],
                measure.RecipeLink,
                ["A completed parser search produced the same morphology as a Disapproved analysis.",
                 "Other readings, including Approved and Unknown readings, remain separate.",
                 "The finding does not say that the whole word is impossible."],
                ParsimonyVerification.NotRun);
        }).OrderBy(item => item.FindingId, StringComparer.Ordinal).ToArray();
        var status = facts.ExcludedEvidence == 0 ? ParsimonyMeasureStatus.Computed
            : ParsimonyMeasureStatus.Inconclusive;
        return Result(measure, status, facts.EligibleMorphologies, facts.MatchedMorphologies, findings,
            facts.Detail, status == ParsimonyMeasureStatus.Inconclusive ? facts.Detail : null);
    }

    private static ParsimonyMeasureResult RunReviewedNegativeAccepted(MeasureDefinition measure,
        ParsimonyQuerySession session, string bundleId)
    {
        var facts = session.ReadReviewedNegativeAcceptedFacts();
        if (!facts.Requested)
            return Unavailable(measure, facts.Detail ?? ParsimonyNotes.NoAssessmentReason,
                facts.Detail ?? ParsimonyNotes.NoAssessmentReason);
        if (!facts.Available)
            return Unavailable(measure, facts.Detail ?? "no reviewed negative has a completed, exact parser case, so there is nothing to compare.",
                facts.Detail ?? "no reviewed negative has a completed, exact parser case, so there is nothing to compare.");

        var findings = facts.AcceptedCases.Select(item =>
        {
            var evidence = JsonSerializer.Serialize(new
            {
                item.CaseId,
                item.RevisionId,
                item.ContentDigest,
                item.WritingSystem,
                item.FormNfd,
                acceptedParserAnalysisOrdinals = JsonSerializer.Deserialize<int[]>(
                    item.AcceptedAnalysisOrdinalsJson),
                completion = "complete",
            });
            return new ParsimonyFinding(
                $"{measure.Id}:{item.CaseId}:{item.RevisionId}", measure.Id, measure.Axis, measure.Tier,
                new ParsimonyFindingAttachment(ParsimonyAttachmentKind.WordCase, item.CaseId),
                "reviewed-negative/" + item.CaseId,
                new ParsimonyMeasureNumber(1, facts.EligibleNegatives, measure.Unit), measure.Threshold!,
                Digest(evidence),
                [EvidenceReference(bundleId, "parser-cases", new { caseKey = item.CaseKey })],
                measure.RecipeLink,
                ["A completed parser search accepted this human-confirmed negative case.",
                 "The finding is attributed to the exact Notebook revision and parser case."],
                ParsimonyVerification.NotRun);
        }).OrderBy(item => item.FindingId, StringComparer.Ordinal).ToArray();
        var status = facts.ExcludedNegatives == 0 ? ParsimonyMeasureStatus.Computed
            : ParsimonyMeasureStatus.Inconclusive;
        return Result(measure, status, facts.EligibleNegatives, facts.AcceptedNegatives, findings,
            facts.Detail, status == ParsimonyMeasureStatus.Inconclusive ? facts.Detail : null);
    }

    private static ParsimonyMeasureResult RunAlternationFamilies(MeasureDefinition measure,
        ParsimonyQuerySession session, string bundleId)
    {
        var discovery = session.ReadAlternationFamilies();
        var limitations = discovery.Limitations.Concat(
        [
            "This static evidence does not establish that a sound change is productive.",
            "Text and parser witness counts were unavailable for this static measure run.",
            "No grammar change or allomorph retirement was proposed by this query.",
        ]).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var denominator = discovery.EligibleMorphemes;
        var findings = discovery.Families.Select(family =>
        {
            var evidence = JsonSerializer.Serialize(new
            {
                family.Key,
                family.ChangeKey,
                family.Lane,
                family.Directed,
                family.ContextKey,
                family.GateKey,
                family.InputPhonemeGuids,
                family.OutputPhonemeGuids,
                family.ChangedFeatures,
                family.SharedFeatures,
                family.Members,
            });
            var evidenceRefs = new List<ParsimonyEvidenceReference>
            {
                EvidenceReference(bundleId, "alternation-families", new { }),
            };
            evidenceRefs.AddRange(family.Members.SelectMany(member => member.AllomorphGuids)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
                .Select(guid => EvidenceReference(bundleId, "allomorph-context", new { objectGuid = guid })));
            return new ParsimonyFinding(
                $"{measure.Id}:{Digest(family.Key)}", measure.Id, measure.Axis, measure.Tier,
                new ParsimonyFindingAttachment(ParsimonyAttachmentKind.Group, family.Key,
                    GroupKind: ParsimonyGroupKind.AlternationFamily),
                family.Key, new ParsimonyMeasureNumber(family.Members.Count, denominator, measure.Unit),
                measure.Threshold!, Digest(evidence), Array.AsReadOnly(evidenceRefs.ToArray()),
                measure.RecipeLink, Array.AsReadOnly(limitations), ParsimonyVerification.NotRun);
        }).OrderBy(item => item.FindingId, StringComparer.Ordinal).ToArray();
        var participatingMorphemes = discovery.Families.SelectMany(item => item.Members)
            .Select(item => item.EntryGuid).Distinct(StringComparer.Ordinal).LongCount();
        var status = discovery.Inconclusive ? ParsimonyMeasureStatus.Inconclusive : ParsimonyMeasureStatus.Computed;
        var detail = discovery.Inconclusive ? string.Join(" ", discovery.Limitations) : null;
        return Result(measure, status, denominator, participatingMorphemes, findings, detail,
            discovery.Inconclusive
                ? "some allomorph conditions, forms, or loaded rules could not be fully determined from the available evidence, so they were not compared."
                : null);
    }

    private static ParsimonyMeasureResult RunTemplatePrecedence(MeasureDefinition measure,
        ParsimonyQuerySession session, string bundleId)
    {
        var facts = session.ReadTemplateSequenceFacts();
        var findings = new List<ParsimonyFinding>();
        var pairCounts = new Dictionary<AffixPairKey, PairOrderCount>();
        long eligible = 0;
        long contradicted = 0;
        var abstained = new HashSet<string>(StringComparer.Ordinal);
        var capCases = 0;
        var chainCases = 0;
        var unsupportedCases = 0;

        foreach (var item in facts.Analyses)
        {
            if (!TryGetSupportedAnalysis(item, out var analysis))
            {
                if (HasPotentialTemplateMorphology(item))
                {
                    abstained.Add(item.Evidence.AnalysisGuid);
                    unsupportedCases++;
                }
                continue;
            }
            RecordPairOrders(analysis, pairCounts, session);
            if (analysis.Occurrences.Count < 2 || !HasSameSidePair(analysis.Occurrences)) continue;
            if (!TryGetApplicableTemplates(facts, analysis.RootCategoryGuid, out var templates))
            {
                abstained.Add(item.Evidence.AnalysisGuid);
                unsupportedCases++;
                continue;
            }
            if (templates.Count == 0) continue;

            var attempts = EvaluateTemplates(analysis.Occurrences, templates, out var exceeded);
            if (exceeded)
            {
                abstained.Add(item.Evidence.AnalysisGuid);
                capCases++;
                continue;
            }
            if (attempts.Any(attempt => attempt.Search.Embeddings.Count > 0))
            {
                eligible++;
                continue;
            }
            var composition = FindMultiTemplateEmbedding(analysis.Occurrences, templates);
            if (composition is MultiTemplateSearch.Found or MultiTemplateSearch.Incomplete)
            {
                abstained.Add(item.Evidence.AnalysisGuid);
                if (composition == MultiTemplateSearch.Found) chainCases++;
                else capCases++;
                continue;
            }

            var reversed = attempts.Select(attempt => (attempt.Template,
                    Pairs: CertainlyReversedPairs(analysis.Occurrences, attempt.Slots)))
                .Where(item => item.Pairs.Count > 0).ToArray();
            if (reversed.Length == 0)
            {
                if (attempts.Count > 0) eligible++;
                continue;
            }

            eligible++;
            contradicted++;
            foreach (var itemWithPairs in reversed)
            {
                var pairs = itemWithPairs.Pairs.Select(pair => new
                {
                    firstMsa = pair.First.MsaGuid,
                    first = DescribeMorph(pair.First, session, analysis.Morphs),
                    secondMsa = pair.Second.MsaGuid,
                    second = DescribeMorph(pair.Second, session, analysis.Morphs),
                    observed = $"{DescribeMorph(pair.First, session, analysis.Morphs)} before " +
                        DescribeMorph(pair.Second, session, analysis.Morphs),
                }).ToArray();
                var evidence = JsonSerializer.Serialize(new
                {
                    analysis = item.Evidence.AnalysisGuid,
                    wordform = item.Evidence.WordformGuid,
                    template = itemWithPairs.Template.Guid,
                    pairs,
                });
                var references = new List<ParsimonyEvidenceReference>
                {
                    EvidenceReference(bundleId, "template-order", new { objectGuid = itemWithPairs.Template.Guid }),
                    EvidenceReference(bundleId, "approved-morph-sequences",
                        new { objectGuid = item.Evidence.AnalysisGuid }),
                };
                foreach (var msaGuid in itemWithPairs.Pairs.SelectMany(pair =>
                             new[] { pair.First.MsaGuid, pair.Second.MsaGuid }).Distinct(StringComparer.Ordinal))
                    references.Add(EvidenceReference(bundleId, "affix-context", new { objectGuid = msaGuid }));
                findings.Add(new ParsimonyFinding(
                    $"{measure.Id}:{itemWithPairs.Template.Guid}:{item.Evidence.AnalysisGuid}", measure.Id,
                    measure.Axis, measure.Tier,
                    new ParsimonyFindingAttachment(measure.AttachesTo, itemWithPairs.Template.Guid,
                        measure.AuthoredObjectKind), null,
                    new ParsimonyMeasureNumber(1, 1, measure.Unit), measure.Threshold!, Digest(evidence),
                    references, measure.RecipeLink,
                    ["No active applicable template embeds this Approved sequence in order.",
                     "Multi-template chains are not inferred; parser comparison was not run."],
                    ParsimonyVerification.NotRun));
            }
        }

        var variability = DescribePairVariability(pairCounts);
        var detailParts = new List<string>();
        if (variability.Count > 0) detailParts.Add("Pair-order variability: " + string.Join("; ", variability));
        if (unsupportedCases > 0)
            detailParts.Add($"Abstained for {unsupportedCases} Approved analysis case(s) with unsupported roots, morph types, or category ancestry.");
        if (chainCases > 0)
            detailParts.Add($"Abstained for {chainCases} possible multi-template chain(s); chains are unsupported.");
        if (capCases > 0)
            detailParts.Add($"Abstained for {capCases} Approved analysis case(s) because assignment enumeration exceeded its safety bound.");
        var status = abstained.Count == 0 ? ParsimonyMeasureStatus.Computed : ParsimonyMeasureStatus.Inconclusive;
        var result = findings.OrderBy(item => item.FindingId, StringComparer.Ordinal).ToArray();
        var detail = detailParts.Count == 0 ? null : string.Join(" ", detailParts);
        return Result(measure, status, eligible, contradicted, result, detail,
            abstained.Count == 0 ? null : "some Approved analysis cases could not be checked, because their roots, " +
                "morph types, or category ancestry are unsupported, they form multi-template chains, or assignment " +
                "enumeration exceeded its bound.");
    }

    private static ParsimonyMeasureResult RunSlotBlocking(MeasureDefinition measure,
        ParsimonyQuerySession session, string bundleId)
    {
        var facts = session.ReadTemplateSequenceFacts();
        var witnesses = new Dictionary<string, SlotBlockingWitnesses>(StringComparer.Ordinal);
        var slotEligible = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        long eligible = 0;
        var abstained = new HashSet<string>(StringComparer.Ordinal);
        var capCases = 0;
        var chainCases = 0;
        var unsupportedCases = 0;
        var unknownObligationCases = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in facts.Analyses)
        {
            if (!TryGetSupportedAnalysis(item, out var analysis))
            {
                if (HasPotentialTemplateMorphology(item))
                {
                    abstained.Add(item.Evidence.AnalysisGuid);
                    unsupportedCases++;
                }
                continue;
            }
            if (!TryGetApplicableTemplates(facts, analysis.RootCategoryGuid, out var templates))
            {
                abstained.Add(item.Evidence.AnalysisGuid);
                unsupportedCases++;
                continue;
            }
            if (templates.Count == 0) continue;
            var attempts = EvaluateTemplates(analysis.Occurrences, templates, out var exceeded);
            if (exceeded)
            {
                abstained.Add(item.Evidence.AnalysisGuid);
                capCases++;
                continue;
            }
            var compatible = attempts.Where(attempt => attempt.Search.Embeddings.Count > 0).ToArray();
            if (compatible.Length == 0)
            {
                var composition = FindMultiTemplateEmbedding(analysis.Occurrences, templates);
                if (composition is MultiTemplateSearch.Found or MultiTemplateSearch.Incomplete)
                {
                    abstained.Add(item.Evidence.AnalysisGuid);
                    if (composition == MultiTemplateSearch.Found) chainCases++;
                    else capCases++;
                }
                continue;
            }
            eligible++;

            foreach (var attempt in compatible)
            {
                foreach (var slot in attempt.Template.Slots.Where(slot =>
                             slot.CompiledOrder.HasValue && slot.Optional == false))
                {
                    if (!slotEligible.TryGetValue(slot.Guid, out var cases))
                        slotEligible.Add(slot.Guid, cases = new HashSet<string>(StringComparer.Ordinal));
                    cases.Add(item.Evidence.AnalysisGuid);
                }
            }

            var obligationResults = compatible.Select(attempt =>
                (Attempt: attempt, Result: EvaluateObligations(attempt))).ToArray();
            if (obligationResults.Any(item => item.Result.Status == TemplateObligationStatus.Accepts)) continue;
            if (obligationResults.Any(item => item.Result.Status == TemplateObligationStatus.Unknown))
            {
                abstained.Add(item.Evidence.AnalysisGuid);
                unknownObligationCases.Add(item.Evidence.AnalysisGuid);
                continue;
            }

            var failures = obligationResults.Select(item => new SlotBlockingTemplateFailure(
                    item.Attempt.Template.Guid, item.Result.MissingSlotAlternatives))
                .ToArray();
            var causeSlots = failures.SelectMany(failure => failure.MissingSlotAlternatives)
                .SelectMany(slots => slots).Distinct(StringComparer.Ordinal).ToArray();
            if (causeSlots.Length == 0)
            {
                abstained.Add(item.Evidence.AnalysisGuid);
                unknownObligationCases.Add(item.Evidence.AnalysisGuid);
                continue;
            }
            foreach (var slotGuid in causeSlots)
            {
                if (!witnesses.TryGetValue(slotGuid, out var slotWitnesses))
                {
                    witnesses.Add(slotGuid, slotWitnesses = new SlotBlockingWitnesses(slotGuid));
                }
                slotWitnesses.Add(item.Evidence, failures);
            }
        }

        var slotNames = facts.Templates.SelectMany(template => template.Slots)
            .GroupBy(slot => slot.Guid, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Name, StringComparer.Ordinal);
        var findings = witnesses.Values.OrderBy(item => item.SlotGuid, StringComparer.Ordinal).Select(item =>
        {
            var witnessRows = item.Analyses.Values.OrderBy(value => value.Evidence.AnalysisGuid,
                StringComparer.Ordinal).ToArray();
            var allCauseSlots = witnessRows.SelectMany(value => value.Failures.Values)
                .SelectMany(failure => failure.MissingSlotAlternatives).SelectMany(slots => slots)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var evidence = JsonSerializer.Serialize(new
            {
                slot = item.SlotGuid,
                witnesses = witnessRows.Select(value => new
                {
                    analysis = value.Evidence.AnalysisGuid,
                    wordform = value.Evidence.WordformGuid,
                    templates = value.Failures.Values.OrderBy(failure => failure.TemplateGuid,
                            StringComparer.Ordinal)
                        .Select(failure => new
                        {
                            template = failure.TemplateGuid,
                            missingSlotAlternatives = failure.MissingSlotAlternatives,
                        }),
                }),
            });
            var refs = new List<ParsimonyEvidenceReference>();
            foreach (var slotGuid in allCauseSlots)
                refs.Add(EvidenceReference(bundleId, "slot-context", new { objectGuid = slotGuid }));
            var templateFailures = witnessRows.SelectMany(value => value.Failures.Values)
                .GroupBy(failure => failure.TemplateGuid, StringComparer.Ordinal).Select(group => group.First())
                .OrderBy(failure => failure.TemplateGuid, StringComparer.Ordinal).ToArray();
            foreach (var failure in templateFailures)
                refs.Add(EvidenceReference(bundleId, "template-order", new { objectGuid = failure.TemplateGuid }));
            foreach (var witness in witnessRows)
                refs.Add(EvidenceReference(bundleId, "approved-morph-sequences",
                    new { objectGuid = witness.Evidence.AnalysisGuid }));

            var causeDescriptions = witnessRows.SelectMany(value => value.Failures.Values.Select(failure =>
                    DescribeTemplateFailure(failure, session, slotNames)))
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var limitations = new List<string>
            {
                "No order-compatible applicable template satisfies all obligatory slots for this Approved sequence.",
            };
            if (causeDescriptions.Length > 0)
                limitations.Add("Obligatory-slot failures: " + string.Join("; ", causeDescriptions));
            if (allCauseSlots.Length > 1 || templateFailures.Length > 1 || witnessRows.Any(value =>
                    value.Failures.Values.Any(failure => failure.MissingSlotAlternatives.Count > 1)))
                limitations.Add("The cause is joint or ambiguous across the listed template alternatives and obligatory slots; no single slot is established as the sole cause.");
            limitations.Add("This Text-tier measure does not run the one-slot-optional parser counterfactual; the finding does not recommend making the slot optional.");

            return new ParsimonyFinding(
                $"{measure.Id}:{item.SlotGuid}", measure.Id, measure.Axis, measure.Tier,
                new ParsimonyFindingAttachment(measure.AttachesTo, item.SlotGuid, measure.AuthoredObjectKind), null,
                new ParsimonyMeasureNumber(witnessRows.Length,
                    Math.Max(witnessRows.Length, slotEligible.GetValueOrDefault(item.SlotGuid)?.Count ?? 0), measure.Unit),
                measure.Threshold!, Digest(evidence), refs, measure.RecipeLink,
                limitations, ParsimonyVerification.Inconclusive);
        }).ToArray();
        var findingCases = witnesses.Values.SelectMany(item => item.Analyses.Keys)
            .Distinct(StringComparer.Ordinal).LongCount();
        var status = abstained.Count == 0 && unknownObligationCases.Count == 0
            ? ParsimonyMeasureStatus.Computed
            : ParsimonyMeasureStatus.Inconclusive;
        var detail = "This Text-tier measure does not run the one-slot-optional parser counterfactual. Use a single-slot Draft, scratch Dry Run, and paired Assessments before recommending optionality.";
        if (unsupportedCases > 0)
            detail += $" Abstained for {unsupportedCases} Approved analysis case(s) with unsupported roots, morph types, or category ancestry.";
        if (chainCases > 0)
            detail += $" Abstained for {chainCases} possible multi-template chain(s); chains are unsupported.";
        if (capCases > 0)
            detail += $" Abstained for {capCases} Approved analysis case(s) because assignment enumeration exceeded its safety bound.";
        if (unknownObligationCases.Count > 0)
            detail += $" Obligation is unknown for {unknownObligationCases.Count} order-compatible case(s) with an unresolved slot optionality fact.";
        var clauses = new List<string>();
        if (abstained.Count > 0)
            clauses.Add("some Approved analysis cases could not be checked, because their roots, morph types, or " +
                "category ancestry are unsupported, they form multi-template chains, or assignment enumeration " +
                "exceeded its bound");
        if (unknownObligationCases.Count > 0)
            clauses.Add("the slot optionality of some order-compatible cases is unknown");
        return Result(measure, status, eligible, findingCases, findings, detail,
            status == ParsimonyMeasureStatus.Inconclusive ? string.Join(" and ", clauses) + "." : null);
    }

    private static TemplateObligationEvaluation EvaluateObligations(EvaluatedTemplate attempt)
    {
        var required = attempt.Template.Slots.Where(slot => slot.CompiledOrder.HasValue && slot.Optional == false)
            .Select(slot => slot.Guid).ToArray();
        var unknown = attempt.Template.Slots.Where(slot => slot.CompiledOrder.HasValue && slot.Optional is null)
            .Select(slot => slot.Guid).ToArray();
        var missingAlternatives = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var canFillKnownObligations = false;
        foreach (var embedding in attempt.Search.Embeddings)
        {
            var filled = embedding.Select(assignment => assignment.SlotGuid).ToHashSet(StringComparer.Ordinal);
            var missingRequired = required.Where(slotGuid => !filled.Contains(slotGuid))
                .Order(StringComparer.Ordinal).ToArray();
            var missingUnknown = unknown.Where(slotGuid => !filled.Contains(slotGuid)).ToArray();
            if (missingRequired.Length == 0)
            {
                canFillKnownObligations = true;
                if (missingUnknown.Length == 0)
                    return new TemplateObligationEvaluation(TemplateObligationStatus.Accepts, []);
            }
            else
            {
                var key = string.Join('\u001f', missingRequired);
                missingAlternatives.TryAdd(key, Array.AsReadOnly(missingRequired));
            }
        }
        if (!canFillKnownObligations)
            return new TemplateObligationEvaluation(TemplateObligationStatus.Fails,
                Array.AsReadOnly(missingAlternatives.Values.OrderBy(
                    slots => string.Join('\u001f', slots), StringComparer.Ordinal).ToArray()));
        return new TemplateObligationEvaluation(TemplateObligationStatus.Unknown, []);
    }

    private static string DescribeTemplateFailure(SlotBlockingTemplateFailure failure,
        ParsimonyQuerySession session, IReadOnlyDictionary<string, string> slotNames)
    {
        var templateName = session.DescribeObject(failure.TemplateGuid) ?? failure.TemplateGuid;
        var alternatives = failure.MissingSlotAlternatives.Select(slots => "[" + string.Join(" + ", slots.Select(
            slotGuid => $"\"{session.DescribeObject(slotGuid) ?? slotNames.GetValueOrDefault(slotGuid) ?? slotGuid}\"")) + "]");
        return $"{templateName} leaves one of these obligatory-slot sets unfilled across its order-compatible embeddings: " +
            string.Join(" or ", alternatives);
    }

    private static bool TryGetSupportedAnalysis(TemplateMeasureAnalysis item,
        out SupportedTemplateAnalysis analysis)
    {
        analysis = null!;
        var roots = item.Evidence.Morphs.Where(morph => morph.MsaKind == "stem").ToArray();
        if (roots.Length != 1 || item.RootCategoryGuids.Count != 1) return false;
        var affixes = item.Evidence.Morphs.Where(morph => morph.MsaKind != "stem").ToArray();
        if (affixes.Any(morph => morph.MsaKind != "inflectional" || morph.MsaGuid is null ||
                morph.MorphType is not ("prefix" or "suffix"))) return false;
        var occurrences = affixes.OrderBy(morph => morph.Ordinal).Select(morph =>
            new TemplateEmbeddingOccurrence(morph.Ordinal, morph.MsaGuid!, morph.MorphType!)).ToArray();
        analysis = new SupportedTemplateAnalysis(item.Evidence, item.RootCategoryGuids[0], occurrences,
            affixes.ToDictionary(morph => morph.Ordinal));
        return true;
    }

    private static bool HasPotentialTemplateMorphology(TemplateMeasureAnalysis item) =>
        item.Evidence.Morphs.Any(morph => morph.MsaKind == "inflectional" ||
            morph.MorphType is "prefix" or "suffix") ||
        item.Evidence.Morphs.Count(morph => morph.MsaKind == "stem") > 1;

    private static bool TryGetApplicableTemplates(TemplateSequenceMeasureFacts facts, string rootCategoryGuid,
        out IReadOnlyList<ApplicableTemplate> templates)
    {
        var ancestors = new HashSet<string>(StringComparer.Ordinal);
        var current = rootCategoryGuid;
        while (true)
        {
            if (!facts.CategoryParents.TryGetValue(current, out var parent) || !ancestors.Add(current))
            {
                templates = [];
                return false;
            }
            if (parent is null) break;
            current = parent;
        }
        templates = Array.AsReadOnly(facts.Templates.Where(template => !template.Disabled &&
                ancestors.Contains(template.CategoryGuid))
            .Select(template => new ApplicableTemplate(template, TemplateSequenceEmbedding.InSurfaceOrder(
                template.Slots.Where(slot => slot.CompiledOrder.HasValue).Select(slot =>
                    new TemplateEmbeddingSlot(slot.Guid, slot.Side, slot.CompiledOrder!.Value, slot.Optional ?? false,
                        slot.MsaGuids)))))
            .Where(template => template.Slots.Count > 0)
            .OrderBy(template => template.Template.Guid, StringComparer.Ordinal).ToArray());
        return true;
    }

    private static IReadOnlyList<EvaluatedTemplate> EvaluateTemplates(
        IReadOnlyList<TemplateEmbeddingOccurrence> occurrences,
        IReadOnlyList<ApplicableTemplate> templates, out bool exceeded)
    {
        var results = new List<EvaluatedTemplate>();
        var total = 0;
        exceeded = false;
        foreach (var template in templates)
        {
            var membershipComplete = occurrences.All(occurrence => template.Slots.Any(slot =>
                slot.Side == occurrence.Side && slot.MsaGuids.Contains(occurrence.MsaGuid)));
            if (!membershipComplete) continue;
            var remaining = Math.Max(1, TemplateSequenceEmbedding.MaximumEmbeddings - total);
            var search = TemplateSequenceEmbedding.Enumerate(occurrences, template.Slots, remaining);
            results.Add(new EvaluatedTemplate(template.Template, template.Slots, search));
            total += search.Embeddings.Count;
            if (!search.Complete || total > TemplateSequenceEmbedding.MaximumEmbeddings)
            {
                exceeded = true;
                return results;
            }
        }
        return results;
    }

    private static MultiTemplateSearch FindMultiTemplateEmbedding(
        IReadOnlyList<TemplateEmbeddingOccurrence> occurrences,
        IReadOnlyList<ApplicableTemplate> templates)
    {
        const int searchNodeLimit = 4096;
        var candidates = occurrences.Select(occurrence => templates.SelectMany(template =>
                template.Slots.Select((slot, index) => new MultiTemplateCandidate(template.Template.Guid, slot, index))
                    .Where(candidate => candidate.Slot.Side == occurrence.Side &&
                        candidate.Slot.MsaGuids.Contains(occurrence.MsaGuid)))
            .OrderBy(candidate => candidate.TemplateGuid, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Index).ToArray()).ToArray();
        if (candidates.Any(candidate => candidate.Length == 0)) return MultiTemplateSearch.None;

        var lastPositions = new Dictionary<(string Template, string Side), int>();
        var usedSlots = new HashSet<string>(StringComparer.Ordinal);
        var usedTemplates = new HashSet<string>(StringComparer.Ordinal);
        var visited = 0;
        var found = false;
        var incomplete = false;

        void Search(int occurrenceIndex)
        {
            if (found || incomplete) return;
            if (++visited > searchNodeLimit)
            {
                incomplete = true;
                return;
            }
            if (occurrenceIndex == occurrences.Count)
            {
                found = usedTemplates.Count > 1;
                return;
            }
            var occurrence = occurrences[occurrenceIndex];
            foreach (var candidate in candidates[occurrenceIndex])
            {
                var key = (candidate.TemplateGuid, candidate.Slot.Side);
                var previous = lastPositions.GetValueOrDefault(key, -1);
                if (candidate.Index <= previous || !usedSlots.Add(candidate.Slot.Guid)) continue;
                lastPositions[key] = candidate.Index;
                var addedTemplate = usedTemplates.Add(candidate.TemplateGuid);
                Search(occurrenceIndex + 1);
                if (addedTemplate) usedTemplates.Remove(candidate.TemplateGuid);
                if (previous < 0) lastPositions.Remove(key);
                else lastPositions[key] = previous;
                usedSlots.Remove(candidate.Slot.Guid);
                if (found || incomplete) return;
            }
        }

        Search(0);
        return found ? MultiTemplateSearch.Found : incomplete ? MultiTemplateSearch.Incomplete : MultiTemplateSearch.None;
    }

    private static IReadOnlyList<ReversedAffixPair> CertainlyReversedPairs(
        IReadOnlyList<TemplateEmbeddingOccurrence> occurrences,
        IReadOnlyList<TemplateEmbeddingSlot> slots)
    {
        var positions = slots.Select((slot, index) => (slot, index)).ToArray();
        var repeatedMsas = occurrences.GroupBy(item => (item.Side, item.MsaGuid))
            .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet();
        var result = new List<ReversedAffixPair>();
        for (var firstIndex = 0; firstIndex < occurrences.Count; firstIndex++)
        for (var secondIndex = firstIndex + 1; secondIndex < occurrences.Count; secondIndex++)
        {
            var first = occurrences[firstIndex];
            var second = occurrences[secondIndex];
            if (first.Side != second.Side || first.MsaGuid == second.MsaGuid ||
                repeatedMsas.Contains((first.Side, first.MsaGuid)) ||
                repeatedMsas.Contains((second.Side, second.MsaGuid))) continue;
            var firstMorph = first;
            var secondMorph = second;
            var firstSlots = positions.Where(item => item.slot.Side == firstMorph.Side &&
                item.slot.MsaGuids.Contains(firstMorph.MsaGuid)).Select(item => item.index).ToArray();
            var secondSlots = positions.Where(item => item.slot.Side == secondMorph.Side &&
                item.slot.MsaGuids.Contains(secondMorph.MsaGuid)).Select(item => item.index).ToArray();
            var placements = (from a in firstSlots from b in secondSlots where a != b select (a, b)).ToArray();
            if (placements.Length > 0 && placements.All(placement => placement.a > placement.b))
                result.Add(new ReversedAffixPair(first, second));
        }
        return Array.AsReadOnly(result.ToArray());
    }

    private static bool HasSameSidePair(IReadOnlyList<TemplateEmbeddingOccurrence> occurrences) =>
        occurrences.GroupBy(item => item.Side, StringComparer.Ordinal).Any(group => group.Count() > 1);

    private static void RecordPairOrders(SupportedTemplateAnalysis analysis,
        IDictionary<AffixPairKey, PairOrderCount> counts, ParsimonyQuerySession session)
    {
        foreach (var sideGroup in analysis.Occurrences.GroupBy(item => item.Side, StringComparer.Ordinal))
        {
            var side = sideGroup.OrderBy(item => item.MorphOrdinal).ToArray();
            var repetitions = side.GroupBy(item => item.MsaGuid, StringComparer.Ordinal)
                .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
            for (var firstIndex = 0; firstIndex < side.Length; firstIndex++)
            for (var secondIndex = firstIndex + 1; secondIndex < side.Length; secondIndex++)
            {
                var first = side[firstIndex];
                var second = side[secondIndex];
                if (repetitions.Contains(first.MsaGuid) || repetitions.Contains(second.MsaGuid)) continue;
                var firstIsCanonical = StringComparer.Ordinal.Compare(first.MsaGuid, second.MsaGuid) < 0;
                var key = firstIsCanonical
                    ? new AffixPairKey(sideGroup.Key, first.MsaGuid, second.MsaGuid)
                    : new AffixPairKey(sideGroup.Key, second.MsaGuid, first.MsaGuid);
                if (!counts.TryGetValue(key, out var count))
                {
                    var firstLabel = firstIsCanonical ? DescribeMorph(first, session, analysis.Morphs) :
                        DescribeMorph(second, session, analysis.Morphs);
                    var secondLabel = firstIsCanonical ? DescribeMorph(second, session, analysis.Morphs) :
                        DescribeMorph(first, session, analysis.Morphs);
                    counts.Add(key, count = new PairOrderCount(firstLabel, secondLabel));
                }
                if (firstIsCanonical) count.FirstBeforeSecond++;
                else count.SecondBeforeFirst++;
            }
        }
    }

    private static IReadOnlyList<string> DescribePairVariability(
        IReadOnlyDictionary<AffixPairKey, PairOrderCount> pairCounts) => Array.AsReadOnly(pairCounts
        .Where(pair => pair.Value.FirstBeforeSecond > 0 && pair.Value.SecondBeforeFirst > 0)
        .OrderBy(pair => pair.Key.Side, StringComparer.Ordinal)
        .ThenBy(pair => pair.Key.FirstMsaGuid, StringComparer.Ordinal)
        .ThenBy(pair => pair.Key.SecondMsaGuid, StringComparer.Ordinal)
        .Select(pair =>
        {
            var first = pair.Value.FirstBeforeSecond;
            var second = pair.Value.SecondBeforeFirst;
            var total = first + second;
            return $"{pair.Value.FirstLabel}/{pair.Value.SecondLabel} min({first},{second})/{total}=" +
                $"{(double)Math.Min(first, second) / total:0.00} (descriptive only)";
        }).ToArray());

    private static string DescribeMorph(ParsimonyApprovedMorph morph, ParsimonyQuerySession session)
    {
        var form = morph.Forms.Values.FirstOrDefault();
        var entryForm = morph.EntryGuid is null ? null : session.DescribeObject(morph.EntryGuid);
        var gloss = morph.Glosses.Values.FirstOrDefault();
        var label = form ?? entryForm ?? morph.MsaGuid ?? morph.MorphGuid ?? "unknown morpheme";
        return gloss is null ? label : $"{label} ({gloss})";
    }

    private static string DescribeMorph(TemplateEmbeddingOccurrence occurrence, ParsimonyQuerySession session,
        IReadOnlyDictionary<int, ParsimonyApprovedMorph> morphs) => DescribeMorph(morphs[occurrence.MorphOrdinal], session);

    private sealed record SupportedTemplateAnalysis(ParsimonyApprovedMorphSequenceViewRow Evidence,
        string RootCategoryGuid, IReadOnlyList<TemplateEmbeddingOccurrence> Occurrences,
        IReadOnlyDictionary<int, ParsimonyApprovedMorph> Morphs);

    private sealed record ApplicableTemplate(TemplateMeasure Template,
        IReadOnlyList<TemplateEmbeddingSlot> Slots);

    private sealed record EvaluatedTemplate(TemplateMeasure Template,
        IReadOnlyList<TemplateEmbeddingSlot> Slots, TemplateEmbeddingSearch Search);

    private sealed record ReversedAffixPair(TemplateEmbeddingOccurrence First,
        TemplateEmbeddingOccurrence Second);

    private sealed record MultiTemplateCandidate(string TemplateGuid, TemplateEmbeddingSlot Slot, int Index);

    private enum TemplateObligationStatus
    {
        Accepts,
        Fails,
        Unknown,
    }

    private sealed record TemplateObligationEvaluation(TemplateObligationStatus Status,
        IReadOnlyList<IReadOnlyList<string>> MissingSlotAlternatives);

    private sealed record SlotBlockingTemplateFailure(string TemplateGuid,
        IReadOnlyList<IReadOnlyList<string>> MissingSlotAlternatives);

    private enum MultiTemplateSearch
    {
        None,
        Found,
        Incomplete,
    }

    private sealed record AffixPairKey(string Side, string FirstMsaGuid, string SecondMsaGuid);

    private sealed class PairOrderCount(string firstLabel, string secondLabel)
    {
        public string FirstLabel { get; } = firstLabel;
        public string SecondLabel { get; } = secondLabel;
        public long FirstBeforeSecond { get; set; }
        public long SecondBeforeFirst { get; set; }
    }

    private sealed class SlotBlockingWitnesses(string slotGuid)
    {
        public string SlotGuid { get; } = slotGuid;
        public Dictionary<string, SlotBlockingCase> Analyses { get; } = new(StringComparer.Ordinal);

        public void Add(ParsimonyApprovedMorphSequenceViewRow evidence,
            IReadOnlyList<SlotBlockingTemplateFailure> failures)
        {
            if (!Analyses.TryGetValue(evidence.AnalysisGuid, out var item))
                Analyses.Add(evidence.AnalysisGuid, item = new SlotBlockingCase(evidence));
            foreach (var failure in failures)
                item.Failures.TryAdd(failure.TemplateGuid, failure);
        }
    }

    private sealed class SlotBlockingCase(ParsimonyApprovedMorphSequenceViewRow evidence)
    {
        public ParsimonyApprovedMorphSequenceViewRow Evidence { get; } = evidence;
        public Dictionary<string, SlotBlockingTemplateFailure> Failures { get; } = new(StringComparer.Ordinal);
    }

    private static string Signature(AdhocProhibitionFact item) => JsonSerializer.Serialize(new
    {
        kind = item.Kind,
        primary = new { guid = item.PrimaryGuid, kind = item.PrimaryTargetKind },
        others = item.Others.Select(target => new { guid = target.Guid, kind = target.Kind }),
        adjacency = item.Adjacency,
    });

    private static string FormsSignature(IReadOnlyDictionary<string, IReadOnlyList<string>> forms) =>
        JsonSerializer.Serialize(forms.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new { writingSystem = pair.Key, forms = pair.Value.Order(StringComparer.Ordinal) }));

    private static string EnvironmentsSignature(IReadOnlyList<AllomorphEnvironmentFact> environments) =>
        JsonSerializer.Serialize(environments.OrderBy(item => item.Role, StringComparer.Ordinal)
            .ThenBy(item => item.Ordinal).ThenBy(item => item.EnvironmentGuid, StringComparer.Ordinal)
            .Select(item => new { item.Role, item.Ordinal, item.EnvironmentGuid }));

    private static ParsimonyMeasureResult Result(MeasureDefinition measure, ParsimonyMeasureStatus status,
        long eligible, long findingsCount, IReadOnlyList<ParsimonyFinding> findings, string? detail,
        string? noteReason = null)
    {
        var number = status is ParsimonyMeasureStatus.Computed or ParsimonyMeasureStatus.Inconclusive
            ? new ParsimonyMeasureNumber(findingsCount, eligible, measure.Unit)
            : null;
        var run = new ParsimonyMeasureRun(measure.Id, status, eligible, findingsCount, measure.Unit, number,
            eligible == 0 ? null : (double)findingsCount / eligible, detail);
        return new ParsimonyMeasureResult(run, findings, noteReason);
    }

    private static ParsimonyMeasureResult Unavailable(MeasureDefinition measure, string detail,
        string noteReason) =>
        new(new ParsimonyMeasureRun(measure.Id, ParsimonyMeasureStatus.NotAvailable, null, null,
            measure.Unit, null, null, detail), [], noteReason);

    private static ParsimonyEvidenceReference EvidenceReference(string bundleId, string view, object arguments)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(arguments));
        return new ParsimonyEvidenceReference(bundleId, view, document.RootElement.Clone());
    }

    private static string Digest(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed record AllomorphSignature(string EntryGuid, string MorphType, string Forms, string Environments);
}

/// <summary>The measure-run status and all findings from the same full-input query.</summary>
/// <param name="NoteReason">The one short clause a Parsimony note shows, or null when the check looked.</param>
public sealed record ParsimonyMeasureResult
{
    /// <summary>Pairs a run with its findings; an Inconclusive or NotAvailable run must name why it could not look.</summary>
    /// <exception cref="ArgumentException">Thrown when a run that could not look has no note reason.</exception>
    public ParsimonyMeasureResult(ParsimonyMeasureRun run, IReadOnlyList<ParsimonyFinding> findings,
        string? noteReason = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.Status is ParsimonyMeasureStatus.Inconclusive or ParsimonyMeasureStatus.NotAvailable &&
            string.IsNullOrWhiteSpace(noteReason))
            throw new ArgumentException($"The {run.Status} run of '{run.MeasureId}' needs a note reason.",
                nameof(noteReason));
        Run = run;
        Findings = findings;
        NoteReason = noteReason;
    }

    /// <summary>The measure outcome with its full, internal reason.</summary>
    public ParsimonyMeasureRun Run { get; }

    /// <summary>The matching findings; empty when the measure could not look.</summary>
    public IReadOnlyList<ParsimonyFinding> Findings { get; }

    /// <summary>The one short clause a Parsimony note shows, or null when no note applies.</summary>
    public string? NoteReason { get; }
}
