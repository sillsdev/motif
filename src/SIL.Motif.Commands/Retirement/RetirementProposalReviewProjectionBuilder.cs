using System.Text.Json;
using SIL.Motif.Commands.Parsimony;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Retirement;
using SIL.Motif.Projection.Retirement;

namespace SIL.Motif.Commands.Retirement;

/// <summary>Exact rule attribution for one tested reading, or an explicit unavailable reason.</summary>
public sealed record RetirementRuleAttribution(string CaseId, string ReadingId, string Status,
    string? RuleId, string? EvidenceDigest, string? Detail);

/// <summary>Builds one validated view from an exact Proposal, Dry Run, parser pair and reference census.</summary>
public static class RetirementProposalReviewProjectionBuilder
{
    private const string BundleForm = "analysis/wfiMorphBundle/form";

    /// <summary>Builds a review from one Proposal, its observed Dry Run, paired Reports, and reference details.</summary>
    public static RetirementProposalReviewProjection Build(Proposal proposal, DryRunProjection dryRun,
        RetirementReviewStatistics statistics, RetirementExpectationTranslation translation,
        RecipeVerificationResult verification, ParsimonyCandidateEvidenceResponse evidence,
        AllomorphReferenceFootprint footprint, string findingId,
        IReadOnlyList<RetirementRuleAttribution>? ruleAttributions = null,
        IReadOnlyDictionary<string, string>? observedSurfaceAfter = null,
        AllomorphReferenceDestinationDiagnostics? unresolved = null,
        IReadOnlyCollection<string>? findingOperationIds = null)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(dryRun);
        ArgumentNullException.ThrowIfNull(statistics);
        ArgumentNullException.ThrowIfNull(translation);
        ArgumentNullException.ThrowIfNull(verification);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(footprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(findingId);
        RetirementExpectationTranslator.ValidatePair(translation);
        ValidateBinding(proposal, dryRun, statistics, translation, evidence, footprint, findingId);

        var unavailable = new SortedSet<string>(StringComparer.Ordinal);
        unavailable.UnionWith(footprint.Unavailable);
        unavailable.UnionWith(translation.Original.Unavailable);
        unavailable.UnionWith(translation.Translated.Unavailable);
        unavailable.UnionWith(verification.Unavailable);
        if (verification.Status == RecipeVerificationStatus.Incomplete)
            unavailable.Add("Some affected reading results are incomplete.");

        var boundOperationIds = (findingOperationIds ?? []).ToHashSet(StringComparer.Ordinal);
        var operations = dryRun.Operations.ToDictionary(operation => operation.OperationId,
            StringComparer.Ordinal);
        if (boundOperationIds.Any(id => !operations.ContainsKey(id)))
            throw new InvalidDataException("The finding binding names an operation outside the Dry Run.");
        var operationParts = new Dictionary<string, List<string>>(StringComparer.Ordinal)
        {
            ["rule"] = [], ["bundle"] = [], ["other-references"] = [], ["retired-form"] = [],
        };
        var unlinkedOperations = new List<string>();
        foreach (var operation in dryRun.Operations)
        {
            var part = PartFor(operation.Kind);
            if (part is null)
            {
                if (!boundOperationIds.Contains(operation.OperationId))
                    unlinkedOperations.Add(operation.OperationId);
            }
            else operationParts[part].Add(operation.OperationId);
        }

        var operationPartById = operationParts.SelectMany(pair => pair.Value.Select(id => (id, pair.Key)))
            .ToDictionary(pair => pair.id, pair => pair.Key, StringComparer.Ordinal);
        var partEffects = operationParts.ToDictionary(pair => pair.Key, pair => dryRun.Effects.Where(effect =>
            effect.OperationIds.Any(id => operationPartById.GetValueOrDefault(id) == pair.Key)).ToArray(),
            StringComparer.Ordinal);
        var unlinkedEffects = dryRun.Effects.Where(effect => effect.OperationIds.Count == 0 ||
            effect.OperationIds.All(id => !operationPartById.ContainsKey(id) && !boundOperationIds.Contains(id))).ToArray();
        if (unlinkedEffects.Length > 0)
            unavailable.Add($"{unlinkedEffects.Length} Dry Run effect(s) have no direct operation link.");
        if (unlinkedOperations.Count > 0)
            unavailable.Add($"{unlinkedOperations.Count} Proposal operation(s) are outside the four retirement parts.");

        var bundleText = translation.BundleTextEffects.ToDictionary(effect => effect.Bundle,
            StringComparer.Ordinal);
        var ruleDetails = operationParts["rule"].Select(id => operations[id])
            .Where(operation => operation.EntityId is not null)
            .Select(operation => new RetirementReviewDetail(operation.EntityId!, "rule-component",
                operation.Kind, null, null, null, operation.EntityId, null, null, null, null, null)).ToArray();
        var bundleDetails = footprint.References.Where(reference => reference.Kind == "bundle-morph")
            .Select(reference => Detail(reference, bundleText)).ToArray();
        var otherDetails = footprint.References.Where(reference => reference.Kind != "bundle-morph" &&
            reference.Kind != "entry-alternate-form")
            .Select(reference => Detail(reference, bundleText)).ToArray();
        var retiredDetails = operationParts["retired-form"].SelectMany(id => operations[id].AfterJson is { } json
                ? RetiredFormIds(json).Select(form => new RetirementReviewDetail(form, "retired-form",
                    $"Delete alternate form {form}", null, null, null, null, null, null, null, null, null))
                : [])
            .DistinctBy(detail => detail.Id, StringComparer.Ordinal).ToArray();
        var parts = new[]
        {
            new RetirementReviewPart("rule", "New rule and sound class", operationParts["rule"],
                partEffects["rule"]) { Details = ruleDetails },
            new RetirementReviewPart("bundle", "Analyses and bundle text", operationParts["bundle"],
                partEffects["bundle"]) { Details = bundleDetails },
            new RetirementReviewPart("other-references", "Other references", operationParts["other-references"],
                partEffects["other-references"]) { Details = otherDetails },
            new RetirementReviewPart("retired-form", "Deleted forms", operationParts["retired-form"],
                partEffects["retired-form"]) { Details = retiredDetails },
        };
        var affectedReadings = BuildReadings(translation, verification, ruleAttributions ?? [],
            observedSurfaceAfter, unavailable);
        var finding = BuildFinding(statistics.Detector, evidence, findingId) with
        {
            BindingOperationIds = Array.AsReadOnly(boundOperationIds.Order(StringComparer.Ordinal).ToArray()),
            BindingEffects = Array.AsReadOnly(dryRun.Effects.Where(effect =>
                effect.OperationIds.Any(boundOperationIds.Contains)).ToArray()),
        };
        var unresolvedReview = unresolved is null
            ? new RetirementUnresolvedReferenceReview(0, 0, 0, 0, [])
            : BuildUnresolvedReview(footprint, unresolved);
        if (unresolved is not null) unavailable.UnionWith(unresolved.Unavailable);

        return new RetirementProposalReviewProjection(proposal.ProposalId.Value, dryRun.IntentDigest, dryRun,
            statistics, parts, affectedReadings, finding, unresolvedReview,
            Array.AsReadOnly(unavailable.ToArray()))
        {
            UnlinkedEffects = Array.AsReadOnly(unlinkedEffects),
            UnlinkedOperationIds = Array.AsReadOnly(unlinkedOperations.ToArray()),
        };
    }

