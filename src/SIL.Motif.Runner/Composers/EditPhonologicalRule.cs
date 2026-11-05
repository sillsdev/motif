using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>The parts of a rule an edit may name; each part that is null is left as it is.</summary>
public sealed record PhonologicalRuleParts(
    string? Name = null,
    PhonologicalRuleDirection? Direction = null,
    IReadOnlyList<PhonologicalRuleItem>? Input = null,
    IReadOnlyList<PhonologicalRuleItem>? Output = null,
    IReadOnlyList<EnvironmentContext>? Left = null,
    IReadOnlyList<EnvironmentContext>? Right = null,
    IReadOnlyList<PhonologicalRuleOutcome>? RightHandSides = null);

/// <summary>Replaces parts of one existing regular rewrite rule in place, keeping its identity and position.</summary>
/// <param name="Rule">The rule to edit.</param>
/// <param name="Change">The parts to replace.</param>
/// <param name="Expected">Parts the rule must still hold, so a stale edit is refused; null checks nothing.</param>
public sealed record EditPhonologicalRuleIntent(CanonicalId Rule, PhonologicalRuleParts Change,
    PhonologicalRuleParts? Expected = null);

/// <summary>Parses the closed JSON intent for editing a rule in place.</summary>
public static class EditPhonologicalRuleIntentParser
{
    private const string ConstructName = "EditPhonologicalRule";
    private static readonly string[] PartNames = ["name", "direction", "input", "output", "left", "right", "rightHandSides"];

    /// <summary>Reads the rule, the replacement parts and the optional expectation.</summary>
    public static EditPhonologicalRuleIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, ConstructName, "rule", "name", "direction", "input", "output",
            "left", "right", "rightHandSides", "expected");
        var change = Parts(authored, ConstructName);
        if (change == new PhonologicalRuleParts())
            throw new ContractParseException($"'{ConstructName}': give at least one of {string.Join(", ", PartNames)}.");
        PhonologicalRuleParts? expected = null;
        if (authored.TryGetProperty("expected", out var guard))
        {
            SoundSystemIntentParsing.Object(guard, ConstructName + ".expected", PartNames);
            expected = Parts(guard, ConstructName + ".expected");
        }
        return new(SoundSystemIntentParsing.Id(authored, "rule"), change, expected);
    }

    private static PhonologicalRuleParts Parts(JsonElement element, string kind)
    {
        return new(
            element.TryGetProperty("name", out _) ? SoundSystemIntentParsing.Text(element, "name") : null,
            element.TryGetProperty("direction", out _) ? AuthorPhonologicalRuleIntentParser.ParseDirection(element) : null,
            element.TryGetProperty("input", out _) ? AuthorPhonologicalRuleIntentParser.ParseItems(element, "input") : null,
            element.TryGetProperty("output", out _) ? AuthorPhonologicalRuleIntentParser.ParseItems(element, "output") : null,
            element.TryGetProperty("left", out _) ? AuthorPhonologicalRuleIntentParser.ParseContexts(element, "left") : null,
            element.TryGetProperty("right", out _) ? AuthorPhonologicalRuleIntentParser.ParseContexts(element, "right") : null,
            element.TryGetProperty("rightHandSides", out _) ? AuthorPhonologicalRuleIntentParser.ParseOutcomes(element) : null);
    }
}

/// <summary>
/// Lowers an <see cref="EditPhonologicalRuleIntent"/>: a name or direction write, and for each replaced part the
/// deletion of old contexts followed by creation of new ones. Replacing the right-hand-side sequence replaces its
/// contexts and applicability sets under an expected-current guard; other edits preserve those sets.
/// </summary>
public static class EditPhonologicalRuleComposer
{
    private const string ConstructName = "EditPhonologicalRule";
    private const string Rationale = "Authored by the EditPhonologicalRule composer.";

    /// <summary>Builds the operations, or throws when the rule is not editable here or no longer matches the expectation.</summary>
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, EditPhonologicalRuleIntent intent,
        Func<CanonicalId>? mintId = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var ruleId = LexicalFormAuthoring.Normalize(intent.Rule);
        var rule = LexicalFormAuthoring.Resolve<IPhSegmentRule>(cache, ruleId, ConstructName);
        if (rule is not IPhRegularRule regular || regular.RightHandSidesOS.Count == 0 || rule.Owner is not IPhPhonData data)
            throw new InvalidOperationException(
                $"'{ConstructName}': '{ruleId.Value}' is not a regular rewrite rule with at least one right-hand side.");
        var rhs = regular.RightHandSidesOS[0];
        var current = PhonologicalRuleReader.Read(rule) ?? throw new InvalidOperationException(
            $"'{ConstructName}': '{ruleId.Value}' holds a context that Motif cannot read back, so it cannot be edited here.");
        if (intent.Expected is { } expected) CheckExpected(ruleId, current, expected);

