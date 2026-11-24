using System.Text.Json.Serialization;
using SIL.Motif.Contract.Query.Phon;

namespace SIL.Motif.Contract.Query.Corpus;

/// <summary>Asks a plain corpus which forms satisfy a query expression, with no project (<c>motif query --corpus &lt;words&gt; forms</c>).</summary>
/// <param name="CorpusPath">A TSV word list or a folder of .txt files.</param>
/// <param name="Where">The expression text, <c>phon(C V C V)</c> or any v2 text expression; null with <paramref name="QueryJson"/>.</param>
/// <param name="QueryJson">A <c>motif.query.v2</c> document over the <c>forms</c> source; its <c>where</c> and <c>limit</c> apply.</param>
/// <param name="GrammarPath">The grammar PanGloss loads to segment words and name natural classes; needed by <c>phon</c> and <c>env</c>.</param>
/// <param name="SegmentsPath">An optional segment file of multi-character graphemes for the surface-only predicates.</param>
/// <param name="FoldCase">Lower-case every form before counting.</param>
/// <param name="SplitHyphens">Split running text at hyphens between letters.</param>
/// <param name="Limit">The most rows returned; default 100.</param>
/// <param name="SegmentTest"><c>engine</c> (the default) or <c>identity</c>, how <c>phon</c> and <c>env</c> test a segment; overrides the document's <c>segmentTest</c>.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CorpusFormsRequest(string CorpusPath, string? Where = null, string? QueryJson = null, string? GrammarPath = null,
    string? SegmentsPath = null, bool FoldCase = false, bool SplitHyphens = false, int? Limit = null, string? SegmentTest = null);

/// <summary>One word of a corpus that satisfied the query.</summary>
/// <param name="Form">The word.</param>
/// <param name="Tokens">How often it occurs.</param>
/// <param name="ParseState">Its parse state when the list carried one.</param>
/// <param name="Matches">Where each <c>phon</c> or <c>env</c> predicate matched it; null when the query has none.</param>
public sealed record CorpusFormRow(string Form, long Tokens, string? ParseState = null, IReadOnlyList<QueryMatch>? Matches = null);

/// <summary>The words of a corpus that satisfy a query, versioned <see cref="SchemaName"/>.</summary>
/// <param name="Schema">The schema name.</param>
/// <param name="Corpus">The corpus and how its forms were segmented; <c>grammar</c> when PanGloss segmented them.</param>
/// <param name="Where">The query as canonical text.</param>
/// <param name="Matched">Every word that satisfied it.</param>
/// <param name="Rows">At most <c>Limit</c> of them, by token count then form.</param>
/// <param name="Phon">What the pattern predicates could not decide, counted; null when the query has none.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CorpusFormsResponse(string Schema, CorpusSummary Corpus, string? Where, int Matched,
    IReadOnlyList<CorpusFormRow> Rows, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PhonReport? Phon)
{
    /// <summary>The schema name of the JSON form.</summary>
    public const string SchemaName = "motif.query.corpus-forms.v1";
}

/// <summary>Evaluates a query expression over the word types of a corpus.</summary>
public static class CorpusFormsQuery
{
    private static readonly QuerySpec Spec = new(
        column => column is "form" or "tokens" or "parse.state" || QuerySources.IsTextColumn(column),
        _ => false, column => column is "tokens" or "syllables");

    /// <summary>Runs the expression; the matches and the undecided words come back from <paramref name="phon"/>.</summary>
    /// <returns>Every matching word, by token count then form.</returns>
    public static IReadOnlyList<CorpusFormRow> Run(CorpusWordList corpus, QueryExpr? where, PhonSession? phon)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        var record = new QueryRecord(QuerySources.Forms, [], [], Examples: 0, Filter: where);
        var rows = corpus.Words.Select(word => (IQueryRowView)new CorpusView(corpus.Row(word)));
        var outcome = QueryEngine.Execute(record, rows, Spec, phon: phon);
        return [.. outcome.Selected.Cast<CorpusView>().Select(view => view.Row.Word)
            .OrderByDescending(word => word.Tokens).ThenBy(word => word.Form, StringComparer.Ordinal)
            .Select(word => new CorpusFormRow(word.Form, word.Tokens, word.ParseState,
                phon is { Ran: true } ? phon.MatchesFor(word.Form) : null))];
    }

    private sealed class CorpusView(CorpusRow row) : IQueryRowView
    {
        public CorpusRow Row { get; } = row;
        public string Id => Row.Word.Form;
        public string SortKey => Row.Word.Form;
        public long? Tokens => Row.Word.Tokens;
        public IReadOnlyList<string> Get(string column) => Row.Get(column);
    }
}
