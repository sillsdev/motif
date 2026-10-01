using SIL.Motif.App.Controls;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// What PanGloss built for a word, against what FieldWorks holds for it: the Matrix's five columns. An outcome alone
/// never says whether anything is wrong; that is the <see cref="MeaningTone"/> of the outcome and the opinion together.
/// </summary>
public enum ParserOutcome
{
    /// <summary>PanGloss built what FieldWorks holds.</summary>
    Same,

    /// <summary>PanGloss built something else.</summary>
    Different,

    /// <summary>The search finished and built nothing.</summary>
    NoParse,

    /// <summary>The search reached its step or time limit, so the result is unknown.</summary>
    Stopped,

    /// <summary>The word has not been parsed, or was skipped.</summary>
    NotParsed,
}

/// <summary>
/// The tone of what an opinion and an outcome mean together, one for each group of the Matrix's meanings. It is the
/// only mark that fills or edges a box.
/// </summary>
public enum MeaningTone
{
    /// <summary>Kept, PanGloss confirms, Fine, Correct.</summary>
    Fine,

    /// <summary>Differs: have a look, New: PanGloss proposes, Grammar can't build it, and the like.</summary>
    Look,

    /// <summary>Lost, Built something else, Built anyway.</summary>
    Problem,

    /// <summary>Unknown yet, Not parsed, Nobody can analyze, Nothing to compare.</summary>
    Neutral,
}

/// <summary>How one attempt in a Try a Word trace ended, on FieldWorks' green and red path.</summary>
public enum TraceStepMark
{
    /// <summary>The attempt built the word.</summary>
    Built,

    /// <summary>A rule refused the attempt.</summary>
    Refused,

    /// <summary>The attempt was tried and neither built the word nor was refused.</summary>
    Tried,
}

/// <summary>The question a mark answers. No two kinds share a glyph.</summary>
public enum MarkKind
{
    /// <summary>What FieldWorks holds: Approved, Disapproved, Unknown, or nothing.</summary>
    Opinion,

    /// <summary>What PanGloss built: a <see cref="ParserOutcome"/>.</summary>
    Outcome,

    /// <summary>What an opinion and an outcome mean together: a <see cref="MeaningTone"/>.</summary>
    Meaning,

    /// <summary>What the grammar check says: an error, a warning or a note.</summary>
    Severity,

    /// <summary>How an attempt in a trace ended: a <see cref="TraceStepMark"/>.</summary>
    TraceStep,
}

/// <summary>
/// One mark as a view draws it: its kind, the style class of its value, its glyph and its word. A view puts the
/// <see cref="KindClass"/> and <see cref="Value"/> classes on a control and the colour follows from the tokens, so
/// the same thing never looks different twice.
/// </summary>
/// <param name="Kind">The question this mark answers.</param>
/// <param name="Value">The style class for its value, such as <c>same</c> or <c>approved</c>.</param>
/// <param name="Glyph">The sign beside its word, for readers who cannot tell the colours apart; empty for none.</param>
/// <param name="Word">The window's word for it.</param>
public sealed record Mark(MarkKind Kind, string Value, string Glyph, string Word)
{
    /// <summary>The FieldWorks opinion a <see cref="MarkKind.Opinion"/> mark shows; otherwise <see langword="null"/>.</summary>
    public OpinionMarkKind? Opinion { get; init; }

    /// <summary>The style class naming the mark's kind, such as <c>outcome</c>.</summary>
    public string KindClass => Kind switch
    {
        MarkKind.Opinion => "opinion",
        MarkKind.Outcome => "outcome",
        MarkKind.Meaning => "meaning",
        MarkKind.Severity => "severity",
        _ => "step",
    };

