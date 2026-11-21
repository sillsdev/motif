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
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RegressedWord(
    [property: JsonRequired] string Word,
    [property: JsonRequired] string CaseKey,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? BeforeReadingCount,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? AfterReadingCount,
    [property: JsonRequired] IReadOnlyList<string> Before,
    [property: JsonRequired] IReadOnlyList<string> After) : IJsonOnDeserialized, IJsonOnSerializing
{
    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (Word is null || CaseKey is null || Before is null || Before.Any(item => item is null) ||
            After is null || After.Any(item => item is null) ||
            BeforeReadingCount is < 0 or > 9007199254740991L || AfterReadingCount is < 0 or > 9007199254740991L)
            throw new JsonException("Invalid RegressedWord.");
    }
}
