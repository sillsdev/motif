using System.Globalization;
using System.Text;

namespace SIL.Motif.Contract.Parsimony;

/// <summary>A tag and the stems that carry it, against the stems a rule could apply to.</summary>
/// <param name="TagId">The exception feature or tag.</param>
/// <param name="RuleId">The rule whose candidate hosts are counted.</param>
/// <param name="CandidateHosts">How many stems satisfy the rule's context and could host the rule.</param>
/// <param name="CarriersAmongHosts">How many of those hosts carry the tag.</param>
public sealed record TagPolarityFacts(string TagId, string RuleId, int CandidateHosts, int CarriersAmongHosts);

/// <summary>A tag whose carriers all already satisfy a rule's context.</summary>
/// <param name="TagId">The tag.</param>
/// <param name="RuleId">The rule.</param>
/// <param name="Carriers">All stems carrying the tag.</param>
/// <param name="CarriersMatchingContext">The carriers whose form matches the rule context.</param>
public sealed record TagContextFacts(string TagId, string RuleId, int Carriers, int CarriersMatchingContext);

/// <summary>A lexeme form with its entry id, for near-duplicate bucketing.</summary>
/// <param name="EntryId">The entry.</param>
/// <param name="Form">The lexeme form.</param>
public sealed record LexemeForm(string EntryId, string Form);

/// <summary>
/// The four redundancy measures of the weekly sweep. Each is a pure function over counts or forms that a
/// load already holds: O(hosts) for polarity and context (one pass over the tag-and-rule facts), O(entries) for
/// near-duplicate lexemes (one hash bucket per folded form). Variant shape groups are
/// <see cref="RuleAttempts.SameShapeGroups"/>, O(added objects). None compares entries pairwise or parses a word.
/// </summary>
public static class RedundancyMeasures
{
    /// <summary>Tags carried by at least half of a rule's candidate hosts: the tag is the default, so its polarity should flip.</summary>
    /// <param name="facts">One row per tag and rule.</param>
    /// <param name="majority">The carrier share that counts as a majority.</param>
    public static IReadOnlyList<TagPolarityFacts> PolarityFlips(IEnumerable<TagPolarityFacts> facts,
        double majority = 0.5) =>
        [.. facts.Where(f => f.CandidateHosts > 0 && (double)f.CarriersAmongHosts / f.CandidateHosts >= majority)
            .OrderBy(f => f.TagId, StringComparer.Ordinal).ThenBy(f => f.RuleId, StringComparer.Ordinal)];

    /// <summary>Tags with carriers, all of which already match a rule context: the context may imply the tag.</summary>
    /// <param name="facts">One row per tag and rule.</param>
    public static IReadOnlyList<TagContextFacts> ImpliedByContext(IEnumerable<TagContextFacts> facts) =>
        [.. facts.Where(f => f.Carriers > 0 && f.CarriersMatchingContext == f.Carriers)
            .OrderBy(f => f.TagId, StringComparer.Ordinal).ThenBy(f => f.RuleId, StringComparer.Ordinal)];

    /// <summary>The key two lexeme forms share when they differ only in case, Unicode form or iy/y.</summary>
    /// <param name="form">The form.</param>
    public static string FoldKey(string form) =>
        form.Normalize(NormalizationForm.FormC).ToLower(CultureInfo.InvariantCulture).Replace("iy", "y", StringComparison.Ordinal);

    /// <summary>Groups of two or more entries whose forms fold to one key, found by hashing, never by pairing.</summary>
    /// <param name="lexemes">All lexeme forms.</param>
    public static IReadOnlyList<IReadOnlyList<LexemeForm>> NearDuplicateLexemes(IEnumerable<LexemeForm> lexemes) =>
        [.. lexemes.GroupBy(l => FoldKey(l.Form), StringComparer.Ordinal)
            .Where(g => g.Select(l => l.Form).Distinct(StringComparer.Ordinal).Count() > 1)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => (IReadOnlyList<LexemeForm>)[.. g])];
}
