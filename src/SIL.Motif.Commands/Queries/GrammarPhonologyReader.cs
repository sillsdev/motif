using System.Xml;
using System.Xml.Linq;

namespace SIL.Motif.Commands.Queries;

/// <summary>
/// Reads the natural classes and phonological features straight from a <c>.fwdata</c> file with one forward pass of the XML,
/// instead of copying the project and loading all of LibLCM, which takes minutes on a project of a few hundred thousand objects.
/// </summary>
internal static class GrammarPhonologyReader
{
    /// <summary>The classes and features of the file, unsorted, or null when the file is not the XML of a FieldWorks project.</summary>
    internal static (List<PhonValue> Classes, List<PhonValue> Features)? TryRead(string fwDataPath)
    {
        try
        {
            using var stream = new FileStream(fwDataPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16, FileOptions.SequentialScan);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, IgnoreWhitespace = true });
            return Read(reader);
        }
        catch (Exception exception) when (exception is XmlException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static (List<PhonValue>, List<PhonValue>)? Read(XmlReader reader)
    {
        if (!reader.ReadToFollowing("languageproject")) return null;
        var classes = new List<XElement>();
        var features = new List<XElement>();
        XElement? project = null;
        reader.Read();
        while (!reader.EOF && reader.NodeType != XmlNodeType.EndElement)
        {
            if (reader.NodeType != XmlNodeType.Element || reader.Name != "rt")
            {
                reader.Read();
                continue;
            }
            var kind = reader.GetAttribute("class") ?? "";
            if (kind == "LangProject") project = (XElement)XNode.ReadFrom(reader);
            else if (kind.StartsWith("PhNC", StringComparison.Ordinal)) classes.Add((XElement)XNode.ReadFrom(reader));
            else if (kind is "FsClosedFeature" or "FsComplexFeature" or "FsOpenFeature") features.Add((XElement)XNode.ReadFrom(reader));
            else reader.Skip();
        }
        if (project is null) return null;
        var analysis = Values(project, "CurrentAnalysisWss") is { Count: > 0 } current ? current : Values(project, "AnalysisWss");
        var phonology = project.Element("PhFeatureSystem")?.Element("objsur")?.Attribute("guid")?.Value;
        return (classes.Select(entry => PhonValues.ClassValue(Best(entry, "Name", analysis), Best(entry, "Abbreviation", analysis),
                    (string?)entry.Attribute("class") == "PhNCSegments" ? entry.Element("Segments")?.Elements("objsur").Count() ?? 0 : null)).ToList(),
            features.Where(entry => phonology is null || (string?)entry.Attribute("ownerguid") == phonology)
                .Select(entry => new PhonValue(Best(entry, "Name", analysis),
                    Best(entry, "Abbreviation", analysis) is { Length: > 0 } abbreviation ? abbreviation : null)).ToList());
    }

    private static List<string> Values(XElement project, string field) =>
        (project.Element(field)?.Elements("Uni").Select(unicode => unicode.Value) ?? []).SelectMany(text => text.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToList();

    // The best analysis alternative: the first analysis writing system with text, else whatever text there is.
    private static string Best(XElement entry, string field, List<string> analysis)
    {
        var alternatives = entry.Element(field)?.Elements("AUni").ToList() ?? [];
        foreach (var ws in analysis)
            if (alternatives.FirstOrDefault(alternative => (string?)alternative.Attribute("ws") == ws)?.Value is { Length: > 0 } text) return text;
        return alternatives.Select(alternative => alternative.Value).FirstOrDefault(text => text.Length > 0) ?? "";
    }
}
