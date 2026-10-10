using System;
using System.Collections.Generic;
using System.Linq;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>Builds the operations for one complete regular rewrite rule.</summary>
public static class AuthorPhonologicalRuleComposer
{
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, AuthorPhonologicalRuleIntent intent,
        Func<CanonicalId>? mintId = null, CanonicalId? plannedNaturalClass = null,
        CanonicalId? authoredRuleId = null, IReadOnlyList<OperationEnvelope>? prerequisites = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        SoundSystemAuthoring.RequireName(intent.Name);
        if (intent.Input is null || intent.Output is null || intent.Left is null || intent.Right is null)
            throw new InvalidOperationException("Rule input, output, left context and right context must be supplied.");
        if (intent.Input.Count > 1 || intent.Output.Count > 1)
            throw new InvalidOperationException("A simple rewrite rule supports at most one input and one output item.");
        RequireContextLength(intent.Left, "left");
        RequireContextLength(intent.Right, "right");

        var data = cache.LangProject.PhonologicalDataOA;
        ValidatePlacement(data, intent.Placement);
        var input = intent.Input.Select(item => ResolveRuleItem(cache, item, plannedNaturalClass)).ToArray();
        var output = intent.Output.Select(item => ResolveRuleItem(cache, item, plannedNaturalClass)).ToArray();
        var left = ResolveContexts(cache, intent.Left, isLeft: true, plannedNaturalClass);
        var right = ResolveContexts(cache, intent.Right, isLeft: false, plannedNaturalClass);

        var builder = new SoundSystemAuthoring.Builder(mintId);
        var boundaryOperations = new List<OperationEnvelope>();
        var referencedBoundaries = left.Concat(right).Select(context => context.Reference.ToGuid()).ToHashSet();
        if (referencedBoundaries.Contains(LangProjectTags.kguidPhRuleWordBdry) ||
            referencedBoundaries.Contains(LangProjectTags.kguidPhRuleMorphBdry))
        {
            var firstSet = SoundSystemAuthoring.FirstSet(cache);
            var firstSetId = SoundSystemAuthoring.Id(firstSet);
            if (referencedBoundaries.Contains(LangProjectTags.kguidPhRuleWordBdry))
                boundaryOperations.AddRange(SoundSystemAuthoring.EnsureOwnedBoundaryMarker(cache, builder, firstSet,
                    firstSetId, LangProjectTags.kguidPhRuleWordBdry, "word", "#"));
            if (referencedBoundaries.Contains(LangProjectTags.kguidPhRuleMorphBdry))
                boundaryOperations.AddRange(SoundSystemAuthoring.EnsureOwnedBoundaryMarker(cache, builder, firstSet,
                    firstSetId, LangProjectTags.kguidPhRuleMorphBdry, "morpheme", "+"));
        }

        var dataId = SoundSystemAuthoring.Id(data);
        var ruleId = builder.Create(PhPhonDataPhonRulesOperationKinds.Create, dataId,
            new { @class = "PhRegularRule" }, intent.Placement, authoredRuleId);
        var create = builder.Operations[^1];
        if (prerequisites is { Count: > 0 }) AddDependencies(builder.Operations, create, prerequisites);
        builder.Text(PhSegmentRuleNameOperationKinds.SetName, ruleId, cache, cache.DefaultAnalWs, intent.Name);
        builder.Add(PhSegmentRuleDirectionOperationKinds.SetDirection, ruleId,
            new { value = DirectionValue(intent.Direction) });

        var rhsId = builder.Create(PhRegularRuleRightHandSidesOperationKinds.Create, ruleId);
        if (input.Length == 1)
            CreateOwnedSimpleContext(builder, ruleId, input[0], PhSegmentRuleStrucDescOperationKinds.Create);
        if (output.Length == 1)
            CreateOwnedSimpleContext(builder, rhsId, output[0], PhSegRuleRHSStrucChangeOperationKinds.Create);
        CreateRuleContext(builder, dataId, rhsId, left, PhSegRuleRHSLeftContextOperationKinds.Create);
        CreateRuleContext(builder, dataId, rhsId, right, PhSegRuleRHSRightContextOperationKinds.Create);

