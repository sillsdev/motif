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
    private const int MaximumItems = 8;

    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, AuthorPhonologicalRuleIntent intent,
        Func<CanonicalId>? mintId = null, CanonicalId? plannedNaturalClass = null,
        CanonicalId? authoredRuleId = null, IReadOnlyList<OperationEnvelope>? prerequisites = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        SoundSystemAuthoring.RequireName(intent.Name);
        _ = DirectionValue(intent.Direction);
        if (intent.Input is null || intent.Output is null || intent.Left is null || intent.Right is null)
            throw new InvalidOperationException("Rule input, output, left context and right context must be supplied; a context with no items is empty.");
        RequireItemCount(intent.Input, "input");
        if (intent.RightHandSides is not null && (intent.Output.Count + intent.Left.Count + intent.Right.Count != 0))
            throw new InvalidOperationException("Use rightHandSides or the single-outcome output/left/right shorthand, not both.");
        RequireOutcomes(cache, intent.Outcomes);

        FeatureRuleAuthoring.Validate(cache, intent);
        var data = cache.LangProject.PhonologicalDataOA;

        ValidatePlacement(data, intent.Placement);
        var input = intent.Input.Select(item => ResolveRuleItem(cache, item, plannedNaturalClass)).ToArray();
        var outcomes = intent.Outcomes.Select(o => ResolveOutcome(cache, o, plannedNaturalClass)).ToArray();

        var builder = new SoundSystemAuthoring.Builder(mintId);
        var boundaryOperations = EnsureBoundaries(cache, builder,
            outcomes.SelectMany(o => o.Left).ToArray(), outcomes.SelectMany(o => o.Right).ToArray());

        var dataId = SoundSystemAuthoring.Id(data);
        var ruleId = builder.Create(PhPhonDataPhonRulesOperationKinds.Create, dataId,
            new { @class = "PhRegularRule" }, intent.Placement, authoredRuleId);
        var create = builder.Operations[^1];
        if (prerequisites is { Count: > 0 }) AddDependencies(builder.Operations, create, prerequisites);
        builder.Text(PhSegmentRuleNameOperationKinds.SetName, ruleId, cache, cache.DefaultAnalWs, intent.Name);
        builder.Add(PhSegmentRuleDirectionOperationKinds.SetDirection, ruleId,
            new { value = DirectionValue(intent.Direction) });

        var lowering = new FeatureRuleAuthoring.Lowering(cache, builder, dataId, null);
        var features = new RuleOutcomeFeatures(cache, builder);
        CanonicalId? previous = null;
        foreach (var outcome in outcomes)
        {
            var rhsId = builder.Create(PhRegularRuleRightHandSidesOperationKinds.Create, ruleId,
                placement: previous is { } prior ? new Placement(prior, null) : null);
            AddStructure(cache, builder, dataId, ruleId, rhsId, previous is null ? input : null,
                outcome.Output, outcome.Left, outcome.Right, lowering: lowering);
            features.Apply(rhsId, outcome.Required, outcome.Excluded);
            AddCategories(builder, rhsId, outcome.Categories);
            previous = rhsId;
        }

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

    internal static void RequireOutcomes(LcmCache cache, IReadOnlyList<PhonologicalRuleOutcome> outcomes)
    {
        if (outcomes.Count < 1)
            throw new InvalidOperationException("A rewrite rule needs at least one rightHandSide.");
        foreach (var outcome in outcomes)
        {
            ArgumentNullException.ThrowIfNull(outcome);
            RequireItemCount(outcome.Output, "output");
            RequireContextLength(outcome.Left, "left");
            RequireContextLength(outcome.Right, "right");
            ValidateBoundaryMarkers(cache, outcome.Left, outcome.Right);
            RuleOutcomeFeatures.Validate(cache, outcome.RequiredFeatures, outcome.ExcludedFeatures);
            _ = Categories(cache, outcome.InputCategories);
        }
    }

    internal sealed record ResolvedOutcome(IReadOnlyList<ResolvedContext> Output, IReadOnlyList<ResolvedContext> Left,
        IReadOnlyList<ResolvedContext> Right, IReadOnlyList<ICmObject> Required, IReadOnlyList<ICmObject> Excluded, IReadOnlyList<CanonicalId> Categories);

    internal static ResolvedOutcome ResolveOutcome(LcmCache cache, PhonologicalRuleOutcome outcome, CanonicalId? planned = null) => new(
        outcome.Output.Select(i => ResolveRuleItem(cache, i, planned)).ToArray(),
        ResolveContexts(cache, outcome.Left, true, planned), ResolveContexts(cache, outcome.Right, false, planned),
        RuleOutcomeFeatures.Items(cache, outcome.RequiredFeatures ?? [], "requiredFeatures"),
        RuleOutcomeFeatures.Items(cache, outcome.ExcludedFeatures ?? [], "excludedFeatures"), Categories(cache, outcome.InputCategories));

    private static CanonicalId[] Categories(LcmCache cache, IReadOnlyList<CanonicalId>? ids)
    {
        var values = (ids ?? []).Select(LexicalFormAuthoring.Normalize).ToArray();
        if (values.Select(id => id.ToGuid()).Distinct().Count() != values.Length)
            throw new InvalidOperationException("An outcome's inputCategories must not repeat an identity.");
        foreach (var id in values) _ = ReferenceFieldLowering.Resolve<IPartOfSpeech>(cache, id, "rule input category");
        return values;
    }

    internal static void AddCategories(SoundSystemAuthoring.Builder builder, CanonicalId rhsId, IReadOnlyList<CanonicalId> categories)
    {
        foreach (var id in categories)
            builder.Add(PhSegRuleRHSInputPOSesOperationKinds.AddRefInputPOSes, rhsId, new { member = id.Value });
    }

    /// <summary>Creates each part that is given under a rule whose same part is empty.</summary>
    internal static void AddStructure(LcmCache cache, SoundSystemAuthoring.Builder builder, CanonicalId dataId, CanonicalId ruleId,
        CanonicalId rhsId, IReadOnlyList<ResolvedContext>? input, IReadOnlyList<ResolvedContext>? output,
        IReadOnlyList<ResolvedContext>? left, IReadOnlyList<ResolvedContext>? right,
        IReadOnlyList<OperationEnvelope>? after = null,
        IReadOnlyDictionary<string, IPhFeatureConstraint>? existingVariables = null,
        FeatureRuleAuthoring.Lowering? lowering = null)
    {
        lowering ??= new FeatureRuleAuthoring.Lowering(cache, builder, dataId, existingVariables);
        input = lowering.Prepare(input);
        output = lowering.Prepare(output);
        left = lowering.Prepare(left);
        right = lowering.Prepare(right);
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
            SetContextReference(builder, contextId, item);
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
            SetContextReference(builder, contextId, contexts[0]);
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
            SetContextReference(builder, id, item);
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
        ResolvedContext item)
    {
        var kind = item.ClassName switch
        {
            "PhSimpleContextSeg" => PhSimpleContextSegFeatureStructureOperationKinds.SetFeatureStructure,
            "PhSimpleContextNC" => PhSimpleContextNCFeatureStructureOperationKinds.SetFeatureStructure,
            "PhSimpleContextBdry" => PhSimpleContextBdryFeatureStructureOperationKinds.SetFeatureStructure,
            _ => throw new InvalidOperationException($"Context class '{item.ClassName}' is not a supported simple context."),
        };
        builder.Add(kind, context, new { @ref = item.Reference.Value }, requires: item.Requires);
        foreach (var (constraint, positive) in item.Constraints ?? [])
            builder.Add(positive ? PhSimpleContextNCPlusConstrOperationKinds.AddRefPlusConstr
                : PhSimpleContextNCMinusConstrOperationKinds.AddRefMinusConstr,
                context, new { member = constraint.Value }, requires: item.Requires);
    }

    internal static ResolvedContext ResolveRuleItem(LcmCache cache, PhonologicalRuleItem item,
        CanonicalId? plannedNaturalClass)
    {
        ArgumentNullException.ThrowIfNull(item);
        if ((item.Phoneme is null ? 0 : 1) + (item.NaturalClass is null ? 0 : 1) + (item.Change is null ? 0 : 1) != 1)
            throw new InvalidOperationException("Each rule input or output item must name one phoneme, natural class or Output feature change.");
        if (item.Change is { } change)
            return new("PhSimpleContextNC", default, Change: change, Variables: item.Variables);
        if (item.Phoneme is { } phoneme)
        {
            _ = SoundSystemAuthoring.RequirePhoneme(cache, phoneme);
            return new("PhSimpleContextSeg", phoneme);
        }
        var naturalClass = item.NaturalClass ?? throw new InvalidOperationException("A rule item needs a phoneme or natural class.");
        if (naturalClass != plannedNaturalClass) _ = RequireNaturalClass(cache, naturalClass, item.Variables is { Count: > 0 });
        return new("PhSimpleContextNC", naturalClass, Variables: item.Variables);
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
                if (naturalClass != plannedNaturalClass) _ = RequireNaturalClass(cache, naturalClass, item.Variables is { Count: > 0 });
                resolved[index] = new("PhSimpleContextNC", naturalClass, item.Minimum, item.Maximum, Variables: item.Variables);
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

    private static IPhNaturalClass RequireNaturalClass(LcmCache cache, CanonicalId id, bool hasVariables = false)
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
            if (!hasVariables && (features.FeaturesOA is null || features.FeaturesOA.FeatureSpecsOC.Count == 0))
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
            throw new InvalidOperationException($"A rewrite rule supports at most {MaximumItems} {side} items.");
    }

    internal sealed record ResolvedContext(string ClassName, CanonicalId Reference, int? Minimum = null, int? Maximum = null,
        IReadOnlyList<PhonologicalFeatureValue>? Change = null, IReadOnlyList<PhonologicalRuleVariable>? Variables = null,
        IReadOnlyList<(CanonicalId Id, bool Positive)>? Constraints = null, IReadOnlyList<OperationEnvelope>? Requires = null);
}