    /// <summary>Shapes a destination refusal with its exact counts and every unresolved source row.</summary>
    public static RetirementUnresolvedReferenceReview BuildUnresolvedReview(AllomorphReferenceFootprint footprint,
        AllomorphReferenceDestinationDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(footprint);
        ArgumentNullException.ThrowIfNull(diagnostics);
        var footprintReferences = footprint.References.ToHashSet();
        if (diagnostics.UnresolvedReferences.Any(reference => !footprintReferences.Contains(reference)))
            throw new InvalidDataException("The unresolved destination rows do not belong to the retirement census.");
        var approvedAnalyses = diagnostics.UnresolvedReferences
            .Where(reference => reference.Kind == "bundle-morph" && reference.Opinion == "approved")
            .Select(reference => reference.Analysis).OfType<Guid>().Distinct().Count();
        var adhocRules = diagnostics.UnresolvedReferences
            .Where(reference => reference.Kind.StartsWith("adhoc-", StringComparison.Ordinal))
            .Select(reference => reference.SourceObject).Distinct().Count();
        var customReferences = diagnostics.UnresolvedReferences.Count(reference => reference.IsCustom);
        var otherReferences = diagnostics.UnresolvedReferences.Count(reference =>
            !reference.IsCustom && reference.Kind == "other-native");
        if (approvedAnalyses != diagnostics.UnresolvedApprovedAnalyses ||
            adhocRules != diagnostics.UnresolvedAdhocRules ||
            customReferences != diagnostics.UnsupportedCustomReferences ||
            otherReferences != diagnostics.UnsupportedOtherReferences)
            throw new InvalidDataException("The unresolved destination counts do not match their detail rows.");
        var rows = diagnostics.UnresolvedReferences.Select(reference => Detail(reference,
            new Dictionary<string, RetirementBundleTextEffect>(StringComparer.Ordinal))).ToArray();
        return new RetirementUnresolvedReferenceReview(diagnostics.UnresolvedApprovedAnalyses,
            diagnostics.UnresolvedAdhocRules, diagnostics.UnsupportedOtherReferences,
            diagnostics.UnsupportedCustomReferences, Array.AsReadOnly(rows)) { Message = diagnostics.Message };
    }

