using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SIL.Motif.Host.PanGloss;

namespace SIL.Motif.Commands.Queries;

/// <summary>How PanGloss's parse of one word ended.</summary>
internal enum WordParseStatus
{
    /// <summary>The parse ran to the end; it may have found no analysis.</summary>
    Completed,
    /// <summary>The attempt or work limit stopped the parse, so analyses may be missing.</summary>
    Capped,
    /// <summary>The word timeout stopped the parse, so analyses may be missing.</summary>
    TimedOut,
    /// <summary>PanGloss could not segment the word with the grammar's characters.</summary>
    Unsegmentable,
    /// <summary>The resident parser did not answer.</summary>
    Failed,
}

/// <summary>One morph of an analysis, named by the authored identity PanGloss reports for it.</summary>
internal sealed record ParsedMorph(string EntryId, string MsaId);

/// <summary>One analysis of a word: its morphs in order, and the surface shape PanGloss gives for them.</summary>
internal sealed record ParsedAnalysis(IReadOnlyList<ParsedMorph> Morphs, string Surface);

/// <summary>What the resident parser said of one word.</summary>
internal sealed record WordParse(WordParseStatus Status, IReadOnlyList<ParsedAnalysis> Analyses, string? Detail = null);

/// <summary>Parses single words with the grammar, giving each analysis's morphs; a test replays recorded replies.</summary>
internal interface IRealizesParser
{
    WordParse Parse(string word, CancellationToken cancellationToken);
}

/// <summary>Parses through the warm pool's resident <c>pangloss serve</c>, asking for the trace-details document that names each morph.</summary>
internal sealed class ResidentRealizesParser(WarmPanGlossPool pool, string key, string stateToken, string grammarPath) : IRealizesParser
{
    public WordParse Parse(string word, CancellationToken cancellationToken)
    {
        var reply = pool.TryWordAsync(key, stateToken, grammarPath, word, traceDetails: true, cancellationToken).GetAwaiter().GetResult();
        return reply switch
        {
            WarmParseResult.Completed completed => ServeParseReply.Read(completed.Status, completed.Json),
            WarmParseResult.Fallback fallback => new WordParse(WordParseStatus.Failed, [], fallback.Code + ": " + fallback.Message),
            _ => throw new OperationCanceledException(cancellationToken),
        };
    }
}

/// <summary>Reads the trace-details document of a serve <c>parse</c> reply down to the analyses' morph identities.</summary>
internal static class ServeParseReply
{
    /// <summary><paramref name="status"/> is the envelope's status and <paramref name="resultJson"/> its <c>result</c> document.</summary>
    public static WordParse Read(string? status, string resultJson)
    {
        try
        {
            using var document = JsonDocument.Parse(resultJson);
            var root = document.RootElement;
            var analyses = new List<ParsedAnalysis>();
            if (root.TryGetProperty("result", out var result) && result.TryGetProperty("analyses", out var list))
                foreach (var analysis in list.EnumerateArray())
                {
                    var morphs = new List<ParsedMorph>();
                    if (analysis.TryGetProperty("morphs", out var morphList))
                        foreach (var morph in morphList.EnumerateArray())
                            if (morph.TryGetProperty("identity", out var identity) &&
                                identity.TryGetProperty("entryId", out var entry) && entry.GetString() is { } entryId)
                                morphs.Add(new ParsedMorph(entryId, identity.TryGetProperty("msaId", out var msa) ? msa.GetString() ?? "" : ""));
                    analyses.Add(new ParsedAnalysis(morphs, analysis.TryGetProperty("surface", out var surface) ? surface.GetString() ?? "" : ""));
                }
            return new WordParse(Classify(status, root), analyses);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return new WordParse(WordParseStatus.Failed, [], "PanGloss wrote a parse answer Motif cannot read: " + exception.Message);
        }
    }

    private static WordParseStatus Classify(string? status, JsonElement root)
    {
        if (status is "SKIPPED") return WordParseStatus.Unsegmentable;
        if (status is "TIMEOUT") return WordParseStatus.TimedOut;
        if (status is "CAP") return WordParseStatus.Capped;
        if (root.TryGetProperty("search", out var search))
        {
            if (search.TryGetProperty("timedOut", out var timedOut) && timedOut.ValueKind == JsonValueKind.True) return WordParseStatus.TimedOut;
            if (search.TryGetProperty("capped", out var capped) && capped.ValueKind == JsonValueKind.True) return WordParseStatus.Capped;
        }
        return WordParseStatus.Completed;
    }
}

