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

/// <summary>Restricts the stems a derivational affix attaches to and the class of the stems it derives, on the affix entry's derivational grammatical info.</summary>
/// <param name="Msa">The derivational grammatical info (a MoDerivAffMsa) of an existing affix entry.</param>
/// <param name="FromInflectionClass">The inflection class, of the input category or an ancestor, that stems must belong to.</param>
/// <param name="FromExceptionFeatures">The exact From Exception Features set; an empty list clears the links when supplied.</param>
/// <param name="ClearFromInflectionClass">When true, clears the existing From Inflection Class; false leaves it unchanged.</param>
/// <param name="ToInflectionClass">The inflection class, of the output category or an ancestor, that derived stems receive.</param>
/// <param name="ClearToInflectionClass">When true, clears the existing To Inflection Class; false leaves it unchanged.</param>
/// <param name="ToExceptionFeatures">The exact To Exception Features set the derived stem receives; an empty list clears the links when supplied.</param>
/// <param name="AlsoMsas">Further derivational grammatical info that receives the identical change, so one intent edits many affixes.</param>
public sealed record EditDerivationalAffixRestrictionIntent(CanonicalId Msa, CanonicalId? FromInflectionClass = null,
    IReadOnlyList<CanonicalId>? FromExceptionFeatures = null, bool ClearFromInflectionClass = false,
    CanonicalId? ToInflectionClass = null, bool ClearToInflectionClass = false,
    IReadOnlyList<CanonicalId>? ToExceptionFeatures = null, IReadOnlyList<CanonicalId>? AlsoMsas = null);

/// <summary>Parses the closed JSON intent for restricting a derivational affix's input.</summary>
public static class EditDerivationalAffixRestrictionIntentParser
{
    private const string ConstructName = "EditDerivationalAffixRestriction";

    /// <summary>Reads the grammatical info, the optional class clear or replacement, and exception features.</summary>
    public static EditDerivationalAffixRestrictionIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, ConstructName, "msa", "fromInflectionClass", "clearFromInflectionClass",
            "fromExceptionFeatures", "toInflectionClass", "clearToInflectionClass", "toExceptionFeatures", "alsoMsas");
        var clearClass = ClearFlag(authored, "clearFromInflectionClass");
        var clearToClass = ClearFlag(authored, "clearToInflectionClass");
        var result = new EditDerivationalAffixRestrictionIntent(SoundSystemIntentParsing.Id(authored, "msa"),
            authored.TryGetProperty("fromInflectionClass", out _)
                ? SoundSystemIntentParsing.Id(authored, "fromInflectionClass") : null,
            authored.TryGetProperty("fromExceptionFeatures", out _)
                ? SoundSystemIntentParsing.Ids(authored, "fromExceptionFeatures", ordered: false) : null,
            clearClass,
            authored.TryGetProperty("toInflectionClass", out _)
                ? SoundSystemIntentParsing.Id(authored, "toInflectionClass") : null,
            clearToClass,
            authored.TryGetProperty("toExceptionFeatures", out _)
                ? SoundSystemIntentParsing.Ids(authored, "toExceptionFeatures", ordered: false) : null,
            authored.TryGetProperty("alsoMsas", out _)
                ? SoundSystemIntentParsing.Ids(authored, "alsoMsas", ordered: true) : null);
        if (result.FromInflectionClass is not null && result.ClearFromInflectionClass)
            throw new ContractParseException($"'{ConstructName}': give 'fromInflectionClass' or 'clearFromInflectionClass', not both.");
        if (result.ToInflectionClass is not null && result.ClearToInflectionClass)
            throw new ContractParseException($"'{ConstructName}': give 'toInflectionClass' or 'clearToInflectionClass', not both.");
        if (!HasChange(result))
            throw new ContractParseException($"'{ConstructName}': {NothingToChange}");
        return result;
    }

    internal const string NothingToChange = "give 'fromInflectionClass', 'clearFromInflectionClass', " +
        "'fromExceptionFeatures', 'toInflectionClass', 'clearToInflectionClass' or 'toExceptionFeatures'.";

    internal static bool HasChange(EditDerivationalAffixRestrictionIntent intent) =>
        intent.FromInflectionClass is not null || intent.ClearFromInflectionClass || intent.FromExceptionFeatures is not null
        || intent.ToInflectionClass is not null || intent.ClearToInflectionClass || intent.ToExceptionFeatures is not null;

    private static bool ClearFlag(JsonElement authored, string name)
    {
        if (!authored.TryGetProperty(name, out var value)) return false;
        if (value.ValueKind != JsonValueKind.True)
            throw new ContractParseException($"'{ConstructName}': '{name}' must be true when supplied.");
        return true;
    }
}

