using System.Globalization;
using System.Text;
using SIL.Motif.Runner.Operations;
using SIL.TextEditing;

namespace SIL.Motif.App.Compare;

/// <summary>What a token is: a word, a stretch of white space, one punctuation or symbol cluster, or an embedded object.</summary>
public enum TokenKind
{
    Word,
    Space,
    Other,
    Object,
}

/// <summary>
/// One unit the sort compares: a word (a run of grapheme clusters that form words), a stretch of white space, one
/// other cluster or one embedded object, with the pieces of runs that make it up. Text is NFD, so a change of
/// normalisation alone is no difference, and a base character is never parted from its combining marks.
/// </summary>
public sealed class Token
{
    internal Token(string text, TokenKind kind, IReadOnlyList<TextRun> pieces)
    {
        Text = text;
        Kind = kind;
        Pieces = pieces;
        Format = string.Join("|", pieces.Select(piece => Tokens.FormatOf(piece) + "#" + piece.Text.Length));
        WritingSystems = string.Join("|", pieces.Select(piece => piece.Ws + "#" + piece.Text.Length));
        FormatWithoutWritingSystem = string.Join("|", pieces.Select(piece => Tokens.FormatOf(piece with { Ws = "" }) + "#" + piece.Text.Length));
    }

    /// <summary>The token's text, NFD.</summary>
    public string Text { get; }

    /// <summary>What the token is.</summary>
    public TokenKind Kind { get; }

    /// <summary>The runs, cut to this token, that make it up.</summary>
    public IReadOnlyList<TextRun> Pieces { get; }

    /// <summary>Everything about the token's formatting: each piece's writing system, style, properties and object data.</summary>
    public string Format { get; }

    internal string WritingSystems { get; }

    internal string FormatWithoutWritingSystem { get; }

    public override string ToString() => Text;
}

/// <summary>Cuts runs into <see cref="Token"/>s and joins tokens back into runs.</summary>
public static class Tokens
{
    private const char ObjectCharacter = '￼';

    /// <summary>The tokens of <paramref name="runs"/>, in order.</summary>
    public static IReadOnlyList<Token> Of(IReadOnlyList<TextRun> runs)
    {
        var normalised = runs.Select(run => run with { Text = run.Text.Normalize(NormalizationForm.FormD) })
            .Where(run => run.Text.Length > 0).ToArray();
        var text = string.Concat(normalised.Select(run => run.Text));
        var starts = GraphemeClusters.Starts(text);
        var tokens = new List<Token>();
        var index = 0;
        while (index < starts.Count)
        {
            var kind = KindAt(text, starts[index]);
            var end = index + 1;
            if (kind is TokenKind.Word or TokenKind.Space)
                while (end < starts.Count && KindAt(text, starts[end]) == kind) end++;
            var from = starts[index];
            var to = end < starts.Count ? starts[end] : text.Length;
            tokens.Add(new Token(text[from..to], kind, Slice(normalised, from, to)));
            index = end;
        }
        return tokens;
    }

    /// <summary>The runs <paramref name="tokens"/> make, with neighbouring pieces of the same formatting joined.</summary>
    public static IReadOnlyList<TextRun> Runs(IEnumerable<Token> tokens) => Join(tokens.SelectMany(token => token.Pieces));

    /// <summary><paramref name="runs"/> with neighbouring runs of the same formatting joined and empty runs dropped.</summary>
    public static IReadOnlyList<TextRun> Join(IEnumerable<TextRun> runs)
    {
        var joined = new List<TextRun>();
        foreach (var run in runs)
        {
            if (run.Text.Length == 0) continue;
            // An object run stays alone, so its object data is never stretched over text.
            if (joined.Count > 0 && run.ObjData is null && joined[^1].ObjData is null && FormatOf(joined[^1]) == FormatOf(run))
                joined[^1] = joined[^1] with { Text = joined[^1].Text + run.Text };
            else joined.Add(run);
        }
        return joined;
    }

    internal static string FormatOf(TextRun run) =>
        run.Ws + "\u0001" + run.CharacterStyle + "\u0001" + TextVersions.Signature(run.Props) + "\u0001" + run.ObjData;

    private static TokenKind KindAt(string text, int offset)
    {
        if (text[offset] == ObjectCharacter) return TokenKind.Object;
        var rune = Rune.GetRuneAt(text, offset);
        if (Rune.IsWhiteSpace(rune)) return TokenKind.Space;
        return Rune.GetUnicodeCategory(rune) switch
        {
            UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter
                or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter or UnicodeCategory.NonSpacingMark
                or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark or UnicodeCategory.DecimalDigitNumber
                or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber or UnicodeCategory.ModifierSymbol => TokenKind.Word,
            _ => TokenKind.Other,
        };
    }

    private static IReadOnlyList<TextRun> Slice(IReadOnlyList<TextRun> runs, int from, int to)
    {
        var pieces = new List<TextRun>();
        var at = 0;
        foreach (var run in runs)
        {
            var end = at + run.Text.Length;
            var start = Math.Max(at, from);
            var stop = Math.Min(end, to);
            if (start < stop) pieces.Add(run with { Text = run.Text[(start - at)..(stop - at)] });
            at = end;
            if (at >= to) break;
        }
        return pieces;
    }
}
