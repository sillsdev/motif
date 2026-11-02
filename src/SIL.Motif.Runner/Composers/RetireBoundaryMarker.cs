using System;
using System.Collections.Generic;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>Retires a stray duplicate of the word or morpheme boundary marker in the first phoneme set.</summary>
/// <param name="Marker">The duplicate boundary marker to remove.</param>
public sealed record RetireBoundaryMarkerIntent(CanonicalId Marker);

/// <summary>Parses the closed JSON intent for retiring a boundary marker.</summary>
public static class RetireBoundaryMarkerIntentParser
{
    /// <summary>Reads the marker; it is required.</summary>
    public static RetireBoundaryMarkerIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, "RetireBoundaryMarker", "marker");
        return new(SoundSystemIntentParsing.Id(authored, "marker"));
    }
}

/// <summary>
/// Lowers a boundary marker retirement to one <see cref="BoundaryMarkerDeleteOperationKinds.DeleteBoundaryMarkers"/>.
/// Only an unreferenced duplicate of the standard '#' or '+' marker is retired; the standard marker and any marker
/// a rule, environment or other object uses are refused, so nothing is retargeted or cascaded.
/// </summary>
public static class RetireBoundaryMarkerComposer
{
    private const string Rationale = "Authored by the RetireBoundaryMarker composer: the marker duplicates the standard one and is unused.";

    /// <summary>Composes the retirement without mutating the caller's project.</summary>
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, RetireBoundaryMarkerIntent intent)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var id = LexicalFormAuthoring.Normalize(intent.Marker);
        BoundaryMarkerRetirement.Inspect(cache, id);
        return [new(CanonicalId.Mint(), BoundaryMarkerDeleteOperationKinds.DeleteBoundaryMarkers,
            target: id, after: JsonSerializer.SerializeToElement(new { }), rationale: Rationale)];
    }
}
