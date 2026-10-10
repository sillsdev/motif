using System.Collections.Immutable;

namespace SIL.Motif.Host.PanGloss;

/// <summary>Versions of the PanGloss wire formats read by Motif.</summary>
public static class PanGlossInterfaceVersions
{
    /// <summary>The PanGloss source tag that defines the formats Motif reads.</summary>
    public const string SourceTag = "v0.8.1";
    /// <summary>The version of the object returned by <c>pangloss --describe</c>.</summary>
    public const int DescribeSchemaVersion = 1;
    /// <summary>The version of reports returned by <c>pangloss grammar-health</c>.</summary>
    public const int GrammarHealthSchemaVersion = 4;
    /// <summary>The identifier of detailed parse-trace documents.</summary>
    public const string TraceDetailsSchemaId = "pangloss.trace-details.v3";
    /// <summary>The version of the SQLite cache written by <c>batch --stats</c>.</summary>
    public const int StatsCacheVersion = 8;
    /// <summary>The version of structured parser compile errors.</summary>
    public const int CompileErrorSchemaVersion = 1;
    /// <summary>The grammar facts schema version reported by <c>pangloss --describe</c>.</summary>
    public const int FactsSchemaVersion = 8;
    /// <summary>The grammar facts context version reported by <c>pangloss --describe</c>.</summary>
    public const int FactsContextVersion = 1;
    /// <summary>The stats manifest version reported by <c>pangloss --describe</c>.</summary>
    public const int StatsManifestVersion = 1;
    /// <summary>The exact number of fields in a completed batch TSV row.</summary>
    public const int BatchTsvCompletionColumnCount = 5;
    /// <summary>The zero-based batch TSV position of the word index.</summary>
    public const int BatchTsvIndexColumn = 0;
    /// <summary>The zero-based batch TSV position of the word text.</summary>
    public const int BatchTsvWordColumn = 1;
    /// <summary>The zero-based batch TSV position of elapsed milliseconds.</summary>
    public const int BatchTsvElapsedMsColumn = 2;
    /// <summary>The zero-based batch TSV position of the completion status.</summary>
    public const int BatchTsvStatusColumn = 3;
    /// <summary>The zero-based batch TSV position of the parse signature.</summary>
    public const int BatchTsvSignatureColumn = 4;

    /// <summary>The ordered field names in a completed batch TSV row.</summary>
    public static ImmutableArray<string> BatchTsvColumns { get; } =
        ["index", "word", "elapsed_ms", "status", "signature"];
}
