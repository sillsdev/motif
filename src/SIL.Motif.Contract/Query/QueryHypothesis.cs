using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Query;

/// <summary>
/// A hypothesised boundary change: at the <c>final</c> edge of a stem, <c>Dropped</c> becomes <c>Gained</c> before each of
/// <c>Affixes</c> (suffixes); at the <c>initial</c> edge, after each affix (prefixes). Word sets are named ids of the
/// project's attested (<c>sampled-tokens</c>) review word sets.
/// </summary>
/// <param name="Dropped">The letters the stem edge has today.</param>
/// <param name="Gained">The letters the hypothesis puts there; may be empty.</param>
/// <param name="At"><c>final</c> or <c>initial</c>.</param>
/// <param name="Affixes">The affixes the change fires before (<c>final</c>) or after (<c>initial</c>), without hyphens.</param>
/// <param name="WordSets">Attested word sets the <c>evidence</c> column looks the forms up in (their union).</param>
/// <param name="Discovery">Word sets the hypothesis was mined from; a <c>control</c> hit must be absent from them.</param>
/// <param name="HeldOut">Word sets nobody mined; only <c>control</c> reads them and it returns counts only.</param>
/// <param name="Controls">How many control letters <c>control</c> tries, 1 to 9.</param>
/// <param name="ControlLetters">Control letters to use instead of the chosen ones, of the same class as the gained letters.</param>
public sealed record QueryHypothesis(string Dropped, string Gained, string At, IReadOnlyList<string> Affixes,
    IReadOnlyList<string> WordSets, IReadOnlyList<string>? Discovery = null, IReadOnlyList<string>? HeldOut = null,
    int Controls = 5, IReadOnlyList<string>? ControlLetters = null);

/// <summary>One scorer's result over the whole selected universe.</summary>
/// <param name="Scorer"><c>tolerance</c>, <c>reliability</c> or <c>control</c>.</param>
/// <param name="Version">The scorer's formula version; a sort key is not a verdict when it says so.</param>
/// <param name="Unit"><c>msa</c> or <c>stem</c>: what <see cref="Counts"/> count.</param>
/// <param name="Counts">Integer results, such as hits, scope or N.</param>
/// <param name="Numbers">Real results, such as a ratio, a limit or a threshold; null when undefined.</param>
/// <param name="Verdict"><c>productive</c> / <c>not-productive</c> for tolerance, <c>PASS</c> / <c>FAIL</c> / <c>UNDECIDED</c> for control.</param>
/// <param name="Notes">Caveats the reader needs.</param>
public sealed record QueryScore(string Scorer, string Version, string Unit, IReadOnlyDictionary<string, long> Counts,
    IReadOnlyDictionary<string, double?> Numbers, string? Verdict, IReadOnlyList<string> Notes)
{
    /// <summary>Control hits by letter, for <c>control</c>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, long>? Controls { get; init; }
}

/// <summary>A scorer was asked to count in a unit its hits or exceptions are not in.</summary>
public sealed class QueryMixedUnitsException(string message) : ArgumentException(message);

/// <summary>A query asks about something the available evidence did not measure.</summary>
public sealed class QueryNotMeasuredException(string message) : ArgumentException(message);

/// <summary>
/// What a hypothesised-change query needs besides the stems: the word sets, the forms of every entry and,
/// when the project has one, what already parses. Held-out words are read by <c>control</c> alone.
/// </summary>
/// <param name="Evidence">The words of the <see cref="QueryHypothesis.WordSets"/>.</param>
/// <param name="Discovery">The words of the discovery sets.</param>
/// <param name="HeldOut">The words of the held-out sets.</param>
/// <param name="Forms">Every lexeme, alternate and variant form with its entry, for the entry-collision check.</param>
/// <param name="Parse">Reads which words parse today; null when the project has no current parser evidence.</param>
public sealed record QueryHypothesisInputs(IReadOnlyCollection<string> Evidence, IReadOnlyCollection<string> Discovery,
    IReadOnlyCollection<string> HeldOut, IReadOnlyCollection<(string Form, string Entry)> Forms,
    Func<QueryParseEvidence?>? Parse);

/// <summary>What parses today, as measured by the current parser run.</summary>
/// <param name="Parses">Each measured word and whether it has an analysis; a word absent from it was not measured.</param>
/// <param name="Stale">Whether the project was saved after the run.</param>
public sealed record QueryParseEvidence(IReadOnlyDictionary<string, bool> Parses, bool Stale);
