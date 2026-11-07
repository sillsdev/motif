using System.Collections.Generic;
using System.Globalization;

namespace SIL.TextEditing
{
    /// <summary>
    /// Repairs the cluster starts that <see cref="StringInfo"/> reports: removes every start at which the UAX #29
    /// rules say "do not break". Runtimes before .NET 5 lack most rules; .NET 10 still lacks GB9c (Indic
    /// conjuncts), so on net10.0 only that rule runs and everything else is StringInfo's. It looks only at the code points around each start, so it works on the
    /// text as it stands and keeps no state between starts. Property classes are approximated from Unicode general
    /// categories plus the few explicit ranges below, which is enough for the rules it implements.
    /// </summary>
    static class ClusterFuser
    {
        public static void Fuse(string text, List<int> starts)
        {
            for (int k = starts.Count - 1; k >= 1; k--)
                if (ForbidsBreak(text, starts[k]))
                    starts.RemoveAt(k);
        }

        static bool ForbidsBreak(string text, int at)
        {
            int next = CodePointAt(text, at);

#if NET10_0_OR_GREATER
            return IsConsonant(next) && ConjunctBefore(text, at);
#else
            // GB9, GB9a: never break before Extend, ZWJ or SpacingMark.
            if (IsExtend(next) || next == 0x200D || IsSpacingMark(next)) return true;

            int prevStart = StartOfCodePointBefore(text, at);
            int prev = CodePointAt(text, prevStart);

            // GB11: ExtPict Extend* ZWJ x ExtPict.
            if (prev == 0x200D && IsExtPict(next) && ExtPictBefore(text, prevStart)) return true;

            // GB12, GB13: regional indicators pair up from the start of their run.
            if (IsRegionalIndicator(prev) && IsRegionalIndicator(next) && RunLengthBefore(text, at) % 2 == 1) return true;

            // GB9c: Consonant [Extend Linker]* Linker [Extend Linker]* x Consonant.
            if (IsConsonant(next) && ConjunctBefore(text, at)) return true;

            return false;
#endif
        }

        static bool ExtPictBefore(string text, int zwjAt)
        {
            int i = zwjAt;
            while (i > 0)
            {
                i = StartOfCodePointBefore(text, i);
                int cp = CodePointAt(text, i);
                if (IsExtPict(cp)) return true;
                if (!IsExtend(cp)) return false;
            }
            return false;
        }

        static int RunLengthBefore(string text, int at)
        {
            int n = 0;
            for (int i = at; i > 0;)
            {
                i = StartOfCodePointBefore(text, i);
                if (!IsRegionalIndicator(CodePointAt(text, i))) break;
                n++;
            }
            return n;
        }

        static bool ConjunctBefore(string text, int at)
        {
            bool sawLinker = false;
            for (int i = at; i > 0;)
            {
                i = StartOfCodePointBefore(text, i);
                int cp = CodePointAt(text, i);
                if (IsLinker(cp)) { sawLinker = true; continue; }
                if (IsConsonant(cp)) return sawLinker;
                if (!IsExtend(cp) && cp != 0x200D) return false;
            }
            return false;
        }

        static int CodePointAt(string text, int i)
        {
            char c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                return char.ConvertToUtf32(c, text[i + 1]);
            return c;
        }

        static int StartOfCodePointBefore(string text, int i)
        {
            int j = i - 1;
            if (j > 0 && char.IsLowSurrogate(text[j]) && char.IsHighSurrogate(text[j - 1])) j--;
            return j;
        }

        static UnicodeCategory Category(int cp) =>
            cp < 0x10000 ? CharUnicodeInfo.GetUnicodeCategory((char)cp) : CharUnicodeInfo.GetUnicodeCategory(char.ConvertFromUtf32(cp), 0);

        static bool IsExtend(int cp)
        {
            if (cp == 0x200C || (cp >= 0x1F3FB && cp <= 0x1F3FF)) return true; // ZWNJ, emoji skin-tone modifiers
            var c = Category(cp);
            return c == UnicodeCategory.NonSpacingMark || c == UnicodeCategory.EnclosingMark;
        }

        static bool IsSpacingMark(int cp) => Category(cp) == UnicodeCategory.SpacingCombiningMark;

        static bool IsRegionalIndicator(int cp) => cp >= 0x1F1E6 && cp <= 0x1F1FF;

        static bool IsExtPict(int cp) =>
            (cp >= 0x1F000 && cp <= 0x1FAFF) || (cp >= 0x2600 && cp <= 0x27BF) || (cp >= 0x2B05 && cp <= 0x2B55) ||
            (cp >= 0x231A && cp <= 0x23FF) || (cp >= 0x2194 && cp <= 0x21AA) || (cp >= 0x25AA && cp <= 0x25FE) ||
            cp == 0x00A9 || cp == 0x00AE || cp == 0x203C || cp == 0x2049 || cp == 0x2122 || cp == 0x2139 ||
            cp == 0x24C2 || cp == 0x2934 || cp == 0x2935 || cp == 0x3030 || cp == 0x303D || cp == 0x3297 || cp == 0x3299;

        // Indic conjuncts: the scripts Unicode lists (Devanagari, Bengali, Gujarati, Oriya, Telugu, Malayalam).
        static bool IsLinker(int cp) =>
            cp == 0x094D || cp == 0x09CD || cp == 0x0ACD || cp == 0x0B4D || cp == 0x0C4D || cp == 0x0D4D;

        static bool IsConsonant(int cp) =>
            (cp >= 0x0915 && cp <= 0x0939) || (cp >= 0x0958 && cp <= 0x095F) || (cp >= 0x0978 && cp <= 0x097F) || // Devanagari
            (cp >= 0x0995 && cp <= 0x09B9) || cp == 0x09DC || cp == 0x09DD || cp == 0x09DF || cp == 0x09F0 || cp == 0x09F1 || // Bengali
            (cp >= 0x0A95 && cp <= 0x0AB9) || cp == 0x0AF9 || // Gujarati
            (cp >= 0x0B15 && cp <= 0x0B39) || cp == 0x0B5C || cp == 0x0B5D || cp == 0x0B5F || cp == 0x0B71 || // Oriya
            (cp >= 0x0C15 && cp <= 0x0C39) || cp == 0x0C58 || cp == 0x0C59 || cp == 0x0C5A || // Telugu
            (cp >= 0x0D15 && cp <= 0x0D3A); // Malayalam
    }
}
