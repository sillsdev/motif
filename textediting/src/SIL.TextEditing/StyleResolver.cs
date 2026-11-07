using System;
using System.Collections.Generic;

namespace SIL.TextEditing
{
    /// <summary>
    /// The host's styles (ADR 0004). The field asks for the effective formatting of a named style in a writing system and
    /// draws what comes back; it never reads a stylesheet or gives a style name any meaning. The host answers with the
    /// style's own text properties, keyed by FieldWorks property numbers like a run's bag, with inheritance (based-on) and
    /// the writing system's overrides already applied. A null answer means "no such style".
    /// </summary>
    public interface IStyleResolver
    {
        /// <summary>The properties of the named paragraph or character style for the writing system, or null.</summary>
        PropertyBag? Resolve(string styleName, string ws);
    }

    /// <summary>The character formatting of one run after styles and the run's own properties are applied.</summary>
    public sealed class CharFormat
    {
        /// <summary>The size of superscript and subscript text against the text beside it where the font gives none: two thirds. Provenance: this is the value
        /// FieldWorks falls back to when a font gives no superscript or subscript size in its OS/2 table; FieldWorks reads the size, and the raise or drop,
        /// from that table first, and so does the field (<c>ScriptPlacement</c>). Observed behaviour; the code is ours.</summary>
        public const double DefaultSuperscriptScale = 2.0 / 3.0;

        /// <summary>CSS-style weight 100 to 900; 400 is normal and 700 bold.</summary>
        public int Weight { get; set; } = 400;
        public bool Italic { get; set; }
        /// <summary>0 off, 1 superscript, 2 subscript.</summary>
        public int Superscript { get; set; }
        /// <summary>FieldWorks underline type: 0 none, 1 dotted, 2 dashed, 3 single, 4 double, 5 strikethrough, 6 squiggle.</summary>
        public int Underline { get; set; }
        /// <summary>BGR colour of the underline, or null for the text colour.</summary>
        public int? UnderColor { get; set; }
        /// <summary>Font size in points, before any superscript scaling.</summary>
        public double SizePt { get; set; }
        /// <summary>Baseline offset in points; positive raises the text.</summary>
        public double OffsetPt { get; set; }
        /// <summary>BGR colour, or null for the field's text colour (also for the "transparent" special value).</summary>
        public int? ForeColor { get; set; }
        /// <summary>BGR colour of the run's background, or null for none.</summary>
        public int? BackColor { get; set; }
        /// <summary>A font family name (default-font markup already replaced), or null for the writing system's font.</summary>
        public string? FontFamily { get; set; }
        /// <summary>OpenType feature list ("lnum=1, ss01") added to the writing system's, or null.</summary>
        public string? FontFeatures { get; set; }

        public bool Bold => Weight >= 550;

        internal CharFormat Copy() => (CharFormat)MemberwiseClone();

        /// <summary>True when the other format has every value this one has, so the two draw alike.</summary>
        public bool SameAs(CharFormat? o) => o != null && Weight == o.Weight && Italic == o.Italic && Superscript == o.Superscript && Underline == o.Underline
            && UnderColor == o.UnderColor && SizePt == o.SizePt && OffsetPt == o.OffsetPt && ForeColor == o.ForeColor && BackColor == o.BackColor
            && FontFamily == o.FontFamily && FontFeatures == o.FontFeatures;

        /// <summary>A hash that agrees with <see cref="SameAs"/>.</summary>
        public int SameHash() => ((((Weight * 31 + (Italic ? 1 : 0)) * 31 + Superscript) * 31 + Underline) * 31 + SizePt.GetHashCode()) * 31 + (ForeColor ?? -1) + 7 * (BackColor ?? -1) + 13 * (UnderColor ?? -1) + 17 * (FontFamily?.GetHashCode() ?? 0) + 19 * OffsetPt.GetHashCode() + 23 * (FontFeatures?.GetHashCode() ?? 0);

        /// <summary>Applies a bag's character properties over this format (a number font over the paragraph's, say).</summary>
        public void ApplyOver(PropertyBag bag) => Apply(bag);

