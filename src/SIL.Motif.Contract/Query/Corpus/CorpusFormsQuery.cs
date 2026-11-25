using System.Text.Json.Serialization;
using SIL.Motif.Contract.Query.Phon;
using SIL.Motif.Contract.Query.Readings;

namespace SIL.Motif.Contract.Query.Corpus;

/// <summary>Asks a plain corpus which forms satisfy a query expression, with no project (<c>motif query --corpus &lt;words&gt; forms</c>).</summary>
/// <param name="CorpusPath">A TSV word list or a folder of .txt files.</param>
/// <param name="Where">The expression text, <c>phon(C V C V)</c> or any v2 text expression; null with <paramref name="QueryJson"/>.</param>
/// <param name="QueryJson">A <c>motif.query.v2</c> document over the <c>forms</c> source; its <c>where</c> and <c>limit</c> apply.</param>
/// <param name="GrammarPath">The grammar PanGloss loads to segment words and name natural classes; needed by <c>phon</c> and <c>env</c>, and parsed with by <c>realizes</c>.</param>
/// <param name="SegmentsPath">An optional segment file of multi-character graphemes for the surface-only predicates.</param>
/// <param name="FoldCase">Lower-case every form before counting.</param>
/// <param name="SplitHyphens">Split running text at hyphens between letters.</param>
/// <param name="Limit">The most rows returned; default 100.</param>
/// <param name="SegmentTest"><c>engine</c> (the default) or <c>identity</c>, how <c>phon</c> and <c>env</c> test a segment; overrides the document's <c>segmentTest</c>.</param>
/// <param name="ProjectPath">A FieldWorks project whose lexicon and stored analyses <c>realizes</c> reads, and whose entries name the morphs of a reading; with <paramref name="GrammarPath"/> both sources are used.</param>
/// <param name="GroupBy">Only <c>cause</c>: group the words with more than one reading by why they have several.</param>
/// <param name="WhatIfRetire">Entry, analysis (<c>msa:</c>) or form (<c>form:</c>) ids to retire; the answer lists the words that would lose or lose some readings. An estimate.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CorpusFormsRequest(string CorpusPath, string? Where = null, string? QueryJson = null, string? GrammarPath = null,
    string? SegmentsPath = null, bool FoldCase = false, bool SplitHyphens = false, int? Limit = null, string? SegmentTest = null,
    string? ProjectPath = null, string? GroupBy = null, IReadOnlyList<string>? WhatIfRetire = null);

/// <summary>One word of a corpus that satisfied the query.</summary>
/// <param name="Form">The word.</param>
/// <param name="Tokens">How often it occurs.</param>
/// <param name="ParseState">Its parse state when the list carried one.</param>
/// <param name="Matches">Where each <c>phon</c> or <c>env</c> predicate matched it; null when the query has none.</param>
/// <param name="Realizes">For each <c>realizes</c> predicate the word satisfies, the forms of its variant set the word carries; null when the query has none.</param>
/// <param name="Readings">How many readings the grammar gives the word; null when the query does not read the corpus parse.</param>
public sealed record CorpusFormRow(string Form, long Tokens, string? ParseState = null, IReadOnlyList<QueryMatch>? Matches = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<RealizesHit>? Realizes = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Readings = null);

/// <summary>What the corpus parse behind a readings query covers, and whether its counts can be trusted.</summary>
/// <param name="Complete">False when a word's parse was capped, timed out or got no answer, or a morph is unknown to the project, so its readings may be fewer than the grammar's.</param>
/// <param name="Types">The word types parsed.</param>
/// <param name="Capped">Words stopped at the parser's work limit.</param>
/// <param name="TimedOut">Words stopped at the word timeout.</param>
/// <param name="Failed">Words the batch gave no answer for.</param>
/// <param name="Unsegmentable">Words the grammar's characters cannot segment; they have no reading and do not make the counts incomplete.</param>
/// <param name="UnknownMsa">Distinct analysis ids in the parse that the project's lexicon does not have (a grammar and project that do not match); they make the counts incomplete.</param>
/// <param name="MorphClasses"><c>project</c> when the project's lexicon named each morph's entry and class, else <c>unavailable</c> and entry ids and classes are absent.</param>
/// <param name="Note">What an incomplete or class-less run means for the answer; null when nothing needs saying.</param>
public sealed record ReadingsReport(bool Complete, int Types, int Capped, int TimedOut, int Failed, int Unsegmentable, int UnknownMsa, string MorphClasses, string? Note);

