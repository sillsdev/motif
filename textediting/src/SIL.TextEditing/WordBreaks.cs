using System;
using System.Collections.Generic;

namespace SIL.TextEditing
{
    /// <summary>
    /// The word rule of the double-click, Ctrl+Left and Right and the whole-word find: the spell checker's rule (<see cref="SpellScanner"/>) over grapheme
    /// clusters, as FieldWorks' views use one rule, the writing system's, everywhere (ADR 0026, issue 102). Every code point has a class: Word (a number,
    /// or what the host's <see cref="IWordRules.IsWordForming"/> says forms words in the writing system of that character, by default letters, combining
    /// marks and modifier symbols, or a character the host's <see cref="IWordRules.JoinsWords"/> says stays in a word, an apostrophe or a zero-width joiner,
    /// when a Word code point stands on each side of it), Space (white space), Object (U+FFFC, an inline object) or Other (any other punctuation or
    /// symbol, an underscore and a format character included). A word is a maximal run of Word clusters; a cluster, the caret's unit, takes the class of
    /// its first code point, so a combining mark after a space or a hyphen stays with it and a trailing zero-width joiner or non-joiner stays with the
    /// cluster before it. A run of Space or of Other clusters is its own "word" for double-click, and an Object is always alone. The overloads that take
    /// a <see cref="Paragraph"/> ask the host's rule for the writing system of each character; the ones that take only a string use
    /// <see cref="DefaultWordRules"/> and one writing system. There is no dictionary, so Thai, Khmer and Chinese text without spaces is one word per run
    /// of letters; a host that needs better can replace the rule. This is not UAX #29 word breaking.
    /// </summary>
    public static class WordBreaks
    {
        enum Kind : sbyte { Word, Space, Other, Object }

        /// <summary>The clusters of a text and their classes, each class worked out when first asked for (a Ctrl+arrow looks at a few clusters, not at
        /// every one of a long paragraph, and asks the host about those only).</summary>
        sealed class Clusters
        {
            readonly string _text;
            readonly IReadOnlyList<int> _starts;
            readonly IWordRules _rules;
            readonly Func<int, string>? _wsAt;
            readonly sbyte[] _kinds;

            public Clusters(string text, IWordRules? rules, Func<int, string>? wsAt)
            {
                _text = text;
                _starts = GraphemeClusters.Starts(text);
                _rules = rules ?? DefaultWordRules.Instance;
                _wsAt = wsAt;
                _kinds = new sbyte[_starts.Count];
                for (int i = 0; i < _kinds.Length; i++) _kinds[i] = -1;
            }

            public int Count => _starts.Count;
            public int Start(int cluster) => _starts[cluster];
            public int End(int cluster) => cluster + 1 < _starts.Count ? _starts[cluster + 1] : _text.Length;

            /// <summary>The cluster that holds the offset (the last that starts at or before it).</summary>
            public int At(int offset)
            {
                int lo = 0, hi = _starts.Count - 1;
                while (lo < hi)
                {
                    int mid = lo + (hi - lo + 1) / 2;
                    if (_starts[mid] <= offset) lo = mid; else hi = mid - 1;
                }
                return lo;
            }

            public Kind this[int cluster]
            {
                get
                {
                    var kind = _kinds[cluster];
                    if (kind < 0) _kinds[cluster] = kind = (sbyte)Classify(_starts[cluster]);
                    return (Kind)kind;
                }
            }

            /// <summary>True when cluster <paramref name="left"/> and the one after it are one unit: of one class, and, for words, not parted by a tail of the first that
            /// is not itself word (a zero-width non-joiner with no letter after it, which the spell checker's word stops before).</summary>
            public bool Joined(int left)
            {
                var kind = this[left];
                if (kind != this[left + 1]) return false;
                if (kind != Kind.Word) return true;
                int last = SpellScanner.CodePointBefore(_text, End(left), out int width);
                return End(left) - width == _starts[left] || Classify(End(left) - width) == Kind.Word;
            }

