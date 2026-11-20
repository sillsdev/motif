using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Query;

namespace SIL.Motif.App.GuideQueries;

/// <summary>One morph of a word's analysis, cut from the word and tied to its lexicon entry.</summary>
public sealed record GuideQueryMorph(string Form, string Gloss, string Category, string Entry);

/// <summary>One analysis of a word: its morphs in order.</summary>
public sealed record GuideQueryReading(string Category, bool Approved, IReadOnlyList<GuideQueryMorph> Morphs)
{
    /// <summary>The morph cut written as one string, such as <c>ka-onan</c>.</summary>
    public string Cut => string.Join("-", Morphs.Select(morph => morph.Form));

    /// <summary>The glosses of the cut's morphs joined the same way; empty when no morph has one.</summary>
    public string Glosses => Morphs.Any(morph => morph.Gloss.Length > 0) ? string.Join("-", Morphs.Select(morph => morph.Gloss)) : "";
}

/// <summary>One side's reading of a row: the form and the parse of the word spelled like it.</summary>
public sealed record GuideQueryWord(string Id, string Form, string Base, string Kind, string Entry, IReadOnlyList<string> Category,
    long? Tokens, string ParseState, int ParseTokens, IReadOnlyList<GuideQueryReading> Analyses);

/// <summary>Whether one side of the run was read, and why not when it was not.</summary>
public sealed record GuideQuerySideState(string Side, string Status, string Source, string? TrialId, string? Reason)
{
    /// <summary>Whether the side has not been run yet.</summary>
    public bool IsPending => Status != "ready";
}

/// <summary>One row with each side's reading; a side not read, pending, or lacking the form is null.</summary>
public sealed record GuideQueryRow(string Id, GuideQueryWord? Baseline, GuideQueryWord? Proposal);

/// <summary>One group of a grouped query: its key columns and how many forms it holds.</summary>
public sealed record GuideQueryGroup(string Key, int Count);

/// <summary>What one run of a Guide query returned; it is shown and dropped, never saved.</summary>
public sealed record GuideQueryRun(string Mode, IReadOnlyList<GuideQuerySideState> States, IReadOnlyList<GuideQueryRow> Rows,
    IReadOnlyList<GuideQueryGroup> Groups)
{
    /// <summary>The shape version of <c>sides</c> this reader understands.</summary>
    public const int SupportedVersion = 1;

    /// <summary>The state of <paramref name="side"/> (<c>baseline</c> or <c>proposal</c>), or null when the run did not ask for it.</summary>
    public GuideQuerySideState? StateOf(string side) => States.FirstOrDefault(state => state.Side == side);

    /// <summary>Reads the answer of a <c>motif query forms --as-of</c> run; the text is the message when it cannot be read.</summary>
    public static (GuideQueryRun? Run, string? Problem) From(FormsQueryResponse? response)
    {
        if (response is null) return (null, MotifAppStrings.GuideQueryNoAnswer);
        var groups = response.Result.Groups.Select(group => new GuideQueryGroup(string.Join(", ", group.Key), group.Count)).ToArray();
        if (response.Sides is not { } sides)
            return groups.Length > 0 ? (new GuideQueryRun("", [], [], groups), null) : (null, MotifAppStrings.GuideQueryNoSides);
        if (sides.Version != SupportedVersion) return (null, MotifAppStrings.GuideQuerySidesVersion);
        var states = sides.States.Select(state => new GuideQuerySideState(state.Side, state.Status, state.Source, state.TrialId, state.Reason)).ToArray();
        var rows = sides.Rows.Select(row => new GuideQueryRow(row.Id, Word(row.Baseline), Word(row.Proposal))).ToArray();
        return (new GuideQueryRun(sides.Mode, states, rows, groups), null);
    }

    private static GuideQueryWord? Word(QuerySideRow? row)
    {
        if (row is null) return null;
        var form = row.Form;
        var analyses = row.Parse.Analyses.Select(reading => new GuideQueryReading(reading.Category, reading.Approved,
            reading.Morphs.Select(morph => new GuideQueryMorph(morph.Form, morph.Gloss, morph.Category, morph.Entry)).ToArray())).ToArray();
        return new GuideQueryWord(form.Id, form.Form, form.Base, form.Kind, form.Entry, form.Category, form.Tokens,
            row.Parse.State, row.Parse.Tokens, analyses);
    }
}
