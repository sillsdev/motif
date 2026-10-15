namespace SIL.Motif.App.Controls.WordPresentation;

/// <summary>The column heading words, shared by the header row and the captions of stacked rows.</summary>
internal static class WordListHeadings
{
    internal const string DefaultMeaning = "Meaning";

    /// <summary>Returns a column's heading, or an empty string for a column that has none.</summary>
    internal static string TextOf(WordListColumn column, string meaningHeader) => column switch
    {
        WordListColumn.Word => "Word",
        WordListColumn.Opinion => "Opinion",
        WordListColumn.FieldWorks => "FieldWorks",
        WordListColumn.PanGloss => "PanGloss",
        WordListColumn.Meaning => meaningHeader,
        WordListColumn.Places => "Places",
        WordListColumn.Time => "Time",
        WordListColumn.Next => "Next",
        _ => string.Empty,
    };
}
