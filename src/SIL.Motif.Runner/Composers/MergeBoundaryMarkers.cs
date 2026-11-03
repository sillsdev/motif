using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>Merges a stray duplicate of the word or morpheme boundary marker into the standard marker.</summary>
/// <param name="Marker">The duplicate boundary marker to merge away.</param>
public sealed record MergeBoundaryMarkersIntent(CanonicalId Marker);

/// <summary>Parses the closed JSON intent for merging a boundary marker.</summary>
public static class MergeBoundaryMarkersIntentParser
{
    /// <summary>Reads the duplicate marker; it is required.</summary>
    public static MergeBoundaryMarkersIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, "MergeBoundaryMarkers", "marker");
        return new(SoundSystemIntentParsing.Id(authored, "marker"));
    }
}

/// <summary>
/// Re-points every rule or environment context that uses a stray duplicate '#' or '+' marker at the standard marker,
/// then deletes the duplicate (creating the standard marker first when the set lacks it), so a project with two '+' markers accepts rules that name a boundary. Anything other
/// than a boundary context that uses the marker is refused.
/// </summary>
public static class MergeBoundaryMarkersComposer
{
    private const string Rationale = "Authored by the MergeBoundaryMarkers composer: the marker duplicates the standard one.";

    /// <summary>Composes the merge without mutating the caller's project.</summary>
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, MergeBoundaryMarkersIntent intent)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var id = LexicalFormAuthoring.Normalize(intent.Marker);
        var marker = BoundaryMarkerRetirement.Inspect(cache, id, allowUsers: true);
        var spelling = marker.CodesOS.SelectMany(code => code.Representation.AvailableWritingSystemIds
            .Select(ws => code.Representation.get_String(ws)?.Text)).OfType<string>().First();
        var standardGuid = spelling == "#" ? LangProjectTags.kguidPhRuleWordBdry : LangProjectTags.kguidPhRuleMorphBdry;
        var standard = CanonicalId.FromGuid(standardGuid);
        var users = marker.ReferringObjects.Where(user => !ReferenceEquals(user, marker) && user.Owner != marker).ToArray();
        var foreign = users.FirstOrDefault(user => user is not IPhSimpleContextBdry);
        if (foreign is not null)
            throw new InvalidOperationException(
                $"The marker {id.Value} is used by a {foreign.ClassName} ({foreign.Guid:D}), which this merge cannot re-point.");

        var set = cache.LangProject.PhonologicalDataOA.PhonemeSetsOS[0];
        var builder = new SoundSystemAuthoring.Builder(null);
        var created = SoundSystemAuthoring.EnsureOwnedBoundaryMarker(cache, builder, set, SoundSystemAuthoring.Id(set),
            standardGuid, spelling == "#" ? "word" : "morpheme", spelling);
        var operations = new List<OperationEnvelope>(builder.Operations);
        foreach (var user in users.OrderBy(user => user.Guid))
        {
            var repoint = new OperationEnvelope(CanonicalId.Mint(),
                PhSimpleContextBdryFeatureStructureOperationKinds.SetFeatureStructure,
                target: CanonicalId.FromGuid(user.Guid), after: JsonSerializer.SerializeToElement(new { @ref = standard.Value }),
                rationale: Rationale);
            operations.Add(repoint);
            if (created.Count > 0) AuthorPhonologicalRuleComposer.AddDependencies(operations, repoint, created);
        }
        var delete = new OperationEnvelope(CanonicalId.Mint(), BoundaryMarkerDeleteOperationKinds.DeleteBoundaryMarkers,
            target: id, after: JsonSerializer.SerializeToElement(new { }), rationale: Rationale);
        operations.Add(delete);
        AuthorPhonologicalRuleComposer.AddDependencies(operations, delete, operations.Take(operations.Count - 1).ToArray());
        return operations;
    }
}
