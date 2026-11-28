using System.Collections.Immutable;
using System.Text.Json;
using SIL.Motif.Contract.HandAnalysis;

namespace SIL.Motif.Contract.Review;

/// <summary>The classes of word a Trial's Reading delta names.</summary>
public static class ReadingDeltaClasses
{
    /// <summary>Parsed before the change and has no Reading after it.</summary>
    public const string Lost = "lost";

    /// <summary>Had no Reading before the change and has one after it.</summary>
    public const string Gained = "gained";

    /// <summary>Parsed on both sides and shares no Reading between them.</summary>
    public const string Changed = "changed";

    /// <summary>Keeps every Reading it had and gains more.</summary>
    public const string Doubled = "doubled";

    /// <summary>A known non-word that parsed before the change and no longer parses.</summary>
    public const string Rejected = "stopped-negative";

    /// <summary>The classes a read shows by default.</summary>
    public static readonly IReadOnlyList<string> Default = [Lost, Gained, Changed, Doubled];

    /// <summary>Every class a read can show.</summary>
    public static readonly IReadOnlyList<string> All = [Lost, Gained, Changed, Doubled, Rejected];
}

/// <summary>One Reading by signature with the number of copies the parser emitted or stopped emitting.</summary>
/// <param name="Id">The signature's canonical identity, the one the Trial export and report rows carry.</param>
/// <param name="Count">The copies that differ between the sides; one Reading may occur more than once.</param>
/// <param name="Signature">The signature as one manifest row writes it, an array of morpheme objects.</param>
public sealed record ReadingDeltaReading(string Id, long Count, JsonElement Signature);

/// <summary>A measured form's Reading change between the Baseline and the Trial copy.</summary>
/// <param name="Word">The measured spelling, in NFC.</param>
/// <param name="CaseKey">The Trial case the spelling belongs to, as report and regression rows name it.</param>
/// <param name="BeforeCount">The lexicon-backed Reading copies before the change.</param>
/// <param name="AfterCount">The lexicon-backed Reading copies after it.</param>
/// <param name="Retired">The Readings before minus after.</param>
/// <param name="Added">The Readings after minus before.</param>
/// <param name="SenseChanged">How many retired Readings pair with an added one that differs only in sense resolution; each pair is one change.</param>
public sealed record ReadingDeltaWord(string Word, string CaseKey, long BeforeCount, long AfterCount,
    IReadOnlyList<ReadingDeltaReading> Retired, IReadOnlyList<ReadingDeltaReading> Added, long SenseChanged);

/// <summary>The ways a Trial's Reading changes can be grouped by cause.</summary>
public static class ReadingDeltaGroupings
{
    /// <summary>The lexical entry a morpheme belongs to.</summary>
    public const string Entry = "entry";

    /// <summary>The inflectional affix slot of an affix morpheme's morphosyntactic analysis.</summary>
    public const string Slot = "slot";

    /// <summary>A phonological or compound rule; a Reading's identity does not carry one, so it cannot be grouped by.</summary>
    public const string Rule = "rule";

    /// <summary>The key of an inflectional affix whose analysis names no slot.</summary>
    public const string NoSlot = "(no slot)";

    /// <summary>The key of a derivational affix, which has no slot.</summary>
    public const string Derivational = "(derivational)";

    /// <summary>The key of an analysis the Baseline copy does not hold.</summary>
    public const string UnknownMsa = "(unknown msa)";

    /// <summary>The groupings a read can produce.</summary>
    public static readonly IReadOnlyList<string> Supported = [Entry, Slot];
}