            string WsAt(int index) => _wsAt?.Invoke(index) ?? string.Empty;

            bool Forms(int index, int cp) => SpellScanner.IsWordFormingIn(_rules, WsAt(index), cp, false);

            Kind Classify(int index)
            {
                int cp = SpellScanner.CodePointAt(_text, index, out int width);
                if (cp == Run.ObjectReplacement) return Kind.Object;
                string ws = WsAt(index);
                if (SpellScanner.IsWordFormingIn(_rules, ws, cp, false)) return Kind.Word;
                // A joiner (an apostrophe, a zero-width joiner) with a word-forming character right on each side stays in the word, as the spell checker's rule has it.
                if (index > 0 && index + width < _text.Length && _rules.JoinsWords(ws, cp))
                {
                    int before = SpellScanner.CodePointBefore(_text, index, out int beforeWidth);
                    int after = SpellScanner.CodePointAt(_text, index + width, out _);
                    if (Forms(index - beforeWidth, before) && Forms(index + width, after)) return Kind.Word;
                }
                return char.IsWhiteSpace(_text, index) ? Kind.Space : Kind.Other;
            }
        }

        /// <summary>The writing system of the run that holds each offset of the paragraph, for the host's rule.</summary>
        internal static Func<int, string> WsOf(Paragraph paragraph) => offset =>
        {
            int i = paragraph.RunIndexAt(offset, out _);
            return i < paragraph.Runs.Count ? paragraph.Runs[i].Ws : paragraph.DefaultWs;
        };

        static void WordAt(string text, int charIndex, IWordRules? rules, Func<int, string>? wsAt, out int start, out int end)
        {
            start = end = 0;
            if (string.IsNullOrEmpty(text)) return;
            var clusters = new Clusters(text, rules, wsAt);
            int k = clusters.At(Math.Max(0, Math.Min(text.Length - 1, charIndex)));
            var kind = clusters[k];
            int a = k, b = k;
            if (kind != Kind.Object)
            {
                while (a > 0 && clusters.Joined(a - 1)) a--;
                while (b + 1 < clusters.Count && clusters.Joined(b)) b++;
            }
            start = clusters.Start(a);
            end = clusters.End(b);
        }

        /// <summary>The run containing the character at <paramref name="charIndex"/> (clamped into the text): the word,
        /// white-space run or punctuation run a double-click selects. Empty text gives (0, 0). The default word rule.</summary>
        public static void WordAt(string text, int charIndex, out int start, out int end) => WordAt(text, charIndex, null, null, out start, out end);

        /// <summary>As <see cref="WordAt(string, int, out int, out int)"/>, by the host's word rule (<paramref name="rules"/>, null for the default) for the
        /// writing system of each character of the paragraph.</summary>
        public static void WordAt(Paragraph paragraph, int charIndex, IWordRules? rules, out int start, out int end)
            => WordAt(paragraph, charIndex, rules, null, out start, out end);

        /// <summary>As <see cref="WordAt(Paragraph, int, IWordRules, out int, out int)"/>, with the host's styles, so that a change of character style stops the
        /// word when the two resolved styles differ in writing system or spell-check property (without a resolver a style change never does).</summary>
        public static void WordAt(Paragraph paragraph, int charIndex, IWordRules? rules, IStyleResolver? styles, out int start, out int end)
        {
            WordAt(paragraph.Text, charIndex, rules, WsOf(paragraph), out start, out end);
            ClipToFormat(paragraph, charIndex, styles, ref start, ref end);
        }