/// <summary>The words with more than one reading, grouped by cause (<c>--group-by cause</c>).</summary>
/// <param name="Complete">Mirrors <see cref="ReadingsReport.Complete"/>.</param>
/// <param name="Words">Word types with more than one reading among the matches.</param>
/// <param name="Tokens">Their tokens.</param>
/// <param name="Groups">One group per cause, in priority order.</param>
public sealed record CauseReport(bool Complete, int Words, long Tokens, IReadOnlyList<CauseGroup> Groups);

/// <summary>One word's loss under <c>--what-if-retire</c>.</summary>
/// <param name="Word">The word.</param>
/// <param name="Tokens">Its tokens.</param>
/// <param name="Readings">How many readings it has now.</param>
/// <param name="ReadingsVia">How many use the retired morphemes.</param>
/// <param name="ReadingsOther">How many do not.</param>
/// <param name="Lost">Every reading uses them, so the word would lose its parse.</param>
/// <param name="Reduced">Some but not all use them, so the word would keep fewer readings.</param>
/// <param name="MayHaveMore">The word's parse was capped or timed out, so a hidden reading may keep it.</param>
/// <param name="Alternative">For a reduced word, the readings that remain, each as its PanGloss signature or its morph texts joined by +; null otherwise.</param>
public sealed record WhatIfWord(string Word, long Tokens, int Readings, int ReadingsVia, int ReadingsOther, bool Lost, bool Reduced, bool MayHaveMore,
    IReadOnlyList<string>? Alternative = null);

/// <summary>Types and tokens of one class of word.</summary>
/// <param name="Types">Word types.</param>
/// <param name="Tokens">Their tokens.</param>
public sealed record TypesTokens(int Types, long Tokens);

/// <summary>What retiring morphemes would do to the matched words, read from the cached reading sets; an estimate, not a Trial.</summary>
/// <param name="Estimate">Always true: retiring cannot add readings, but a capped parse can hide some; confirm with a Trial.</param>
/// <param name="Exact">Whether PanGloss re-parsed the affected words; false until its <c>batch --retire</c> is used.</param>
/// <param name="Method">How it was computed.</param>
/// <param name="Note">The caution to read with it.</param>
/// <param name="Retire">The morphemes retired.</param>
/// <param name="Complete">Mirrors <see cref="ReadingsReport.Complete"/>.</param>
/// <param name="Affected">Words with a finished parse and at least one reading via the morphemes; capped and failed words are counted apart, so the totals add up.</param>
/// <param name="Lost">Words that would lose every reading.</param>
/// <param name="Reduced">Words that would keep some readings.</param>
/// <param name="Capped">Words whose parse stopped at the work limit or the word timeout and that use the morphemes in a reading shown: not counted in Lost or Reduced, because a hidden reading may keep them.</param>
/// <param name="Failed">Matched words the batch gave no answer for: nothing is known about them.</param>
/// <param name="Words">The affected words, lost first then by tokens; at most the limit.</param>
public sealed record WhatIfReport(bool Estimate, bool Exact, string Method, string Note, IReadOnlyList<string> Retire, bool Complete,
    TypesTokens Affected, TypesTokens Lost, TypesTokens Reduced, TypesTokens Capped, TypesTokens Failed, IReadOnlyList<WhatIfWord> Words);

