using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>Restricts the stems a derivational affix attaches to, on the affix entry's derivational grammatical info.</summary>
/// <param name="Msa">The derivational grammatical info (a MoDerivAffMsa) of an existing affix entry.</param>
/// <param name="FromInflectionClass">The inflection class, of the input category or an ancestor, that stems must belong to.</param>
/// <param name="FromExceptionFeatures">The exact From Exception Features set; an empty list clears the links when supplied.</param>
/// <param name="ClearFromInflectionClass">When true, clears the existing From Inflection Class; false leaves it unchanged.</param>
public sealed record EditDerivationalAffixRestrictionIntent(CanonicalId Msa, CanonicalId? FromInflectionClass = null,
    IReadOnlyList<CanonicalId>? FromExceptionFeatures = null, bool ClearFromInflectionClass = false);

/// <summary>Parses the closed JSON intent for restricting a derivational affix's input.</summary>
public static class EditDerivationalAffixRestrictionIntentParser
{
    private const string ConstructName = "EditDerivationalAffixRestriction";

    /// <summary>Reads the grammatical info, the optional class clear or replacement, and exception features.</summary>
    public static EditDerivationalAffixRestrictionIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, ConstructName, "msa", "fromInflectionClass", "clearFromInflectionClass",
            "fromExceptionFeatures");
        var clearClass = false;
        if (authored.TryGetProperty("clearFromInflectionClass", out var clearValue))
        {
            if (clearValue.ValueKind != JsonValueKind.True)
                throw new ContractParseException($"'{ConstructName}': 'clearFromInflectionClass' must be true when supplied.");
            clearClass = true;
        }
        var result = new EditDerivationalAffixRestrictionIntent(SoundSystemIntentParsing.Id(authored, "msa"),
            authored.TryGetProperty("fromInflectionClass", out _)
                ? SoundSystemIntentParsing.Id(authored, "fromInflectionClass") : null,
            authored.TryGetProperty("fromExceptionFeatures", out _)
                ? SoundSystemIntentParsing.Ids(authored, "fromExceptionFeatures", ordered: false) : null,
            clearClass);
        if (result.FromInflectionClass is not null && result.ClearFromInflectionClass)
            throw new ContractParseException($"'{ConstructName}': give 'fromInflectionClass' or 'clearFromInflectionClass', not both.");
        if (result.FromInflectionClass is null && !result.ClearFromInflectionClass && result.FromExceptionFeatures is null)
            throw new ContractParseException(
                $"'{ConstructName}': give 'fromInflectionClass', 'clearFromInflectionClass' or 'fromExceptionFeatures'.");
        return result;
    }
}

/// <summary>
/// Lowers one <see cref="EditDerivationalAffixRestrictionIntent"/> into operations on a MoDerivAffMsa's
/// FromInflectionClass and FromProdRestrict; class behavior is pinned by
/// <c>ADerivationalPrefixRestrictedToAClassStopsApplyingToUnmarkedStems</c>.
/// </summary>
public static class EditDerivationalAffixRestrictionComposer
{
    private const string ConstructName = "EditDerivationalAffixRestriction";
    private const string Rationale = "Authored by the EditDerivationalAffixRestriction composer.";

