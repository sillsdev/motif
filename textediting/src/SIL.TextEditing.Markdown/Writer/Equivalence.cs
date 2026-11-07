using System;
using System.Collections.Generic;
using System.Text;

namespace SIL.TextEditing.Markdown.Writer
{
    /// <summary>What a round trip through Markdown preserves, and so what the writer checks and the tests compare.</summary>
    internal static class Equivalence
    {
        /// <summary>The paragraph properties the notation spells: <see cref="Paragraph.Props"/> without the style name (which
        /// <see cref="Paragraph.Style"/> carries) and with <see cref="Paragraph.Rtl"/> written over the direction, as the LCModel adapter
        /// does when it builds the StyleRules.</summary>
        public static PropertyBag EffectiveProps(Paragraph paragraph)
        {
            var bag = paragraph.Props.Clone();
            bag.StrProps.Remove(TextPropertyNumbers.NamedStyle);
            bool hadRtl = bag.TryGetInt(128, out int rtl);
            if (paragraph.Rtl != (hadRtl && rtl != 0)) bag.IntProps[128] = new IntPropValue(0, paragraph.Rtl ? 1 : 0);
            return bag;
        }

        public static Paragraph Normalized(Paragraph paragraph)
        {
            var copy = paragraph.Clone(false);
            copy.Normalize();
            return copy;
        }

        /// <summary>True when both paragraphs say the same after empty runs are dropped and neighbours of one format are merged:
        /// the same style, the same other paragraph properties (direction included), the same runs (text, writing system, properties,
        /// object data) and, for an empty paragraph, the same typing writing system. Keys are not compared, and a non-empty paragraph's
        /// <see cref="Paragraph.DefaultWs"/> is its first run's, as the adapter sets it.</summary>
        public static bool Equal(Paragraph a, Paragraph b)
        {
            if (!string.Equals(a.Style, b.Style, StringComparison.Ordinal)) return false;
            if (!EffectiveProps(a).ContentEquals(EffectiveProps(b))) return false;
            var x = Normalized(a);
            var y = Normalized(b);
            if (x.Runs.Count != y.Runs.Count) return false;
            if (x.Runs.Count == 0) return string.Equals(a.DefaultWs, b.DefaultWs, StringComparison.Ordinal);
            for (int i = 0; i < x.Runs.Count; i++)
            {
                var r = x.Runs[i];
                var s = y.Runs[i];
                if (r.Ws != s.Ws || !SameText(r.Text, s.Text) || r.ObjData != s.ObjData || !r.Props.ContentEquals(s.Props)) return false;
            }
            return true;
        }

        /// <summary>The same text, or the same once both are NFD: the reader gives NFD whatever form the text was written in.</summary>
        static bool SameText(string a, string b)
            => string.Equals(a, b, StringComparison.Ordinal)
               || string.Equals(TextNormalizer.Normalize(a, NormalizationForm.FormD), TextNormalizer.Normalize(b, NormalizationForm.FormD), StringComparison.Ordinal);

        public static bool Equal(IReadOnlyList<Run> a, IReadOnlyList<Run> b)
        {
            var p = new Paragraph { Runs = new List<Run>(a) };
            var q = new Paragraph { Runs = new List<Run>(b) };
            p.DefaultWs = q.DefaultWs = "x";
            return Equal(p, q);
        }
    }
}
