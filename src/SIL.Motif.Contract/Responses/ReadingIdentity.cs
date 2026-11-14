using System.Collections.Generic;

namespace SIL.Motif.Contract.Responses;

/// <summary>Whether a Trial's before and after readings of the affected words are the same set.</summary>
public enum ReadingIdentityStatus
{
    /// <summary>Every affected word was searched to completion on both sides and no reading was lost or gained.</summary>
    Identical,

    /// <summary>A completed pair of searches lost or gained at least one reading; the difference is listed.</summary>
    Differs,

    /// <summary>Some affected word has no completed pair of searches, so no identity can be claimed.</summary>
    Incomplete,
}

/// <summary>The readings one affected word lost and gained, each written as its ordered morph identities.</summary>
/// <param name="Word">The word as the Trial parsed it.</param>
/// <param name="Lost">Readings the before search produced, after translation, that the after search did not.</param>
/// <param name="New">Readings the after search produced that the before search did not.</param>
public sealed record ReadingIdentityDifference(string Word, IReadOnlyList<string> Lost, IReadOnlyList<string> New);

/// <summary>
/// The identity of the readings of the affected words across one Trial: how many words and readings were compared
/// and exactly which readings differ. A difference is a finding to weigh, not a refusal, because a rule can add a
/// real, correct ambiguity.
/// </summary>
/// <param name="Status">The verdict, which is never <see cref="ReadingIdentityStatus.Identical"/> over unfinished searches.</param>
/// <param name="AffectedWords">How many words the proof was asked about.</param>
/// <param name="IdenticalWords">How many of them completed on both sides with the same readings.</param>
/// <param name="ReadingsBefore">How many readings the before search produced for the completed words.</param>
/// <param name="LostReadings">How many before readings the after search did not produce.</param>
/// <param name="NewReadings">How many after readings the before search did not produce.</param>
/// <param name="Differences">The words with a lost or new reading.</param>
/// <param name="IncompleteWords">The words with no completed pair of searches.</param>
/// <param name="Unavailable">Reasons no verdict beyond the counts can be given.</param>
public sealed record ReadingIdentityResult(
    ReadingIdentityStatus Status,
    int AffectedWords,
    int IdenticalWords,
    int ReadingsBefore,
    int LostReadings,
    int NewReadings,
    IReadOnlyList<ReadingIdentityDifference> Differences,
    IReadOnlyList<string> IncompleteWords,
    IReadOnlyList<string> Unavailable);
