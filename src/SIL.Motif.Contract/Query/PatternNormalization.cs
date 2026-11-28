using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Query;

/// <summary>
/// Query matches text in one normal form, NFC: the pattern is composed before it is compiled and each value is composed before it is
/// matched, so a pattern written decomposed (a Tamil two-part vowel sign as two code points) and one written composed count the same words.
/// A pattern whose composing would change what it means (a character class or range holding a base and its mark, or a mark that a
/// quantifier binds to alone) is refused with <see cref="RefusalCodes.QueryPatternNotNormalizable"/>, never matched on mismatched forms.
/// </summary>
public static class PatternNormalization
{
    /// <summary>The value as matched: NFC, with the ASCII and already-composed fast paths.</summary>
    public static string Value(string text) => text.IsNormalized(NormalizationForm.FormC) ? text : text.Normalize(NormalizationForm.FormC);

    /// <summary>Whether <paramref name="text"/> is in the form Query matches in.</summary>
    public static bool IsNormal(string text) => text.IsNormalized(NormalizationForm.FormC);

    /// <summary>The pattern composed to NFC.</summary>
    /// <exception cref="QueryRefusalException">The pattern changes meaning when composed.</exception>
    public static string Pattern(string pattern)
    {
        if (IsNormal(pattern)) return pattern;
        var result = new StringBuilder(pattern.Length);
        var run = new StringBuilder();
        void FlushRun(int next)
        {
            if (run.Length == 0) return;
            var text = run.ToString();
            var composed = text.Normalize(NormalizationForm.FormC);
            if (composed != text && next < pattern.Length && pattern[next] is '*' or '+' or '?' or '{' && IsMark(text[^1]))
                throw Refuse(pattern, "a quantifier follows a combining mark that composes with the letter before it, so the quantifier would bind to a different character once composed. Group the whole letter, such as (ொ)?, or write the pattern composed");
            result.Append(composed);
            run.Clear();
        }
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c == '\\')
            {
                FlushRun(i);
                result.Append(c);
                if (i + 1 < pattern.Length) result.Append(pattern[++i]);
            }
            else if (c == '[')
            {
                FlushRun(i);
                var end = ClassEnd(pattern, i);
                if (end < 0) { result.Append(pattern, i, pattern.Length - i); break; }
                var body = pattern.Substring(i, end - i + 1);
                if (!IsNormal(body)) throw Refuse(pattern, $"the character class {body} holds a letter and a combining mark that compose, which would change which characters it matches. List the composed letter, or the marks apart from their letters");
                result.Append(body);
                i = end;
            }
            else if (char.IsAscii(c) && !char.IsLetterOrDigit(c))
            {
                FlushRun(i);
                result.Append(c);
            }
            else run.Append(c);
        }
        FlushRun(pattern.Length);
        return result.ToString();
    }

    private static bool IsMark(char c) => CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark;

    // A "]" right after "[" or "[^" is literal.
    private static int ClassEnd(string pattern, int start)
    {
        var i = start + 1;
        if (i < pattern.Length && pattern[i] == '^') i++;
        if (i < pattern.Length && pattern[i] == ']') i++;
        for (; i < pattern.Length; i++)
        {
            if (pattern[i] == '\\') i++;
            else if (pattern[i] == ']') return i;
        }
        return -1;
    }

    private static QueryRefusalException Refuse(string pattern, string why) => new(new Refusal(
        RefusalCodes.QueryPatternNotNormalizable, FailureReason.InvalidArgument,
        $"The pattern {pattern} cannot be composed to NFC safely: {why}.",
        new Dictionary<string, string>(StringComparer.Ordinal) { ["pattern"] = pattern }));
}
