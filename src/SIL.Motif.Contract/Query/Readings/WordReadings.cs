using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Query.Readings;

/// <summary>The morph classes a project lexicon can give an analysis; null on a morph when no project was named.</summary>
public static class MorphKinds
{
    /// <summary>A stem (root) analysis.</summary>
    public const string Stem = "stem";
    /// <summary>A derivational affix.</summary>
    public const string Derivational = "derivational";
    /// <summary>An inflectional affix.</summary>
    public const string Inflectional = "inflectional";
    /// <summary>Any other affix (unclassified, clitic and so on).</summary>
    public const string Affix = "affix";
}

/// <summary>The parse states a word carries when the grammar parsed the corpus.</summary>
public static class ReadingStates
{
    /// <summary>The parse finished with at least one reading.</summary>
    public const string Parsed = "parsed";
    /// <summary>The parse finished and the grammar gives the word no reading.</summary>
    public const string None = "none";
    /// <summary>The parse stopped at the work limit; readings may be missing.</summary>
    public const string Capped = "capped";
    /// <summary>The parse stopped at the word timeout; readings may be missing.</summary>
    public const string TimedOut = "timed-out";
    /// <summary>The grammar's characters cannot segment the word.</summary>
    public const string Unsegmentable = "unsegmentable";
    /// <summary>The batch wrote no answer for the word.</summary>
    public const string Failed = "failed";

    /// <summary>Every state, in the order the help lists them.</summary>
    public static IReadOnlyList<string> All { get; } = [Parsed, None, Capped, TimedOut, Unsegmentable, Failed];

    /// <summary>Whether the readings of a word in this state may be fewer than the grammar's.</summary>
    public static bool MayBeIncomplete(string state) => state is Capped or TimedOut or Failed;
}

/// <summary>One morph of a reading: its form and analysis ids, the surface text it occupies and, when known, its entry and class.</summary>
/// <param name="Form">The form (allomorph) id PanGloss reports.</param>
/// <param name="Msa">The morphosyntactic analysis id.</param>
/// <param name="Text">The surface text of the morph; empty when PanGloss placed no span.</param>
/// <param name="Start">Where the text starts in the surface, or -1 when no span was placed.</param>
/// <param name="End">Where the text ends (exclusive), or -1.</param>
/// <param name="InflType">PanGloss's inflection type, when it gives one.</param>
/// <param name="Entry">The lexical entry id: from the project lexicon, or from PanGloss when its batch names it; null when neither is known.</param>
/// <param name="Kind">One of <see cref="MorphKinds"/> from the project lexicon; null when no project was named.</param>
/// <param name="Signature">PanGloss's own signature of the morph when its batch gives one; null today.</param>
public sealed record ReadingMorph(string Form, string Msa, string Text, int Start, int End, string? InflType = null,
    string? Entry = null, string? Kind = null, string? Signature = null)
{
    /// <summary>The identity two readings compare a morph by.</summary>
    [JsonIgnore]
    public (string Form, string Msa) Key => (Form, Msa);
}

/// <summary>One reading of a word: its morphs in surface order.</summary>
/// <param name="Morphs">The ordered morphs.</param>
/// <param name="Signature">PanGloss's own signature of the reading when its batch gives one; null today.</param>
public sealed record WordReading(IReadOnlyList<ReadingMorph> Morphs, string? Signature = null);

/// <summary>Every reading of one word type with the state of its parse.</summary>
/// <param name="State">One of <see cref="ReadingStates"/>.</param>
/// <param name="Readings">The readings the parse produced.</param>
public sealed record WordReadings(string State, IReadOnlyList<WordReading> Readings)
{
    /// <summary>Whether the readings may be fewer than the grammar's.</summary>
    public bool MayBeIncomplete => ReadingStates.MayBeIncomplete(State);
}

/// <summary>The cached corpus parse as the readings queries see it.</summary>
public interface IReadingSource
{
    /// <summary>The readings of a word, or null for a word the parse was not run for.</summary>
    WordReadings? Get(string word);
}

/// <summary>A morpheme named for <c>only-via</c> and <c>--what-if-retire</c>: an entry, an analysis or a form id.</summary>
/// <param name="Kind"><c>entry</c>, <c>msa</c> or <c>form</c>; <c>id</c> is a bare guid taken as an analysis or a form id (the command line only).</param>
/// <param name="Id">The id, a guid.</param>
public sealed record ReadingTarget(string Kind, string Id)
{
    /// <summary>The kinds, in the order help lists them.</summary>
    public static IReadOnlyList<string> Kinds { get; } = ["entry", "msa", "form"];

    /// <summary>Whether a morph is the one named.</summary>
    public bool Matches(ReadingMorph morph) => Kind switch
    {
        "entry" => string.Equals(morph.Entry, Id, StringComparison.OrdinalIgnoreCase),
        "msa" => string.Equals(morph.Msa, Id, StringComparison.OrdinalIgnoreCase),
        "form" => string.Equals(morph.Form, Id, StringComparison.OrdinalIgnoreCase),
        "id" => string.Equals(morph.Msa, Id, StringComparison.OrdinalIgnoreCase) || string.Equals(morph.Form, Id, StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

    /// <summary>The text form, <c>entry:ID</c>.</summary>
    public override string ToString() => Kind + ":" + Id;

    /// <summary>Reads <c>entry:ID</c>, <c>msa:ID</c> or <c>form:ID</c>; false when it is neither.</summary>
    public static bool TryParse(string text, out ReadingTarget target)
    {
        target = null!;
        var at = text.IndexOf(':');
        if (at < 0) return false;
        var kind = text[..at].Trim().ToLowerInvariant();
        var id = text[(at + 1)..].Trim();
        if (!(Kinds.Contains(kind) || kind == "id") || !Guid.TryParse(id, out var guid)) return false;
        target = new ReadingTarget(kind, guid.ToString("D"));
        return true;
    }
}

/// <summary>The counts <c>only-via</c> and <c>--what-if-retire</c> read from a word's reading set.</summary>
/// <param name="Readings">How many readings the word has.</param>
/// <param name="Via">How many use the target.</param>
public readonly record struct ViaCount(int Readings, int Via)
{
    /// <summary>The readings that do not use the target.</summary>
    public int Other => Readings - Via;

    /// <summary>The word has a reading and every reading uses the target.</summary>
    public bool OnlyVia => Readings > 0 && Via == Readings;

    /// <summary>Counts the readings of <paramref name="word"/> that use any of the targets.</summary>
    public static ViaCount Of(WordReadings word, IReadOnlyList<ReadingTarget> targets) =>
        new(word.Readings.Count, word.Readings.Count(reading => reading.Morphs.Any(morph => targets.Any(target => target.Matches(morph)))));
}
