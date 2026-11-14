using SIL.TextEditing.Markdown;

namespace SIL.Motif.Host.RichText;

/// <summary>
/// The style names Motif gives FieldWorks Markdown (ADR 0089 as amended). The library names no table styles,
/// so without these a pipe table is <c>fwmd.syntax.unsupported-markup</c>; with them its header row is read as
/// <see cref="TableHeader"/>, its body rows as <see cref="TableRow"/>, and the writer writes such rows as a pipe table.
/// Every <see cref="MarkdownOptions"/> Motif builds takes its names from <see cref="Names"/>.
/// </summary>
public static class MotifMarkdownStyles
{
    /// <summary>The paragraph style of a pipe table's header row.</summary>
    public const string TableHeader = "Table Header";

    /// <summary>The paragraph style of a pipe table's body rows.</summary>
    public const string TableRow = "Table Row";

    /// <summary>The library's default names, with Motif's table styles.</summary>
    public static MarkdownStyleNames Names() => new() { TableHeader = TableHeader, TableRow = TableRow };
}
