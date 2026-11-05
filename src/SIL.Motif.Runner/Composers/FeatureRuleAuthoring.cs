using System;
using System.Collections.Generic;
using System.Linq;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

internal static class FeatureRuleAuthoring
{
    internal static void Validate(LcmCache cache, AuthorPhonologicalRuleIntent intent)
    {
        var outputs = intent.Outcomes.SelectMany(o => o.Output).ToArray();
        var contexts = intent.Outcomes.SelectMany(o => o.Left.Concat(o.Right)).ToArray();
        var uses = intent.Input.Concat(outputs).SelectMany(i => i.Variables ?? [])
            .Concat(contexts.SelectMany(i => i.Variables ?? [])).ToArray();
        if (uses.Select(v => v.Variable).Distinct(StringComparer.Ordinal).Count() > 24)
            throw new InvalidOperationException("FieldWorks HCLoader can name at most 24 alpha variables across one rule's Input and right-hand sides.");
        var visible = intent.Input.Concat(outputs).SelectMany(i => i.Variables ?? [])
            .Concat(intent.Outcomes.SelectMany(o => Visible(o.Left).Concat(Visible(o.Right))))
            .Select(v => v.Variable).ToHashSet(StringComparer.Ordinal);
        foreach (var group in uses.GroupBy(v => v.Variable, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(group.Key) || group.Any(v => v.Polarity is not ("+" or "-")))
                throw new InvalidOperationException($"Variable '{group.Key}' needs a name and polarity '+' or '-'.");
            if (group.Select(v => v.Feature.ToGuid()).Distinct().Count() != 1)
                throw new InvalidOperationException($"Variable '{group.Key}' refers to contradictory features.");
            if (!visible.Contains(group.Key))
                throw new InvalidOperationException($"Variable '{group.Key}' occurs only inside repeated items in context sequences; FieldWorks HCLoader cannot discover it through rule.FeatureConstraints. Give it a reference in Input, Output, a non-repeated sequence item or a single-item context.");
            RequireFeature(cache, group.First().Feature);
        }
        foreach (var item in intent.Input)
            if (item.Change is not null) throw new InvalidOperationException("A feature change is allowed only in rule Output.");
        foreach (var outcome in intent.Outcomes)
        foreach (var item in outcome.Output)
        {
            if (item.Change is not { } change) continue;
            if (intent.Input.Count != outcome.Output.Count)
                throw new InvalidOperationException("A feature change needs an aligned matched segment: Input and Output must have equal lengths.");
            var duplicate = change.GroupBy(v => v.Feature.ToGuid()).FirstOrDefault(group => group.Count() > 1);
            if (duplicate is not null)
                throw new InvalidOperationException($"Feature '{duplicate.First().Feature}' has contradictory values in one change.");
            SoundSystemAuthoring.ValidateFeatures(cache, change);
            if (change.Count == 0 && item.Variables is not { Count: > 0 })
                throw new InvalidOperationException("A feature change needs a feature value or an alpha variable.");
            foreach (var value in change) RequireFeature(cache, value.Feature);
        }
        foreach (var item in intent.Input.Concat(outputs)) ValidateItem(item.NaturalClass, item.Change, item.Variables);
        foreach (var context in contexts) ValidateItem(context.NaturalClass, null, context.Variables);

        static IEnumerable<PhonologicalRuleVariable> Visible(IReadOnlyList<EnvironmentContext> contexts) =>
            contexts.Where(c => contexts.Count == 1 || c.Minimum is null).SelectMany(c => c.Variables ?? []);

        void ValidateItem(CanonicalId? naturalClass, IReadOnlyList<PhonologicalFeatureValue>? change,
            IReadOnlyList<PhonologicalRuleVariable>? variables)
        {
            if (variables is not { Count: > 0 }) return;
            if (change is null && (naturalClass is null ||
                ReferenceFieldLowering.Resolve<IPhNaturalClass>(cache, naturalClass.Value, "alpha reference") is not IPhNCFeatures))
                throw new InvalidOperationException($"Variable '{variables[0].Variable}' needs a feature natural class or Output change.");
            foreach (var group in variables.GroupBy(v => v.Feature.ToGuid()))
                if (group.Count() > 1)
                    throw new InvalidOperationException($"Variable '{group.First().Variable}' has duplicate or contradictory polarity for feature '{group.First().Feature}'.");
            var fixedFeatures = change?.Select(v => v.Feature.ToGuid()).ToHashSet() ?? [];
            if (naturalClass is { } id && ReferenceFieldLowering.Resolve<IPhNaturalClass>(cache, id, "alpha reference") is IPhNCFeatures nc)
                fixedFeatures.UnionWith(nc.FeaturesOA?.FeatureSpecsOC.Where(s => s.FeatureRA is not null).Select(s => s.FeatureRA.Guid) ?? []);
            foreach (var variable in variables)
                if (fixedFeatures.Contains(variable.Feature.ToGuid()))
                    throw new InvalidOperationException($"Variable '{variable.Variable}' contradicts a fixed value of feature '{variable.Feature}'.");
        }
    }