    public static Mark Approved { get; } = Of(OpinionMarkKind.Approved);
    public static Mark Disapproved { get; } = Of(OpinionMarkKind.Disapproved);
    public static Mark Unknown { get; } = Of(OpinionMarkKind.Unknown);
    public static Mark NotInFieldWorks { get; } = Of(OpinionMarkKind.None);
    public static Mark Same { get; } = Of(ParserOutcome.Same);
    public static Mark Different { get; } = Of(ParserOutcome.Different);
    public static Mark NoParse { get; } = Of(ParserOutcome.NoParse);
    public static Mark Stopped { get; } = Of(ParserOutcome.Stopped);
    public static Mark NotParsed { get; } = Of(ParserOutcome.NotParsed);
    public static Mark Error { get; } = Of(GrammarDiagnosticLevel.Error);
    public static Mark Warning { get; } = Of(GrammarDiagnosticLevel.Warning);
    public static Mark Information { get; } = Of(GrammarDiagnosticLevel.Information);

    /// <summary>The mark for a FieldWorks opinion: its letter in a box, or a dashed empty box for none.</summary>
    public static Mark Of(OpinionMarkKind opinion) => new(MarkKind.Opinion, opinion switch
    {
        OpinionMarkKind.Approved => "approved",
        OpinionMarkKind.Disapproved => "disapproved",
        OpinionMarkKind.Unknown => "unknown",
        _ => "none",
    }, MarkGlyphs.Of(opinion), WindowWords.Of(opinion)) { Opinion = opinion };

    /// <summary>The mark for what PanGloss built: a sign and a word in coloured text.</summary>
    public static Mark Of(ParserOutcome outcome) => new(MarkKind.Outcome, outcome switch
    {
        ParserOutcome.Same => "same",
        ParserOutcome.Different => "different",
        ParserOutcome.NoParse => "noParse",
        ParserOutcome.Stopped => "stopped",
        _ => "notParsed",
    }, MarkGlyphs.Of(outcome), WindowWords.Of(outcome));

    /// <summary>The mark for a meaning's tone, which has a word and a fill but no glyph.</summary>
    public static Mark Of(MeaningTone tone) => new(MarkKind.Meaning, tone switch
    {
        MeaningTone.Fine => "fine",
        MeaningTone.Look => "look",
        MeaningTone.Problem => "problem",
        _ => "neutral",
    }, string.Empty, WindowWords.Of(tone));

    /// <summary>The mark for a grammar-check level.</summary>
    public static Mark Of(GrammarDiagnosticLevel level) => new(MarkKind.Severity, level switch
    {
        GrammarDiagnosticLevel.Error => "error",
        GrammarDiagnosticLevel.Warning => "warning",
        _ => "info",
    }, GrammarLevelMarks.Of(level), WindowWords.Of(level));

    /// <summary>The mark for how a trace attempt ended.</summary>
    public static Mark Of(TraceStepMark step) => new(MarkKind.TraceStep, step switch
    {
        TraceStepMark.Built => "built",
        TraceStepMark.Refused => "refused",
        _ => "tried",
    }, MarkGlyphs.Of(step), WindowWords.Of(step));

    /// <summary>Every mark of <paramref name="kind"/>, one for each value.</summary>
    public static IReadOnlyList<Mark> AllOf(MarkKind kind) => kind switch
    {
        MarkKind.Opinion => Enum.GetValues<OpinionMarkKind>().Select(Of).ToArray(),
        MarkKind.Outcome => Enum.GetValues<ParserOutcome>().Select(Of).ToArray(),
        MarkKind.Meaning => Enum.GetValues<MeaningTone>().Select(Of).ToArray(),
        MarkKind.Severity => Enum.GetValues<GrammarDiagnosticLevel>().Select(Of).ToArray(),
        _ => Enum.GetValues<TraceStepMark>().Select(Of).ToArray(),
    };
}

/// <summary>One kind of mark's glyphs, as <see cref="MarkGlyphs.All"/> lists them.</summary>
/// <param name="Kind">The kind of mark that owns these glyphs.</param>
/// <param name="Glyphs">Every glyph that kind draws.</param>
public sealed record MarkGlyphTable(MarkKind Kind, IReadOnlyList<string> Glyphs);

