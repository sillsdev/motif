using System.Xml;
using System.Xml.Linq;

namespace SIL.Motif.Host.Texts;

/// <summary>
/// Serializes an <see cref="InterlinearTextProjection"/> to FLExText XML — validated in tests against
/// <c>FlexInterlinear.xsd</c> — and parses it back, for the writer-agreement test against
/// <see cref="FlexTextJsonWriter"/>. Written only behind <c>--flextext</c>; the JSON is the default.
/// </summary>
public static class FlexTextXmlWriter
{
    /// <summary>
    /// Builds the FLExText document: a single <c>&lt;document&gt;</c> wrapping the one
    /// <c>&lt;interlinear-text&gt;</c> this projection describes, which is all the schema requires beyond
    /// what <paramref name="projection"/> already carries.
    /// </summary>
    public static XDocument Write(InterlinearTextProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);

        var interlinearText = new XElement(
            "interlinear-text",
            new XAttribute("guid", projection.Guid.ToString()),
            projection.Title.Select(WriteItem),
            new XElement("paragraphs", projection.Paragraphs.Select(WriteParagraph)));

        return new XDocument(new XElement("document", interlinearText));
    }

    /// <summary>Writes the document to <paramref name="stream"/> as indented UTF-8 XML.</summary>
    public static void WriteTo(InterlinearTextProjection projection, Stream stream)
    {
        using var writer = XmlWriter.Create(stream, new XmlWriterSettings { Indent = true });
        Write(projection).Save(writer);
    }

    /// <summary>Parses a document written by <see cref="Write"/> back into a projection.</summary>
    public static InterlinearTextProjection Read(XDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var textElement = document.Root?.Element("interlinear-text")
            ?? throw new FormatException("Expected a document/interlinear-text element.");

        var guid = Guid.Parse(textElement.Attribute("guid")!.Value);
        var title = textElement.Elements("item").Select(ReadItem).ToList();
        var paragraphs = textElement.Element("paragraphs")?.Elements("paragraph")
            .Select(ReadParagraph).ToList() ?? [];

        return new InterlinearTextProjection(guid, title, paragraphs);
    }

    private static XElement WriteParagraph(InterlinearParagraph paragraph) => new(
        "paragraph",
        new XAttribute("guid", paragraph.Guid.ToString()),
        new XElement("phrases", paragraph.Phrases.Select(WritePhrase)));

    private static InterlinearParagraph ReadParagraph(XElement element)
    {
        var guid = Guid.Parse(element.Attribute("guid")!.Value);
        var phrases = element.Element("phrases")?.Elements("phrase").Select(ReadPhrase).ToList() ?? [];
        return new InterlinearParagraph(guid, phrases);
    }

    private static XElement WritePhrase(InterlinearPhrase phrase)
    {
        // The words slot precedes the trailing item slot the schema allows after it; see the reader below.
        var element = new XElement(
            "phrase",
            new XAttribute("guid", phrase.Guid.ToString()),
            new XElement("words", phrase.Words.Select(WriteWord)));
        element.Add(phrase.Items.Select(WriteItem));
        return element;
    }

    private static InterlinearPhrase ReadPhrase(XElement element)
    {
        var guid = Guid.Parse(element.Attribute("guid")!.Value);
        var items = element.Elements("item").Select(ReadItem).ToList();
        var words = element.Element("words")?.Elements("word").Select(ReadWord).ToList() ?? [];
        return new InterlinearPhrase(guid, items, words);
    }

    private static XElement WriteWord(InterlinearWord word)
    {
        var element = new XElement("word");
        if (word.WordformGuid is { } guid) element.Add(new XAttribute("guid", guid.ToString()));
        element.Add(word.Items.Select(WriteItem));

        if (word.Morphemes.Count > 0)
        {
            element.Add(new XElement(
                "morphemes",
                new XAttribute("analysisStatus", ToSchemaAnalysisStatus(word.AnalysisStatus)),
                word.Morphemes.Select(WriteMorpheme)));
        }

        return element;
    }

    private static InterlinearWord ReadWord(XElement element)
    {
        var wordformGuid = element.Attribute("guid") is { } guidAttribute
            ? Guid.Parse(guidAttribute.Value)
            : (Guid?)null;
        var items = element.Elements("item").Select(ReadItem).ToList();

        var morphemesElement = element.Element("morphemes");
        if (morphemesElement is null)
            return new InterlinearWord(wordformGuid, InterlinearAnalysisStatus.Unanalysed, items, []);

        var status = FromSchemaAnalysisStatus(morphemesElement.Attribute("analysisStatus")!.Value);
        var morphemes = morphemesElement.Elements("morph").Select(ReadMorpheme).ToList();
        return new InterlinearWord(wordformGuid, status, items, morphemes);
    }

    private static XElement WriteMorpheme(InterlinearMorpheme morpheme)
    {
        var element = new XElement("morph");
        if (morpheme.MorphGuid is { } guid) element.Add(new XAttribute("guid", guid.ToString()));
        element.Add(morpheme.Items.Select(WriteItem));
        return element;
    }

    private static InterlinearMorpheme ReadMorpheme(XElement element)
    {
        var morphGuid = element.Attribute("guid") is { } guidAttribute
            ? Guid.Parse(guidAttribute.Value)
            : (Guid?)null;
        var items = element.Elements("item").Select(ReadItem).ToList();
        return new InterlinearMorpheme(morphGuid, items);
    }

    private static XElement WriteItem(FlexItem item) => new(
        "item", new XAttribute("type", item.Type), new XAttribute("lang", item.Lang), item.Value);

    private static FlexItem ReadItem(XElement element) =>
        new(element.Attribute("type")!.Value, element.Attribute("lang")!.Value, element.Value);

    // analysisStatusTypes is a closed enum: unapproved maps to its nearest sanctioned value, "guess".
    private static string ToSchemaAnalysisStatus(string status) => status switch
    {
        InterlinearAnalysisStatus.Approved => "humanApproved",
        InterlinearAnalysisStatus.Unapproved => "guess",
        _ => throw new ArgumentOutOfRangeException(
            nameof(status), status, "An analysed word carries approved or unapproved status only."),
    };

    private static string FromSchemaAnalysisStatus(string schemaStatus) =>
        schemaStatus == "humanApproved" ? InterlinearAnalysisStatus.Approved : InterlinearAnalysisStatus.Unapproved;
}