        var change = intent.Change;
        if (change.RightHandSides is not null)
        {
            if (change.Output is not null || change.Left is not null || change.Right is not null)
                throw new InvalidOperationException("Use rightHandSides or the single-outcome output/left/right shorthand, not both.");
            if (intent.Expected?.RightHandSides is null && !(current.RightHandSides is null &&
                intent.Expected is { Output: not null, Left: not null, Right: not null }))
                throw new InvalidOperationException("Replacing rightHandSides requires expected.rightHandSides from a detailed rule read; a single-outcome read may instead guard output, left and right together.");
            return PhonologicalRuleOutcomeEditor.Build(cache, regular, current, change, mintId);
        }
        if (regular.RightHandSidesOS.Count > 1 && (change.Output is not null || change.Left is not null || change.Right is not null))
            throw new InvalidOperationException("A rule with multiple outcomes needs rightHandSides and expected.rightHandSides; output/left/right is ambiguous.");
        if (change.Name is not null) SoundSystemAuthoring.RequireName(change.Name);
        if (change.Input is not null) AuthorPhonologicalRuleComposer.RequireItemCount(change.Input, "input");
        if (change.Output is not null) AuthorPhonologicalRuleComposer.RequireItemCount(change.Output, "output");
        if (change.Left is not null) AuthorPhonologicalRuleComposer.RequireContextLength(change.Left, "left");
        if (change.Right is not null) AuthorPhonologicalRuleComposer.RequireContextLength(change.Right, "right");
        AuthorPhonologicalRuleComposer.ValidateBoundaryMarkers(cache, change.Left ?? [], change.Right ?? []);
        var input = change.Input?.Select(item => AuthorPhonologicalRuleComposer.ResolveRuleItem(cache, item, null)).ToArray();
        var output = change.Output?.Select(item => AuthorPhonologicalRuleComposer.ResolveRuleItem(cache, item, null)).ToArray();
        var left = change.Left is null ? null
            : AuthorPhonologicalRuleComposer.ResolveContexts(cache, change.Left, isLeft: true, null);
        var right = change.Right is null ? null
            : AuthorPhonologicalRuleComposer.ResolveContexts(cache, change.Right, isLeft: false, null);

        var finalOutcomes = current.Outcomes.Select((o, i) => i == 0 ? o with
        {
            Output = change.Output ?? o.Output, Left = change.Left ?? o.Left, Right = change.Right ?? o.Right,
        } : o).ToArray();
        FeatureRuleAuthoring.Validate(cache, new(current.Name, change.Direction ?? current.Direction,
            change.Input ?? current.Input, [], [], [], RightHandSides: finalOutcomes));
        var currentVariables = current.Input.Concat(current.Outcomes.SelectMany(o => o.Output)).SelectMany(i => i.Variables ?? [])
            .Concat(current.Outcomes.SelectMany(o => o.Left.Concat(o.Right)).SelectMany(c => c.Variables ?? []))
            .Select(v => v.Variable).ToHashSet(StringComparer.Ordinal);
        var variables = data.FeatConstraintsOS.Where(c => currentVariables.Contains(PhonologicalRuleReader.VariableName(c)))
            .ToDictionary(PhonologicalRuleReader.VariableName);
        var replacements = (input ?? []).Concat(output ?? []).Concat(left ?? []).Concat(right ?? []);
        foreach (var variable in replacements.SelectMany(c => c.Variables ?? []))
            if (variables.TryGetValue(variable.Variable, out var stored) && stored.FeatureRA?.Guid != variable.Feature.ToGuid())
                throw new InvalidOperationException($"Variable '{variable.Variable}' cannot change its feature in place.");
        if (input is not null)
        {
            var removedInput = rule.StrucDescOS.Select(c => c.Guid).ToHashSet();
            // Last-Input collection is pinned by `ReplacingAllInputReferencesPreservesAlphaSharing`.
            foreach (var variable in variables.Where(v => v.Value.ReferringObjects.All(o => removedInput.Contains(o.Guid))).ToArray())
                variables.Remove(variable.Key);
        }
        var builder = new SoundSystemAuthoring.Builder(mintId, Rationale);
        var boundaries = AuthorPhonologicalRuleComposer.EnsureBoundaries(cache, builder, left, right);
        var target = SoundSystemAuthoring.Id(rule);
        if (change.Name is not null)
            builder.Text(PhSegmentRuleNameOperationKinds.SetName, target, cache, cache.DefaultAnalWs, change.Name);
        if (change.Direction is { } direction)
            builder.Add(PhSegmentRuleDirectionOperationKinds.SetDirection, target,
                new { value = AuthorPhonologicalRuleComposer.DirectionValue(direction) });

        var deletions = new List<OperationEnvelope>();
        if (input is not null)
            foreach (var context in rule.StrucDescOS)
                deletions.Add(Delete(builder, context));
        var inputDeletions = deletions.ToArray();
        if (output is not null)
            foreach (var context in rhs.StrucChangeOS)
                deletions.Add(Delete(builder, context, inputDeletions));
        if (left is not null && rhs.LeftContextOA is { } oldLeft) deletions.Add(Delete(builder, oldLeft, inputDeletions));
        if (right is not null && rhs.RightContextOA is { } oldRight) deletions.Add(Delete(builder, oldRight, inputDeletions));
        var after = deletions.Concat(boundaries).ToArray();