/// <summary>
/// Each kind of mark's own glyphs. A glyph belongs to one kind only: ✓ is a trace step, never a parser outcome, and
/// the triangle is a warning, never a difference.
/// </summary>
public static class MarkGlyphs
{
    /// <summary>The opinion's letter; a missing analysis has none and is drawn as a dashed box.</summary>
    public static string Of(OpinionMarkKind opinion) => opinion switch
    {
        OpinionMarkKind.Approved => "A",
        OpinionMarkKind.Disapproved => "D",
        OpinionMarkKind.Unknown => "U",
        _ => string.Empty,
    };

    /// <summary>The outcome's sign.</summary>
    public static string Of(ParserOutcome outcome) => outcome switch
    {
        ParserOutcome.Same => "=",
        ParserOutcome.Different => "≠",
        ParserOutcome.NoParse => "∅",
        ParserOutcome.Stopped => "◐",
        _ => "–",
    };

    /// <summary>The trace step's sign.</summary>
    public static string Of(TraceStepMark step) => step switch
    {
        TraceStepMark.Built => "✓",
        TraceStepMark.Refused => "✗",
        _ => "·",
    };

    /// <summary>Every kind's glyphs, so a test can prove no two kinds share one.</summary>
    public static IReadOnlyList<MarkGlyphTable> All { get; } =
    [
        new(MarkKind.Opinion, Glyphs(Enum.GetValues<OpinionMarkKind>().Select(Of))),
        new(MarkKind.Outcome, Glyphs(Enum.GetValues<ParserOutcome>().Select(Of))),
        new(MarkKind.Meaning, []),
        new(MarkKind.Severity, Glyphs(Enum.GetValues<GrammarDiagnosticLevel>().Select(GrammarLevelMarks.Of))),
        new(MarkKind.TraceStep, Glyphs(Enum.GetValues<TraceStepMark>().Select(Of))),
    ];

    private static string[] Glyphs(IEnumerable<string> glyphs) => glyphs.Where(glyph => glyph.Length > 0).ToArray();
}

/// <summary>
/// The window's one set of words for marks (ADR 0046, ADR 0049): every outcome word, every capitalised opinion
/// label, and the meaning an opinion and an outcome have together. A view takes its words from here rather than
/// spelling them, so Analyze texts, the Matrix and Lists never disagree.
/// </summary>
public static class WindowWords
{
    /// <summary>The outcome's word: Same, Different, No parse, Stopped or Not parsed.</summary>
    public static string Of(ParserOutcome outcome) => outcome switch
    {
        ParserOutcome.Same => "Same",
        ParserOutcome.Different => "Different",
        ParserOutcome.NoParse => "No parse",
        ParserOutcome.Stopped => "Stopped",
        _ => "Not parsed",
    };

    /// <summary>The opinion in FieldWorks' own words.</summary>
    public static string Of(OpinionMarkKind opinion) => opinion switch
    {
        OpinionMarkKind.Approved => "Approved",
        OpinionMarkKind.Disapproved => "Disapproved",
        OpinionMarkKind.Unknown => "Unknown",
        _ => "Not in FieldWorks",
    };

    /// <summary>The tone's word: Fine, Have a look, Problem or Unknown yet.</summary>
    public static string Of(MeaningTone tone) => tone switch
    {
        MeaningTone.Fine => "Fine",
        MeaningTone.Look => "Have a look",
        MeaningTone.Problem => "Problem",
        _ => "Unknown yet",
    };

    /// <summary>The grammar-check level's word.</summary>
    public static string Of(GrammarDiagnosticLevel level) => level switch
    {
        GrammarDiagnosticLevel.Error => "Error",
        GrammarDiagnosticLevel.Warning => "Warning",
        _ => "Information",
    };

    /// <summary>The trace step's word.</summary>
    public static string Of(TraceStepMark step) => step switch
    {
        TraceStepMark.Built => "built",
        TraceStepMark.Refused => "refused",
        _ => "tried",
    };

