using System;
using System.Globalization;
using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.PanGloss;

namespace SIL.Motif.Commands.Queries;

/// <summary>
/// Maps one <c>pangloss.diagnostic.v1</c> capture to Motif's response: PanGloss's authored GUIDs become canonical ids,
/// and anything it could not bind to authored data stays an explicit unknown. Nothing here runs a parser or infers a
/// cause the capture did not record.
/// </summary>
public static class RejectionTraceProjection
{
    /// <summary>Projects the single row of a capture, keeping at most <paramref name="maxPaths"/> terminal paths.</summary>
    public static RejectionTraceResponse Project(
        RejectionTraceDocument document, string input, string tracedAgainst, int maxPaths)
    {
        var header = document.Header;
        var grammar = Obj(header, "grammar");
        var mappings = new Dictionary<(string Kind, string Key), JsonElement>();
        foreach (var mapping in document.Of("mapping"))
        {
            var o = mapping.GetProperty("object");
            mappings[(Str(o, "kind")!, Str(o, "key")!)] = o;
        }
        var events = document.Of("event").ToDictionary(e => e.GetProperty("eventId").GetInt32());
        var wordEnd = document.Of("word_end").FirstOrDefault();

        var outputs = document.Of("output").Select(o => ToOutput(o, mappings)).ToList();
        var outputsShown = outputs.Take(RejectionTraceLimits.OutputPreviewCap).ToList();

        var terminal = document.Of("path").Where(p => Str(p, "outcome") != "intermediate").ToList();
        var shown = terminal.Take(maxPaths).Select(p => ToPath(p, events, mappings)).ToList();

        var search = wordEnd.ValueKind == JsonValueKind.Object ? Obj(wordEnd, "search") : default;
        var capture = wordEnd.ValueKind == JsonValueKind.Object ? Obj(wordEnd, "capture") : default;
        var notes = new List<string>
        {
            "Each stop is the first terminal decision PanGloss recorded on that path; optional skips and failed " +
            "alternatives on a surviving path are observations, not causes.",
        };
        if (search.ValueKind == JsonValueKind.Object && Str(search, "status") != "complete")
            notes.Add("The search did not run to completion, so a path with no recorded stop is not a proven dead end.");
        if (terminal.Count == 0)
            notes.Add("PanGloss kept no terminal path for this word.");
        if (outputs.Count > 0 && terminal.Any(p => Str(p, "outcome") == "rejected"))
            notes.Add("The word has outputs; the rejected paths are other candidates for the same spelling.");

        return new RejectionTraceResponse(
            "parse", input, tracedAgainst,
            new RejectionTraceProducer(Str(header, "parserVersion") ?? "unknown", Str(header, "parserBuild") ?? "unknown",
                Str(header, "schemaVersion") ?? RejectionTraceDocument.SchemaVersion, Str(header, "mode") ?? "unknown",
                Str(grammar, "hash"), Str(grammar, "semantics")),
            new RejectionTraceSearch(Str(search, "status") ?? "unknown", Str(search, "outputCensus") ?? "unavailable",
                Num(search, "outputCount"), Num(search, "workUnits"), Strings(search, "stopReasons"))
            { ErrorCode = search.ValueKind == JsonValueKind.Object ? Str(Obj(search, "error"), "code") : null },
            new RejectionTraceCapture(Str(capture, "coverage") ?? "none", Strings(capture, "truncatedBy"),
                Count(capture, "omittedPaths"), Count(capture, "omittedEvents"), Count(capture, "omittedOutputs")),
            outputsShown, shown,
            new RejectionTraceBound(maxPaths, terminal.Count, shown.Count, terminal.Count - shown.Count,
                outputsShown.Count, outputs.Count - outputsShown.Count))
        { Notes = notes };
    }

    private static RejectionTracePath ToPath(
        JsonElement path, Dictionary<int, JsonElement> events, Dictionary<(string, string), JsonElement> mappings)
    {
        var outcome = Str(path, "outcome")!;
        var id = path.GetProperty("pathId").GetInt32();
        int? parent = Str(path, "parentStatus") == "retained" ? Int(path, "parentPathId") : null;
        var outputId = Int(path, "outputId");
        var firstReject = Int(path, "firstRejectEventId");
        if (outcome == "accepted")
            return new RejectionTracePath(id, parent, Int(path, "depth"), outcome, "accepted", outputId, null, null,
                outputId is null ? "This path produced an output that was not kept." : $"This path produced output {outputId}.");
        if (firstReject is { } eventId && events.TryGetValue(eventId, out var recorded))
        {
            var rejection = ToEvent(recorded, mappings);
            return new RejectionTracePath(id, parent, Int(path, "depth"), outcome, "rejected", null, rejection, null,
                rejection.Sentence);
        }
        var why = outcome == "rejected"
            ? "unknown: PanGloss marked the path rejected but kept no terminal event for it"
            : "unknown: PanGloss did not record a stop for this path, which was still unresolved when the capture ended";
        return new RejectionTracePath(id, parent, Int(path, "depth"), outcome, "unknown", null, null, why,
            "Motif cannot name where this path stopped; " + why["unknown: ".Length..] + ".");
    }