/// <summary>Where each morph of an analysis sits in the word, read from the surface shape PanGloss reports.</summary>
internal static class SurfaceShape
{
    /// <summary>
    /// The text of each morph and where it starts, or null when the shape does not line up with the word. A shape is the morphs'
    /// surface characters in order, joined by <c>+?</c>; a character is plain or a bracketed set of the spellings it stands for.
    /// </summary>
    public static IReadOnlyList<(string Text, int Start)>? Pieces(string word, string surface, int morphCount)
    {
        var shapes = surface.Split("+?");
        if (shapes.Length != morphCount) return null;
        var at = 0;
        var pieces = new List<(string, int)>();
        foreach (var shape in shapes)
        {
            var start = at;
            foreach (var alternatives in Units(shape))
            {
                var hit = alternatives.FirstOrDefault(alternative => alternative.Length > 0 && at + alternative.Length <= word.Length &&
                    string.CompareOrdinal(word, at, alternative, 0, alternative.Length) == 0);
                if (hit is null) return null;
                at += hit.Length;
            }
            pieces.Add((word[start..at], start));
        }
        // A piece is M's only when every morph's piece, joined in order, is the word itself.
        return at == word.Length && string.Concat(pieces.Select(piece => piece.Item1)).Equals(word, StringComparison.OrdinalIgnoreCase) ? pieces : null;
    }

    // A unit lists the spellings it stands for, longest first; a plain character stands for itself.
    private static IEnumerable<string[]> Units(string shape)
    {
        var i = 0;
        while (i < shape.Length)
        {
            if (shape[i] != '[')
            {
                var length = char.IsHighSurrogate(shape[i]) && i + 1 < shape.Length ? 2 : 1;
                yield return [shape.Substring(i, length)];
                i += length;
                continue;
            }
            var close = shape.IndexOf(']', i);
            if (close < 0) yield break;
            yield return [.. Spellings(shape[(i + 1)..close].Replace("()/", "", StringComparison.Ordinal)).OrderByDescending(s => s.Length)];
            i = close + 1;
        }
    }

    private static IEnumerable<string> Spellings(string set)
    {
        var i = 0;
        while (i < set.Length)
        {
            if (set[i] == '(')
            {
                var close = set.IndexOf(')', i);
                if (close < 0) yield break;
                yield return set[(i + 1)..close];
                i = close + 1;
            }
            else
            {
                var length = char.IsHighSurrogate(set[i]) && i + 1 < set.Length ? 2 : 1;
                yield return set.Substring(i, length);
                i += length;
            }
        }
    }
}

/// <summary>Chooses which word types to trace: all of them, or a deterministic sample weighted by how often each occurs.</summary>
internal static class RealizesSample
{
    /// <summary>The number of matching word types traced when no sample size is given.</summary>
    public const int Default = 200;

    /// <summary>
    /// With <paramref name="size"/> at zero or at least the number of types, every type, most frequent first. Otherwise the
    /// <paramref name="size"/> types with the highest weighted-sampling keys, where a type's key is log(u)/tokens and u comes
    /// from a hash of the word, so the same corpus and size always give the same sample.
    /// </summary>
    public static IReadOnlyList<CorpusEntryRef> Choose(IReadOnlyList<(string Form, long Tokens)> words, int size)
    {
        var ranked = words.Select(word => new CorpusEntryRef(word.Form, word.Tokens, Key(word.Form, word.Tokens)));
        var all = size <= 0 || size >= words.Count;
        var ordered = all ? ranked.OrderByDescending(word => word.Tokens).ThenBy(word => word.Form, StringComparer.Ordinal)
            : ranked.OrderByDescending(word => word.Key).ThenBy(word => word.Form, StringComparer.Ordinal);
        return [.. all ? ordered : ordered.Take(size).OrderByDescending(word => word.Tokens).ThenBy(word => word.Form, StringComparer.Ordinal)];
    }

    private static double Key(string form, long tokens)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(form));
        var u = (BitConverter.ToUInt64(hash, 0) >> 11) / (double)(1UL << 53);
        return Math.Log(Math.Max(u, double.Epsilon)) / Math.Max(1, tokens);
    }
}

/// <summary>A word type chosen for parsing.</summary>
internal sealed record CorpusEntryRef(string Form, long Tokens, double Key);
