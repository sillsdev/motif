using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Query.Phon;

/// <summary>One match of a pattern in a word: segment and byte spans and the text they cover.</summary>
/// <param name="Start">The first segment, counting from 0.</param>
/// <param name="End">One past the last segment.</param>
/// <param name="ByteStart">The UTF-8 byte offset of the start in the word's NFD form.</param>
/// <param name="ByteEnd">The UTF-8 byte offset of the end.</param>
/// <param name="Text">The matched text, from the NFD form.</param>
/// <param name="TargetStart">For an <c>env</c>, the first segment of the target, which is the span PanGloss reports; null for <c>phon</c>.</param>
/// <param name="TargetEnd">For an <c>env</c>, one past the target's last segment.</param>
public sealed record QueryMatchSpan(int Start, int End, int ByteStart, int ByteEnd, string Text, int? TargetStart = null, int? TargetEnd = null);

/// <summary>The matches of one <c>phon</c> or <c>env</c> predicate in a row's form.</summary>
/// <param name="Kind"><c>phon</c> or <c>env</c>.</param>
/// <param name="Pattern">The predicate's pattern or environment text.</param>
/// <param name="Spans">Every match, in order.</param>
/// <param name="Partial">PanGloss stopped on this word, so these are only the matches it kept before the stop.</param>
public sealed record QueryMatch(string Kind, string Pattern, IReadOnlyList<QueryMatchSpan> Spans,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] bool Partial = false);

/// <summary>A word PanGloss did not match, and why.</summary>
/// <param name="Word">The word.</param>
/// <param name="Reason">PanGloss's reason, such as <c>no_character_definition</c> or <c>work_limit</c>.</param>
/// <param name="CharIndex">For an unsegmentable word, the character where segmentation stopped; null otherwise.</param>
public sealed record QuerySkippedWord(string Word, string Reason, int? CharIndex = null);

/// <summary>What a pattern query could not decide, counted so it is never dropped silently.</summary>
/// <param name="Candidates">The distinct forms sent to PanGloss after the cheaper predicates had filtered the rows.</param>
/// <param name="Unsegmentable">Candidates the grammar's surface table cannot segment; they match no pattern and no negation of one.</param>
/// <param name="UnsegmentableSample">The first few of them.</param>
/// <param name="Incomplete">Candidates PanGloss stopped on (a work, deadline or match cap); one that kept matches still shows them, flagged partial, and the rest decide nothing.</param>
/// <param name="IncompleteSample">The first few of them.</param>
/// <param name="Complete">Whether every request ran to its end; false when PanGloss stopped one at a cap.</param>
/// <param name="StopReason">PanGloss's first stop reason when <paramref name="Complete"/> is false.</param>
/// <param name="ParserVersion">The PanGloss version that answered.</param>
/// <param name="Requests">The <c>match</c> requests sent; one per pattern per batch, never one per word.</param>
/// <param name="SegmentTest">The segment test every pattern ran under, <c>engine</c> or <c>identity</c> (see <see cref="PhonSegmentTest"/>).</param>
/// <param name="MatchMode"><c>identity</c> under <c>--segment-test identity</c> when a pattern holds a literal or a segment-list class, which that test treats as exact members and so may miss segments a rule treats as the same; null otherwise, and always null under <c>engine</c>.</param>
/// <param name="MayUnderReport">True with <paramref name="MatchMode"/> <c>identity</c>: a span list may miss segments a rule would treat as the same.</param>
/// <param name="Warnings">What the answer does not prove, such as absence under <c>not</c> over an identity-tested pattern.</param>
public sealed record PhonReport(int Candidates, int Unsegmentable, IReadOnlyList<QuerySkippedWord> UnsegmentableSample,
    int Incomplete, IReadOnlyList<QuerySkippedWord> IncompleteSample, bool Complete, string? StopReason, string? ParserVersion, int Requests,
    string SegmentTest = PhonSegmentTest.Engine,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? MatchMode = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool MayUnderReport = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Warnings = null);

/// <summary>A natural class of the grammar as PanGloss's <c>inventory</c> op lists it.</summary>
/// <param name="Token">The text between the brackets of <c>[token]</c> that reaches the class; null when none does.</param>
/// <param name="Name">The grammar's label for the class, or null.</param>
/// <param name="Id">The class's id in the grammar.</param>
/// <param name="Kind"><c>segment</c> (an explicit segment list) or <c>feature</c> (a feature structure).</param>
/// <param name="FullName">The class's Name in the analysis writing system as FieldWorks shows it (<c>Vowels</c> for the label <c>V</c>); null when it has none or the grammar was loaded from HermitCrab XML.</param>
public sealed record PhonInventoryClass(string? Token, string? Name, string Id, string Kind, string? FullName = null);

