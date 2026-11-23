using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Query.Corpus;

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

    private static CommandOutcome<PairsQueryResponse> Refuse(string code, string message) =>
        CommandOutcome<PairsQueryResponse>.Refused(new Refusal(code, FailureReason.InvalidArgument, message));
}
