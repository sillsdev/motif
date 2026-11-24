using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Query;
using SIL.Motif.Contract.Query.Corpus;
using SIL.Motif.Contract.Query.Phon;

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
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        QueryExpr? where;
        var limit = request.Limit ?? DefaultLimit;
        string segmentTest;
        if ((request.Where is null) == (request.QueryJson is null))
            return Invalid("Give the query as --where <expression> or as --query-json, one of the two.");
        if (limit < 1) return Invalid("limit is a positive whole number.");
        try
        {
            if (request.QueryJson is { } json)
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
        PhonSession? phon = null;
        if (QueryExprTools.HasPatterns(where))
        {
            var matcher = PhonMatchers.ForGrammar(request.GrammarPath!, parserPath);
            if (!matcher.Succeeded) return CommandOutcome<CorpusFormsResponse>.Refused(matcher.Refusal!);
            phon = new PhonSession(matcher.Value!, segmentTest: segmentTest);
        }
        try
        {
            var rows = CorpusFormsQuery.Run(corpus, where, phon);
            var summary = new CorpusSummary(request.CorpusPath, corpus.TypeCount, corpus.TokenCount,
                phon is { Ran: true } ? "grammar" : corpus.SegmentationLabel, request.FoldCase);
            return CommandOutcome<CorpusFormsResponse>.Success(new CorpusFormsResponse(CorpusFormsResponse.SchemaName, summary,
                where is null ? null : QueryExprText.Print(where), rows.Count, [.. rows.Take(limit)], phon is { Ran: true } ? phon.Report : null));
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

    private static CommandOutcome<CorpusFormsResponse> Invalid(string message) =>
        CommandOutcome<CorpusFormsResponse>.Refused(new Refusal(RefusalCodes.QueryCorpusInvalid, FailureReason.InvalidArgument, message));

    private static CommandOutcome<PairsQueryResponse> Refuse(string code, string message) =>
        CommandOutcome<PairsQueryResponse>.Refused(new Refusal(code, FailureReason.InvalidArgument, message));
}
