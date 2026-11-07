using System.Collections.Generic;
using System.Linq;

namespace SIL.TextEditing
{
    // Single-paragraph mode (ADR 0012): the field never makes a second paragraph.
    public sealed partial class DocumentEditor
    {
        /// <summary>When true the editor never splits a paragraph: <see cref="Enter"/> does nothing (the field hands the key to
        /// the host, as FieldWorks does when a view does not accept Return), a line break in typed text becomes a space, and
        /// pasted paragraphs are joined into one with a space between them. A document that already holds several
        /// paragraphs keeps them; only new splits are refused.</summary>
        public bool SingleParagraph { get; set; }

        /// <summary>Single-line mode (ADR 0030), for a box whose text is a notation: <see cref="SingleParagraph"/> as well, and no hard line break
        /// either. <see cref="InsertLineBreak"/> (Shift+Enter) does nothing, and a line separator (U+2028) in typed or pasted text becomes a space, as a
        /// line end does. Setting it sets <see cref="SingleParagraph"/>; clearing it leaves that on.</summary>
        public bool SingleLine
        {
            get => _singleLine;
            set
            {
                _singleLine = value;
                if (value) SingleParagraph = true;
            }
        }

        bool _singleLine;

        /// <summary>The line separators of a single-line text turned into spaces.</summary>
        static void LineBreaksToSpaces(IEnumerable<Paragraph> paragraphs)
        {
            foreach (var p in paragraphs)
            {
                bool any = false;
                foreach (var r in p.Runs)
                    if (!r.IsObject && r.Text.IndexOf(LineSeparator) >= 0) { r.Text = r.Text.Replace(LineSeparator, ' '); any = true; }
                if (any) p.Normalize();
            }
        }

        /// <summary>True for the characters that separate paragraphs or lines of a paragraph list: CR, LF, NEL (U+0085) and the
        /// paragraph separator (U+2029). The line separator (U+2028) is not one: it is FieldWorks' hard line break inside a
        /// paragraph, so a single paragraph keeps it.</summary>
        internal static bool IsParagraphSeparator(char c) => c == (char)13 || c == (char)10 || c == (char)0x85 || c == (char)0x2029;

        /// <summary>Text for a single paragraph: tabs removed, each separator (CR LF counts as one) turned into one space, and a
        /// run of separators that ends the text dropped. Nothing else is collapsed, so "a", two newlines, "b" gives "a" and two spaces
        /// and "b", a leading newline gives a space even at the start of the text, and a no-break space is kept.</summary>
        internal static string OneLine(string text)
        {
            var sb = new System.Text.StringBuilder(text.Length);
            int end = text.Length;   // separators (and tabs) after the last other character end the text and are dropped
            while (end > 0 && (text[end - 1] == '\t' || IsParagraphSeparator(text[end - 1]))) end--;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\t') continue;
                if (!IsParagraphSeparator(c)) { sb.Append(c); continue; }
                int j = i + 1;
                if (c == (char)13 && j < text.Length && text[j] == (char)10) j++;
                if (i < end) sb.Append(' ');
                i = j - 1;
            }
            return sb.ToString();
        }

        /// <summary>The fragment as one paragraph: the first paragraph's style and properties and the runs of every paragraph in
        /// order, a separator between neighbours that <see cref="SeparatorsToSpaces"/> then turns into one space (in the writing
        /// system of the text before it). Empty paragraphs are separators too, so each is a space, except at the very end.</summary>
        static List<Paragraph> JoinIntoOne(List<Paragraph> fragment)
        {
            var joined = fragment[0].Clone(false);
            joined.Runs = new RunList();
            for (int i = 0; i < fragment.Count; i++)
            {
                if (i > 0) joined.Runs.Add(new Run { Text = "\n", Ws = joined.Runs.Count > 0 ? joined.Runs[joined.Runs.Count - 1].Ws : fragment[i].DefaultWs });
                joined.Runs.AddRange(fragment[i].Runs.Select(r => r.Clone()));
            }
            return new List<Paragraph> { joined };
        }

        /// <summary>Removes the tab characters from the paragraph's text runs (the multi-paragraph paste rule; a single paragraph
        /// does it in <see cref="SeparatorsToSpaces"/>, which must see them to pair CR LF as FieldWorks does).</summary>
        static void RemoveTabs(Paragraph paragraph)
        {
            bool any = false;
            foreach (var r in paragraph.Runs)
                if (!r.IsObject && r.Text.IndexOf('\t') >= 0) { r.Text = r.Text.Replace("\t", ""); any = true; }
            if (any) paragraph.Normalize();
        }

        /// <summary>Removes tabs and turns every separator (CR LF counts as one), across run boundaries, into one space, and drops
        /// every separator that ends the paragraph. Nothing else is collapsed, and a separator at the start leaves a space. The hard
        /// line break and object runs count as content. The space takes the writing system of the text before it, else of the
        /// separator's own run. Output runs are built with a string builder so a long run stays linear.</summary>
        static void SeparatorsToSpaces(Paragraph paragraph)
        {
            if (!paragraph.Runs.Any(r => !r.IsObject && r.Text.Any(c => c == '\t' || IsParagraphSeparator(c)))) return;
            var chars = new List<char>();
            var owners = new List<Run>();
            var afterTab = new List<bool>();   // a tab stood just before this character, so a CR before it is not paired with it
            bool tab = false;
            foreach (var r in paragraph.Runs)
                foreach (var c in r.Text)
                {
                    if (c == '\t') { tab = true; continue; }
                    chars.Add(c); owners.Add(r); afterTab.Add(tab); tab = false;
                }
            var runs = new List<Run>();
            var sb = new System.Text.StringBuilder();
            Run? owner = null;        // the source run the pending text copies, null for a synthesised space
            string spaceWs = "";
            void Flush()
            {
                if (sb.Length == 0) return;
                runs.Add(owner != null ? owner.CloneWithText(sb.ToString()) : new Run { Text = sb.ToString(), Ws = spaceWs });
                sb.Clear();
            }
            void Emit(char c, Run source, bool plain)
            {
                bool same = sb.Length > 0 && (plain ? owner == null && spaceWs == source.Ws : ReferenceEquals(owner, source));
                if (!same) { Flush(); owner = plain ? null : source; spaceWs = source.Ws; }
                sb.Append(c);
            }
            int i = 0, n = chars.Count;
            int end = n;              // separators after the last other character end the text and are dropped
            while (end > 0 && IsParagraphSeparator(chars[end - 1])) end--;
            while (i < n)
            {
                if (!IsParagraphSeparator(chars[i])) { Emit(chars[i], owners[i], false); i++; continue; }
                int j = i + 1;
                if (chars[i] == (char)13 && j < n && chars[j] == (char)10 && !afterTab[j]) j++;
                if (i < end) Emit(' ', i > 0 ? owners[i - 1] : owners[i], true);
                i = j;
            }
            Flush();
            paragraph.Runs = new RunList(runs);
            paragraph.Normalize();
        }
    }
}
