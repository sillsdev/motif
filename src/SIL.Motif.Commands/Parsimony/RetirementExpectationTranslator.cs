using System.Text.Json;
using System.Diagnostics.CodeAnalysis;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.HumanJudgments;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Retirement;
using SIL.Motif.Model.DryRun;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Host.Parsimony;

namespace SIL.Motif.Commands.Parsimony;

/// <summary>The refusal class reported by frozen retirement expectation translation.</summary>
public enum RetirementExpectationTranslationRefusalKind
{
    /// <summary>A source, Proposal, Dry Run or mapping binding does not match.</summary>
    BindingMismatch,

    /// <summary>A required role or identity is incomplete or has no exact authored destination.</summary>
    IncompleteIdentity,

    /// <summary>Two distinct source readings collapse to one translated reading in a word case.</summary>
    ManyToOneCollision,

    /// <summary>An Approved and Disapproved reading share one translated morphology in a word case.</summary>
    OpinionContradiction,

    /// <summary>A native bundle Form copy is absent or differs from the frozen source text.</summary>
    FormCopyMismatch,

    /// <summary>A staged Notebook revision does not explicitly replace the frozen human judgment.</summary>
    JudgmentRevisionMismatch,
}

/// <summary>An unsafe or mismatched translation input, retained as a structured refusal.</summary>
public sealed class RetirementExpectationTranslationRefusalException(
    RetirementExpectationTranslationRefusalKind kind, string message) : InvalidOperationException(message)
{
    /// <summary>The reason the adapter refused the candidate translation.</summary>
    public RetirementExpectationTranslationRefusalKind Kind { get; } = kind;
}

/// <summary>Translates only exact retirement roles whose native Morph and Form effects are bound by one Dry Run.</summary>
public static class RetirementExpectationTranslator
{
    /// <summary>
    /// Keeps the original frozen record intact and creates a candidate expectation set from exact role mappings.
    /// The Proposal and Dry Run must agree, and each mapped bundle must carry the native Form-copy effect.
    /// </summary>
    public static RetirementExpectationTranslation Translate(
        FrozenExpectationSet original,
        BaselineToken sourceBaseline,
        IReadOnlyList<RetireAllomorphIntent> retirements,
        Proposal proposal,
        DryRun dryRun)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(sourceBaseline);
        ArgumentNullException.ThrowIfNull(retirements);
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(dryRun);

        var frozen = FrozenExpectationCodec.Parse(FrozenExpectationCodec.ToJson(original));
        if (sourceBaseline != frozen.Baseline)
            Refuse(RetirementExpectationTranslationRefusalKind.BindingMismatch,
                "The source Baseline does not match the frozen before expectations.");
        if (ManifestDigest(frozen.Cases, frozen.ReviewedNegatives) != frozen.ManifestDigest)
            Refuse(RetirementExpectationTranslationRefusalKind.BindingMismatch,
                "The frozen before manifest digest does not match its cases and reviewed negatives.");
        if (retirements.Count == 0 || retirements.Any(item => item is null || item.Scope != AllomorphRetirementScope.Affix))
            Refuse(RetirementExpectationTranslationRefusalKind.BindingMismatch,
                "Frozen translation requires one or more authored affix retirement components.");
        ValidateDryRunBinding(proposal, dryRun);

        var mappings = BuildMappings(retirements);
        var mappingByRole = mappings.ToDictionary(RoleKey.From);
        var retirementBundles = BuildBundleMap(retirements, mappingByRole);
        if (retirementBundles.Count == 0)
            Refuse(RetirementExpectationTranslationRefusalKind.IncompleteIdentity,
                "Frozen translation needs at least one exact bundle role with native Form-copy evidence.");
        var bundledRoles = retirementBundles.Values.Select(item => RoleKey.From(item.Mapping)).ToHashSet();
        if (!bundledRoles.SetEquals(mappingByRole.Keys))
            Refuse(RetirementExpectationTranslationRefusalKind.IncompleteIdentity,
                "Every translated role mapping needs at least one exact bundle and native Form-copy effect.");
        var effects = BindBundleEffects(frozen, retirementBundles, proposal, dryRun);
        var translatedCases = TranslateCases(frozen, retirementBundles, mappingByRole);
        RefuseIdentityCollisions(frozen.Cases, translatedCases);
        var translatedNegatives = TranslateAuthoredNegativeRevisions(frozen, sourceBaseline, proposal, dryRun);
        RefuseNegativeContradictions(translatedCases, translatedNegatives);