    private static void ValidateBinding(Proposal proposal, DryRunProjection dryRun,
        RetirementReviewStatistics statistics, RetirementExpectationTranslation translation,
        ParsimonyCandidateEvidenceResponse evidence, AllomorphReferenceFootprint footprint, string findingId)
    {
        var intent = IntentDigest.Compute(proposal);
        if (proposal.ProposalId.Value != dryRun.ProposalId || intent != dryRun.IntentDigest ||
            evidence.Candidate.ProposalId != proposal.ProposalId.Value ||
            evidence.Candidate.ProposalIntentDigest != intent || evidence.Candidate.DryRunEffectDigest != dryRun.EffectDigest ||
            translation.ProposalIntentDigest != intent || translation.DryRunEffectDigest != dryRun.EffectDigest ||
            translation.DryRunFootprintDigest != dryRun.FootprintDigest)
            throw new InvalidDataException("The retirement review inputs name different Proposals or Dry Runs.");
        if (statistics.DetailManifestDigest != footprint.Digest)
            throw new InvalidDataException("The retirement counts do not name this reference detail manifest.");
        if (statistics.Detector.FindingId != findingId)
            throw new InvalidDataException("The detector counts do not name the requested finding.");
        RetirementReviewCodec.Parse(RetirementReviewCodec.ToJson(statistics));
        ValidateReferenceCounts(statistics, footprint, translation, dryRun);

        ValidateReportTranslation(evidence.Before.Inputs.RetirementExpectationTranslation, translation);
        ValidateReportTranslation(evidence.After.Inputs.RetirementExpectationTranslation, translation);
        var beforeFinding = evidence.Before.Findings.SingleOrDefault(item => item.FindingId == findingId);
        if (beforeFinding is null || CanonicalDigest(beforeFinding.EvidenceDigest) != statistics.Detector.BeforeEvidenceDigest ||
            beforeFinding.Number.Numerator != statistics.Detector.BeforeNumerator ||
            beforeFinding.Number.Denominator != statistics.Detector.BeforeDenominator)
            throw new InvalidDataException("The before finding does not match the retirement review counts.");
        var afterFinding = evidence.After.Findings.SingleOrDefault(item => item.FindingId == findingId);
        if (afterFinding is not null && (CanonicalDigest(afterFinding.EvidenceDigest) != statistics.Detector.AfterEvidenceDigest ||
            afterFinding.Number.Numerator != statistics.Detector.AfterNumerator ||
            afterFinding.Number.Denominator != statistics.Detector.AfterDenominator))
            throw new InvalidDataException("The after finding does not match the retirement review counts.");
        var disposition = evidence.After.DispositionProjection;
        var suppressed = disposition?.Findings.Any(item => item.Finding.FindingId == findingId &&
            string.Equals(item.State, "suppressed", StringComparison.OrdinalIgnoreCase)) == true;
        if (statistics.Detector.Resolved != (afterFinding is null))
            throw new InvalidDataException("The rebuilt finding result conflicts with its resolution count.");
        _ = suppressed;
    }

