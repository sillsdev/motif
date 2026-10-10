using SIL.Motif.Host.Texts;
using SIL.Motif.Contract.Requests;

namespace SIL.Motif.Commands.SelectionReading;

/// <summary>One displayed page within a source line, including punctuation tokens.</summary>
public sealed record TextLinePage(int LineNumber, int TokenOffset, int TokenCount = 20);

/// <summary>A zero-based occurrence range with a bounded result size.</summary>
public sealed record OccurrenceRange(int Start = 0, int Count = 64)
{
    public const int DefaultCount = 64;
    public const int MaximumCount = 200;

    internal void Validate()
    {
        if (Start < 0 || Count < 0 || Count > MaximumCount)
            throw new ArgumentOutOfRangeException(nameof(Count), "Occurrence ranges require a non-negative start and at most 200 results.");
    }
}

/// <summary>A one-based Text line range with a bounded source-token window.</summary>
public sealed record TextLineRange(int StartLine = 1, int LineCount = 32, int TokenOffset = 0, int TokenLimit = 512)
{
    public const int DefaultLineCount = 32;
    public const int MaximumLineCount = 64;
    public const int DefaultTokenLimit = 512;
    public const int MaximumTokenLimit = 1024;

    internal void Validate()
    {
        if (StartLine < 1 || LineCount < 0 || LineCount > MaximumLineCount || TokenOffset < 0 ||
            TokenLimit < 0 || TokenLimit > MaximumTokenLimit)
            throw new ArgumentOutOfRangeException(nameof(LineCount), "Text ranges require a positive line, at most 64 lines and at most 1,024 tokens.");
    }
}

/// <summary>A word occurrence with its captured sentence and selected analysis context.</summary>
public sealed record WordOccurrenceDetail(
    SelectionOccurrenceLocation Location,
    string Sentence,
    string SentenceStyle,
    string? SentenceWritingSystem,
    TextWordsProjectedToken Token,
    TextWordsProjectedAnalysis? Analysis,
    TextWordsProjectedWordform? Wordform);

/// <summary>A bounded range of one word's captured occurrence evidence.</summary>
public sealed record WordOccurrences(
    TextWordKey Word,
    SelectionWordSummary? WordSummary,
    int Start,
    int TotalCount,
    IReadOnlyList<WordOccurrenceDetail> Occurrences)
{
    /// <summary>Captured analysis detail for each exact wordform represented by this spelling.</summary>
    public IReadOnlyDictionary<TextWordKey, TextWordsProjectedWordform> Wordforms { get; init; } =
        new Dictionary<TextWordKey, TextWordsProjectedWordform>();

    /// <summary>Baseline-wide spelling context when the requested word has no exact wordform identity.</summary>
    public SIL.Motif.Contract.Responses.WordContextResponse? WordContext { get; init; }

    /// <summary>Exact Selection wordform identities that share this spelling.</summary>
    public IReadOnlyList<TextWordKey> CandidateWordforms { get; init; } = [];
}

/// <summary>Read marks, the current Draft, and attributed grammar findings for a Selection.</summary>
public sealed record SelectionPresentationState(
    IReadOnlyDictionary<Guid, IReadOnlyList<OccurrenceAnchor>> ReadOccurrences,
    SIL.Motif.Contract.Responses.PendingChangesSnapshot PendingChanges,
    IReadOnlyList<SIL.Motif.Contract.Responses.GrammarWarning> Warnings)
{
    public IReadOnlyDictionary<TextWordKey, IReadOnlyList<SelectionPendingChange>> PendingByWord { get; init; } =
        new Dictionary<TextWordKey, IReadOnlyList<SelectionPendingChange>>();

    public IReadOnlyDictionary<TextWordKey, IReadOnlyList<SIL.Motif.Contract.Responses.GrammarWarning>> WarningsByWord { get; init; } =
        new Dictionary<TextWordKey, IReadOnlyList<SIL.Motif.Contract.Responses.GrammarWarning>>();
}

/// <summary>One Draft change and its fit, joined to an exact Selection word row.</summary>
public sealed record SelectionPendingChange(
    SIL.Motif.Contract.Responses.PendingChange Change,
    SIL.Motif.Contract.Responses.ChangeFit? Fit);

/// <summary>One source token and the analysis selected by its Text.</summary>
public sealed record TextLineToken(TextWordsProjectedToken Token, TextWordsProjectedAnalysis? Analysis);

/// <summary>One captured sentence token without analysis models, actions or references to an Analyze page.</summary>
public sealed record SelectionSentenceToken(string Text, string? TextWritingSystem, string? Form,
    OccurrenceAnchor? Occurrence);

