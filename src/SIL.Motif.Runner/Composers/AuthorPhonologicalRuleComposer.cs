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
    private const int MaximumItems = 1;

    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, AuthorPhonologicalRuleIntent intent,
        Func<CanonicalId>? mintId = null, CanonicalId? plannedNaturalClass = null,
        CanonicalId? authoredRuleId = null, IReadOnlyList<OperationEnvelope>? prerequisites = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        SoundSystemAuthoring.RequireName(intent.Name);
        if (intent.Input is null || intent.Output is null || intent.Left is null || intent.Right is null)
            throw new InvalidOperationException("Rule input, output, left context and right context must be supplied; a context with no items is empty.");
        RequireItemCount(intent.Input, "input");
        RequireItemCount(intent.Output, "output");
        RequireContextLength(intent.Left, "left");
        RequireContextLength(intent.Right, "right");

        var data = cache.LangProject.PhonologicalDataOA;
        ValidateBoundaryMarkers(cache, intent.Left, intent.Right);
        ValidatePlacement(data, intent.Placement);
        var input = intent.Input.Select(item => ResolveRuleItem(cache, item, plannedNaturalClass)).ToArray();
        var output = intent.Output.Select(item => ResolveRuleItem(cache, item, plannedNaturalClass)).ToArray();
        var left = ResolveContexts(cache, intent.Left, isLeft: true, plannedNaturalClass);
        var right = ResolveContexts(cache, intent.Right, isLeft: false, plannedNaturalClass);

        var builder = new SoundSystemAuthoring.Builder(mintId);
        var boundaryOperations = EnsureBoundaries(cache, builder, left, right);

        var dataId = SoundSystemAuthoring.Id(data);
        var ruleId = builder.Create(PhPhonDataPhonRulesOperationKinds.Create, dataId,
            new { @class = "PhRegularRule" }, intent.Placement, authoredRuleId);
        var create = builder.Operations[^1];
        if (prerequisites is { Count: > 0 }) AddDependencies(builder.Operations, create, prerequisites);
        builder.Text(PhSegmentRuleNameOperationKinds.SetName, ruleId, cache, cache.DefaultAnalWs, intent.Name);
        builder.Add(PhSegmentRuleDirectionOperationKinds.SetDirection, ruleId,
            new { value = DirectionValue(intent.Direction) });

        var rhsId = builder.Create(PhRegularRuleRightHandSidesOperationKinds.Create, ruleId);
        AddStructure(builder, dataId, ruleId, rhsId, input, output, left, right);

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

    /// <summary>Creates each part that is given under a rule whose same part is empty.</summary>
    internal static void AddStructure(SoundSystemAuthoring.Builder builder, CanonicalId dataId, CanonicalId ruleId,
        CanonicalId rhsId, IReadOnlyList<ResolvedContext>? input, IReadOnlyList<ResolvedContext>? output,
        IReadOnlyList<ResolvedContext>? left, IReadOnlyList<ResolvedContext>? right,
        IReadOnlyList<OperationEnvelope>? after = null)
    {
        if (input is not null) CreateOwnedItems(builder, ruleId, input, PhSegmentRuleStrucDescOperationKinds.Create, after);
        if (output is not null) CreateOwnedItems(builder, rhsId, output, PhSegRuleRHSStrucChangeOperationKinds.Create, after);
        if (left is not null)
            CreateRuleContext(builder, dataId, rhsId, left, PhSegRuleRHSLeftContextOperationKinds.Create, after);
        if (right is not null)
            CreateRuleContext(builder, dataId, rhsId, right, PhSegRuleRHSRightContextOperationKinds.Create, after);
    }

    internal static IReadOnlyList<OperationEnvelope> EnsureBoundaries(LcmCache cache,
        SoundSystemAuthoring.Builder builder, IReadOnlyList<ResolvedContext>? left, IReadOnlyList<ResolvedContext>? right)
    {
        var boundaryOperations = new List<OperationEnvelope>();
        var referencedBoundaries = (left ?? []).Concat(right ?? []).Select(context => context.Reference.ToGuid()).ToHashSet();
        if (!referencedBoundaries.Contains(LangProjectTags.kguidPhRuleWordBdry) &&
            !referencedBoundaries.Contains(LangProjectTags.kguidPhRuleMorphBdry))
            return boundaryOperations;
        var firstSet = SoundSystemAuthoring.FirstSet(cache);
        var firstSetId = SoundSystemAuthoring.Id(firstSet);
        if (referencedBoundaries.Contains(LangProjectTags.kguidPhRuleWordBdry))
            boundaryOperations.AddRange(SoundSystemAuthoring.EnsureOwnedBoundaryMarker(cache, builder, firstSet,
                firstSetId, LangProjectTags.kguidPhRuleWordBdry, "word", "#"));
        if (referencedBoundaries.Contains(LangProjectTags.kguidPhRuleMorphBdry))
            boundaryOperations.AddRange(SoundSystemAuthoring.EnsureOwnedBoundaryMarker(cache, builder, firstSet,
                firstSetId, LangProjectTags.kguidPhRuleMorphBdry, "morpheme", "+"));
        return boundaryOperations;
    }

    private static void CreateOwnedItems(SoundSystemAuthoring.Builder builder, CanonicalId owner,
        IReadOnlyList<ResolvedContext> items, string createKind, IReadOnlyList<OperationEnvelope>? after)
    {
        CanonicalId? previous = null;
        foreach (var item in items)
        {
            var placement = previous is { } prior ? new Placement(prior, null) : null;
            var contextId = builder.Create(createKind, owner, new { @class = item.ClassName }, placement,
                requires: after);
            SetContextReference(builder, contextId, item.Reference, item.ClassName);
            previous = contextId;
        }
    }

    private static void CreateRuleContext(SoundSystemAuthoring.Builder builder, CanonicalId dataId,
        CanonicalId rhsId, IReadOnlyList<ResolvedContext> contexts, string createKind,
        IReadOnlyList<OperationEnvelope>? after)
    {
        if (contexts.Count == 0) return;
        if (contexts.Count == 1 && contexts[0].Minimum is null)
        {
            var contextId = builder.Create(createKind, rhsId, new { @class = contexts[0].ClassName }, requires: after);
            SetContextReference(builder, contextId, contexts[0].Reference, contexts[0].ClassName);
            return;
        }
        if (contexts.Count == 1)
        {
            var item = contexts[0];
            var iterationId = builder.Create(createKind, rhsId, new { @class = "PhIterationContext" }, requires: after);
            builder.Add(PhIterationContextBoundsOperationKinds.SetBounds, iterationId,
                new { minimum = item.Minimum, maximum = item.Maximum });
            var inner = CreatePooled(builder, dataId, item with { Minimum = null, Maximum = null }, after);
            builder.Add(PhIterationContextMemberOperationKinds.SetMember, iterationId, new { @ref = inner.Id.Value },
                requires: inner.Operations);
            return;
        }

        var sequenceId = builder.Create(createKind, rhsId, new { @class = "PhSequenceContext" }, requires: after);
        CanonicalId? previousMember = null;
        OperationEnvelope? previousAdd = null;
        foreach (var item in contexts)
        {
            var member = CreatePooled(builder, dataId, item, after);
            var placement = previousMember is { } previous ? new Placement(previous, null) : null;
            var required = previousAdd is { } prior ? member.Operations.Append(prior).ToArray() : member.Operations;
            var add = builder.Add(PhSequenceContextMembersOperationKinds.AddRefMembers, sequenceId,
                new { member = member.Id.Value }, placement: placement, requires: required);
            previousMember = member.Id;
            previousAdd = add;
        }
    }

    private static (CanonicalId Id, OperationEnvelope[] Operations) CreatePooled(
        SoundSystemAuthoring.Builder builder, CanonicalId dataId, ResolvedContext item,
        IReadOnlyList<OperationEnvelope>? after)
    {
        var start = builder.Operations.Count;
        CanonicalId id;
        if (item.Minimum is null)
        {
            id = builder.Create(PhPhonDataContextsOperationKinds.Create, dataId, new { @class = item.ClassName },
                requires: after);
            SetContextReference(builder, id, item.Reference, item.ClassName);
        }
        else
        {
            var inner = CreatePooled(builder, dataId, item with { Minimum = null, Maximum = null }, after);
            id = builder.Create(PhPhonDataContextsOperationKinds.Create, dataId, new { @class = "PhIterationContext" },
                requires: after);
            builder.Add(PhIterationContextBoundsOperationKinds.SetBounds, id,
                new { minimum = item.Minimum, maximum = item.Maximum });
            builder.Add(PhIterationContextMemberOperationKinds.SetMember, id, new { @ref = inner.Id.Value },
                requires: inner.Operations);
        }
        return (id, builder.Operations.Skip(start).ToArray());
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

    internal static ResolvedContext ResolveRuleItem(LcmCache cache, PhonologicalRuleItem item,
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

    internal static IReadOnlyList<ResolvedContext> ResolveContexts(LcmCache cache,
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
            if (item.Minimum is not null && (item.Phoneme is null && item.NaturalClass is null))
                throw new InvalidOperationException("Only a phoneme or natural class can be optional or repeated.");
            if (item.Phoneme is { } phoneme)
                resolved[index] = ResolveRuleItem(cache, new PhonologicalRuleItem(Phoneme: phoneme), plannedNaturalClass)
                    with { Minimum = item.Minimum, Maximum = item.Maximum };
            else if (item.NaturalClass is { } naturalClass)
            {
                if (naturalClass != plannedNaturalClass) _ = RequireNaturalClass(cache, naturalClass);
                resolved[index] = new("PhSimpleContextNC", naturalClass, item.Minimum, item.Maximum);
            }
            else if (item.BoundaryMarker is { } boundaryId)
            {
                var marker = SoundSystemAuthoring.FirstSet(cache).BoundaryMarkersOC
                    .SingleOrDefault(candidate => candidate.Guid == boundaryId.ToGuid());
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

    internal static void ValidateBoundaryMarkers(LcmCache cache, IReadOnlyList<EnvironmentContext> leftContexts,
        IReadOnlyList<EnvironmentContext> rightContexts)
    {
        var firstSet = cache.LangProject.PhonologicalDataOA.PhonemeSetsOS.FirstOrDefault();
        if (firstSet is null)
            return;

        var named = leftContexts.Concat(rightContexts).Select(context => context.BoundaryMarker)
            .OfType<CanonicalId>().Select(id => id.ToGuid()).ToHashSet();
        var stored = firstSet.BoundaryMarkersOC.SelectMany(marker => marker.CodesOS
                .SelectMany(code => code.Representation.AvailableWritingSystemIds
                    .Select(ws => code.Representation.get_String(ws)?.Text)
                    .Where(text => text is "#" or "+")
                    .Select(text => (Marker: marker, Representation: text!)))
                .DistinctBy(item => (item.Marker.Guid, item.Representation)))
            .ToArray();
        var conflicts = stored
            .Where(item => item.Marker.Guid != ReservedBoundaryId(item.Representation))
            .Where(item => named.Contains(item.Marker.Guid) || !stored.Any(other =>
                other.Marker.Guid == ReservedBoundaryId(item.Representation) && other.Representation == item.Representation))
            .Select(item =>
            {
                var role = item.Representation == "#" ? "word" : "morpheme";
                var expectedId = CanonicalId.FromGuid(ReservedBoundaryId(item.Representation));
                return $"'{item.Representation}' is stored under marker {CanonicalId.FromGuid(item.Marker.Guid)}; " +
                    $"the {role} boundary must use standard marker {expectedId}.";
            })
            .ToArray();
        if (conflicts.Length > 0)
            throw new InvalidOperationException("The first phoneme set has conflicting boundary markers: " +
                string.Join(" ", conflicts) + " Merge the stray marker into the standard one with compose-merge-boundary-markers.");
    }

    private static Guid ReservedBoundaryId(string representation) => representation switch
    {
        "#" => LangProjectTags.kguidPhRuleWordBdry,
        "+" => LangProjectTags.kguidPhRuleMorphBdry,
        _ => throw new InvalidOperationException("A reserved boundary representation was not recognized."),
    };

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
        var rules = data.PhonRulesOS.Select(rule => rule.Guid).ToArray();
        var afterIndex = placement.After is { } after ? Array.IndexOf(rules, after.ToGuid()) : -1;
        var beforeIndex = placement.Before is { } before ? Array.IndexOf(rules, before.ToGuid()) : rules.Length;
        if ((placement.After is not null && afterIndex < 0) || (placement.Before is not null && beforeIndex < 0) ||
            beforeIndex != afterIndex + 1)
            throw new InvalidOperationException("Rule placement anchors are stale or are not adjacent in the current order.");
    }

    internal static int DirectionValue(PhonologicalRuleDirection direction) => direction switch
    {
        PhonologicalRuleDirection.LeftToRightIterative => 0,
        PhonologicalRuleDirection.RightToLeftIterative => 1,
        PhonologicalRuleDirection.Simultaneous => 2,
        _ => throw new InvalidOperationException("The rule direction is not supported."),
    };

    internal static void RequireContextLength(IReadOnlyCollection<EnvironmentContext> contexts, string side)
    {
        if (contexts.Count > 8)
            throw new InvalidOperationException($"The {side} context must contain at most 8 simple items.");
    }

    internal static OperationEnvelope AddDependencies(List<OperationEnvelope> operations,
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

    internal static void RequireItemCount(IReadOnlyCollection<PhonologicalRuleItem> items, string side)
    {
        if (items.Count > MaximumItems)
            throw new InvalidOperationException($"A rewrite rule supports at most one {side} item; Black (2025, fn 66) warns more will not do what you hope.");
    }

    internal sealed record ResolvedContext(string ClassName, CanonicalId Reference, int? Minimum = null, int? Maximum = null);
}
