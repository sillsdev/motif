using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Snapshot;

namespace SIL.Motif.Runner.Operations;

/// <summary>Names the operation that takes one stray duplicate boundary marker out of the first phoneme set.</summary>
public static class BoundaryMarkerDeleteOperationKinds
{
    /// <summary>Deletes one boundary marker and the codes it owns.</summary>
    public const string DeleteBoundaryMarkers = "grammar/phPhonemeSet/deleteBoundaryMarkers";

#pragma warning disable CA2255 // Load-time registration makes this kind available before parsing and dispatch.
    [ModuleInitializer]
    internal static void Register()
    {
        OperationKindRegistry.Register(DeleteBoundaryMarkers);
        OperationHandlerRegistry.Register(DeleteBoundaryMarkers, new BoundaryMarkerDeleteHandler());
        OperationSlotRegistry.RegisterEntityDeletion(DeleteBoundaryMarkers);
    }
#pragma warning restore CA2255
}

/// <summary>Decides whether a boundary marker is a stray duplicate that can go, for the composer and the handler.</summary>
internal static class BoundaryMarkerRetirement
{
    private const string Kind = BoundaryMarkerDeleteOperationKinds.DeleteBoundaryMarkers;

    internal static IPhBdryMarker Inspect(LcmCache cache, CanonicalId id)
    {
        if (!cache.ServiceLocator.GetInstance<ICmObjectRepository>().TryGetObject(id.ToGuid(), out var found) ||
            found is not IPhBdryMarker marker)
            throw new InvalidOperationException($"'{Kind}': '{id.Value}' is not a boundary marker.");
        var set = cache.LangProject.PhonologicalDataOA.PhonemeSetsOS.FirstOrDefault();
        if (set is null || marker.Owner != set)
            throw new InvalidOperationException($"'{Kind}': '{id.Value}' is not a boundary marker of the first phoneme set.");
        var wordMarker = LangProjectTags.kguidPhRuleWordBdry;
        var morphemeMarker = LangProjectTags.kguidPhRuleMorphBdry;
        if (marker.Guid == wordMarker || marker.Guid == morphemeMarker)
            throw new InvalidOperationException(
                $"'{Kind}': '{id.Value}' is the standard boundary marker that rules use; only a duplicate of it can be retired.");
        var spellings = Spellings(marker);
        if (spellings.Length == 0 || spellings.Any(text => text is not ("#" or "+")))
            throw new InvalidOperationException(
                $"'{Kind}': '{id.Value}' is not a duplicate of the word or morpheme boundary marker ('#' or '+').");
        foreach (var spelling in spellings)
        {
            var standard = spelling == "#" ? wordMarker : morphemeMarker;
            if (!set.BoundaryMarkersOC.Any(other => other.Guid == standard && Spellings(other).Contains(spelling)))
                throw new InvalidOperationException(
                    $"'{Kind}': the standard marker for '{spelling}' is not in the phoneme set, so '{id.Value}' is not a duplicate.");
        }
        var users = marker.ReferringObjects.Where(user => !ReferenceEquals(user, marker) && user.Owner != marker).ToArray();
        if (users.Length > 0)
            throw new InvalidOperationException(
                $"'{Kind}': '{id.Value}' is still used by a {users[0].ClassName} ({users[0].Guid:D}); " +
                "re-point that use to the standard marker first.");
        return marker;
    }

    private static string[] Spellings(IPhBdryMarker marker) => marker.CodesOS
        .SelectMany(code => code.Representation.AvailableWritingSystemIds
            .Select(ws => code.Representation.get_String(ws)?.Text).OfType<string>())
        .Distinct(StringComparer.Ordinal).ToArray();
}

/// <summary>Deletes one retired duplicate boundary marker and re-reads the set's markers.</summary>
internal sealed class BoundaryMarkerDeleteHandler : IOperationHandler
{
    private const string Kind = BoundaryMarkerDeleteOperationKinds.DeleteBoundaryMarkers;

    public ExpectedEffect ApplyAndCaptureEffect(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        RequireEmptyPayload(operation);
        var id = OperationSlotRegistry.Target(operation);
        var marker = BoundaryMarkerRetirement.Inspect(cache, id);
        touchedTargets.Add(id);
        var before = ReferenceFieldAlternatives.ToAlternatives(id);
        marker.Delete();
        return new ExpectedEffect(id, SnapshotFields.PhPhonemeSetBoundaryMarkers, before,
            ReferenceFieldAlternatives.ToAlternatives(null));
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation)
    {
        var id = OperationSlotRegistry.Target(operation);
        BoundaryMarkerRetirement.Inspect(cache, id);
        var current = ReferenceFieldAlternatives.ToAlternatives(id);
        return new ExpectedEffect(id, SnapshotFields.PhPhonemeSetBoundaryMarkers, current, current);
    }

    private static void RequireEmptyPayload(OperationEnvelope operation)
    {
        if (operation.After is not { } after)
            throw new InvalidOperationException($"Operation '{operation.OperationId.Value}' of kind '{Kind}' requires 'after'.");
        FsFeatStrucFeatureSpecsPayload.Parse(after, Kind);
    }
}
