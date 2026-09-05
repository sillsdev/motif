using System;
using System.Collections.Generic;

namespace SIL.Motif.Contract.Requests;

/// <summary>
/// Which of the four agreed sources (design decision 4) a Selection should draw from, and how. Every
/// source is optional and they combine: a caller wanting the union of two sources sets both.
/// </summary>
/// <param name="AllWordforms">Every wordform currently in the project.</param>
/// <param name="TextIds">The wordforms of these chosen Texts, by their own <c>IText</c> identity.</param>
/// <param name="Words">
/// A pasted or typed list, one word per entry. Entries that are empty or all whitespace are dropped
/// rather than treated as a blank word.
/// </param>
/// <param name="RetryFailed">The words the previous Baseline run found no analysis for.</param>
/// <param name="RetrySlowerThan">
/// Non-null to also include the words whose previous Baseline run recorded an elapsed time strictly
/// greater than this threshold. A word that timed out comes back only when its recorded cap itself
/// clears the threshold, not merely because it timed out.
/// </param>
public sealed record SelectionRequest(
    bool AllWordforms,
    IReadOnlyList<Guid> TextIds,
    IReadOnlyList<string> Words,
    bool RetryFailed,
    TimeSpan? RetrySlowerThan);
