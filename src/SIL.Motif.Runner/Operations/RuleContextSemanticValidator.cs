using System;
using System.Collections.Generic;
using System.Linq;
using SIL.LCModel;
using SIL.Motif.Contract.Model;

namespace SIL.Motif.Runner.Operations;

internal static class RuleContextSemanticValidator
{
    private const int MaximumSequenceMembers = 8;

    private static readonly HashSet<string> ContextOperationKinds = new(StringComparer.Ordinal)
    {
        PhPhonDataContextsOperationKinds.Create,
        PhSegmentRuleStrucDescOperationKinds.Create,
        PhSegRuleRHSStrucChangeOperationKinds.Create,
        PhSegRuleRHSLeftContextOperationKinds.Create,
        PhSegRuleRHSRightContextOperationKinds.Create,
        PhSimpleContextSegFeatureStructureOperationKinds.SetFeatureStructure,
        PhSimpleContextSegFeatureStructureOperationKinds.ClearFeatureStructure,
        PhSimpleContextNCFeatureStructureOperationKinds.SetFeatureStructure,
        PhSimpleContextNCFeatureStructureOperationKinds.ClearFeatureStructure,
        PhSimpleContextBdryFeatureStructureOperationKinds.SetFeatureStructure,
        PhSimpleContextBdryFeatureStructureOperationKinds.ClearFeatureStructure,
        PhSequenceContextMembersOperationKinds.AddRefMembers,
        PhSequenceContextMembersOperationKinds.RemoveRefMembers,
        PhSequenceContextMembersOperationKinds.MoveMembers,
    };

    public static void ValidateProposal(LcmCache cache, IReadOnlyList<OperationEnvelope> operations)
    {
        var relevant = operations.Where(operation => ContextOperationKinds.Contains(operation.Kind)).ToArray();
        if (relevant.Length == 0) return;

        var repository = cache.ServiceLocator.GetInstance<ICmObjectRepository>();
        var rules = new Dictionary<Guid, IPhSegmentRule>();
        var standaloneContexts = new Dictionary<Guid, IPhPhonContext>();
        foreach (var operation in relevant)
        {
            if (operation.Target is { } targetId && repository.IsValidObjectId(targetId.ToGuid()))
                Include(repository.GetObject(targetId.ToGuid()));
            if (operation.EntityId is { } entityId && repository.IsValidObjectId(entityId.ToGuid()))
                Include(repository.GetObject(entityId.ToGuid()));
        }

        foreach (var rule in rules.Values)
            ValidateRule(cache.LangProject.PhonologicalDataOA, rule);
        foreach (var context in standaloneContexts.Values)
            ValidateTree(cache.LangProject.PhonologicalDataOA, context, allowSequence: true);

        var contextRules = new Dictionary<Guid, HashSet<Guid>>();
        foreach (var rule in cache.LangProject.PhonologicalDataOA.PhonRulesOS.OfType<IPhSegmentRule>())
        {
            var seen = new HashSet<Guid>();
            foreach (var root in RuleRoots(rule)) Collect(root, rule.Guid, seen, contextRules);
        }
        foreach (var rule in rules.Values)
        {
            var seen = new HashSet<Guid>();
            foreach (var root in RuleRoots(rule)) Collect(root, rule.Guid, seen, contextRules);
            foreach (var id in seen)
                if (contextRules.TryGetValue(id, out var owners) && owners.Any(owner => owner != rule.Guid))
                    throw new InvalidOperationException("A rule context reference cannot be reused by another rule.");
        }

        void Include(ICmObject value)
        {
            if (value is IPhSegmentRule rule)
            {
                rules[rule.Guid] = rule;
                return;
            }
            if (value is IPhPhonContext context)
            {
                if (FindRule(context) is { } ownerRule) rules[ownerRule.Guid] = ownerRule;
                else standaloneContexts[value.Guid] = context;
                return;
            }
            for (var owner = value.Owner; owner is not null; owner = owner.Owner)
                if (owner is IPhSegmentRule parentRule)
                {
                    rules[parentRule.Guid] = parentRule;
                    return;
                }
        }
    }

    public static void ValidateRule(IPhPhonData data, IPhSegmentRule rule)
    {
        if (rule is not IPhRegularRule regular)
            throw new InvalidOperationException("Rule contexts are supported only on regular rewrite rules.");
        var seen = new HashSet<Guid>();
        var active = new HashSet<Guid>();
        foreach (var context in rule.StrucDescOS)
            ValidateNode(data, context, allowSequence: false, seen, active);
        foreach (var rhs in regular.RightHandSidesOS)
        {
            foreach (var context in rhs.StrucChangeOS)
                ValidateNode(data, context, allowSequence: false, seen, active);
            if (rhs.LeftContextOA is { } left)
                ValidateNode(data, left, allowSequence: true, seen, active);
            if (rhs.RightContextOA is { } right)
                ValidateNode(data, right, allowSequence: true, seen, active);
        }
    }

    private static void ValidateTree(IPhPhonData data, IPhPhonContext context, bool allowSequence) =>
        ValidateNode(data, context, allowSequence, new HashSet<Guid>(), new HashSet<Guid>());

