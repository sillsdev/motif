using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

internal static class PhonologicalRuleOutcomeEditor
{
    internal static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, IPhRegularRule rule,
        AuthorPhonologicalRuleIntent current, PhonologicalRuleParts change, Func<CanonicalId>? mintId)
    {
        var final = new AuthorPhonologicalRuleIntent(change.Name ?? current.Name, change.Direction ?? current.Direction,
            change.Input ?? current.Input, [], [], [], RightHandSides: change.RightHandSides);
        SoundSystemAuthoring.RequireName(final.Name);
        AuthorPhonologicalRuleComposer.RequireItemCount(final.Input, "input");
        AuthorPhonologicalRuleComposer.RequireOutcomes(cache, final.Outcomes);
        FeatureRuleAuthoring.Validate(cache, final);
        _ = AuthorPhonologicalRuleComposer.DirectionValue(final.Direction);
        var outcomes = final.Outcomes.Select(o => AuthorPhonologicalRuleComposer.ResolveOutcome(cache, o)).ToArray();
        var input = change.Input?.Select(i => AuthorPhonologicalRuleComposer.ResolveRuleItem(cache, i, null)).ToArray();
        var data = cache.LangProject.PhonologicalDataOA;
        var oldConstraints = rule.RightHandSidesOS.SelectMany(PhonologicalRuleOutcomeGraph.Members)
            .OfType<IPhFeatureConstraint>().DistinctBy(c => c.Guid).Select(SoundSystemAuthoring.Id).ToArray();
        var variables = data.FeatConstraintsOS.Where(c => current.Input.Concat(current.Outcomes.SelectMany(o => o.Output))
                .SelectMany(i => i.Variables ?? []).Concat(current.Outcomes.SelectMany(o => o.Left.Concat(o.Right))
                    .SelectMany(c => c.Variables ?? [])).Any(v => v.Variable == PhonologicalRuleReader.VariableName(c)))
            .ToDictionary(PhonologicalRuleReader.VariableName);
        foreach (var v in final.Input.Concat(final.Outcomes.SelectMany(o => o.Output)).SelectMany(i => i.Variables ?? [])
                     .Concat(final.Outcomes.SelectMany(o => o.Left.Concat(o.Right)).SelectMany(c => c.Variables ?? [])))
            if (variables.TryGetValue(v.Variable, out var stored) && stored.FeatureRA.Guid != v.Feature.ToGuid())
                throw new InvalidOperationException($"Variable '{v.Variable}' cannot change its feature in place.");
        if (input is not null)
        {
            var removedInput = rule.StrucDescOS.Select(c => c.Guid).ToHashSet();
            // Last-Input collection is pinned by `ReplacingAllInputReferencesPreservesAlphaSharing`.
            foreach (var pair in variables.Where(p => p.Value.ReferringObjects.All(o => removedInput.Contains(o.Guid))).ToArray())
                variables.Remove(pair.Key);
        }

        var builder = new SoundSystemAuthoring.Builder(mintId, "Authored by the EditPhonologicalRule composer.");
        var boundaries = AuthorPhonologicalRuleComposer.EnsureBoundaries(cache, builder,
            outcomes.SelectMany(o => o.Left).ToArray(), outcomes.SelectMany(o => o.Right).ToArray());
        var ruleId = SoundSystemAuthoring.Id(rule);
        if (change.Name is not null)
            builder.Text(PhSegmentRuleNameOperationKinds.SetName, ruleId, cache, cache.DefaultAnalWs, change.Name);
        if (change.Direction is { } direction)
            builder.Add(PhSegmentRuleDirectionOperationKinds.SetDirection, ruleId,
                new { value = AuthorPhonologicalRuleComposer.DirectionValue(direction) });
        var deletions = new List<OperationEnvelope>();
        if (input is not null)
            foreach (var context in rule.StrucDescOS)
                deletions.Add(builder.Add(PhonologicalRuleDeleteOperationKinds.DeleteContexts, SoundSystemAuthoring.Id(context), new { }));
        var inputDeletions = deletions.ToArray();
        foreach (var rhs in rule.RightHandSidesOS)
            deletions.Add(builder.Add(PhonologicalRuleDeleteOperationKinds.DeleteRightHandSides, SoundSystemAuthoring.Id(rhs),
                new { graphDigest = PhonologicalRuleOutcomeGraph.Digest(cache, rhs) }, requires: inputDeletions));
        var after = deletions.Concat(boundaries).ToArray();
        var dataId = SoundSystemAuthoring.Id(data);
        var lowering = new FeatureRuleAuthoring.Lowering(cache, builder, dataId, variables);
        var features = new RuleOutcomeFeatures(cache, builder);
        CanonicalId? previous = null;
        foreach (var outcome in outcomes)
        {
            var rhsId = builder.Create(PhRegularRuleRightHandSidesOperationKinds.Create, ruleId,
                placement: previous is { } prior ? new Placement(prior, null) : null, requires: after);
            AuthorPhonologicalRuleComposer.AddStructure(cache, builder, dataId, ruleId, rhsId, previous is null ? input : null,
                outcome.Output, outcome.Left, outcome.Right, after, lowering: lowering);
            features.Apply(rhsId, outcome.Required, outcome.Excluded);
            AuthorPhonologicalRuleComposer.AddCategories(builder, rhsId, outcome.Categories);
            previous = rhsId;
        }
        if (oldConstraints.Length > 0)
            builder.Add(PhonologicalRuleConstraintCollectionHandler.Kind, ruleId,
                new { constraints = oldConstraints.Select(id => id.Value).ToArray() }, requires: builder.Operations.ToArray());
        return builder.Operations;
    }
}
