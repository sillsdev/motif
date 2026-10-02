namespace SIL.Motif.Contract.Jobs;

/// <summary>Completed searches and the word whose search has started in a sequential Trial batch.</summary>
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