    private static void ValidateNode(IPhPhonData data, IPhPhonContext context, bool allowSequence,
        HashSet<Guid> seen, HashSet<Guid> active)
    {
        if (active.Contains(context.Guid))
            throw new InvalidOperationException("Rule contexts cannot contain a cycle.");
        if (!seen.Add(context.Guid))
            throw new InvalidOperationException("A rule context reference cannot be reused.");
        if (!HasSupportedOwner(context, data))
            throw new InvalidOperationException("A rule context must be owned by its context pool, rule, or RHS.");

        active.Add(context.Guid);
        switch (context)
        {
            case IPhSimpleContextSeg segment:
                if (segment.FeatureStructureRA is not { } phoneme || !IsOwnedBy(phoneme, data) ||
                    !data.PhonemeSetsOS.Any(set => set.PhonemesOC.Contains(phoneme)))
                    throw new InvalidOperationException("A segment context must reference a phoneme in this project.");
                break;
            case IPhSimpleContextNC naturalClassContext:
                if (naturalClassContext.FeatureStructureRA is not { } naturalClass || !IsOwnedBy(naturalClass, data) ||
                    !data.NaturalClassesOS.Contains(naturalClass))
                    throw new InvalidOperationException("A natural-class context must reference a class in this project.");
                if (naturalClassContext.PlusConstrRS.Count != 0 || naturalClassContext.MinusConstrRS.Count != 0)
                    throw new InvalidOperationException("Natural-class feature constraints are not supported in rule contexts.");
                break;
            case IPhSimpleContextBdry boundaryContext:
                if (boundaryContext.FeatureStructureRA is not { } boundary ||
                    !IsOwnedBy(boundary, data) ||
                    !data.PhonemeSetsOS.Any(set => set.BoundaryMarkersOC.Contains(boundary)))
                    throw new InvalidOperationException("A boundary context must reference a marker in this project.");
                break;
            case IPhSequenceContext sequence when allowSequence:
                if (sequence.MembersRS.Count == 0 || sequence.MembersRS.Count > MaximumSequenceMembers)
                    throw new InvalidOperationException($"A rule context sequence must contain between 1 and {MaximumSequenceMembers} members.");
                foreach (var member in sequence.MembersRS)
                {
                    if (active.Contains(member.Guid))
                        throw new InvalidOperationException("Rule contexts cannot contain a cycle.");
                    if (member is IPhSequenceContext)
                        throw new InvalidOperationException("A rule context sequence can contain only simple contexts.");
                    ValidateNode(data, member, allowSequence: false, seen, active);
                }
                break;
            default:
                throw new InvalidOperationException($"Rule context class '{context.ClassName}' is not supported.");
        }
        active.Remove(context.Guid);
    }

    private static IEnumerable<IPhPhonContext> RuleRoots(IPhSegmentRule rule)
    {
        foreach (var context in rule.StrucDescOS) yield return context;
        if (rule is not IPhRegularRule regular) yield break;
        foreach (var rhs in regular.RightHandSidesOS)
        {
            foreach (var context in rhs.StrucChangeOS) yield return context;
            if (rhs.LeftContextOA is { } left) yield return left;
            if (rhs.RightContextOA is { } right) yield return right;
        }
    }

    private static void Collect(IPhPhonContext context, Guid ruleId, HashSet<Guid> seen,
        Dictionary<Guid, HashSet<Guid>> contextRules)
    {
        if (!seen.Add(context.Guid)) return;
        if (!contextRules.TryGetValue(context.Guid, out var rules))
        {
            rules = new HashSet<Guid>();
            contextRules.Add(context.Guid, rules);
        }
        rules.Add(ruleId);
        if (context is IPhSequenceContext sequence)
            foreach (var member in sequence.MembersRS.OfType<IPhPhonContext>())
                Collect(member, ruleId, seen, contextRules);
    }

    private static IPhSegmentRule? FindRule(ICmObject value)
    {
        for (ICmObject? current = value; current is not null; current = current.Owner)
            if (current is IPhSegmentRule rule) return rule;
        return null;
    }

    private static bool IsOwnedBy(ICmObject value, ICmObject owner)
    {
        for (ICmObject? current = value; current is not null; current = current.Owner)
            if (current.Guid == owner.Guid) return true;
        return false;
    }

    private static bool HasSupportedOwner(IPhPhonContext context, IPhPhonData data) => context.Owner switch
    {
        IPhPhonData ownerData => ownerData.Guid == data.Guid && ownerData.ContextsOS.Any(item => item.Guid == context.Guid),
        IPhSegmentRule ownerRule => ownerRule.StrucDescOS.Any(item => item.Guid == context.Guid),
        IPhSegRuleRHS ownerRhs => ownerRhs.StrucChangeOS.Any(item => item.Guid == context.Guid) ||
                                  ownerRhs.LeftContextOA?.Guid == context.Guid ||
                                  ownerRhs.RightContextOA?.Guid == context.Guid,
        _ => false,
    };
}
