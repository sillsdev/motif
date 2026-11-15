using SIL.Motif.App.Controls;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>One rule's work for the traced word in an earlier parse, or work attributed to no object.</summary>
/// <param name="Rule">The rule's name, or what the row gathers.</param>
/// <param name="KindLabel">The rule's kind as the window names it; empty for a gathered row.</param>
/// <param name="TimeText">The parser work the earlier parse recorded.</param>
/// <param name="ShareText">Its share of the word's total work; empty when that total was not kept.</param>
/// <param name="IsOtherTime">Whether the work was not attributed to an object.</param>
public sealed record TryWordEarlierRuleTime(string Rule, string KindLabel, string TimeText, string ShareText,
    bool IsOtherTime)
{
    /// <summary>The recorded kind, absent for rows that gather several kinds or unattributed work.</summary>
    public string? KindTip => KindLabel.Length > 0 ? KindLabel : null;
}

/// <summary>One stored analysis, with its FieldWorks opinion rendered in the window's words.</summary>
public sealed class TryWordStoredAnalysis
{
    public TryWordStoredAnalysis(IReadOnlyList<ParserReading> readings, WordTraceReading? trace)
    {
        var reading = readings[0];
        Opinion = WindowWords.OpinionOf(reading.StoredAnalysisOpinion);
        HasMultipleOpinions = readings.Select(item => WindowWords.OpinionOf(item.StoredAnalysisOpinion)).Distinct().Count() > 1;
        OpinionLabel = readings.Count == 1 ? WindowWords.Of(Opinion) : string.Join(" · ", readings
            .GroupBy(item => WindowWords.OpinionOf(item.StoredAnalysisOpinion))
            .OrderBy(group => group.Key).Select(group => string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.TryWordOpinionCountFormat, WindowWords.Of(group.Key), group.Count())));
        Text = string.Join(" + ", reading.Morphs.Select(morph => morph.Form));
        Morphs = reading.Morphs.Select(morph => new ParserReadingMorphViewModel(morph)).ToArray();
        Pieces = reading.Morphs.Select((morph, index) => new TryWordTracePiece(
            readings.Select(item => item.Morphs[index]).ToArray(), trace)).ToArray();
    }

    public OpinionMarkKind Opinion { get; }
    public Mark OpinionMark => HasMultipleOpinions ? Mark.Of(MeaningTone.Neutral) : Mark.Of(Opinion);
    public bool HasMultipleOpinions { get; private set; }
    public string OpinionLabel { get; }
    public string Text { get; }
    public IReadOnlyList<ParserReadingMorphViewModel> Morphs { get; }
    public IReadOnlyList<TryWordTracePiece> Pieces { get; }
    public bool HasUnrecordedStep => Pieces.Any(piece => piece.HasNoRecordedStep);
}

/// <summary>A stored piece and only the trace events that name its exact authored identity.</summary>
public sealed class TryWordTracePiece
{
    public TryWordTracePiece(ParserReadingMorph morph, WordTraceReading? trace) : this([morph], trace) { }

    public TryWordTracePiece(IReadOnlyList<ParserReadingMorph> morphs, WordTraceReading? trace)
    {
        Morphs = [new ParserReadingMorphViewModel(morphs[0])];
        var statuses = morphs.Select(morph => StatusFor(morph, trace)).Distinct().ToArray();
        var status = statuses.Length == 1 ? statuses[0] : PieceStatus.Differ;
        HasNoRecordedStep = status == PieceStatus.NoStep;
        Status = status switch
        {
            PieceStatus.BuiltAndRefused => MotifAppStrings.TryWordPieceBuiltRefused,
            PieceStatus.Refused => MotifAppStrings.TraceStepRefused,
            PieceStatus.Built => MotifAppStrings.TraceStepBuilt,
            PieceStatus.Tried => MotifAppStrings.TryWordPieceTried,
            PieceStatus.NoStep => MotifAppStrings.NoStepRecorded,
            _ => MotifAppStrings.TryWordPieceDiffer,
        };
        StatusParts = status switch
        {
            PieceStatus.BuiltAndRefused =>
            [
                new(Mark.Of(TraceStepMark.Built), MotifAppStrings.TraceStepBuilt, string.Empty),
                new(Mark.Of(TraceStepMark.Refused), MotifAppStrings.TraceStepRefused, "· "),
            ],
            PieceStatus.Refused => [new(Mark.Of(TraceStepMark.Refused), Status, string.Empty)],
            PieceStatus.Built => [new(Mark.Of(TraceStepMark.Built), Status, string.Empty)],
            PieceStatus.NoStep => [],
            _ => [new(null, Status, string.Empty)],
        };
    }

    private enum PieceStatus { BuiltAndRefused, Refused, Built, Tried, NoStep, Differ }

    private static PieceStatus StatusFor(ParserReadingMorph morph, WordTraceReading? trace)
    {
        var steps = trace is null ? [] : Flatten(trace.Root).Where(step =>
            step.SourceIdentityQuality == "authored" && step.SourceIdentityKind == "morphRule" &&
            Guid.TryParse(step.SourceIdentityId, out var source) &&
            Guid.TryParse(morph.GrammaticalInfoId, out var msa) && source == msa).ToArray();
        var refusal = steps.FirstOrDefault(step => step.FailureReason is { Length: > 0 });
        var built = steps.Any(step => step.OutcomeStatus is "success" or "successful" or "succeeded");
        return built && refusal is not null ? PieceStatus.BuiltAndRefused : refusal is not null ? PieceStatus.Refused
            : built ? PieceStatus.Built : steps.Length > 0 ? PieceStatus.Tried : PieceStatus.NoStep;
    }

    public IReadOnlyList<ParserReadingMorphViewModel> Morphs { get; }
    public string Status { get; }
    /// <summary>Whether the trace holds no step for this piece, whatever words say so.</summary>
    public bool HasNoRecordedStep { get; }
    /// <summary>The status words and signs that form the visible line.</summary>
    public IReadOnlyList<TryWordTraceStatus> StatusParts { get; }

    private static IEnumerable<TraceStep> Flatten(TraceStep step)
    {
        yield return step;
        foreach (var child in step.Children)
            foreach (var descendant in Flatten(child)) yield return descendant;
    }
}

/// <summary>One trace status word, optionally preceded by its mark and a separator.</summary>
/// <param name="Mark">The sign for the status word, or <see langword="null"/> for plain text.</param>
/// <param name="Text">The status word.</param>
/// <param name="Separator">The visible separator before this status word.</param>
public sealed record TryWordTraceStatus(Mark? Mark, string Text, string Separator);

/// <summary>An opinion with its explicit FieldWorks source for the result line.</summary>
public sealed record TryWordOpinion(OpinionMarkKind Opinion, string Label)
{
    public Mark Mark => Mark.Of(Opinion);
}
