using System.Collections.Immutable;

namespace SIL.Motif.Host.PanGloss;

/// <summary>Versions of the PanGloss wire formats pinned by Motif.</summary>
public static class PanGlossInterfaceVersions
{
    /// <summary>The PanGloss source tag that defines the formats Motif reads.</summary>
    public const string SourceTag = "v0.35.0";
    /// <summary>The version of the object returned by <c>pangloss --describe</c>.</summary>
    public const int DescribeSchemaVersion = 1;
    /// <summary>The version of reports returned by <c>pangloss grammar-health</c>.</summary>
    public const int GrammarHealthSchemaVersion = 5;
    /// <summary>The version of optional FST capability reports.</summary>
    public const int FstHealthSchemaVersion = 10;
    /// <summary>The identifier of detailed parse-trace documents.</summary>
    public const string TraceDetailsSchemaId = "pangloss.trace-details.v5";
    /// <summary>The identifier advertised for bounded rejection diagnostics.</summary>
    public const string RejectionTraceSchemaId = "pangloss.diagnostic.v1";
    /// <summary>The identifier a PanGloss declares at <c>interfaces.whyNot</c> and writes in every why-not report.</summary>
    public const string WhyNotSchemaId = "pangloss.whynot.v1";
    /// <summary>The id promise a why-not capability must make: every id in a report is a <c>grammar.xml</c> id.</summary>
    public const string WhyNotIds = "grammar-xml";
    /// <summary>What a PanGloss must declare to generate forms with a rule trace, written for the refusal that names it.</summary>
    public const string ServeGenerateNeeded =
        "a PanGloss release after 0.27.0 whose --describe declares interfaces.serveOps.generate (sillsdev/PanGloss#434)";
    /// <summary>What a PanGloss must declare to match phonological patterns over a word list, written for the refusal that names it.</summary>
    public const string ServeMatchNeeded =
        "a PanGloss release after 0.28.1 whose --describe declares interfaces.serveOps.match (sillsdev/PanGloss#436, release v0.29.0)";
    /// <summary>What a PanGloss must declare to match environments, feature bundles and alpha variables, written for the refusal that names it.</summary>
    public const string ServeMatchConstructsNeeded =
        "a PanGloss release whose --describe lists environment, featureBundle and alphaVariable under interfaces.serveOps.match constructs.supported (release v0.30.0)";
    /// <summary>What a PanGloss must declare to match under a chosen segment test, written for the refusal that names it.</summary>
    public const string ServeMatchSegmentTestNeeded =
        "a PanGloss release whose --describe lists engine and identity under interfaces.serveOps.match segmentTests (release v0.31.0)";
    /// <summary>What a PanGloss must declare to list a grammar's natural classes and features over <c>serve</c>, written for the refusal that names it.</summary>
    public const string ServeInventoryNeeded =
        "a PanGloss release whose --describe declares interfaces.serveOps.inventory (release v0.30.0)";
    /// <summary>What a PanGloss must declare to say which phonological rules may change a morpheme over <c>serve</c>, written for the refusal that names it.</summary>
    public const string ServeEffectsNeeded =
        "a PanGloss release whose --describe declares interfaces.serveOps.effects (release v0.31.0)";
    /// <summary>The capture modes the pinned release advertises for bounded rejection diagnostics.</summary>
    public static ImmutableArray<string> RejectionTraceModes { get; } = ["lineage"];
    /// <summary>The full SQLite cache version written by <c>batch --stats</c>, without compact storage.</summary>
    public const int StatsCacheVersion = 13;
    /// <summary>The meaning of the statistics cache counters.</summary>
    public const int StatsCounterSemanticsVersion = 8;
    /// <summary>The version of structured parser compile errors.</summary>
    public const int CompileErrorSchemaVersion = 1;
    /// <summary>The grammar facts schema version reported by <c>pangloss --describe</c>.</summary>
    public const int FactsSchemaVersion = 9;
    /// <summary>The grammar facts context version reported by <c>pangloss --describe</c>.</summary>
    public const int FactsContextVersion = 1;
    /// <summary>The stats manifest version reported by <c>pangloss --describe</c>.</summary>
    public const int StatsManifestVersion = 2;
    /// <summary>The first batch TSV version whose <c>--work-columns</c> writes only <c>work_steps</c>, so a headerless row ends with it.</summary>
    public const int BatchTsvWorkColumnsVersion = 5;
    /// <summary>The exact number of fields in a completed batch TSV row without optional columns.</summary>
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
