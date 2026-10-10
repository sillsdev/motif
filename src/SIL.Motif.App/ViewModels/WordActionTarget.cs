using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>Freezes a word action's identity, source context and producing Assessment before an asynchronous write.</summary>
public sealed record WordActionTarget(
    string Form,
    Guid? WordformId,
    OccurrenceAnchor? Occurrence,
    string? AssessmentId,
    ExpectedContext? ExpectedContext = null)
{
    /// <summary>The missing parser readings captured with an Accept new set choice.</summary>
    public IReadOnlyList<WordActionReading> ParserOnlyReadings { get; init; } = [];
}

/// <summary>A parser reading and its Assessment position, without a displayed token or reading model.</summary>
public sealed record WordActionReading(ParseAnalysis? Analysis, string? Text, int? Index);

/// <summary>A pending action captured from a word card, including the readings selected before the write starts.</summary>
public sealed record WordChangeAction(
    WordActionTarget Target, string Kind, IReadOnlyList<WordActionReading> Readings, bool IsAvailable);

/// <summary>A Fix-menu choice bound to the word and evidence that supplied the menu.</summary>
public sealed record WordMarkingChoice(WordActionTarget Target, AnalysisMarkingChoice Choice, bool IsAvailable)
{
    public string Label => Choice.Label;
    public string Subtitle => Choice.Subtitle;
}
