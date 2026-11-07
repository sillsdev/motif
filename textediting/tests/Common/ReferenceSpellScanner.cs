using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SIL.TextEditing.Tests
{
    /// <summary>The spell scanner as it was before issue 91 (one pass over the whole paragraph), kept as the oracle the windowed scanner
    /// is compared with.</summary>
    static class ReferenceSpellScanner
    {
        static UnicodeCategory CategoryOf(int codePoint)
        {
            if (codePoint < 0 || codePoint > 0x10FFFF) return UnicodeCategory.OtherNotAssigned;
            if (codePoint < 0x10000) return CharUnicodeInfo.GetUnicodeCategory((char)codePoint);
            return CharUnicodeInfo.GetUnicodeCategory(char.ConvertFromUtf32(codePoint), 0);
        }

        static bool IsNumber(int cp)
        {
            var c = CategoryOf(cp);
            return c == UnicodeCategory.DecimalDigitNumber || c == UnicodeCategory.LetterNumber || c == UnicodeCategory.OtherNumber;
        }

        public static List<SpellingMark> Check(Paragraph paragraph, ISpellCheckHost host, IStyleResolver? styles = null, bool readOnly = false, int limit = int.MaxValue)
        {
            var marks = new List<SpellingMark>();
            ScanWords(paragraph, host, styles, readOnly, limit, -1, (a, b, scan, problem, word, checker) => { marks.Add(new SpellingMark(a, b - a, problem)); return false; });
            return marks;
        }

        public static (int Start, int End, SpellingProblem Problem, string Word)? At(Paragraph paragraph, ISpellCheckHost host, int offset, IStyleResolver? styles = null, bool readOnly = false, int limit = int.MaxValue)
        {
            (int, int, SpellingProblem, string)? found = null;
            ScanWords(paragraph, host, styles, readOnly, limit, offset, (a, b, scan, problem, word, checker) =>
            {
                if (offset < a || offset > b) return false;
                found = (a, b, problem, word);
                return true;
            });
            return found;
        }

        delegate bool Report(int start, int end, Scan scan, SpellingProblem problem, string word, ISpellChecker? checker);

        static void ScanWords(Paragraph paragraph, ISpellCheckHost host, IStyleResolver? styles, bool readOnly, int limit, int focus, Report report)
        {
            var scan = new Scan { Text = paragraph.Text, Spans = paragraph.Spans() };
            int n = scan.Text.Length;
            if (n == 0) return;
            scan.RunOf = new int[n];
            scan.Checked = new bool[n];
            for (int i = 0; i < scan.Spans.Count; i++)
            {
                var span = scan.Spans[i];
                var mode = SpellScanner.ModeOf(paragraph, span.Run, styles);
                bool isChecked = mode == SpellCheckMode.ForceCheck || (mode == SpellCheckMode.Normal && !readOnly && span.Run.Editable != RunEditable.NotEditable);
                for (int k = span.Start; k < span.Start + span.Length; k++)
                {
                    scan.RunOf[k] = i;
                    scan.Checked[k] = isChecked;
                }
            }

            string WsAt(int index) => scan.Spans[scan.RunOf[index]].Ws;

            bool WordForming(int index, int cp)
            {
                if (cp == Run.ObjectReplacement) return true;
                return IsNumber(cp) || host.IsWordForming(WsAt(index), cp);
            }

            int pos = 0;
            while (pos < n && pos < limit)
            {
                int cp = CodePointAt(scan.Text, pos, out int width);
                if (!WordForming(pos, cp)) { pos += width; continue; }
                int start = pos;
                int end = pos + width;
                while (end < n)
                {
                    int c2 = CodePointAt(scan.Text, end, out int w2);
                    if (WordForming(end, c2)) { end += w2; continue; }
                    if (end + w2 < n && host.JoinsWords(WsAt(end), c2))
                    {
                        int c3 = CodePointAt(scan.Text, end + w2, out _);
                        if (WordForming(end + w2, c3)) { end += w2; continue; }
                    }
                    break;
                }
                if ((focus < 0 || (focus >= start && focus <= end)) && CheckWord(scan, host, start, end, report)) return;
                if (focus >= 0 && end >= focus) return;
                pos = end;
            }
        }

        sealed class Scan
        {
            public string Text = string.Empty;
            public int[] RunOf = new int[0];
            public List<RunSpan> Spans = new List<RunSpan>();
            public bool[] Checked = new bool[0];
        }

        static int CodePointAt(string text, int index, out int width)
        {
            char c = text[index];
            if (char.IsHighSurrogate(c) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
            {
                width = 2;
                return char.ConvertToUtf32(c, text[index + 1]);
            }
            width = 1;
            return char.IsSurrogate(c) ? 0xFFFD : c;
        }

        // True when the report asked to stop.
        static bool CheckWord(Scan scan, ISpellCheckHost host, int a, int b, Report report)
        {
            // Cut the ends back to text that is checked.
            while (b > a && !scan.Checked[a]) a = RunEnd(scan, a);
            while (b > a && !scan.Checked[b - 1]) b = scan.Spans[scan.RunOf[b - 1]].Start;
            if (b <= a) return false;
            // Objects and the no-break space that precedes a footnote marker are not words: nothing to mark when nothing else is there.
            bool onlyObjects = true;
            for (int i = a; i < b && onlyObjects; i++)
                if (scan.Text[i] != Run.ObjectReplacement && scan.Text[i] != '﻿') onlyObjects = false;
            if (onlyObjects) return false;
            string lastWs = scan.Spans[scan.RunOf[b - 1]].Ws;
            for (int i = a; i < b; i++)
            {
                if (scan.Text[i] == Run.ObjectReplacement) continue;
                if (!scan.Checked[i] || scan.Spans[scan.RunOf[i]].Ws != lastWs)
                    return report(a, b, scan, SpellingProblem.Mixed, Nfc(scan.Text.Substring(a, b - a)) ?? scan.Text.Substring(a, b - a), null);
            }
            var checker = host.GetChecker(lastWs);
            if (checker == null) return false;
            string word = scan.Text.Substring(a, b - a);
            if (word.IndexOf(Run.ObjectReplacement) >= 0)
                return report(a, b, scan, SpellingProblem.Embedded, word, checker);
            var normal = Nfc(word);
            if (normal == null) return false;
            // Numbers alone are not words; no dictionary has them, and FieldWorks' dictionaries accept them.
            if (IsAllNumbers(normal) || checker.Check(normal)) return false;
            return report(a, b, scan, SpellingProblem.Misspelled, normal, checker);
        }

        static string? Nfc(string text)
        {
            try { return text.Normalize(NormalizationForm.FormC); }
            catch (ArgumentException) { return null; }
        }

        static int RunEnd(Scan scan, int index)
        {
            var span = scan.Spans[scan.RunOf[index]];
            return span.Start + span.Length;
        }

        static bool IsAllNumbers(string word)
        {
            for (int i = 0; i < word.Length; )
            {
                int cp = CodePointAt(word, i, out int w);
                if (!IsNumber(cp)) return false;
                i += w;
            }
            return true;
        }
    }
}
