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
        IRealizesGrammar? realizesGrammar = null, Func<MorphItem, string?>? entryOfForm = null, bool allowIndexBuild = true,
        IDictionary<string, double>? phases = null, ProjectSession? session = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!allowIndexBuild)
        {
            var inner = indexBuilder;
            indexBuilder = new NoBuildIndexBuilder(() => inner ?? RealizesQuery.BatchBuilder(parserPath));
        }
        var clock = System.Diagnostics.Stopwatch.StartNew();
        void Phase(string name, double since) { if (phases is not null) phases[name] = Math.Round((phases.TryGetValue(name, out var spent) ? spent : 0) + clock.Elapsed.TotalMilliseconds - since, 1); }
        QueryExpr? where;
        var limit = request.Limit ?? DefaultLimit;
        string segmentTest;
        var groupColumns = request.GroupBy is null ? null : request.GroupBy.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var wantsCauses = groupColumns is ["cause"];
        var retire = new List<ReadingTarget>();
        var listed = new RealizesListed();
        QueryExpr? printed = null;
        TwinSet? twins = null;
        if (request.CountBy is not null && !string.Equals(request.CountBy, "morph-seq", StringComparison.Ordinal)) return Invalid("count-by is 'morph-seq'.");
        var wantsMorphSeqs = request.CountBy is not null;
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
        // Refused before the slow parse index is built.
        if (request.WriteWords is { } early && File.Exists(early) && !request.Force) return WriteWordsExists(early);
        foreach (var text in request.WhatIfRetire ?? [])
        {
            if (ReadingTarget.TryParse(text, out var target)) retire.Add(target);
            else if (FormsMorphemeResolver.TryGuid(text, out var guid)) retire.Add(new ReadingTarget("id", guid));
            else return Invalid($"what-if-retire takes entry:<id>, msa:<id> or form:<id>, or a bare id, not '{text}'.");
        }
        var readingsOnly = wantsCauses || wantsMorphSeqs || request.WhatIfRetire is { Count: > 0 };
        if (request.WhatIfRetire is { Count: 0 }) return Invalid("what-if-retire needs at least one id.");
        if ((request.Where is null) == (request.QueryJson is null) && !((readingsOnly || groupsColumns is not null || request.BaselineGrammarPath is not null || request.Sum is not null || request.Sample is not null || request.WriteWords is not null) && request.Where is null && request.QueryJson is null))
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
            printed = where;
            if (RealizesFiles.Has(where)) where = RealizesFiles.Expand(where, listed);
            if (request.Twin is { Count: 0 }) return Invalid("twin takes a template such as '$1ில்'.");
            if (request.Twin is { Count: > 0 } templates)
            {
                try { twins = TwinSet.Compile(where, templates, request.FoldCase); }
                catch (ArgumentException exception) when (exception is not QueryRefusalException) { return Invalid(exception.Message); }
            }
            var twinColumns = QueryExprTools.Leaves(where).Select(leaf => leaf.Field).Concat(groupsColumns ?? []).Where(TwinSet.IsColumn).ToList();
            if (twinColumns.Count > 0 && twins is null) return Invalid($"{twinColumns[0]} is a column of --twin; give a template, such as --twin '$1ில்'.");
            if (twins is not null && twinColumns.FirstOrDefault(column => TwinSet.Template(column) > twins.Count) is { } beyond)
                return Invalid($"{beyond} names template {TwinSet.Template(beyond)}, and --twin was given {twins.Count}.");
            if (twinColumns.FirstOrDefault(TwinSet.NeedsParse) is { } needsParse && string.IsNullOrWhiteSpace(request.GrammarPath))
                return CommandOutcome<CorpusFormsResponse>.Refused(ReadingsRefusals.NeedsGrammar(needsParse).Refusal);
            var baselineColumns = QueryExprTools.Leaves(where).Select(leaf => leaf.Field).Concat(groupsColumns ?? []).Where(BaselineSet.IsColumn).ToList();
            if (request.BaselineGrammarPath is { } baselinePath)
            {
                if (string.IsNullOrWhiteSpace(baselinePath)) return Invalid("baseline-grammar names a grammar.");
                if (string.IsNullOrWhiteSpace(request.GrammarPath)) return CommandOutcome<CorpusFormsResponse>.Refused(ReadingsRefusals.NeedsGrammar("--baseline-grammar").Refusal);
                if (SameFile(baselinePath, request.GrammarPath))
                    return CommandOutcome<CorpusFormsResponse>.Refused(new Refusal(RefusalCodes.QueryBaselineSameGrammar, FailureReason.InvalidArgument,
                        $"--baseline-grammar and --grammar are the same file ('{request.GrammarPath}'), so every word would be unchanged. Compare two different grammars, such as the original and a copy you changed."));
            }
            else if (baselineColumns.Count > 0)
                return Invalid($"{baselineColumns[0]} is a column of --baseline-grammar; give the grammar to compare with, such as --baseline-grammar before.fwdata.");
            if (request.ByMorph && !RealizesLowering.Has(where)) return Invalid("by-morph groups the rows of a realizes(...) query; the query has none.");
            if (QueryExprTools.HasPatterns(where) && string.IsNullOrWhiteSpace(request.GrammarPath))
                return CommandOutcome<CorpusFormsResponse>.Refused(QueryRefusalException.PhonNeedsGrammar().Refusal);
            if (RealizesLowering.Has(where) && string.IsNullOrWhiteSpace(request.GrammarPath) && string.IsNullOrWhiteSpace(request.ProjectPath))
                return CommandOutcome<CorpusFormsResponse>.Refused(RealizesRefusals.NeedsProject().Refusal);
            if (groupsColumns?.Contains("readings") == true && string.IsNullOrWhiteSpace(request.GrammarPath))
                return CommandOutcome<CorpusFormsResponse>.Refused(ReadingsRefusals.NeedsGrammar("--group-by readings").Refusal);
            if ((readingsOnly || OnlyViaLowering.UsesReadings(where) || MorphSeqLowering.Has(where)) && string.IsNullOrWhiteSpace(request.GrammarPath))
                return CommandOutcome<CorpusFormsResponse>.Refused(ReadingsRefusals.NeedsGrammar(
                    wantsCauses ? "--group-by cause" : wantsMorphSeqs ? "--count-by morph-seq" : readingsOnly ? "--what-if-retire" : MorphSeqLowering.Has(where) ? "morph and seq" : "readings and only-via").Refusal);
        }
        catch (QueryRefusalException refused)
        {
            return CommandOutcome<CorpusFormsResponse>.Refused(refused.Refusal);
        }
        CorpusWordList corpus;
        IReadOnlyList<CorpusSourceInfo> sources;
        var loadStart = clock.Elapsed.TotalMilliseconds;
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
        Phase("corpus", loadStart);
        RealizesRun? realizes = null;
        if (RealizesLowering.Has(where))
        {
            var context = new RealizesContext(corpus, string.IsNullOrWhiteSpace(request.GrammarPath) ? null : request.GrammarPath,
                string.IsNullOrWhiteSpace(request.ProjectPath) ? null : request.ProjectPath, request.FoldCase,
                () => indexBuilder ?? RealizesQuery.BatchBuilder(parserPath), cacheRoot ?? RealizesParseIndexCache.DefaultRoot,
                notice ?? (_ => { }),
                string.IsNullOrWhiteSpace(request.GrammarPath) && realizesGrammar is null ? null
                    : () => realizesGrammar ?? new ResidentRealizesGrammar(request.GrammarPath!, parserPath), request.Thorough, catalog is null ? null : catalog.MsasOf);
            try { realizes = RealizesQuery.Prepare(where!, context, cancellationToken, listed); }
            catch (QueryRefusalException refused) { return CommandOutcome<CorpusFormsResponse>.Refused(refused.Refusal); }
        }
        ParsedReadings? readings = null;
        ReadingsReport? readingsReport = null;
        ReadingsReport? baselineReport = null;
        BaselineCells? baseline = null;
        var liveState = !string.IsNullOrWhiteSpace(request.GrammarPath) && QueryExprTools.Leaves(where).Any(leaf => leaf.Field == CorpusFormsQuery.ParseStateColumn);
        var groupsRead = !string.IsNullOrWhiteSpace(request.GrammarPath) && groupsColumns?.Any(column => column is "readings" or CorpusFormsQuery.ParseStateColumn) == true;
        var seqItems = MorphSeqSupport.ItemsOf(where);
        var twinParse = twins is not null && !string.IsNullOrWhiteSpace(request.GrammarPath);
        var comparing = request.BaselineGrammarPath is not null;
        var usesReadings = comparing || readingsOnly || OnlyViaLowering.UsesReadings(where) || liveState || groupsRead || seqItems.Count > 0 || twinParse;
        MorphSeqPlan? seqPlan = null;
        if (usesReadings)
        {
            try
            {
                var needsEntries = OnlyViaLowering.Find(where).Select(via => new ReadingTarget(via.Kind, via.Id)).Concat(retire).Where(target => target.Kind == "entry").ToList();
                var needsClasses = wantsCauses || retire.Count > 0 || OnlyViaLowering.Find(where).Count > 0 || seqItems.Any(item => item.NeedsProject);
                string? catalogFailure = null;
                var loaded = catalog;
                var formEntries = new Dictionary<string, string?>(StringComparer.Ordinal);
                var projectStart = clock.Elapsed.TotalMilliseconds;
                if (needsClasses && loaded is null)
                    loaded = MorphCatalog.TryLoad(request.ProjectPath, request.GrammarPath, out catalogFailure, cache =>
                    {
                        foreach (var item in seqItems.Where(item => item.NeedsLexiconForm))
                            formEntries[item.Kind + ":" + item.Value] = RealizesQuery.Resolve(cache, item.Value, item.Kind == "root" ? true : false).EntryGuid;
                    }, session);
                Phase("project", projectStart);
                if (loaded is null && seqItems.FirstOrDefault(item => item.NeedsProject) is { } needy) throw MorphSeqRefusals.NeedsProject(needy, catalogFailure);
                if (wantsCauses && loaded is null) throw ReadingsIdRefusals.CausesNeedProject(catalogFailure);
                if (needsEntries.Count > 0 && loaded is null) throw ReadingsRefusals.NeedsProject(needsEntries[0], catalogFailure);
                var catalogUsed = loaded;
                // A bare id is an entry when the project says so, else an analysis or a form id: both are exact.
                for (var at = 0; at < retire.Count; at++)
                    if (retire[at].Kind == "id" && catalogUsed is not null && catalogUsed.HasEntry(retire[at].Id)) retire[at] = retire[at] with { Kind = "entry" };
                var words = corpus.Words.Select(word => word.Form).ToList();
                (RealizesParseIndex Index, bool Built, double Seconds) built;
                var indexStart = clock.Elapsed.TotalMilliseconds;
                try
                {
                    built = RealizesParseIndexCache.GetOrBuild(cacheRoot ?? RealizesParseIndexCache.DefaultRoot, request.GrammarPath!, words,
                        indexBuilder ?? RealizesQuery.BatchBuilder(parserPath), notice ?? (_ => { }), cancellationToken);
                }
                catch (RealizesIndexException failed) { throw RealizesRefusals.IndexFailed(failed.Message); }
                Phase(built.Built ? "index-build" : "index-load", indexStart);
                readings = new ParsedReadings(built.Index, catalogUsed);
                var reported = words;
                if (twins is not null)
                    reported = [.. words, .. BindTwins(twins, corpus, readings, catalogUsed, request.GrammarPath!, cacheRoot, indexBuilder, parserPath, notice, cancellationToken)];
                readingsReport = readings.Report(reported, needsClasses);
                if (comparing)
                {
                    (RealizesParseIndex Index, bool Built, double Seconds) before;
                    try
                    {
                        before = RealizesParseIndexCache.GetOrBuild(cacheRoot ?? RealizesParseIndexCache.DefaultRoot, request.BaselineGrammarPath!, words,
                            indexBuilder ?? RealizesQuery.BatchBuilder(parserPath), notice ?? (_ => { }), cancellationToken);
                    }
                    catch (RealizesIndexException failed) { throw RealizesRefusals.IndexFailed("The baseline grammar: " + failed.Message); }
                    var beforeReadings = new ParsedReadings(before.Index, null);
                    baselineReport = beforeReadings.Report(words, false);
                    baseline = BaselineCells.Bind(words, beforeReadings, readings);
                    readingsReport = WithBaseline(readingsReport, baselineReport, words.Count(word => baseline.Cell(word)?.Change == BaselineSet.Unknown));
                }
                if (seqItems.Count > 0)
                {
                    seqPlan = MorphSeqSupport.Compile(realizes is null ? where! : realizes.Lowered!, request.Adjacent, item =>
                        formEntries.TryGetValue(item.Kind + ":" + item.Value, out var resolved) ? resolved : entryOfForm?.Invoke(item));
                    if (seqItems.FirstOrDefault(item => item.NeedsLexiconForm && !formEntries.ContainsKey(item.Kind + ":" + item.Value) && entryOfForm?.Invoke(item) is null) is { } unresolved)
                        throw MorphSeqRefusals.NeedsProject(unresolved, catalogFailure);
                    if (MorphSeqLowering.Find(where).Any(found => found.Negated) && !readingsReport.Complete) throw MorphSeqRefusals.NegationIncomplete(readingsReport);
                    if (MorphSeqSupport.FirstUnmatched(seqPlan, readings, words) is { } unmatched) throw MorphSeqRefusals.ItemUnknown(unmatched, catalogUsed is not null);
                }
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
        if (twins is not null && !twinParse) twins.Bind(corpus, null);
        PhonSession? phon = null;
        if (QueryExprTools.HasPatterns(realizes is null ? where : realizes.Lowered))
        {
            var matcher = PhonMatchers.ForGrammar(request.GrammarPath!, parserPath);
            if (!matcher.Succeeded) return CommandOutcome<CorpusFormsResponse>.Refused(matcher.Refusal!);
            phon = new PhonSession(matcher.Value!, segmentTest: segmentTest);
        }
        var queryStart = clock.Elapsed.TotalMilliseconds;
        try
        {
            var lowered = OnlyViaLowering.Lower(realizes is null ? where : realizes.Lowered);
            if (seqPlan is not null) lowered = OnlyViaLowering.Lower(seqPlan.Lowered);
            var rows = CorpusFormsQuery.Run(corpus, lowered, phon, readings, seqPlan?.Tests, twins, baseline);
            if (realizes is not null) rows = [.. rows.Select(row => row with { Realizes = realizes.HitsFor(row.Form) })];
            var summary = new CorpusSummary(sources.Count > 1 ? string.Join(",", sources.Select(source => source.Path)) : request.CorpusPath, corpus.TypeCount, corpus.TokenCount,
                phon is { Ran: true } ? "grammar" : corpus.SegmentationLabel, request.FoldCase, sources.Count > 1 ? sources : null);
            var matched = rows.Count;
            var matchedRows = rows;
            ValueGroups? groups = groupsColumns is null ? null : CorpusFormsQuery.Group(corpus, rows, groupsColumns, readings, twins, baseline);
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
            if ((groups is not null || wantsMorphSeqs) && request.Sample is null && request.Limit is null && request.QueryJson is null) limit = 0;
            WordsWritten? written = null;
            if (request.WriteWords is { } writePath)
            {
                if (File.Exists(writePath) && !request.Force) return WriteWordsExists(writePath);
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
            MorphSeqReport? morphSeqs = null;
            if (readings is not null && wantsMorphSeqs)
            {
                var keep = seqPlan is null ? (_ => true) : seqPlan.Tests.ReadingFilter(seqPlan.Lowered);
                morphSeqs = MorphSeqSupport.Count(matchedRows, readings, keep, readingsReport!.Complete, request.Limit ?? DefaultLimit);
            }
            IReadOnlyList<MorphemeGroup>? byMorph = null;
            if (request.ByMorph && realizes is not null) byMorph = ByMorphemes(realizes, matchedRows);
            if (readings is not null && retire.Count > 0)
                whatIf = Retirement.Estimate(readings, [.. matchedRows.Select(row => (row.Form, row.Tokens))], retire, readingsReport!.Complete, limit);
            Phase("query", queryStart);
            return CommandOutcome<CorpusFormsResponse>.Success(new CorpusFormsResponse(CorpusFormsResponse.SchemaName, summary,
                printed is null ? null : QueryExprText.Print(printed), matched, [.. rows.Take(limit)], phon is { Ran: true } ? phon.Report : null,
                realizes?.Sets, readingsReport, causes, whatIf, groups, totals, sample, written, morphSeqs, byMorph, baselineReport));
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

    private static bool SameFile(string first, string second) =>
        string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    // The target report, incomplete when the baseline has a word that did not finish.
    private static ReadingsReport WithBaseline(ReadingsReport target, ReadingsReport baseline, int unknownWords)
    {
        if (baseline.Complete && unknownWords == 0) return target;
        var note = $"The baseline grammar's parse: {baseline.Capped} words stopped at the work limit, {baseline.TimedOut} at the word timeout and {baseline.Failed} got no answer. " +
            $"{unknownWords} word types therefore have change = unknown: they are neither gained nor lost.";
        return target with { Complete = target.Complete && baseline.Complete, Note = target.Note is null ? note : target.Note + " " + note };
    }

    /// <summary>The most word types the twins may add to the corpus parse: one batch parses the ones the corpus lacks.</summary>
    internal static int MaxAbsentTwins { get; set; } = 100_000;

    // A twin of a corpus word reads the corpus index; one the corpus lacks is parsed by a cached batch of its own.
    private static IReadOnlyList<string> BindTwins(TwinSet twins, CorpusWordList corpus, ParsedReadings readings, MorphCatalog? catalog, string grammarPath, string? cacheRoot,
        IRealizesIndexBuilder? indexBuilder, string? parserPath, Action<string>? notice, CancellationToken cancellationToken)
    {
        var absent = twins.AbsentForms(corpus);
        if (absent.Count > MaxAbsentTwins)
            throw new QueryRefusalException(new Refusal(RefusalCodes.QueryCorpusInvalid, FailureReason.InvalidArgument,
                $"--twin makes {absent.Count} forms the corpus lacks; parsing more than {MaxAbsentTwins} of them is refused. Narrow the where."));
        ParsedReadings? extra = null;
        if (absent.Count > 0)
        {
            try
            {
                // Their own folder, so a run of twin batches never evicts the corpus index.
                var built = RealizesParseIndexCache.GetOrBuild(Path.Combine(cacheRoot ?? RealizesParseIndexCache.DefaultRoot, "twins"), grammarPath, absent,
                    indexBuilder ?? RealizesQuery.BatchBuilder(parserPath), notice ?? (_ => { }), cancellationToken);
                extra = new ParsedReadings(built.Index, catalog);
            }
            catch (RealizesIndexException failed) { throw RealizesRefusals.IndexFailed(failed.Message); }
        }
        readings.Extra = extra;
        twins.Bind(corpus, readings.Get);
        return absent;
    }

    // One group per morpheme set, including the ones no word carries, so a stem list shows its zeros.
    private static IReadOnlyList<MorphemeGroup> ByMorphemes(RealizesRun run, IReadOnlyList<CorpusFormRow> rows)
    {
        var found = new Dictionary<string, List<CorpusFormRow>>(StringComparer.Ordinal);
        foreach (var set in run.Sets) found.TryAdd(set.Morpheme, []);
        foreach (var row in rows)
            foreach (var hit in row.Realizes ?? [])
                if (found.TryGetValue(hit.Morpheme, out var list) && !list.Contains(row)) list.Add(row);
        return [.. found.Select(pair => new MorphemeGroup(pair.Key, pair.Value.Count, pair.Value.Sum(row => row.Tokens),
                [.. pair.Value.OrderByDescending(row => row.Tokens).ThenBy(row => row.Form, StringComparer.Ordinal).Take(5).Select(row => row.Form)]))
            .OrderByDescending(group => group.Tokens).ThenBy(group => group.Morpheme, StringComparer.Ordinal)];
    }

    private static CommandOutcome<CorpusFormsResponse> WriteWordsExists(string path) =>
        CommandOutcome<CorpusFormsResponse>.Refused(new Refusal(RefusalCodes.QueryWriteWordsExists, FailureReason.InvalidArgument,
            $"write-words: '{path}' exists; nothing was written. Give --force to overwrite it or name another file."));

    // Written beside the target and moved into place, so a failed write never leaves half a word list.
    private static void WriteWords(string path, IReadOnlyList<CorpusFormRow> rows, bool withCounts)
    {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var temp = full + ".tmp-" + Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            // LF on every platform: WriteAllLines would use CRLF on Windows.
            File.WriteAllText(temp, string.Concat(rows.Select(row => (withCounts ? row.Form + "\t" + row.Tokens.ToString(System.Globalization.CultureInfo.InvariantCulture) : row.Form) + "\n")),
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
