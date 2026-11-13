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
/// Names the operations that delete one grammar or text object nothing refers to. Each targets the object itself and
/// fails at Apply while anything refers to it, naming each referrer, so LibLCM's reference cleanup never rewrites
/// another object behind the linguist's back.
/// </summary>
public static class UnusedObjectDeleteOperationKinds
{
    /// <summary>Deletes an unreferenced item of the Exception Features list.</summary>
    public const string DeleteExceptionFeature = "lists/cmPossibilityList/deletePossibilities";

    /// <summary>Deletes an unreferenced phonological environment.</summary>
    public const string DeleteEnvironment = "grammar/phPhonData/deleteEnvironments";

    /// <summary>Deletes an unreferenced natural class.</summary>
    public const string DeleteNaturalClass = "grammar/phPhonData/deleteNaturalClasses";

    /// <summary>Deletes one Note of a Text Segment.</summary>
    public const string DeleteTextSegmentNote = "texts/segment/deleteNotes";

#pragma warning disable CA2255 // Load-time registration makes these kinds available before parsing and dispatch.
    [ModuleInitializer]
    internal static void Register()
    {
        foreach (var kind in new[] { DeleteExceptionFeature, DeleteEnvironment, DeleteNaturalClass, DeleteTextSegmentNote })
        {
            OperationKindRegistry.Register(kind);
            OperationHandlerRegistry.Register(kind, new UnusedObjectDeleteHandler(kind));
            OperationSlotRegistry.RegisterEntityDeletion(kind);
        }
    }
#pragma warning restore CA2255
}

/// <summary>Resolves, checks, deletes and re-reads one unreferenced object, for any of these kinds.</summary>
internal sealed class UnusedObjectDeleteHandler(string kind) : IOperationHandler
{
    public ExpectedEffect ApplyAndCaptureEffect(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        if (operation.After is not { } after)
            throw new InvalidOperationException($"Operation '{operation.OperationId.Value}' of kind '{kind}' requires 'after'.");
        FsFeatStrucFeatureSpecsPayload.Parse(after, kind);
        var (id, target) = Resolve(cache, operation);
        touchedTargets.Add(id);
        var before = ReferenceFieldAlternatives.ToAlternatives(id);
        if (target is INote note && note.Owner is ISegment segment)
            segment.NotesOS.Remove(note);
        else
            target.Delete();
        return new ExpectedEffect(id, Field, before, ReferenceFieldAlternatives.ToAlternatives(null));
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation)
    {
        var (id, _) = TargetOnly(cache, operation);
        var current = ReferenceFieldAlternatives.ToAlternatives(id);
        return new ExpectedEffect(id, Field, current, current);
    }

    private string Field => kind switch
    {
        UnusedObjectDeleteOperationKinds.DeleteExceptionFeature => SnapshotFields.CmPossibilityListPossibilities,
        UnusedObjectDeleteOperationKinds.DeleteEnvironment => SnapshotFields.PhPhonDataEnvironments,
        UnusedObjectDeleteOperationKinds.DeleteNaturalClass => SnapshotFields.PhPhonDataNaturalClasses,
        _ => SnapshotFields.TextSegmentNotes,
    };

    private (CanonicalId Id, ICmObject Target) TargetOnly(LcmCache cache, OperationEnvelope operation) => kind switch
    {
        UnusedObjectDeleteOperationKinds.DeleteExceptionFeature => Cast(TargetResolution.Resolve<ICmPossibility>(cache, operation, kind)),
        UnusedObjectDeleteOperationKinds.DeleteEnvironment => Cast(TargetResolution.Resolve<IPhEnvironment>(cache, operation, kind)),
        UnusedObjectDeleteOperationKinds.DeleteNaturalClass => Cast(TargetResolution.Resolve<IPhNaturalClass>(cache, operation, kind)),
        _ => Cast(TargetResolution.Resolve<INote>(cache, operation, kind)),
    };

    private static (CanonicalId, ICmObject) Cast<T>((CanonicalId Id, T Object) resolved) where T : ICmObject =>
        (resolved.Id, resolved.Object);

    private (CanonicalId Id, ICmObject Target) Resolve(LcmCache cache, OperationEnvelope operation)
    {
        var (id, target) = TargetOnly(cache, operation);
        switch (target)
        {
            case ICmPossibility feature: UnusedObjects.RequireUnusedExceptionFeature(cache, feature, id, kind); break;
            case IPhEnvironment environment: UnusedObjects.RequireUnusedEnvironment(environment, id, kind); break;
            case IPhNaturalClass naturalClass: UnusedObjects.RequireUnusedNaturalClass(cache, naturalClass, id, kind); break;
            case INote note: UnusedObjects.RequireSegmentNote(note, id, kind); break;
        }
        return (id, target);
    }
}
