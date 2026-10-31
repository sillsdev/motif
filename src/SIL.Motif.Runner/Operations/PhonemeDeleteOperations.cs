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
/// Names the two operations that take a phoneme, or one written representation of a phoneme, out of the first
/// phoneme set. Both target the member itself, as <see cref="FsFeatStrucFeatureSpecsOperationKinds.DeleteFeatureSpecs"/>
/// does, and both refuse while anything else still refers to it, so LibLCM's reference cleanup never rewrites a rule,
/// an environment or a natural class on its own.
/// </summary>
public static class PhonemeDeleteOperationKinds
{
    /// <summary>Deletes one phoneme and the codes and features it owns.</summary>
    public const string DeletePhonemes = "grammar/phPhonemeSet/deletePhonemes";

    /// <summary>Deletes one written representation of a phoneme that keeps at least one other.</summary>
    public const string DeleteCodes = "grammar/phTerminalUnit/deleteCodes";

#pragma warning disable CA2255 // Load-time registration makes these kinds available before parsing and dispatch.
    [ModuleInitializer]
    internal static void Register()
    {
        OperationKindRegistry.Register(DeletePhonemes);
        OperationKindRegistry.Register(DeleteCodes);
        OperationHandlerRegistry.Register(DeletePhonemes, new PhonemeDeleteHandler(DeletePhonemes));
        OperationHandlerRegistry.Register(DeleteCodes, new PhonemeDeleteHandler(DeleteCodes));
        OperationSlotRegistry.RegisterEntityDeletion(DeletePhonemes);
        OperationSlotRegistry.RegisterEntityDeletion(DeleteCodes);
    }
#pragma warning restore CA2255
}

/// <summary>Resolves, checks, deletes and re-reads one phoneme or code, for either deletion kind.</summary>
internal sealed class PhonemeDeleteHandler(string kind) : IOperationHandler
{
    private bool IsPhoneme => kind == PhonemeDeleteOperationKinds.DeletePhonemes;

    private string Field => IsPhoneme ? SnapshotFields.PhPhonemeSetPhonemes : SnapshotFields.PhTerminalUnitCodes;

    public ExpectedEffect ApplyAndCaptureEffect(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        RequireEmptyPayload(operation);
        var (id, member) = Resolve(cache, operation);
        touchedTargets.Add(id);
        var users = member.ReferringObjects.Where(user => !ReferenceEquals(user, member)).ToArray();
        if (users.Length > 0)
            throw new InvalidOperationException(
                $"'{kind}': '{id.Value}' is still used by {Describe(users[0])}; remove that use first.");
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
        if (IsPhoneme)
        {
            var (id, phoneme) = TargetResolution.Resolve<IPhPhoneme>(cache, operation, kind);
            if (phoneme.Owner is not IPhPhonemeSet)
                throw new InvalidOperationException($"'{kind}': '{id.Value}' is not a member of a phoneme set.");
            return (id, phoneme);
        }

        var (codeId, code) = TargetResolution.Resolve<IPhCode>(cache, operation, kind);
        if (code.Owner is not IPhPhoneme owner)
            throw new InvalidOperationException($"'{kind}': '{codeId.Value}' is not a representation of a phoneme.");
        if (owner.CodesOS.Count < 2)
            throw new InvalidOperationException(
                $"'{kind}': '{codeId.Value}' is the only representation of its phoneme; delete the phoneme instead.");
        return (codeId, code);
    }

    private void RequireEmptyPayload(OperationEnvelope operation)
    {
        if (operation.After is not { } after)
            throw new InvalidOperationException(
                $"Operation '{operation.OperationId.Value}' of kind '{kind}' requires 'after'.");
        FsFeatStrucFeatureSpecsPayload.Parse(after, kind);
    }

    private static string Describe(ICmObject user) => $"a {user.ClassName} ({user.Guid:D})";
}
