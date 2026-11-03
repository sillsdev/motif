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

/// <summary>Adds the same exception features to many stems, clitics or inflectional affixes, keeping what each already has.</summary>
/// <param name="Msas">The grammatical info (MoStemMsa or MoInflAffMsa) of each existing entry to tag.</param>
/// <param name="ExceptionFeatures">The features to add to every one of them; a feature an analysis already has is left alone.</param>
public sealed record AddExceptionFeaturesIntent(
    IReadOnlyList<CanonicalId> Msas, IReadOnlyList<CanonicalId> ExceptionFeatures);

/// <summary>Parses the closed JSON intent for adding exception features to many analyses.</summary>
public static class AddExceptionFeaturesIntentParser
{
    /// <summary>Reads the non-empty analysis list and the non-empty feature list.</summary>
    public static AddExceptionFeaturesIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, "AddExceptionFeatures", "msas", "exceptionFeatures");
        var msas = SoundSystemIntentParsing.Ids(authored, "msas", ordered: true);
        var features = SoundSystemIntentParsing.Ids(authored, "exceptionFeatures", ordered: false);
        if (msas.Length == 0 || features.Length == 0)
            throw new ContractParseException(
                "'AddExceptionFeatures': 'msas' and 'exceptionFeatures' must each name at least one id.");
        return new AddExceptionFeaturesIntent(msas, features);
    }
}

/// <summary>
/// Lowers one <see cref="AddExceptionFeaturesIntent"/> into one add operation for each feature an analysis does not
/// yet carry, so a lexical class of many stems is tagged in one step and nothing already set is removed.
/// </summary>
public static class AddExceptionFeaturesComposer
{
    private const string ConstructName = "AddExceptionFeatures";
    private const string Rationale = "Authored by the AddExceptionFeatures composer.";

    /// <summary>Builds the additions; an analysis that already has every feature contributes nothing.</summary>
    /// <param name="cache">The project to resolve <paramref name="intent"/>'s references against.</param>
    /// <param name="intent">The authored analyses and features.</param>
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, AddExceptionFeaturesIntent intent)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var msas = intent.Msas.Select(LexicalFormAuthoring.Normalize).ToArray();
        var features = intent.ExceptionFeatures.Select(LexicalFormAuthoring.Normalize)
            .OrderBy(id => id.Value, StringComparer.Ordinal).ToArray();
        if (msas.Length == 0 || features.Length == 0)
            throw new InvalidOperationException($"'{ConstructName}': 'msas' and 'exceptionFeatures' must each name at least one id.");
        if (msas.Select(id => id.ToGuid()).Distinct().Count() != msas.Length)
            throw new InvalidOperationException($"'{ConstructName}': 'msas' must not repeat an identity.");
        if (features.Select(id => id.ToGuid()).Distinct().Count() != features.Length)
            throw new InvalidOperationException($"'{ConstructName}': 'exceptionFeatures' must not repeat an identity.");
        ExceptionFeatureTargets.RequireProjectFeatures(cache, features, ConstructName);

        var operations = new List<OperationEnvelope>();
        foreach (var msaId in msas)
        {
            var (addKind, _, current) = ExceptionFeatureTargets.Resolve(cache, msaId, ConstructName);
            var held = current.Select(item => item.Guid).ToHashSet();
            foreach (var featureId in features.Where(id => !held.Contains(id.ToGuid())))
                operations.Add(new OperationEnvelope(CanonicalId.Mint(), addKind, target: msaId,
                    after: JsonSerializer.SerializeToElement(new { member = featureId.Value }), rationale: Rationale));
        }
        return operations;
    }
}
