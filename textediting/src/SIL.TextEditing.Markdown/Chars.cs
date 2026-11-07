namespace SIL.TextEditing.Markdown
{
    /// <summary>Characters the notation treats specially, named so no source line holds an invisible one.</summary>
    internal static class Chars
    {
        public const char LineSeparator = (char)0x2028;
        public const string LineSeparatorText = "\x2028";
        public const char ParagraphSeparator = (char)0x2029;
        public const char ByteOrderMark = (char)0xFEFF;
        public const char Tab = (char)9;
        public const char ObjectReplacement = (char)0xFFFC;
        public const char NoBreakSpace = (char)0xA0;
        public const char ZeroWidthJoiner = (char)0x200D;

        /// <summary>A character that takes no space and draws nothing, but changes how text breaks, joins or runs: zero-width space, the
        /// joiners, word joiner, soft hyphen, the bidi marks, embeddings and isolates, the invisible operators, the Mongolian vowel separator
        /// and the combining grapheme joiner. The writer makes it a reference so a person or an agent editing the Markdown can see it.</summary>
        public static bool IsInvisible(char c) =>
            c == 0xAD || c == 0x34F || c == 0x61C || c == 0x180E
            || (c >= 0x200B && c <= 0x200F) || (c >= 0x202A && c <= 0x202E) || (c >= 0x2060 && c <= 0x2064) || (c >= 0x2066 && c <= 0x2069);

        /// <summary>Whether the zero-width joiner at <paramref name="at"/> joins two emoji (as in a family or a profession emoji), where a
        /// reference would only add noise. The emoji before may carry a presentation selector or a skin tone.</summary>
        public static bool JoinsEmoji(string text, int at)
        {
            int before = at - 1;
            if (before >= 0 && text[before] == 0xFE0F) before--;
            if (before >= 1 && char.IsLowSurrogate(text[before]) && char.IsHighSurrogate(text[before - 1]))
            {
                int tone = char.ConvertToUtf32(text[before - 1], text[before]);
                if (tone >= 0x1F3FB && tone <= 0x1F3FF) before -= 2;
            }
            return Pictographic(text, before, back: true) && Pictographic(text, at + 1, back: false);
        }

        static bool Pictographic(string text, int i, bool back)
        {
            if (i < 0 || i >= text.Length) return false;
            int cp = text[i];
            if (back && char.IsLowSurrogate(text[i]) && i >= 1 && char.IsHighSurrogate(text[i - 1])) cp = char.ConvertToUtf32(text[i - 1], text[i]);
            else if (!back && char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) cp = char.ConvertToUtf32(text[i], text[i + 1]);
            return (cp >= 0x1F000 && cp <= 0x1FAFF) || (cp >= 0x2600 && cp <= 0x27BF) || (cp >= 0x2300 && cp <= 0x23FF) || (cp >= 0x2B00 && cp <= 0x2BFF);
        }
    }
}
