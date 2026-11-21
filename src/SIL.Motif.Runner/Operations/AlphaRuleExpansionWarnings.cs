using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Model.Effects;

namespace SIL.Motif.Runner.Operations;

/// <summary>
/// Adds a Dry Run warning for a touched phonological rule whose alpha variables range over many feature values. The
/// parser binds each variable to every value of its feature, so a rule's cost grows with the product of those counts.
/// This reads the rule's variables and their features, not a parse; the Trial reports the actual cost.
/// </summary>
internal static class AlphaRuleExpansionWarnings
{
    /// <summary>The number of value combinations above which a rule's alpha variables are worth a warning.</summary>
    internal const int CombinationLimit = 8;

    /// <summary>Each distinct alpha variable of <paramref name="rule"/> with the number of values its feature takes.</summary>
    internal static IReadOnlyList<(IFsClosedFeature Feature, int Values)> Variables(IPhSegmentRule rule)
    {
        var constraints = new Dictionary<Guid, IFsClosedFeature>();
        foreach (var context in Contexts(rule).OfType<IPhSimpleContextNC>())
            foreach (var constraint in context.PlusConstrRS.Concat(context.MinusConstrRS))
                if (constraint.FeatureRA is IFsClosedFeature feature) constraints[constraint.Guid] = feature;
        return constraints.Values.Select(feature => (feature, feature.ValuesOC.Count)).ToArray();
    }

    internal static void Append(LcmCache cache, IReadOnlyList<OperationEnvelope> operations, List<ExpectedEffect> effects)
    {
        var repository = cache.ServiceLocator.GetInstance<ICmObjectRepository>();
        var rules = new Dictionary<Guid, IPhSegmentRule>();
        var constraints = new HashSet<Guid>();
        foreach (var operation in operations)
            foreach (var id in new[] { operation.Target, operation.EntityId })
            {
                if (id is not { } value || !repository.TryGetObject(value.ToGuid(), out var found)) continue;
                if (found is IPhFeatureConstraint constraint) constraints.Add(constraint.Guid);
                for (var owner = found; owner is not null; owner = owner.Owner)
                    if (owner is IPhSegmentRule rule) { rules[rule.Guid] = rule; break; }
            }
        if (constraints.Count > 0)
            foreach (var rule in cache.LangProject.PhonologicalDataOA.PhonRulesOS.OfType<IPhSegmentRule>())
                if (Contexts(rule).OfType<IPhSimpleContextNC>().Any(context =>
                        context.PlusConstrRS.Concat(context.MinusConstrRS).Any(item => constraints.Contains(item.Guid))))
                    rules[rule.Guid] = rule;

        foreach (var rule in rules.Values)
        {
            var variables = Variables(rule).Select(item => (Name(item.Feature), item.Values)).ToArray();
            if (Warning(RuleName(rule), variables) is not { } warning) continue;
            effects.Add(new(CanonicalId.FromGuid(rule.Guid), "dryRunWarning", new Dictionary<string, string>(),
                new Dictionary<string, string> { ["warning"] = warning },
                System.Text.Json.JsonSerializer.SerializeToElement(new { warning })));
        }
    }

    /// <summary>The warning for a rule whose variables multiply past the limit, or null when it is cheap enough.</summary>
    internal static string? Warning(string ruleName, IReadOnlyList<(string Feature, int Values)> variables)
    {
        var combinations = variables.Aggregate(1L, (total, item) => Math.Min(total * Math.Max(item.Values, 1), int.MaxValue));
        if (combinations <= CombinationLimit) return null;
        var features = string.Join(", ", variables.Select(item => $"{item.Feature} ({item.Values} values)"));
        return $"Rule '{ruleName}' has {variables.Count} {DryRunWarningMarkers.AlphaRuleExpansion} {features}: the parser tries each " +
            $"variable with every value of its feature, up to {combinations} combinations for this rule on every word that reaches it, " +
            "so it can be slow enough that a Trial stops words. A rule over one natural class per value group avoids the expansion; " +
            "if the Trial stops words, remove this rule first.";
    }

    /// <summary>The alpha variables an authored rule names, each with the values its feature takes in <paramref name="cache"/>.</summary>
    internal static IReadOnlyList<(string Feature, int Values)> Variables(LcmCache cache, IEnumerable<PhonologicalRuleVariable> uses)
    {
        var repository = cache.ServiceLocator.GetInstance<ICmObjectRepository>();
        var result = new List<(string, int)>();
        foreach (var group in uses.GroupBy(use => use.Variable, StringComparer.Ordinal))
            if (repository.TryGetObject(group.First().Feature.ToGuid(), out var found) && found is IFsClosedFeature feature)
                result.Add((Name(feature), feature.ValuesOC.Count));
        return result;
    }

    internal static string RuleName(IPhSegmentRule rule) =>
        rule.Name.BestAnalysisAlternative.Text is { Length: > 0 } text && text != "***"
            ? text : CanonicalId.FromGuid(rule.Guid).Value;

    private static string Name(IFsClosedFeature feature) =>
        feature.Name.BestAnalysisAlternative.Text is { Length: > 0 } text && text != "***"
            ? text : CanonicalId.FromGuid(feature.Guid).Value;

    private static IEnumerable<IPhPhonContext> Contexts(IPhSegmentRule rule)
    {
        var pending = new Stack<IPhPhonContext>();
        void Push(IPhPhonContext? context) { if (context is not null) pending.Push(context); }
        foreach (var context in rule.StrucDescOS) Push(context);
        if (rule is IPhRegularRule regular)
            foreach (var rhs in regular.RightHandSidesOS)
            {
                foreach (var context in rhs.StrucChangeOS) Push(context);
                Push(rhs.LeftContextOA);
                Push(rhs.RightContextOA);
            }
        while (pending.Count > 0)
        {
            var context = pending.Pop();
            yield return context;
            if (context is IPhSequenceContext sequence)
                foreach (var member in sequence.MembersRS) Push(member);
            else if (context is IPhIterationContext { MemberRA: { } iterated }) Push(iterated);
        }
    }
}
