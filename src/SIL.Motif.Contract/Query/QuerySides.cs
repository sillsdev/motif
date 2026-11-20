using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Query;

/// <summary>The names a query's sides, side statuses and parse states take.</summary>
public static class QuerySideNames
{
    /// <summary>The Baseline side: the project as captured before any Proposal.</summary>
    public const string Baseline = "baseline";
    /// <summary>The Proposal side: the Proposal's Trial copy, which carries the Proposal's grammar.</summary>
    public const string Proposal = "proposal";
    /// <summary>Both sides, side by side.</summary>
    public const string Both = "both";
    /// <summary>The side was read.</summary>
    public const string Ready = "ready";
    /// <summary>The side has not been run: the Proposal has no completed Trial.</summary>
    public const string Pending = "pending";
    /// <summary>The form is not a word of the Texts, so it has no parse state. It means no data, not a failed parse: a lexeme stem that is not itself a word of the Texts is absent even when its inflected words are parsed.</summary>
    public const string Absent = "absent";
    /// <summary>The word has an analysis a person approved.</summary>
    public const string Approved = "approved";
    /// <summary>The word has analyses and none is approved or fully disapproved.</summary>
    public const string Parsed = "parsed";
    /// <summary>The word has no analysis.</summary>
    public const string Unparsed = "unparsed";
    /// <summary>The Proposal has no Trial.</summary>
    public const string ReasonNoTrial = "no-trial";
    /// <summary>The Proposal's latest Trial has not completed.</summary>
    public const string ReasonTrialNotCompleted = "trial-not-completed";
    /// <summary>The project has no current Baseline.</summary>
    public const string ReasonNoBaseline = "no-baseline";
    /// <summary>The Trial's copy of the project was cleared.</summary>
    public const string ReasonTrialCleared = "trial-cleared";
    /// <summary>Every parse state a row's <c>parse.state</c> takes; a consumer should still tolerate an unknown one.</summary>
    public static readonly IReadOnlyList<string> ParseStates = [Approved, Parsed, Unparsed, Failed, Absent];
    /// <summary>Every side status a state takes.</summary>
    public static readonly IReadOnlyList<string> Statuses = [Ready, Pending];
    /// <summary>Every reason a side is not ready.</summary>
    public static readonly IReadOnlyList<string> Reasons = [ReasonNoTrial, ReasonTrialNotCompleted, ReasonNoBaseline, ReasonTrialCleared];
    /// <summary>The word has analyses, and a person has disapproved every one.</summary>
    public const string Failed = "failed";
}

/// <summary>One morph of a word's analysis, cut from the word and tied to its lexicon entry.</summary>
/// <param name="Form">The morph's form as cut from the word.</param>
/// <param name="Gloss">The gloss of the sense the morph is tied to; empty when it has none.</param>
/// <param name="Category">The part of speech of the morph's analysis; empty when it has none.</param>
/// <param name="Entry">The canonical id of the lexicon entry the morph belongs to; empty when the morph is not tied to one.</param>
public sealed record QueryMorph(string Form, string Gloss, string Category, string Entry);

/// <summary>One analysis of a word: its morphs in order.</summary>
/// <param name="Category">The analysis's part of speech; empty when it has none.</param>
/// <param name="Approved">Whether a person approved this analysis.</param>
/// <param name="Morphs">The morph cut, in word order.</param>
public sealed record QueryReading(string Category, bool Approved, IReadOnlyList<QueryMorph> Morphs);

/// <summary>The parse state and analyses of the word whose spelling is a row's form.</summary>
/// <param name="State">One of <see cref="QuerySideNames.ParseStates"/>: failed means every analysis is disapproved, absent means the form is no word of the Texts (no data, not a failed parse).</param>
/// <param name="Tokens">How many times the word occurs in the Texts; unlike the latest-Trial <c>tokens</c> column it counts the Texts of this side's own copy, and a form with no such word has 0.</param>
/// <param name="Analyses">The word's analyses under this side's grammar, empty when it has none.</param>
public sealed record QueryWordParse(string State, int Tokens, IReadOnlyList<QueryReading> Analyses);

/// <summary>One row as one side reads it: the form's columns and its word's parse.</summary>
public sealed record QuerySideRow(QueryForm Form, QueryWordParse Parse);

/// <summary>One form's row with each side's reading; a side not read, still pending, or lacking the form is null.</summary>
/// <param name="Id">The form's id, unique per row and the same on both sides for a form that exists on both. A variant row's id is the variant form's id, then '@', then the id of the main entry it varies.</param>
/// <param name="Baseline">The Baseline reading, or null.</param>
/// <param name="Proposal">The Proposal reading, or null.</param>
public sealed record QueryPairedRow(string Id, QuerySideRow? Baseline, QuerySideRow? Proposal);

/// <summary>Whether one side was read.</summary>
/// <param name="Side">baseline or proposal.</param>
/// <param name="Status">One of <see cref="QuerySideNames.Statuses"/>.</param>
/// <param name="Source">What was read: baseline, or the Proposal id.</param>
/// <param name="TrialId">The Trial whose copy was read; null for the Baseline and for a pending side with no Trial.</param>
/// <param name="Reason">Why the side is pending, one of <see cref="QuerySideNames.Reasons"/>; null when it is ready.</param>
public sealed record QuerySideState(string Side, string Status, string Source, string? TrialId, string? Reason);

/// <summary>The sides of a query run with <c>--as-of</c>.</summary>
/// <param name="Version">The shape version, <see cref="CurrentVersion"/>; it rises when a property changes meaning or is removed.</param>
/// <param name="Mode">baseline, proposal or both.</param>
/// <param name="States">One state per side the query asked for.</param>
/// <param name="Rows">The matching rows, sorted; empty when the query grouped, or when no side was read.</param>
public sealed record QuerySidesResult(int Version, string Mode, IReadOnlyList<QuerySideState> States, IReadOnlyList<QueryPairedRow> Rows)
{
    /// <summary>The shape version this build writes.</summary>
    public const int CurrentVersion = 1;
}
