namespace SIL.Motif.Contract.Query;

/// <summary>A closed, read-only query over a Trial's materialized facts.</summary>
public sealed record QueryRecord(string From, IReadOnlyList<QueryCondition> Where, IReadOnlyList<string> Group,
    int MinCount = 1, int Examples = 5, bool Ids = false, bool RawLetters = false, string? TrialId = null);

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
public sealed record QueryGroup(IReadOnlyList<string> Key, int Count, long? Tokens, IReadOnlyList<QueryForm> Examples);

/// <summary>The rows or groups returned by a query.</summary>
public sealed record QueryResult(IReadOnlyList<QueryForm> Rows, IReadOnlyList<QueryGroup> Groups, IReadOnlyList<string> Ids);
