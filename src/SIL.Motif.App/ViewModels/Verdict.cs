namespace SIL.Motif.App.ViewModels;

/// <summary>
/// What a view is saying about a word, an occurrence or an attempt, as one of six meanings shared by every
/// stage, plus two for what the project itself holds, drawn in FieldWorks' own colours. Each view keeps its own
/// word for a meaning — Texts says "Approved" where Results says "Matches" — but the meaning decides the colour
/// and the glyph, so the same thing never looks different twice.
/// </summary>
public enum Verdict
{
    /// <summary>The parser and the project agree: Approved, Matches, or a successful attempt.</summary>
    Agrees,

    /// <summary>They contradict each other: Disapproved, Differs from stored, or an attempt a rule stopped.</summary>
    Differs,

    /// <summary>Something new to consider: nothing stored here, or a reading the project has no opinion on.</summary>
    New,

    /// <summary>Nothing came out: No parse, or an approved analysis the parser missed.</summary>
    NoResult,

    /// <summary>The search did not finish: a time or step limit, or a word left out of the run.</summary>
    Limit,

    /// <summary>Neutral: one spelling with several analyses, which is usually homographs.</summary>
    Several,

    /// <summary>
    /// The project approves an analysis of this word. Cyan, as FieldWorks colours a guess taken from an approved
    /// analysis, so a word looks the same here as in a FieldWorks text.
    /// </summary>
    Approved,

    /// <summary>
    /// The project holds a candidate analysis of this word: one nobody has approved or rejected. Tan, as FieldWorks
    /// colours a guess its parser offered.
    /// </summary>
    Candidate,
}

/// <summary>The glyph and style class each <see cref="Verdict"/> wears, so colour is never the only signal.</summary>
public static class Verdicts
{
    /// <summary>The style class a view puts on a control to colour it for <paramref name="verdict"/>.</summary>
    public static string ClassOf(Verdict verdict) => verdict switch
    {
        Verdict.Agrees => "agrees",
        Verdict.Differs => "differs",
        Verdict.New => "new",
        Verdict.NoResult => "noresult",
        Verdict.Limit => "limit",
        Verdict.Approved => "approved",
        Verdict.Candidate => "candidate",
        _ => "several",
    };

    /// <summary>The glyph shown beside the label, for readers who cannot tell the colours apart.</summary>
    public static string GlyphOf(Verdict verdict) => verdict switch
    {
        Verdict.Agrees => "✓",
        Verdict.Differs => "≠",
        Verdict.New => "+",
        Verdict.NoResult => "∅",
        Verdict.Limit => "⏱",
        Verdict.Approved => "✓",
        Verdict.Candidate => "?",
        _ => "◇",
    };

    /// <summary>What the key at the top of a view calls this meaning.</summary>
    public static string LegendOf(Verdict verdict) => verdict switch
    {
        Verdict.Agrees => "agrees",
        Verdict.Differs => "differs from stored",
        Verdict.New => "not stored yet",
        Verdict.NoResult => "no parse",
        Verdict.Limit => "stopped at a limit",
        Verdict.Approved => "approved in the project",
        Verdict.Candidate => "candidate",
        _ => "several analyses",
    };
}
