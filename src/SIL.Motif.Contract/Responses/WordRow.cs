using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Responses;

/// <summary>What PanGloss built for a word, against what FieldWorks holds for it: one of the Matrix's columns.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<WordRowOutcome>))]
public enum WordRowOutcome
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

/// <summary>The tone of what an opinion and an outcome mean together; one tone for each group of meanings.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<WordRowTone>))]
public enum WordRowTone
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

/// <summary>Whether a word row includes PanGloss's resolved morpheme details.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<WordRowReadingAvailability>))]
public enum WordRowReadingAvailability
{
    /// <summary>The query includes resolved details; an empty list can be a recorded no-reading result.</summary>
    [JsonStringEnumMemberName("included")]
    Included,

    /// <summary>The query deliberately omits resolved details, so an empty list does not mean no reading exists.</summary>
    [JsonStringEnumMemberName("not_requested")]
    NotRequested,
}

/// <summary>
/// One word as every page's word row shows it: what FieldWorks holds, what PanGloss built, what the two mean
/// together, and where the word occurs. Every page builds its row from the same projection, so a word reads the
/// same on the Matrix, in Lists and on Timing.
/// </summary>
/// <param name="Word">The word form, as the parser was asked about it.</param>
/// <param name="Outcome">What PanGloss built, from the same placement the Matrix uses.</param>
/// <param name="Meaning">What the opinion and the outcome mean together, in the Matrix's words, such as <c>Lost</c>.</param>
/// <param name="Tone">The tone that <paramref name="Meaning"/> takes.</param>
public sealed record WordRow(string Word, WordRowOutcome Outcome, string Meaning, WordRowTone Tone)
{
    /// <summary>The stable comparison identity used for grouping, independent of Meaning's wording.</summary>
    public string MeaningCode { get; init; } = "not-parsed";

    /// <summary>The full comparison evidence used to place and describe this word, when projected.</summary>
    public WordComparison? Comparison { get; init; }

    /// <summary>A second line qualifying the headline, or empty when none is needed.</summary>
    public string MeaningDetail { get; init; } = string.Empty;

    /// <summary>Whether <see cref="PanGlossMorphemes"/> contains resolved parser details.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public WordRowReadingAvailability PanGlossReadingAvailability { get; init; }

    /// <summary>The FieldWorks analysis's glosses, morph by morph, or empty when FieldWorks holds none.</summary>
    public string Gloss { get; init; } = string.Empty;

    /// <summary>
    /// What FieldWorks holds for the word, as a <see cref="ProjectStanding"/> value, or <see langword="null"/> when
    /// the project's analyses were not read.
    /// </summary>
    public string? Opinion { get; init; }

    /// <summary>The canonical ID of the FieldWorks analysis the row shows, or <see langword="null"/> when it shows none.</summary>
    public string? FieldWorksAnalysisId { get; init; }

    /// <summary>
    /// The morphemes of the analysis FieldWorks holds for the word: the approved one, else its only analysis; empty
    /// when it holds none or more than one without an approved one.
    /// </summary>
    public IReadOnlyList<ParserReadingMorph> FieldWorksMorphemes { get; init; } = [];

    /// <summary>
    /// The morphemes of the PanGloss reading closest to <see cref="FieldWorksMorphemes"/>, shown only when the
    /// outcome is <see cref="WordRowOutcome.Different"/>; empty otherwise.
    /// </summary>
    public IReadOnlyList<ParserReadingMorph> PanGlossMorphemes { get; init; } = [];

    /// <summary>
    /// The positions in <see cref="PanGlossMorphemes"/>, counting from one, of the morphemes FieldWorks' analysis does
    /// not have in that order. Morphemes are compared by their allomorph and grammatical info, never by spelling, so
    /// a morpheme with no identity always differs. Empty when FieldWorks holds no analysis to compare with.
    /// </summary>
    public IReadOnlyList<int> DifferingPositions { get; init; } = [];

    /// <summary>How many readings PanGloss built for the word.</summary>
    public int PanGlossReadingCount { get; init; }

    /// <summary>How many places in the chosen Texts the word occurs, or <see langword="null"/> when not known.</summary>
    public int? Places { get; init; }

    /// <summary>How long the parser took over the word, or <see langword="null"/> when it was not measured.</summary>
    public int? ElapsedMs { get; init; }
    /// <summary>The producing measurement, independent of the other words shown beside this one.</summary>
    public WordMeasurementOrigin? Origin { get; init; }

    /// <summary>
    /// Whether some place the word occurs has not been marked read, or <see langword="null"/> when its read state is
    /// not known.
    /// </summary>
    public bool? IsUnread { get; init; }

    /// <summary>
    /// How many grammar warnings name something this word's analysis uses, or <see langword="null"/> while warnings
    /// are not joined to words.
    /// </summary>
    public int? WarningsNamed { get; init; }

    /// <summary>
    /// A <c>silfw:</c> link selecting the word in FieldWorks' Word Analyses, or <see langword="null"/> when the
    /// project has no wordform spelled this way.
    /// </summary>
    public string? WordAnalysesLink { get; init; }
}
