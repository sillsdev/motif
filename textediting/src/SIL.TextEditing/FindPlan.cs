using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SIL.TextEditing
{
    /// <summary>One match in a paragraph: [Start, End) in the paragraph's own offsets, and for a regular expression the
    /// replacement with its group references filled in.</summary>
    internal readonly struct Hit
    {
        public Hit(int start, int end, string? expansion)
        {
            Start = start;
            End = end;
            Expansion = expansion;
        }

        public int Start { get; }
        public int End { get; }
        public string? Expansion { get; }
    }

    /// <summary>The search itself (ADR 0025): ordinal comparison over NFD text with our own case folding and mark stripping, so
    /// both target frameworks give the same answers (the platform's culture-aware compare differs between .NET Framework and
    /// .NET). Offsets found in the NFD text are mapped back to the paragraph's own text, which may store another form.</summary>
    internal sealed class FindPlan
    {
        readonly FindOptions _o;
        readonly bool _diacriticsMatter;
        readonly char[] _key = Array.Empty<char>();
        readonly Regex? _regex;
        readonly IWordRules? _rules;

        FindPlan(FindOptions options, IWordRules? rules)
        {
            _rules = rules;
            _o = options.Clone();
            _diacriticsMatter = _o.MatchDiacritics || _o.MatchCase;
            var nfd = TextNormalizer.Normalize(_o.Pattern, NormalizationForm.FormD);
            if (_o.UseRegularExpression)
            {
                try { _regex = new Regex(nfd, (_o.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase) | RegexOptions.CultureInvariant, _o.RegexTimeout); }
                catch (ArgumentException) { Valid = false; }
            }
            else
            {
                var key = new List<char>();
                Fold(nfd, key, null);
                _key = key.ToArray();
            }
        }

        /// <param name="rules">The word rule a whole-word search uses (<see cref="DocumentEditor.WordRules"/>); null for the default.</param>
        public static FindPlan Create(FindOptions options, IWordRules? rules = null) => new FindPlan(options, rules);

        /// <summary>False when the pattern is a regular expression that does not compile: nothing matches.</summary>
        public bool Valid { get; } = true;

        /// <summary>True once a regular-expression search ran out of time: the plan then matches nothing more.</summary>
        public bool TimedOut { get; private set; }

        /// <summary>A conjoining vowel or final-consonant jamo: it continues a syllable that a lead jamo began.</summary>
        static bool IsJamoContinuation(char c) => (c >= (char)0x1160 && c <= (char)0x11FF) || (c >= (char)0xD7B0 && c <= (char)0xD7FF);

        static int CodePointLength(string s, int i) => char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]) ? 2 : 1;

        /// <summary>Combining marks: general category Mn, Mc or Me. The rule FieldWorks uses is the Unicode diacritic and extender
        /// properties, which .NET does not expose; modifier letters and symbols count as letters here (ADR 0025).</summary>
        internal static bool IsMark(string s, int i)
        {
            var c = CharUnicodeInfo.GetUnicodeCategory(s, i);
            return c == UnicodeCategory.NonSpacingMark || c == UnicodeCategory.SpacingCombiningMark || c == UnicodeCategory.EnclosingMark;
        }

        /// <summary>The comparison key of NFD text: embedded objects dropped (they are ignorable), marks dropped when they do
        /// not matter, case folded when it does not matter. <paramref name="index"/> receives the NFD index of each key char.</summary>
        void Fold(string nfd, List<char> key, List<int>? index)
        {
            for (int i = 0; i < nfd.Length;)
            {
                int n = CodePointLength(nfd, i);
                char c = nfd[i];
                bool skip = c == Run.ObjectReplacement || (!_diacriticsMatter && IsMark(nfd, i));
                if (!skip)
                    for (int k = 0; k < n; k++)
                    {
                        key.Add(_o.MatchCase || n > 1 ? nfd[i + k] : char.ToLowerInvariant(nfd[i + k]));
                        index?.Add(i + k);
                    }
                i += n;
            }
        }

        /// <summary>The paragraph's NFD text with, for each NFD offset (and the end), the paragraph offset where a match may
        /// start there (-1 when it may not: inside a character that normalisation split) and where a match ending there ends
        /// (the end of the whole character when it ends inside one).</summary>
        sealed class NfdMap
        {
            public string Nfd = string.Empty;
            public int[] Start = Array.Empty<int>();
            public int[] End = Array.Empty<int>();

            public static NfdMap Build(string t)
            {
                var map = new NfdMap();
                var whole = TextNormalizer.Normalize(t, NormalizationForm.FormD);
                if (whole == t)
                {
                    map.Nfd = t;
                    map.Start = new int[t.Length + 1];
                    map.End = new int[t.Length + 1];
                    for (int i = 0; i <= t.Length; i++)
                    {
                        bool inside = i > 0 && i < t.Length && char.IsLowSurrogate(t[i]) && char.IsHighSurrogate(t[i - 1]);
                        map.Start[i] = inside ? -1 : i;
                        map.End[i] = inside ? i + 1 : i;
                    }
                    return map;
                }
                // Normalise a base and its marks at a time, so each piece is in canonical order and maps back to its own text.
                var nfd = new StringBuilder(whole.Length);
                var start = new List<int>(whole.Length + 1);
                var end = new List<int>(whole.Length + 1);
                for (int i = 0; i < t.Length;)
                {
                    int j = i + CodePointLength(t, i);
                    while (j < t.Length && IsMark(t, j)) j += CodePointLength(t, j);
                    var piece = t.Substring(i, j - i);
                    var n = TextNormalizer.Normalize(piece, NormalizationForm.FormD);
                    if (n == piece)
                        for (int k = 0; k < n.Length; k++)
                        {
                            bool inside = k > 0 && char.IsLowSurrogate(n[k]) && char.IsHighSurrogate(n[k - 1]);
                            start.Add(inside ? -1 : i + k);
                            end.Add(inside ? i + k + 1 : i + k);
                        }
                    else
                        for (int k = 0; k < n.Length; k++) { start.Add(k == 0 ? i : -1); end.Add(k == 0 ? i : j); }
                    nfd.Append(n);
                    i = j;
                }
                start.Add(t.Length);
                end.Add(t.Length);
                map.Nfd = nfd.ToString();
                map.Start = start.ToArray();
                map.End = end.ToArray();
                return map;
            }
        }

        /// <summary>The matches in one paragraph, in order. Greedy and non-overlapping, or with <paramref name="overlapping"/> every
        /// place the pattern matches (a backward search wants those). <paramref name="replacement"/> fills in a regular
        /// expression's group references.</summary>
        public List<Hit> Matches(Paragraph para, bool overlapping, string? replacement = null)
        {
            var hits = new List<Hit>();
            if (!Valid || TimedOut) return hits;
            if (_o.PropertiesOnly) { PropertyStretches(para, hits); return hits; }
            if (_o.Pattern.Length == 0) return hits;
            var text = para.Text;
            if (text.Length == 0) return hits;
            var map = NfdMap.Build(text);
            bool[]? boundaries = null;
            string? stripped = null;
            int[]? backToText = null;
            bool Accept(int ns, int ne, string? expansion)
            {
                // Matching is over decomposed text, so a match must not split a Hangul syllable (a lead jamo alone, or a lead and vowel
                // without the final consonant that follows): it would select the whole syllable when stored composed and only part of it
                // when stored decomposed. Refused in both.
                if ((ns > 0 && IsJamoContinuation(map.Nfd[ns])) || (ne < map.Nfd.Length && IsJamoContinuation(map.Nfd[ne]))) return false;
                int os = map.Start[ns];
                if (os < 0) return false;
                int oe = map.End[ne];
                if (oe <= os) return false;
                if (!_o.UseRegularExpression && _o.WholeWord)
                {
                    // An embedded object is ignorable here too: word boundaries are judged on the text without them.
                    if (boundaries == null)
                    {
                        stripped = text.IndexOf(Run.ObjectReplacement) < 0 ? null : text.Replace(Run.ObjectReplacement.ToString(), string.Empty);
                        var wsOf = WordBreaks.WsOf(para);
                        if (stripped != null)
                        {
                            // The writing system of a character is looked up where it is in the paragraph's own text, not in the text without its objects.
                            backToText = new int[stripped.Length];
                            for (int i = 0, j = 0; i < text.Length; i++) if (text[i] != Run.ObjectReplacement) backToText[j++] = i;
                        }
                        var back = backToText;
                        boundaries = WordBreaks.Boundaries(stripped ?? text, _rules, back == null ? wsOf : j => wsOf(back[j]));
                    }
                    if (!boundaries[Unobjected(text, os, stripped != null)] || !boundaries[Unobjected(text, oe, stripped != null)]) return false;
                }
                if (!PropertiesMatch(para, os, oe)) return false;
                hits.Add(new Hit(os, oe, expansion));
                return true;
            }

            if (_regex != null)
            {
                // The expression sees the text without embedded objects, as the plain search does, and a match that would cut a letter from
                // its marks (it starts or ends beside one) is refused, so the answer is the same whichever form the document stores.
                var seen = new StringBuilder(map.Nfd.Length);
                var back = new List<int>(map.Nfd.Length);
                for (int i = 0; i < map.Nfd.Length; i++)
                    if (map.Nfd[i] != Run.ObjectReplacement) { seen.Append(map.Nfd[i]); back.Add(i); }
                try
                {
                    foreach (Match m in _regex.Matches(seen.ToString()))
                    {
                        if (m.Length == 0) continue;
                        int ns = back[m.Index], ne = back[m.Index + m.Length - 1] + 1;
                        if ((ns > 0 && IsMark(map.Nfd, ns)) || (ne < map.Nfd.Length && IsMark(map.Nfd, ne))) continue;
                        Accept(ns, ne, replacement == null ? null : m.Result(replacement));
                    }
                }
                catch (RegexMatchTimeoutException)
                {
                    TimedOut = true;
                    hits.Clear();
                }
                return hits;
            }

            var key = new List<char>(map.Nfd.Length);
            var index = new List<int>(map.Nfd.Length);
            Fold(map.Nfd, key, index);
            int len = _key.Length;
            if (len == 0) return hits;
            for (int k = 0; k + len <= key.Count; k++)
            {
                int j = 0;
                while (j < len && key[k + j] == _key[j]) j++;
                if (j < len) continue;
                int ns = index[k], ne = index[k + len - 1] + 1;
                if (_diacriticsMatter)
                {
                    // A match that a combining mark follows is part of a longer letter, not a match.
                    if (ne < map.Nfd.Length && IsMark(map.Nfd, ne)) continue;
                }
                else
                {
                    while (ne < map.Nfd.Length && IsMark(map.Nfd, ne)) ne += CodePointLength(map.Nfd, ne);
                }
                if (Accept(ns, ne, null) && !overlapping) k += len - 1;
            }
            return hits;
        }

        /// <summary>An offset in the text, as an offset in the text with its embedded objects removed.</summary>
        static int Unobjected(string text, int offset, bool has)
        {
            if (!has) return offset;
            int n = 0;
            for (int i = 0; i < offset; i++) if (text[i] == Run.ObjectReplacement) n++;
            return offset - n;
        }

        /// <summary>True when every run the range touches has the writing system and character style asked for. Embedded objects are
        /// ignorable and do not count.</summary>
        bool PropertiesMatch(Paragraph para, int start, int end)
        {
            if (_o.WritingSystem == null && _o.CharacterStyle == null) return true;
            int pos = 0;
            foreach (var r in para.Runs)
            {
                int rs = pos, re = pos + r.Text.Length;
                pos = re;
                if (re <= start || rs >= end || r.IsObject) continue;
                if (!RunHasProperties(r)) return false;
            }
            return true;
        }

        bool RunHasProperties(Run r)
        {
            if (_o.WritingSystem != null && r.Ws != _o.WritingSystem) return false;
            if (_o.CharacterStyle != null)
            {
                r.Props.StrProps.TryGetValue(TextPropertyNumbers.NamedStyle, out var style);
                if ((style ?? string.Empty) != _o.CharacterStyle) return false;
            }
            return true;
        }

        /// <summary>An empty pattern with a writing system or style: each stretch of consecutive runs that has them.</summary>
        void PropertyStretches(Paragraph para, List<Hit> hits)
        {
            int pos = 0, from = -1;
            foreach (var r in para.Runs)
            {
                int rs = pos, re = pos + r.Text.Length;
                pos = re;
                if (re == rs) continue;
                if (!r.IsObject && RunHasProperties(r)) { if (from < 0) from = rs; continue; }
                if (from >= 0) hits.Add(new Hit(from, rs, null));
                from = -1;
            }
            if (from >= 0) hits.Add(new Hit(from, pos, null));
        }
    }
}