    private static void ValidateReportTranslation(RetirementExpectationTranslation? reportTranslation,
        RetirementExpectationTranslation translation)
    {
        if (reportTranslation is null || reportTranslation.MappingDigest != translation.MappingDigest ||
            reportTranslation.Original.ManifestDigest != translation.Original.ManifestDigest ||
            reportTranslation.Translated.ManifestDigest != translation.Translated.ManifestDigest ||
            reportTranslation.DryRunEffectDigest != translation.DryRunEffectDigest ||
            reportTranslation.DryRunFootprintDigest != translation.DryRunFootprintDigest)
            throw new InvalidDataException("The paired Reports do not carry this retirement expectation translation.");
    }

    private static void ValidateReferenceCounts(RetirementReviewStatistics statistics,
        AllomorphReferenceFootprint footprint, RetirementExpectationTranslation translation, DryRunProjection dryRun)
    {
        var bundleReferences = footprint.References.Where(reference => reference.Kind == "bundle-morph").ToArray();
        var bundleCount = bundleReferences.Select(reference => reference.Bundle).OfType<Guid>().Distinct().Count();
        var analysisCount = bundleReferences.Select(reference => reference.Analysis).OfType<Guid>().Distinct().Count();
        var wordformCount = bundleReferences.Select(reference => reference.Wordform).OfType<Guid>().Distinct().Count();
        var bundleOpinions = OpinionCounts(bundleReferences.Select(reference =>
            (RequiredId(reference.Bundle, "bundle"), RequiredOpinion(reference.Opinion))));
        var analysisOpinions = OpinionCounts(bundleReferences.Select(reference =>
            (RequiredId(reference.Analysis, "analysis"), RequiredOpinion(reference.Opinion))));
        var wordformOpinions = OpinionCounts(footprint.AffectedWordforms.SelectMany(wordform =>
            wordform.Analyses.Select(analysis =>
                (CanonicalId.FromGuid(wordform.Wordform).Value, analysis.Opinion))));
        if (statistics.BundlesRepointed != bundleOpinions ||
            statistics.AnalysesRepointed != analysisOpinions ||
            statistics.WordformsRepointed != wordformOpinions ||
            statistics.BundlesRepointed.Total() != bundleCount ||
            statistics.AnalysesRepointed.Total() != analysisCount ||
            statistics.WordformsRepointed.Total() != wordformCount ||
            statistics.AssessedFormCases != translation.Original.Cases.Count)
            throw new InvalidDataException("The retirement counts do not cover the complete reference manifest.");

        var adhocReferences = footprint.References.Where(reference =>
            reference.Kind.StartsWith("adhoc-", StringComparison.Ordinal)).ToArray();
        foreach (var partition in statistics.Adhoc)
        {
            var rows = adhocReferences.Where(reference => reference.Grouped == partition.Grouped &&
                reference.Enabled == partition.Enabled).ToArray();
            if (rows.Any(reference => reference.Rule is null) ||
                partition.Rules != rows.Select(reference => reference.Rule).OfType<Guid>().Distinct().Count() ||
                partition.TargetOccurrences != rows.Length)
                throw new InvalidDataException("The ad hoc reference counts do not match the detail manifest.");
        }
        var otherReferences = footprint.References.Count(reference => reference.Kind != "bundle-morph" &&
            !reference.Kind.StartsWith("adhoc-", StringComparison.Ordinal) &&
            reference.Kind != "entry-alternate-form");
        if (statistics.OtherReferences != otherReferences)
            throw new InvalidDataException("The other-reference count does not match the detail manifest.");

        var formChanges = dryRun.Effects.Where(effect => effect.Field == BundleForm)
            .SelectMany(effect => effect.Changes).ToArray();
        if (statistics.BundleTextAlternativesChanged != formChanges.Length ||
            statistics.BundleTextAlternativesCleared != formChanges.Count(change => change.After is null))
            throw new InvalidDataException("The bundle text counts do not match the observed Dry Run effects.");

        var deletedForms = dryRun.Operations.Where(operation => operation.Kind == "lexical/lexEntry/deleteAlternateForm")
            .SelectMany(operation => operation.AfterJson is { } json ? RetiredFormIds(json) : [])
            .Distinct(StringComparer.Ordinal).Count();
        if (statistics.OwnedObjectsDeleted != deletedForms + footprint.OwnedDependents.Count)
            throw new InvalidDataException("The deletion count does not match the Proposal and ownership census.");
    }