        /// <summary>The font family a FieldWorks default-font markup stands for; any other name is returned as it is.</summary>
        public static string FontNameFromMarkup(string markup)
        {
            switch (markup)
            {
                case "<default serif>": return "Times New Roman";
                case "<default sans serif>": return "Arial";
                case "<default body>": return "Charis SIL";
                case "<default monospace>": return "Courier New";
                default: return markup;
            }
        }

        /// <summary>A BGR text colour as red, green and blue, or false for a special value (high byte set: transparent).</summary>
        public static bool TryColor(int bgr, out byte r, out byte g, out byte b)
        {
            r = (byte)(bgr & 0xFF);
            g = (byte)((bgr >> 8) & 0xFF);
            b = (byte)((bgr >> 16) & 0xFF);
            return ((bgr >> 24) & 0xFF) == 0;
        }

        /// <summary>Applies one bag's character properties over the current state, as FieldWorks' property store does:
        /// toggles may invert, a relative size or enumerated size is taken against the current size.</summary>
        internal void Apply(PropertyBag bag)
        {
            foreach (var kv in bag.IntProps)
            {
                int v = kv.Value.Value, var = kv.Value.Variation;
                switch (kv.Key)
                {
                    case TextPropertyNumbers.Italic:
                        if (v == TextPropertyNumbers.ToggleInvert) Italic = !Italic;
                        else if (v == TextPropertyNumbers.ToggleOn) Italic = true;
                        else if (v == TextPropertyNumbers.ToggleOff) Italic = false;
                        break;
                    case TextPropertyNumbers.Bold:
                        if (v == TextPropertyNumbers.ToggleOff) Weight = 400;
                        else if (v == TextPropertyNumbers.ToggleOn) Weight = 700;
                        else if (v == TextPropertyNumbers.ToggleInvert) Weight = Weight < 550 ? 700 : 400;
                        else if (v > 0) Weight = Math.Max(100, Math.Min(900, v));
                        break;
                    case TextPropertyNumbers.Superscript:
                        Superscript = v >= 0 && v <= 2 ? v : 0;
                        break;
                    case TextPropertyNumbers.Underline:
                        Underline = v;
                        break;
                    case TextPropertyNumbers.UnderColor:
                        UnderColor = v;
                        break;
                    case TextPropertyNumbers.FontSize:
                        ApplySize(v, var);
                        break;
                    case TextPropertyNumbers.Offset:
                        if (var == TextPropertyNumbers.VariationMilliPoint) OffsetPt = v / 1000.0;
                        else if (var == TextPropertyNumbers.VariationRelative) OffsetPt = SizePt * v / 10000.0;
                        break;
                    case TextPropertyNumbers.ForeColor:
                        ForeColor = ((v >> 24) & 0xFF) == 0 ? v : (int?)null;
                        break;
                    case TextPropertyNumbers.BackColor:
                        BackColor = ((v >> 24) & 0xFF) == 0 ? v : (int?)null;
                        break;
                }
            }
            if (bag.StrProps.TryGetValue(TextPropertyNumbers.FontFamily, out var family) && family.Length > 0)
                FontFamily = FontNameFromMarkup(family);
            if (bag.StrProps.TryGetValue(TextPropertyNumbers.FontVariations, out var features) && features.Length > 0)
                FontFeatures = string.IsNullOrEmpty(FontFeatures) ? features : FontFeatures + ", " + features;
        }

        // Named sizes, in 1/1000 point: xx-small to xx-large; then "smaller" and "larger" step through the same table.
        // Provenance: FieldWorks' named font size values are 5, 7, 9, 12, 14, 18 and 24 pt (enumeration numbers 0 to 6, then smaller 7 and larger 8), a
        // persisted compatibility table like a property number. "Smaller" is the largest named size below the current size, else two thirds of it. "Larger" is
        // meant to be the smallest named size above the current one, else half as much again, as FieldWorks' own comment says; its loop counts the wrong way
        // and reads past its table for sizes under 18 pt, so what it gives there is undefined and the field follows the comment, not the loop.
        static readonly int[] NamedSizes = { 5000, 7000, 9000, 12000, 14000, 18000, 24000 };

