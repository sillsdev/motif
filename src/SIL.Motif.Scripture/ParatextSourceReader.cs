using System.Xml.Linq;
using SIL.Machine.Corpora;
using SIL.Scripture;

namespace SIL.Motif.Scripture;

/// <summary>Opens a Paratext project folder through Machine's <see cref="ParatextTextCorpus"/> and lists its chapters.</summary>
public static class ParatextSourceReader
{
    /// <summary>
    /// Reads the folder's Settings.xml, versification and the books of the canon, deuterocanon included;
    /// extra material such as front matter is left out. Verse text is read only to learn which chapters
    /// have it; nothing is kept. Throws <see cref="DirectoryNotFoundException"/> or
    /// <see cref="InvalidDataException"/> with a readable message when the folder is not a Paratext project.
    /// </summary>
    public static ParatextSource Open(string projectFolder)
    {
        if (!Directory.Exists(projectFolder))
            throw new DirectoryNotFoundException($"The Paratext project folder '{projectFolder}' does not exist.");

        var settingsPath = Path.Combine(projectFolder, "Settings.xml");
        if (!File.Exists(settingsPath))
            throw new InvalidDataException($"'{projectFolder}' has no Settings.xml, so it is not a Paratext project.");

        var settings = ReadSettings(settingsPath);
        ParatextTextCorpus corpus;
        try
        {
            corpus = new ParatextTextCorpus(projectFolder);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new InvalidDataException($"Machine could not open the Paratext project '{projectFolder}': {ex.Message}", ex);
        }

        var byBook = new SortedDictionary<int, SortedSet<int>>();
        foreach (var text in corpus.Texts)
        {
            var book = Canon.BookIdToNumber(text.Id);
            if (!Canon.IsCanonical(book)) continue;
            var chapters = new SortedSet<int>();
            foreach (var row in text.GetRows())
            {
                var chapter = ((ScriptureRef)row.Ref).ChapterNum;
                if (chapter > 0 && !row.IsEmpty) chapters.Add(chapter);
            }

            if (chapters.Count > 0) byBook[book] = chapters;
        }

        var result = byBook.ToDictionary(
            pair => Canon.BookNumberToId(pair.Key),
            pair => (IReadOnlyList<int>)pair.Value.ToList());
        return new ParatextSource(settings.Name, settings.Guid, corpus.Versification, result);
    }

    private static (string Name, string Guid) ReadSettings(string path)
    {
        try
        {
            var root = XDocument.Load(path).Root;
            return (root?.Element("Name")?.Value.Trim() ?? string.Empty, root?.Element("Guid")?.Value.Trim() ?? string.Empty);
        }
        catch (System.Xml.XmlException ex)
        {
            throw new InvalidDataException($"'{path}' is not a valid Paratext Settings.xml: {ex.Message}", ex);
        }
    }
}
