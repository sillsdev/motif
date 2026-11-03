using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;

namespace SIL.Motif.Runner.Composers;

/// <summary>Reorders the phonological rules, either by moving one rule or by naming the whole new order.</summary>
/// <param name="Rule">The rule to move; null when <paramref name="Order"/> names the whole order.</param>
/// <param name="After">The rule the moved rule must directly follow.</param>
/// <param name="Before">The rule the moved rule must directly precede.</param>
/// <param name="Order">Every rule of the project, in the order they must apply.</param>
public sealed record OrderPhonologicalRulesIntent(CanonicalId? Rule = null, CanonicalId? After = null,
    CanonicalId? Before = null, IReadOnlyList<CanonicalId>? Order = null);

/// <summary>Parses the closed JSON intent for ordering rules.</summary>
public static class OrderPhonologicalRulesIntentParser
{
    private const string ConstructName = "OrderPhonologicalRules";

    /// <summary>Reads either <c>order</c>, or <c>rule</c> with exactly one of <c>after</c> and <c>before</c>.</summary>
    public static OrderPhonologicalRulesIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, ConstructName, "rule", "after", "before", "order");
        var hasOrder = authored.TryGetProperty("order", out _);
        var hasRule = authored.TryGetProperty("rule", out _);
        var hasAfter = authored.TryGetProperty("after", out _);
        var hasBefore = authored.TryGetProperty("before", out _);
        if (hasOrder == hasRule)
            throw new ContractParseException($"'{ConstructName}': give 'order', or 'rule' with 'after' or 'before'.");
        if (hasOrder)
        {
            if (hasAfter || hasBefore)
                throw new ContractParseException($"'{ConstructName}': 'order' cannot be combined with 'after' or 'before'.");
            return new(Order: SoundSystemIntentParsing.Ids(authored, "order", ordered: true));
        }
        if (hasAfter == hasBefore)
            throw new ContractParseException($"'{ConstructName}': give exactly one of 'after' and 'before' with 'rule'.");
        return new(SoundSystemIntentParsing.Id(authored, "rule"),
            hasAfter ? SoundSystemIntentParsing.Id(authored, "after") : null,
            hasBefore ? SoundSystemIntentParsing.Id(authored, "before") : null);
    }
}

/// <summary>Lowers an <see cref="OrderPhonologicalRulesIntent"/> into the minimal moves that reach the new order.</summary>
public static class OrderPhonologicalRulesComposer
{
    private const string ConstructName = "OrderPhonologicalRules";

    /// <summary>Plans the moves; throws on an unknown rule, an incomplete order, or an order that changes nothing.</summary>
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, OrderPhonologicalRulesIntent intent,
        Func<CanonicalId>? mintId = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var data = cache.LangProject.PhonologicalDataOA;
        var current = data.PhonRulesOS.Select(rule => CanonicalId.FromGuid(rule.Guid)).ToList();
        List<CanonicalId> desired;
        if (intent.Order is { } order)
        {
            desired = order.Select(LexicalFormAuthoring.Normalize).ToList();
            var missing = current.Except(desired).ToArray();
            var unknown = desired.Except(current).ToArray();
            if (desired.Distinct().Count() != desired.Count || missing.Length > 0 || unknown.Length > 0)
                throw new InvalidOperationException(
                    $"'{ConstructName}': 'order' must list every rule of the project once. " +
                    (missing.Length > 0 ? $"Missing: {string.Join(", ", missing.Select(id => id.Value))}. " : string.Empty) +
                    (unknown.Length > 0 ? $"Not rules of this project: {string.Join(", ", unknown.Select(id => id.Value))}." : string.Empty));
        }
        else
        {
            var moved = Require(current, intent.Rule!.Value, "rule");
            var anchor = Require(current, (intent.After ?? intent.Before)!.Value, intent.After is null ? "before" : "after");
            if (moved == anchor)
                throw new InvalidOperationException($"'{ConstructName}': a rule cannot be placed beside itself.");
            desired = current.Where(id => id != moved).ToList();
            var at = desired.IndexOf(anchor);
            desired.Insert(intent.After is not null ? at + 1 : at, moved);
        }
        if (desired.SequenceEqual(current))
            throw new InvalidOperationException($"'{ConstructName}': the rules already apply in that order.");
        return PhonologicalRuleOrderModel.PlanMoves(SoundSystemAuthoring.Id(data), current, desired, mintId);
    }

    private static CanonicalId Require(List<CanonicalId> current, CanonicalId id, string field)
    {
        var normalized = LexicalFormAuthoring.Normalize(id);
        if (!current.Contains(normalized))
            throw new InvalidOperationException($"'{ConstructName}': '{field}' {id.Value} is not a phonological rule of this project.");
        return normalized;
    }
}