        /// <summary>FieldWorks' double-click stops at a change of writing system or spell-check property (or a character style that differs in those), so a word that runs on across
        /// such a change is selected a piece at a time (issue 116). Only the double-click does it: Ctrl+arrows, the whole-word find, the word of a context
        /// request and the spell checker, which has its own answer for a word of several writing systems, take the whole word. Other properties (bold, size,
        /// colour) do not stop it. The run's own properties are compared, and two different style names through the host's style resolver.</summary>
        static void ClipToFormat(Paragraph paragraph, int charIndex, IStyleResolver? styles, ref int start, ref int end)
        {
            var runs = paragraph.Runs;
            if (runs.Count < 2 || start >= end) return;
            charIndex = Math.Max(0, Math.Min(paragraph.Length - 1, charIndex));
            int pos = 0, at = -1, atStart = 0;
            for (int i = 0; i < runs.Count; i++)
            {
                int next = pos + runs[i].Text.Length;
                if (charIndex < next) { at = i; atStart = pos; break; }
                pos = next;
            }
            if (at < 0) return;
            int from = atStart, to = atStart + runs[at].Text.Length;
            for (int i = at - 1, p = atStart; i >= 0 && SameWordFormat(runs[i], runs[at], styles); i--) { p -= runs[i].Text.Length; from = p; }
            for (int i = at + 1; i < runs.Count && SameWordFormat(runs[i], runs[at], styles); i++) to += runs[i].Text.Length;
            start = Math.Max(start, from);
            end = Math.Min(end, to);
        }

        static bool SameWordFormat(Run a, Run b, IStyleResolver? styles)
        {
            if (a.Ws != b.Ws || SpellCheckOf(a) != SpellCheckOf(b)) return false;
            string sa = StyleOf(a), sb = StyleOf(b);
            if (sa == sb || styles == null) return true;
            // Two different styles stop the word only where they differ in what the double-click cares about: the writing system and the spell-check property.
            var ba = sa.Length == 0 ? null : styles.Resolve(sa, a.Ws);
            var bb = sb.Length == 0 ? null : styles.Resolve(sb, b.Ws);
            // The writing system is compared only when both runs have a style, as FieldWorks does; an undefined spell-check is the normal one.
            if (ba != null && bb != null && StyleInt(ba, TextPropertyNumbers.WritingSystem) != StyleInt(bb, TextPropertyNumbers.WritingSystem)) return false;
            return StyleSpell(ba) == StyleSpell(bb);
        }

        static int StyleSpell(PropertyBag? bag) => bag != null && bag.TryGetInt(TextPropertyNumbers.SpellCheck, out var v) ? v : TextPropertyNumbers.SpellCheckNormal;

        static int StyleInt(PropertyBag? bag, int number) => bag != null && bag.TryGetInt(number, out var v) ? v : -1;

        // An absent property is the normal spell checking and no character style.
        static int SpellCheckOf(Run r) => r.Props.TryGetInt(TextPropertyNumbers.SpellCheck, out var v) ? v : TextPropertyNumbers.SpellCheckNormal;

        static string StyleOf(Run r) => r.Props.TryGetStr(TextPropertyNumbers.NamedStyle, out var s) ? s : string.Empty;

        /// <summary>For each offset 0 to the text length, whether a word starts or ends there: the start and end of the text, and
        /// every cluster boundary where the class changes (word, white space, other) or an object sits. Offsets inside a cluster
        /// are not boundaries. A whole-word find uses it. The default word rule.</summary>
        public static bool[] Boundaries(string text) => Boundaries(text, null, null);

        /// <summary>As <see cref="Boundaries(string)"/>, by the host's word rule for the writing system of each character of the paragraph.</summary>
        public static bool[] Boundaries(Paragraph paragraph, IWordRules? rules) => Boundaries(paragraph.Text, rules, WsOf(paragraph));

        /// <summary>As <see cref="Boundaries(string)"/>; <paramref name="wsAt"/> says the writing system at an offset of <paramref name="text"/> (null for one
        /// writing system).</summary>
        internal static bool[] Boundaries(string text, IWordRules? rules, Func<int, string>? wsAt)
        {
            var result = new bool[text.Length + 1];
            result[0] = result[text.Length] = true;
            if (text.Length == 0) return result;
            var clusters = new Clusters(text, rules, wsAt);
            for (int k = 1; k < clusters.Count; k++)
                result[clusters.Start(k)] = !clusters.Joined(k - 1) || clusters[k] == Kind.Object || clusters[k - 1] == Kind.Object;
            return result;
        }

