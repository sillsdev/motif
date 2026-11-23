using System.Text.Json;
using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Review;

/// <summary>
/// The words a Trial lost or regressed, grouped by what happened to them, each group capped at
/// <see cref="TrialRegressions.WordsPerGroup"/> named words with a count of the rest.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TrialRegressions(
    [property: JsonRequired] IReadOnlyList<RegressionGroup> Groups) : IJsonOnDeserialized, IJsonOnSerializing
{
    /// <summary>The most words a group names before it counts the rest.</summary>
    public const int WordsPerGroup = 10;

    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (Groups is null || Groups.Any(item => item is null)) throw new JsonException("Invalid TrialRegressions.");
    }
}

/// <summary>One kind of regression: how many words, the named first few, how many are not named and how to read them.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RegressionGroup(
    [property: JsonRequired] string Cause,
    [property: JsonRequired] long Total,
    [property: JsonRequired] long Remaining,
    [property: JsonRequired] IReadOnlyList<RegressedWord> Words,
    [property: JsonRequired] string NextStep) : IJsonOnDeserialized, IJsonOnSerializing
{
    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (Cause is null || NextStep is null || Words is null || Words.Any(item => item is null) ||
            Total is < 0 or > 9007199254740991L || Remaining < 0 || Remaining > Total)
            throw new JsonException("Invalid RegressionGroup.");
    }
}

/// <summary>A word a Trial lost or regressed, with its analyses before and after the change.</summary>
/// <param name="Word">The wordform.</param>
/// <param name="CaseKey">The report case, for reading the word in full.</param>
/// <param name="BeforeReadingCount">The readings the Baseline parse produced; null when not measured.</param>
/// <param name="AfterReadingCount">The readings the Trial parse produced; null when not measured.</param>
/// <param name="Before">The Baseline analyses, as morph forms and glosses; at most a few, empty when not retained.</param>
/// <param name="After">The Trial analyses, as morph forms and glosses; empty when the word no longer parses.</param>
/// <param name="BeforeReadings">The same Baseline analyses by canonical id, for feeding to other commands; null in a report written before ids were kept.</param>
/// <param name="AfterReadings">The same Trial analyses by canonical id; null in a report written before ids were kept.</param>
/// <param name="TraceBaseline">A shell-safe command that traces this word at the Baseline.</param>
/// <param name="TraceTrial">A shell-safe command that traces this word at the Proposal's Trial copy.</param>
/// <param name="TouchedBy">
/// Proposal operations that name an object a Baseline analysis of this word uses. An overlap of ids, never a cause:
/// an operation may overlap and not matter, and a cause may touch no id the analysis holds. An empty list means the
/// overlap was computed and found none; null means it was not computed, and a report Limitation says why.
/// </param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RegressedWord(
    [property: JsonRequired] string Word,
    [property: JsonRequired] string CaseKey,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? BeforeReadingCount,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? AfterReadingCount,
    [property: JsonRequired] IReadOnlyList<string> Before,
    [property: JsonRequired] IReadOnlyList<string> After,
    IReadOnlyList<RegressedReading>? BeforeReadings = null,
    IReadOnlyList<RegressedReading>? AfterReadings = null,
    string? TraceBaseline = null,
    string? TraceTrial = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] IReadOnlyList<TouchedByOperation>? TouchedBy = null) : IJsonOnDeserialized, IJsonOnSerializing
{
    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (Word is null || CaseKey is null || Before is null || Before.Any(item => item is null) ||
            After is null || After.Any(item => item is null) ||
            BeforeReadings?.Any(item => item is null) == true || AfterReadings?.Any(item => item is null) == true ||
            TouchedBy?.Any(item => item is null) == true ||
            BeforeReadingCount is < 0 or > 9007199254740991L || AfterReadingCount is < 0 or > 9007199254740991L)
            throw new JsonException("Invalid RegressedWord.");
    }
}

/// <summary>One analysis of a regressed word, with the canonical ids of each morph.</summary>
/// <param name="Analysis">The analysis as morph forms and glosses, as in <see cref="RegressedWord.Before"/>.</param>
/// <param name="Morphs">The morphs in order.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RegressedReading(
    [property: JsonRequired] string Analysis,
    [property: JsonRequired] IReadOnlyList<RegressedMorph> Morphs) : IJsonOnDeserialized, IJsonOnSerializing
{
    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (Analysis is null || Morphs is null || Morphs.Any(item => item is null))
            throw new JsonException("Invalid RegressedReading.");
    }
}

/// <summary>One morph of an analysis by canonical id; an id the report does not hold is null, never guessed.</summary>
/// <param name="EntryId">The lexical entry that owns the form, read from the Baseline; null for a form it does not hold.</param>
/// <param name="MsaId">The morphosyntactic analysis.</param>
/// <param name="FormId">The allomorph or lexeme form.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RegressedMorph(
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? EntryId,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? MsaId,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? FormId);

/// <summary>A Proposal operation that names an object a lost Reading uses, stated as an overlap and not a cause.</summary>
/// <param name="Position">The operation's one-based position in the Proposal.</param>
/// <param name="OperationId">The operation's canonical id.</param>
/// <param name="Kind">The operation kind.</param>
/// <param name="SharedIds">The canonical ids of the objects both the operation and the Reading name.</param>
/// <param name="Statement">One sentence that says what the overlap is and is not.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TouchedByOperation(
    [property: JsonRequired] int Position,
    [property: JsonRequired] string OperationId,
    [property: JsonRequired] string Kind,
    [property: JsonRequired] IReadOnlyList<string> SharedIds,
    [property: JsonRequired] string Statement) : IJsonOnDeserialized, IJsonOnSerializing
{
    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (Position < 1 || OperationId is null || Kind is null || Statement is null || SharedIds is null ||
            SharedIds.Any(item => item is null))
            throw new JsonException("Invalid TouchedByOperation.");
    }
}
