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
/// <param name="Thorough">Whether <c>realizes</c> at the <c>generated</c> tier takes three test roots per rule environment instead of two.</param>
/// <param name="GroupBy"><c>cause</c> (the words with more than one reading, by why), or one or two comma-separated columns (<c>parse.state</c>, <c>readings</c>, <c>shape</c>, <c>syllables</c>, <c>fold</c>, <c>initial:n</c>, <c>final:n</c>, <c>tokens</c>, <c>form</c>): types and tokens per distinct value.</param>
/// <param name="WhatIfRetire">Entry, analysis (<c>msa:</c>) or form (<c>form:</c>) ids to retire; the answer lists the words that would lose or lose some readings. An estimate.</param>
/// <param name="Sum"><c>tokens</c>: give <c>totals</c> (types and tokens of every match) even without a grouping; a grouping always carries tokens.</param>
/// <param name="Sample">Keep only this many of the matching words, chosen by <paramref name="Seed"/> and not by frequency; groups and totals still cover every match.</param>
/// <param name="Seed">The seed of the sample; default 0. The same seed over the same matches gives the same words.</param>
/// <param name="WriteWords">A file to write the matching (or sampled) forms to, one per line; every match, not just the first <paramref name="Limit"/>.</param>
/// <param name="WithCounts">Write <c>form&lt;TAB&gt;count</c> lines instead of the form alone.</param>
/// <param name="Force">Overwrite <paramref name="WriteWords"/> when it exists; otherwise that is refused and nothing is written.</param>
/// <param name="CountBy"><c>morph-seq</c>: count the distinct morph sequences of the matching readings.</param>
/// <param name="Adjacent">Every <c>seq(...)</c> needs its morphs next to each other, as if each said <c>adjacent=true</c>.</param>
/// <param name="ByMorph">Group the rows of a <c>realizes</c> query per morpheme.</param>
/// <param name="BaselineGrammarPath">A second grammar whose parse of the corpus is the baseline: the columns <c>base.parse.state</c>, <c>base.readings</c> and <c>change</c> compare <paramref name="GrammarPath"/> with it, word by word.</param>
/// <param name="Twin">Templates that rewrite each match of the where's <c>form ~</c> regex from its groups; the twin columns describe the results.</param>
/// <param name="Approved">A JSONL file of approved readings in the linguist-oracle format, one <c>{"type": "&lt;word&gt;", "readings": [{"gloss": "...", "class": "correct"}, ...]}</c> line per word, where a reading is approved when its <c>class</c> is <c>correct</c> and its <c>gloss</c> is the rendered morph sequence (<c>text/gloss</c> per morph joined by <c> + </c>); the answer then carries an <c>approved</c> comparison with what the grammar produces. Needs <paramref name="GrammarPath"/>.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CorpusFormsRequest(string CorpusPath, string? Where = null, string? QueryJson = null, string? GrammarPath = null,
    string? SegmentsPath = null, bool FoldCase = false, bool SplitHyphens = false, int? Limit = null, string? SegmentTest = null,
    string? ProjectPath = null, string? GroupBy = null, IReadOnlyList<string>? WhatIfRetire = null, bool Thorough = false, string? Sum = null, int? Sample = null,
    long? Seed = null, string? WriteWords = null, bool WithCounts = false, bool Force = false, string? CountBy = null, bool Adjacent = false, bool ByMorph = false,
    IReadOnlyList<string>? Twin = null, string? BaselineGrammarPath = null, string? Approved = null);