        AuthorPhonologicalRuleComposer.AddStructure(cache, builder, SoundSystemAuthoring.Id(data), target,
            SoundSystemAuthoring.Id(rhs), input, output, left, right, after.Length == 0 ? null : after, variables);
        return builder.Operations;
    }

    private static OperationEnvelope Delete(SoundSystemAuthoring.Builder builder, IPhPhonContext context,
        IReadOnlyList<OperationEnvelope>? after = null) =>
        builder.Add(PhonologicalRuleDeleteOperationKinds.DeleteContexts, SoundSystemAuthoring.Id(context), new { }, requires: after);

    private static PhonologicalRuleItem NormalizeItem(PhonologicalRuleItem item) => item with
    {
        Phoneme = item.Phoneme is { } phoneme ? LexicalFormAuthoring.Normalize(phoneme) : null,
        NaturalClass = item.NaturalClass is { } naturalClass ? LexicalFormAuthoring.Normalize(naturalClass) : null,
        Change = item.Change?.Select(v => new PhonologicalFeatureValue(LexicalFormAuthoring.Normalize(v.Feature), LexicalFormAuthoring.Normalize(v.Value))).ToArray(),
        Variables = NormalizeVariables(item.Variables),
    };

    private static PhonologicalRuleVariable[]? NormalizeVariables(IReadOnlyList<PhonologicalRuleVariable>? variables) =>
        variables?.Select(v => v with { Feature = LexicalFormAuthoring.Normalize(v.Feature) }).ToArray();

    private static EnvironmentContext NormalizeContext(EnvironmentContext context) => context with
    {
        Phoneme = context.Phoneme is { } phoneme ? LexicalFormAuthoring.Normalize(phoneme) : null,
        NaturalClass = context.NaturalClass is { } naturalClass ? LexicalFormAuthoring.Normalize(naturalClass) : null,
        BoundaryMarker = context.BoundaryMarker is { } marker ? LexicalFormAuthoring.Normalize(marker) : null,
        Variables = NormalizeVariables(context.Variables),
    };

    private static void CheckExpected(CanonicalId rule, AuthorPhonologicalRuleIntent current, PhonologicalRuleParts expected)
    {
        var actual = PhonologicalRuleReader.ToJson(current);
        actual["rightHandSides"] = PhonologicalRuleReader.OutcomesJson(current.Outcomes);
        actual["output"] = PhonologicalRuleReader.ItemsJson(current.Outcomes[0].Output);
        actual["left"] = PhonologicalRuleReader.ContextsJson(current.Outcomes[0].Left);
        actual["right"] = PhonologicalRuleReader.ContextsJson(current.Outcomes[0].Right);
        if (current.Outcomes.Count > 1 && (expected.Output is not null || expected.Left is not null || expected.Right is not null))
            throw new InvalidOperationException("An expected output/left/right shorthand cannot guard a rule with multiple outcomes; read rightHandSides.");
        var stale = new List<string>();
        Compare("name", expected.Name is null ? null : JsonValue.Create(expected.Name));
        Compare("direction", expected.Direction is null ? null
            : JsonValue.Create(PhonologicalRuleReader.DirectionText(expected.Direction.Value)));
        Compare("input", expected.Input is null ? null : PhonologicalRuleReader.ItemsJson(expected.Input.Select(NormalizeItem)));
        Compare("output", expected.Output is null ? null : PhonologicalRuleReader.ItemsJson(expected.Output.Select(NormalizeItem)));
        Compare("left", expected.Left is null ? null : PhonologicalRuleReader.ContextsJson(expected.Left.Select(NormalizeContext)));
        Compare("right", expected.Right is null ? null : PhonologicalRuleReader.ContextsJson(expected.Right.Select(NormalizeContext)));
        Compare("rightHandSides", expected.RightHandSides is null ? null : PhonologicalRuleReader.OutcomesJson(
            expected.RightHandSides.Select(o => o with
            {
                Output = o.Output.Select(NormalizeItem).ToArray(), Left = o.Left.Select(NormalizeContext).ToArray(),
                Right = o.Right.Select(NormalizeContext).ToArray(),
                RequiredFeatures = o.RequiredFeatures?.Select(LexicalFormAuthoring.Normalize).ToArray(),
                ExcludedFeatures = o.ExcludedFeatures?.Select(LexicalFormAuthoring.Normalize).ToArray(),
                InputCategories = o.InputCategories?.Select(LexicalFormAuthoring.Normalize).ToArray(),
            })));
        if (stale.Count > 0)
            throw new InvalidOperationException(
                $"'{ConstructName}': '{rule.Value}' no longer matches the expectation for {string.Join(", ", stale)}. " +
                "Read it again with motif_grammar (kind phonological_rules, detail detailed) and repeat the edit.");

        void Compare(string part, JsonNode? wanted)
        {
            if (wanted is not null && wanted.ToJsonString() != actual[part]!.ToJsonString()) stale.Add(part);
        }
    }
}