/// <summary>The Reading changes that share one cause.</summary>
/// <param name="Key">The cause: an entry's canonical id, a slot's canonical id, <c>(no slot)</c>, <c>(derivational)</c> or <c>(unknown msa)</c>.</param>
/// <param name="Label">The entry's headword or the slot's name, or null when the Baseline could not name it.</param>
/// <param name="Retired">Reading copies retired that name this cause.</param>
/// <param name="Added">Reading copies added that name this cause.</param>
/// <param name="Words">The distinct words those copies belong to.</param>
/// <param name="Tokens">The Baseline's occurrences of those words; zero when the Baseline holds none.</param>
/// <param name="SampleWords">Up to five of the words, the most frequent first.</param>
public sealed record ReadingDeltaGroup(string Key, string? Label, long Retired, long Added, long Words, long Tokens,
    IReadOnlyList<string> SampleWords);

/// <summary>A page of cause groups, ordered by tokens then key.</summary>
/// <param name="By">entry or slot.</param>
/// <param name="Total">Every group, however many rows the page holds.</param>
/// <param name="Rows">The page.</param>
/// <param name="NextCursor">The cursor for the next page of groups, or null at the end.</param>
public sealed record ReadingDeltaGroups(string By, long Total, IReadOnlyList<ReadingDeltaGroup> Rows,
    string? NextCursor = null);

/// <summary>One class of word: how many, and the page of rows the read asked for.</summary>
/// <param name="Class">One of <see cref="ReadingDeltaClasses"/>.</param>
/// <param name="Total">Every word of the class, however many rows the page holds.</param>
/// <param name="Rows">The page, ordered by spelling.</param>
/// <param name="NextCursor">The cursor for the next page of this class, or null at the end.</param>
public sealed record ReadingDeltaClass(string Class, long Total, IReadOnlyList<ReadingDeltaWord> Rows,
    string? NextCursor = null);

/// <summary>The counts over every measured form of a Trial.</summary>
/// <param name="Measured">Forms measured on both sides with every Reading identified.</param>
/// <param name="ParsedBefore">Measured forms that had a Reading before the change.</param>
/// <param name="Lost">Forms of the lost class.</param>
/// <param name="Gained">Forms of the gained class.</param>
/// <param name="Changed">Forms of the changed class.</param>
/// <param name="Doubled">Forms of the doubled class.</param>
/// <param name="RejectedNegatives">Known non-words that stopped parsing, an improvement that needs no manifest row.</param>
/// <param name="RetiredReadings">Reading copies retired across every measured form.</param>
/// <param name="AddedReadings">Reading copies added across every measured form.</param>
/// <param name="Unmeasured">Forms left out because a side did not finish or a Reading has no complete identity.</param>
/// <param name="SenseChanged">Retired and added Reading pairs that share entry, MSA and allomorph and differ only in sense resolution,
/// as when a form that resolved to one sense now has several candidates. Each pair is one change, not a loss and a gain; the
/// Readings stay in the retired and added counts and the manifests.</param>
public sealed record ReadingDeltaCounts(long Measured, long ParsedBefore, long Lost, long Gained, long Changed,
    long Doubled, long RejectedNegatives, long RetiredReadings, long AddedReadings, long Unmeasured, long SenseChanged)
{
    /// <summary>The Reading changes with each sense-resolution pair counted once.</summary>
    public long ReadingChanges => RetiredReadings + AddedReadings - SenseChanged;
}

/// <summary>Reading copies grouped by one object a manifest row names.</summary>
/// <param name="Id">The entry, morphosyntactic analysis or allomorph.</param>
/// <param name="Readings">The Reading copies that name it.</param>
/// <param name="Words">The distinct words those copies belong to.</param>
public sealed record ReadingDeltaObject(string Id, long Readings, long Words);

/// <summary>What one manifest file holds.</summary>
/// <param name="Kind">retired or added.</param>
/// <param name="Path">The file, or null when the read wrote none.</param>
/// <param name="Rows">Manifest rows.</param>
/// <param name="Readings">Reading copies across the rows.</param>
/// <param name="ByObject">Reading and word counts per entry, MSA and allomorph, keyed by level.</param>
public sealed record ReadingManifestSummary(string Kind, string? Path, long Rows, long Readings,
    IReadOnlyDictionary<string, IReadOnlyList<ReadingDeltaObject>> ByObject);