    /// <summary>Builds the restriction operations without mutating the caller's project.</summary>
    /// <param name="cache">The project to resolve <paramref name="intent"/>'s references against.</param>
    /// <param name="intent">The authored restriction.</param>
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, EditDerivationalAffixRestrictionIntent intent)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        if (intent.FromInflectionClass is not null && intent.ClearFromInflectionClass)
            throw new InvalidOperationException(
                $"'{ConstructName}': give 'fromInflectionClass' or 'clearFromInflectionClass', not both.");
        if (intent.FromInflectionClass is null && !intent.ClearFromInflectionClass && intent.FromExceptionFeatures is null)
            throw new InvalidOperationException(
                $"'{ConstructName}': give 'fromInflectionClass', 'clearFromInflectionClass' or 'fromExceptionFeatures'.");
        var msaId = LexicalFormAuthoring.Normalize(intent.Msa);
        var msa = LexicalFormAuthoring.Resolve<IMoMorphSynAnalysis>(cache, msaId, ConstructName);
        if (msa is not IMoDerivAffMsa derivational || msa.Owner is not ILexEntry entry)
            throw new InvalidOperationException(
                $"'{ConstructName}': '{msaId.Value}' is not the derivational grammatical info of an affix entry. " +
                "Only a derivational affix takes From Inflection Class and From Exception Features; an inflectional " +
                "affix is restricted with the inflection classes of its form (compose-edit-affix-inflection-classes).");
        if (entry.LexemeFormOA is not IMoAffixAllomorph && entry.LexemeFormOA is not IMoAffixProcess)
            throw new InvalidOperationException(
                $"'{ConstructName}': '{msaId.Value}' belongs to an entry whose lexeme form is not an affix form.");
        if (derivational.FromPartOfSpeechRA is not { } fromCategory)
            throw new InvalidOperationException(
                $"'{ConstructName}': the derivational grammatical info has no From category, so there is no category " +
                "to take an inflection class or exception features from.");
        var operations = new List<OperationEnvelope>();
        if (intent.FromInflectionClass is { } classId)
        {
            var wanted = LexicalFormAuthoring.RequireInflectionClasses(cache, [classId], [fromCategory], ConstructName)[0];
            if (derivational.FromInflectionClassRA?.Guid != wanted.ToGuid())
                operations.Add(new OperationEnvelope(CanonicalId.Mint(), MoDerivAffMsaFromInflectionClassOperationKinds.SetFromInflectionClass,
                    target: msaId, after: JsonSerializer.SerializeToElement(new { @ref = wanted.Value }), rationale: Rationale));
        }
        else if (intent.ClearFromInflectionClass && derivational.FromInflectionClassRA is not null)
        {
            operations.Add(new OperationEnvelope(CanonicalId.Mint(),
                MoDerivAffMsaFromInflectionClassOperationKinds.ClearFromInflectionClass,
                target: msaId, after: JsonSerializer.SerializeToElement(new { }), rationale: Rationale));
        }
        if (intent.FromExceptionFeatures is { } featureIds)
        {
            var wantedFeatures = featureIds.Select(LexicalFormAuthoring.Normalize).OrderBy(id => id.Value, StringComparer.Ordinal).ToArray();
            if (wantedFeatures.Select(id => id.ToGuid()).Distinct().Count() != wantedFeatures.Length)
                throw new InvalidOperationException($"'{ConstructName}': 'fromExceptionFeatures' must not repeat an identity.");
            var allowed = cache.LangProject.MorphologicalDataOA?.ProdRestrictOA?.ReallyReallyAllPossibilities
                .Select(item => item.Guid).ToHashSet() ?? [];
            foreach (var featureId in wantedFeatures)
            {
                if (!allowed.Contains(featureId.ToGuid()))
                    throw new InvalidOperationException(
                        $"'{ConstructName}': '{featureId.Value}' is not one of the project's exception features.");
            }
            var currentFeatures = derivational.FromProdRestrictRC.Select(item => CanonicalId.FromGuid(item.Guid))
                .OrderBy(id => id.Value, StringComparer.Ordinal).ToArray();
            var currentIds = currentFeatures.Select(id => id.ToGuid()).ToHashSet();
            var wantedIds = wantedFeatures.Select(id => id.ToGuid()).ToHashSet();
            foreach (var featureId in currentFeatures.Where(id => !wantedIds.Contains(id.ToGuid())))
                operations.Add(FeatureOperation(MoDerivAffMsaFromProdRestrictOperationKinds.RemoveRefFromProdRestrict, featureId));
            foreach (var featureId in wantedFeatures.Where(id => !currentIds.Contains(id.ToGuid())))
                operations.Add(FeatureOperation(MoDerivAffMsaFromProdRestrictOperationKinds.AddRefFromProdRestrict, featureId));
        }
        return operations;

        OperationEnvelope FeatureOperation(string kind, CanonicalId featureId) => new(CanonicalId.Mint(), kind,
            target: msaId, after: JsonSerializer.SerializeToElement(new { member = featureId.Value }), rationale: Rationale);
    }
}
