using System;
using System.Text;

namespace SIL.TextEditing
{
    /// <summary>
    /// Unicode normalisation that never throws on ill-formed UTF-16: <see cref="string.Normalize(NormalizationForm)"/> does,
    /// on a lone surrogate or a noncharacter. Normalizable stretches are normalized; rejected code points are kept
    /// as they are and end the stretch before them, so nothing composes across them.
    /// </summary>
    public static class TextNormalizer
    {
        public static string Normalize(string text, NormalizationForm form)
        {
            if (text.Length == 0) return text;
            StringBuilder? sb = null;
            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                int length = char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]) ? 2 : 1;
                int cp = length == 2 ? char.ConvertToUtf32(c, text[i + 1]) : c;
                if (!char.IsSurrogate(c) && !IsNoncharacter(cp) || length == 2 && !IsNoncharacter(cp)) { i += length - 1; continue; }
                sb = sb ?? new StringBuilder(text.Length);
                sb.Append(NormalizeStretch(text.Substring(start, i - start), form)).Append(text, i, length);
                i += length - 1;
                start = i + 1;
            }
            if (sb == null) return NormalizeStretch(text, form);
            return sb.Append(NormalizeStretch(text.Substring(start), form)).ToString();
        }

        internal static bool IsNoncharacter(int cp) => cp >= 0xFDD0 && cp <= 0xFDEF || (cp & 0xFFFE) == 0xFFFE;

        static string NormalizeStretch(string text, NormalizationForm form)
        {
            try { return text.Normalize(form); }
            catch (ArgumentException)
            {
                var result = new StringBuilder(text.Length);
                int start = 0;
                for (int i = 0; i < text.Length; i++)
                {
                    int length = char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]) ? 2 : 1;
                    try { text.Substring(i, length).Normalize(form); }
                    catch (ArgumentException)
                    {
                        result.Append(NormalizeOrKeep(text.Substring(start, i - start), form)).Append(text, i, length);
                        start = i + length;
                    }
                    i += length - 1;
                }
                return result.Append(NormalizeOrKeep(text.Substring(start), form)).ToString();
            }
        }

        static string NormalizeOrKeep(string text, NormalizationForm form)
        {
            try { return text.Normalize(form); }
            catch (ArgumentException) { return text; }
        }

        /// <summary>True when the text ends in a high surrogate that has no low surrogate after it yet.</summary>
        public static bool EndsWithLoneHighSurrogate(string text) =>
            text.Length > 0 && char.IsHighSurrogate(text[text.Length - 1]);
    }
}
