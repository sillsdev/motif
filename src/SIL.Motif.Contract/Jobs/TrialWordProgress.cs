namespace SIL.Motif.Contract.Jobs;

/// <summary>Completed searches and the latest started word that has not completed in a batch.</summary>
/// <param name="Completed">The number of distinct word results received so far.</param>
/// <param name="Total">The number of words requested for the batch.</param>
/// <param name="CurrentWord">The latest started unfinished word observed, or <see langword="null"/> if none remain.</param>
public sealed record TrialWordProgress(int Completed, int Total, string? CurrentWord)
{
    public IReadOnlyList<StoppedParseWord> StoppedWords { get; init; } = Array.Empty<StoppedParseWord>();
    public ParseWordTiming? SlowestWord { get; init; }
    public int? PerWordLimitMs { get; init; }
}

/// <summary>A completed word's measured search time, in milliseconds.</summary>
public sealed record ParseWordTiming(string Word, double ElapsedMs);

/// <summary>A flushed word result whose search stopped at a declared limit.</summary>
public sealed record StoppedParseWord(string Word, string Reason, double ElapsedMs);
