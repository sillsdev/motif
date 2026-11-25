using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Query.Corpus;

/// <summary>Asks for the affix pairs of a plain corpus, with no project (<c>motif query --corpus &lt;words&gt; pairs</c>).</summary>
/// <param name="CorpusPath">A TSV word list (form, count, optional parse state), a weights .json file or a folder of .txt files; several joined with commas or newlines are one corpus.</param>
/// <param name="Differ"><c>suffix</c> or <c>prefix</c>.</param>
/// <param name="X">A regular expression the whole differing part must match.</param>
/// <param name="Having">Literal Xs a base must all take.</param>
/// <param name="SegmentsPath">An optional segment file of multi-character graphemes.</param>
/// <param name="FoldCase">Lower-case every form before counting.</param>
/// <param name="SplitHyphens">Split running text at hyphens between letters.</param>
/// <param name="MaxXLength">The longest X in segments; default 4.</param>
/// <param name="MaxResults">The most X rows and base rows; default 100.</param>
/// <param name="Examples">The most example pairs per X; default 5.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PairsQueryRequest(string CorpusPath, string Differ, string? X = null, IReadOnlyList<string>? Having = null,
    string? SegmentsPath = null, bool FoldCase = false, bool SplitHyphens = false, int? MaxXLength = null, int? MaxResults = null, int? Examples = null);

/// <summary>One source file or folder of a corpus.</summary>
/// <param name="Path">The path as given.</param>
/// <param name="Kind"><c>weights.json</c> (a JSON object of form to count), <c>tsv</c> (a word list) or <c>texts</c> (a folder of .txt files).</param>
/// <param name="Types">Its word types on their own, after normalisation.</param>
/// <param name="Tokens">Its tokens on their own.</param>
public sealed record CorpusSourceInfo(string Path, string Kind, int Types, long Tokens);

/// <summary>The corpus a corpus query ran over.</summary>
/// <param name="Source">The corpus path; several sources are joined with commas.</param>
/// <param name="Types">Word types of the whole corpus, equal forms of different sources counted once.</param>
/// <param name="Tokens">Tokens of the whole corpus, the sum over the sources.</param>
/// <param name="Segmentation">How forms were segmented.</param>
/// <param name="FoldCase">Whether forms were lower-cased.</param>
/// <param name="Sources">Each source with its own type and token counts when there is more than one; null for a single source.</param>
public sealed record CorpusSummary(string Source, int Types, long Tokens, string Segmentation, bool FoldCase,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<CorpusSourceInfo>? Sources = null);

/// <summary>The pairs of a corpus, versioned <see cref="SchemaName"/>.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PairsQueryResponse(string Schema, CorpusSummary Corpus, string Differ, string? XPattern, IReadOnlyList<string> Having,
    int MaxXLength, int MaxResults, int PairCount, IReadOnlyList<PairsX> Xs, IReadOnlyList<PairsBase> Bases, PairsTruncation Truncation)
{
    /// <summary>The schema name of the JSON form.</summary>
    public const string SchemaName = "motif.query.pairs.v1";

    /// <summary>Wraps an analysis result.</summary>
    public static PairsQueryResponse From(CorpusSummary corpus, PairsResult result) =>
        new(SchemaName, corpus, result.Differ == PairsDiffer.Suffix ? "suffix" : "prefix", result.XPattern, result.Having, result.MaxXLength,
            result.MaxResults, result.PairCount, result.Xs, result.Bases, result.Truncation);
}