        static bool TryWordAt(string text, int charIndex, IWordRules? rules, Func<int, string>? wsAt, out int start, out int end)
        {
            start = end = Math.Max(0, Math.Min(text.Length, charIndex));
            if (text.Length == 0 || charIndex < 0 || charIndex >= text.Length) return false;
            var clusters = new Clusters(text, rules, wsAt);
            int k = clusters.At(charIndex);
            if (clusters[k] != Kind.Word) return false;
            int a = k, b = k;
            while (a > 0 && clusters.Joined(a - 1)) a--;
            while (b + 1 < clusters.Count && clusters.Joined(b)) b++;
            start = clusters.Start(a);
            end = clusters.End(b);
            return true;
        }

        /// <summary>The word under <paramref name="charIndex"/>: true with its range when the character there is part of a word
        /// (not white space, punctuation or an object); false with an empty range at the index otherwise. The default word rule.</summary>
        public static bool TryWordAt(string text, int charIndex, out int start, out int end) => TryWordAt(text, charIndex, null, null, out start, out end);

        /// <summary>As <see cref="TryWordAt(string, int, out int, out int)"/>, by the host's word rule for the writing system of each character of the
        /// paragraph. A context request asks for the word this way, so it is the word the spell checker marks.</summary>
        public static bool TryWordAt(Paragraph paragraph, int charIndex, IWordRules? rules, out int start, out int end)
            => TryWordAt(paragraph.Text, charIndex, rules, WsOf(paragraph), out start, out end);

        static int NextWordStart(string text, int index, IWordRules? rules, Func<int, string>? wsAt)
        {
            if (string.IsNullOrEmpty(text) || index >= text.Length) return text?.Length ?? 0;
            var clusters = new Clusters(text, rules, wsAt);
            int k = clusters.At(Math.Max(0, index));
            var kind = clusters[k];
            if (kind != Kind.Space)
            {
                k++;
                if (kind != Kind.Object) while (k < clusters.Count && clusters.Joined(k - 1)) k++;
            }
            while (k < clusters.Count && clusters[k] == Kind.Space) k++;
            return k < clusters.Count ? clusters.Start(k) : text.Length;
        }

        /// <summary>Ctrl+Right: the start of the next word after <paramref name="index"/> (skipping the rest of the current
        /// word or punctuation run and then white space), or the text length. The default word rule.</summary>
        public static int NextWordStart(string text, int index) => NextWordStart(text, index, null, null);

        /// <summary>As <see cref="NextWordStart(string, int)"/>, by the host's word rule for the writing system of each character of the paragraph.</summary>
        public static int NextWordStart(Paragraph paragraph, int index, IWordRules? rules) => NextWordStart(paragraph.Text, index, rules, WsOf(paragraph));

        static int PreviousWordStart(string text, int index, IWordRules? rules, Func<int, string>? wsAt)
        {
            if (string.IsNullOrEmpty(text) || index <= 0) return 0;
            var clusters = new Clusters(text, rules, wsAt);
            int k = clusters.At(Math.Min(index, text.Length) - 1);
            while (k >= 0 && clusters[k] == Kind.Space) k--;
            if (k < 0) return 0;
            var kind = clusters[k];
            if (kind != Kind.Object) while (k > 0 && clusters.Joined(k - 1)) k--;
            return clusters.Start(k);
        }

        /// <summary>Ctrl+Left: the start of the word before <paramref name="index"/> (skipping white space first), or 0. The default word rule.</summary>
        public static int PreviousWordStart(string text, int index) => PreviousWordStart(text, index, null, null);

        /// <summary>As <see cref="PreviousWordStart(string, int)"/>, by the host's word rule for the writing system of each character of the paragraph.</summary>
        public static int PreviousWordStart(Paragraph paragraph, int index, IWordRules? rules) => PreviousWordStart(paragraph.Text, index, rules, WsOf(paragraph));
    }
}