/// <summary>
/// The Reading changes of one Trial, computed from the evidence its first pass retained: no second measurement.
/// </summary>
/// <param name="SchemaVersion">The version of this shape.</param>
/// <param name="JobId">The Trial job the evidence belongs to.</param>
/// <param name="ProposalRevision">The Proposal revision the Trial measured.</param>
/// <param name="Counts">Counts over every measured form.</param>
/// <param name="Classes">The requested classes, each with a page of rows.</param>
/// <param name="Manifests">The retired and added manifests, in that order, or empty when none was requested.</param>
/// <param name="Limitations">Why some forms are not in the delta, by code.</param>
/// <param name="Expectations">The delta held to manifests and doubled caps, present only when the read was given any.</param>
/// <param name="Receipt">The receipt written for a green Trial, present only when the read was asked to write one and it earned one.</param>
/// <param name="Siblings">The sibling words the Trial measured beside its own list and how they fared, present only when it sampled any.</param>
/// <param name="Groups">The changed Readings grouped by cause, present only when the read asked for a grouping; the class rows are then left out.</param>
public sealed record ReadingDelta(int SchemaVersion, string JobId, string ProposalRevision, ReadingDeltaCounts Counts,
    IReadOnlyList<ReadingDeltaClass> Classes, IReadOnlyList<ReadingManifestSummary> Manifests,
    IReadOnlyList<string> Limitations,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    ReadingExpectationResult? Expectations = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    TrialReceiptSummary? Receipt = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    TrialSiblings? Siblings = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    ReadingDeltaGroups? Groups = null)
{
    /// <summary>The current version of this shape.</summary>
    public const int Version = 1;
}

/// <summary>A Reading copy on one side of a measured form, ready for comparison.</summary>
/// <param name="Id">The signature's canonical identity.</param>
/// <param name="Signature">The signature itself.</param>
public sealed record ReadingCopy(string Id, ReadingSignature Signature);

/// <summary>One measured form with the lexicon-backed Readings of both sides.</summary>
/// <param name="Word">The measured spelling.</param>
/// <param name="CaseKey">The Trial case the spelling belongs to.</param>
/// <param name="Negative">Whether the form belongs to a set of known non-words.</param>
/// <param name="Before">The Baseline's Readings, one entry per copy.</param>
/// <param name="After">The Trial copy's Readings, one entry per copy.</param>
public sealed record ReadingDeltaInput(string Word, string CaseKey, bool Negative,
    IReadOnlyList<ReadingCopy> Before, IReadOnlyList<ReadingCopy> After);

/// <summary>A form's Reading delta with the signatures kept, so a manifest can be written from it.</summary>
/// <param name="Word">The measured spelling.</param>
/// <param name="CaseKey">The Trial case.</param>
/// <param name="BeforeCount">Reading copies before.</param>
/// <param name="AfterCount">Reading copies after.</param>
/// <param name="Retired">Signature and copies before minus after, ordered as a manifest orders its rows.</param>
/// <param name="Added">Signature and copies after minus before, ordered as a manifest orders its rows.</param>
/// <param name="Classes">The classes the form belongs to.</param>
/// <param name="SenseChanged">Retired and added copies that pair as one sense-resolution change.</param>
public sealed record ReadingDeltaForm(string Word, string CaseKey, long BeforeCount, long AfterCount,
    IReadOnlyList<(ReadingSignature Signature, long Count)> Retired, IReadOnlyList<(ReadingSignature Signature, long Count)> Added,
    IReadOnlySet<string> Classes, long SenseChanged = 0);

