using System.Text;
using System.Text.Json;
using SIL.Motif.Contract.Parsing;

namespace SIL.Motif.Runner.Composers;

/// <summary>One authored paragraph: its segments, each a list of word tokens in reading order.</summary>
/// <param name="Segments">Each segment's tokens; a token is one exact NFD surface form with no spaces.</param>
public sealed record AuthorTextParagraph(IReadOnlyList<IReadOnlyList<string>> Segments);

/// <summary>Creates one Text from a title and paragraphs of segments of word tokens.</summary>
/// <param name="Title">The Text's name, in NFD.</param>
/// <param name="WritingSystem">
/// The writing system tag of the title, the paragraphs and every wordform form this intent creates or reuses.
/// </param>
/// <param name="Paragraphs">The paragraphs in order.</param>
public sealed record AuthorTextIntent(string Title, string WritingSystem, IReadOnlyList<AuthorTextParagraph> Paragraphs);

/// <summary>
/// Parses the JSON shape an agent authors for <see cref="AuthorTextComposer"/>:
/// <c>{ "title": "...", "ws": "...", "paragraphs": [ { "segments": [ ["token", ...], ... ] } ] }</c>.
/// Closed to any other property and to a repeated one.
/// </summary>
public static class AuthorTextIntentParser
{
    private const string ConstructName = "AuthorText";

    /// <summary>Reads the title, writing system and tokens, refusing a non-NFD or spaced token.</summary>
    public static AuthorTextIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, ConstructName, "title", "ws", "paragraphs");
        var title = SoundSystemIntentParsing.Text(authored, "title");
        var ws = SoundSystemIntentParsing.Text(authored, "ws");
        if (string.IsNullOrWhiteSpace(title) || title != title.Trim() || !title.IsNormalized(NormalizationForm.FormD))
            throw new ContractParseException($"'{ConstructName}': 'title' must be a nonblank NFD string without edge spaces.");
        if (string.IsNullOrWhiteSpace(ws))
            throw new ContractParseException($"'{ConstructName}': 'ws' must name a writing system.");
        var paragraphs = SoundSystemIntentParsing.Array(authored, "paragraphs").Select(paragraph =>
        {
            SoundSystemIntentParsing.Object(paragraph, ConstructName + " paragraph", "segments");
            var segments = SoundSystemIntentParsing.Array(paragraph, "segments").Select(segment =>
            {
                if (segment.ValueKind != JsonValueKind.Array)
                    throw new ContractParseException($"'{ConstructName}': each segment is an array of tokens.");
                var tokens = segment.EnumerateArray().Select(Token).ToArray();
                if (tokens.Length == 0)
                    throw new ContractParseException($"'{ConstructName}': each segment holds at least one token.");
                return (IReadOnlyList<string>)tokens;
            }).ToArray();
            if (segments.Length == 0)
                throw new ContractParseException($"'{ConstructName}': each paragraph holds at least one segment.");
            return new AuthorTextParagraph(segments);
        }).ToArray();
        if (paragraphs.Length == 0)
            throw new ContractParseException($"'{ConstructName}': 'paragraphs' needs at least one paragraph.");
        return new AuthorTextIntent(title, ws, paragraphs);
    }

    private static string Token(JsonElement element)
    {
        var token = element.ValueKind == JsonValueKind.String ? element.GetString()! : string.Empty;
        if (token.Length == 0 || token.Any(char.IsWhiteSpace))
            throw new ContractParseException($"'{ConstructName}': every token is one nonblank string without spaces.");
        if (!token.IsNormalized(NormalizationForm.FormD))
            throw new ContractParseException($"'{ConstructName}': token '{token}' must be in NFD.");
        return token;
    }
}
