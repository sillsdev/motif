namespace SIL.Motif.App.ViewModels;

/// <summary>The document kind used to choose a compact, readable file glyph.</summary>
public enum HandoffFileKind
{
    /// <summary>A JSON document.</summary>
    Json,

    /// <summary>A newline-delimited JSON document.</summary>
    Jsonl,

    /// <summary>A Markdown document.</summary>
    Markdown,

    /// <summary>A Python script.</summary>
    Python,

    /// <summary>A plain-text document.</summary>
    Text,

    /// <summary>An XML document.</summary>
    Xml,

    /// <summary>A file whose extension is not one of the displayed kinds.</summary>
    Unknown,
}

/// <summary>One file a completed Handoff wrote, already verified to sit inside its own output directory.</summary>
public sealed record HandoffFileViewModel(string RelativePath, string FullPath)
{
    /// <summary>The display kind derived only from this file's relative path.</summary>
    public HandoffFileKind Kind => KindFromRelativePath(RelativePath);

    /// <summary>The upper-cased extension shown in the document glyph.</summary>
    public string KindLabel => Kind switch
    {
        HandoffFileKind.Json => "JSON",
        HandoffFileKind.Jsonl => "JSONL",
        HandoffFileKind.Markdown => "MD",
        HandoffFileKind.Python => "PY",
        HandoffFileKind.Text => "TXT",
        HandoffFileKind.Xml => "XML",
        _ => "FILE",
    };

    /// <summary>What a Handoff's files are for, by name, in the order a reader would open them.</summary>
    public static IReadOnlyList<(string Name, string Purpose)> KnownFiles { get; } =
    [
        ("handoff.md", "What this Handoff is and how to read it."),
        ("assessment.json", "Every word the parser was asked about: its readings, verdicts and timings."),
        ("texts.json", "The chosen texts, word by word, with the analyses the project stores."),
        ("grammar.json", "The grammar the parser used, as it read it."),
        ("parse_grammar_texts_assessment.py", "A reader the chat model can run over the three files."),
    ];

    /// <summary>One line on what this file holds, or empty for a file this list does not know.</summary>
    public string Purpose => KnownFiles.FirstOrDefault(known =>
        string.Equals(known.Name, Path.GetFileName(RelativePath), StringComparison.OrdinalIgnoreCase)).Purpose ?? string.Empty;

    public bool HasPurpose => Purpose.Length > 0;

    /// <summary>The accessible action name for starting a drag of this file.</summary>
    public string DragAccessibleName => $"Drag {RelativePath}";

    /// <summary>The accessible action name for copying this file's full path.</summary>
    public string CopyPathAccessibleName => $"Copy path for {RelativePath}";

    /// <summary>Maps a relative path's extension to the glyph kind without touching the filesystem.</summary>
    public static HandoffFileKind KindFromRelativePath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        return Path.GetExtension(relativePath).ToUpperInvariant() switch
        {
            ".JSON" => HandoffFileKind.Json,
            ".JSONL" => HandoffFileKind.Jsonl,
            ".MD" => HandoffFileKind.Markdown,
            ".PY" => HandoffFileKind.Python,
            ".TXT" => HandoffFileKind.Text,
            ".XML" => HandoffFileKind.Xml,
            _ => HandoffFileKind.Unknown,
        };
    }
}
