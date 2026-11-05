using System.Text;
using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsing;

namespace SIL.Motif.Runner.Operations;

/// <summary>A Note with an explicit creation identity and analysis-language content.</summary>
public sealed record TextCreateNote(CanonicalId Id, string Text);

/// <summary>A segment's explicit wordform bindings, optional baseline offset and analysis-language annotations.</summary>
public sealed record TextCreateSegment(CanonicalId Id, IReadOnlyList<CanonicalId> Words, int? BeginOffset,
    string? FreeTranslation, string? LiteralTranslation, IReadOnlyList<TextCreateNote> Notes);

/// <summary>A paragraph with explicit segments; Text enables LibLCM parsing instead of legacy space joining.</summary>
public sealed record TextCreateParagraph(CanonicalId Id, string WritingSystem, IReadOnlyList<TextCreateSegment> Segments, string? Text);

/// <summary>The closed text creation payload, including optional segment annotations in AnalysisWritingSystem.</summary>
public sealed record TextCreatePayload(string NameWritingSystem, string NameText, CanonicalId Contents,
    IReadOnlyList<TextCreateParagraph> Paragraphs, string? AnalysisWritingSystem);

/// <summary>Parses text creation without imposing limits beyond LibLCM's model.</summary>
public static class LangProjectTextsCreatePayload
{
    private const string Kind = LangProjectTextsOperationKinds.Create;

    public static TextCreatePayload Parse(JsonElement after)
    {
        Object(after, "name", "contents", "paragraphs", "analysisWs");
        var name = Required(after, "name", JsonValueKind.Object);
        Object(name, "ws", "text");
        var nameWs = Tag(name, "ws");
        var nameText = Text(name, "text");
        var contents = Id(after, "contents");
        var seen = new HashSet<CanonicalId>(CanonicalId.ByGuid) { contents };
        CanonicalId Unique(JsonElement item)
        {
            var id = Id(item, "id");
            return seen.Add(id) ? id : throw Refuse("a created identity is used twice.");
        }
        var paragraphs = new List<TextCreateParagraph>();
        foreach (var item in Required(after, "paragraphs", JsonValueKind.Array).EnumerateArray())
        {
            Object(item, "id", "ws", "segments", "text");
            var paragraphId = Unique(item);
            var ws = Tag(item, "ws");
            var baseline = OptionalText(item, "text");
            var segments = new List<TextCreateSegment>();
            foreach (var segment in Required(item, "segments", JsonValueKind.Array).EnumerateArray())
            {
                Object(segment, "id", "words", "beginOffset", "freeTranslation", "literalTranslation", "notes");
                var segmentId = Unique(segment);
                var words = Required(segment, "words", JsonValueKind.Array).EnumerateArray().Select(word =>
                    word.ValueKind == JsonValueKind.String && CanonicalId.TryParse(word.GetString()!, out var id)
                        ? id : throw Refuse("every word is a wordform identity.")).ToArray();
                if (baseline is null && words.Length == 0) throw Refuse("each legacy segment holds at least one word.");
                int? offset = null;
                if (segment.TryGetProperty("beginOffset", out var value))
                {
                    if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number) || number < 0 || baseline is null || number >= baseline.Length)
                        throw Refuse("beginOffset needs a valid offset into paragraph text.");
                    offset = number;
                }
                if (baseline is not null && (offset is null || segments.Count == 0 && offset != 0 ||
                    segments.Count > 0 && offset <= segments[^1].BeginOffset))
                    throw Refuse("parsed segments need increasing beginOffset values starting at zero.");
                var notes = new List<TextCreateNote>();
                if (segment.TryGetProperty("notes", out _))
                    foreach (var note in Required(segment, "notes", JsonValueKind.Array).EnumerateArray())
                    {
                        Object(note, "id", "text");
                        notes.Add(new TextCreateNote(Unique(note), Text(note, "text")));
                    }
                segments.Add(new TextCreateSegment(segmentId, words, offset, OptionalText(segment, "freeTranslation"),
                    OptionalText(segment, "literalTranslation"), notes));
            }
            if (segments.Count == 0) throw Refuse("each paragraph holds at least one segment.");
            paragraphs.Add(new TextCreateParagraph(paragraphId, ws, segments, baseline));
        }
        if (paragraphs.Count == 0) throw Refuse("a text holds at least one paragraph.");
        var analysisWs = after.TryGetProperty("analysisWs", out _) ? Tag(after, "analysisWs") : null;
        if (analysisWs is null && paragraphs.SelectMany(p => p.Segments).Any(s =>
            s.FreeTranslation is not null || s.LiteralTranslation is not null || s.Notes.Count > 0))
            throw Refuse("annotations require analysisWs.");
        return new TextCreatePayload(nameWs, nameText, contents, paragraphs, analysisWs);
    }

    private static void Object(JsonElement item, params string[] fields)
    {
        ClosedPayloadParsing.RequireObject(item, Kind);
        ClosedPayloadParsing.RejectUnknownProperties(item, fields, Kind);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in item.EnumerateObject())
            if (!seen.Add(property.Name)) throw Refuse($"duplicate property '{property.Name}'.");
    }

    private static CanonicalId Id(JsonElement item, string field) => ClosedPayloadParsing.GetRequiredCanonicalId(item, field, Kind);
    private static string? OptionalText(JsonElement item, string field) => item.TryGetProperty(field, out _) ? Text(item, field) : null;
    private static string Text(JsonElement item, string field)
    {
        var text = ClosedPayloadParsing.GetRequiredString(item, field, Kind);
        if (string.IsNullOrWhiteSpace(text) || text != text.Trim() || !text.IsNormalized(NormalizationForm.FormD))
            throw Refuse($"'{field}' needs nonblank NFD text without edge spaces.");
        return text;
    }

    private static string Tag(JsonElement item, string field)
    {
        var tag = ClosedPayloadParsing.GetRequiredString(item, field, Kind);
        return !string.IsNullOrWhiteSpace(tag) ? tag : throw Refuse($"'{field}' needs a writing system tag.");
    }

    private static JsonElement Required(JsonElement parent, string name, JsonValueKind kind) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == kind ? value : throw Refuse($"'{name}' must be {kind}.");
    private static ContractParseException Refuse(string message) => new($"'{Kind}' operation 'after': {message}");
}