/// <summary>Computes the Reading delta of measured forms; the one definition of lost, gained, changed and doubled.</summary>
public static class ReadingDeltaBuilder
{
    /// <summary>
    /// Subtracts each form's Reading multisets both ways and classifies the form. Lost, gained, changed and doubled
    /// follow the grammar lanes' gate: changed means the sides share no Reading, doubled means every old Reading is
    /// kept and there are more copies after. A known non-word that stopped parsing is rejected, never lost.
    /// </summary>
    /// <param name="forms">The measured forms; a spelling that appears twice keeps its first row.</param>
    /// <returns>One delta per spelling, ordered by spelling.</returns>
    public static IReadOnlyList<ReadingDeltaForm> Compute(IEnumerable<ReadingDeltaInput> forms)
    {
        ArgumentNullException.ThrowIfNull(forms);
        var result = new List<ReadingDeltaForm>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var form in forms.Where(item => seen.Add(item.Word)).OrderBy(item => item.Word, StringComparer.Ordinal))
        {
            var before = Tally(form.Before);
            var after = Tally(form.After);
            var retired = Minus(before, after);
            var added = Minus(after, before);
            var beforeCount = before.Values.Sum(item => item.Count);
            var afterCount = after.Values.Sum(item => item.Count);
            var classes = new HashSet<string>(StringComparer.Ordinal);
            if (beforeCount > 0 && afterCount == 0) classes.Add(form.Negative ? ReadingDeltaClasses.Rejected : ReadingDeltaClasses.Lost);
            if (beforeCount == 0 && afterCount > 0) classes.Add(ReadingDeltaClasses.Gained);
            if (beforeCount > 0 && afterCount > 0 && !before.Keys.Any(after.ContainsKey)) classes.Add(ReadingDeltaClasses.Changed);
            if (beforeCount > 0 && retired.Count == 0 && afterCount > beforeCount) classes.Add(ReadingDeltaClasses.Doubled);
            result.Add(new ReadingDeltaForm(form.Word, form.CaseKey, beforeCount, afterCount, Ordered(retired), Ordered(added), classes,
                SenseChangePairs(retired, added)));
        }
        return result;
    }

    // Pairs by equal sense-free identity, one copy to one.
    private static long SenseChangePairs(Dictionary<string, (ReadingSignature Signature, long Count)> retired,
        Dictionary<string, (ReadingSignature Signature, long Count)> added)
    {
        var open = added.Values.Select(item => (item.Signature.SenseFree().Id, Count: item.Count)).ToList();
        long pairs = 0;
        foreach (var item in retired.Values.OrderBy(item => item.Signature.Id, StringComparer.Ordinal))
        {
            var left = item.Count;
            var key = item.Signature.SenseFree().Id;
            for (var index = 0; index < open.Count && left > 0; index++)
            {
                if (open[index].Id != key) continue;
                var take = Math.Min(left, open[index].Count);
                left -= take;
                pairs += take;
                open[index] = (key, open[index].Count - take);
            }
        }
        return pairs;
    }

    private static Dictionary<string, (ReadingSignature Signature, long Count)> Tally(IEnumerable<ReadingCopy> readings)
    {
        var tally = new Dictionary<string, (ReadingSignature Signature, long Count)>(StringComparer.Ordinal);
        foreach (var reading in readings)
            tally[reading.Id] = (reading.Signature, tally.TryGetValue(reading.Id, out var found) ? found.Count + 1 : 1);
        return tally;
    }

    private static Dictionary<string, (ReadingSignature Signature, long Count)> Minus(
        Dictionary<string, (ReadingSignature Signature, long Count)> left,
        Dictionary<string, (ReadingSignature Signature, long Count)> right)
    {
        var result = new Dictionary<string, (ReadingSignature Signature, long Count)>(StringComparer.Ordinal);
        foreach (var (id, item) in left)
        {
            var remaining = item.Count - (right.TryGetValue(id, out var other) ? other.Count : 0);
            if (remaining > 0) result[id] = (item.Signature, remaining);
        }
        return result;
    }

    private static IReadOnlyList<(ReadingSignature Signature, long Count)> Ordered(
        Dictionary<string, (ReadingSignature Signature, long Count)> readings) =>
        [.. readings.Values.OrderBy(item => ReadingManifestFormat.Key(item.Signature), StringComparer.Ordinal)];
}
