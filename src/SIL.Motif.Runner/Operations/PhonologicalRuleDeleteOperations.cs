using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Snapshot;

namespace SIL.Motif.Runner.Operations;

/// <summary>
/// Names the operations that remove a phonological rule, right-hand side or directly owned context.
/// Each targets the object itself; right-hand-side deletion also guards its complete current contents.
/// </summary>
public static class PhonologicalRuleDeleteOperationKinds
{
    /// <summary>Deletes one rule with everything it owns, and the pooled contexts only it used.</summary>
    public const string DeletePhonRules = "grammar/phPhonData/deletePhonRules";

    /// <summary>Deletes one right-hand side and its contexts so an ordered outcome sequence can be replaced.</summary>
    public const string DeleteRightHandSides = "grammar/phRegularRule/deleteRightHandSides";

    /// <summary>Deletes one context a rule owns directly, so the rule's side can be written again.</summary>
    public const string DeleteContexts = "grammar/phSegmentRule/deleteContexts";

#pragma warning disable CA2255 // Load-time registration makes these kinds available before parsing and dispatch.
    [ModuleInitializer]
    internal static void Register()
    {
        OperationKindRegistry.Register(DeletePhonRules);
        OperationKindRegistry.Register(DeleteRightHandSides);
        OperationHandlerRegistry.Register(DeleteRightHandSides, new PhonologicalRuleOutcomeDeleteHandler());
        OperationSlotRegistry.RegisterEntityDeletion(DeleteRightHandSides);
        OperationKindRegistry.Register(PhonologicalRuleConstraintCollectionHandler.Kind);
        OperationHandlerRegistry.Register(PhonologicalRuleConstraintCollectionHandler.Kind, new PhonologicalRuleConstraintCollectionHandler());
        OperationSlotRegistry.RegisterMany(PhonologicalRuleConstraintCollectionHandler.Kind, operation =>
            PhonologicalRuleConstraintCollectionHandler.Constraints(operation).Select(id => new OperationSlot(id, null)).ToArray());
        OperationKindRegistry.Register(DeleteContexts);
        OperationHandlerRegistry.Register(DeletePhonRules, new PhonologicalRuleDeleteHandler(DeletePhonRules));
        OperationHandlerRegistry.Register(DeleteContexts, new PhonologicalRuleDeleteHandler(DeleteContexts));
        OperationSlotRegistry.RegisterEntityDeletion(DeletePhonRules);
        OperationSlotRegistry.RegisterEntityDeletion(DeleteContexts);
    }
#pragma warning restore CA2255
}

internal sealed class PhonologicalRuleDeleteHandler(string kind) : IOperationHandler
{
    private bool IsRule => kind == PhonologicalRuleDeleteOperationKinds.DeletePhonRules;

    private string Field => IsRule ? SnapshotFields.PhPhonDataPhonRules : SnapshotFields.PhSegmentRuleStrucDesc;

    public ExpectedEffect ApplyAndCaptureEffect(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        if (operation.After is not { } after)
            throw new InvalidOperationException($"Operation '{operation.OperationId.Value}' of kind '{kind}' requires 'after'.");
        FsFeatStrucFeatureSpecsPayload.Parse(after, kind);
        var (id, member) = Resolve(cache, operation);
        touchedTargets.Add(id);
        var before = ReferenceFieldAlternatives.ToAlternatives(id);
        member.Delete();
        return new ExpectedEffect(id, Field, before, ReferenceFieldAlternatives.ToAlternatives(null));
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation)
    {
        var (id, _) = Resolve(cache, operation);
        var current = ReferenceFieldAlternatives.ToAlternatives(id);
        return new ExpectedEffect(id, Field, current, current);
    }

    private (CanonicalId Id, ICmObject Member) Resolve(LcmCache cache, OperationEnvelope operation)
    {
        if (IsRule)
        {
            var (id, rule) = TargetResolution.Resolve<IPhSegmentRule>(cache, operation, kind);
            if (rule.Owner is not IPhPhonData)
                throw new InvalidOperationException($"'{kind}': '{id.Value}' is not a rule of the phonological data.");
            var users = rule.ReferringObjects.Where(user => !ReferenceEquals(user, rule) && !IsOwnedBy(user, rule)).ToArray();
            if (users.Length > 0)
                throw new InvalidOperationException(
                    $"'{kind}': '{id.Value}' is still used by a {users[0].ClassName} ({users[0].Guid:D}); remove that use first.");
            return (id, rule);
        }

        var (contextId, context) = TargetResolution.Resolve<IPhPhonContext>(cache, operation, kind);
        var ownedByRule = context.Owner is IPhSegmentRule or IPhSegRuleRHS;
        if (!ownedByRule)
            throw new InvalidOperationException(
                $"'{kind}': '{contextId.Value}' is not a context that a rule or its right-hand side owns directly.");
        return (contextId, context);
    }

    private static bool IsOwnedBy(ICmObject value, ICmObject owner)
    {
        for (var current = value.Owner; current is not null; current = current.Owner)
            if (current.Guid == owner.Guid) return true;
        return false;
    }
}