/// <summary>The words of a corpus that satisfy a query, versioned <see cref="SchemaName"/>.</summary>
/// <param name="Schema">The schema name.</param>
/// <param name="Corpus">The corpus and how its forms were segmented; <c>grammar</c> when PanGloss segmented them.</param>
/// <param name="Where">The query as canonical text.</param>
/// <param name="Matched">Every word that satisfied it.</param>
/// <param name="Rows">At most <c>Limit</c> of them, by token count then form.</param>
/// <param name="Phon">What the pattern predicates could not decide, counted; null when the query has none.</param>
/// <param name="Realizes">The variant set each <c>realizes</c> predicate searched for, with its completeness and compiled pattern; null when the query has none.</param>
/// <param name="ReadingsInfo">The corpus parse a readings query read and whether it is complete; null when the query reads no readings.</param>
/// <param name="Causes">The <c>--group-by cause</c> groups; null when not asked.</param>
/// <param name="WhatIf">The <c>--what-if-retire</c> estimate; null when not asked.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CorpusFormsResponse(string Schema, CorpusSummary Corpus, string? Where, int Matched,
    IReadOnlyList<CorpusFormRow> Rows, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PhonReport? Phon,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<RealizesResult>? Realizes = null,
    [property: JsonPropertyName("readingsInfo"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ReadingsReport? ReadingsInfo = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CauseReport? Causes = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] WhatIfReport? WhatIf = null)
{
    /// <summary>The schema name of the JSON form.</summary>
    public const string SchemaName = "motif.query.corpus-forms.v1";
}

/// <summary>Evaluates a query expression over the word types of a corpus.</summary>
public static class CorpusFormsQuery
{
    /// <summary>The live parse-state column name.</summary>
    public const string ParseStateColumn = "parse.state";

    private static readonly QuerySpec Spec = new(
        column => column is "form" or "tokens" or ParseStateColumn or "readings" || OnlyViaLowering.IsColumn(column) || QuerySources.IsTextColumn(column),
        _ => false, column => column is "tokens" or "syllables" or "readings");

    /// <summary>Runs the expression; the matches and the undecided words come back from <paramref name="phon"/>.</summary>
    /// <returns>Every matching word, by token count then form.</returns>
    public static IReadOnlyList<CorpusFormRow> Run(CorpusWordList corpus, QueryExpr? where, PhonSession? phon, IReadingSource? readings = null)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        var record = new QueryRecord(QuerySources.Forms, [], [], Examples: 0, Filter: where);
        var rows = corpus.Words.Select(word => (IQueryRowView)new CorpusView(corpus.Row(word), readings));
        var outcome = QueryEngine.Execute(record, rows, Spec, phon: phon);
        return [.. outcome.Selected.Cast<CorpusView>().Select(view => view.Row.Word)
            .OrderByDescending(word => word.Tokens).ThenBy(word => word.Form, StringComparer.Ordinal)
            .Select(word => new CorpusFormRow(word.Form, word.Tokens, readings?.Get(word.Form)?.State ?? word.ParseState,
                phon is { Ran: true } ? phon.MatchesFor(word.Form) : null, Readings: readings?.Get(word.Form)?.Readings.Count))];
    }

    private sealed class CorpusView(CorpusRow row, IReadingSource? readings) : IQueryRowView
    {
        public CorpusRow Row { get; } = row;
        public string Id => Row.Word.Form;
        public string SortKey => Row.Word.Form;
        public long? Tokens => Row.Word.Tokens;
        public IReadOnlyList<string> Get(string column)
        {
            if (readings is null) return Row.Get(column);
            var word = readings.Get(Row.Word.Form);
            if (column == "readings") return word is null ? [] : [word.Readings.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)];
            if (column == ParseStateColumn) return word is null ? Row.Get(column) : [word.State];
            if (OnlyViaLowering.TryTarget(column, out var target))
                return word is not null && ViaCount.Of(word, [target]).OnlyVia ? ["only"] : [];
            return Row.Get(column);
        }
    }
}
