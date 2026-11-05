using System.Text;
using System.Text.Json;
using SIL.Motif.Contract.Parsing;

namespace SIL.Motif.Runner.Composers;

/// <summary>An authored segment's baseline and optional analysis-language annotations.</summary>
public sealed record AuthorTextSegment(string Text, string? FreeTranslation = null,
    string? LiteralTranslation = null, IReadOnlyList<string>? Notes = null);

/// <summary>One authored paragraph, whose baseline is its supplied segments joined by spaces.</summary>
public sealed record AuthorTextParagraph(IReadOnlyList<AuthorTextSegment> Segments);

/// <summary>Creates a Text in a vernacular writing system, with annotations in an analysis writing system.</summary>
/// <param name="Title">The Text's name, in NFD.</param>
/// <param name="WritingSystem">The writing system tag of the title and baseline.</param>
/// <param name="Paragraphs">The paragraphs in order.</param>
/// <param name="AnalysisWritingSystem">An analysis writing system tag; omitted uses the project's default.</param>
public sealed record AuthorTextIntent(string Title, string WritingSystem, IReadOnlyList<AuthorTextParagraph> Paragraphs,
    string? AnalysisWritingSystem = null);

/// <summary>
/// Parses a closed text intent with title, ws, optional analysisWs and paragraphs of segments.
/// A segment is a legacy array of whitespace tokens or an object with text, freeTranslation, literalTranslation and notes.
/// Baseline and annotation strings use NFD. Segment boundaries are determined by LibLCM from the paragraph text.
/// </summary>
public static class AuthorTextIntentParser
{
    private const string ConstructName = "AuthorText";

    public static AuthorTextIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, ConstructName, "title", "ws", "analysisWs", "paragraphs");
        var title = Nfd(SoundSystemIntentParsing.Text(authored, "title"), "title");
        var ws = SoundSystemIntentParsing.Text(authored, "ws");
        var analysisWs = authored.TryGetProperty("analysisWs", out _) ? SoundSystemIntentParsing.Text(authored, "analysisWs") : null;
        if (string.IsNullOrWhiteSpace(ws) || analysisWs is not null && string.IsNullOrWhiteSpace(analysisWs))
            throw Refuse("'ws' and 'analysisWs' must name writing systems.");
        var paragraphs = SoundSystemIntentParsing.Array(authored, "paragraphs").Select(paragraph =>
        {
            SoundSystemIntentParsing.Object(paragraph, ConstructName + " paragraph", "segments");
            var segments = SoundSystemIntentParsing.Array(paragraph, "segments").Select(Segment).ToArray();
            if (segments.Length == 0) throw Refuse("each paragraph holds at least one segment.");
            return new AuthorTextParagraph(segments);
        }).ToArray();
        if (paragraphs.Length == 0) throw Refuse("'paragraphs' needs at least one paragraph.");
        return new AuthorTextIntent(title, ws, paragraphs, analysisWs);
    }

    private static AuthorTextSegment Segment(JsonElement segment)
    {
        if (segment.ValueKind == JsonValueKind.Array)
        {
            var tokens = segment.EnumerateArray().Select(Token).ToArray();
            if (tokens.Length == 0) throw Refuse("each segment holds at least one token.");
            return new AuthorTextSegment(string.Join(" ", tokens));
        }
        SoundSystemIntentParsing.Object(segment, ConstructName + " segment", "text", "freeTranslation", "literalTranslation", "notes");
        string? Optional(string name) => segment.TryGetProperty(name, out _) ? Nfd(SoundSystemIntentParsing.Text(segment, name), name) : null;
        return new AuthorTextSegment(Nfd(SoundSystemIntentParsing.Text(segment, "text"), "text"),
            Optional("freeTranslation"), Optional("literalTranslation"),
            segment.TryGetProperty("notes", out _) ? SoundSystemIntentParsing.Array(segment, "notes").Select(note =>
                note.ValueKind == JsonValueKind.String ? Nfd(note.GetString()!, "note") : throw Refuse("each note is a string.")).ToArray() : null);
    }

    private static string Token(JsonElement element)
    {
        var token = element.ValueKind == JsonValueKind.String ? element.GetString()! : string.Empty;
        if (token.Length == 0 || token.Any(char.IsWhiteSpace)) throw Refuse("every token is one nonblank string without spaces.");
        return Nfd(token, "token");
    }

    private static string Nfd(string text, string field)
    {
        if (string.IsNullOrWhiteSpace(text) || text != text.Trim() || !text.IsNormalized(NormalizationForm.FormD))
            throw Refuse($"'{field}' must be a nonblank NFD string without edge spaces.");
        return text;
    }

    private static ContractParseException Refuse(string message) => new($"'{ConstructName}': {message}");
}
