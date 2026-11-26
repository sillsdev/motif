using CommunityToolkit.Mvvm.ComponentModel;
using SIL.Motif.Contract.Gdr;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// One <c>motif-show</c> of a v2 corpus query: a headline that says "at least N" when the parse behind the counts is incomplete, and
/// the table as text lines (a header, then one line per row). Computed from a run and dropped with it; never stored.
/// </summary>
public sealed partial class GuideEvidenceDisplayViewModel(GuideShowKind kind, int? limit, IReadOnlyList<string>? columns, GuideSort? sort) : ObservableObject
{
    /// <summary>How the directive shows the query.</summary>
    public GuideShowKind Kind { get; } = kind;

    /// <summary>The directive's row limit, or null.</summary>
    public int? Limit { get; } = limit;

    /// <summary>The directive's columns, or null for the query's.</summary>
    public IReadOnlyList<string>? Columns { get; } = columns;

    /// <summary>The query definition's sort, or null.</summary>
    public GuideSort? Sort { get; } = sort;

    /// <summary>The count sentence, or the refusal or cold-index message when there is no table.</summary>
    [ObservableProperty]
    private string _headline = "";

    /// <summary>The header line then the rows; empty for a counts display and when there is no table.</summary>
    public IReadOnlyList<string> Lines { get; private set; } = [];

    /// <summary><see cref="Lines"/> as one text block: one selectable control, not a collection.</summary>
    [ObservableProperty]
    private string _tableText = "";

    /// <summary>Whether the lines show: every kind but counts.</summary>
    public bool ShowsLines => Kind != GuideShowKind.Counts;

    /// <summary>Replaces what the display shows with a ready table.</summary>
    public void Show(GuideEvidenceTable table)
    {
        Headline = table.Headline;
        var lines = new List<string>();
        if (ShowsLines)
        {
            if (Kind == GuideShowKind.List)
            {
                foreach (var row in table.Rows) lines.Add(row.Count > 0 ? row[0] : "");
            }
            else
            {
                lines.Add(string.Join(" | ", table.Columns));
                foreach (var row in table.Rows) lines.Add(string.Join(" | ", row));
            }
            if (table.Truncated > 0) lines.Add($"... {table.Truncated:N0} more rows not shown");
        }
        SetLines(lines);
    }

    /// <summary>Shows a message (a refusal or a cold index) in place of a table.</summary>
    public void ShowMessage(string message)
    {
        Headline = message;
        SetLines([]);
    }

    private void SetLines(IReadOnlyList<string> lines)
    {
        Lines = lines;
        TableText = string.Join("\n", lines);
    }

    /// <summary>Clears the display.</summary>
    public void Clear() => ShowMessage("");
}
