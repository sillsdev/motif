using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>Builds compare-and-set relinks for explicitly named natural-class users.</summary>
public static class RelinkNaturalClassComposer
{
    private const string ConstructName = "RelinkNaturalClass";

    /// <summary>Relinks selected environment strings and rewrite-rule contexts to a Draft-created class.</summary>
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, RelinkNaturalClassIntent intent)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(intent.Environments);
        ArgumentNullException.ThrowIfNull(intent.RuleContexts);
        if (intent.Source == intent.Replacement)
            throw new InvalidOperationException($"'{ConstructName}': source and replacement must be different classes.");

        var source = RequireNaturalClass(cache, intent.Source);
        var replacement = RequireNaturalClass(cache, intent.Replacement);
        ValidateClassForReference(cache, source);
        ValidateClassForReference(cache, replacement);
        if (intent.Environments.Count + intent.RuleContexts.Count == 0)
            throw new InvalidOperationException($"'{ConstructName}': declare at least one environment or rule context.");

        var dependencies = new[] { new OperationDependency(intent.ReplacementCreationOperation) };
        var operations = new List<OperationEnvelope>();
        var environmentIds = new HashSet<CanonicalId>();
        foreach (var environmentIntent in intent.Environments)
        {
            ArgumentNullException.ThrowIfNull(environmentIntent);
            if (!environmentIds.Add(environmentIntent.Target))
                throw new InvalidOperationException($"'{ConstructName}': environment '{environmentIntent.Target.Value}' is declared more than once.");
            var environment = ReferenceFieldLowering.Resolve<IPhEnvironment>(cache, environmentIntent.Target, ConstructName);
            if (!cache.LangProject.PhonologicalDataOA.EnvironmentsOS.Contains(environment))
                throw new InvalidOperationException($"'{ConstructName}': environment '{environmentIntent.Target.Value}' is not owned by this project.");
            var oldText = AuthorEnvironmentComposer.RenderPattern(cache,
                environmentIntent.ExpectedLeft, environmentIntent.ExpectedRight);
            if (!ContainsClass(environmentIntent.ExpectedLeft, intent.Source) &&
                !ContainsClass(environmentIntent.ExpectedRight, intent.Source))
                throw new InvalidOperationException($"'{ConstructName}': environment '{environmentIntent.Target.Value}' does not declare the source class.");
            if (!string.Equals(environment.StringRepresentation?.Text, oldText, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"'{ConstructName}': environment '{environmentIntent.Target.Value}' differs from its expected typed contexts.");

            var left = ReplaceClass(environmentIntent.ExpectedLeft, intent.Source, intent.Replacement);
            var right = ReplaceClass(environmentIntent.ExpectedRight, intent.Source, intent.Replacement);
            var text = AuthorEnvironmentComposer.RenderPattern(cache, left, right);
            operations.Add(new OperationEnvelope(CanonicalId.Mint(),
                PhEnvironmentStringRepresentationOperationKinds.Set, target: environmentIntent.Target,
                after: JsonSerializer.SerializeToElement(new
                {
                    ws = cache.WritingSystemFactory.GetStrFromWs(cache.DefaultVernWs),
                    text,
                    left = AuthorEnvironmentComposer.ContextPayload(left),
                    right = AuthorEnvironmentComposer.ContextPayload(right),
                }), dependsOn: dependencies, rationale: "Authored by the RelinkNaturalClass composer."));
        }

        var ruleContexts = RuleContextIndex(cache);
        var declaredContexts = new HashSet<CanonicalId>();
        foreach (var id in intent.RuleContexts)
        {
            if (!declaredContexts.Add(id))
                throw new InvalidOperationException($"'{ConstructName}': rule context '{id.Value}' is declared more than once.");
            if (!ruleContexts.TryGetValue(id, out var context) || context.FeatureStructureRA?.Guid != source.Guid)
                throw new InvalidOperationException($"'{ConstructName}': rule context '{id.Value}' is not a declared user of the source class.");
            operations.Add(new OperationEnvelope(CanonicalId.Mint(),
                PhSimpleContextNCFeatureStructureOperationKinds.SetFeatureStructure, target: id,
                after: JsonSerializer.SerializeToElement(new { @ref = intent.Replacement.Value }),
                dependsOn: dependencies, rationale: "Authored by the RelinkNaturalClass composer."));
        }
        return operations;
    }

    /// <summary>Checks that the replacement class was created earlier in the Draft and every generated reference depends on it.</summary>
    /// <param name="existing">Operations already present in the Draft.</param>
    /// <param name="intent">The authored source, replacement and creator identities.</param>
    /// <param name="additions">The relink operations produced for this intent.</param>
    public static void RequireEarlierCreation(IReadOnlyList<OperationEnvelope> existing,
        RelinkNaturalClassIntent intent, IReadOnlyList<OperationEnvelope> additions)
    {
        var creation = existing.SingleOrDefault(operation => operation.OperationId == intent.ReplacementCreationOperation);
        if (creation is null || creation.Kind != PhPhonDataNaturalClassesOperationKinds.Create ||
            creation.EntityId != intent.Replacement)
            throw new InvalidOperationException(
                $"'{ConstructName}': replacement class must be created by an earlier natural-class operation in this Draft.");
        if (additions.Count == 0 || additions.Any(operation =>
                !operation.DependsOn.Any(dependency => dependency.OperationId == creation.OperationId)))
            throw new InvalidOperationException($"'{ConstructName}': each reference must depend on the replacement class creation.");
    }

    private static IPhNaturalClass RequireNaturalClass(LcmCache cache, CanonicalId id)
    {
        var naturalClass = ReferenceFieldLowering.Resolve<IPhNaturalClass>(cache, id, ConstructName);
        if (!cache.LangProject.PhonologicalDataOA.NaturalClassesOS.Contains(naturalClass))
            throw new InvalidOperationException($"'{ConstructName}': natural class '{id.Value}' is not owned by this project.");
        return naturalClass;
    }

    private static void ValidateClassForReference(LcmCache cache, IPhNaturalClass naturalClass)
    {
        if (naturalClass is IPhNCSegments segments)
        {
            if (segments.SegmentsRC.Count == 0)
                throw new InvalidOperationException($"'{ConstructName}': an empty segment class cannot be referenced.");
            foreach (var member in segments.SegmentsRC)
                _ = SoundSystemAuthoring.RequirePhoneme(cache, SoundSystemAuthoring.Id(member));
            return;
        }
        if (naturalClass is IPhNCFeatures features && features.FeaturesOA?.FeatureSpecsOC.Count > 0) return;
        throw new InvalidOperationException($"'{ConstructName}': the natural class must have a nonempty supported description.");
    }

    private static Dictionary<CanonicalId, IPhSimpleContextNC> RuleContextIndex(LcmCache cache) =>
        cache.LangProject.PhonologicalDataOA.PhonRulesOS.OfType<IPhSegmentRule>()
            .SelectMany(EditNaturalClassComposer.RuleContexts).OfType<IPhSimpleContextNC>()
            .GroupBy(context => CanonicalId.FromGuid(context.Guid))
            .ToDictionary(group => group.Key, group => group.First());

    private static bool ContainsClass(IEnumerable<EnvironmentContext> contexts, CanonicalId id) =>
        contexts.Any(context => context.NaturalClass == id);

    private static EnvironmentContext[] ReplaceClass(IReadOnlyList<EnvironmentContext> contexts,
        CanonicalId source, CanonicalId replacement) => contexts.Select(context =>
        context.NaturalClass == source ? context with { NaturalClass = replacement } : context).ToArray();
}
