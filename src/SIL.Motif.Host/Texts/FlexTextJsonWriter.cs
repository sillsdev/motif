using System.Text.Json;
using System.Text.Json.Nodes;

namespace SIL.Motif.Host.Texts;

/// <summary>
/// Serializes an <see cref="InterlinearTextProjection"/> to Motif's JSON mirror of FLExText — the format
/// design decision 3 in the AI handoff design note puts in the Handoff by default — and parses it back,
/// for the writer-agreement test against <see cref="FlexTextXmlWriter"/>.
/// </summary>
/// <remarks>
/// Element names are FLExText's own, verbatim, as JSON keys: <c>document</c>, <c>interlinear-text</c>,
/// <c>paragraphs</c>/<c>paragraph</c>, <c>phrases</c>/<c>phrase</c>, <c>words</c>/<c>word</c>,
/// <c>morphemes</c>/<c>morph</c>, <c>item</c>. A repeated element becomes a JSON array under the plural
/// key, and every <c>item</c> collapses to <c>{ "type", "lang", "value" }</c> — the two writers share no
/// other vocabulary than the projection itself, so neither can drift from the other unnoticed.
/// </remarks>
public static class FlexTextJsonWriter
{
    /// <summary>Builds the JSON tree for one Text, nested exactly as <see cref="FlexTextXmlWriter.Write"/> nests it.</summary>
    public static JsonObject Write(InterlinearTextProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);

        var interlinearText = new JsonObject
        {
            ["guid"] = projection.Guid.ToString(),
            ["item"] = WriteItems(projection.Title),
            ["paragraphs"] = new JsonObject
            {
                ["paragraph"] = new JsonArray(projection.Paragraphs.Select(WriteParagraph).ToArray()),
            },
        };

        return new JsonObject { ["document"] = new JsonObject { ["interlinear-text"] = new JsonArray(interlinearText) } };
    }

    /// <summary>Serializes <paramref name="projection"/> to an indented JSON string.</summary>
    public static string Serialize(InterlinearTextProjection projection) =>
        Write(projection).ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    /// <summary>Parses JSON written by <see cref="Write"/> back into a projection.</summary>
    public static InterlinearTextProjection Read(JsonNode root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var interlinearText = root["document"]?["interlinear-text"]?.AsArray().FirstOrDefault()?.AsObject()
            ?? throw new FormatException("Expected document.interlinear-text[0].");

        var guid = Guid.Parse(interlinearText["guid"]!.GetValue<string>());
        var title = ReadItems(interlinearText["item"]);
        var paragraphs = interlinearText["paragraphs"]?["paragraph"]?.AsArray()
            .Select(node => ReadParagraph(node!.AsObject())).ToList() ?? [];

        return new InterlinearTextProjection(guid, title, paragraphs);
    }

    private static JsonNode WriteParagraph(InterlinearParagraph paragraph) => new JsonObject
    {
        ["guid"] = paragraph.Guid.ToString(),
        ["phrases"] = new JsonObject
        {
            ["phrase"] = new JsonArray(paragraph.Phrases.Select(WritePhrase).ToArray()),
        },
    };

    private static InterlinearParagraph ReadParagraph(JsonObject o)
    {
        var guid = Guid.Parse(o["guid"]!.GetValue<string>());
        var phrases = o["phrases"]?["phrase"]?.AsArray()
            .Select(node => ReadPhrase(node!.AsObject())).ToList() ?? [];
        return new InterlinearParagraph(guid, phrases);
    }

    private static JsonNode WritePhrase(InterlinearPhrase phrase) => new JsonObject
    {
        ["guid"] = phrase.Guid.ToString(),
        ["words"] = new JsonObject { ["word"] = new JsonArray(phrase.Words.Select(WriteWord).ToArray()) },
        ["item"] = WriteItems(phrase.Items),
    };

    private static InterlinearPhrase ReadPhrase(JsonObject o)
    {
        var guid = Guid.Parse(o["guid"]!.GetValue<string>());
        var items = ReadItems(o["item"]);
        var words = o["words"]?["word"]?.AsArray().Select(node => ReadWord(node!.AsObject())).ToList() ?? [];
        return new InterlinearPhrase(guid, items, words);
    }

    private static JsonNode WriteWord(InterlinearWord word)
    {
        var element = new JsonObject { ["item"] = WriteItems(word.Items) };
        if (word.WordformGuid is { } guid) element["guid"] = guid.ToString();

        if (word.Morphemes.Count > 0)
        {
            element["morphemes"] = new JsonObject
            {
                ["analysisStatus"] = word.AnalysisStatus,
                ["morph"] = new JsonArray(word.Morphemes.Select(WriteMorpheme).ToArray()),
            };
        }

        return element;
    }

    private static InterlinearWord ReadWord(JsonObject o)
    {
        var wordformGuid = o["guid"] is JsonValue guidValue ? Guid.Parse(guidValue.GetValue<string>()) : (Guid?)null;
        var items = ReadItems(o["item"]);

        var morphemes = o["morphemes"];
        if (morphemes is null)
            return new InterlinearWord(wordformGuid, InterlinearAnalysisStatus.Unanalysed, items, []);

        var morphemesObject = morphemes.AsObject();
        var status = morphemesObject["analysisStatus"]!.GetValue<string>();
        var morphList = morphemesObject["morph"]?.AsArray()
            .Select(node => ReadMorpheme(node!.AsObject())).ToList() ?? [];
        return new InterlinearWord(wordformGuid, status, items, morphList);
    }

    private static JsonNode WriteMorpheme(InterlinearMorpheme morpheme)
    {
        var element = new JsonObject { ["item"] = WriteItems(morpheme.Items) };
        if (morpheme.MorphGuid is { } guid) element["guid"] = guid.ToString();
        return element;
    }

    private static InterlinearMorpheme ReadMorpheme(JsonObject o)
    {
        var morphGuid = o["guid"] is JsonValue guidValue ? Guid.Parse(guidValue.GetValue<string>()) : (Guid?)null;
        return new InterlinearMorpheme(morphGuid, ReadItems(o["item"]));
    }

    private static JsonArray WriteItems(IReadOnlyList<FlexItem> items) => new(items.Select(WriteItem).ToArray());

    private static JsonNode WriteItem(FlexItem item) =>
        new JsonObject { ["type"] = item.Type, ["lang"] = item.Lang, ["value"] = item.Value };

    private static List<FlexItem> ReadItems(JsonNode? node) =>
        node?.AsArray().Select(n => ReadItem(n!.AsObject())).ToList() ?? [];

    private static FlexItem ReadItem(JsonObject o) =>
        new(o["type"]!.GetValue<string>(), o["lang"]!.GetValue<string>(), o["value"]!.GetValue<string>());
}
