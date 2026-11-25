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
            corpus = CorpusWordList.Load(request.CorpusPath, new CorpusOptions(request.FoldCase, segments, request.SplitHyphens));
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
        IRealizesIndexBuilder? indexBuilder = null, string? cacheRoot = null, Action<string>? notice = null, MorphCatalog? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        QueryExpr? where;
        var limit = request.Limit ?? DefaultLimit;
        string segmentTest;
        var wantsCauses = request.GroupBy is not null;
        var retire = new List<ReadingTarget>();
        if (request.GroupBy is not null && !string.Equals(request.GroupBy, "cause", StringComparison.Ordinal))
            return Invalid("group-by is 'cause': the words with more than one reading, grouped by why.");
        foreach (var text in request.WhatIfRetire ?? [])
        {
            if (ReadingTarget.TryParse(text, out var target)) retire.Add(target);
            else if (FormsMorphemeResolver.TryGuid(text, out var guid)) retire.Add(new ReadingTarget("id", guid));
            else return Invalid($"what-if-retire takes entry:<id>, msa:<id> or form:<id>, or a bare id, not '{text}'.");
        }
        var readingsOnly = wantsCauses || request.WhatIfRetire is { Count: > 0 };
        if (request.WhatIfRetire is { Count: 0 }) return Invalid("what-if-retire needs at least one id.");
        if ((request.Where is null) == (request.QueryJson is null) && !(readingsOnly && request.Where is null && request.QueryJson is null))
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
            if ((readingsOnly || OnlyViaLowering.UsesReadings(where)) && string.IsNullOrWhiteSpace(request.GrammarPath))
                return CommandOutcome<CorpusFormsResponse>.Refused(ReadingsRefusals.NeedsGrammar(
                    wantsCauses ? "--group-by cause" : readingsOnly ? "--what-if-retire" : "readings and only-via").Refusal);
        }
        catch (QueryRefusalException refused)
        {
            return CommandOutcome<CorpusFormsResponse>.Refused(refused.Refusal);
        }
        CorpusWordList corpus;
        try
        {
            var segments = request.SegmentsPath is { Length: > 0 } path ? CorpusSegments.Load(path) : CorpusSegments.None;
            corpus = CorpusWordList.Load(request.CorpusPath, new CorpusOptions(request.FoldCase, segments, request.SplitHyphens));
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
                notice ?? (_ => { }));
            try { realizes = RealizesQuery.Prepare(where!, context, cancellationToken); }
            catch (QueryRefusalException refused) { return CommandOutcome<CorpusFormsResponse>.Refused(refused.Refusal); }
        }
        ParsedReadings? readings = null;
        ReadingsReport? readingsReport = null;
        var liveState = !string.IsNullOrWhiteSpace(request.GrammarPath) && QueryExprTools.Leaves(where).Any(leaf => leaf.Field == CorpusFormsQuery.ParseStateColumn);
        var usesReadings = readingsOnly || OnlyViaLowering.UsesReadings(where) || liveState;
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
        if (QueryExprTools.HasPatterns(where))
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
            var summary = new CorpusSummary(request.CorpusPath, corpus.TypeCount, corpus.TokenCount,
                phon is { Ran: true } ? "grammar" : corpus.SegmentationLabel, request.FoldCase);
            CauseReport? causes = null;
            WhatIfReport? whatIf = null;
            if (readings is not null && wantsCauses)
            {
                var builder = new CauseGroupBuilder();
                var tokens = 0L;
                var count = 0;
                foreach (var row in rows)
                    if (readings.Get(row.Form) is { Readings.Count: > 1 } word) { builder.Add(row.Form, row.Tokens, word); count++; tokens += row.Tokens; }
                causes = new CauseReport(readingsReport!.Complete, count, tokens, builder.Build());
            }
            if (readings is not null && retire.Count > 0)
                whatIf = Retirement.Estimate(readings, [.. rows.Select(row => (row.Form, row.Tokens))], retire, readingsReport!.Complete, limit);
            return CommandOutcome<CorpusFormsResponse>.Success(new CorpusFormsResponse(CorpusFormsResponse.SchemaName, summary,
                where is null ? null : QueryExprText.Print(where), rows.Count, [.. rows.Take(limit)], phon is { Ran: true } ? phon.Report : null,
                realizes?.Sets, readingsReport, causes, whatIf));
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

    /// <summary>The estimator behind <c>--what-if-retire</c>; PanGloss's exact retire pass replaces it.</summary>
    private static readonly IRetireEstimator Retirement = new ReadingSetRetire();

    private static CommandOutcome<CorpusFormsResponse> Invalid(string message) =>
        CommandOutcome<CorpusFormsResponse>.Refused(new Refusal(RefusalCodes.QueryCorpusInvalid, FailureReason.InvalidArgument, message));

    private static CommandOutcome<PairsQueryResponse> Refuse(string code, string message) =>
        CommandOutcome<PairsQueryResponse>.Refused(new Refusal(code, FailureReason.InvalidArgument, message));
}