/// <summary>
/// Lowers one <see cref="EditDerivationalAffixRestrictionIntent"/> into operations on each named MoDerivAffMsa's
/// FromInflectionClass, ToInflectionClass, FromProdRestrict and ToProdRestrict; class behavior is pinned by
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
        if (intent.ToInflectionClass is not null && intent.ClearToInflectionClass)
            throw new InvalidOperationException(
                $"'{ConstructName}': give 'toInflectionClass' or 'clearToInflectionClass', not both.");
        if (!EditDerivationalAffixRestrictionIntentParser.HasChange(intent))
            throw new InvalidOperationException($"'{ConstructName}': {EditDerivationalAffixRestrictionIntentParser.NothingToChange}");
        var targets = new[] { intent.Msa }.Concat(intent.AlsoMsas ?? []).Select(LexicalFormAuthoring.Normalize).ToArray();
        if (targets.Select(id => id.ToGuid()).Distinct().Count() != targets.Length)
            throw new InvalidOperationException($"'{ConstructName}': 'msa' and 'alsoMsas' must not repeat an identity.");
        return targets.SelectMany(target => BuildOne(cache, intent, target)).ToArray();
    }

    private static IReadOnlyList<OperationEnvelope> BuildOne(
        LcmCache cache, EditDerivationalAffixRestrictionIntent intent, CanonicalId msaId)
    {
        var msa = LexicalFormAuthoring.Resolve<IMoMorphSynAnalysis>(cache, msaId, ConstructName);
        if (msa is not IMoDerivAffMsa derivational || msa.Owner is not ILexEntry entry)
            throw new InvalidOperationException(
                $"'{ConstructName}': '{msaId.Value}' is not the derivational grammatical info of an affix entry. " +
                "Only a derivational affix takes From Inflection Class and From Exception Features; an inflectional " +
                "affix is restricted with the inflection classes of its form (compose-edit-affix-inflection-classes).");
        if (entry.LexemeFormOA is not IMoAffixAllomorph && entry.LexemeFormOA is not IMoAffixProcess)
            throw new InvalidOperationException(
                $"'{ConstructName}': '{msaId.Value}' belongs to an entry whose lexeme form is not an affix form.");
        var wantsFrom = intent.FromInflectionClass is not null || intent.FromExceptionFeatures is not null;
        if (intent.ToExceptionFeatures is not null && derivational.ToPartOfSpeechRA is null)
            throw new InvalidOperationException(
                $"'{ConstructName}': the derivational grammatical info has no To category, so there is no category " +
                "to take To Exception Features from.");
        if (wantsFrom && derivational.FromPartOfSpeechRA is null)
            throw new InvalidOperationException(
                $"'{ConstructName}': the derivational grammatical info has no From category, so there is no category " +
                "to take an inflection class or exception features from.");
        if (intent.ToInflectionClass is not null && derivational.ToPartOfSpeechRA is null)
            throw new InvalidOperationException(
                $"'{ConstructName}': the derivational grammatical info has no To category, so there is no category " +
                "to take a To Inflection Class from.");
        var operations = new List<OperationEnvelope>();
        if (intent.FromInflectionClass is { } classId)
        {
            var wanted = LexicalFormAuthoring.RequireInflectionClasses(cache, [classId], [derivational.FromPartOfSpeechRA!], ConstructName)[0];
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
        if (intent.ToInflectionClass is { } toClassId)
        {
            var wanted = LexicalFormAuthoring.RequireInflectionClasses(cache, [toClassId], [derivational.ToPartOfSpeechRA!], ConstructName)[0];
            if (derivational.ToInflectionClassRA?.Guid != wanted.ToGuid())
                operations.Add(new OperationEnvelope(CanonicalId.Mint(), MoDerivAffMsaToInflectionClassOperationKinds.SetToInflectionClass,
                    target: msaId, after: JsonSerializer.SerializeToElement(new { @ref = wanted.Value }), rationale: Rationale));
        }
        else if (intent.ClearToInflectionClass && derivational.ToInflectionClassRA is not null)
        {
            operations.Add(new OperationEnvelope(CanonicalId.Mint(),
                MoDerivAffMsaToInflectionClassOperationKinds.ClearToInflectionClass,
                target: msaId, after: JsonSerializer.SerializeToElement(new { }), rationale: Rationale));
        }
        if (intent.FromExceptionFeatures is { } fromIds)
            operations.AddRange(ExceptionSet(cache, msaId, fromIds, derivational.FromProdRestrictRC, "fromExceptionFeatures",
                MoDerivAffMsaFromProdRestrictOperationKinds.RemoveRefFromProdRestrict,
                MoDerivAffMsaFromProdRestrictOperationKinds.AddRefFromProdRestrict));
        if (intent.ToExceptionFeatures is { } toIds)
            operations.AddRange(ExceptionSet(cache, msaId, toIds, derivational.ToProdRestrictRC, "toExceptionFeatures",
                MoDerivAffMsaToProdRestrictOperationKinds.RemoveRefToProdRestrict,
                MoDerivAffMsaToProdRestrictOperationKinds.AddRefToProdRestrict));
        return operations;

    }

    private static IEnumerable<OperationEnvelope> ExceptionSet(LcmCache cache, CanonicalId msaId,
        IReadOnlyList<CanonicalId> featureIds, IEnumerable<ICmPossibility> current, string field, string removeKind, string addKind)
    {
        var wantedFeatures = featureIds.Select(LexicalFormAuthoring.Normalize).OrderBy(id => id.Value, StringComparer.Ordinal).ToArray();
        if (wantedFeatures.Select(id => id.ToGuid()).Distinct().Count() != wantedFeatures.Length)
            throw new InvalidOperationException($"'{ConstructName}': '{field}' must not repeat an identity.");
        ExceptionFeatureTargets.RequireProjectFeatures(cache, wantedFeatures, ConstructName);
        var currentFeatures = current.Select(item => CanonicalId.FromGuid(item.Guid))
            .OrderBy(id => id.Value, StringComparer.Ordinal).ToArray();
        var currentIds = currentFeatures.Select(id => id.ToGuid()).ToHashSet();
        var wantedIds = wantedFeatures.Select(id => id.ToGuid()).ToHashSet();
        foreach (var featureId in currentFeatures.Where(id => !wantedIds.Contains(id.ToGuid())))
            yield return FeatureOperation(removeKind, featureId);
        foreach (var featureId in wantedFeatures.Where(id => !currentIds.Contains(id.ToGuid())))
            yield return FeatureOperation(addKind, featureId);

        OperationEnvelope FeatureOperation(string kind, CanonicalId featureId) => new(CanonicalId.Mint(), kind,
            target: msaId, after: JsonSerializer.SerializeToElement(new { member = featureId.Value }), rationale: Rationale);
    }
}