        void ApplySize(int v, int variation)
        {
            if (variation == TextPropertyNumbers.VariationMilliPoint || variation == TextPropertyNumbers.VariationDefault)
                SizePt = v / 1000.0;
            else if (variation == TextPropertyNumbers.VariationRelative)
                SizePt = SizePt * v / 10000.0;
            else if (variation == TextPropertyNumbers.VariationEnum)
            {
                int mpt = (int)Math.Round(SizePt * 1000);
                if (v >= 0 && v < NamedSizes.Length) SizePt = NamedSizes[v] / 1000.0;
                else if (v == NamedSizes.Length)
                {
                    // Smaller: the largest named size below the current one, else two thirds.
                    int i = 1;
                    while (i < NamedSizes.Length && NamedSizes[i] < mpt) i++;
                    SizePt = (NamedSizes[i - 1] < mpt ? NamedSizes[i - 1] : mpt * 2 / 3) / 1000.0;
                }
                else if (v == NamedSizes.Length + 1)
                {
                    // Larger: the smallest named size above the current one, else half as much again.
                    int i = 0;
                    while (i < NamedSizes.Length && NamedSizes[i] <= mpt) i++;
                    SizePt = (i < NamedSizes.Length ? NamedSizes[i] : mpt * 3 / 2) / 1000.0;
                }
            }
        }
    }

    /// <summary>The paragraph formatting after the paragraph's style and its own properties are applied. Lengths are in points.</summary>
    public sealed class ParaFormat
    {
        /// <summary>FieldWorks alignment: 0 leading, 1 left, 2 center, 3 right, 4 trailing, 5 justify.</summary>
        public int Align { get; set; }
        public double FirstIndent { get; set; }
        public double LeadingIndent { get; set; }
        public double TrailingIndent { get; set; }
        public double SpaceBefore { get; set; }
        public double SpaceAfter { get; set; }
        public double MarginTop { get; set; }
        /// <summary>The line height in points, or 0 for the font's own.</summary>
        public double LineHeight { get; set; }
        /// <summary>True when <see cref="LineHeight"/> is exact; false is "at least".</summary>
        public bool ExactLineHeight { get; set; }
        public int? ParaColor { get; set; }
        public double PadLeading { get; set; }
        public double PadTrailing { get; set; }
        public double PadTop { get; set; }
        public double PadBottom { get; set; }
        public double BorderTop { get; set; }
        public double BorderBottom { get; set; }
        public double BorderLeading { get; set; }
        public double BorderTrailing { get; set; }
        public int? BorderColor { get; set; }
        /// <summary>0 none, 10 to 15 number schemes, 100 and up bullets.</summary>
        public int BulNumScheme { get; set; }
        public int? BulNumStartAt { get; set; }
        public string BulNumTextBefore { get; set; } = string.Empty;
        public string BulNumTextAfter { get; set; } = string.Empty;
        public string BulNumFontInfo { get; set; } = string.Empty;
        /// <summary>The most lines to show; 0 for all.</summary>
        public int MaxLines { get; set; }

