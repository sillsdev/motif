namespace SIL.Motif.Contract.Review;

/// <summary>How a Trial's Reading delta reads against the author's expected-change manifests and doubled-word caps.</summary>
public static class ReadingExpectationStates
{
    /// <summary>Every measured word changed exactly as declared and the doubled caps hold.</summary>
    public const string Green = "green";

    /// <summary>A measured word's retired or added Readings differ from the manifest.</summary>
    public const string Mismatch = "mismatch";

    /// <summary>The words that kept every old Reading and gained more exceed a cap.</summary>
    public const string DoubledOverCap = "doubled-over-cap";

    /// <summary>The Trial cannot be held to the expectations: a form was not measured, none was, or it predates the current Proposal revision or Baseline.</summary>
    public const string Unmeasured = "unmeasured";
}

/// <summary>The two sides of a manifest mismatch.</summary>
public static class ReadingExpectationKinds
{
    /// <summary>The retired Readings of a word, held against <c>--expect-retired</c>.</summary>
    public const string Retired = "retired";

    /// <summary>The added Readings of a word, held against <c>--expect-added</c>.</summary>
    public const string Added = "added";
}

/// <summary>One word whose Readings differ from a manifest.</summary>
/// <param name="Kind">retired or added.</param>
/// <param name="Word">The word as the manifest spells it.</param>
/// <param name="Unexpected">Readings the Trial shows that the manifest does not declare; the id is the signature's id.</param>
/// <param name="Missing">Readings the manifest declares that the Trial does not show; the id is the manifest's own signature encoding.</param>
public sealed record ReadingExpectationMismatch(string Kind, string Word, IReadOnlyList<ReadingDeltaReading> Unexpected,
    IReadOnlyList<ReadingDeltaReading> Missing);

/// <summary>
/// A Trial's Reading delta held to the author's manifests and caps. Mirrors the grammar lanes' gate: a manifest names the
/// whole delta of every measured word, a known non-word that stopped parsing needs no row, and the doubled cap applies even
/// when the added Readings are declared.
/// </summary>
/// <param name="State">One of <see cref="ReadingExpectationStates"/>.</param>
/// <param name="Reason">Why the state is not green, or null when it is.</param>
/// <param name="MismatchedWords">Measured words whose retired or added Readings differ from a manifest, per manifest.</param>
/// <param name="Mismatches">The first rows of those words, ordered by kind then spelling.</param>
/// <param name="ManifestWordsNotMeasured">Manifest words the Trial did not measure; they are ignored, as the lane gate ignores them.</param>
/// <param name="Doubled">Words that parsed before, kept every old Reading and gained more.</param>
/// <param name="DoubledSample">Up to twenty of them, ordered by spelling.</param>
/// <param name="DoubledParsedBefore">The measured words that parsed before the change, the denominator of the fraction cap.</param>
/// <param name="MaxDoubled">The cap on the count, or null when none was given.</param>
/// <param name="MaxDoubledFrac">The cap as a fraction of <paramref name="DoubledParsedBefore"/>, or null when none was given.</param>
/// <param name="DoubledOverCap">Whether either cap was exceeded.</param>
public sealed record ReadingExpectationResult(string State, string? Reason, long MismatchedWords,
    IReadOnlyList<ReadingExpectationMismatch> Mismatches, long ManifestWordsNotMeasured, long Doubled,
    IReadOnlyList<string> DoubledSample, long DoubledParsedBefore, long? MaxDoubled, double? MaxDoubledFrac, bool DoubledOverCap);
