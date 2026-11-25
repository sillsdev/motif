using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Query;
using SIL.Motif.Contract.Query.Corpus;
using SIL.Motif.Contract.Query.Phon;
using SIL.Motif.Contract.Query.Readings;

namespace SIL.Motif.Commands.Queries;

/// <summary>Queries a plain corpus word list. It needs no project or Trial and reads only the files named.</summary>
public static class CorpusQueryCommand
{
    /// <summary>Finds the word pairs of the corpus that differ only by an affix.</summary>
    public static CommandOutcome<PairsQueryResponse> Pairs(PairsQueryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var differ = request.Differ?.Trim().ToLowerInvariant() switch { "suffix" => PairsDiffer.Suffix, "prefix" => PairsDiffer.Prefix, _ => (PairsDiffer?)null };
        if (differ is null) return Refuse(RefusalCodes.QueryCorpusInvalid, "differ must be 'suffix' or 'prefix'.");
        CorpusWordList corpus;
        try
        {
            var segments = request.SegmentsPath is { Length: > 0 } path ? CorpusSegments.Load(path) : CorpusSegments.None;
            corpus = CorpusWordList.LoadMany(CorpusWordList.SplitSources(request.CorpusPath), new CorpusOptions(request.FoldCase, segments, request.SplitHyphens), out _);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CorpusFormatException or ArgumentException)
        {
            return Refuse(RefusalCodes.QueryCorpusUnreadable, exception.Message);
        }
        var options = new PairsOptions(differ.Value, request.X, request.Having, request.MaxXLength ?? 4, request.MaxResults ?? 100, request.Examples ?? 5);
        try
        {
            var result = PairsAnalysis.Run(corpus, options);
            var summary = new CorpusSummary(request.CorpusPath, corpus.TypeCount, corpus.TokenCount, corpus.SegmentationLabel, request.FoldCase);
            return CommandOutcome<PairsQueryResponse>.Success(PairsQueryResponse.From(summary, result));
        }
        catch (ArgumentException exception)
        {
            return Refuse(RefusalCodes.QueryCorpusInvalid, exception.Message);
        }
    }

    /// <summary>The default number of rows a corpus forms query returns.</summary>
    public const int DefaultLimit = 100;

    /// <summary>
    /// Finds the words of the corpus that satisfy a query expression. A <c>phon</c> or <c>env</c> predicate needs
    /// <see cref="CorpusFormsRequest.GrammarPath"/>: PanGloss, loading that grammar, segments the words and names the natural classes.
    /// </summary>
    public static CommandOutcome<CorpusFormsResponse> Forms(CorpusFormsRequest request, string? parserPath = null,
        CancellationToken cancellationToken = default, Action<string>? notice = null) =>
        Forms(request, parserPath, cancellationToken, indexBuilder: null, notice: notice);

