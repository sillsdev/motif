using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>Sets the exact exception features on a stem or clitic, or the exact ones an inflectional affix requires.</summary>
/// <param name="Msa">The grammatical info (an MoStemMsa or an MoInflAffMsa) of an existing entry.</param>
/// <param name="ExceptionFeatures">
/// The exact set. On a stem it is the features the stem carries; on an inflectional affix it is the features a stem must
/// carry, all of them, for the affix to attach. An empty list clears the set.
/// </param>
public sealed record EditExceptionFeaturesIntent(CanonicalId Msa, IReadOnlyList<CanonicalId> ExceptionFeatures);

/// <summary>Parses the closed JSON intent for editing exception features.</summary>
public static class EditExceptionFeaturesIntentParser
{
    /// <summary>Reads the grammatical info and the exact feature set, which may be empty.</summary>
    public static EditExceptionFeaturesIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, "EditExceptionFeatures", "msa", "exceptionFeatures");
        return new EditExceptionFeaturesIntent(SoundSystemIntentParsing.Id(authored, "msa"),
            SoundSystemIntentParsing.Ids(authored, "exceptionFeatures", ordered: false));
    }
}

/// <summary>
/// Lowers one <see cref="EditExceptionFeaturesIntent"/> into add and remove operations on a stem's exception features
/// or an inflectional affix's From Exception Features. A derivational affix keeps its own composer.
/// </summary>
public static class EditExceptionFeaturesComposer
{
    private const string ConstructName = "EditExceptionFeatures";
    private const string Rationale = "Authored by the EditExceptionFeatures composer.";

    /// <summary>Builds the difference between the current set and the wanted set.</summary>
    /// <param name="cache">The project to resolve <paramref name="intent"/>'s references against.</param>
    /// <param name="intent">The authored set.</param>
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, EditExceptionFeaturesIntent intent)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var msaId = LexicalFormAuthoring.Normalize(intent.Msa);
        var msa = LexicalFormAuthoring.Resolve<IMoMorphSynAnalysis>(cache, msaId, ConstructName);
        string addKind, removeKind;
        IEnumerable<ICmPossibility> current;
        switch (msa)
        {
            case IMoStemMsa stem:
                addKind = MoStemMsaProdRestrictOperationKinds.AddRefProdRestrict;
                removeKind = MoStemMsaProdRestrictOperationKinds.RemoveRefProdRestrict;
                current = stem.ProdRestrictRC;
                break;
            case IMoInflAffMsa inflectional:
                addKind = MoInflAffMsaFromProdRestrictOperationKinds.AddRefFromProdRestrict;
                removeKind = MoInflAffMsaFromProdRestrictOperationKinds.RemoveRefFromProdRestrict;
                current = inflectional.FromProdRestrictRC;
                break;
            case IMoDerivAffMsa:
                throw new InvalidOperationException(
                    $"'{ConstructName}': '{msaId.Value}' is a derivational affix; set its From Exception Features with " +
                    "compose-edit-derivational-affix-restriction.");
            default:
                throw new InvalidOperationException(
                    $"'{ConstructName}': '{msaId.Value}' is not the grammatical info of a stem or an inflectional affix. " +
                    "An unclassified affix has no exception features.");
        }

        var wanted = intent.ExceptionFeatures.Select(LexicalFormAuthoring.Normalize)
            .OrderBy(id => id.Value, StringComparer.Ordinal).ToArray();
        if (wanted.Select(id => id.ToGuid()).Distinct().Count() != wanted.Length)
            throw new InvalidOperationException($"'{ConstructName}': 'exceptionFeatures' must not repeat an identity.");
        var allowed = cache.LangProject.MorphologicalDataOA?.ProdRestrictOA?.ReallyReallyAllPossibilities
            .Select(item => item.Guid).ToHashSet() ?? [];
        foreach (var featureId in wanted.Where(id => !allowed.Contains(id.ToGuid())))
            throw new InvalidOperationException(
                $"'{ConstructName}': '{featureId.Value}' is not one of the project's exception features.");

        var currentIds = current.Select(item => item.Guid).ToHashSet();
        var wantedIds = wanted.Select(id => id.ToGuid()).ToHashSet();
        var operations = new List<OperationEnvelope>();
        foreach (var existing in current.Select(item => CanonicalId.FromGuid(item.Guid))
                     .Where(id => !wantedIds.Contains(id.ToGuid())).OrderBy(id => id.Value, StringComparer.Ordinal))
            operations.Add(Member(removeKind, existing));
        foreach (var featureId in wanted.Where(id => !currentIds.Contains(id.ToGuid())))
            operations.Add(Member(addKind, featureId));
        return operations;

        OperationEnvelope Member(string kind, CanonicalId featureId) => new(CanonicalId.Mint(), kind, target: msaId,
            after: JsonSerializer.SerializeToElement(new { member = featureId.Value }), rationale: Rationale);
    }
}
