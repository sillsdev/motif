using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;

namespace SIL.Motif.Runner.Composers;

/// <summary>Reads a regular rewrite rule back into the intent form that authored it, so it can be checked and edited.</summary>
public static class PhonologicalRuleReader
{
    /// <summary>
    /// The rule as an authoring intent, or null when it is not a regular rule with one right-hand side or holds a
    /// context the intent cannot express.
    /// </summary>
    public static AuthorPhonologicalRuleIntent? Read(IPhSegmentRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (rule is not IPhRegularRule { RightHandSidesOS.Count: 1 } regular) return null;
        if (rule.Direction is < 0 or > 2 || rule.StrucDescOS.Count > 8 || regular.RightHandSidesOS[0].StrucChangeOS.Count > 8)
            return null;
        var rhs = regular.RightHandSidesOS[0];
        var input = rule.StrucDescOS.Select(Item).ToArray();
        var output = rhs.StrucChangeOS.Select(Item).ToArray();
        if (input.Any(item => item is null) || output.Any(item => item is null)) return null;
        var left = Contexts(rhs.LeftContextOA);
        var right = Contexts(rhs.RightContextOA);
        if (left is null || right is null) return null;
        var direction = rule.Direction switch
        {
            1 => PhonologicalRuleDirection.RightToLeftIterative,
            2 => PhonologicalRuleDirection.Simultaneous,
            _ => PhonologicalRuleDirection.LeftToRightIterative,
        };
        return new AuthorPhonologicalRuleIntent(rule.Name.BestAnalysisAlternative.Text ?? string.Empty, direction,
            input!, output!, left, right);
    }

    /// <summary>The wire form of one rule item list, as the authoring tools accept it.</summary>
    public static JsonArray ItemsJson(IEnumerable<PhonologicalRuleItem> items) => new(items.Select(item =>
    {
        var node = new JsonObject();
        if (item.Phoneme is { } phoneme) node["phoneme"] = phoneme.Value;
        else if (item.Change is { } change) node["change"] = new JsonArray(change.Select(value => (JsonNode)new JsonObject
        {
            ["feature"] = value.Feature.Value, ["value"] = value.Value.Value,
        }).ToArray());
        else node["naturalClass"] = item.NaturalClass!.Value.Value;
        AddVariables(node, item.Variables);
        return (JsonNode)node;
    }).ToArray());

    private static void AddVariables(JsonObject node, IReadOnlyList<PhonologicalRuleVariable>? variables)
    {
        if (variables is not { Count: > 0 }) return;
        node["variables"] = new JsonArray(variables.Select(v => (JsonNode)new JsonObject
        {
            ["variable"] = v.Variable, ["feature"] = v.Feature.Value, ["polarity"] = v.Polarity,
        }).ToArray());
    }

    /// <summary>The wire form of one context list, as the authoring tools accept it.</summary>
    public static JsonArray ContextsJson(IEnumerable<EnvironmentContext> contexts) => new(contexts.Select(context =>
    {
        var node = new JsonObject();
        if (context.Phoneme is { } phoneme) node["phoneme"] = phoneme.Value;
        else if (context.NaturalClass is { } naturalClass) node["naturalClass"] = naturalClass.Value;
        else if (context.Boundary is { } boundary) node["boundary"] = boundary;
        else node["boundaryMarker"] = context.BoundaryMarker!.Value.Value;
        AddVariables(node, context.Variables);
        if (context.Minimum is { } minimum)
        {
            node["minimum"] = minimum;
            node["maximum"] = context.Maximum;
        }
        return (JsonNode)node;
    }).ToArray());

    /// <summary>The wire form of the whole rule, without placement.</summary>
    public static JsonObject ToJson(AuthorPhonologicalRuleIntent intent) => new()
    {
        ["name"] = intent.Name,
        ["direction"] = DirectionText(intent.Direction),
        ["input"] = ItemsJson(intent.Input),
        ["output"] = ItemsJson(intent.Output),
        ["left"] = ContextsJson(intent.Left),
        ["right"] = ContextsJson(intent.Right),
    };

    internal static string DirectionText(PhonologicalRuleDirection direction) => direction switch
    {
        PhonologicalRuleDirection.RightToLeftIterative => "right-to-left",
        PhonologicalRuleDirection.Simultaneous => "simultaneous",
        _ => "left-to-right",
    };

    private static PhonologicalRuleItem? Item(IPhPhonContext context)
    {
        if (context is IPhSimpleContextSeg { FeatureStructureRA: { } phoneme }) return new(Phoneme: Id(phoneme));
        if (context is not IPhSimpleContextNC { FeatureStructureRA: { } naturalClass } nc) return null;
        var variables = Variables(nc);
        if (variables is null) return null;
        return new(NaturalClass: Id(naturalClass), Variables: variables.Length == 0 ? null : variables);
    }

    internal static string VariableName(IPhFeatureConstraint constraint) => Id(constraint).Value;

    private static PhonologicalRuleVariable[]? Variables(IPhSimpleContextNC context)
    {
        var all = context.PlusConstrRS.Concat(context.MinusConstrRS).ToArray();
        if (all.Any(c => c.FeatureRA is not IFsClosedFeature || c.Owner is not IPhPhonData) ||
            all.Select(c => c.FeatureRA.Guid).Distinct().Count() != all.Length) return null;
        return context.PlusConstrRS.Select(c => new PhonologicalRuleVariable(VariableName(c), Id(c.FeatureRA), "+"))
            .Concat(context.MinusConstrRS.Select(c => new PhonologicalRuleVariable(VariableName(c), Id(c.FeatureRA), "-"))).ToArray();
    }

    private static EnvironmentContext[]? Contexts(IPhPhonContext? context)
    {
        if (context is null) return [];
        if (context is IPhSequenceContext sequence)
        {
            if (sequence.MembersRS.Count is < 1 or > 8) return null;
            var members = sequence.MembersRS.Select(Context).ToArray();
            return members.Any(member => member is null) ? null : members.Select(member => member!).ToArray();
        }
        return Context(context) is { } single ? [single] : null;
    }

    private static EnvironmentContext? Context(IPhPhonContext context) => context switch
    {
        IPhSimpleContextSeg { FeatureStructureRA: { } phoneme } => new(Phoneme: Id(phoneme)),
        IPhSimpleContextNC { FeatureStructureRA: { } naturalClass } nc => Variables(nc) is { } variables
            ? new(NaturalClass: Id(naturalClass), Variables: variables.Length == 0 ? null : variables) : null,
        IPhSimpleContextBdry { FeatureStructureRA: { } marker } => marker.Guid == LangProjectTags.kguidPhRuleWordBdry
            ? new(Boundary: "word")
            : marker.Guid == LangProjectTags.kguidPhRuleMorphBdry ? new(Boundary: "morpheme")
            : new(BoundaryMarker: Id(marker)),
        IPhIterationContext { MemberRA: IPhSimpleContext member } iteration
            when member is not IPhSimpleContextBdry && iteration.Minimum >= 0 &&
                 (iteration.Maximum == -1 || (iteration.Maximum >= 1 && iteration.Maximum >= iteration.Minimum))
            => Context(member) is { } inner
                ? iteration is { Minimum: 1, Maximum: 1 } ? inner
                    : inner with { Minimum = iteration.Minimum, Maximum = iteration.Maximum }
                : null,
        _ => null,
    };

    private static CanonicalId Id(ICmObject value) => CanonicalId.FromGuid(value.Guid);
}