        internal void Apply(PropertyBag bag, double baseSizePt)
        {
            foreach (var kv in bag.IntProps)
            {
                int v = kv.Value.Value, var = kv.Value.Variation;
                double pt = v / 1000.0;
                switch (kv.Key)
                {
                    case TextPropertyNumbers.Align: Align = v; break;
                    case TextPropertyNumbers.FirstIndent: FirstIndent = pt; break;
                    case TextPropertyNumbers.LeadingIndent: LeadingIndent = pt; break;
                    case TextPropertyNumbers.TrailingIndent: TrailingIndent = pt; break;
                    case TextPropertyNumbers.SpaceBefore: SpaceBefore = pt; break;
                    case TextPropertyNumbers.SpaceAfter: SpaceAfter = pt; break;
                    case TextPropertyNumbers.MarginTop: MarginTop = pt; break;
                    case TextPropertyNumbers.LineHeight:
                        if (var == TextPropertyNumbers.VariationRelative)
                        {
                            // Provenance: a relative line height (a share of 10000) is that share of 1.4 times the font size of the paragraph's base writing
                            // system, as FieldWorks works it out: it has no font metrics there and takes the height a line needs as 40 % more than the size.
                            LineHeight = baseSizePt * 1.4 * v / 10000.0;
                            ExactLineHeight = false;
                        }
                        else
                        {
                            LineHeight = Math.Abs(pt);
                            ExactLineHeight = v < 0;
                        }
                        break;
                    case TextPropertyNumbers.ParaColor: ParaColor = ((v >> 24) & 0xFF) == 0 ? v : (int?)null; break;
                    case TextPropertyNumbers.PadLeading: PadLeading = pt; break;
                    case TextPropertyNumbers.PadTrailing: PadTrailing = pt; break;
                    case TextPropertyNumbers.PadTop: PadTop = pt; break;
                    case TextPropertyNumbers.PadBottom: PadBottom = pt; break;
                    case TextPropertyNumbers.BorderTop: BorderTop = pt; break;
                    case TextPropertyNumbers.BorderBottom: BorderBottom = pt; break;
                    case TextPropertyNumbers.BorderLeading: BorderLeading = pt; break;
                    case TextPropertyNumbers.BorderTrailing: BorderTrailing = pt; break;
                    case TextPropertyNumbers.BorderColor: BorderColor = ((v >> 24) & 0xFF) == 0 ? v : (int?)null; break;
                    case TextPropertyNumbers.BulNumScheme: BulNumScheme = v; break;
                    case TextPropertyNumbers.BulNumStartAt: BulNumStartAt = v; break;
                    case TextPropertyNumbers.MaxLines: MaxLines = Math.Max(0, v); break;
                }
            }
            if (bag.StrProps.TryGetValue(TextPropertyNumbers.BulNumTxtBefore, out var before)) BulNumTextBefore = before;
            if (bag.StrProps.TryGetValue(TextPropertyNumbers.BulNumTxtAfter, out var after)) BulNumTextAfter = after;
            if (bag.StrProps.TryGetValue(TextPropertyNumbers.BulNumFontInfo, out var info)) BulNumFontInfo = info;
        }
    }

    /// <summary>
    /// Works out the formatting of a run or paragraph from the layers FieldWorks applies, in its order: the paragraph style's
    /// properties, the paragraph's own, then for a run its character style and its own properties. Later layers win.
    /// </summary>
    public static class FormatResolver
    {
        /// <summary>Resolves the character formats of one paragraph's runs, asking the host's styles once for each (style name, writing system) and applying
        /// the paragraph's layers once for each writing system, however many runs there are. It gives what <see cref="ResolveChar"/> gives. Made for one
        /// layout of the paragraph: it never sees a change to the styles or the paragraph.</summary>
        public sealed class CharFormats
        {
            readonly Paragraph _paragraph;
            readonly IStyleResolver? _styles;
            readonly Dictionary<(string, string), PropertyBag?> _bags = new Dictionary<(string, string), PropertyBag?>();
            readonly Dictionary<(string, double), CharFormat> _layers = new Dictionary<(string, double), CharFormat>();

            public CharFormats(Paragraph paragraph, IStyleResolver? styles)
            {
                _paragraph = paragraph;
                _styles = styles;
            }

            /// <summary>How many times the host's styles were asked for a style.</summary>
            public int StyleLookups { get; private set; }

            PropertyBag? Lookup(string style, string ws)
            {
                if (_bags.TryGetValue((style, ws), out var bag)) return bag;
                StyleLookups++;
                return _bags[(style, ws)] = _styles!.Resolve(style, ws);
            }

            public CharFormat Resolve(Run run, double baseSizePt)
            {
                if (!_layers.TryGetValue((run.Ws, baseSizePt), out var layer))
                {
                    layer = new CharFormat { SizePt = baseSizePt };
                    var paraStyle = StyleName(_paragraph);
                    if (_styles != null && paraStyle.Length > 0 && Lookup(paraStyle, run.Ws) is PropertyBag bag) layer.Apply(bag);
                    layer.Apply(_paragraph.Props);
                    _layers[(run.Ws, baseSizePt)] = layer;
                }
                var f = layer.Copy();
                if (run.Props.TryGetStr(TextPropertyNumbers.NamedStyle, out var charStyle) && charStyle.Length > 0 && _styles != null && Lookup(charStyle, run.Ws) is PropertyBag cbag) f.Apply(cbag);
                f.Apply(run.Props);
                return f;
            }
        }

