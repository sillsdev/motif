using System;
using System.Collections.Generic;
using System.Linq;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>
/// Builds the operations for one metathesis rule, written the way FieldWorks writes it: the left environment, the
/// left switch, the optional middle, the right switch and the right environment are the rule's structural
/// description in that order, and its structural change records which position each part holds.
/// </summary>
public static class AuthorMetathesisRuleComposer
{
    /// <summary>Resolves the intent against the project and lowers it to operations.</summary>
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, AuthorMetathesisRuleIntent intent,
        Func<CanonicalId>? mintId = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        SoundSystemAuthoring.RequireName(intent.Name);
        var direction = AuthorPhonologicalRuleComposer.DirectionValue(intent.Direction);
        if (intent.Left is null || intent.Right is null)
            throw new InvalidOperationException("A metathesis rule's left and right environments must be supplied; an environment with no items is empty.");
        AuthorPhonologicalRuleComposer.RequireContextLength(intent.Left, "left");
        AuthorPhonologicalRuleComposer.RequireContextLength(intent.Right, "right");
        AuthorPhonologicalRuleComposer.ValidateBoundaryMarkers(cache, intent.Left, intent.Right);
        var data = cache.LangProject.PhonologicalDataOA;
        AuthorPhonologicalRuleComposer.ValidatePlacement(data, intent.Placement);

        var left = ResolveEnvironment(cache, intent.Left, isLeft: true);
        var right = ResolveEnvironment(cache, intent.Right, isLeft: false);
        var leftSwitch = Switch(cache, intent.LeftSwitch, "left switch");
        var middle = intent.Middle is null ? null : Switch(cache, intent.Middle, "middle");
        var rightSwitch = Switch(cache, intent.RightSwitch, "right switch");

        var builder = new SoundSystemAuthoring.Builder(mintId);
        var boundaryOperations = AuthorPhonologicalRuleComposer.EnsureBoundaries(cache, builder, left, right);
        var dataId = SoundSystemAuthoring.Id(data);
        var ruleId = builder.Create(PhPhonDataPhonRulesOperationKinds.Create, dataId,
            new { @class = "PhMetathesisRule" }, intent.Placement);
        builder.Text(PhSegmentRuleNameOperationKinds.SetName, ruleId, cache, cache.DefaultAnalWs, intent.Name);
        builder.Add(PhSegmentRuleDirectionOperationKinds.SetDirection, ruleId, new { value = direction });

        var contexts = left.Append(leftSwitch).Concat(middle is null ? [] : [middle]).Append(rightSwitch)
            .Concat(right).ToArray();
        AuthorPhonologicalRuleComposer.AddStructure(cache, builder, dataId, ruleId, default, contexts, null, null, null);
        var leftSwitchIndex = left.Count;
        var middleIndex = middle is null ? -1 : leftSwitchIndex + 1;
        var rightSwitchIndex = leftSwitchIndex + (middle is null ? 1 : 2);
        var structure = builder.Operations.ToArray();
        var payload = new Dictionary<string, object>
        {
            ["leftEnvironment"] = left.Count - 1,
            ["leftSwitch"] = leftSwitchIndex,
            ["middle"] = middleIndex,
            ["rightSwitch"] = rightSwitchIndex,
            ["rightEnvironment"] = right.Count == 0 ? -1 : rightSwitchIndex + 1,
        };
        if (middle is not null) payload["middleWith"] = intent.MiddleWithLeft ? "left" : "right";
        var change = builder.Add(PhMetathesisRuleStrucChangeOperationKinds.Set, ruleId, payload);
        AuthorPhonologicalRuleComposer.AddDependencies(builder.Operations, change, structure);

        var toEnable = builder.Operations.ToArray();
        var enable = builder.Add(PhSegmentRuleDisabledOperationKinds.SetDisabled, ruleId, new { value = false });
        AuthorPhonologicalRuleComposer.AddDependencies(builder.Operations, enable, toEnable);
        if (boundaryOperations.Count > 0)
        {
            var boundaryIds = boundaryOperations.Select(operation => operation.OperationId).ToHashSet();
            foreach (var operation in builder.Operations.ToArray())
                if (!boundaryIds.Contains(operation.OperationId))
                    AuthorPhonologicalRuleComposer.AddDependencies(builder.Operations, operation, boundaryOperations);
        }
        return builder.Operations;
    }

    private static IReadOnlyList<AuthorPhonologicalRuleComposer.ResolvedContext> ResolveEnvironment(LcmCache cache,
        IReadOnlyList<EnvironmentContext> contexts, bool isLeft)
    {
        if (contexts.Any(context => context.Minimum is not null || context.Variables is { Count: > 0 }))
            throw new InvalidOperationException("A metathesis environment item cannot be optional, repeated or carry alpha variables.");
        return AuthorPhonologicalRuleComposer.ResolveContexts(cache, contexts, isLeft, null);
    }

    private static AuthorPhonologicalRuleComposer.ResolvedContext Switch(LcmCache cache, PhonologicalRuleItem item,
        string role)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Change is not null || item.Variables is { Count: > 0 })
            throw new InvalidOperationException($"The {role} must be one phoneme or natural class, without a feature change or alpha variables.");
        return AuthorPhonologicalRuleComposer.ResolveRuleItem(cache, item, null);
    }
}
