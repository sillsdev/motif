namespace SIL.Motif.Contract.Query;

/// <summary>A closed, read-only query over a Trial's materialized facts.</summary>
/// <remarks>
/// <c>Fold</c> names the fold set of the <c>fold</c> column as comma-separated parts (<c>nfc</c>, <c>case</c>, <c>iy~y</c>,
/// <c>o~u</c>, <c>e~i</c>, <c>c~k</c> or a raw <c>from&gt;to</c>); the default is <c>nfc,case</c>. <c>Distinct</c> names a
/// column of which a group must hold at least two different values to be reported.
/// </remarks>
/// <remarks>
/// <c>Change</c> states a hypothesised boundary change and adds the <c>predicted</c>, <c>evidence</c> and <c>collides</c>
/// columns to the stems source. <c>Score</c> names the scorers (<c>tolerance</c>, <c>reliability</c>, <c>control</c>) run over
/// the whole selected universe, <c>Exceptions</c> are the selectors that mark a universe row an exception for
/// <c>tolerance</c>, and <c>Unit</c> (<c>msa</c> or <c>stem</c>) is the unit the scorers claim.
/// </remarks>
/// <remarks>
/// <c>split</c> and <c>size</c> read the stems and forms sources, with or without a change. Their deviants are the rows the
/// <c>Exceptions</c> selectors match, or the <c>Members</c> ids, or, under a change with an evidence word set, the rows whose
/// unchanged form alone is attested. <c>Context</c> selectors name the context <c>size</c> weighs. <c>Evidence</c> asks for the
/// one-screen composite over the grouping <c>Where</c> or <c>Members</c> names, compared with the whole source.
/// </remarks>
public sealed record QueryRecord(string From, IReadOnlyList<QueryCondition> Where, IReadOnlyList<string> Group,
    int MinCount = 1, int Examples = 5, bool Ids = false, bool RawLetters = false, string? TrialId = null,
    string? Fold = null, string? Distinct = null, QueryHypothesis? Change = null, IReadOnlyList<string>? Score = null,
    IReadOnlyList<QueryCondition>? Exceptions = null, string? Unit = null, IReadOnlyList<string>? Members = null,
    IReadOnlyList<QueryCondition>? Context = null, bool Evidence = false);

/// <summary>One selector; selectors in a query are combined with AND.</summary>
public sealed record QueryCondition(string Col, string Op, string Value);

/// <summary>One form and the facts needed to compare it to its base.</summary>
public sealed record QueryForm(string Id, string Form, string Base, string Kind, string Entry,
    string MorphType, string VariantType, IReadOnlyList<string> Environments, IReadOnlyList<string> Category,
    long? Tokens = null)
{
    /// <summary>The derived difference from the base, populated by query evaluation.</summary>
    public QueryChange? Change { get; init; }
    /// <summary>Segments removed from the base, including repetitions.</summary>
    public IReadOnlyList<string>? LettersDropped { get; init; }
    /// <summary>Segments added to the base, including repetitions.</summary>
    public IReadOnlyList<string>? LettersGained { get; init; }
}

/// <summary>The contiguous change between a base and its form.</summary>
public sealed record QueryChange(string Dropped, string Gained, string At, string Left, string Right, string Abstract);

/// <summary>One grouped result and its examples ordered by form.</summary>
public sealed record QueryGroup(IReadOnlyList<string> Key, int Count, long? Tokens, IReadOnlyList<QueryForm> Examples)
{
    /// <summary>Example rows of the stems, uses and objects sources; null for the forms source.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<QueryItem>? Samples { get; init; }
}

/// <summary>The rows or groups returned by a query.</summary>
public sealed record QueryResult(IReadOnlyList<QueryForm> Rows, IReadOnlyList<QueryGroup> Groups, IReadOnlyList<string> Ids)
{
    /// <summary>One entry per requested scorer; null when the query named none.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<QueryScore>? Scores { get; init; }
    /// <summary>What a hypothesised-change query could not measure, worded for the reader; null when it measured everything.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Notes { get; init; }
    /// <summary>The one-screen composite of an evidence query; null for any other query.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public QueryEvidence? Evidence { get; init; }
    /// <summary>Matching rows of the stems, uses and objects sources; null for the forms source.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<QueryItem>? Items { get; init; }
}


/// <summary>One stored row of the stems, uses or objects source; <c>Values[i]</c> holds column <c>i</c> of the source.</summary>
public sealed record QueryRow(string Id, IReadOnlyList<string[]> Values);

/// <summary>One row of the stems, uses or objects source with its columns named.</summary>
public sealed record QueryItem(string Id, IReadOnlyDictionary<string, string[]> Values);