    // A test passes the parser that replays recorded replies in place of the resident one.
    internal static CommandOutcome<CorpusFormsResponse> Forms(CorpusFormsRequest request, string? parserPath,
        CancellationToken cancellationToken,
        IRealizesIndexBuilder? indexBuilder = null, string? cacheRoot = null, Action<string>? notice = null, MorphCatalog? catalog = null,
        IRealizesGrammar? realizesGrammar = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        QueryExpr? where;
        var limit = request.Limit ?? DefaultLimit;
        string segmentTest;
        var groupColumns = request.GroupBy is null ? null : request.GroupBy.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var wantsCauses = groupColumns is ["cause"];
        var retire = new List<ReadingTarget>();
        if (request.GroupBy is not null)
        {
            if (groupColumns is not { Length: 1 or 2 } || groupColumns.Distinct(StringComparer.Ordinal).Count() != groupColumns.Length)
                return Invalid("group-by is 'cause' or one or two different columns, such as parse.state or parse.state,shape.");
            if (!wantsCauses && groupColumns.FirstOrDefault(column => !CorpusFormsQuery.IsGroupColumn(column)) is { } unknown)
                return Invalid($"group-by is 'cause' or a column of the words (form, tokens, parse.state, readings, shape, syllables, fold, initial:n, final:n), not '{unknown}'.");
        }
        if (request.Sum is not null && !string.Equals(request.Sum, "tokens", StringComparison.Ordinal)) return Invalid("sum is 'tokens'.");
        if (request.Sample is < 1) return Invalid("sample is a positive whole number.");
        if (request.Seed is not null && request.Sample is null) return Invalid("seed goes with sample.");
        if (request.WithCounts && request.WriteWords is null) return Invalid("with-counts goes with write-words.");
        if (request.WriteWords is { } target0 && string.IsNullOrWhiteSpace(target0)) return Invalid("write-words names a file.");
        var groupsColumns = wantsCauses ? null : groupColumns;
        foreach (var text in request.WhatIfRetire ?? [])
        {
            if (ReadingTarget.TryParse(text, out var target)) retire.Add(target);
            else if (FormsMorphemeResolver.TryGuid(text, out var guid)) retire.Add(new ReadingTarget("id", guid));
            else return Invalid($"what-if-retire takes entry:<id>, msa:<id> or form:<id>, or a bare id, not '{text}'.");
        }
        var readingsOnly = wantsCauses || request.WhatIfRetire is { Count: > 0 };
        if (request.WhatIfRetire is { Count: 0 }) return Invalid("what-if-retire needs at least one id.");
        if ((request.Where is null) == (request.QueryJson is null) && !((readingsOnly || groupsColumns is not null || request.Sum is not null || request.Sample is not null || request.WriteWords is not null) && request.Where is null && request.QueryJson is null))
            return Invalid("Give the query as --where <expression> or as --query-json, one of the two.");
        if (limit < 1) return Invalid("limit is a positive whole number.");
        try
        {
            if (request.Where is null && request.QueryJson is null) { where = null; segmentTest = PhonSegmentTest.Normalize(request.SegmentTest, "segment-test"); }
            else if (request.QueryJson is { } json)
            {
                if (!QueryDocument.TryParse(json, out var document, out var documentRefusal))
                    return CommandOutcome<CorpusFormsResponse>.Refused(documentRefusal);
                if (document.From != QuerySources.Forms)
                    return Invalid("A corpus holds forms only; the document's from must be forms.");
                where = document.Where;
                limit = request.Limit ?? document.Limit ?? DefaultLimit;
                segmentTest = PhonSegmentTest.Normalize(request.SegmentTest ?? document.SegmentTest, request.SegmentTest is null ? "segmentTest" : "segment-test");
            }
            else
            {
                if (!QueryExprText.TryParse(request.Where!, out where, out var textRefusal))
                    return CommandOutcome<CorpusFormsResponse>.Refused(textRefusal);
                segmentTest = PhonSegmentTest.Normalize(request.SegmentTest, "segment-test");
            }
            if (QueryExprTools.HasPatterns(where) && string.IsNullOrWhiteSpace(request.GrammarPath))
                return CommandOutcome<CorpusFormsResponse>.Refused(QueryRefusalException.PhonNeedsGrammar().Refusal);
            if (RealizesLowering.Has(where) && string.IsNullOrWhiteSpace(request.GrammarPath) && string.IsNullOrWhiteSpace(request.ProjectPath))
                return CommandOutcome<CorpusFormsResponse>.Refused(RealizesRefusals.NeedsProject().Refusal);
            if (groupsColumns?.Contains("readings") == true && string.IsNullOrWhiteSpace(request.GrammarPath))
                return CommandOutcome<CorpusFormsResponse>.Refused(ReadingsRefusals.NeedsGrammar("--group-by readings").Refusal);
            if ((readingsOnly || OnlyViaLowering.UsesReadings(where)) && string.IsNullOrWhiteSpace(request.GrammarPath))
                return CommandOutcome<CorpusFormsResponse>.Refused(ReadingsRefusals.NeedsGrammar(
                    wantsCauses ? "--group-by cause" : readingsOnly ? "--what-if-retire" : "readings and only-via").Refusal);
        }
        catch (QueryRefusalException refused)
        {
            return CommandOutcome<CorpusFormsResponse>.Refused(refused.Refusal);
        }
        CorpusWordList corpus;
        IReadOnlyList<CorpusSourceInfo> sources;
        try
        {
            var segments = request.SegmentsPath is { Length: > 0 } path ? CorpusSegments.Load(path) : CorpusSegments.None;
            corpus = CorpusWordList.LoadMany(CorpusWordList.SplitSources(request.CorpusPath), new CorpusOptions(request.FoldCase, segments, request.SplitHyphens), out sources);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CorpusFormatException or ArgumentException)
        {
            return CommandOutcome<CorpusFormsResponse>.Refused(new Refusal(RefusalCodes.QueryCorpusUnreadable, FailureReason.InvalidArgument,
                exception.Message));
        }
        RealizesRun? realizes = null;
        if (RealizesLowering.Has(where))
        {
            var context = new RealizesContext(corpus, string.IsNullOrWhiteSpace(request.GrammarPath) ? null : request.GrammarPath,
                string.IsNullOrWhiteSpace(request.ProjectPath) ? null : request.ProjectPath, request.FoldCase,
                () => indexBuilder ?? RealizesQuery.BatchBuilder(parserPath), cacheRoot ?? RealizesParseIndexCache.DefaultRoot,
                notice ?? (_ => { }),
                string.IsNullOrWhiteSpace(request.GrammarPath) && realizesGrammar is null ? null
                    : () => realizesGrammar ?? new ResidentRealizesGrammar(request.GrammarPath!, parserPath), request.Thorough);
            try { realizes = RealizesQuery.Prepare(where!, context, cancellationToken); }
            catch (QueryRefusalException refused) { return CommandOutcome<CorpusFormsResponse>.Refused(refused.Refusal); }
        }
        ParsedReadings? readings = null;
        ReadingsReport? readingsReport = null;
        var liveState = !string.IsNullOrWhiteSpace(request.GrammarPath) && QueryExprTools.Leaves(where).Any(leaf => leaf.Field == CorpusFormsQuery.ParseStateColumn);
        var groupsRead = !string.IsNullOrWhiteSpace(request.GrammarPath) && groupsColumns?.Any(column => column is "readings" or CorpusFormsQuery.ParseStateColumn) == true;
        var usesReadings = readingsOnly || OnlyViaLowering.UsesReadings(where) || liveState || groupsRead;
        if (usesReadings)
        {
            try
            {
                var needsEntries = OnlyViaLowering.Find(where).Select(via => new ReadingTarget(via.Kind, via.Id)).Concat(retire).Where(target => target.Kind == "entry").ToList();
                var needsClasses = wantsCauses || retire.Count > 0 || OnlyViaLowering.Find(where).Count > 0;
                string? catalogFailure = null;
                var loaded = catalog;
                if (needsClasses && loaded is null) loaded = MorphCatalog.TryLoad(request.ProjectPath, request.GrammarPath, out catalogFailure);
                if (wantsCauses && loaded is null) throw ReadingsIdRefusals.CausesNeedProject(catalogFailure);
                if (needsEntries.Count > 0 && loaded is null) throw ReadingsRefusals.NeedsProject(needsEntries[0], catalogFailure);
                var catalogUsed = loaded;
                // A bare id is an entry when the project says so, else an analysis or a form id: both are exact.
                for (var at = 0; at < retire.Count; at++)
                    if (retire[at].Kind == "id" && catalogUsed is not null && catalogUsed.HasEntry(retire[at].Id)) retire[at] = retire[at] with { Kind = "entry" };
                var words = corpus.Words.Select(word => word.Form).ToList();
                (RealizesParseIndex Index, bool Built, double Seconds) built;
                try
                {
                    built = RealizesParseIndexCache.GetOrBuild(cacheRoot ?? RealizesParseIndexCache.DefaultRoot, request.GrammarPath!, words,
                        indexBuilder ?? RealizesQuery.BatchBuilder(parserPath), notice ?? (_ => { }), cancellationToken);
                }
                catch (RealizesIndexException failed) { throw RealizesRefusals.IndexFailed(failed.Message); }
                readings = new ParsedReadings(built.Index, catalogUsed);
                readingsReport = readings.Report(words, needsClasses);
                var asked = OnlyViaLowering.Find(where).Select(via => new ReadingTarget(via.Kind, via.Id)).Concat(retire);
                foreach (var target in asked)
                {
                    var known = target.Kind switch
                    {
                        "entry" => catalogUsed!.HasEntry(target.Id),
                        "msa" => readings.Mentions(target.Id) || catalogUsed?.Of(target.Id) is not null,
                        "form" => readings.Mentions(target.Id),
                        _ => readings.Mentions(target.Id),
                    };
                    if (!known) throw ReadingsIdRefusals.Unknown(target, catalogUsed is not null);
                }
            }
            catch (QueryRefusalException refused) { return CommandOutcome<CorpusFormsResponse>.Refused(refused.Refusal); }
        }
        PhonSession? phon = null;
        if (QueryExprTools.HasPatterns(realizes is null ? where : realizes.Lowered))
        {
            var matcher = PhonMatchers.ForGrammar(request.GrammarPath!, parserPath);
            if (!matcher.Succeeded) return CommandOutcome<CorpusFormsResponse>.Refused(matcher.Refusal!);
            phon = new PhonSession(matcher.Value!, segmentTest: segmentTest);
        }
        try
        {
            var lowered = OnlyViaLowering.Lower(realizes is null ? where : realizes.Lowered);
            var rows = CorpusFormsQuery.Run(corpus, lowered, phon, readings);
            if (realizes is not null) rows = [.. rows.Select(row => row with { Realizes = realizes.HitsFor(row.Form) })];
            var summary = new CorpusSummary(sources.Count > 1 ? string.Join(",", sources.Select(source => source.Path)) : request.CorpusPath, corpus.TypeCount, corpus.TokenCount,
                phon is { Ran: true } ? "grammar" : corpus.SegmentationLabel, request.FoldCase, sources.Count > 1 ? sources : null);
            var matched = rows.Count;
            var matchedRows = rows;
            ValueGroups? groups = groupsColumns is null ? null : CorpusFormsQuery.Group(corpus, rows, groupsColumns, readings);
            TypesTokens? totals = groups is not null || request.Sum is not null ? new TypesTokens(rows.Count, rows.Sum(row => row.Tokens)) : null;
            SampleInfo? sample = null;
            if (request.Sample is { } sampleSize)
            {
                var seed = request.Seed ?? 0;
                rows = CorpusFormsQuery.Sample(rows, sampleSize, seed);
                sample = new SampleInfo(rows.Count, seed, matched);
                if (request.Limit is null) limit = Math.Max(limit, sampleSize);
            }
            // A grouping is a summary: it lists no rows unless --limit asks for some.
            if (groups is not null && request.Sample is null && request.Limit is null && request.QueryJson is null) limit = 0;
            WordsWritten? written = null;
            if (request.WriteWords is { } writePath)
            {
                if (File.Exists(writePath) && !request.Force)
                    return CommandOutcome<CorpusFormsResponse>.Refused(new Refusal(RefusalCodes.QueryWriteWordsExists, FailureReason.InvalidArgument,
                        $"write-words: '{writePath}' exists; nothing was written. Give --force to overwrite it or name another file."));
                try { WriteWords(writePath, rows, request.WithCounts); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                { return Invalid($"write-words: '{writePath}' cannot be written: {exception.Message}"); }
                written = new WordsWritten(writePath, rows.Count, request.WithCounts);
            }
            CauseReport? causes = null;
            WhatIfReport? whatIf = null;
            if (readings is not null && wantsCauses)
            {
                var builder = new CauseGroupBuilder();
                var tokens = 0L;
                var count = 0;
                foreach (var row in matchedRows)
                    if (readings.Get(row.Form) is { Readings.Count: > 1 } word) { builder.Add(row.Form, row.Tokens, word); count++; tokens += row.Tokens; }
                causes = new CauseReport(readingsReport!.Complete, count, tokens, builder.Build());
            }
            if (readings is not null && retire.Count > 0)
                whatIf = Retirement.Estimate(readings, [.. matchedRows.Select(row => (row.Form, row.Tokens))], retire, readingsReport!.Complete, limit);
            return CommandOutcome<CorpusFormsResponse>.Success(new CorpusFormsResponse(CorpusFormsResponse.SchemaName, summary,
                where is null ? null : QueryExprText.Print(where), matched, [.. rows.Take(limit)], phon is { Ran: true } ? phon.Report : null,
                realizes?.Sets, readingsReport, causes, whatIf, groups, totals, sample, written));
        }
        catch (QueryRefusalException refused)
        {
            return CommandOutcome<CorpusFormsResponse>.Refused(refused.Refusal);
        }
        catch (ArgumentException exception)
        {
            return Invalid(exception.Message);
        }
    }

    // Written beside the target and moved into place, so a failed write never leaves half a word list.
    private static void WriteWords(string path, IReadOnlyList<CorpusFormRow> rows, bool withCounts)
    {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var temp = full + ".tmp-" + Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            File.WriteAllLines(temp, rows.Select(row => withCounts ? row.Form + "\t" + row.Tokens.ToString(System.Globalization.CultureInfo.InvariantCulture) : row.Form),
                new System.Text.UTF8Encoding(false));
            File.Move(temp, full, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    /// <summary>The estimator behind <c>--what-if-retire</c>; PanGloss's exact retire pass replaces it.</summary>
    private static readonly IRetireEstimator Retirement = new ReadingSetRetire();

    private static CommandOutcome<CorpusFormsResponse> Invalid(string message) =>
        CommandOutcome<CorpusFormsResponse>.Refused(new Refusal(RefusalCodes.QueryCorpusInvalid, FailureReason.InvalidArgument, message));

    private static CommandOutcome<PairsQueryResponse> Refuse(string code, string message) =>
        CommandOutcome<PairsQueryResponse>.Refused(new Refusal(code, FailureReason.InvalidArgument, message));
}
