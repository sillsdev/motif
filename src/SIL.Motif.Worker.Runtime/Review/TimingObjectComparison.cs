using SIL.Motif.Contract;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Worker.Runtime.Review;

/// <summary>Pairs complete object counters by stable identity over an explicitly bound word scope.</summary>
public static class TimingObjectComparison
{
    /// <summary>Compares the remaining work only when every attributed counter is complete and consistent.</summary>
    public static TimingObjectCostChange Unattributed(IReadOnlyList<AssessmentObjectTiming> before,
        IReadOnlyList<AssessmentObjectTiming> after, HashSet<string> words, decimal beforeTotal, decimal afterTotal, bool complete)
    {
        var oldRows = before.Where(row => words.Contains(row.Word)).ToArray();
        var newRows = after.Where(row => words.Contains(row.Word)).ToArray();
        bool Valid(AssessmentObjectTiming[] rows) => rows.All(row => row.ParserWork is >= 0) &&
            rows.Select(row => (row.Word, row.Kind, row.Key, row.IdentityQuality, row.Scope, row.Direction))
                .Distinct().Count() == rows.Length;
        double? percent = null;
        if (complete && Valid(oldRows) && Valid(newRows))
        {
            var oldRemaining = beforeTotal - oldRows.Sum(row => (decimal)row.ParserWork!.Value);
            var newRemaining = afterTotal - newRows.Sum(row => (decimal)row.ParserWork!.Value);
            if (oldRemaining >= 0 && newRemaining >= 0)
                percent = ReviewWorkComparison.PercentChange(oldRemaining, newRemaining);
        }
        return new TimingObjectCostChange("unattributed", "unattributed", "Not attributed",
            percent is { } value ? new TimingCostChange(value, null) :
            TimingCostChange.Unavailable("Complete paired unattributed work is unavailable or the Baseline is zero."))
            { Status = percent is null ? "unavailable" : "paired", IdentityQuality = "kind" };
    }

    /// <summary>Returns percentages only where the object evidence is complete on both sides.</summary>
    public static IReadOnlyList<TimingObjectCostChange> Compare(IReadOnlyList<AssessmentObjectTiming> before,
        IReadOnlyList<AssessmentObjectTiming> after, HashSet<string> words, bool complete, string by = "rule")
    {
        var oldRows = before.Where(row => words.Contains(row.Word)).ToArray();
        var newRows = after.Where(row => words.Contains(row.Word)).ToArray();
        var groups = oldRows.Select(row => (Side: 0, Row: row)).Concat(newRows.Select(row => (Side: 1, Row: row)))
            .GroupBy(item => by == "kind" ? (item.Row.Kind, Key: item.Row.Kind, Quality: "kind", Scope: (string?)null)
                : (item.Row.Kind, Key: ObjectIdentity.CanonicalKey(item.Row.Key, item.Row.IdentityQuality) ?? item.Row.Key,
                    Quality: item.Row.IdentityQuality, item.Row.Scope));
        return groups.Select(group =>
        {
            var key = group.Key;
            var oldObject = group.Where(item => item.Side == 0).Select(item => item.Row).ToArray();
            var newObject = group.Where(item => item.Side == 1).Select(item => item.Row).ToArray();
            var identityKnown = group.All(item => item.Row.IdentityQuality == "authored" &&
                Guid.TryParse(item.Row.Key, out _) || !string.IsNullOrWhiteSpace(item.Row.Scope));
            var oldWords = oldObject.Select(row => row.Word).ToHashSet(StringComparer.Ordinal);
            var newWords = newObject.Select(row => row.Word).ToHashSet(StringComparer.Ordinal);
            var allPresent = words.IsSubsetOf(oldWords) && words.IsSubsetOf(newWords);
            var oldKeys = oldObject.Select(row => (row.Word, Key: ObjectIdentity.CanonicalKey(row.Key, row.IdentityQuality),
                row.Direction, row.IdentityQuality, row.Scope)).ToHashSet();
            var newKeys = newObject.Select(row => (row.Word, Key: ObjectIdentity.CanonicalKey(row.Key, row.IdentityQuality),
                row.Direction, row.IdentityQuality, row.Scope)).ToHashSet();
            allPresent &= oldObject.Length == oldKeys.Count && newObject.Length == newKeys.Count && oldKeys.SetEquals(newKeys);
            var countersKnown = oldObject.Concat(newObject).All(row => row.ParserWork is >= 0);
            var percent = complete && identityKnown && allPresent && countersKnown
                ? ReviewWorkComparison.PercentChange(oldObject.Sum(row => (decimal)row.ParserWork!.Value),
                    newObject.Sum(row => (decimal)row.ParserWork!.Value)) : null;
            var unpairedLocal = by != "kind" && key.Quality != "authored" &&
                (oldObject.Length == 0 || newObject.Length == 0);
            var status = !complete || !identityKnown || !countersKnown || unpairedLocal ? "unavailable"
                : oldObject.Length == 0 ? "new" : newObject.Length == 0 ? "missing"
                : percent is not null ? "paired" : "unavailable";
            return new TimingObjectCostChange(key.Kind, key.Key, by == "kind" ? key.Kind : group.First().Row.Object,
                percent is { } value ? new TimingCostChange(value, null) :
                TimingCostChange.Unavailable(status switch { "new" => "The object appears only after the Trial.",
                    "missing" => "The object appears only before the Trial.",
                    _ => "Matching complete object counters are unavailable for these words." }))
                { Status = status, IdentityQuality = key.Quality, Scope = key.Scope };
        }).ToArray();
    }
}
