using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Controls;

namespace SIL.Motif.App.Controls.WordPresentation;

/// <summary>A stable host supplied identity for one word presentation.</summary>
public readonly record struct WordPresentationKey(string Value)
{
    /// <summary>Validates that the host supplied a stable, nonempty identity.</summary>
    public WordPresentationKey Validate() => string.IsNullOrWhiteSpace(Value)
        ? throw new ArgumentException("A word presentation needs a stable key.", nameof(Value))
        : this;
}

/// <summary>The list family that owns navigation and keyboard actions for a word.</summary>
public enum WordListOwner
{
    WordList,
    Lists,
    Matrix,
    FixFirst,
    Review,
    Timing,
    WhatChanged,
    Warnings,
    ReadOnly,
}

/// <summary>Typed facts and list-owned notes for one row.</summary>
public sealed record WordPresentation(
    WordPresentationKey Key,
    long EvidenceRevision,
    WordRowViewModel Facts,
    WordListOwner Owner,
    string? StagedText = null,
    string? Note = null,
    string? TimeText = null,
    string? MeasuredText = null,
    WordPendingChange? PendingChange = null)
{
    /// <summary>Checks the identity and evidence revision before the row is attached.</summary>
    public WordPresentation Validate()
    {
        Key.Validate();
        ArgumentNullException.ThrowIfNull(Facts);
        if (EvidenceRevision < 0) throw new ArgumentOutOfRangeException(nameof(EvidenceRevision));
        return this;
    }
}

/// <summary>Persistent interaction state keyed to the word item rather than a recycled control.</summary>
public sealed record WordInteractionState(
    WordPresentationKey Key,
    bool IsOpen = false,
    bool? IsChecked = false,
    bool IsSelected = false);

/// <summary>The semantic action a row asks its list host to perform.</summary>
public enum WordAction
{
    OpenCard,
    CloseCard,
    OpenInText,
    TryWord,
    OpenAnalyses,
    SetOpinion,
    ToggleRead,
    NavigatePrevious,
    NavigateNext,
    NavigatePreviousLine,
    NavigateNextLine,
    NavigateCardPrevious,
    NavigateCardNext,
    NavigatePreviousPage,
    NavigateNextPage,
    NavigateFirst,
    NavigateLast,
    OpenMorphology,
    UndoChange,
    ReconfirmChange,
}

/// <summary>A typed action request carrying the word identity and evidence revision.</summary>
public sealed record WordRequest(
    WordPresentationKey Key,
    long EvidenceRevision,
    WordAction Action,
    OpinionMarkKind? Opinion = null,
    MorphologyProducer? Producer = null);

/// <summary>The result a host returns after handling an action.</summary>
public sealed record WordActionResult(bool Handled, string? StatusMessage = null);

/// <summary>A heading section rendered by the module-owned card.</summary>
public sealed record WordCardHeading(string Text) : WordCardSection;

/// <summary>A separately tagged text section rendered by the module-owned card.</summary>
public sealed record WordCardText(string Heading, string Text, string? WritingSystem = null) : WordCardSection;

/// <summary>A morphology section whose forms and glosses remain independently selectable.</summary>
public sealed record WordCardMorphology(
    string Heading,
    IReadOnlyList<ParserReadingMorphViewModel> Morphs,
    IReadOnlyList<MorphemePanelAnnotation>? Annotations = null) : WordCardSection;

/// <summary>The comparison and occurrence evidence shown in a word's detailed card.</summary>
public sealed record WordCardAnalysis(
    CompareWordViewModel? Analysis,
    ResultsTokenViewModel? Token,
    string? NotParsedText = null) : WordCardSection;

/// <summary>Places where an Analyze texts word occurs, kept as typed sentence rows.</summary>
public sealed record WordCardOccurrences(IReadOnlyList<WordOccurrenceRowViewModel> Occurrences,
    object? Source = null) : WordCardSection
{
    /// <summary>The bounded occurrence page source, or the supplied finite rows.</summary>
    public object DisplaySource => Source ?? Occurrences;
}

/// <summary>A pending Review change and its sentence context, rendered inside the shared word card.</summary>
public sealed record WordCardPendingChange(
    string Reading,
    bool HasAnalyses,
    bool HasMorphs,
    bool IsParserBuilt,
    IReadOnlyList<ParserReadingMorphViewModel> Morphs,
    bool IsUncertain,
    IReadOnlyList<WordCardSentenceToken> BeforeWords,
    IReadOnlyList<WordCardSentenceToken> ContextTokens,
    bool HasUnavailableContext,
    WorkspaceContext Context,
    SIL.Motif.App.Services.IProgressivePageSource? ContextSource = null) : WordCardSection;

/// <summary>A sentence word with its writing system and whether Review marks it as changed.</summary>
public sealed record WordCardSentenceToken(string Form, string? WritingSystem, bool IsChanged);

/// <summary>The pending-change actions and status displayed on a Review word row.</summary>
public sealed record WordPendingChange(
    bool IsUncertain,
    bool StillFits,
    IReadOnlyList<WordCardSentenceToken> AfterWords,
    string ReconfirmAutomationName,
    string UndoAutomationName);

/// <summary>A typed section in a word card.</summary>
public abstract record WordCardSection;

/// <summary>The complete typed evidence document returned for one identity and revision.</summary>
public sealed record WordCardDocument(
    WordPresentationKey Key,
    long EvidenceRevision,
    IReadOnlyList<WordCardSection> Sections);

/// <summary>The outcome of an on-open evidence read.</summary>
public sealed record WordCardReadResult(
    WordCardDocument? Document,
    string? Failure,
    bool CanRetry)
{
    /// <summary>Creates a successful read for the requested identity and revision.</summary>
    public static WordCardReadResult Read(WordCardDocument document) => new(document, null, false);

    /// <summary>Creates a visible failure result without treating cancellation as an error.</summary>
    public static WordCardReadResult Failed(string message, bool canRetry = true) => new(null, message, canRetry);
}

/// <summary>Actions and evidence reads owned by the page or list that presents a word.</summary>
public interface IWordPresentationHost
{
    /// <summary>Reads card evidence only after the row opens.</summary>
    Task<WordCardReadResult> ReadCardAsync(
        WordPresentationKey key,
        long evidenceRevision,
        CancellationToken cancellationToken);

    /// <summary>Handles an action without exposing the host's controls or command objects.</summary>
    ValueTask<WordActionResult> HandleAsync(WordRequest request, CancellationToken cancellationToken);
}