/// <summary>One word of a corpus that satisfied the query.</summary>
/// <param name="Form">The word.</param>
/// <param name="Tokens">How often it occurs.</param>
/// <param name="ParseState">Its parse state when the list carried one.</param>
/// <param name="Matches">Where each <c>phon</c> or <c>env</c> predicate matched it; null when the query has none.</param>
/// <param name="Realizes">For each <c>realizes</c> predicate the word satisfies, the forms of its variant set the word carries; null when the query has none.</param>
/// <param name="Readings">How many readings the grammar gives the word; null when the query does not read the corpus parse.</param>
/// <param name="Twins">The word rewritten by each <c>--twin</c> template; null when not asked or the regex does not match the word.</param>
/// <param name="Baseline">The word's parse under <c>--baseline-grammar</c> and what changed since; null when not asked.</param>
public sealed record CorpusFormRow(string Form, long Tokens, string? ParseState = null, IReadOnlyList<QueryMatch>? Matches = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<RealizesHit>? Realizes = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Readings = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<TwinCell>? Twins = null,
    [property: JsonPropertyName("baseline"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BaselineCell? Baseline = null);

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

/// <summary>One distinct morph sequence of the matching readings (<c>--count-by morph-seq</c>).</summary>
/// <param name="Seq">The morphs in surface order, each as its text, then <c>/</c> and its gloss when the project gives one; a morph with no placed text shows the first eight characters of its analysis id.</param>
/// <param name="Types">Word types with a matching reading that has the sequence; a word counts once per distinct sequence.</param>
/// <param name="Tokens">Their tokens.</param>
/// <param name="Examples">Up to five of the words, most frequent first.</param>
public sealed record MorphSeqEntry(IReadOnlyList<string> Seq, int Types, long Tokens, IReadOnlyList<string> Examples);

/// <summary>The distinct morph sequences of the matching readings, by tokens.</summary>
/// <param name="Complete">Mirrors <see cref="ReadingsReport.Complete"/>: false when a capped, timed-out or unanswered word may hide readings, so counts are lower bounds.</param>
/// <param name="Words">Matching word types with at least one matching reading.</param>
/// <param name="Tokens">Their tokens.</param>
/// <param name="Distinct">How many distinct sequences there are; <paramref name="Sequences"/> lists at most --limit (default 100) of them.</param>
/// <param name="Sequences">The most frequent sequences, by tokens then types then sequence.</param>
public sealed record MorphSeqReport(bool Complete, int Words, long Tokens, int Distinct, IReadOnlyList<MorphSeqEntry> Sequences);

/// <summary>The matching words that carry one morpheme of a <c>realizes</c> query (<c>--by-morph</c>).</summary>
/// <param name="Morpheme">The morpheme as the realizes set names it.</param>
/// <param name="Types">Matching word types that carry one of its forms.</param>
/// <param name="Tokens">Their tokens.</param>
/// <param name="Examples">Up to five of the words, most frequent first.</param>
public sealed record MorphemeGroup(string Morpheme, int Types, long Tokens, IReadOnlyList<string> Examples);

/// <summary>One value (or pair of values) of the grouped columns with the matching words that carry it.</summary>
/// <param name="Key">The values joined by <c> | </c>; <c>(none)</c> for a word with no value.</param>
/// <param name="Values">One value per grouped column, in order.</param>
/// <param name="Types">Matching word types.</param>
/// <param name="Tokens">Their tokens.</param>
public sealed record ValueGroup(string Key, IReadOnlyList<string> Values, int Types, long Tokens);

/// <summary>The matching words grouped by columns (<c>--group-by parse.state</c>).</summary>
/// <param name="Columns">The grouped columns.</param>
/// <param name="Groups">One group per distinct value, by tokens then key.</param>
public sealed record ValueGroups(IReadOnlyList<string> Columns, IReadOnlyList<ValueGroup> Groups);

/// <summary>The sample <c>--sample</c> kept.</summary>
/// <param name="Count">Words kept.</param>
/// <param name="Seed">The seed.</param>
/// <param name="Of">How many words matched.</param>
public sealed record SampleInfo(int Count, long Seed, int Of);

/// <summary>The file <c>--write-words</c> wrote.</summary>
/// <param name="Path">The file.</param>
/// <param name="Words">Lines written.</param>
/// <param name="WithCounts">Whether each line carries the count.</param>
public sealed record WordsWritten(string Path, int Words, bool WithCounts);

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
/// <param name="Groups">The <c>--group-by &lt;column&gt;</c> groups over every match; null when not asked.</param>
/// <param name="Totals">Types and tokens of every match; present with <c>--group-by &lt;column&gt;</c> or <c>--sum tokens</c>.</param>
/// <param name="Sample">The <c>--sample</c> that was drawn; null when not asked.</param>
/// <param name="Written">The <c>--write-words</c> file; null when not asked.</param>
/// <param name="MorphSeqs">The <c>--count-by morph-seq</c> report; null when not asked.</param>
/// <param name="ByMorph">The <c>--by-morph</c> groups of a <c>realizes</c> query, by tokens; null when not asked.</param>
/// <param name="BaselineInfo">The corpus parse under <c>--baseline-grammar</c> and whether it is complete; null when not asked. When it is not complete <c>readingsInfo.complete</c> is false too.</param>
/// <param name="Approved">The <c>--approved</c> comparison of an approved-readings file with the grammar's readings; null when not asked.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CorpusFormsResponse(string Schema, CorpusSummary Corpus, string? Where, int Matched,
    IReadOnlyList<CorpusFormRow> Rows, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PhonReport? Phon,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<RealizesResult>? Realizes = null,
    [property: JsonPropertyName("readingsInfo"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ReadingsReport? ReadingsInfo = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CauseReport? Causes = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] WhatIfReport? WhatIf = null,
    [property: JsonPropertyName("groups"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ValueGroups? Groups = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TypesTokens? Totals = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SampleInfo? Sample = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] WordsWritten? Written = null,
    [property: JsonPropertyName("morphSeqs"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] MorphSeqReport? MorphSeqs = null,
    [property: JsonPropertyName("byMorph"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<MorphemeGroup>? ByMorph = null,
    [property: JsonPropertyName("baselineInfo"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ReadingsReport? BaselineInfo = null,
    [property: JsonPropertyName("approved"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ApprovedReport? Approved = null)
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
        column => column is "form" or "tokens" or ParseStateColumn or "readings" || TwinSet.IsColumn(column) || BaselineSet.IsColumn(column) || OnlyViaLowering.IsColumn(column) || MorphSeqLowering.IsColumn(column) || QuerySources.IsTextColumn(column),
        _ => false, column => column is "tokens" or "syllables" or "readings" || TwinSet.IsNumeric(column) || BaselineSet.IsNumeric(column));

    /// <summary>Whether a name is a column a corpus word has, so it can be grouped by.</summary>
    public static bool IsGroupColumn(string column) =>
        column is "form" or "tokens" or ParseStateColumn or "readings" || TwinSet.IsColumn(column) || BaselineSet.IsColumn(column) || QuerySources.IsTextColumn(column);

    /// <summary>The value a group key shows for a word with no value in the column.</summary>
    public const string NoValue = "(none)";

    /// <summary>Counts the types and tokens of the rows per distinct value of the columns; groups run by tokens then key.</summary>
    public static ValueGroups Group(CorpusWordList corpus, IReadOnlyList<CorpusFormRow> rows, IReadOnlyList<string> columns, IReadingSource? readings = null, TwinSet? twins = null, BaselineCells? baseline = null)
    {
        var counts = new Dictionary<string, (string[] Values, int Types, long Tokens)>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (!corpus.TryGet(row.Form, out var word)) continue;
            var view = corpus.Row(word);
            var values = columns.Select(column => Values(view, readings, column, twins, baseline) is { Count: > 0 } found ? string.Join(",", found) : NoValue).ToArray();
            var key = string.Join(" | ", values);
            counts[key] = counts.TryGetValue(key, out var seen) ? (seen.Values, seen.Types + 1, seen.Tokens + row.Tokens) : (values, 1, row.Tokens);
        }
        return new ValueGroups(columns, [.. counts.OrderByDescending(pair => pair.Value.Tokens).ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new ValueGroup(pair.Key, pair.Value.Values, pair.Value.Types, pair.Value.Tokens))]);
    }

    /// <summary>Keeps <paramref name="count"/> of the rows, the ones whose hash with the seed is smallest, so the choice ignores frequency and order; returned by tokens then form.</summary>
    public static IReadOnlyList<CorpusFormRow> Sample(IReadOnlyList<CorpusFormRow> rows, int count, long seed)
    {
        var chosen = rows.Select(row => (Row: row, Hash: SampleHash(seed, row.Form))).OrderBy(pair => pair.Hash).ThenBy(pair => pair.Row.Form, StringComparer.Ordinal)
            .Take(count).Select(pair => pair.Row);
        return [.. chosen.OrderByDescending(row => row.Tokens).ThenBy(row => row.Form, StringComparer.Ordinal)];
    }

    private static ulong SampleHash(long seed, string form)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\0" + form));
        return System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(bytes);
    }

    private static IReadOnlyList<string> Values(CorpusRow row, IReadingSource? readings, string column, TwinSet? twins, BaselineCells? baseline)
    {
        if (baseline is not null && BaselineSet.IsColumn(column)) return baseline.Value(row.Word.Form, column);
        if (twins is not null && TwinSet.IsColumn(column)) return twins.Value(row.Word.Form, column);
        if (readings is null) return row.Get(column);
        var word = readings.Get(row.Word.Form);
        if (column == "readings") return word is null ? [] : [word.Readings.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)];
        // A word with no parse entry is counted failed by the readings report, so it groups there.
        if (column == ParseStateColumn) return [word?.State ?? ReadingStates.Failed];
        return row.Get(column);
    }

    /// <summary>Runs the expression; the matches and the undecided words come back from <paramref name="phon"/>.</summary>
    /// <returns>Every matching word, by token count then form.</returns>
    public static IReadOnlyList<CorpusFormRow> Run(CorpusWordList corpus, QueryExpr? where, PhonSession? phon, IReadingSource? readings = null, ReadingTests? tests = null, TwinSet? twins = null, BaselineCells? baseline = null)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        var record = new QueryRecord(QuerySources.Forms, [], [], Examples: 0, Filter: where);
        var rows = corpus.Words.Select(word => (IQueryRowView)new CorpusView(corpus.Row(word), readings, tests, twins, baseline));
        var outcome = QueryEngine.Execute(record, rows, Spec, phon: phon);
        return [.. outcome.Selected.Cast<CorpusView>().Select(view => view.Row.Word)
            .OrderByDescending(word => word.Tokens).ThenBy(word => word.Form, StringComparer.Ordinal)
            .Select(word => new CorpusFormRow(word.Form, word.Tokens, readings?.Get(word.Form)?.State ?? word.ParseState,
                phon is { Ran: true } ? phon.MatchesFor(word.Form) : null, Readings: readings?.Get(word.Form)?.Readings.Count, Twins: twins?.Cells(word.Form), Baseline: baseline?.Cell(word.Form)))];
    }

    private sealed class CorpusView(CorpusRow row, IReadingSource? readings, ReadingTests? tests, TwinSet? twins, BaselineCells? baseline) : IQueryRowView
    {
        public CorpusRow Row { get; } = row;
        public string Id => Row.Word.Form;
        public string SortKey => Row.Word.Form;
        public long? Tokens => Row.Word.Tokens;
        public IReadOnlyList<string> Get(string column)
        {
            if (twins is not null && TwinSet.IsColumn(column)) return twins.Value(Row.Word.Form, column);
            if (baseline is not null && BaselineSet.IsColumn(column)) return baseline.Value(Row.Word.Form, column);
            if (readings is null) return Row.Get(column);
            var word = readings.Get(Row.Word.Form);
            if (column == "readings") return word is null ? [] : [word.Readings.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)];
            if (column == ParseStateColumn) return word is null ? Row.Get(column) : [word.State];
            if (OnlyViaLowering.TryTarget(column, out var target))
                return word is not null && ViaCount.Of(word, [target]).OnlyVia ? ["only"] : [];
            if (MorphSeqLowering.IsColumn(column))
                return word is not null && tests is not null && tests.Word(column, word) ? ["yes"] : [];
            return Row.Get(column);
        }
    }
}
