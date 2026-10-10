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

/// <summary>Builds identity-checked member edits for an existing segment natural class.</summary>
public static class EditNaturalClassComposer
{
    private const string ConstructName = "EditNaturalClass";

    /// <summary>Composes segment-member additions and removals after comparing the expected current identities.</summary>
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, EditNaturalClassIntent intent)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(intent.ExpectedMembers);
        ArgumentNullException.ThrowIfNull(intent.Members);

        var naturalClass = ResolveSegmentClass(cache, intent.Target);
        var expected = Unique(intent.ExpectedMembers, "expectedMembers");
        var desired = Unique(intent.Members, "members");
        if (expected.Length == 0 || desired.Length == 0)
            throw new InvalidOperationException($"'{ConstructName}': a segment class must have at least one member.");

        var current = naturalClass.SegmentsRC.Select(SoundSystemAuthoring.Id)
            .OrderBy(id => id.Value, StringComparer.Ordinal).ToArray();
        if (!expected.OrderBy(id => id.Value, StringComparer.Ordinal).SequenceEqual(current))
            throw new InvalidOperationException(
                $"'{ConstructName}': target '{intent.Target.Value}' members differ from the expected member list.");

        foreach (var member in desired)
            _ = SoundSystemAuthoring.RequirePhoneme(cache, member);

        var currentSet = current.ToHashSet();
        var desiredSet = desired.ToHashSet();
        return currentSet.Except(desiredSet).OrderBy(id => id.Value, StringComparer.Ordinal)
            .Select(id => ReferenceOperation(PhNCSegmentsSegmentsOperationKinds.RemoveRefSegments, intent.Target, id))
            .Concat(desiredSet.Except(currentSet).OrderBy(id => id.Value, StringComparer.Ordinal)
                .Select(id => ReferenceOperation(PhNCSegmentsSegmentsOperationKinds.AddRefSegments, intent.Target, id)))
            .ToArray();
    }

    /// <summary>Returns the environments, rules and insertions that use the target segment class.</summary>
    public static IReadOnlyList<ComposedRelatedObject> ReadAffectedUsers(LcmCache cache, EditNaturalClassIntent intent)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        _ = ResolveSegmentClass(cache, intent.Target);
        return ReadAffectedUsers(cache, intent.Target);
    }

    /// <summary>Returns the declared users of an existing project natural class.</summary>
    public static IReadOnlyList<ComposedRelatedObject> ReadAffectedUsers(LcmCache cache, CanonicalId target)
    {
        ArgumentNullException.ThrowIfNull(cache);
        var naturalClass = ResolveNaturalClass(cache, target);
        var abbreviation = naturalClass.Abbreviation.BestAnalysisAlternative.Text;
        if (string.IsNullOrWhiteSpace(abbreviation))
            throw new InvalidOperationException($"'{ConstructName}': target '{target.Value}' has no abbreviation.");

        var data = cache.LangProject.PhonologicalDataOA;
        var environments = data.EnvironmentsOS
            .Where(environment => environment.StringRepresentation?.Text?.Contains(
                $"[{abbreviation}]", StringComparison.Ordinal) == true)
            .Select(environment => new ComposedRelatedObject("environment", Id(environment),
                ReadName(environment.Name.BestAnalysisAlternative.Text, environment.Guid)));
        var rules = data.PhonRulesOS.OfType<IPhSegmentRule>()
            .Where(rule => RuleContexts(rule).OfType<IPhSimpleContextNC>()
                .Any(context => context.FeatureStructureRA?.Guid == naturalClass.Guid))
            .Select(rule => new ComposedRelatedObject("phonological-rule", Id(rule),
                ReadName(rule.Name.BestAnalysisAlternative.Text, rule.Guid)));
        var insertions = cache.ServiceLocator.GetInstance<ICmObjectRepository>().AllInstances()
            .OfType<IMoInsertNC>()
            .Where(insertion => insertion.ContentRA?.Guid == naturalClass.Guid)
            .Select(insertion => new ComposedRelatedObject("morphological-insertion", Id(insertion),
                "Natural-class insertion"));
        return environments.Concat(rules).Concat(insertions).DistinctBy(user => (user.Kind, user.Id))
            .OrderBy(user => user.Kind, StringComparer.Ordinal)
            .ThenBy(user => user.Id, StringComparer.Ordinal).ToArray();
    }

    private static IPhNCSegments ResolveSegmentClass(LcmCache cache, CanonicalId id)
    {
        var naturalClass = ResolveNaturalClass(cache, id);
        return naturalClass as IPhNCSegments ?? throw new InvalidOperationException(
            $"'{ConstructName}': target '{id.Value}' is not a segment class and cannot change subtype in place.");
    }

    private static IPhNaturalClass ResolveNaturalClass(LcmCache cache, CanonicalId id)
    {
        var naturalClass = ReferenceFieldLowering.Resolve<IPhNaturalClass>(cache, id, ConstructName);
        if (!cache.LangProject.PhonologicalDataOA.NaturalClassesOS.Contains(naturalClass))
            throw new InvalidOperationException($"'{ConstructName}': natural class '{id.Value}' is not owned by this project.");
        return naturalClass;
    }

    private static CanonicalId[] Unique(IReadOnlyList<CanonicalId> ids, string name)
    {
        if (ids.Distinct().Count() != ids.Count)
            throw new InvalidOperationException($"'{ConstructName}': '{name}' must contain unique identities.");
        return ids.ToArray();
    }

    internal static IEnumerable<IPhPhonContext> RuleContexts(IPhSegmentRule rule)
    {
        foreach (var context in rule.StrucDescOS)
            foreach (var nested in Flatten(context)) yield return nested;
        if (rule is not IPhRegularRule regular) yield break;
        foreach (var rhs in regular.RightHandSidesOS)
        {
            foreach (var context in rhs.StrucChangeOS)
                foreach (var nested in Flatten(context)) yield return nested;
            if (rhs.LeftContextOA is { } left)
                foreach (var nested in Flatten(left)) yield return nested;
            if (rhs.RightContextOA is { } right)
                foreach (var nested in Flatten(right)) yield return nested;
        }
    }

    private static IEnumerable<IPhPhonContext> Flatten(IPhPhonContext context)
    {
        yield return context;
        if (context is not IPhSequenceContext sequence) yield break;
        foreach (var member in sequence.MembersRS)
            foreach (var nested in Flatten(member)) yield return nested;
    }

    private static OperationEnvelope ReferenceOperation(string kind, CanonicalId target, CanonicalId member) =>
        new(CanonicalId.Mint(), kind, target: target,
            after: JsonSerializer.SerializeToElement(new { member = member.Value }),
            rationale: "Authored by the EditNaturalClass composer.");

    private static string Id(ICmObject value) => CanonicalId.FromGuid(value.Guid).Value;

    private static string ReadName(string? name, Guid guid) =>
        string.IsNullOrWhiteSpace(name) || name == "***" ? guid.ToString("D") : name;
}
