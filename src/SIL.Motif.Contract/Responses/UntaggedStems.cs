namespace SIL.Motif.Contract.Responses;

/// <summary>
/// Stems not tagged with an inflection class whose category, and every category above it, has no default class,
/// where an affix of that category is restricted by class. The parser gives such a stem no class, so it cannot take
/// those affixes. This is advice that ranks a place to look; it never stops an Apply, a Draft or an edit.
/// </summary>
/// <param name="Count">How many entries have such a stem.</param>
/// <param name="Top">At most <see cref="OverviewMeasureLimits.TopWords"/> of them, the most affixes held back first.</param>
public sealed record UntaggedStems(int Count, IReadOnlyList<UntaggedStem> Top)
{
    /// <summary>No entry is in this state, or the Baseline was captured without the measure.</summary>
    public static UntaggedStems None { get; } = new(0, []);
}

/// <summary>One entry with a stem not tagged with an inflection class.</summary>
/// <param name="EntryId">The lexical entry's FieldWorks GUID.</param>
/// <param name="StemAllomorphId">The GUID of the entry's stem allomorph, which the inspector opens.</param>
/// <param name="Headword">The entry's headword.</param>
/// <param name="Category">The stem's category, by name.</param>
/// <param name="RestrictedAffixes">
/// How many affix allomorphs restricted by class belong to the category or one above it, so the stem cannot take them.
/// </param>
public sealed record UntaggedStem(Guid EntryId, string StemAllomorphId, string Headword, string Category, int RestrictedAffixes);