        var sortedCases = translatedCases.OrderBy(item => item.CaseId, StringComparer.Ordinal).ToArray();
        var sortedNegatives = translatedNegatives.OrderBy(item => item.CaseId, StringComparer.Ordinal).ToArray();
        var manifestDigest = ManifestDigest(sortedCases, sortedNegatives);
        var translated = new FrozenExpectationSet(frozen.Baseline, frozen.HumanInputDigest, manifestDigest,
            FrozenExpectationContractPrefix + manifestDigest, Array.AsReadOnly(sortedCases),
            Array.AsReadOnly(sortedNegatives), frozen.Unavailable);
        translated = FrozenExpectationCodec.Parse(FrozenExpectationCodec.ToJson(translated));

        var mappingDigest = RetirementExpectationTranslationCodec.ComputeMappingDigest(mappings);
        var result = new RetirementExpectationTranslation(frozen, translated, mappings, effects, mappingDigest,
            dryRun.IntentDigest, dryRun.Anchor.FootprintDigest, dryRun.EffectDigest);
        result = RetirementExpectationTranslationCodec.Parse(RetirementExpectationTranslationCodec.ToJson(result));
        ValidatePair(result);
        return result;
    }

    /// <summary>Checks that a stored pair changes only mapped Forms and explicitly revised negatives.</summary>
    public static void ValidatePair(RetirementExpectationTranslation value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var pair = RetirementExpectationTranslationCodec.Parse(RetirementExpectationTranslationCodec.ToJson(value));
        if (ManifestDigest(pair.Original.Cases, pair.Original.ReviewedNegatives) != pair.Original.ManifestDigest ||
            ManifestDigest(pair.Translated.Cases, pair.Translated.ReviewedNegatives) != pair.Translated.ManifestDigest)
            Refuse(RetirementExpectationTranslationRefusalKind.BindingMismatch,
                "A frozen expectation manifest digest does not match its cases and reviewed negatives.");
        var mappings = pair.Mappings.ToDictionary(RoleKey.From);
        var effectsByBundle = pair.BundleTextEffects.ToDictionary(item => GuidOf(item.Bundle));
        var effectBundles = effectsByBundle.Keys.ToHashSet();
        var originalCases = pair.Original.Cases.ToDictionary(item => item.CaseId, StringComparer.Ordinal);
        if (originalCases.Count != pair.Translated.Cases.Count)
            Refuse(RetirementExpectationTranslationRefusalKind.BindingMismatch,
                "The translated expectation set changes the frozen surface-case inventory.");
        foreach (var translatedCase in pair.Translated.Cases)
        {
            if (!originalCases.TryGetValue(translatedCase.CaseId, out var originalCase) ||
                originalCase.Wordform != translatedCase.Wordform ||
                originalCase.WritingSystem != translatedCase.WritingSystem ||
                originalCase.Surface != translatedCase.Surface ||
                originalCase.InSelection != translatedCase.InSelection || originalCase.HeldOut != translatedCase.HeldOut)
                Refuse(RetirementExpectationTranslationRefusalKind.BindingMismatch,
                    "The translated expectation set changes frozen case identity or Selection provenance.");
            var originalReadings = originalCase.Readings.ToDictionary(item => item.ReadingId, StringComparer.Ordinal);
            if (originalReadings.Count != translatedCase.Readings.Count)
                Refuse(RetirementExpectationTranslationRefusalKind.BindingMismatch,
                    "The translated expectation set changes the frozen reading inventory.");
            foreach (var translatedReading in translatedCase.Readings)
            {
                if (!originalReadings.TryGetValue(translatedReading.ReadingId, out var originalReading) ||
                    originalReading.Analysis != translatedReading.Analysis || originalReading.Opinion != translatedReading.Opinion ||
                    originalReading.ProvenanceDigest != translatedReading.ProvenanceDigest ||
                    !originalReading.Evaluations.SequenceEqual(translatedReading.Evaluations) ||
                    originalReading.Morphs.Count != translatedReading.Morphs.Count)
                    Refuse(RetirementExpectationTranslationRefusalKind.BindingMismatch,
                        "The translated expectation set changes reading identity, Opinion or evaluation membership.");
                for (var index = 0; index < originalReading.Morphs.Count; index++)
                {
                    var originalMorph = originalReading.Morphs[index];
                    var translatedMorph = translatedReading.Morphs[index];
                    if (!SameMorphExceptForm(originalMorph, translatedMorph))
                        Refuse(RetirementExpectationTranslationRefusalKind.BindingMismatch,
                            "The translated expectation set changes morphology outside its Form identity.");
                    var bundleIsBound = originalMorph.Bundle is not null && effectBundles.Contains(GuidOf(originalMorph.Bundle));
                    RetirementExpectationMapping? mapping = null;
                    var hasMapping = originalMorph.Form is not null && originalMorph.Msa is not null &&
                        mappings.TryGetValue(RoleKey.From(originalMorph.Form, originalMorph.Msa,
                            originalMorph.InflType, originalMorph.ExpansionRole), out mapping);
                    if (bundleIsBound && hasMapping)
                    {
                        if (!SameId(translatedMorph.Form, mapping!.ReplacementForm))
                            Refuse(RetirementExpectationTranslationRefusalKind.BindingMismatch,
                                "A bound retirement bundle did not use its exact role replacement.");
                    }
                    else if (!NullableSameId(originalMorph.Form, translatedMorph.Form))
                        Refuse(RetirementExpectationTranslationRefusalKind.BindingMismatch,
                            "The translated expectation set changes an unmapped or unbound Form.");
                }
            }
        }

        var originalNegatives = pair.Original.ReviewedNegatives.Select(item => item.CaseId)
            .ToHashSet(StringComparer.Ordinal);
        if (pair.Translated.ReviewedNegatives.Any(item => !originalNegatives.Contains(item.CaseId)))
            Refuse(RetirementExpectationTranslationRefusalKind.BindingMismatch,
                "The translated expectation set adds a reviewed-negative case without an original record.");
        foreach (var effect in pair.BundleTextEffects)
        {
            var bundleId = GuidOf(effect.Bundle);
            var sourceMorphs = pair.Original.Cases.SelectMany(item => item.Readings)
                .SelectMany(item => item.Morphs).Where(item => item.Bundle is not null &&
                    GuidOf(item.Bundle) == bundleId).ToArray();
            if (sourceMorphs.Length == 0 || sourceMorphs.Any(item => item.Form is null || item.Msa is null ||
                    !mappings.ContainsKey(RoleKey.From(item.Form, item.Msa, item.InflType, item.ExpansionRole)) ||
                    !SameAlternatives(item.BundleText, effect.Before)))
                Refuse(RetirementExpectationTranslationRefusalKind.FormCopyMismatch,
                    "A stored bundle text effect has no complete matching frozen source identity and text.");
        }
    }

    /// <summary>Translates one same-entry component without merging it with other retirement components.</summary>
    public static RetirementExpectationTranslation Translate(
        FrozenExpectationSet original,
        BaselineToken sourceBaseline,
        RetireAllomorphIntent retirement,
        Proposal proposal,
        DryRun dryRun) => Translate(original, sourceBaseline, [retirement], proposal, dryRun);

    private const string FrozenExpectationContractPrefix = "frozen-expectations/v1/";

    private static IReadOnlyList<RetirementExpectationMapping> BuildMappings(
        IReadOnlyList<RetireAllomorphIntent> retirements)
    {
        foreach (var component in retirements)
        {
            if (component.RetiredForms is null || component.RetiredForms.Count == 0 ||
                component.RoleReplacements is null || component.RoleReplacements.Count == 0 ||
                component.RoleReplacements.Any(item => item is null || item.Replacement is null))
                Refuse(RetirementExpectationTranslationRefusalKind.IncompleteIdentity,
                    "The retirement component has no complete source or role identity.");
            foreach (var mapping in component.RoleReplacements)
            {
                if (!component.RetiredForms.Any(item => item is not null && SameId(item.Id, mapping.RetiredForm) &&
                        SameId(item.Entry, component.Entry)) ||
                    !SameId(component.Entry, mapping.Replacement.Entry))
                    Refuse(RetirementExpectationTranslationRefusalKind.IncompleteIdentity,
                        "A role mapping names an undeclared source or a replacement outside its entry.");
            }
        }
        var values = retirements.SelectMany(item => item.RoleReplacements)
            .Select(item => new RetirementExpectationMapping(item.RetiredForm, item.Msa, item.InflType,
                item.ExpansionRole, item.Replacement.Id))
            .OrderBy(item => item.RetiredForm, StringComparer.Ordinal)
            .ThenBy(item => item.Msa, StringComparer.Ordinal)
            .ThenBy(item => item.InflType, StringComparer.Ordinal)
            .ThenBy(item => item.ExpansionRole, StringComparer.Ordinal)
            .ThenBy(item => item.ReplacementForm, StringComparer.Ordinal).ToArray();
        if (values.Length == 0)
            Refuse(RetirementExpectationTranslationRefusalKind.IncompleteIdentity,
                "The retirement has no unique exact role mappings.");
        foreach (var item in values)
        {
            if (!CanonicalId.TryParse(item.RetiredForm, out _) || !CanonicalId.TryParse(item.Msa, out _) ||
                (item.InflType is not null && !CanonicalId.TryParse(item.InflType, out _)) ||
                !CanonicalId.TryParse(item.ReplacementForm, out _) ||
                item.ExpansionRole != "whole")
                Refuse(RetirementExpectationTranslationRefusalKind.IncompleteIdentity,
                    "A retirement role mapping has an incomplete or unsupported whole-form identity.");
            if (SameId(item.RetiredForm, item.ReplacementForm))
                Refuse(RetirementExpectationTranslationRefusalKind.IncompleteIdentity,
                    "A retirement role mapping must name a different replacement Form.");
        }
        if (values.GroupBy(RoleKey.From).Any(group => group.Count() != 1))
            Refuse(RetirementExpectationTranslationRefusalKind.IncompleteIdentity,
                "The retirement has no unique exact role mappings.");
        return Array.AsReadOnly(values);
    }

    private static Dictionary<Guid, BundleBinding> BuildBundleMap(
        IReadOnlyList<RetireAllomorphIntent> retirements,
        IReadOnlyDictionary<RoleKey, RetirementExpectationMapping> mappingByRole)
    {
        var result = new Dictionary<Guid, BundleBinding>();
        foreach (var component in retirements)
        foreach (var bundle in component.Bundles)
        {
            var key = RoleKey.From(bundle.RetiredForm, bundle.Msa, bundle.InflType, bundle.ExpansionRole);
            if (!mappingByRole.TryGetValue(key, out var mapping) ||
                !SameId(mapping.RetiredForm, bundle.RetiredForm))
                Refuse(RetirementExpectationTranslationRefusalKind.IncompleteIdentity,
                    $"Bundle '{bundle.Bundle}' has no exact role-qualified retirement mapping.");
            var identity = new BundleBinding(bundle, mapping);
            if (!result.TryAdd(GuidOf(bundle.Bundle), identity))
                Refuse(RetirementExpectationTranslationRefusalKind.IncompleteIdentity,
                    $"Bundle '{bundle.Bundle}' is named more than once for retirement.");
        }
        return result;
    }

    private static IReadOnlyList<RetirementBundleTextEffect> BindBundleEffects(
        FrozenExpectationSet original,
        IReadOnlyDictionary<Guid, BundleBinding> bundles,
        Proposal proposal,
        DryRun dryRun)
    {
        var proposalOperations = proposal.Operations.Where(item =>
            item.Kind == AllomorphRetargetOperationKinds.SetBundleMorph).ToArray();
        var proposalBundles = proposalOperations.Select(item => item.Target?.ToGuid() ?? throw Refusal(
                RetirementExpectationTranslationRefusalKind.BindingMismatch,
                "A bundle Morph operation has no exact target identity.")).ToArray();
        if (proposalBundles.Length != bundles.Count || proposalBundles.Distinct().Count() != bundles.Count ||
            !proposalBundles.ToHashSet().SetEquals(bundles.Keys))
            Refuse(RetirementExpectationTranslationRefusalKind.BindingMismatch,
                "The Proposal bundle Morph operations do not match the authored retirement bundle inventory.");
        var effects = new List<RetirementBundleTextEffect>();
        foreach (var (bundleGuid, binding) in bundles.OrderBy(item => item.Key))
        {
            var bundleId = CanonicalId.FromGuid(bundleGuid);
            var operation = proposalOperations.Where(item => item.Target is { } target && target.ToGuid() == bundleGuid)
                .ToArray();
            if (operation.Length != 1)
                throw Refusal(RetirementExpectationTranslationRefusalKind.BindingMismatch,
                    $"Bundle '{binding.Bundle.Bundle}' has no unique bundle Morph operation in the Proposal.");
            var after = operation[0].After ?? throw Refusal(
                RetirementExpectationTranslationRefusalKind.BindingMismatch,
                $"Bundle '{binding.Bundle.Bundle}' operation has no authored destination.");
            BundleMorphRetargetPayload payload;
            try
            {
                payload = BundleMorphRetargetPayload.Parse(after);
            }
            catch (Exception error) when (error is FormatException or ContractParseException or InvalidOperationException)
            {
                throw Refusal(RetirementExpectationTranslationRefusalKind.BindingMismatch,
                    $"Bundle '{binding.Bundle.Bundle}' has an invalid bundle Morph operation: {error.Message}");
            }
            if (!payload.RetiredForms.Any(item => SameId(item.Value, binding.Bundle.RetiredForm)) ||
                !SameId(payload.RetiredForm.Value, binding.Bundle.RetiredForm) ||
                !SameId(payload.Replacement.Value, binding.Mapping.ReplacementForm))
                Refuse(RetirementExpectationTranslationRefusalKind.BindingMismatch,
                    $"Bundle '{binding.Bundle.Bundle}' operation does not match its authored role mapping.");

            var morphEffect = SingleEffect(dryRun, bundleId, SnapshotFields.WfiMorphBundleMorph);
            var formEffect = SingleEffect(dryRun, bundleId, SnapshotFields.WfiMorphBundleForm);
            if (!HasReference(morphEffect.Before, binding.Bundle.RetiredForm) ||
                !HasReference(morphEffect.After, binding.Mapping.ReplacementForm))
                Refuse(RetirementExpectationTranslationRefusalKind.BindingMismatch,
                    $"Bundle '{binding.Bundle.Bundle}' Morph read-back does not match its authored mapping.");

            var sourceTexts = SourceBundleTexts(original, binding.Bundle);
            if (sourceTexts.Count == 0 || sourceTexts.Any(text => !SameAlternatives(text, formEffect.Before)))
                Refuse(RetirementExpectationTranslationRefusalKind.FormCopyMismatch,
                    $"Bundle '{binding.Bundle.Bundle}' Form before text does not match the frozen source text.");
            effects.Add(new RetirementBundleTextEffect(bundleId.Value,
                NormalizeAlternatives(formEffect.Before), NormalizeAlternatives(formEffect.After)));
        }
        return Array.AsReadOnly(effects.OrderBy(item => item.Bundle, StringComparer.Ordinal).ToArray());
    }

    private static IReadOnlyList<IReadOnlyList<FrozenBundleText>> SourceBundleTexts(
        FrozenExpectationSet original, RetirementBundle bundle)
    {
        var bundleId = GuidOf(bundle.Bundle);
        var analysisId = GuidOf(bundle.Analysis);
        var wordformId = GuidOf(bundle.Wordform);
        var result = new List<IReadOnlyList<FrozenBundleText>>();
        foreach (var item in original.Cases.Where(item => item.Wordform is not null && GuidOf(item.Wordform) == wordformId))
        foreach (var reading in item.Readings.Where(item => item.Analysis is not null && GuidOf(item.Analysis) == analysisId))
        foreach (var morph in reading.Morphs.Where(item => item.Bundle is not null && GuidOf(item.Bundle) == bundleId))
        {
            if (!SameId(morph.Form, bundle.RetiredForm) || !SameId(morph.Msa, bundle.Msa) ||
                !NullableSameId(morph.InflType, bundle.InflType) || morph.ExpansionRole != bundle.ExpansionRole)
                Refuse(RetirementExpectationTranslationRefusalKind.IncompleteIdentity,
                    $"Bundle '{bundle.Bundle}' frozen identity does not match its authored role.");
            result.Add(morph.BundleText);
        }
        if (result.Count == 0)
            Refuse(RetirementExpectationTranslationRefusalKind.IncompleteIdentity,
                $"Bundle '{bundle.Bundle}' is absent from the frozen affected-reading manifest.");
        return result;
    }

    private static ExpectedEffect SingleEffect(DryRun dryRun, CanonicalId bundle, string field)
    {
        var rows = dryRun.ExpectedEffects.Where(item => item.CanonicalId == bundle && item.Field == field).ToArray();
        if (rows.Length != 1)
            Refuse(RetirementExpectationTranslationRefusalKind.FormCopyMismatch,
                $"Bundle '{bundle.Value}' needs exactly one '{field}' read-back effect.");
        return rows[0];
    }

    private static IReadOnlyList<FrozenExpectationCase> TranslateCases(
        FrozenExpectationSet original,
        IReadOnlyDictionary<Guid, BundleBinding> bundles,
        IReadOnlyDictionary<RoleKey, RetirementExpectationMapping> mappingByRole)
    {
        var affectedWordforms = bundles.Values.Select(item => GuidOf(item.Bundle.Wordform)).ToHashSet();
        var retiredForms = mappingByRole.Keys.Select(item => item.Retired).ToHashSet();
        var seenBundles = new HashSet<Guid>();
        var result = new List<FrozenExpectationCase>();
        foreach (var item in original.Cases)
        {
            var affected = item.Wordform is not null && affectedWordforms.Contains(GuidOf(item.Wordform));
            var readings = new List<FrozenExpectedReading>();
            foreach (var reading in item.Readings)
            {
                if (affected && reading.Opinion is "approved" or "disapproved" &&
                    reading.Morphs.Any(morph => morph.Form is null || morph.Msa is null))
                    Refuse(RetirementExpectationTranslationRefusalKind.IncompleteIdentity,
                        $"Affected {reading.Opinion} reading '{reading.ReadingId}' has incomplete morphology identity.");
                var morphs = new List<FrozenExpectationMorph>();
                foreach (var morph in reading.Morphs)
                {
                    if (morph.Form is null || !retiredForms.Contains(GuidOf(morph.Form)))
                    {
                        morphs.Add(morph);
                        continue;
                    }
                    if (morph.Bundle is null)
                        throw Refusal(RetirementExpectationTranslationRefusalKind.IncompleteIdentity,
                            $"Reading '{reading.ReadingId}' names a retired Form without its mapped bundle identity.");
                    if (!bundles.TryGetValue(GuidOf(morph.Bundle), out var binding))
                        throw Refusal(RetirementExpectationTranslationRefusalKind.IncompleteIdentity,
                            $"Reading '{reading.ReadingId}' names a retired Form without its mapped bundle identity.");
                    if (morph.Bundle is null || reading.Analysis is null || item.Wordform is null ||
                        GuidOf(reading.Analysis) != GuidOf(binding.Bundle.Analysis) ||
                        GuidOf(item.Wordform) != GuidOf(binding.Bundle.Wordform) ||
                        GuidOf(morph.Bundle) != GuidOf(binding.Bundle.Bundle) ||
                        !SameId(morph.Msa, binding.Bundle.Msa) ||
                        !NullableSameId(morph.InflType, binding.Bundle.InflType) ||
                        morph.ExpansionRole != binding.Bundle.ExpansionRole)
                        Refuse(RetirementExpectationTranslationRefusalKind.IncompleteIdentity,
                            $"Reading '{reading.ReadingId}' does not identify the exact authored bundle role.");
                    var key = RoleKey.From(morph.Form, morph.Msa, morph.InflType, morph.ExpansionRole);
                    if (!mappingByRole.TryGetValue(key, out var mapping) ||
                        !SameId(mapping.ReplacementForm, binding.Mapping.ReplacementForm))
                        Refuse(RetirementExpectationTranslationRefusalKind.IncompleteIdentity,
                            $"Reading '{reading.ReadingId}' has no exact replacement for its retired Form role.");
                    seenBundles.Add(GuidOf(morph.Bundle));
                    morphs.Add(morph with { Form = mapping.ReplacementForm });
                }
                readings.Add(reading with { Morphs = Array.AsReadOnly(morphs.ToArray()) });
            }
            result.Add(item with { Readings = Array.AsReadOnly(readings.ToArray()) });
        }
        if (!bundles.Keys.ToHashSet().SetEquals(seenBundles))
            Refuse(RetirementExpectationTranslationRefusalKind.IncompleteIdentity,
                "The frozen manifest does not contain every authored retirement bundle.");
        return Array.AsReadOnly(result.ToArray());
    }

    private static IReadOnlyList<FrozenReviewedNegative> TranslateAuthoredNegativeRevisions(
        FrozenExpectationSet original, BaselineToken sourceBaseline, Proposal proposal, DryRun dryRun)
    {
        var candidates = ReadStagedJudgments(sourceBaseline, proposal, dryRun);
        var result = original.ReviewedNegatives.ToDictionary(item => item.CaseId, StringComparer.Ordinal);
        foreach (var negative in original.ReviewedNegatives)
        {
            var revisions = candidates.Where(item => item.Judgment.Replaces.Any(parent =>
                parent.RevisionId == negative.Revision && parent.ContentDigest == negative.ContentDigest)).ToArray();
            if (revisions.Length > 1)
                Refuse(RetirementExpectationTranslationRefusalKind.JudgmentRevisionMismatch,
                    $"Reviewed negative '{negative.CaseId}' has multiple staged replacement revisions.");
            if (revisions.Length == 0) continue;
            var revision = revisions[0];
            if (revision.Judgment.Actor?.Kind != JudgmentActorKind.Human)
                Refuse(RetirementExpectationTranslationRefusalKind.JudgmentRevisionMismatch,
                    $"Staged revision for reviewed negative '{negative.CaseId}' is not an explicitly authored human revision.");
            if (revision.Judgment.Body is RetractionJudgment)
            {
                result.Remove(negative.CaseId);
                continue;
            }
            if (revision.Judgment.Body is not ReviewedNegativeJudgment body || body.CaseId != negative.CaseId)
                Refuse(RetirementExpectationTranslationRefusalKind.JudgmentRevisionMismatch,
                    $"Staged revision for reviewed negative '{negative.CaseId}' changes judgment kind or case identity.");
            result[negative.CaseId] = ToFrozenNegative(revision);
        }
        return Array.AsReadOnly(result.Values.OrderBy(item => item.CaseId, StringComparer.Ordinal).ToArray());
    }

    private static IReadOnlyList<StagedJudgment> ReadStagedJudgments(
        BaselineToken sourceBaseline, Proposal proposal, DryRun dryRun)
    {
        var result = new List<StagedJudgment>();
        foreach (var operation in proposal.Operations.Where(item => item.Kind == HumanJudgmentCustomFieldOperationKinds.Set))
        {
            var target = operation.Target ?? throw Refusal(
                RetirementExpectationTranslationRefusalKind.JudgmentRevisionMismatch,
                "A staged Notebook judgment operation has no exact record and text.");
            var payload = operation.After ?? throw Refusal(
                RetirementExpectationTranslationRefusalKind.JudgmentRevisionMismatch,
                "A staged Notebook judgment operation has no exact record and text.");
            if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty("text", out var textValue) ||
                textValue.ValueKind != JsonValueKind.String)
                throw Refusal(RetirementExpectationTranslationRefusalKind.JudgmentRevisionMismatch,
                    "A staged Notebook judgment operation has no exact record and text.");
            var expected = dryRun.ExpectedEffects.Where(item => item.CanonicalId == target &&
                item.Field == SnapshotFields.RnGenericRecMotifHumanJudgment).ToArray();
            var value = textValue.GetString()!;
            if (expected.Length != 1 || expected[0].Before.Count != 0 ||
                !expected[0].After.TryGetValue("text", out var afterText) || afterText != value)
                Refuse(RetirementExpectationTranslationRefusalKind.JudgmentRevisionMismatch,
                    $"Notebook judgment record '{target.Value}' is not an exact new-record Dry Run effect.");
            if (!Guid.TryParseExact(sourceBaseline.ProjectIdentity, "D", out var projectGuid))
                Refuse(RetirementExpectationTranslationRefusalKind.BindingMismatch,
                    "A staged Notebook revision requires the source Baseline's exact project identity.");
            var projectId = CanonicalId.FromGuid(projectGuid).Value;
            HumanJudgment judgment;
            try
            {
                judgment = HumanJudgmentCodec.Parse(value, projectId, target.Value);
            }
            catch (FormatException error)
            {
                throw Refusal(RetirementExpectationTranslationRefusalKind.JudgmentRevisionMismatch,
                    "A staged Notebook judgment is invalid: " + error.Message);
            }
            result.Add(new StagedJudgment(judgment, HumanJudgmentCodec.LogicalDigest(judgment)));
        }
        return Array.AsReadOnly(result.ToArray());
    }

    private static FrozenReviewedNegative ToFrozenNegative(StagedJudgment staged)
    {
        var body = (ReviewedNegativeJudgment)staged.Judgment.Body;
        var morphs = body.Target is ReadingNegativeTarget reading
            ? reading.Morphs.Select(item => new FrozenExpectationMorph(null, item.Identity.Form, item.Identity.Msa!,
                item.Identity.InflType, "whole", item.Identity.GuessedString, item.GuessedWritingSystem, []))
                .ToArray()
            : [];
        return new FrozenReviewedNegative(body.CaseId, staged.Judgment.RevisionId, staged.ContentDigest,
            body.WritingSystem, body.Form, body.Context, body.Target is SurfaceNegativeTarget ? "surface" : "reading",
            Array.AsReadOnly(morphs));
    }

    private static void ValidateDryRunBinding(Proposal proposal, DryRun dryRun)
    {
        var expectedIntent = IntentDigest.Compute(proposal);
        var expectedEffects = ExpectedEffectSetDigest.Compute(dryRun.ExpectedEffects);
        if (dryRun.IntentDigest != expectedIntent || dryRun.Anchor.IntentDigest != expectedIntent ||
            dryRun.EffectDigest != expectedEffects || dryRun.Anchor.EffectDigest != expectedEffects)
            Refuse(RetirementExpectationTranslationRefusalKind.BindingMismatch,
                "The Dry Run does not bind the supplied Proposal and actual expected effects.");
        if (!Sha256Value.IsCanonical(dryRun.Anchor.FootprintDigest))
            Refuse(RetirementExpectationTranslationRefusalKind.BindingMismatch,
                "The Dry Run does not carry a canonical before-footprint digest.");
    }

    private static bool HasReference(IReadOnlyDictionary<string, string> value, string id) =>
        value.TryGetValue("ref", out var actual) && SameId(actual, id);

    private static bool SameAlternatives(IReadOnlyList<FrozenBundleText> frozen,
        IReadOnlyDictionary<string, string> actual)
    {
        var expected = frozen.Where(item => !string.IsNullOrEmpty(item.Text))
            .ToDictionary(item => item.WritingSystem, item => item.Text.Normalize(System.Text.NormalizationForm.FormD),
                StringComparer.Ordinal);
        var normalized = NormalizeAlternatives(actual);
        return expected.Count == normalized.Count && expected.All(item =>
            normalized.TryGetValue(item.Key, out var text) && text == item.Value);
    }

    private static IReadOnlyDictionary<string, string> NormalizeAlternatives(IReadOnlyDictionary<string, string> values) =>
        new SortedDictionary<string, string>(values.Where(item => !string.IsNullOrEmpty(item.Value))
            .ToDictionary(item => item.Key, item => item.Value.Normalize(System.Text.NormalizationForm.FormD),
                StringComparer.Ordinal), StringComparer.Ordinal);

    private static void RefuseIdentityCollisions(
        IReadOnlyList<FrozenExpectationCase> original,
        IReadOnlyList<FrozenExpectationCase> translated)
    {
        var translatedByCase = translated.ToDictionary(item => item.CaseId, StringComparer.Ordinal);
        foreach (var beforeCase in original)
        {
            var afterCase = translatedByCase[beforeCase.CaseId];
            var signatures = beforeCase.Readings.Join(afterCase.Readings, item => item.ReadingId,
                item => item.ReadingId, (before, after) => new ReadingSignature(before, after))
                .Select(item => new SignaturePair(Signature(item.Before.Morphs), Signature(item.After.Morphs), item.Before.Opinion))
                .ToArray();
            foreach (var group in signatures.GroupBy(item => item.After, StringComparer.Ordinal))
            {
                if (group.Any(item => item.Opinion == "approved") && group.Any(item => item.Opinion == "disapproved"))
                    Refuse(RetirementExpectationTranslationRefusalKind.OpinionContradiction,
                        $"An Approved and Disapproved reading in case '{beforeCase.CaseId}' have the same translated morphology.");
                if (group.Select(item => item.Before).Distinct(StringComparer.Ordinal).Count() > 1)
                    Refuse(RetirementExpectationTranslationRefusalKind.ManyToOneCollision,
                        $"Distinct readings in case '{beforeCase.CaseId}' collapse to one translated morphology.");
            }
        }
    }

    private static void RefuseNegativeContradictions(
        IReadOnlyList<FrozenExpectationCase> cases,
        IReadOnlyList<FrozenReviewedNegative> negatives)
    {
        foreach (var negative in negatives)
        foreach (var item in cases.Where(item => item.WritingSystem == negative.WritingSystem && item.Surface == negative.Surface))
        {
            var approved = item.Readings.Where(reading => reading.Opinion == "approved").ToArray();
            if (negative.Target == "surface" && approved.Length > 0)
                Refuse(RetirementExpectationTranslationRefusalKind.OpinionContradiction,
                    $"Reviewed surface negative '{negative.CaseId}' contradicts an Approved reading for its word case.");
            if (negative.Target != "reading") continue;
            var negativeSignature = Signature(negative.Morphs);
            if (approved.Any(reading => Signature(reading.Morphs) == negativeSignature))
                Refuse(RetirementExpectationTranslationRefusalKind.OpinionContradiction,
                    $"Reviewed negative '{negative.CaseId}' contradicts an Approved translated reading.");
        }
    }

    private static string Signature(IReadOnlyList<FrozenExpectationMorph> morphs) => string.Join('\u001e', morphs.Select(item =>
        string.Join('\0', IdentityPart(item.Form), IdentityPart(item.Msa), IdentityPart(item.InflType),
            item.GuessedString ?? "<no-guess>")));

    private static string IdentityPart(string? value) => value is null ? "<null>" : GuidOf(value).ToString("D");

    private static string ManifestDigest(
        IReadOnlyList<FrozenExpectationCase> cases, IReadOnlyList<FrozenReviewedNegative> negatives)
    {
        var json = JsonSerializer.Serialize(new { cases, reviewedNegatives = negatives }, JsonOptions);
        return IntentDigest.Sha256Of(CanonicalJson.CanonicalizeToUtf8(json));
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static bool NullableSameId(string? left, string? right) => left is null
        ? right is null
        : right is not null && SameId(left, right);

    private static bool SameMorphExceptForm(FrozenExpectationMorph left, FrozenExpectationMorph right) =>
        NullableSameId(left.Bundle, right.Bundle) && NullableSameId(left.Msa, right.Msa) &&
        NullableSameId(left.InflType, right.InflType) && left.ExpansionRole == right.ExpansionRole &&
        left.GuessedString == right.GuessedString && left.GuessedWritingSystem == right.GuessedWritingSystem &&
        left.BundleText.SequenceEqual(right.BundleText);

    private static bool SameId(string? left, string right) =>
        left is not null && CanonicalId.TryParse(left, out var leftId) &&
        CanonicalId.TryParse(right, out var rightId) && leftId.ToGuid() == rightId.ToGuid();

    private static Guid GuidOf(string value) =>
        CanonicalId.TryParse(value, out var id) ? id.ToGuid() : throw new FormatException("A portable identity is invalid.");

    [DoesNotReturn]
    private static void Refuse(RetirementExpectationTranslationRefusalKind kind, string message) => throw Refusal(kind, message);
    private static RetirementExpectationTranslationRefusalException Refusal(
        RetirementExpectationTranslationRefusalKind kind, string message) => new(kind, message);

    private sealed record BundleBinding(RetirementBundle Bundle, RetirementExpectationMapping Mapping);
    private sealed record StagedJudgment(HumanJudgment Judgment, string ContentDigest);
    private sealed record ReadingSignature(FrozenExpectedReading Before, FrozenExpectedReading After);
    private sealed record SignaturePair(string Before, string After, string Opinion);

    private readonly record struct RoleKey(Guid Retired, Guid Msa, Guid? InflType, string ExpansionRole)
    {
        public static RoleKey From(RetirementExpectationMapping item) =>
            From(item.RetiredForm, item.Msa, item.InflType, item.ExpansionRole);

        public static RoleKey From(string retired, string msa, string? inflType, string role) =>
            new(GuidOf(retired), GuidOf(msa), inflType is null ? null : GuidOf(inflType), role);
    }
}