        public static CharFormat ResolveChar(Paragraph paragraph, Run run, IStyleResolver? styles, double baseSizePt)
        {
            var f = new CharFormat { SizePt = baseSizePt };
            var paraStyle = StyleName(paragraph);
            if (styles != null && paraStyle.Length > 0)
            {
                var bag = styles.Resolve(paraStyle, run.Ws);
                if (bag != null) f.Apply(bag);
            }
            f.Apply(paragraph.Props);
            if (run.Props.TryGetStr(TextPropertyNumbers.NamedStyle, out var charStyle) && charStyle.Length > 0 && styles != null)
            {
                var bag = styles.Resolve(charStyle, run.Ws);
                if (bag != null) f.Apply(bag);
            }
            f.Apply(run.Props);
            return f;
        }

        public static ParaFormat ResolveParagraph(Paragraph paragraph, IStyleResolver? styles, double baseSizePt)
        {
            var f = new ParaFormat();
            var style = StyleName(paragraph);
            if (styles != null && style.Length > 0)
            {
                var bag = styles.Resolve(style, paragraph.DefaultWs);
                if (bag != null) f.Apply(bag, baseSizePt);
            }
            f.Apply(paragraph.Props, baseSizePt);
            return f;
        }

        static string StyleName(Paragraph p)
        {
            if (p.Style.Length > 0) return p.Style;
            return p.Props.TryGetStr(TextPropertyNumbers.NamedStyle, out var s) ? s : string.Empty;
        }
    }

    /// <summary>
    /// Bullets and numbering as FieldWorks draws them: the scheme (10 to 15 number styles, 100 and up bullets), the number of
    /// a paragraph from the run of paragraphs before it, and the text drawn in the hanging space.
    /// </summary>
    public static class BulletNumbers
    {
        public const int Arabic = 10, RomanUpper = 11, RomanLower = 12, LetterUpper = 13, LetterLower = 14, Arabic01 = 15, BulletBase = 100;

        // Bullet scheme number -> Unicode code point. The scheme numbers are stored in documents, so this mapping is persisted
        // compatibility data (ADR 0010): a scheme must keep drawing the glyph it has always meant. Two pairs of schemes (116 and
        // 117, 121 and 122) map to the same glyph; that is deliberate, kept so documents that use either number still draw.
        static readonly Dictionary<int, int> BulletGlyphs = new Dictionary<int, int>
        {
            [100] = 0x00B7,   // middle dot
            [101] = 0x2022,   // bullet
            [102] = 0x25CF,   // black circle
            [103] = 0x274D,   // shadowed white circle
            [104] = 0x25AA,   // black small square
            [105] = 0x25A0,   // black square
            [106] = 0x25AB,   // white small square
            [107] = 0x25A1,   // white square
            [108] = 0x2751,   // lower right shadowed white square
            [109] = 0x2752,   // upper right shadowed white square
            [110] = 0x2B27,   // black medium lozenge
            [111] = 0x29EB,   // black lozenge
            [112] = 0x25C6,   // black diamond
            [113] = 0x2756,   // black diamond minus white x
            [114] = 0x2318,   // place of interest sign
            [115] = 0x261E,   // white right pointing index
            [116] = 0x271E,   // shadowed white latin cross
            [117] = 0x271E,   // the same cross again (duplicate kept for data compatibility)
            [118] = 0x2730,   // shadowed white star
            [119] = 0x27A2,   // three-d top-lighted rightwards arrowhead
            [120] = 0x27B2,   // circled heavy white rightwards arrow
            [121] = 0x2794,   // heavy wide-headed rightwards arrow
            [122] = 0x2794,   // the same arrow again (duplicate kept for data compatibility)
            [123] = 0x21E8,   // rightwards white arrow
            [124] = 0x2713,   // check mark
        };

        /// <summary>The font a bullet is drawn in (the one font known to have all the bullet glyphs).</summary>
        public const string BulletFont = "Quivira";

        /// <summary>Numbers above this are written as plain digits in the roman schemes.</summary>
        const int MaxRoman = 14999;