    /// <summary>
    /// The opinion behind a stored analysis's <see cref="ReadingGrade"/>; a stored analysis nobody judged is
    /// Unknown, and anything that is not a grade is not in FieldWorks.
    /// </summary>
    public static OpinionMarkKind OpinionOf(string? grade) => grade switch
    {
        ReadingGrade.Approved => OpinionMarkKind.Approved,
        ReadingGrade.Disapproved => OpinionMarkKind.Disapproved,
        null or ReadingGrade.Candidate or ReadingGrade.NoOpinion => OpinionMarkKind.Unknown,
        _ => OpinionMarkKind.None,
    };

    /// <summary>The opinion a Matrix row stands for; an incorrect spelling is not an opinion and reads as none.</summary>
    public static OpinionMarkKind OpinionOf(WordProjectStatus row) => row switch
    {
        WordProjectStatus.Approved => OpinionMarkKind.Approved,
        WordProjectStatus.Candidate => OpinionMarkKind.Unknown,
        WordProjectStatus.Rejected => OpinionMarkKind.Disapproved,
        _ => OpinionMarkKind.None,
    };

    /// <summary>The label a Matrix row shows: its opinion, or FieldWorks' own "Incorrect spelling".</summary>
    public static string LabelOf(WordProjectStatus row) =>
        row == WordProjectStatus.IncorrectSpelling ? "Incorrect spelling" : Of(OpinionOf(row));

    /// <summary>The outcome a Matrix column stands for.</summary>
    public static ParserOutcome OutcomeOf(CompareColumnKind column) => column switch
    {
        CompareColumnKind.Match => ParserOutcome.Same,
        CompareColumnKind.NoMatch => ParserOutcome.Different,
        CompareColumnKind.NoParse => ParserOutcome.NoParse,
        CompareColumnKind.Timeout => ParserOutcome.Stopped,
        _ => ParserOutcome.NotParsed,
    };

    /// <summary>The Matrix column an outcome stands for.</summary>
    public static CompareColumnKind ColumnOf(ParserOutcome outcome) => outcome switch
    {
        ParserOutcome.Same => CompareColumnKind.Match,
        ParserOutcome.Different => CompareColumnKind.NoMatch,
        ParserOutcome.NoParse => CompareColumnKind.NoParse,
        ParserOutcome.Stopped => CompareColumnKind.Timeout,
        _ => CompareColumnKind.Skipped,
    };

    /// <summary>
    /// The outcome behind one occurrence's comparison: a conflict with an opinion, or readings beyond the approved
    /// ones, is still PanGloss building something different.
    /// </summary>
    public static ParserOutcome OutcomeOf(AnalysisMarkingClass marking) => marking switch
    {
        AnalysisMarkingClass.Same => ParserOutcome.Same,
        AnalysisMarkingClass.Conflict or AnalysisMarkingClass.Different or AnalysisMarkingClass.Extra =>
            ParserOutcome.Different,
        AnalysisMarkingClass.None => ParserOutcome.NoParse,
        AnalysisMarkingClass.Capped => ParserOutcome.Stopped,
        _ => ParserOutcome.NotParsed,
    };

    /// <summary>The tone each of the Matrix's families takes.</summary>
    public static MeaningTone ToneOf(CompareFamilyKind family) => family switch
    {
        CompareFamilyKind.Good or CompareFamilyKind.Fine => MeaningTone.Fine,
        CompareFamilyKind.Review or CompareFamilyKind.New => MeaningTone.Look,
        CompareFamilyKind.Violation => MeaningTone.Problem,
        _ => MeaningTone.Neutral,
    };

    /// <summary>
    /// What an opinion and an outcome mean together, in the Matrix's words, and the tone that colours it. The words
    /// come from <see cref="CompareSemantics.MeaningOf"/>, which the CLI shares.
    /// </summary>
    /// <param name="standing">The <see cref="ProjectStanding"/> wire value of what FieldWorks holds.</param>
    /// <param name="outcome">What PanGloss built.</param>
    public static (string Word, MeaningTone Tone) MeaningOf(string? standing, ParserOutcome outcome)
    {
        var (word, family) = CompareSemantics.MeaningOf(standing, ColumnOf(outcome));
        return (word, ToneOf(family));
    }
}
