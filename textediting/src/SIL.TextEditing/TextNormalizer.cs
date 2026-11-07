using System.Text;

namespace SIL.TextEditing
{
    /// <summary>
    /// Unicode normalisation that never throws on ill-formed UTF-16: <see cref="string.Normalize(NormalizationForm)"/> does,
    /// on a lone surrogate. Well-formed stretches are normalised; each lone surrogate is kept as it is and ends the
    /// stretch before it, so nothing composes across it.
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
                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) { i++; continue; }
                if (!char.IsSurrogate(c)) continue;
                sb = sb ?? new StringBuilder(text.Length);
                sb.Append(text.Substring(start, i - start).Normalize(form)).Append(c);
                start = i + 1;
            }
            if (sb == null) return text.Normalize(form);
            return sb.Append(text.Substring(start).Normalize(form)).ToString();
        }

        /// <summary>True when the text ends in a high surrogate that has no low surrogate after it yet.</summary>
        public static bool EndsWithLoneHighSurrogate(string text) =>
            text.Length > 0 && char.IsHighSurrogate(text[text.Length - 1]);
    }
}