/// <summary>A phonological feature as PanGloss's <c>inventory</c> op lists it.</summary>
/// <param name="Name">The feature's name.</param>
/// <param name="Id">The feature's id in the grammar.</param>
/// <param name="Values">The names of its symbols, in order.</param>
public sealed record PhonInventoryFeature(string Name, string Id, IReadOnlyList<string> Values);

/// <summary>PanGloss's answer to <c>inventory</c>: every natural class and feature of the loaded grammar.</summary>
/// <param name="Classes">The classes in grammar order.</param>
/// <param name="Features">The features in grammar order.</param>
/// <param name="Complete">False when PanGloss stopped at a limit and listed only some.</param>
/// <param name="StopReason">The stop, when <paramref name="Complete"/> is false.</param>
public sealed record PhonInventory(IReadOnlyList<PhonInventoryClass> Classes, IReadOnlyList<PhonInventoryFeature> Features, bool Complete = true, string? StopReason = null);

/// <summary>
/// How PanGloss's <c>match</c> tests a segment against a pattern node. <c>engine</c> is rule application's own test, so a literal
/// matches every segment with its feature bundle and a segment-list <c>[class]</c> matches the union of its members' features; the
/// spans are a superset of what a rule rewrites (positions it leaves unchanged and overlapping spans included). <c>identity</c> is
/// the exact test: a literal names one segment and a class its members. Environment sides are exact under both, as in a rule.
/// </summary>
public static class PhonSegmentTest
{
    /// <summary>Rule application's test; Motif's default, because the point of a pattern is to find the words a rule would touch.</summary>
    public const string Engine = "engine";

    /// <summary>The exact test, for "exactly these letters".</summary>
    public const string Identity = "identity";

    /// <summary>What Motif sends when none is given.</summary>
    public const string Default = Engine;

    /// <summary>The accepted spellings.</summary>
    public static IReadOnlyList<string> All { get; } = [Engine, Identity];

    /// <summary>The mode named, or <see cref="Default"/> when none is; a spelling that is neither is refused.</summary>
    /// <param name="text">What the person or document wrote, or null.</param>
    /// <param name="path">Where it was written, for the refusal.</param>
    /// <exception cref="QueryRefusalException">The text is neither mode.</exception>
    public static string Normalize(string? text, string path = "segmentTest") =>
        text is null ? Default
        : All.Contains(text, StringComparer.Ordinal) ? text
        : throw QueryRefusalException.DocumentInvalid($"The segment test is engine or identity, not '{text}'.", path);

    /// <summary>The <c>matchMode</c> a report carries under <see cref="Identity"/> while a pattern holds a literal or a segment-list class.</summary>
    public const string IdentityLabel = "identity";
}

/// <summary>PanGloss's answer for one word.</summary>
/// <param name="Word">The word as sent.</param>
/// <param name="Status"><c>ok</c>, <c>unsegmentable</c> or <c>incomplete</c>.</param>
/// <param name="Spans">The matches, in order; empty unless the status is <c>ok</c> or a match cap kept some.</param>
/// <param name="Reason">For <c>unsegmentable</c>, PanGloss's reason; for <c>incomplete</c>, its stop.</param>
/// <param name="CharIndex">For <c>unsegmentable</c>, the character where segmentation stopped.</param>
/// <param name="Normalized">The word's NFD form when the offsets are into it rather than into the word.</param>
public sealed record PhonWordResult(string Word, string Status, IReadOnlyList<PhonRawSpan> Spans, string? Reason = null,
    int? CharIndex = null, string? Normalized = null);

/// <summary>A span as PanGloss reports it.</summary>
public sealed record PhonRawSpan(int Start, int End, int ByteStart, int ByteEnd);

/// <summary>PanGloss's answer to one <c>match</c> request.</summary>
public sealed record PhonBatchResult(IReadOnlyList<PhonWordResult> Words, bool Complete, string? StopReason);

/// <summary>Runs a pattern over words on the grammar's own matcher: PanGloss's <c>match</c> serve op in a product, a fake in a test.</summary>
public interface IPhonMatcher
{
    /// <summary>The version of the matcher that answers, or null when unknown.</summary>
    string? ParserVersion { get; }

    /// <summary>The grammar's classes and features from PanGloss's <c>inventory</c> op, or null when unavailable.</summary>
    PhonInventory? Inventory(CancellationToken cancellationToken) => null;

    /// <summary>Applies the pattern to the words under the segment test and returns one result per word, in order, in one request.</summary>
    /// <exception cref="QueryRefusalException">The matcher is unavailable or refused the pattern; the refusal says why.</exception>
    PhonBatchResult Match(string patternText, IReadOnlyList<string> words, string segmentTest, CancellationToken cancellationToken);
}
