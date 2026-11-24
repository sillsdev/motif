using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Query.Phon;

/// <summary>One match of a pattern in a word: segment and byte spans and the text they cover.</summary>
/// <param name="Start">The first segment, counting from 0.</param>
/// <param name="End">One past the last segment.</param>
/// <param name="ByteStart">The UTF-8 byte offset of the start in the word's NFD form.</param>
/// <param name="ByteEnd">The UTF-8 byte offset of the end.</param>
/// <param name="Text">The matched text, from the NFD form.</param>
/// <param name="TargetStart">For an <c>env</c>, the first segment of the target inside the match; null when the environment's context has no fixed width.</param>
/// <param name="TargetEnd">For an <c>env</c>, one past the target's last segment.</param>
public sealed record QueryMatchSpan(int Start, int End, int ByteStart, int ByteEnd, string Text, int? TargetStart = null, int? TargetEnd = null);

/// <summary>The matches of one <c>phon</c> or <c>env</c> predicate in a row's form.</summary>
/// <param name="Kind"><c>phon</c> or <c>env</c>.</param>
/// <param name="Pattern">The predicate's pattern or environment text.</param>
/// <param name="Spans">Every match, in order.</param>
public sealed record QueryMatch(string Kind, string Pattern, IReadOnlyList<QueryMatchSpan> Spans);

/// <summary>A word PanGloss did not match, and why.</summary>
/// <param name="Word">The word.</param>
/// <param name="Reason">PanGloss's reason, such as <c>no_character_definition</c> or <c>work_limit</c>.</param>
/// <param name="CharIndex">For an unsegmentable word, the character where segmentation stopped; null otherwise.</param>
public sealed record QuerySkippedWord(string Word, string Reason, int? CharIndex = null);

/// <summary>What a pattern query could not decide, counted so it is never dropped silently.</summary>
/// <param name="Candidates">The distinct forms sent to PanGloss after the cheaper predicates had filtered the rows.</param>
/// <param name="Unsegmentable">Candidates the grammar's surface table cannot segment; they match no pattern and no negation of one.</param>
/// <param name="UnsegmentableSample">The first few of them.</param>
/// <param name="Incomplete">Candidates PanGloss stopped on (a work, deadline or match cap) and returned no decision for.</param>
/// <param name="IncompleteSample">The first few of them.</param>
/// <param name="Complete">Whether every request ran to its end; false when PanGloss stopped one at a cap.</param>
/// <param name="StopReason">PanGloss's first stop reason when <paramref name="Complete"/> is false.</param>
/// <param name="ParserVersion">The PanGloss version that answered.</param>
/// <param name="Requests">The <c>match</c> requests sent; one per pattern per batch, never one per word.</param>
public sealed record PhonReport(int Candidates, int Unsegmentable, IReadOnlyList<QuerySkippedWord> UnsegmentableSample,
    int Incomplete, IReadOnlyList<QuerySkippedWord> IncompleteSample, bool Complete, string? StopReason, string? ParserVersion, int Requests);

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

    /// <summary>Applies the pattern text to the words and returns one result per word in order; one request, never one process per word.</summary>
    /// <exception cref="QueryRefusalException">The matcher is unavailable or refused the pattern; the refusal says why.</exception>
    PhonBatchResult Match(string patternText, IReadOnlyList<string> words, CancellationToken cancellationToken);
}