        var toEnable = builder.Operations.ToArray();
        var enable = builder.Add(PhSegmentRuleDisabledOperationKinds.SetDisabled, ruleId, new { value = false });
        AddDependencies(builder.Operations, enable, toEnable);
        var externalPrerequisites = prerequisites?.ToArray() ?? Array.Empty<OperationEnvelope>();
        var allPrerequisites = externalPrerequisites.Concat(boundaryOperations).ToArray();
        if (allPrerequisites.Length > 0)
        {
            var boundaryOperationIds = boundaryOperations.Select(operation => operation.OperationId).ToHashSet();
            foreach (var operation in builder.Operations.ToArray())
            {
                var required = boundaryOperationIds.Contains(operation.OperationId)
                    ? externalPrerequisites
                    : allPrerequisites;
                if (required.Length > 0) AddDependencies(builder.Operations, operation, required);
            }
        }
        return builder.Operations;
    }

    private static void CreateOwnedSimpleContext(SoundSystemAuthoring.Builder builder, CanonicalId owner,
        ResolvedContext item, string createKind)
    {
        var contextId = builder.Create(createKind, owner, new { @class = item.ClassName });
        SetContextReference(builder, contextId, item.Reference, item.ClassName);
    }

    private static void CreateRuleContext(SoundSystemAuthoring.Builder builder, CanonicalId dataId,
        CanonicalId rhsId, IReadOnlyList<ResolvedContext> contexts, string createKind)
    {
        if (contexts.Count == 1)
        {
            CreateOwnedSimpleContext(builder, rhsId, contexts[0], createKind);
            return;
        }

        var sequenceId = builder.Create(createKind, rhsId, new { @class = "PhSequenceContext" });
        CanonicalId? previousMember = null;
        OperationEnvelope? previousAdd = null;
        foreach (var item in contexts)
        {
            var memberId = builder.Create(PhPhonDataContextsOperationKinds.Create, dataId,
                new { @class = item.ClassName });
            var memberCreate = builder.Operations[^1];
            SetContextReference(builder, memberId, item.Reference, item.ClassName);
            var placement = previousMember is { } previous ? new Placement(previous, null) : null;
            var add = builder.Add(PhSequenceContextMembersOperationKinds.AddRefMembers, sequenceId,
                new { member = memberId.Value }, placement: placement);
            var prerequisites = previousAdd is { } prior ? new[] { memberCreate, prior } : [memberCreate];
            AddDependencies(builder.Operations, add, prerequisites);
            previousMember = memberId;
            previousAdd = add;
        }
    }

    private static void SetContextReference(SoundSystemAuthoring.Builder builder, CanonicalId context,
        CanonicalId reference, string contextClass)
    {
        var kind = contextClass switch
        {
            "PhSimpleContextSeg" => PhSimpleContextSegFeatureStructureOperationKinds.SetFeatureStructure,
            "PhSimpleContextNC" => PhSimpleContextNCFeatureStructureOperationKinds.SetFeatureStructure,
            "PhSimpleContextBdry" => PhSimpleContextBdryFeatureStructureOperationKinds.SetFeatureStructure,
            _ => throw new InvalidOperationException($"Context class '{contextClass}' is not a supported simple context."),
        };
        builder.Add(kind, context, new { @ref = reference.Value });
    }

    private static ResolvedContext ResolveRuleItem(LcmCache cache, PhonologicalRuleItem item,
        CanonicalId? plannedNaturalClass)
    {
        ArgumentNullException.ThrowIfNull(item);
        if ((item.Phoneme is null ? 0 : 1) + (item.NaturalClass is null ? 0 : 1) != 1)
            throw new InvalidOperationException("Each rule input or output item must name one phoneme or natural class.");
        if (item.Phoneme is { } phoneme)
        {
            _ = SoundSystemAuthoring.RequirePhoneme(cache, phoneme);
            return new("PhSimpleContextSeg", phoneme);
        }
        var naturalClass = item.NaturalClass ?? throw new InvalidOperationException("A rule item needs a phoneme or natural class.");
        if (naturalClass != plannedNaturalClass) _ = RequireNaturalClass(cache, naturalClass);
        return new("PhSimpleContextNC", naturalClass);
    }

    private static IReadOnlyList<ResolvedContext> ResolveContexts(LcmCache cache,
        IReadOnlyList<EnvironmentContext> contexts, bool isLeft, CanonicalId? plannedNaturalClass)
    {
        var resolved = new ResolvedContext[contexts.Count];
        for (var index = 0; index < contexts.Count; index++)
        {
            var item = contexts[index] ?? throw new InvalidOperationException("A rule context item cannot be null.");
            var fields = (item.Phoneme is null ? 0 : 1) + (item.NaturalClass is null ? 0 : 1) +
                (item.Boundary is null ? 0 : 1) + (item.BoundaryMarker is null ? 0 : 1);
            if (fields != 1)
                throw new InvalidOperationException("Each context item must name one phoneme, natural class or boundary marker.");
            if (item.Phoneme is { } phoneme)
                resolved[index] = ResolveRuleItem(cache, new PhonologicalRuleItem(Phoneme: phoneme), plannedNaturalClass);
            else if (item.NaturalClass is { } naturalClass)
            {
                if (naturalClass != plannedNaturalClass) _ = RequireNaturalClass(cache, naturalClass);
                resolved[index] = new("PhSimpleContextNC", naturalClass);
            }
            else if (item.BoundaryMarker is { } boundaryId)
            {
                var marker = SoundSystemAuthoring.FirstSet(cache).BoundaryMarkersOC
                    .SingleOrDefault(candidate => CanonicalId.FromGuid(candidate.Guid) == boundaryId);
                if (marker is null)
                    throw new InvalidOperationException("A rule boundary must be a marker in the first phoneme set.");
                resolved[index] = new("PhSimpleContextBdry", boundaryId);
            }
            else
            {
                var boundary = item.Boundary!;
                if (boundary is not ("word" or "morpheme"))
                    throw new InvalidOperationException("Boundary must be 'word' or 'morpheme'.");
                if (boundary == "word" && index != (isLeft ? 0 : contexts.Count - 1))
                    throw new InvalidOperationException("A word boundary must be at the outer edge of its rule context.");
                var markerId = ResolveBoundary(boundary);
                resolved[index] = new("PhSimpleContextBdry", markerId);
            }
        }
        return resolved;
    }

    private static IPhNaturalClass RequireNaturalClass(LcmCache cache, CanonicalId id)
    {
        var naturalClass = ReferenceFieldLowering.Resolve<IPhNaturalClass>(cache, id, "AuthorPhonologicalRule");
        var data = cache.LangProject.PhonologicalDataOA;
        if (!data.NaturalClassesOS.Contains(naturalClass))
            throw new InvalidOperationException("A rule natural class must belong to this project's phonological data.");
        if (naturalClass is IPhNCSegments segments)
        {
            if (segments.SegmentsRC.Count == 0)
                throw new InvalidOperationException("An empty natural class cannot be used in a rewrite rule.");
            foreach (var segment in segments.SegmentsRC)
                _ = SoundSystemAuthoring.RequirePhoneme(cache, SoundSystemAuthoring.Id(segment));
        }
        else if (naturalClass is IPhNCFeatures features)
        {
            if (features.FeaturesOA is null || features.FeaturesOA.FeatureSpecsOC.Count == 0)
                throw new InvalidOperationException("An empty natural class cannot be used in a rewrite rule.");
        }
        else
            throw new InvalidOperationException("Only segment and feature natural classes are supported in rewrite rules.");
        return naturalClass;
    }

    private static CanonicalId ResolveBoundary(string boundary)
    {
        return CanonicalId.FromGuid(boundary == "word"
            ? LangProjectTags.kguidPhRuleWordBdry
            : LangProjectTags.kguidPhRuleMorphBdry);
    }

    private static void ValidatePlacement(IPhPhonData data, Placement? placement)
    {
        if (data.PhonRulesOS.Count == 0)
        {
            if (placement is not null)
                throw new InvalidOperationException("The first rule cannot name a neighboring rule for placement.");
            return;
        }
        if (placement is null)
            throw new InvalidOperationException("A nonempty rule sequence requires declared placement beside existing rules.");
        var rules = data.PhonRulesOS.Select(rule => CanonicalId.FromGuid(rule.Guid)).ToArray();
        var afterIndex = placement.After is { } after ? Array.IndexOf(rules, after) : -1;
        var beforeIndex = placement.Before is { } before ? Array.IndexOf(rules, before) : rules.Length;
        if ((placement.After is not null && afterIndex < 0) || (placement.Before is not null && beforeIndex < 0) ||
            beforeIndex != afterIndex + 1)
            throw new InvalidOperationException("Rule placement anchors are stale or are not adjacent in the current order.");
    }

    private static int DirectionValue(PhonologicalRuleDirection direction) => direction switch
    {
        PhonologicalRuleDirection.LeftToRightIterative => 0,
        PhonologicalRuleDirection.RightToLeftIterative => 1,
        PhonologicalRuleDirection.Simultaneous => 2,
        _ => throw new InvalidOperationException("The rule direction is not supported."),
    };

    private static void RequireContextLength(IReadOnlyCollection<EnvironmentContext> contexts, string side)
    {
        if (contexts.Count is 0 or > 8)
            throw new InvalidOperationException($"The {side} context must contain between 1 and 8 simple items.");
    }

    private static OperationEnvelope AddDependencies(List<OperationEnvelope> operations,
        OperationEnvelope operation, IReadOnlyList<OperationEnvelope> prerequisites)
    {
        var dependencies = operation.DependsOn.Concat(prerequisites.Select(item =>
                new OperationDependency(item.OperationId)))
            .DistinctBy(item => item.OperationId).ToArray();
        var replacement = new OperationEnvelope(operation.OperationId, operation.Kind, operation.EntityId,
            operation.Target, operation.After, operation.Placement, dependencies, rationale: operation.Rationale);
        var index = operations.FindIndex(item => item.OperationId == operation.OperationId);
        operations[index] = replacement;
        return replacement;
    }

    private sealed record ResolvedContext(string ClassName, CanonicalId Reference);
}