    private static RetirementFindingReview BuildFinding(RetirementDetectorCounts detector,
        ParsimonyCandidateEvidenceResponse evidence, string findingId)
    {
        var afterFinding = evidence.After.Findings.SingleOrDefault(item => item.FindingId == findingId);
        var disposition = evidence.After.DispositionProjection;
        bool? suppressed = disposition is null ? null : disposition.Findings.Any(item =>
            item.Finding.FindingId == findingId &&
            string.Equals(item.State, "suppressed", StringComparison.OrdinalIgnoreCase));
        return new RetirementFindingReview(findingId, detector.BeforeNumerator, detector.BeforeDenominator,
            detector.AfterNumerator, detector.AfterDenominator, detector.BeforeEvidenceDigest,
            detector.AfterEvidenceDigest, detector.Resolved && afterFinding is null, suppressed);
    }

    private static IReadOnlyList<RetirementAffectedReading> BuildReadings(
        RetirementExpectationTranslation translation, RecipeVerificationResult verification,
        IReadOnlyList<RetirementRuleAttribution> ruleAttributions,
        IReadOnlyDictionary<string, string>? observedSurfaceAfter, ISet<string> unavailable)
    {
        var expectations = translation.Original.Cases.SelectMany(@case => @case.Readings
            .Where(reading => reading.Opinion == "approved")
            .Select(reading => (Case: @case, Reading: reading)))
            .ToDictionary(item => (item.Case.CaseId, item.Reading.ReadingId));
        var results = verification.Readings.ToDictionary(item => (item.CaseId, item.ReadingId));
        if (results.Keys.Any(key => !expectations.ContainsKey(key)))
            throw new InvalidDataException("Recipe verification names a reading outside the frozen retirement manifest.");
        var attributions = ruleAttributions.ToDictionary(item => (item.CaseId, item.ReadingId));
        var rows = new List<RetirementAffectedReading>(expectations.Count);
        foreach (var pair in expectations.OrderBy(pair => pair.Key.CaseId, StringComparer.Ordinal)
                     .ThenBy(pair => pair.Key.ReadingId, StringComparer.Ordinal))
        {
            var key = pair.Key;
            var @case = pair.Value.Case;
            var expected = pair.Value.Reading;
            if (!results.TryGetValue(key, out var result))
            {
                unavailable.Add($"Reading {expected.ReadingId} on '{@case.Surface}' has no verification result.");
                result = new RecipeVerificationReading(@case.CaseId, expected.ReadingId,
                    RecipeVerificationReadingStatus.Incomplete, null, null, false, false);
            }
            string? afterSurface = null;
            var hasAfterSurface = observedSurfaceAfter is not null &&
                observedSurfaceAfter.TryGetValue(@case.CaseId, out afterSurface);
            if (!hasAfterSurface)
            {
                afterSurface = null;
                unavailable.Add($"The after spelling for '{@case.Surface}' was not read back.");
            }
            var attribution = attributions.GetValueOrDefault(key);
            if (attribution is null || attribution.Status != "traced" || attribution.RuleId is null ||
                attribution.EvidenceDigest is null)
            {
                if (attribution is null || attribution.Status != "unavailable")
                    unavailable.Add($"Rule attribution for reading {expected.ReadingId} on '{@case.Surface}' is unavailable.");
            }
            rows.Add(new RetirementAffectedReading(@case.CaseId, expected.ReadingId, @case.Wordform,
                @case.Surface, @case.WritingSystem, @case.Surface, afterSurface, expected.Opinion,
                OpinionGlyph(expected.Opinion), VerificationStatus(result.Status), result.BeforeProduced,
                result.AfterProduced, hasAfterSurface ? StringComparer.Ordinal.Equals(@case.Surface, afterSurface) : null,
                attribution?.Status ?? "unavailable", attribution?.RuleId, attribution?.EvidenceDigest,
                attribution?.Detail ?? (attribution is null ? "Rule attribution was not recorded." : null)));
        }
        return Array.AsReadOnly(rows.ToArray());
    }

