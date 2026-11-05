using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>The complete reference-bearing state of one flat prohibition, including legacy occurrences.</summary>
public sealed record AdhocReferenceState(string Kind, CanonicalId? First, IReadOnlyList<CanonicalId> Rest,
    IReadOnlyList<CanonicalId> Allomorphs, int Adjacency, bool Disabled)
{
    public static AdhocReferenceState Read(IMoAdhocProhib rule) => rule switch
    {
        IMoAlloAdhocProhib allo => new("allomorph", allo.FirstAllomorphRA is { } first ? Id(first) : null,
            allo.RestOfAllosRS.Select(Id).ToArray(), allo.AllomorphsRS.Select(Id).ToArray(), rule.Adjacency, rule.Disabled),
        IMoMorphAdhocProhib morph => new("morpheme", morph.FirstMorphemeRA is { } first ? Id(first) : null,
            morph.RestOfMorphsRS.Select(Id).ToArray(), [], rule.Adjacency, rule.Disabled),
        _ => throw new InvalidOperationException("Only flat allomorph and morpheme prohibitions can be edited or deleted."),
    };

    public static AdhocReferenceState Parse(JsonElement value)
    {
        const string kind = "AdhocReferenceState";
        SoundSystemIntentParsing.Object(value, kind, "kind", "first", "rest", "allomorphs", "adjacency", "disabled");
        var type = SoundSystemIntentParsing.Text(value, "kind");
        if (type is not ("allomorph" or "morpheme")) throw new ContractParseException("kind must be allomorph or morpheme.");
        if (!value.TryGetProperty("first", out var first)) throw new ContractParseException("first is required, with null for no first.");
        var firstId = first.ValueKind == JsonValueKind.Null ? (CanonicalId?)null : SoundSystemIntentParsing.Id(value, "first");
        if (!value.TryGetProperty("adjacency", out var adjacency) || !adjacency.TryGetInt32(out var adjacencyValue))
            throw new ContractParseException("adjacency must be an integer.");
        if (!value.TryGetProperty("disabled", out var disabled) || disabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new ContractParseException("disabled must be a Boolean.");
        return new(type, firstId, Ids(value, "rest"), Ids(value, "allomorphs"), adjacencyValue, disabled.GetBoolean());
    }

    public object Wire() => new { kind = Kind, first = First?.Value, rest = Rest.Select(id => id.Value).ToArray(),
        allomorphs = Allomorphs.Select(id => id.Value).ToArray(), adjacency = Adjacency, disabled = Disabled };

    public bool Same(AdhocReferenceState other) => Kind == other.Kind && First == other.First &&
        Rest.SequenceEqual(other.Rest) && Allomorphs.SequenceEqual(other.Allomorphs) &&
        Adjacency == other.Adjacency && Disabled == other.Disabled;

    private static CanonicalId Id(ICmObject value) => CanonicalId.FromGuid(value.Guid);
    private static CanonicalId[] Ids(JsonElement value, string property)
    {
        if (!value.TryGetProperty(property, out var array) || array.ValueKind != JsonValueKind.Array)
            throw new ContractParseException($"{property} must be an array of canonical ids.");
        return array.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String &&
            CanonicalId.TryParse(item.GetString(), out var id) ? CanonicalId.FromGuid(id.ToGuid()) :
            throw new ContractParseException($"{property} must contain canonical ids.")).ToArray();
    }
}

/// <summary>Retargets occurrences while preserving their entry, order, adjacency and enabled state.</summary>
public sealed record EditAdhocProhibitionReferencesIntent(CanonicalId Target, AdhocReferenceState Expected, AdhocReferenceState References);

/// <summary>Deletes one prohibition only when its complete authored state still matches.</summary>
public sealed record DeleteAdhocProhibitionIntent(CanonicalId Target, AdhocReferenceState Expected);

public static class AdhocProhibitionReferencesIntentParser
{
    public static EditAdhocProhibitionReferencesIntent Edit(JsonElement value)
    {
        SoundSystemIntentParsing.Object(value, "EditAdhocProhibitionReferences", "target", "expected", "references");
        return new(SoundSystemIntentParsing.Id(value, "target"), AdhocReferenceState.Parse(value.GetProperty("expected")),
            AdhocReferenceState.Parse(value.GetProperty("references")));
    }

    public static DeleteAdhocProhibitionIntent Delete(JsonElement value)
    {
        SoundSystemIntentParsing.Object(value, "DeleteAdhocProhibition", "target", "expected");
        return new(SoundSystemIntentParsing.Id(value, "target"), AdhocReferenceState.Parse(value.GetProperty("expected")));
    }
}

public static class EditAdhocProhibitionReferencesComposer
{
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, EditAdhocProhibitionReferencesIntent intent)
    {
        var rule = ReferenceFieldLowering.Resolve<IMoAdhocProhib>(cache, intent.Target, "EditAdhocProhibitionReferences");
        AdhocReferenceEditing.RequireExpected(rule, intent.Expected);
        AdhocReferenceEditing.Validate(cache, intent.Expected, intent.References);
        return [new(CanonicalId.Mint(), AdhocReferenceOperationKinds.Edit, target: intent.Target,
            after: JsonSerializer.SerializeToElement(new { expected = intent.Expected.Wire(), references = intent.References.Wire() }),
            rationale: "Retarget the prohibition references while keeping its other settings.")];
    }
}

public static class DeleteAdhocProhibitionComposer
{
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, DeleteAdhocProhibitionIntent intent)
    {
        var rule = ReferenceFieldLowering.Resolve<IMoAdhocProhib>(cache, intent.Target, "DeleteAdhocProhibition");
        AdhocReferenceEditing.RequireExpected(rule, intent.Expected);
        OwnedGraphGuard.RequireNoExternalReferences(cache, rule);
        return [new(CanonicalId.Mint(), AdhocReferenceOperationKinds.Delete, target: intent.Target,
            after: JsonSerializer.SerializeToElement(new { expected = intent.Expected.Wire(), graphDigest = OwnedGraphGuard.Digest(cache, rule) }),
            rationale: "Delete this prohibition and review native grammatical-analysis cleanup in the Dry Run.")];
    }
}
