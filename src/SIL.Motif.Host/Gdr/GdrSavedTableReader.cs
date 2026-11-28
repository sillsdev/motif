using SIL.LCModel;
using SIL.Motif.Contract.Gdr;

namespace SIL.Motif.Host.Gdr;

/// <summary>One saved table as the project stores it, with the query block it belongs to.</summary>
/// <param name="Gdr">The <c>GDR-n</c> of the section.</param>
/// <param name="Query">The block's definition.</param>
/// <param name="Caption">What the caption line says.</param>
/// <param name="Columns">The header row's cells.</param>
/// <param name="Rows">The body rows' cells.</param>
public sealed record GdrSavedTableSource(string Gdr, GuideQueryDefinition Query, GdrSavedTableCaption Caption,
    IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows);

/// <summary>Reads the saved tables of the Guide's query blocks from a project copy (ADR 0005). It reads only; it runs no query.</summary>
public static class GdrSavedTableReader
{
    /// <summary>Every saved table of every record, in document order.</summary>
    public static IReadOnlyList<GdrSavedTableSource> Read(LcmCache cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        var found = new List<GdrSavedTableSource>();
        foreach (var record in GdrReader.ReadStructure(cache))
        {
            var texts = record.Paragraphs.Select(paragraph => paragraph.Contents.Text ?? "").ToArray();
            var styles = record.Paragraphs.Select(paragraph => paragraph.StyleName).ToArray();
            var definitions = GuideQueryBlocks.Parse(string.Join("\n", texts)).Queries;
            foreach (var site in GdrSavedTables.Sites(texts, styles).Where(site => site.HasTable))
            {
                var query = definitions.FirstOrDefault(definition => definition.Id == site.QueryId);
                if (query is null) continue;
                var cells = Enumerable.Range(site.TableFirst + 1, site.TableCount - 1).Select(i => (IReadOnlyList<string>)texts[i].Split('\t')).ToArray();
                found.Add(new GdrSavedTableSource(record.Label, query, site.Caption!, cells[0], cells.Skip(1).ToArray()));
            }
        }
        return found;
    }
}
