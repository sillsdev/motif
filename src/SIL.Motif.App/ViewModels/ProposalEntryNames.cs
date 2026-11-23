using System.Globalization;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// The entries and senses a Proposal touches, as the Baseline holds them, so a change can say "palit sense 2"
/// instead of an id. An id the Baseline does not hold keeps its id.
/// </summary>
internal sealed class ProposalEntryNames
{
    /// <summary>The most ids read for one Proposal, so a large Proposal costs a bounded number of reads.</summary>
    internal const int MaxIds = 300;

    public static readonly ProposalEntryNames None = new();

    private readonly Dictionary<string, LexicalEntryLookup> _found = new(StringComparer.Ordinal);

    public bool IsEmpty => _found.Count == 0;

    public void Add(IEnumerable<LexicalEntryLookup> lookups)
    {
        foreach (var lookup in lookups)
            if (lookup.Found) _found[lookup.Id] = lookup;
    }

    /// <summary>The ids worth asking for: well-formed ones, each once, at most <see cref="MaxIds"/>.</summary>
    public static IReadOnlyList<string> Askable(IEnumerable<string?> ids) =>
        [.. ids.Where(id => id is not null && CanonicalId.TryParse(id, out _)).Select(id => id!)
            .Distinct(StringComparer.Ordinal).Take(MaxIds)];

    public LexicalEntryLookup? Find(string? id) => id is not null && _found.TryGetValue(id, out var lookup) ? lookup : null;

    /// <summary>"palit sense 2" for a sense, the headword for an entry, or null when the id is not known.</summary>
    public string? Name(string? id)
    {
        if (Find(id) is not { } lookup) return null;
        var headword = Headword(lookup);
        return lookup.SenseId is not null && Outline(lookup) is { Length: > 0 } outline
            ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalEntrySenseName, headword, outline)
            : headword;
    }

    /// <summary>"Sense 2 of palit" or "Entry palit", or null when the id is not known.</summary>
    public string? Target(string? id)
    {
        if (Find(id) is not { } lookup) return null;
        var headword = Headword(lookup);
        return lookup.SenseId is not null && Outline(lookup) is { Length: > 0 } outline
            ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalEntryOfHeadword, outline, headword)
            : string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalEntryHeadword, headword);
    }

    /// <summary>"Senses of palit: 1 sell; 2 buy (changed here)", or null when the entry has no senses to list.</summary>
    public string? Senses(string? id)
    {
        if (Find(id) is not { Senses.Count: > 0 } lookup) return null;
        var items = lookup.Senses.Select(sense =>
        {
            var item = string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalEntrySenseItem, sense.Outline,
                sense.Gloss ?? MotifAppStrings.ProposalEntrySenseNoGloss).Trim();
            return SameId(sense.SenseId, lookup.SenseId)
                ? string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalEntrySenseChangedHere, item) : item;
        });
        return string.Format(CultureInfo.CurrentCulture, MotifAppStrings.ProposalEntrySensesLine, Headword(lookup),
            string.Join("; ", items));
    }

    private static string Headword(LexicalEntryLookup lookup) =>
        string.IsNullOrWhiteSpace(lookup.Headword) ? MotifAppStrings.ProposalEntryNoHeadword : lookup.Headword;

    private static string? Outline(LexicalEntryLookup lookup) =>
        lookup.Senses.FirstOrDefault(sense => SameId(sense.SenseId, lookup.SenseId))?.Outline;

    // One sense can be spelled with or without a prefix, so ids are compared by identity.
    private static bool SameId(string? left, string? right) => left is not null && right is not null &&
        CanonicalId.TryParse(left, out var a) && CanonicalId.TryParse(right, out var b) && a == b;
}