    private static string? PartFor(string kind)
    {
        if (kind.StartsWith("grammar/ph", StringComparison.Ordinal)) return "rule";
        if (kind == "analysis/wfiMorphBundle/setMorph") return "bundle";
        if (kind.StartsWith("grammar/mo", StringComparison.Ordinal) &&
            (kind.Contains("Adhoc", StringComparison.OrdinalIgnoreCase) ||
             kind.EndsWith("/retargetReferences", StringComparison.Ordinal))) return "other-references";
        if (kind == "lexical/lexEntry/deleteAlternateForm") return "retired-form";
        return null;
    }

    private static RetirementReviewDetail Detail(AllomorphReference reference,
        IReadOnlyDictionary<string, RetirementBundleTextEffect> bundleText)
    {
        var bundle = reference.Bundle is { } bundleId ? CanonicalId.FromGuid(bundleId).Value : null;
        bundleText.TryGetValue(bundle ?? string.Empty, out var text);
        return new RetirementReviewDetail(CanonicalId.FromGuid(reference.SourceObject).Value,
            reference.Kind, $"{reference.DeclaringClass}.{reference.Field}" +
            (reference.Ordinal is { } ordinal ? $" at occurrence {ordinal}" : string.Empty),
            Id(reference.Wordform), Id(reference.Analysis), bundle, Id(reference.Rule), reference.Opinion,
            $"{reference.DeclaringClass}.{reference.Field}", reference.Ordinal,
            text is null ? null : Alternatives(text.Before), text is null ? null : Alternatives(text.After));
    }

    private static string? Id(Guid? value) => value is { } id ? CanonicalId.FromGuid(id).Value : null;

    private static string CanonicalDigest(string value)
    {
        var canonical = value.StartsWith("sha256:", StringComparison.Ordinal) ? value : "sha256:" + value;
        Sha256Value.RequireCanonical(canonical, nameof(value));
        return canonical;
    }

    private static string RequiredId(Guid? value, string kind) => value is { } id
        ? CanonicalId.FromGuid(id).Value
        : throw new InvalidDataException($"A bundle reference has no {kind} identity.");

    private static RetirementOpinionCounts OpinionCounts(IEnumerable<(string Id, string Opinion)> values)
    {
        var counts = new int[4];
        foreach (var group in values.GroupBy(value => value.Id, StringComparer.Ordinal))
        {
            var opinions = group.Select(value => value.Opinion)
                .Distinct(StringComparer.Ordinal).ToArray();
            var partition = opinions.Length != 1 ? 3 : opinions[0] switch
            {
                "approved" => 0,
                "disapproved" => 1,
                "unknown" => 2,
                _ => throw new InvalidDataException("A reference opinion is outside the closed review vocabulary."),
            };
            counts[partition]++;
        }
        return new RetirementOpinionCounts(counts[0], counts[1], counts[2], counts[3]);
    }

    private static string RequiredOpinion(string? value) => value ??
        throw new InvalidDataException("A reference opinion is missing from the complete retirement census.");

    private static string Alternatives(IReadOnlyDictionary<string, string> alternatives) =>
        string.Join("; ", alternatives.OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => $"{item.Key}: {item.Value}"));

    private static IEnumerable<string> RetiredFormIds(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("forms", out var forms) || forms.ValueKind != JsonValueKind.Array)
            return [];
        return forms.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()!).ToArray();
    }

    private static string OpinionGlyph(string opinion) => opinion switch
    {
        "approved" => "A",
        "disapproved" => "D",
        "unknown" => "U",
        _ => string.Empty,
    };

    private static string VerificationStatus(RecipeVerificationReadingStatus status) => status switch
    {
        RecipeVerificationReadingStatus.Preserved => "preserved",
        RecipeVerificationReadingStatus.Recovered => "recovered",
        RecipeVerificationReadingStatus.MissingBefore => "missingBefore",
        RecipeVerificationReadingStatus.Lost => "lost",
        RecipeVerificationReadingStatus.FoundAfterBeforeIncomplete => "foundAfterBeforeIncomplete",
        _ => "incomplete",
    };
}