    private static void RequireFeature(LcmCache cache, CanonicalId featureId)
    {
        var feature = ReferenceFieldLowering.Resolve<IFsClosedFeature>(cache, featureId, "phonological rule feature");
        var name = feature.Name.BestAnalysisAlternative.Text ?? featureId.Value;
        if (!cache.LangProject.PhFeatureSystemOA.FeaturesOC.Contains(feature) || feature.ValuesOC.Count == 0)
            throw new InvalidOperationException($"Feature '{name}' ({featureId}) is not a valued phonological feature in this project.");
    }

    internal sealed class Lowering(LcmCache cache, SoundSystemAuthoring.Builder builder, CanonicalId dataId,
        IReadOnlyDictionary<string, IPhFeatureConstraint>? existing)
    {
        private readonly Dictionary<string, (CanonicalId Id, OperationEnvelope[] Operations)> _variables = new(StringComparer.Ordinal);
        private CanonicalId? _previousClass;

        internal AuthorPhonologicalRuleComposer.ResolvedContext[]? Prepare(
            IReadOnlyList<AuthorPhonologicalRuleComposer.ResolvedContext>? items) => items?.Select(Prepare).ToArray();

        private AuthorPhonologicalRuleComposer.ResolvedContext Prepare(AuthorPhonologicalRuleComposer.ResolvedContext item)
        {
            var required = new List<OperationEnvelope>();
            if (item.Change is { } change)
            {
                var start = builder.Operations.Count;
                var placement = _previousClass is { } previous ? new Placement(previous, null)
                    : SoundSystemAuthoring.Append(cache.LangProject.PhonologicalDataOA.NaturalClassesOS);
                var id = builder.Create(PhPhonDataNaturalClassesOperationKinds.Create, dataId,
                    new { @class = "PhNCFeatures" }, placement);
                var label = "change-" + id.ToGuid().ToString("N");
                builder.Text(PhNaturalClassNameOperationKinds.SetName, id, cache, cache.DefaultAnalWs, label);
                builder.Text(PhNaturalClassAbbreviationOperationKinds.SetAbbreviation, id, cache, cache.DefaultAnalWs, label);
                builder.Features(cache, id, PhNCFeaturesFeaturesOperationKinds.Create, change);
                _previousClass = id;
                required.AddRange(builder.Operations.Skip(start));
                item = item with { Reference = id };
            }
            var constraints = new List<(CanonicalId Id, bool Positive)>();
            foreach (var variable in item.Variables ?? [])
            {
                if (!_variables.TryGetValue(variable.Variable, out var resolved))
                {
                    if (existing is not null && existing.TryGetValue(variable.Variable, out var stored))
                    {
                        if (stored.FeatureRA?.Guid != variable.Feature.ToGuid())
                            throw new InvalidOperationException($"Variable '{variable.Variable}' cannot change its feature in place.");
                        resolved = (SoundSystemAuthoring.Id(stored), []);
                    }
                    else
                    {
                        var start = builder.Operations.Count;
                        var id = builder.Create(PhPhonDataFeatConstraintsOperationKinds.Create, dataId);
                        builder.Add(PhFeatureConstraintFeatureOperationKinds.SetFeature, id, new { @ref = variable.Feature.Value });
                        resolved = (id, builder.Operations.Skip(start).ToArray());
                    }
                    _variables.Add(variable.Variable, resolved);
                }
                required.AddRange(resolved.Operations);
                constraints.Add((resolved.Id, variable.Polarity == "+"));
            }
            return item with { Constraints = constraints, Requires = required };
        }
    }
}