/// <summary>A bounded plain sentence strip surrounding an exact occurrence, including unchecked Texts.</summary>
public sealed record SelectionSentenceContext(Guid TextId, string Title, int LineNumber, string SentenceStyle,
    int SourceTokenOffset, int SourceTokenCount, int TargetTokenOffset, IReadOnlyList<SelectionSentenceToken> Tokens);

/// <summary>Exact stored wordform detail for a bounded set of visible word rows.</summary>
public sealed record SelectionWordDetails(IReadOnlyDictionary<TextWordKey, TextWordsProjectedWordform> Wordforms);

/// <summary>A possibly partial source line retaining its captured sentence metadata.</summary>
public sealed record TextLineFragment(
    int LineNumber,
    Guid ParagraphId,
    Guid SegmentId,
    bool ParseIsCurrent,
    string Sentence,
    string SentenceStyle,
    string? SentenceWritingSystem,
    int SourceTokenOffset,
    int SourceTokenCount,
    IReadOnlyList<TextLineToken> Tokens);

/// <summary>A bounded set of lines and source tokens, with an explicit continuation position.</summary>
public sealed record TextLineSlice(
    Guid TextId,
    int StartLine,
    int LineCount,
    int TokenOffset,
    int TokenLimit,
    int TotalTokens,
    int ReturnedTokens,
    IReadOnlyList<TextLineFragment> Lines,
    IReadOnlyDictionary<TextWordKey, TextWordsProjectedWordform> Wordforms,
    IReadOnlyDictionary<string, SelectionAssessmentFacts> Assessments,
    int? NextLine,
    int? NextTokenOffset);

/// <summary>Compact parser facts for one word from its producing Assessment.</summary>
public sealed record SelectionAssessmentFacts(
    string AssessmentId,
    string Outcome,
    bool IsIncomplete,
    string? ProjectStanding,
    int? OccurrenceCount,
    IReadOnlyList<string>? ReadingGrades,
    SIL.Motif.Contract.Responses.WordMeasurementOrigin? Origin)
{
    public SIL.Motif.Contract.Responses.ParseWordEvidence? Morphology { get; init; }
    public SIL.Motif.Contract.Responses.WordAnalysisComparison? AnalysisComparison { get; init; }
    public int MissedApprovedCount { get; init; }
    public IReadOnlyList<SIL.Motif.Contract.Responses.ParserReading>? MissedApproved { get; init; }
    public SIL.Motif.Contract.Responses.WordComparison? Comparison { get; init; }
}

/// <summary>The kind of display object registered to the result that keeps it alive.</summary>
public enum SelectionModelKind
{
    Row,
    Line,
    Token,
    Card,
}

/// <summary>Whether a display object consumes the ordinary allowance or a retained pin.</summary>
public enum SelectionModelUse
{
    Ordinary,
    Pinned,
}

/// <summary>How a caller owns a reader result while it is displayed or prepared.</summary>
public enum SelectionReadPurpose
{
    Visible,
    Prefetch,
    Pin,
}

/// <summary>Owns a reader result and registrations for display objects created from it.</summary>
public sealed class SelectionReadLease : IDisposable
{
    private readonly object _gate = new();
    private readonly Action<SelectionModelKind, SelectionModelUse, bool> _register;
    private readonly SelectionReadPurpose _purpose;
    private Action? _release;
    private bool _disposed;

    internal SelectionReadLease(Action<SelectionModelKind, SelectionModelUse, bool> register,
        SelectionReadPurpose purpose, Action release)
    {
        _register = register;
        _purpose = purpose;
        _release = release;
    }

    /// <summary>The bounded ownership purpose assigned to this result.</summary>
    public SelectionReadPurpose Purpose => _purpose;

    /// <summary>Registers one display object created from this result until the registration is disposed.</summary>
    public IDisposable RegisterModel(SelectionModelKind kind, SelectionModelUse use = SelectionModelUse.Ordinary)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_purpose == SelectionReadPurpose.Prefetch)
                throw new InvalidOperationException("Prefetched Selection results cannot create display models.");
            _register(kind, use, true);
        }
        return new ModelRegistration(_register, kind, use);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Action? release;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            release = _release;
            _release = null;
        }
        release?.Invoke();
    }

    private sealed class ModelRegistration(
        Action<SelectionModelKind, SelectionModelUse, bool> register,
        SelectionModelKind kind,
        SelectionModelUse use) : IDisposable
    {
        private Action<SelectionModelKind, SelectionModelUse, bool>? _register = register;

        public void Dispose() => Interlocked.Exchange(ref _register, null)?.Invoke(kind, use, false);
    }
}
