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

/// <summary>Sets which exception features or inflection classes a phonological rule requires or excludes.</summary>
/// <param name="Rule">An existing regular rewrite rule with one right-hand side.</param>
/// <param name="RequiredFeatures">
/// The exact set the word must carry, every one of them, for the rule to apply; null leaves it unchanged, empty clears it.
/// </param>
/// <param name="ExcludedFeatures">
/// The exact set the word must carry none of for the rule to apply; null leaves it unchanged, empty clears it.
/// </param>
public sealed record EditPhonologicalRuleFeaturesIntent(CanonicalId Rule,
    IReadOnlyList<CanonicalId>? RequiredFeatures = null, IReadOnlyList<CanonicalId>? ExcludedFeatures = null);

/// <summary>Parses the closed JSON intent for editing a rule's required and excluded features.</summary>
public static class EditPhonologicalRuleFeaturesIntentParser
{
    private const string ConstructName = "EditPhonologicalRuleFeatures";

    /// <summary>Reads the rule and at least one of the two sets.</summary>
    public static EditPhonologicalRuleFeaturesIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, ConstructName, "rule", "requiredFeatures", "excludedFeatures");
        var result = new EditPhonologicalRuleFeaturesIntent(SoundSystemIntentParsing.Id(authored, "rule"),
            authored.TryGetProperty("requiredFeatures", out _)
                ? SoundSystemIntentParsing.Ids(authored, "requiredFeatures", ordered: false) : null,
            authored.TryGetProperty("excludedFeatures", out _)
                ? SoundSystemIntentParsing.Ids(authored, "excludedFeatures", ordered: false) : null);
        if (result.RequiredFeatures is null && result.ExcludedFeatures is null)
            throw new ContractParseException($"'{ConstructName}': give 'requiredFeatures' or 'excludedFeatures'.");
        return result;
    }
}

/// <summary>
/// Lowers one <see cref="EditPhonologicalRuleFeaturesIntent"/> into reference changes on the rule's right-hand side. A
/// feature the rule list does not yet hold gets the wrapper item FieldWorks keeps for it in the Phonological Rule
/// Features list, named after the feature; the parser reads the wrapped exception feature or inflection class.
/// </summary>
public static class EditPhonologicalRuleFeaturesComposer
{
    private const string ConstructName = "EditPhonologicalRuleFeatures";
    private const string Rationale = "Authored by the EditPhonologicalRuleFeatures composer.";

    /// <summary>Builds the wrapper creations and the required and excluded set changes.</summary>
    /// <param name="cache">The project to resolve <paramref name="intent"/>'s references against.</param>
    /// <param name="intent">The authored sets.</param>
    /// <param name="mintId">Overrides id minting; tests supply a deterministic source.</param>
    public static IReadOnlyList<OperationEnvelope> Build(
        LcmCache cache, EditPhonologicalRuleFeaturesIntent intent, Func<CanonicalId>? mintId = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        if (intent.RequiredFeatures is null && intent.ExcludedFeatures is null)
            throw new InvalidOperationException($"'{ConstructName}': give 'requiredFeatures' or 'excludedFeatures'.");
        var ruleId = LexicalFormAuthoring.Normalize(intent.Rule);
        var rule = LexicalFormAuthoring.Resolve<IPhSegmentRule>(cache, ruleId, ConstructName);
        if (rule is not IPhRegularRule regular || regular.RightHandSidesOS.Count != 1)
            throw new InvalidOperationException(
                $"'{ConstructName}': '{ruleId.Value}' must have one right-hand side for this shorthand. " +
                "For multiple outcomes use compose-edit-phonological-rule with rightHandSides and expected.rightHandSides.");
        var rhs = regular.RightHandSidesOS[0];
        var rhsId = CanonicalId.FromGuid(rhs.Guid);

        var required = Items(cache, intent.RequiredFeatures, "requiredFeatures");
        var excluded = Items(cache, intent.ExcludedFeatures, "excludedFeatures");
        if (required is not null && excluded is not null && required.Select(x => x.Guid).Intersect(excluded.Select(x => x.Guid)).Any())
            throw new InvalidOperationException($"'{ConstructName}': a feature cannot be both required and excluded.");
        var effectiveRequired = (required?.Select(x => x.Guid) ?? rhs.ReqRuleFeatsRC.Select(w => w.ItemRA?.Guid ?? Guid.Empty)).ToHashSet();
        var effectiveExcluded = (excluded?.Select(x => x.Guid) ?? rhs.ExclRuleFeatsRC.Select(w => w.ItemRA?.Guid ?? Guid.Empty)).ToHashSet();
        if (effectiveRequired.Overlaps(effectiveExcluded))
            throw new InvalidOperationException($"'{ConstructName}': a feature cannot be both required and excluded.");

        var builder = new SoundSystemAuthoring.Builder(mintId, Rationale);
        new RuleOutcomeFeatures(cache, builder).Apply(rhsId, required, excluded, rhs);
        return builder.Operations;
    }

    internal static IReadOnlyList<ICmObject>? Items(LcmCache cache, IReadOnlyList<CanonicalId>? ids, string field)
    {
        if (ids is null) return null;
        var normalized = ids.Select(LexicalFormAuthoring.Normalize).ToArray();
        if (normalized.Select(id => id.ToGuid()).Distinct().Count() != normalized.Length)
            throw new InvalidOperationException($"'{ConstructName}': '{field}' must not repeat an identity.");
        var features = cache.LangProject.MorphologicalDataOA?.ProdRestrictOA?.ReallyReallyAllPossibilities.ToArray()
            ?? Array.Empty<ICmPossibility>();
        var items = new List<ICmObject>();
        foreach (var id in normalized.OrderBy(id => id.Value, StringComparer.Ordinal))
        {
            var item = LexicalFormAuthoring.Resolve<ICmObject>(cache, id, ConstructName);
            var isFeature = item is ICmPossibility possibility && features.Contains(possibility);
            if (!isFeature && item is not IMoInflClass)
                throw new InvalidOperationException(
                    $"'{ConstructName}': '{id.Value}' in '{field}' is neither an exception feature nor an inflection class.");
            items.Add(item);
        }
        return items;
    }
}