        /// <summary>The letter schemes count A to Z, then AA to ZZ, and so on; after this number the count starts over at A.</summary>
        const int LetterCycle = 780;

        public static bool IsBullet(int scheme) => BulletGlyphs.ContainsKey(scheme);
        public static bool IsNumber(int scheme) => scheme >= Arabic && scheme <= Arabic01;

        /// <summary>The Unicode code point a bullet scheme draws, or 0 when the scheme is not a bullet.</summary>
        public static int BulletGlyph(int scheme) => BulletGlyphs.TryGetValue(scheme, out var glyph) ? glyph : 0;

        /// <summary>The number of paragraph <paramref name="index"/> in its run. A paragraph starts a new sequence when its scheme
        /// differs from the previous paragraph's (it then has its start-at value, else 1) or when it has a start-at value the
        /// previous paragraph did not; otherwise it is one more than the previous.</summary>
        public static int Sequence(Func<int, ParaFormat> formatOf, int index)
        {
            int k = index;
            while (k > 0)
            {
                var cur = formatOf(k);
                var prev = formatOf(k - 1);
                if (cur.BulNumScheme != prev.BulNumScheme || (cur.BulNumStartAt != null && cur.BulNumStartAt != prev.BulNumStartAt)) break;
                k--;
            }
            return (formatOf(k).BulNumStartAt ?? 1) + (index - k);
        }

        /// <summary>The label of a paragraph: the number or bullet between the style's before and after text, or empty for none.</summary>
        public static string Label(ParaFormat format, int number)
        {
            int scheme = format.BulNumScheme;
            if (IsBullet(scheme)) return char.ConvertFromUtf32(BulletGlyphs[scheme]);
            if (!IsNumber(scheme)) return string.Empty;
            return format.BulNumTextBefore + Number(scheme, number) + format.BulNumTextAfter;
        }

        public static string Number(int scheme, int n)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            switch (scheme)
            {
                case Arabic01: return n >= 0 && n < 10 ? "0" + n.ToString(inv) : n.ToString(inv);
                case RomanUpper: return n > 0 && n <= MaxRoman ? Roman(n).ToUpperInvariant() : n.ToString(inv);
                case RomanLower: return n > 0 && n <= MaxRoman ? Roman(n) : n.ToString(inv);
                case LetterUpper: return Letters(n, 'A');
                case LetterLower: return Letters(n, 'a');
                default: return n.ToString(inv);
            }
        }

        // Roman numerals written greedily from the largest value down, with the subtractive pairs (cm, cd, xc, xl, ix, iv).
        static readonly int[] RomanValues = { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
        static readonly string[] RomanSymbols = { "m", "cm", "d", "cd", "c", "xc", "l", "xl", "x", "ix", "v", "iv", "i" };

        static string Roman(int n)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < RomanValues.Length; i++)
                while (n >= RomanValues[i]) { sb.Append(RomanSymbols[i]); n -= RomanValues[i]; }
            return sb.ToString();
        }

        /// <summary>The letter scheme: A to Z for 1 to 26, then each letter doubled (AA to ZZ), then tripled, and so on, starting
        /// over at A after <see cref="LetterCycle"/>.</summary>
        static string Letters(int n, char first)
        {
            if (n < 1) return n.ToString(System.Globalization.CultureInfo.InvariantCulture);
            int position = (n - 1) % LetterCycle;
            return new string((char)(first + position % 26), position / 26 + 1);
        }

        /// <summary>The character properties packed in a style's number font string: for each property its number and its value as
        /// two UTF-16 units (low then high), the font family last as its number followed by the name.</summary>
        public static PropertyBag ParseFontInfo(string info)
        {
            var bag = new PropertyBag();
            int i = 0;
            while (i < info.Length)
            {
                int tpt = info[i++];
                if (tpt == TextPropertyNumbers.FontFamily)
                {
                    bag.SetStr(tpt, info.Substring(i));
                    break;
                }
                if (i + 1 >= info.Length) break;
                int value = info[i] | (info[i + 1] << 16);
                i += 2;
                bag.SetInt(tpt, value);
            }
            return bag;
        }
    }
}