    private static RejectionTraceEvent ToEvent(JsonElement e, Dictionary<(string, string), JsonElement> mappings)
    {
        var reason = Str(e, "reason")!;
        var refs = e.GetProperty("objectRefs").EnumerateArray().ToList();
        var subjectRef = refs.FirstOrDefault(r => Str(r, "role") == "subject");
        var subject = subjectRef.ValueKind == JsonValueKind.Object
            ? ToObject(Str(subjectRef, "kind")!, Str(subjectRef, "key")!, mappings)
            : Unmapped("unknown", string.Empty, "the event names no subject");
        var related = refs.Where(r => Str(r, "role") != "subject")
            .Select(r => new RejectionTraceRelated(Str(r, "role")!, ToObject(Str(r, "kind")!, Str(r, "key")!, mappings)))
            .ToList();
        return new RejectionTraceEvent(e.GetProperty("eventId").GetInt32(), Str(e, "direction")!, Str(e, "stage")!, reason,
            subject, related, Operands(Obj(e, "operands")),
            RejectionTraceSentences.For(reason, Str(e, "kind") == "reject"));
    }

    private static IReadOnlyDictionary<string, string> Operands(JsonElement operands)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (operands.ValueKind != JsonValueKind.Object) return result;
        foreach (var property in operands.EnumerateObject())
        {
            if (result.Count >= RejectionTraceLimits.OperandEntryCap) break;
            var value = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString()!,
                JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => property.Value.GetRawText(),
                JsonValueKind.Null => "unavailable",
                _ => null,
            };
            if (value is null) continue;
            result[property.Name] = value.Length > RejectionTraceLimits.OperandValueCap
                ? value[..RejectionTraceLimits.OperandValueCap] : value;
        }
        // A built surface is an object of value and kind, so it is not a scalar the loop above kept.
        if (operands.TryGetProperty("built", out var built) && built.ValueKind == JsonValueKind.Object &&
            Str(built, "value") is { } builtValue && result.Count < RejectionTraceLimits.OperandEntryCap)
            result["built"] = builtValue.Length > RejectionTraceLimits.OperandValueCap
                ? builtValue[..RejectionTraceLimits.OperandValueCap] : builtValue;
        return result;
    }

    private static RejectionTraceOutput ToOutput(JsonElement o, Dictionary<(string, string), JsonElement> mappings) =>
        new(o.GetProperty("outputId").GetInt32(), Str(o, "surface") ?? string.Empty, Int(o, "rootIndex"),
            Str(o, "projection") ?? "unknown",
            o.TryGetProperty("morphs", out var morphs) && morphs.ValueKind == JsonValueKind.Array
                ? morphs.EnumerateArray().Select(m => ToMorph(m, mappings)).ToList()
                : []);

    private static RejectionTraceMorph ToMorph(JsonElement m, Dictionary<(string, string), JsonElement> mappings)
    {
        RejectionTraceObject? Ref(string name) => m.TryGetProperty(name, out var r) && r.ValueKind == JsonValueKind.Object
            ? ToObject(Str(r, "kind")!, Str(r, "key")!, mappings) : null;
        var forms = m.TryGetProperty("forms", out var f) && f.ValueKind == JsonValueKind.Array
            ? f.EnumerateArray().Select(r => ToObject(Str(r, "kind")!, Str(r, "key")!, mappings)).ToList()
            : [];
        return new RejectionTraceMorph(Str(m, "kind") ?? "unknown", Ref("entry"), Ref("msa"), forms,
            Ref("inflectionType"), Str(m, "senseResolution") ?? "unavailable",
            Strings(m, "associatedSenseGuids").Select(ToCanonical).ToList());
    }

    private static RejectionTraceObject ToObject(
        string kind, string key, Dictionary<(string, string), JsonElement> mappings)
    {
        if (!mappings.TryGetValue((kind, key), out var mapping))
            return Unmapped(kind, key, "the capture carries no mapping for this object");
        var quality = Str(mapping, "quality") ?? "unavailable";
        var binding = Str(mapping, "bindingStatus") ?? "unavailable";
        var ids = new List<RejectionTraceId>();
        foreach (var source in mapping.GetProperty("sources").EnumerateArray())
        {
            var relation = Str(source, "relation")!;
            ids.Add(new RejectionTraceId(ToCanonical(Str(source, "guid")!), Str(source, "kind")!, relation,
                source.GetProperty("ordinal").GetInt64(), IsSubjectRelation(kind, relation)));
        }
        var subjectIds = ids.Where(i => i.IsSubject).Select(i => i.Id).Distinct(StringComparer.Ordinal).Count();
        var resolved = binding is "exact" or "multiple" && subjectIds == 1;
        var unknown = resolved ? null : binding switch
        {
            _ when subjectIds > 1 => "unknown: PanGloss bound several authored objects to this one and does not say which",
            "structural" => "unknown: a compiled object with no authored counterpart",
            "unavailable" => "unknown: PanGloss could not establish the authored source",
            _ => ids.Count > 0 ? "unknown: only related objects are bound, not this one" : "unknown: no authored source recorded",
        };
        return new RejectionTraceObject(kind, key, quality, binding, resolved, ids, unknown);
    }

    private static RejectionTraceObject Unmapped(string kind, string key, string why) =>
        new(kind, key, "unavailable", "unavailable", false, [], "unknown: " + why);

    private static bool IsSubjectRelation(string objectKind, string relation) => objectKind switch
    {
        "allomorph" => relation == "form_part",
        "slot" => relation == "slot",
        "rule" => relation is "authored" or "rule",
        "subrule" => relation is "authored" or "subrule",
        _ => relation == "authored",
    };

    private static string ToCanonical(string guid) =>
        CanonicalId.FromGuid(Guid.Parse(guid, CultureInfo.InvariantCulture)).ToString();

    private static JsonElement Obj(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Object ? value : default;

    private static string? Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? Int(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : null;

    private static long? Num(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : null;

    private static List<string> Strings(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).ToList()
            : [];

    private static RejectionTraceCount Count(JsonElement element, string name)
    {
        var count = Obj(element, name);
        return new RejectionTraceCount(Num(count, "value"), Str(count, "accuracy") ?? "unknown");
    }
}
